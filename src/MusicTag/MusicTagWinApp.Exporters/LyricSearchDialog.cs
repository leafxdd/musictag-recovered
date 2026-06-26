using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MusicTag.Candidates;
using MusicTag.Consumers;
using MusicTag.Services;
using MusicTagWinApp.Adapter;
using MusicTagWinApp.Containers;
using MusicTagWinApp.Instances;
using MusicTagWinApp.Properties;
using MusicTagWinApp.Roles;
using MusicTagWinApp.Web;
using MusicTagWinApp.Win32.Taskbar;
using MusicTagWinApp.Writers;

namespace MusicTagWinApp.Exporters;

internal class LyricSearchDialog : Form
{
	private sealed class LyricSearchSession
	{
		private readonly LyricSearchDialog dialog;

		private readonly SearchSource? selectedSource;

		private readonly Dictionary<SourceItem, int> sourceResultLimits = new Dictionary<SourceItem, int>();

		private int remainingResultLimit;

		private int sourceOrderIndex;

		public LyricSearchSession(LyricSearchDialog dialog)
		{
			this.dialog = dialog;
			selectedSource = dialog.selectedSource;
			remainingResultLimit = DatabaseMapper.GetWebSearchResultLimit();
		}

		public void ResetSourceOrder()
		{
			sourceOrderIndex = 0;
		}

		public List<LyricSearchResult> SearchLyricsByKnownMusicId()
		{
			if (!CanSearch())
			{
				return null;
			}
			List<LyricSearchResult> lyrics = new List<LyricSearchResult>();
			if (selectedSource.HasValue)
			{
				SourceItem selectedSourceItem = LyricSearchResult.GetLyricSourceSettings().Find(sourceItem => sourceItem.SearchSource == selectedSource);
				sourceResultLimits[selectedSourceItem] = selectedSourceItem.GetEffectiveSearchResultLimit();
				if (selectedSource == SearchSource.Music163 && dialog.trackInfo.LinkedMusicMetadata.musicId > 0L)
				{
					AddResults(selectedSourceItem, dialog.SearchLyricsFromSource(selectedSource.Value, useKnownMusicId: true, lyrics, 0, searchCandidateTracks: false), lyrics);
				}
			}
			else
			{
				List<SourceItem> sources = LyricSearchResult.GetSortedLyricSourceSettings();
				foreach (SourceItem sourceItem in sources)
				{
					sourceResultLimits[sourceItem] = sourceItem.GetEffectiveSearchResultLimit();
				}
				SourceItem music163Source = sources.Find(CanSearchKnownMusic163Id);
				if (dialog.trackInfo.LinkedMusicMetadata.musicId > 0L && music163Source != null)
				{
					AddResults(music163Source, dialog.SearchLyricsFromSource(music163Source.SearchSource, useKnownMusicId: true, lyrics, 0, searchCandidateTracks: false), lyrics);
				}
			}
			return lyrics;
		}

		public List<LyricSearchResult> SearchLyricsByCandidateTracks()
		{
			if (!CanSearch())
			{
				return null;
			}
			if (remainingResultLimit < int.MaxValue)
			{
				return SearchLyricsFromTrackCandidates();
			}
			return SearchLyricsDirectly();
		}

		private bool CanSearch()
		{
			return dialog.trackInfo.HasTitle() && !dialog.cancellationSource.IsCancellationRequested;
		}

		private bool CanSearchKnownMusic163Id(SourceItem sourceItem)
		{
			return sourceItem.Enabled && remainingResultLimit > 0 && sourceResultLimits[sourceItem] > 0 && sourceItem.SearchSource == SearchSource.Music163;
		}

		private void AddResults(SourceItem sourceItem, List<LyricSearchResult> candidates, List<LyricSearchResult> target)
		{
			List<LyricSearchResult> accepted = candidates.Take(Math.Min(Math.Min(remainingResultLimit, candidates.Count), sourceResultLimits[sourceItem])).ToList();
			target.AddRange(accepted);
			remainingResultLimit -= accepted.Count;
			sourceResultLimits[sourceItem] -= accepted.Count;
		}

