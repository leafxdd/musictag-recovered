using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MusicTag.Consumers;
using MusicTag.Mocks;
using MusicTag.Serialization;
using MusicTag.Services;
using MusicTag.States;
using MusicTagWinApp.Adapter;
using MusicTagWinApp.Containers;
using MusicTagWinApp.Exporters;
using MusicTagWinApp.Instances;
using MusicTagWinApp.Properties;
using MusicTagWinApp.Stubs;
using MusicTagWinApp.Web;
using MusicTagWinApp.Win32.Taskbar;
using MusicTagWinApp.Writers;

namespace MusicTagWinApp.Listeners;

internal class CoverSearchDialog : Form
{
	private sealed class CandidateSearchWorker
	{
		private readonly CoverSearchDialog dialog;

		private readonly IProgress<List<CoverSearchResult>> progress;

		private List<CoverSearchResult> accumulatedCandidates;

		private Dictionary<SourceItem, int> remainingBySource;

		private int remainingTotal;

		public CandidateSearchWorker(CoverSearchDialog dialog, IProgress<List<CoverSearchResult>> progress)
		{
			this.dialog = dialog;
			this.progress = progress;
		}

		public bool Search()
		{
			if (!dialog.GetCurrentTrack().HasTitle())
			{
				return true;
			}

			accumulatedCandidates = new List<CoverSearchResult>();
			remainingTotal = DatabaseMapper.GetWebSearchResultLimit();
			remainingBySource = new Dictionary<SourceItem, int>();

			if (dialog.GetPreferredSource().HasValue)
			{
				SearchPreferredSource(dialog.GetPreferredSource().Value);
				return !dialog.GetSearchCancellation().IsCancellationRequested;
			}

			List<SourceItem> sources = CoverSearchResult.GetSortedCoverSourceSettings();
			foreach (SourceItem sourceItem in sources)
			{
				remainingBySource[sourceItem] = sourceItem.GetEffectiveSearchResultLimit();
			}

			if (!string.IsNullOrWhiteSpace(dialog.GetCurrentTrack().Album) || !string.IsNullOrWhiteSpace(dialog.GetCurrentTrack().Artist))
			{
				foreach (SourceItem sourceItem in sources)
				{
					if (CanSearch(sourceItem, isOtherSource: false))
					{
						SearchAndReport(sourceItem, "album/artist", () => dialog.SearchByAlbumAndArtist(sourceItem.SearchSource, accumulatedCandidates));
					}
				}
			}

			if (dialog.GetCurrentTrack().Title != dialog.GetCurrentTrack().Album)
			{
				foreach (SourceItem sourceItem in sources)
				{
					if (CanSearch(sourceItem, isOtherSource: false))
					{
						SearchAndReport(sourceItem, "title/artist", () => dialog.SearchByTitleAndArtist(sourceItem.SearchSource, accumulatedCandidates));
					}
				}
			}

			if (!string.IsNullOrWhiteSpace(dialog.GetCurrentTrack().Album))
			{
				foreach (SourceItem sourceItem in sources)
				{
					if (CanSearch(sourceItem, isOtherSource: true))
					{
						SearchAndReport(sourceItem, "album/artist", () => dialog.SearchByAlbumAndArtist(sourceItem.SearchSource, accumulatedCandidates));
					}
				}
			}

			if (!string.IsNullOrWhiteSpace(dialog.GetCurrentTrack().Title))
			{
				foreach (SourceItem sourceItem in sources)
				{
					if (CanSearch(sourceItem, isOtherSource: true))
					{
						SearchAndReport(sourceItem, "title/artist", () => dialog.SearchByTitleAndArtist(sourceItem.SearchSource, accumulatedCandidates));
					}
				}
			}

			if (!string.IsNullOrWhiteSpace(dialog.GetCurrentTrack().Artist))
			{
				foreach (SourceItem sourceItem in sources)
				{
					if (CanSearch(sourceItem, isOtherSource: true))
					{
						SearchAndReport(sourceItem, "artist", () => dialog.SearchByArtist(sourceItem.SearchSource, accumulatedCandidates));
					}
				}
			}

			return !dialog.GetSearchCancellation().IsCancellationRequested;
		}

