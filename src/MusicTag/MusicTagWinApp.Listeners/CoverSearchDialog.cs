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
using MusicTagWinApp.Containers;
using MusicTagWinApp.Exporters;
using MusicTagWinApp.Instances;
using MusicTagWinApp.Properties;
using MusicTagWinApp.Stubs;
using MusicTagWinApp.Web;
using MusicTagWinApp.Win32.Taskbar;

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

		private readonly SourceOutcomeTracker outcomeTracker = new SourceOutcomeTracker();

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
			remainingTotal = TextUtilities.GetWebSearchResultLimit();
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
			foreach (SourceItem sourceItem in sources)
			{
				if (sourceItem.Enabled)
				{
					ReportSearching(sourceItem.SearchSource);
				}
			}

			if (!string.IsNullOrWhiteSpace(dialog.GetCurrentTrack().Album) || !string.IsNullOrWhiteSpace(dialog.GetCurrentTrack().Artist))
			{
				foreach (SourceItem sourceItem in sources)
				{
					if (CanSearch(sourceItem, isOtherSource: false))
					{
						SearchAndReport(sourceItem, "album/artist", () => dialog.SearchCoversBySource(sourceItem.SearchSource, (dialog.GetCurrentTrack().Album + " " + dialog.GetCurrentTrack().Artist).Trim(), accumulatedCandidates));
					}
				}
			}

			if (dialog.GetCurrentTrack().Title != dialog.GetCurrentTrack().Album)
			{
				foreach (SourceItem sourceItem in sources)
				{
					if (CanSearch(sourceItem, isOtherSource: false))
					{
						SearchAndReport(sourceItem, "title/artist", () => dialog.SearchCoversBySource(sourceItem.SearchSource, (dialog.GetCurrentTrack().Title + " " + dialog.GetCurrentTrack().Artist).Trim(), accumulatedCandidates));
					}
				}
			}

			if (!string.IsNullOrWhiteSpace(dialog.GetCurrentTrack().Album))
			{
				foreach (SourceItem sourceItem in sources)
				{
					if (CanSearch(sourceItem, isOtherSource: true))
					{
						SearchAndReport(sourceItem, "album/artist", () => dialog.SearchCoversBySource(sourceItem.SearchSource, (dialog.GetCurrentTrack().Album + " " + dialog.GetCurrentTrack().Artist).Trim(), accumulatedCandidates));
					}
				}
			}

			if (!string.IsNullOrWhiteSpace(dialog.GetCurrentTrack().Title))
			{
				foreach (SourceItem sourceItem in sources)
				{
					if (CanSearch(sourceItem, isOtherSource: true))
					{
						SearchAndReport(sourceItem, "title/artist", () => dialog.SearchCoversBySource(sourceItem.SearchSource, (dialog.GetCurrentTrack().Title + " " + dialog.GetCurrentTrack().Artist).Trim(), accumulatedCandidates));
					}
				}
			}

			if (!dialog.GetSearchCancellation().IsCancellationRequested)
			{
				outcomeTracker.ReportFinal(
					sources.Where(sourceItem => sourceItem.Enabled).Select(sourceItem => sourceItem.SearchSource),
					status => dialog.searchStatusReporter?.Invoke(status));
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
			ReportSearching(preferredSource);

			if (!string.IsNullOrWhiteSpace(dialog.GetCurrentTrack().Album) || !string.IsNullOrWhiteSpace(dialog.GetCurrentTrack().Artist))
			{
				SearchAndReport(sourceItem, "album/artist", () => dialog.SearchCoversBySource(preferredSource, (dialog.GetCurrentTrack().Album + " " + dialog.GetCurrentTrack().Artist).Trim(), accumulatedCandidates));
			}

			if (dialog.GetCurrentTrack().Title != dialog.GetCurrentTrack().Album)
			{
				SearchAndReport(sourceItem, "title/artist", () => dialog.SearchCoversBySource(preferredSource, (dialog.GetCurrentTrack().Title + " " + dialog.GetCurrentTrack().Artist).Trim(), accumulatedCandidates));
			}

			if (!dialog.GetSearchCancellation().IsCancellationRequested)
			{
				outcomeTracker.ReportFinal(new[] { preferredSource }, status => dialog.searchStatusReporter?.Invoke(status));
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

		// 状态上报:经 dialog 的 reporter(Progress<T>)编组回 UI 线程。
		private void ReportSearching(SearchSource source)
		{
			dialog.searchStatusReporter?.Invoke(new SourceSearchStatus
			{
				Source = source,
				Phase = SourceSearchPhase.Searching
			});
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
				outcomeTracker.Record(sourceItem.SearchSource, false, dialog.lastSourceTransportResult);
				return;
			}

			outcomeTracker.Record(sourceItem.SearchSource, candidates.Count > 0, dialog.lastSourceTransportResult);
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
		if (!IsDisposed && !GetSearchCancellation().IsCancellationRequested)
		{
			GetCachedCandidates().AddRange(candidates);
			AddCandidatesToList(candidates);
		}
	}

	private sealed class CoverImageLoader
	{
		private readonly CoverSearchDialog dialog;

		private readonly CoverSearchResult candidate;

		private readonly Size targetSize;

		public Size? OriginalSize { get; private set; }

		public string PlaceholderKey { get; private set; }

		public CoverImageLoader(CoverSearchDialog dialog, CoverSearchResult candidate, Size targetSize)
		{
			this.dialog = dialog;
			this.candidate = candidate;
			this.targetSize = targetSize;
		}

		public Image Load()
		{
			CoverDownloadOutcome outcome = CoverDownloadCore.LoadOrDownloadCover(candidate, dialog.GetCoverDownloadPaths(), dialog.GetSearchCancellation(), targetSize);
			if (!outcome.PathReserved)
			{
				return null;
			}
			OriginalSize = outcome.OriginalSize;
			if (outcome.Bitmap != null)
			{
				return outcome.Bitmap;
			}
			if (outcome.Status == RemoteTagProviderBase.DownloadStatus.NotFound)
			{
				PlaceholderKey = "image_not_found";
				return null;
			}
			PlaceholderKey = "download_failed";
			return null;
		}
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

	private readonly Dictionary<string, string> coverPlaceholderKeys;

	private readonly Dictionary<string, Bitmap> coverThumbnailSources;

	private readonly TaskbarProgressController taskbarProgress;

	private IContainer components;

	private FlowLayoutPanel mainLayoutPanel;

	private HeaderAwareListView candidateListView;

	private ImageList candidateImageList;

	private Panel footerPanel;

	private FlowLayoutPanel buttonPanel;

	private Button okButton;

	private Button cancelButton;

	private System.Windows.Forms.Timer cachedCandidateReplayTimer;

	private ContextMenuStrip coverContextMenu;

	private ToolStripMenuItem openCoverMenuItem;

	private ToolStripMenuItem extractCoverMenuItem;

	private SaveFileDialog coverSaveDialog;

	private Label searchStatusLabel;

	private SearchStatusIndicator searchStatusIndicator;

	private Action<SourceSearchStatus> searchStatusReporter;

	private HttpResult lastSourceTransportResult;

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
		coverPlaceholderKeys = new Dictionary<string, string>();
		coverThumbnailSources = new Dictionary<string, Bitmap>();
		InitializeComponent();
		InitializeCandidateImages();
		searchStatusIndicator = new SearchStatusIndicator(searchStatusLabel, () => candidateListView.Items.Count > 0, components);
		taskbarProgress = new TaskbarProgressController(this);
		okButton.Text = Resources.OK;
		cancelButton.Text = Resources.Cancel;
		LayoutSearchDialog();
	}

	private void InitializeCandidateImages()
	{
		ApplyDpiMetrics(DeviceDpi);
	}

	private void ApplyDpiMetrics(int targetDpi)
	{
		targetDpi = Math.Max(targetDpi, 1);
		int thumbnailExtent = Math.Min(256, ImageUtilities.ScaleLogicalPixels(128f, targetDpi));
		Size thumbnailSize = new Size(thumbnailExtent, thumbnailExtent);
		Dictionary<string, string> displayedCoverPaths = new Dictionary<string, string>();
		foreach (ListViewItem item in candidateListView.Items)
		{
			string imageKey = item.ImageKey;
			if (string.IsNullOrWhiteSpace(imageKey) || imageKey == "loading" || imageKey == "download_failed" || imageKey == "image_not_found")
			{
				continue;
			}
			displayedCoverPaths[imageKey] = coverPlaceholderKeys.TryGetValue(imageKey, out string placeholderKey)
				? placeholderKey
				: "download_failed";
		}

		candidateImageList.Images.Clear();
		candidateImageList.ImageSize = thumbnailSize;
		candidateImageList.ColorDepth = ColorDepth.Depth32Bit;
		candidateImageList.TransparentColor = Color.Transparent;
		AddCandidatePlaceholder("loading", "loading", targetDpi);
		AddCandidatePlaceholder("download_failed", "download_failed", targetDpi);
		AddCandidatePlaceholder("image_not_found", "imagenotfound", targetDpi);

		foreach (KeyValuePair<string, string> displayedCover in displayedCoverPaths)
		{
			if (!coverThumbnailSources.TryGetValue(displayedCover.Key, out Bitmap sourceImage))
			{
				sourceImage = CoverDownloadCore.LoadCachedCoverThumbnail(displayedCover.Key, new Size(256, 256));
				if (sourceImage != null)
				{
					coverThumbnailSources[displayedCover.Key] = sourceImage;
				}
			}
			if (sourceImage != null)
			{
				RenderCandidateThumbnail(displayedCover.Key, sourceImage);
				coverPlaceholderKeys.Remove(displayedCover.Key);
				continue;
			}
			AddCandidatePlaceholderCopy(displayedCover.Key, displayedCover.Value);
		}
		LayoutSearchDialog();
	}

	private void AddCandidatePlaceholder(string imageKey, string resourceName, int targetDpi)
	{
		using Bitmap image = ImageUtilities.LoadResourceBitmapForDpi(resourceName, candidateImageList.ImageSize, targetDpi);
		if (image != null)
		{
			candidateImageList.Images.Add(imageKey, image);
			_ = candidateImageList.Handle;
		}
	}

	private void AddCandidatePlaceholderCopy(string imageKey, string placeholderKey)
	{
		using Image placeholder = candidateImageList.Images[placeholderKey];
		if (placeholder != null)
		{
			candidateImageList.Images.Add(imageKey, placeholder);
			_ = candidateImageList.Handle;
		}
	}

	private void ReplaceCoverThumbnailSource(string imageKey, Bitmap sourceImage)
	{
		if (coverThumbnailSources.TryGetValue(imageKey, out Bitmap oldSource))
		{
			oldSource.Dispose();
		}
		coverThumbnailSources[imageKey] = sourceImage;
	}

	private void RenderCandidateThumbnail(string imageKey, Image sourceImage)
	{
		using Bitmap thumbnail = ImageUtilities.ResizeImageToFit(sourceImage, candidateImageList.ImageSize, centerOnCanvas: true);
		if (thumbnail != null)
		{
			ReplaceCandidateImage(imageKey, thumbnail);
		}
	}

	private void ReplaceCandidateImage(string imageKey, Image image)
	{
		int existingIndex = candidateImageList.Images.IndexOfKey(imageKey);
		if (existingIndex >= 0)
		{
			candidateImageList.Images.RemoveAt(existingIndex);
		}
		candidateImageList.Images.Add(imageKey, image);
		_ = candidateImageList.Handle;
	}

	protected override void OnHandleCreated(EventArgs e)
	{
		base.OnHandleCreated(e);
		ApplyDpiMetrics(DeviceDpi);
	}

	protected override void OnDpiChanged(DpiChangedEventArgs e)
	{
		base.OnDpiChanged(e);
		ApplyDpiMetrics(e.DeviceDpiNew);
	}

	protected override void OnShown(EventArgs i)
	{
		base.OnShown(i);
		if (lastCandidateSearchSucceeded && GetCachedCandidates() != null && cachedSearchTrack != null && GetCachedCandidates().Any() && cachedSearchSource == GetPreferredSource() && GetCurrentTrack().Title == cachedSearchTrack.Title && GetCurrentTrack().Artist == cachedSearchTrack.Artist && GetCurrentTrack().Album == cachedSearchTrack.Album)
		{
			// 缓存复用:不联网搜索,状态标识保持隐藏。
			searchStatusIndicator.Reset();
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
		cachedCandidateReplayTimer.Stop();
		searchStatusIndicator.StopCountdown();
		GetSearchCancellation().Cancel();
		base.OnClosed(e);
		if (base.DialogResult == DialogResult.OK)
		{
			preservedCoverPath = GetSelectedCandidate().LocalCoverPath;
		}
		PathFileUtilities.TrimDirectorySize(PathFileUtilities.GetPictureCacheDirectory(), preservedCoverPath, 31457280L, 62914560L);
	}

	private void LayoutSearchDialog()
	{
		candidateListView.Width = mainLayoutPanel.Width;
		candidateListView.Height = mainLayoutPanel.Height - footerPanel.Height;
		SearchStatusIndicator.LayoutFooterStatus(footerPanel, buttonPanel, searchStatusLabel);
	}

	private void OnSearchDialogLayoutChanged(object sender, EventArgs e)
	{
		LayoutSearchDialog();
	}

	private List<CoverSearchResult> SearchCoversBySource(SearchSource source, string query, List<CoverSearchResult> existingCandidates)
	{
		lastSourceTransportResult = null;
		// 经 SearchProviderFactory 按源构造封面 provider(已注入 StatusReporter);酷狗及未知源返回 null,
		// 等价原 switch 的 default(返回空列表、lastSourceTransportResult 保持 null)。
		using ICoverSearchProvider provider = SearchProviderFactory.CreateCoverSearch(source, GetSearchCancellation(), searchStatusReporter);
		if (provider == null)
		{
			return new List<CoverSearchResult>();
		}
		List<CoverSearchResult> covers = provider.SearchCovers(query, SearchProviderPolicy.ResultLimit(source), existingCandidates);
		lastSourceTransportResult = provider.LastTransportResult;
		return covers;
	}

	private async void StartCandidateSearch()
	{
		SetCachedCandidates(new List<CoverSearchResult>());
		IProgress<List<CoverSearchResult>> progress = new Progress<List<CoverSearchResult>>(OnSearchCandidatesFound);
		// 状态通道:Progress<T> 在 UI 线程构造,Report 自动编组回 UI 线程。
		searchStatusReporter = searchStatusIndicator.BeginReporting();
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
			LogService.WriteExceptionDetails(ex, "CoverSearchDialog.StartCandidateSearch");
			searchStatusIndicator.ReportUnexpectedError();
		}
		finally
		{
			if (!IsDisposed)
			{
				GetTaskbarProgress().SetProgressState(TaskbarProgressBarStatus.NoProgress);
				searchStatusIndicator.End();
			}
		}
	}

	private async void StartCoverDownload(CoverSearchResult candidate, int taskNo, int taskSubNo)
	{
		bool queuedNextDownload = false;
		CoverImageLoader coverImageLoader = new CoverImageLoader(this, candidate, new Size(256, 256));
		try
		{
			Image image = await Task.Run(coverImageLoader.Load, GetSearchCancellation().Token);
			if (IsDisposed || GetSearchCancellation().IsCancellationRequested)
			{
				image?.Dispose();
				return;
			}
			ApplyCoverDownloadResult(candidate, image, coverImageLoader.OriginalSize, coverImageLoader.PlaceholderKey);
			queuedNextDownload = StartNextCoverDownload(taskNo, taskSubNo);
		}
		catch (OperationCanceledException) when (GetSearchCancellation().IsCancellationRequested)
		{
		}
		catch (System.Exception ex)
		{
			LogService.WriteExceptionDetails(ex, "CoverSearchDialog.StartCoverDownload");
		}
		finally
		{
			if (!queuedNextDownload)
			{
				startedCoverDownloadCount--;
			}
		}
	}

	private void ApplyCoverDownloadResult(CoverSearchResult candidate, Image image, Size? originalSize, string placeholderKey)
	{
		ListViewItem candidateItem = candidateListView.Items[candidate.ListViewIndex];
		candidateItem.ImageKey = candidate.LocalCoverPath;
		if (image != null && string.IsNullOrWhiteSpace(placeholderKey))
		{
			Bitmap sourceImage = image as Bitmap;
			if (sourceImage == null)
			{
				sourceImage = new Bitmap(image);
				image.Dispose();
			}
			ReplaceCoverThumbnailSource(candidate.LocalCoverPath, sourceImage);
			RenderCandidateThumbnail(candidate.LocalCoverPath, sourceImage);
			coverPlaceholderKeys.Remove(candidate.LocalCoverPath);
			UpdateCandidateDisplayText(candidate, candidateItem, originalSize);
			return;
		}
		if (image == null && !string.IsNullOrWhiteSpace(placeholderKey))
		{
			coverPlaceholderKeys[candidate.LocalCoverPath] = placeholderKey;
			image = candidateImageList.Images[placeholderKey];
		}
		if (image != null)
		{
			try
			{
				ReplaceCandidateImage(candidate.LocalCoverPath, image);
				UpdateCandidateDisplayText(candidate, candidateItem, originalSize);
			}
			finally
			{
				image.Dispose();
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

	private static void UpdateCandidateDisplayText(CoverSearchResult candidate, ListViewItem candidateItem, Size? originalSize)
	{
		foreach (ListViewItem listViewItem in candidateItem.ListView.Items)
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
			DialogService.ShowErrorMessage(Resources.Msg_PleaseSelectItem);
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
		CoverImageActions.LoadAndUseCoverImage(imageKey, useImage);
	}

	private void OpenSelectedCover(object sender, EventArgs e)
	{
		UseSelectedCoverImage(CoverImageActions.OpenCoverImage);
	}

	private void ExtractSelectedCover(object sender, EventArgs e)
	{
		UseSelectedCoverImage(SaveCoverImage);
	}

	protected override void Dispose(bool injectinit)
	{
		if (injectinit)
		{
			foreach (Bitmap sourceImage in coverThumbnailSources.Values)
			{
				sourceImage.Dispose();
			}
			coverThumbnailSources.Clear();
			components?.Dispose();
		}
		base.Dispose(injectinit);
	}

	private void InitializeComponent()
	{
		components = new Container();
		mainLayoutPanel = new FlowLayoutPanel();
		candidateListView = new HeaderAwareListView();
		candidateImageList = new ImageList(components);
		footerPanel = new Panel();
		buttonPanel = new FlowLayoutPanel();
		okButton = new Button();
		cancelButton = new Button();
		searchStatusLabel = new Label();
		cachedCandidateReplayTimer = new System.Windows.Forms.Timer(components);
		coverContextMenu = new ContextMenuStrip(components);
		openCoverMenuItem = new ToolStripMenuItem();
		extractCoverMenuItem = new ToolStripMenuItem();
		coverSaveDialog = new SaveFileDialog();

		mainLayoutPanel.SuspendLayout();
		footerPanel.SuspendLayout();
		buttonPanel.SuspendLayout();
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

		footerPanel.Controls.Add(searchStatusLabel);
		footerPanel.Controls.Add(buttonPanel);
		footerPanel.Dock = DockStyle.Fill;
		footerPanel.Location = new Point(0, 431);
		footerPanel.Margin = new Padding(0);
		footerPanel.Name = "flowLayoutPanel2";
		footerPanel.Size = new Size(534, 60);
		footerPanel.TabIndex = 8;

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
		CancelButton = cancelButton;
		AcceptButton = okButton;

		searchStatusLabel.AutoSize = false;
		searchStatusLabel.AutoEllipsis = true;
		searchStatusLabel.Name = "searchStatusLabel";
		searchStatusLabel.Size = new Size(200, 40);
		searchStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
		searchStatusLabel.ForeColor = SystemColors.GrayText;
		searchStatusLabel.Visible = false;

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
		ResumeLayout(false);
	}

	private void ReplayCachedCandidates(object sender, EventArgs e)
	{
		AddCandidatesToList(GetCachedCandidates());
		cachedCandidateReplayTimer.Stop();
	}

	private void SaveCoverImage(ConfigDescriptorState.PictureData pictureData)
	{
		CoverImageActions.SaveCoverImageAs(coverSaveDialog, pictureData);
	}

}