		private List<LyricSearchResult> SearchLyricsFromTrackCandidates()
		{
			List<TrackSearchResult> tracks = new List<TrackSearchResult>();
			if (selectedSource.HasValue)
			{
				tracks.AddRange(dialog.SearchTrackCandidates(selectedSource.Value, 0, fromCandidateSearch: true));
			}
			else
			{
				foreach (SourceItem sourceItem in LyricSearchResult.GetSortedLyricSourceSettings())
				{
					if (!dialog.cancellationSource.IsCancellationRequested && sourceItem.Enabled)
					{
						tracks.AddRange(dialog.SearchTrackCandidates(sourceItem.SearchSource, sourceOrderIndex++, fromCandidateSearch: false));
					}
				}
			}
			if (dialog.cancellationSource.IsCancellationRequested)
			{
				return null;
			}
			if (knownIdLyrics != null && knownIdLyrics.Count > 0)
			{
				tracks.RemoveAll(track => track.SourceTrackId == knownIdLyrics[0].TrackId);
			}
			foreach (TrackSearchResult track in tracks)
			{
				track.UpdateSimilarityScores(dialog.trackInfo.Title, dialog.trackInfo.Artist, dialog.trackInfo.Album);
			}
			TrackSearchResult.SortBySimilarity(tracks);
			List<LyricSearchResult> lyrics = new List<LyricSearchResult>();
			foreach (TrackSearchResult track in tracks)
			{
				SourceItem sourceItem = LyricSearchResult.GetLyricSourceSettings().Find(candidateSource => candidateSource.SearchSource == track.SearchSource);
				LyricSearchResult lyric;
				if (!dialog.cancellationSource.IsCancellationRequested && remainingResultLimit > 0 && sourceResultLimits[sourceItem] > 0 && (lyric = dialog.DownloadLyricForTrack(track)) != null)
				{
					AddResults(sourceItem, new List<LyricSearchResult> { lyric }, lyrics);
				}
			}
			return lyrics;
		}

		private List<LyricSearchResult> SearchLyricsDirectly()
		{
			List<LyricSearchResult> lyrics = new List<LyricSearchResult>();
			if (selectedSource.HasValue)
			{
				SourceItem sourceItem = LyricSearchResult.GetLyricSourceSettings().Find(candidateSource => candidateSource.SearchSource == selectedSource);
				AddResults(sourceItem, dialog.SearchLyricsFromSource(selectedSource.Value, useKnownMusicId: false, knownIdLyrics, sourceOrderIndex++, searchCandidateTracks: true), lyrics);
			}
			else
			{
				foreach (SourceItem sourceItem in LyricSearchResult.GetSortedLyricSourceSettings())
				{
					if (!dialog.cancellationSource.IsCancellationRequested && sourceItem.Enabled && remainingResultLimit > 0 && sourceResultLimits[sourceItem] > 0)
					{
						AddResults(sourceItem, dialog.SearchLyricsFromSource(sourceItem.SearchSource, useKnownMusicId: false, knownIdLyrics, sourceOrderIndex++, searchCandidateTracks: false), lyrics);
					}
				}
			}
			if (dialog.cancellationSource.IsCancellationRequested)
			{
				return null;
			}
			foreach (LyricSearchResult lyric in lyrics)
			{
				lyric.UpdateSimilarityScores(dialog.trackInfo.Title, dialog.trackInfo.Artist, dialog.trackInfo.Album);
			}
			LyricSearchResult.SortLyricResults(lyrics);
			return lyrics;
		}
	}

	private readonly CancellationTokenSource cancellationSource;

	private static TrackSearchContext cachedLyricSearchContext;

	private static SearchSource? cachedLyricSearchSource;

	private TrackSearchContext trackInfo;

	private SearchSource? selectedSource;

	private static List<LyricSearchResult> candidateLyrics;

	private static List<LyricSearchResult> knownIdLyrics;

	private readonly TaskbarProgressController taskbarProgress;

	private IContainer components;

	private FlowLayoutPanel mainPanel;

	private HeaderAwareListView lyricListView;

	private ColumnHeader titleColumn;

	private FlowLayoutPanel buttonPanel;

	private Button okButton;

	private Button cancelButton;

	private FlowLayoutPanel footerPanel;

	private ColumnHeader artistColumn;

	private ColumnHeader albumColumn;

	private ImageList lyricIconImages;

	private ColumnHeader sourceColumn;

	private ColumnHeader iconColumn;

	private PictureBox progressImage;

	public void SetTrackInfo(TrackSearchContext trackSearchContext)
	{
		trackInfo = trackSearchContext;
	}

	public void SetSelectedSource(SearchSource? searchSource)
	{
		selectedSource = searchSource;
	}