		private void SearchPreferredSource(SearchSource preferredSource)
		{
			SourceItem sourceItem = CoverSearchResult.GetCoverSourceSettings().Find(item => item.SearchSource == preferredSource);
			if (sourceItem == null)
			{
				return;
			}

			remainingBySource[sourceItem] = sourceItem.GetEffectiveSearchResultLimit();

			if (!string.IsNullOrWhiteSpace(dialog.GetCurrentTrack().Album) || !string.IsNullOrWhiteSpace(dialog.GetCurrentTrack().Artist))
			{
				SearchAndReport(sourceItem, "album/artist", () => dialog.SearchByAlbumAndArtist(preferredSource, accumulatedCandidates));
			}

			if (dialog.GetCurrentTrack().Title != dialog.GetCurrentTrack().Album)
			{
				SearchAndReport(sourceItem, "title/artist", () => dialog.SearchByTitleAndArtist(preferredSource, accumulatedCandidates));
			}

			if (!string.IsNullOrWhiteSpace(dialog.GetCurrentTrack().Artist))
			{
				SearchAndReport(sourceItem, "artist", () => dialog.SearchByArtist(preferredSource, accumulatedCandidates));
			}
		}

		private bool CanSearch(SourceItem sourceItem, bool isOtherSource)
		{
			return !dialog.GetSearchCancellation().IsCancellationRequested
				&& sourceItem.Enabled
				&& sourceItem.IsSecondarySource == isOtherSource
				&& remainingTotal > 0
				&& remainingBySource[sourceItem] > 0;
		}

		private void SearchAndReport(SourceItem sourceItem, string searchKind, Func<List<CoverSearchResult>> search)
		{
			if (dialog.GetSearchCancellation().IsCancellationRequested || remainingTotal <= 0 || remainingBySource[sourceItem] <= 0)
			{
				return;
			}

			List<CoverSearchResult> candidates;
			try
			{
				candidates = search() ?? new List<CoverSearchResult>();
			}
			catch (OperationCanceledException) when (dialog.GetSearchCancellation().IsCancellationRequested)
			{
				return;
			}
			catch (System.Exception ex)
			{
				Console.WriteLine($"Tag search error ({sourceItem.SearchSource}, {searchKind}): {ex.GetMessageChain()}");
				return;
			}

			List<CoverSearchResult> selectedCandidates = candidates.Take(Math.Min(Math.Min(remainingTotal, candidates.Count), remainingBySource[sourceItem])).ToList();
			if (selectedCandidates.Count == 0)
			{
				return;
			}

			accumulatedCandidates.AddRange(selectedCandidates);
			progress.Report(selectedCandidates);
			remainingTotal -= selectedCandidates.Count;
			remainingBySource[sourceItem] -= selectedCandidates.Count;
		}
	}

	private void OnSearchCandidatesFound(List<CoverSearchResult> candidates)
	{
		if (!GetSearchCancellation().IsCancellationRequested)
		{
			GetCachedCandidates().AddRange(candidates);
			AddCandidatesToList(candidates);
		}
	}

	private sealed class CoverImageLoader
	{
		private readonly CoverSearchDialog dialog;

		private readonly CoverSearchResult candidate;

		private string coverPath;

		public Size? OriginalSize { get; private set; }

		public CoverImageLoader(CoverSearchDialog dialog, CoverSearchResult candidate)
		{
			this.dialog = dialog;
			this.candidate = candidate;
		}

		public Image Load()
		{
			coverPath = candidate.LocalCoverPath ?? (DatabaseMapper.GetPictureCacheDirectory() + DatabaseMapper.ComputeMd5HashString(candidate.CoverUrl, "UTF-8").Replace("-", ""));
			candidate.LocalCoverPath = coverPath;

			if (!TryReserveCoverPath())
			{
				return null;
			}

			RemoteTagProviderBase.DownloadStatus downloadStatus = RemoteTagProviderBase.DownloadStatus.Error;
			Bitmap bitmap = null;
			if (File.Exists(coverPath))
			{
				bitmap = DecodeCoverImage();
			}

			if (bitmap == null)
			{
				downloadStatus = DownloadCoverFile();
				if (downloadStatus == RemoteTagProviderBase.DownloadStatus.Success)
				{
					bitmap = DecodeCoverImage();
				}
				else
				{
					DeleteFailedCoverFile();
				}
			}

			if (bitmap != null)
			{
				return bitmap;
			}
			if (downloadStatus == RemoteTagProviderBase.DownloadStatus.NotFound)
			{
				return dialog.candidateImageList.Images["image_not_found"];
			}
			return dialog.candidateImageList.Images["download_failed"];
		}