	public LyricSearchResult GetSelectedLyric()
	{
		return lyricListView.SelectedItems[0].Tag as LyricSearchResult;
	}

	public static void ClearCachedLyrics()
	{
		candidateLyrics?.Clear();
		knownIdLyrics?.Clear();
	}

	public LyricSearchDialog()
	{
		cancellationSource = new CancellationTokenSource();
		taskbarProgress = new TaskbarProgressController(this);
		InitializeComponent();
		InitializeImagesAndColumns();
		LayoutFooterControls();
		ApplyLocalizedText();
	}

	private void InitializeImagesAndColumns()
	{
		lyricIconImages.Images.Clear();
		lyricIconImages.ImageSize = new Size(DatabaseMapper.ScaleByDpi(lyricIconImages.ImageSize.Width), DatabaseMapper.ScaleByDpi(lyricIconImages.ImageSize.Height));
		lyricIconImages.ColorDepth = ColorDepth.Depth24Bit;
		lyricIconImages.TransparentColor = Color.Transparent;
		lyricIconImages.Images.Add("fileext_lrc.png", DatabaseMapper.LoadResourceBitmap("fileext_lrc", lyricIconImages.ImageSize));
		lyricIconImages.Images.Add("fileext_txt.png", DatabaseMapper.LoadResourceBitmap("fileext_txt", lyricIconImages.ImageSize));
		foreach (ColumnHeader item in lyricListView.Columns)
		{
			item.Width = DatabaseMapper.ScaleByDpi(item.Width);
		}
		progressImage.Image = DatabaseMapper.LoadResourceBitmap("img_wait");
	}

	private void ApplyLocalizedText()
	{
		okButton.Text = Resources.OK;
		cancelButton.Text = Resources.Cancel;
		sourceColumn.Text = Resources.source;
		titleColumn.Text = Resources.title;
		artistColumn.Text = Resources.artist;
		albumColumn.Text = Resources.album;
	}

	private void LayoutFooterControls()
	{
		lyricListView.Width = mainPanel.Width;
		lyricListView.Height = mainPanel.Height - footerPanel.Height;
		int left = (footerPanel.Width - buttonPanel.Width) / 2;
		buttonPanel.Margin = new Padding(left, buttonPanel.Margin.Top, 0, buttonPanel.Margin.Bottom);
		left = footerPanel.Width - progressImage.Width - buttonPanel.Location.X - buttonPanel.Width - progressImage.Margin.Top;
		progressImage.Margin = new Padding(left, progressImage.Margin.Top, 0, progressImage.Margin.Bottom);
	}

	protected override void OnShown(EventArgs e)
	{
		base.OnShown(e);
		if (candidateLyrics != null && cachedLyricSearchContext != null && candidateLyrics.Any() && cachedLyricSearchSource == selectedSource && trackInfo.Title == cachedLyricSearchContext.Title && trackInfo.Artist == cachedLyricSearchContext.Artist && trackInfo.Album == cachedLyricSearchContext.Album)
		{
			progressImage.Hide();
			AddLyricsToList();
		}
		else
		{
			StartLyricSearch();
		}
		cachedLyricSearchContext = trackInfo;
		cachedLyricSearchSource = selectedSource;
		Text = trackInfo.Title + " | " + trackInfo.Artist + " | " + trackInfo.Album;
	}

	protected override void OnClosed(EventArgs e)
	{
		base.OnClosed(e);
		cancellationSource.Cancel();
	}

	private void HandleMainPanelSizeChanged(object sender, EventArgs e)
	{
		LayoutFooterControls();
	}

	private void AcceptSelection(object sender, EventArgs e)
	{
		if (lyricListView.SelectedItems.Count > 0)
		{
			base.DialogResult = DialogResult.OK;
			Close();
		}
		else
		{
			DatabaseMapper.ShowErrorMessage(Resources.Msg_PleaseSelectItem);
		}
	}

	private void CancelSelection(object sender, EventArgs e)
	{
		base.DialogResult = DialogResult.Cancel;
		Close();
	}

	private void AcceptSelectionOnDoubleClick(object sender, EventArgs e)
	{
		if (lyricListView.FocusedItem != null && lyricListView.SelectedItems.Count > 0)
		{
			okButton.PerformClick();
		}
	}