		private bool TryReserveCoverPath()
		{
			HashSet<string> paths = dialog.GetCoverDownloadPaths();
			lock (paths)
			{
				if (paths.Contains(coverPath))
				{
					return false;
				}
				paths.Add(coverPath);
				return true;
			}
		}

		private RemoteTagProviderBase.DownloadStatus DownloadCoverFile()
		{
			try
			{
				var (downloadStatus, _) = candidate.CoverDownloader(dialog.GetSearchCancellation(), coverPath, 300000);
				return downloadStatus;
			}
			catch (System.Exception ex)
			{
				Console.WriteLine("downloadfile fail:" + ex.Message);
				return RemoteTagProviderBase.DownloadStatus.Error;
			}
		}

		private void DeleteFailedCoverFile()
		{
			try
			{
				if (File.Exists(coverPath))
				{
					File.Delete(coverPath);
				}
			}
			catch (System.Exception ex)
			{
				Console.WriteLine("deletefile fail:" + ex.Message);
			}
		}

		private Bitmap DecodeCoverImage()
		{
			try
			{
				using Bitmap bitmap = new Bitmap(coverPath);
				OriginalSize = bitmap.Size;
				return DatabaseMapper.ResizeImageToFit(bitmap, dialog.candidateImageList.ImageSize, centerOnCanvas: true);
			}
			catch (System.Exception ex)
			{
				Console.WriteLine("decode bitmap fail " + ex.Message);
				return null;
			}
		}
	}

	private static void OpenCoverImage(ConfigDescriptorState.PictureData pictureData)
	{
		string fileExtension = DatabaseMapper.GetImageExtensionForMimeType(pictureData.MimeType, "");
		string tempCoverPath = DatabaseMapper.GetPictureCacheDirectory() + "tempcover" + fileExtension;
		File.WriteAllBytes(tempCoverPath, pictureData.ImageBytes);
		Process.Start(tempCoverPath);
	}

	private int startedCoverDownloadCount;

	private static bool lastCandidateSearchSucceeded;

	private static TrackSearchContext cachedSearchTrack;

	private static SearchSource? cachedSearchSource;

	private TrackSearchContext currentTrack;

	private SearchSource? preferredSource;

	private readonly CancellationTokenSource searchCancellation;

	private static List<CoverSearchResult> cachedCandidates;

	private readonly HashSet<string> coverDownloadPaths;

	private readonly TaskbarProgressController taskbarProgress;

	private IContainer components;

	private FlowLayoutPanel mainLayoutPanel;

	private HeaderAwareListView candidateListView;

	private ImageList candidateImageList;

	private FlowLayoutPanel footerPanel;

	private FlowLayoutPanel buttonPanel;

	private Button okButton;

	private Button cancelButton;

	private System.Windows.Forms.Timer cachedCandidateReplayTimer;

	private ContextMenuStrip coverContextMenu;

	private ToolStripMenuItem openCoverMenuItem;

	private ToolStripMenuItem extractCoverMenuItem;

	private SaveFileDialog coverSaveDialog;

	private PictureBox progressPictureBox;

	private TrackSearchContext GetCurrentTrack()
	{
		return currentTrack;
	}

	public void SetCurrentTrack(TrackSearchContext first)
	{
		currentTrack = first;
	}

	public void SetPreferredSource(SearchSource? instance)
	{
		preferredSource = instance;
	}

	private SearchSource? GetPreferredSource()
	{
		return preferredSource;
	}

	private CancellationTokenSource GetSearchCancellation()
	{
		return searchCancellation;
	}

	public CoverSearchResult GetSelectedCandidate()
	{
		return candidateListView.SelectedItems[0].Tag as CoverSearchResult;
	}

	private static void SetCachedCandidates(List<CoverSearchResult> candidates)
	{
		cachedCandidates = candidates;
	}

	private static List<CoverSearchResult> GetCachedCandidates()
	{
		return cachedCandidates;
	}

	public static void ClearCachedCandidates()
	{
		GetCachedCandidates()?.Clear();
	}

	private HashSet<string> GetCoverDownloadPaths()
	{
		return coverDownloadPaths;
	}

	private TaskbarProgressController GetTaskbarProgress()
	{
		return taskbarProgress;
	}

	public CoverSearchDialog()
	{
		searchCancellation = new CancellationTokenSource();
		coverDownloadPaths = new HashSet<string>();
		InitializeComponent();
		InitializeCandidateImages();
		taskbarProgress = new TaskbarProgressController(this);
		okButton.Text = Resources.OK;
		cancelButton.Text = Resources.Cancel;
		LayoutSearchDialog();
	}

	private void InitializeCandidateImages()
	{
		candidateImageList.Images.Clear();
		candidateImageList.ImageSize = new Size(DatabaseMapper.ScaleByDpi(candidateImageList.ImageSize.Width), DatabaseMapper.ScaleByDpi(candidateImageList.ImageSize.Height));
		candidateImageList.ColorDepth = ColorDepth.Depth24Bit;
		candidateImageList.TransparentColor = Color.Transparent;
		candidateImageList.Images.Add("loading", DatabaseMapper.LoadResourceBitmap("loading", candidateImageList.ImageSize));
		candidateImageList.Images.Add("download_failed", DatabaseMapper.LoadResourceBitmap("download_failed", candidateImageList.ImageSize));
		candidateImageList.Images.Add("image_not_found", DatabaseMapper.LoadResourceBitmap("imagenotfound", candidateImageList.ImageSize));
		progressPictureBox.Image = DatabaseMapper.LoadResourceBitmap("img_wait");
	}

	protected override void OnShown(EventArgs i)
	{
		base.OnShown(i);
		if (lastCandidateSearchSucceeded && GetCachedCandidates() != null && cachedSearchTrack != null && GetCachedCandidates().Any() && cachedSearchSource == GetPreferredSource() && GetCurrentTrack().Title == cachedSearchTrack.Title && GetCurrentTrack().Artist == cachedSearchTrack.Artist && GetCurrentTrack().Album == cachedSearchTrack.Album)
		{
			foreach (CoverSearchResult cachedCandidate in GetCachedCandidates())
			{
				cachedCandidate.CoverDownloadQueued = false;
			}
			cachedCandidateReplayTimer.Tick += ReplayCachedCandidates;
			cachedCandidateReplayTimer.Start();
		}
		else
		{
			StartCandidateSearch();
		}
		cachedSearchTrack = GetCurrentTrack();
		cachedSearchSource = GetPreferredSource();
		Text = string.Concat(GetCurrentTrack().Title, " | ", GetCurrentTrack().Artist, " | ", GetCurrentTrack().Album);
	}

	protected override void OnClosed(EventArgs e)
	{
		string preservedCoverPath = default(string);
		base.OnClosed(e);
		cachedCandidateReplayTimer.Stop();
		GetSearchCancellation().Cancel();
		if (base.DialogResult == DialogResult.OK)
		{
			preservedCoverPath = GetSelectedCandidate().LocalCoverPath;
		}
		DatabaseMapper.TrimDirectorySize(DatabaseMapper.GetPictureCacheDirectory(), preservedCoverPath, 31457280L, 62914560L);
	}

	private void LayoutSearchDialog()
	{
		candidateListView.Width = mainLayoutPanel.Width;
		candidateListView.Height = mainLayoutPanel.Height - footerPanel.Height;
		int buttonLeftMargin = (footerPanel.Width - buttonPanel.Width) / 2;
		buttonPanel.Margin = new Padding(buttonLeftMargin, buttonPanel.Margin.Top, 0, buttonPanel.Margin.Bottom);
		int statusImageLeftMargin = footerPanel.Width - progressPictureBox.Width - buttonPanel.Location.X - buttonPanel.Width - progressPictureBox.Margin.Top;
		progressPictureBox.Margin = new Padding(statusImageLeftMargin, progressPictureBox.Margin.Top, 0, progressPictureBox.Margin.Bottom);
	}