	private List<LyricSearchResult> SearchLyricsFromSource(SearchSource searchSource, bool useKnownMusicId, List<LyricSearchResult> existingLyrics, int sourceOrder, bool searchCandidateTracks)
	{
		return SearchLyricsBySource(searchSource, useKnownMusicId, trackInfo, int.MaxValue, existingLyrics, sourceOrder, cancellationSource, searchCandidateTracks);
	}

	public static List<LyricSearchResult> SearchLyricsBySource(SearchSource searchSource, bool useKnownMusicId, TrackSearchContext trackInfo, int maxResults, List<LyricSearchResult> existingLyrics, int sourceOrder, CancellationTokenSource cancellation, bool searchCandidateTracks)
	{
		switch (searchSource)
		{
			case SearchSource.Music163:
			{
				using NetEaseMusicTagProvider netEaseProvider = new NetEaseMusicTagProvider(cancellation);
				return netEaseProvider.SearchLyrics((trackInfo.Title + " " + trackInfo.Artist).Trim(), Math.Min(15, maxResults), useKnownMusicId ? trackInfo.LinkedMusicMetadata.musicId : 0L, existingLyrics, sourceOrder);
			}
			case SearchSource.QQ:
			{
				using QqMusicTagProvider qqProvider = new QqMusicTagProvider(cancellation);
				return qqProvider.SearchLyrics((trackInfo.Title + " " + trackInfo.Artist).Trim(), Math.Min(15, maxResults), sourceOrder);
			}
			case SearchSource.Kugou:
			{
				using KugouTagProvider kugouTagProvider = new KugouTagProvider(cancellation);
				return kugouTagProvider.SearchLyrics((trackInfo.Title + " " + trackInfo.Artist).Trim(), Math.Min(5, maxResults), sourceOrder);
			}
			case SearchSource.Kuwo:
			{
				using KuwoTagProvider kuwoTagProvider = new KuwoTagProvider(cancellation);
				return kuwoTagProvider.SearchLyrics((trackInfo.Title + " " + trackInfo.Artist).Trim(), Math.Min(5, maxResults), sourceOrder);
			}
			default:
				return new List<LyricSearchResult>();
		}
	}

	private List<TrackSearchResult> SearchTrackCandidates(SearchSource searchSource, int sourceOrder, bool fromCandidateSearch)
	{
		return SearchTracksBySource(searchSource, trackInfo, sourceOrder, cancellationSource, fromCandidateSearch);
	}

	public static List<TrackSearchResult> SearchTracksBySource(SearchSource searchSource, TrackSearchContext trackInfo, int sourceOrder, CancellationTokenSource cancellation, bool fromCandidateSearch)
	{
		switch (searchSource)
		{
			case SearchSource.Music163:
			{
				using NetEaseMusicTagProvider netEaseProvider = new NetEaseMusicTagProvider(cancellation);
				return netEaseProvider.SearchTracks((trackInfo.Title + " " + trackInfo.Artist).Trim(), 15, 0L, 0, sourceOrder, new List<TrackSearchResult>(), new List<TrackSearchResult>());
			}
			case SearchSource.QQ:
			{
				using QqMusicTagProvider qqProvider = new QqMusicTagProvider(cancellation);
				return qqProvider.SearchTracks((trackInfo.Title + " " + trackInfo.Artist).Trim(), 15, 0, sourceOrder, new List<TrackSearchResult>(), new List<TrackSearchResult>());
			}
			case SearchSource.Kugou:
			{
				using KugouTagProvider kugouTagProvider = new KugouTagProvider(cancellation);
				return kugouTagProvider.SearchTracks((trackInfo.Title + " " + trackInfo.Artist).Trim(), 5, 0, sourceOrder, new List<TrackSearchResult>(), new List<TrackSearchResult>());
			}
			case SearchSource.Kuwo:
			{
				using KuwoTagProvider kuwoTagProvider = new KuwoTagProvider(cancellation);
				return kuwoTagProvider.SearchTracks((trackInfo.Title + " " + trackInfo.Artist).Trim(), 5, 0, sourceOrder, new List<TrackSearchResult>(), new List<TrackSearchResult>());
			}
			default:
				return new List<TrackSearchResult>();
		}
	}

	private LyricSearchResult DownloadLyricForTrack(TrackSearchResult track)
	{
		return DownloadLyricBySource(track, trackInfo, cancellationSource);
	}

	public static LyricSearchResult DownloadLyricBySource(TrackSearchResult track, TrackSearchContext trackInfo, CancellationTokenSource cancellation)
	{
		switch (track.SearchSource)
		{
		case SearchSource.Music163:
		{
			using NetEaseMusicTagProvider netEaseProvider = new NetEaseMusicTagProvider(cancellation);
			return netEaseProvider.LoadLyricsForTrack(track);
		}
		case SearchSource.QQ:
		{
			using QqMusicTagProvider qqProvider = new QqMusicTagProvider(cancellation);
			return qqProvider.LoadLyricsForTrack(track);
		}
		case SearchSource.Kugou:
		{
			using KugouTagProvider kugouTagProvider = new KugouTagProvider(cancellation);
			return kugouTagProvider.LoadLyricsForTrack(track);
		}
		default:
			return null;
		case SearchSource.Kuwo:
		{
			using KuwoTagProvider kuwoTagProvider = new KuwoTagProvider(cancellation);
			return kuwoTagProvider.LoadLyricForTrack(track);
		}
		}
	}

	private async void StartLyricSearch()
	{
		LyricSearchSession searchSession = new LyricSearchSession(this);
		CancellationToken cancellationToken = cancellationSource.Token;
		taskbarProgress.SetProgressState(TaskbarProgressBarStatus.Indeterminate);
		try
		{
			candidateLyrics = null;
			knownIdLyrics = await Task.Run(searchSession.SearchLyricsByKnownMusicId, cancellationToken);
			if (knownIdLyrics != null)
			{
				AddLyricsToList(knownIdLyrics);
			}
			searchSession.ResetSourceOrder();
			candidateLyrics = await Task.Run(searchSession.SearchLyricsByCandidateTracks, cancellationToken);
			if (candidateLyrics != null)
			{
				AddLyricsToList(candidateLyrics);
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception ex)
		{
			Console.WriteLine("StartLyricSearch error:" + ex.GetMessageChain());
		}
		finally
		{
			if (!IsDisposed)
			{
				progressImage.Hide();
				taskbarProgress.SetProgressState(TaskbarProgressBarStatus.NoProgress);
			}
		}
	}

	private void AddLyricsToList(List<LyricSearchResult> lyricsToAdd = null)
	{
		List<LyricSearchResult> lyricsToDisplay = new List<LyricSearchResult>();
		if (lyricsToAdd != null)
		{
			lyricsToDisplay.AddRange(lyricsToAdd);
		}
		else
		{
			if (knownIdLyrics != null)
			{
				lyricsToDisplay.AddRange(knownIdLyrics);
			}
			if (candidateLyrics != null)
			{
				lyricsToDisplay.AddRange(candidateLyrics);
			}
		}
		if (base.IsDisposed || cancellationSource.IsCancellationRequested)
		{
			return;
		}
		foreach (LyricSearchResult lyric in lyricsToDisplay)
		{
			ListViewItem listViewItem = new ListViewItem("");
			string lyricUrl = lyric.LyricUrl;
			if (lyricUrl == null && lyric.HasDownloadableLyric())
			{
				listViewItem.ImageIndex = 0;
			}
			else if (!string.IsNullOrEmpty(lyricUrl) && lyricUrl.EndsWith(".lrc", StringComparison.OrdinalIgnoreCase))
			{
				listViewItem.ImageIndex = 0;
			}
			else
			{
				listViewItem.ImageIndex = 1;
			}
			listViewItem.SubItems.Add(lyric.SearchSource.GetDisplayName());
			listViewItem.SubItems.Add(lyric.Title);
			listViewItem.SubItems.Add(lyric.Artist);
			listViewItem.SubItems.Add(lyric.Album);
			listViewItem.Tag = lyric;
			lyricListView.Items.Add(listViewItem);
		}
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing && components != null)
		{
			components.Dispose();
		}
		base.Dispose(disposing);
	}