	private void OnSearchDialogLayoutChanged(object sender, EventArgs e)
	{
		LayoutSearchDialog();
	}

	private List<CoverSearchResult> SearchByAlbumAndArtist(SearchSource source, List<CoverSearchResult> existingCandidates)
	{
		switch (source)
		{
		case SearchSource.Music163:
		{
			using NetEaseMusicTagProvider netEaseProvider = new NetEaseMusicTagProvider(GetSearchCancellation());
			return netEaseProvider.SearchCovers((GetCurrentTrack().Album + " " + GetCurrentTrack().Artist).Trim(), 15, existingCandidates);
		}
		case SearchSource.QQ:
		{
			using QqMusicTagProvider qqProvider = new QqMusicTagProvider(GetSearchCancellation());
			return qqProvider.SearchCovers((GetCurrentTrack().Album + " " + GetCurrentTrack().Artist).Trim(), 15, existingCandidates);
		}
		default:
			return new List<CoverSearchResult>();
		case SearchSource.Kuwo:
		{
			using KuwoTagProvider kuwoTagProvider = new KuwoTagProvider(GetSearchCancellation());
			return kuwoTagProvider.SearchCovers((GetCurrentTrack().Album + " " + GetCurrentTrack().Artist).Trim(), 5, existingCandidates);
		}
		}
	}

	private List<CoverSearchResult> SearchByTitleAndArtist(SearchSource source, List<CoverSearchResult> existingCandidates)
	{
		switch (source)
		{
		case SearchSource.Music163:
		{
			using NetEaseMusicTagProvider netEaseProvider = new NetEaseMusicTagProvider(GetSearchCancellation());
			return netEaseProvider.SearchCovers((GetCurrentTrack().Title + " " + GetCurrentTrack().Artist).Trim(), 15, existingCandidates);
		}
		case SearchSource.QQ:
		{
			using QqMusicTagProvider qqProvider = new QqMusicTagProvider(GetSearchCancellation());
			return qqProvider.SearchCovers((GetCurrentTrack().Title + " " + GetCurrentTrack().Artist).Trim(), 15, existingCandidates);
		}
		default:
			return new List<CoverSearchResult>();
		case SearchSource.Kuwo:
		{
			using KuwoTagProvider kuwoTagProvider = new KuwoTagProvider(GetSearchCancellation());
			return kuwoTagProvider.SearchCovers((GetCurrentTrack().Title + " " + GetCurrentTrack().Artist).Trim(), 5, existingCandidates);
		}
		}
	}

	private List<CoverSearchResult> SearchByArtist(SearchSource source, List<CoverSearchResult> existingCandidates)
	{
		return new List<CoverSearchResult>();
	}

	private async void StartCandidateSearch()
	{
		SetCachedCandidates(new List<CoverSearchResult>());
		IProgress<List<CoverSearchResult>> progress = new Progress<List<CoverSearchResult>>(OnSearchCandidatesFound);
		CandidateSearchWorker searchWorker = new CandidateSearchWorker(this, progress);

		GetTaskbarProgress().SetProgressState(TaskbarProgressBarStatus.Indeterminate);
		try
		{
			lastCandidateSearchSucceeded = await Task.Run(searchWorker.Search, GetSearchCancellation().Token);
		}
		catch (OperationCanceledException) when (GetSearchCancellation().IsCancellationRequested)
		{
			lastCandidateSearchSucceeded = false;
		}
		catch (System.Exception ex)
		{
			lastCandidateSearchSucceeded = false;
			Console.WriteLine("Tag search error: " + ex.GetMessageChain());
		}
		finally
		{
			if (!IsDisposed)
			{
				progressPictureBox.Hide();
				GetTaskbarProgress().SetProgressState(TaskbarProgressBarStatus.NoProgress);
			}
		}
	}

	private async void StartCoverDownload(CoverSearchResult candidate, int taskNo, int taskSubNo)
	{
		bool queuedNextDownload = false;
		CoverImageLoader coverImageLoader = new CoverImageLoader(this, candidate);
		try
		{
			Image image = await Task.Run(coverImageLoader.Load, GetSearchCancellation().Token);
			ApplyCoverDownloadResult(candidate, image, coverImageLoader.OriginalSize);
			queuedNextDownload = StartNextCoverDownload(taskNo, taskSubNo);
		}
		catch (System.Exception ex)
		{
			Console.WriteLine("DownloadPicture error:" + ex.GetMessageChain());
		}
		finally
		{
			if (!queuedNextDownload)
			{
				startedCoverDownloadCount--;
			}
		}
	}