	private void InitializeComponent()
	{
		components = new System.ComponentModel.Container();
		mainPanel = new FlowLayoutPanel();
		lyricListView = new HeaderAwareListView();
		iconColumn = new ColumnHeader();
		sourceColumn = new ColumnHeader();
		titleColumn = new ColumnHeader();
		artistColumn = new ColumnHeader();
		albumColumn = new ColumnHeader();
		lyricIconImages = new ImageList(components);
		footerPanel = new FlowLayoutPanel();
		buttonPanel = new FlowLayoutPanel();
		okButton = new Button();
		cancelButton = new Button();
		progressImage = new PictureBox();
		mainPanel.SuspendLayout();
		footerPanel.SuspendLayout();
		buttonPanel.SuspendLayout();
		((ISupportInitialize)progressImage).BeginInit();
		SuspendLayout();
		mainPanel.Controls.Add(lyricListView);
		mainPanel.Controls.Add(footerPanel);
		mainPanel.Dock = DockStyle.Fill;
		mainPanel.FlowDirection = FlowDirection.TopDown;
		mainPanel.Location = new Point(0, 0);
		mainPanel.Name = "flowLayoutPanel1";
		mainPanel.Size = new Size(684, 661);
		mainPanel.TabIndex = 0;
		mainPanel.WrapContents = false;
		mainPanel.SizeChanged += HandleMainPanelSizeChanged;
		lyricListView.Columns.AddRange(new ColumnHeader[5] { iconColumn, sourceColumn, titleColumn, artistColumn, albumColumn });
		lyricListView.FullRowSelect = true;
		lyricListView.HeaderStyle = ColumnHeaderStyle.Nonclickable;
		lyricListView.HideSelection = false;
		lyricListView.Location = new Point(0, 0);
		lyricListView.Margin = new Padding(0);
		lyricListView.MultiSelect = false;
		lyricListView.Name = "listView1";
		lyricListView.Size = new Size(534, 549);
		lyricListView.SmallImageList = lyricIconImages;
		lyricListView.TabIndex = 0;
		lyricListView.UseCompatibleStateImageBehavior = false;
		lyricListView.View = View.Details;
		lyricListView.DoubleClick += AcceptSelectionOnDoubleClick;
		iconColumn.Text = "";
		iconColumn.Width = 40;
		sourceColumn.Text = Resources.source;
		sourceColumn.Width = 70;
		titleColumn.Text = "Title";
		titleColumn.Width = 180;
		artistColumn.Text = "Artist";
		artistColumn.Width = 180;
		albumColumn.Text = "Album";
		albumColumn.Width = 180;
		lyricIconImages.ColorDepth = ColorDepth.Depth24Bit;
		lyricIconImages.ImageSize = new Size(32, 32);
		lyricIconImages.TransparentColor = Color.Transparent;
		footerPanel.Controls.Add(buttonPanel);
		footerPanel.Controls.Add(progressImage);
		footerPanel.Dock = DockStyle.Fill;
		footerPanel.Location = new Point(0, 549);
		footerPanel.Margin = new Padding(0);
		footerPanel.Name = "flowLayoutPanel2";
		footerPanel.Size = new Size(534, 60);
		footerPanel.TabIndex = 7;
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
		okButton.Click += AcceptSelection;
		cancelButton.Location = new Point(120, 0);
		cancelButton.Margin = new Padding(20, 0, 0, 0);
		cancelButton.Name = "btnCancel";
		cancelButton.Size = new Size(100, 35);
		cancelButton.TabIndex = 2;
		cancelButton.Text = "Cancel";
		cancelButton.UseVisualStyleBackColor = true;
		cancelButton.Click += CancelSelection;
		progressImage.Image = Resources.img_wait;
		progressImage.Location = new Point(220, 13);
		progressImage.Margin = new Padding(0, 13, 0, 0);
		progressImage.Name = "pbProgress";
		progressImage.Size = new Size(32, 32);
		progressImage.SizeMode = PictureBoxSizeMode.Zoom;
		progressImage.TabIndex = 7;
		progressImage.TabStop = false;
		AutoScaleDimensions = new SizeF(96f, 96f);
		AutoScaleMode = AutoScaleMode.Dpi;
		base.ClientSize = new Size(684, 661);
		base.Controls.Add(mainPanel);
		Font = new Font("Tahoma", 9f, FontStyle.Regular, GraphicsUnit.Point, 0);
		MinimumSize = new Size(350, 500);
		base.Name = "FormLyricSearch";
		base.ShowIcon = false;
		base.StartPosition = FormStartPosition.CenterParent;
		Text = "Search Lyric from network";
		mainPanel.ResumeLayout(performLayout: false);
		footerPanel.ResumeLayout(performLayout: false);
		buttonPanel.ResumeLayout(performLayout: false);
		((ISupportInitialize)progressImage).EndInit();
		ResumeLayout(performLayout: false);
	}

}