	private void ApplyCoverDownloadResult(CoverSearchResult candidate, Image image, Size? originalSize)
	{
		ListViewItem candidateItem = candidateListView.Items[candidate.ListViewIndex];
		candidateItem.ImageKey = candidate.LocalCoverPath;
		if (image != null)
		{
			candidateImageList.Images.Add(candidate.LocalCoverPath, image);
			foreach (ListViewItem listViewItem in candidateListView.Items)
			{
				if (listViewItem.ImageKey != candidateItem.ImageKey)
				{
					continue;
				}
				listViewItem.Text = candidate.SearchSource.GetDisplayName();
				if (originalSize.HasValue)
				{
					listViewItem.Text = listViewItem.Text + "|" + originalSize.Value.Width + "x" + originalSize.Value.Height;
				}
			}
			return;
		}

		foreach (ListViewItem listViewItem in candidateListView.Items)
		{
			if (listViewItem.ImageKey == candidateItem.ImageKey && listViewItem.Text.IndexOf('|') > 0)
			{
				candidateItem.Text = listViewItem.Text;
				break;
			}
		}
	}

	private bool StartNextCoverDownload(int taskNo, int taskSubNo)
	{
		foreach (CoverSearchResult candidate in GetCachedCandidates())
		{
			if (GetSearchCancellation().IsCancellationRequested)
			{
				return false;
			}
			if (candidate.CoverDownloadQueued)
			{
				continue;
			}
			candidate.CoverDownloadQueued = true;
			StartCoverDownload(candidate, taskNo, taskSubNo + 1);
			return true;
		}
		return false;
	}

	private void AddCandidatesToList(List<CoverSearchResult> candidates)
	{
		if (candidates == null || base.IsDisposed)
		{
			return;
		}
		foreach (CoverSearchResult candidate in candidates)
		{
			if (!GetSearchCancellation().IsCancellationRequested)
			{
				ListViewItem listViewItem = new ListViewItem(candidate.SearchSource.GetDisplayName());
				listViewItem.ImageKey = "loading";
				listViewItem.Tag = candidate;
				candidateListView.Items.Add(listViewItem);
				candidate.ListViewIndex = listViewItem.Index;
				if (startedCoverDownloadCount < 15)
				{
					candidate.CoverDownloadQueued = true;
					StartCoverDownload(candidate, startedCoverDownloadCount++, 0);
				}
				continue;
			}
			break;
		}
	}

	private void ConfirmSelection(object sender, EventArgs e)
	{
		if (candidateListView.SelectedItems.Count <= 0)
		{
			DatabaseMapper.ShowErrorMessage(Resources.Msg_PleaseSelectItem);
			return;
		}

		base.DialogResult = DialogResult.OK;
		Close();
	}

	private void CancelSelection(object sender, EventArgs e)
	{
		base.DialogResult = DialogResult.Cancel;
		Close();
	}

	private void ConfirmSelectionOnDoubleClick(object sender, EventArgs e)
	{
		if (candidateListView.FocusedItem == null || candidateListView.SelectedItems.Count <= 0)
		{
			return;
		}

		okButton.PerformClick();
	}

	private void ShowCoverContextMenu(object sender, MouseEventArgs e)
	{
		if (e.Button == MouseButtons.Right && candidateListView.SelectedItems.Count > 0)
		{
			openCoverMenuItem.Text = Resources.OpenCover;
			extractCoverMenuItem.Text = Resources.ExtractCover;
			ListViewItem listViewItem = candidateListView.SelectedItems[0];
			openCoverMenuItem.Enabled = !string.IsNullOrWhiteSpace(listViewItem.ImageKey) && listViewItem.ImageKey != "loading" && listViewItem.ImageKey != "download_failed";
			extractCoverMenuItem.Enabled = openCoverMenuItem.Enabled;
			coverContextMenu.Show(candidateListView, e.Location);
		}
	}

	private void UseSelectedCoverImage(Action<ConfigDescriptorState.PictureData> useImage)
	{
		if (candidateListView.SelectedItems.Count <= 0)
		{
			return;
		}
		string imageKey = candidateListView.SelectedItems[0].ImageKey;
		if (string.IsNullOrWhiteSpace(imageKey) || imageKey == "loading" || imageKey == "download_failed" || !File.Exists(imageKey))
		{
			return;
		}
		ConfigDescriptorState.PictureData pictureData = new ConfigDescriptorState.PictureData
		{
			ImageBytes = File.ReadAllBytes(imageKey)
		};
		using (ConfigDescriptorState.LoadPictureImage(pictureData))
		{
			if (pictureData.MimeType != null && pictureData.Width > 0 && pictureData.Height > 0)
			{
				useImage(pictureData);
			}
		}
	}

	private void OpenSelectedCover(object sender, EventArgs e)
	{
		UseSelectedCoverImage(OpenCoverImage);
	}

	private void ExtractSelectedCover(object sender, EventArgs e)
	{
		UseSelectedCoverImage(SaveCoverImage);
	}

	protected override void Dispose(bool injectinit)
	{
		if (injectinit && components != null)
		{
			components.Dispose();
		}
		base.Dispose(injectinit);
	}

	private void InitializeComponent()
	{
		components = new Container();
		mainLayoutPanel = new FlowLayoutPanel();
		candidateListView = new HeaderAwareListView();
		candidateImageList = new ImageList(components);
		footerPanel = new FlowLayoutPanel();
		buttonPanel = new FlowLayoutPanel();
		okButton = new Button();
		cancelButton = new Button();
		progressPictureBox = new PictureBox();
		cachedCandidateReplayTimer = new System.Windows.Forms.Timer(components);
		coverContextMenu = new ContextMenuStrip(components);
		openCoverMenuItem = new ToolStripMenuItem();
		extractCoverMenuItem = new ToolStripMenuItem();
		coverSaveDialog = new SaveFileDialog();

		mainLayoutPanel.SuspendLayout();
		footerPanel.SuspendLayout();
		buttonPanel.SuspendLayout();
		((ISupportInitialize)progressPictureBox).BeginInit();
		coverContextMenu.SuspendLayout();
		SuspendLayout();

		mainLayoutPanel.Controls.Add(candidateListView);
		mainLayoutPanel.Controls.Add(footerPanel);
		mainLayoutPanel.Dock = DockStyle.Fill;
		mainLayoutPanel.FlowDirection = FlowDirection.TopDown;
		mainLayoutPanel.Location = new Point(0, 0);
		mainLayoutPanel.Margin = new Padding(0);
		mainLayoutPanel.Name = "flowLayoutPanel1";
		mainLayoutPanel.Size = new Size(534, 661);
		mainLayoutPanel.TabIndex = 0;
		mainLayoutPanel.WrapContents = false;
		mainLayoutPanel.SizeChanged += OnSearchDialogLayoutChanged;

		candidateListView.Font = new Font("Tahoma", 9f, FontStyle.Regular, GraphicsUnit.Point, 0);
		candidateListView.FullRowSelect = true;
		candidateListView.HideSelection = false;
		candidateListView.LargeImageList = candidateImageList;
		candidateListView.Location = new Point(0, 0);
		candidateListView.Margin = new Padding(0);
		candidateListView.MultiSelect = false;
		candidateListView.Name = "listView1";
		candidateListView.Size = new Size(534, 431);
		candidateListView.TabIndex = 0;
		candidateListView.UseCompatibleStateImageBehavior = false;
		candidateListView.DoubleClick += ConfirmSelectionOnDoubleClick;
		candidateListView.MouseUp += ShowCoverContextMenu;

		candidateImageList.ColorDepth = ColorDepth.Depth32Bit;
		candidateImageList.ImageSize = new Size(128, 128);
		candidateImageList.TransparentColor = Color.Transparent;

		footerPanel.Controls.Add(buttonPanel);
		footerPanel.Controls.Add(progressPictureBox);
		footerPanel.Dock = DockStyle.Fill;
		footerPanel.Location = new Point(0, 431);
		footerPanel.Margin = new Padding(0);
		footerPanel.Name = "flowLayoutPanel2";
		footerPanel.Size = new Size(534, 60);
		footerPanel.TabIndex = 8;
		footerPanel.WrapContents = false;

		buttonPanel.Controls.Add(okButton);
		buttonPanel.Controls.Add(cancelButton);
		buttonPanel.Location = new Point(0, 12);
		buttonPanel.Margin = new Padding(0, 12, 0, 0);
		buttonPanel.Name = "flowLayoutPanel3";
		buttonPanel.Size = new Size(220, 35);
		buttonPanel.TabIndex = 6;

		okButton.Location = new Point(0, 0);
		okButton.Margin = new Padding(0);
		okButton.Name = "btnOK";
		okButton.Size = new Size(100, 35);
		okButton.TabIndex = 1;
		okButton.Text = "OK";
		okButton.UseVisualStyleBackColor = true;
		okButton.Click += ConfirmSelection;

		cancelButton.Location = new Point(120, 0);
		cancelButton.Margin = new Padding(20, 0, 0, 0);
		cancelButton.Name = "btnCancel";
		cancelButton.Size = new Size(100, 35);
		cancelButton.TabIndex = 2;
		cancelButton.Text = "Cancel";
		cancelButton.UseVisualStyleBackColor = true;
		cancelButton.Click += CancelSelection;

		progressPictureBox.Image = Resources.img_wait;
		progressPictureBox.Location = new Point(220, 13);
		progressPictureBox.Margin = new Padding(0, 13, 0, 0);
		progressPictureBox.Name = "pbProgress";
		progressPictureBox.Size = new Size(32, 32);
		progressPictureBox.SizeMode = PictureBoxSizeMode.Zoom;
		progressPictureBox.TabIndex = 7;
		progressPictureBox.TabStop = false;

		cachedCandidateReplayTimer.Interval = 50;

		coverContextMenu.Items.AddRange(new ToolStripItem[2] { openCoverMenuItem, extractCoverMenuItem });
		coverContextMenu.Name = "pictureBoxContextMenuStrip";
		coverContextMenu.Size = new Size(152, 48);

		openCoverMenuItem.Name = "openCoverToolStripMenuItem";
		openCoverMenuItem.Size = new Size(151, 22);
		openCoverMenuItem.Text = "Open Cover";
		openCoverMenuItem.Click += OpenSelectedCover;

		extractCoverMenuItem.Name = "extractCoverToolStripMenuItem";
		extractCoverMenuItem.Size = new Size(151, 22);
		extractCoverMenuItem.Text = "Extract cover";
		extractCoverMenuItem.Click += ExtractSelectedCover;

		coverSaveDialog.RestoreDirectory = true;

		AutoScaleDimensions = new SizeF(96f, 96f);
		AutoScaleMode = AutoScaleMode.Dpi;
		ClientSize = new Size(534, 661);
		Controls.Add(mainLayoutPanel);
		Font = new Font("Tahoma", 9f, FontStyle.Regular, GraphicsUnit.Point, 0);
		MinimumSize = new Size(300, 300);
		Name = "FormPictureSearch";
		ShowIcon = false;
		StartPosition = FormStartPosition.CenterParent;
		Text = "Search picture from network";

		coverContextMenu.ResumeLayout(false);
		mainLayoutPanel.ResumeLayout(false);
		footerPanel.ResumeLayout(false);
		buttonPanel.ResumeLayout(false);
		((ISupportInitialize)progressPictureBox).EndInit();
		ResumeLayout(false);
	}

	private void ReplayCachedCandidates(object sender, EventArgs e)
	{
		AddCandidatesToList(GetCachedCandidates());
		progressPictureBox.Hide();
		cachedCandidateReplayTimer.Stop();
	}

	private void SaveCoverImage(ConfigDescriptorState.PictureData pictureData)
	{
		string imageFilter = DatabaseMapper.GetImageFileDialogFilterForMimeType(pictureData.MimeType);
		if (!string.IsNullOrWhiteSpace(imageFilter))
		{
			coverSaveDialog.Filter = imageFilter;
		}
		if (coverSaveDialog.ShowDialog() != DialogResult.OK)
		{
			return;
		}
		File.WriteAllBytes(coverSaveDialog.FileName, pictureData.ImageBytes);
	}

}
