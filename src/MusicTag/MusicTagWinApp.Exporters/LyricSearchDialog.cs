using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MusicTag.Consumers;
using MusicTag.Serialization;
using MusicTag.Services;
using MusicTagWinApp.Adapter;
using MusicTagWinApp.Containers;
using MusicTagWinApp.Instances;
using MusicTagWinApp.Properties;
using MusicTagWinApp.Roles;
using MusicTagWinApp.Web;
using MusicTagWinApp.Win32.Taskbar;

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
			remainingResultLimit = TextUtilities.GetWebSearchResultLimit();
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

	private Panel footerPanel;

	private ColumnHeader artistColumn;

	private ColumnHeader albumColumn;

	private ImageList lyricIconImages;

	private ColumnHeader sourceColumn;

	private ColumnHeader iconColumn;

	private Label searchStatusLabel;

	private SearchStatusIndicator searchStatusIndicator;

	private Action<SourceSearchStatus> searchStatusReporter;

	private HttpResult lastSourceTransportResult;

	// 本轮各源最终结果统计(后台搜索线程串行写入,await 后由 UI 线程读取):
	// 有结果即视为完成;0 结果且末次传输出错则记录错误,供搜索结束时上报 Error。
	private readonly SourceOutcomeTracker outcomeTracker = new SourceOutcomeTracker();

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
		searchStatusIndicator = new SearchStatusIndicator(searchStatusLabel, () => lyricListView.Items.Count > 0, components);
		LayoutFooterControls();
		ApplyLocalizedText();
	}

	private void InitializeImagesAndColumns()
	{
		lyricIconImages.Images.Clear();
		lyricIconImages.ImageSize = new Size(ImageUtilities.ScaleByDpi(lyricIconImages.ImageSize.Width), ImageUtilities.ScaleByDpi(lyricIconImages.ImageSize.Height));
		lyricIconImages.ColorDepth = ColorDepth.Depth24Bit;
		lyricIconImages.TransparentColor = Color.Transparent;
		lyricIconImages.Images.Add("fileext_lrc.png", ImageUtilities.LoadResourceBitmap("fileext_lrc", lyricIconImages.ImageSize));
		lyricIconImages.Images.Add("fileext_txt.png", ImageUtilities.LoadResourceBitmap("fileext_txt", lyricIconImages.ImageSize));
		foreach (ColumnHeader item in lyricListView.Columns)
		{
			item.Width = ImageUtilities.ScaleByDpi(item.Width);
		}
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
		SearchStatusIndicator.LayoutFooterStatus(footerPanel, buttonPanel, searchStatusLabel);
	}

	protected override void OnShown(EventArgs e)
	{
		base.OnShown(e);
		if (candidateLyrics != null && cachedLyricSearchContext != null && candidateLyrics.Any() && cachedLyricSearchSource == selectedSource && trackInfo.Title == cachedLyricSearchContext.Title && trackInfo.Artist == cachedLyricSearchContext.Artist && trackInfo.Album == cachedLyricSearchContext.Album)
		{
			// 缓存复用:不联网搜索,状态标识保持隐藏。
			searchStatusIndicator.Reset();
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
		searchStatusIndicator.StopCountdown();
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
			DialogService.ShowErrorMessage(Resources.Msg_PleaseSelectItem);
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
		lastSourceTransportResult = null;
		List<LyricSearchResult> lyrics = SearchLyricsBySource(searchSource, useKnownMusicId, trackInfo, int.MaxValue, existingLyrics, sourceOrder, cancellationSource, searchCandidateTracks, searchStatusReporter, result => lastSourceTransportResult = result);
		outcomeTracker.Record(searchSource, (lyrics?.Count ?? 0) > 0, lastSourceTransportResult);
		return lyrics;
	}

	public static List<LyricSearchResult> SearchLyricsBySource(SearchSource searchSource, bool useKnownMusicId, TrackSearchContext trackInfo, int maxResults, List<LyricSearchResult> existingLyrics, int sourceOrder, CancellationTokenSource cancellation, bool searchCandidateTracks, Action<SourceSearchStatus> statusReporter = null, Action<HttpResult> transportSink = null)
	{
		// 经工厂按源构造歌词 provider(已注入 StatusReporter);未知源(原 switch 的 default)返回空列表。
		// knownSongId / existingLyrics 仅网易云接收,QQ/酷狗/酷我经显式接口实现转发时丢弃(等价原 3 参 concrete);
		// useKnownMusicId 仅在 LinkedMusicMetadata 已解引用(musicId>0L)的语境为 true,故三元对其余源短路取 0L、不触 NRE。
		// searchCandidateTracks 形参在各源实现中均未使用,保留以维持静态签名(AutoMatchTagsDialog 复用)。
		using ILyricSearchProvider provider = SearchProviderFactory.CreateLyricSearch(searchSource, cancellation, statusReporter);
		if (provider == null)
		{
			return new List<LyricSearchResult>();
		}
		List<LyricSearchResult> lyrics = provider.SearchLyrics((trackInfo.Title + " " + trackInfo.Artist).Trim(), Math.Min(SearchProviderPolicy.ResultLimit(searchSource), maxResults), useKnownMusicId ? trackInfo.LinkedMusicMetadata.musicId : 0L, existingLyrics, sourceOrder);
		transportSink?.Invoke(provider.LastTransportResult);
		return lyrics;
	}

	private List<TrackSearchResult> SearchTrackCandidates(SearchSource searchSource, int sourceOrder, bool fromCandidateSearch)
	{
		lastSourceTransportResult = null;
		List<TrackSearchResult> tracks = SearchTracksBySource(searchSource, trackInfo, sourceOrder, cancellationSource, fromCandidateSearch, searchStatusReporter, result => lastSourceTransportResult = result);
		outcomeTracker.Record(searchSource, (tracks?.Count ?? 0) > 0, lastSourceTransportResult);
		return tracks;
	}

	public static List<TrackSearchResult> SearchTracksBySource(SearchSource searchSource, TrackSearchContext trackInfo, int sourceOrder, CancellationTokenSource cancellation, bool fromCandidateSearch, Action<SourceSearchStatus> statusReporter = null, Action<HttpResult> transportSink = null)
	{
		// 经工厂按源构造曲目 provider(已注入 StatusReporter);未知源(原 switch 的 default)返回空列表。
		// 原各 case 均以 knownSongId=0L、searchPass=0、两个新建空列表调用,此处逐一复刻;knownSongId 仅网易云接收,
		// QQ/酷狗/酷我经显式接口实现转发时丢弃(酷我的两个空列表映射到 concrete previousResults/currentResults,值同)。
		// fromCandidateSearch 形参在各源实现中均未使用,保留以维持静态签名(AutoMatchTagsDialog 复用)。
		using ITrackSearchProvider provider = SearchProviderFactory.CreateTrackSearch(searchSource, cancellation, statusReporter);
		if (provider == null)
		{
			return new List<TrackSearchResult>();
		}
		List<TrackSearchResult> tracks = provider.SearchTracks((trackInfo.Title + " " + trackInfo.Artist).Trim(), SearchProviderPolicy.ResultLimit(searchSource), 0L, 0, sourceOrder, new List<TrackSearchResult>(), new List<TrackSearchResult>());
		transportSink?.Invoke(provider.LastTransportResult);
		return tracks;
	}

	private LyricSearchResult DownloadLyricForTrack(TrackSearchResult track)
	{
		return DownloadLyricBySource(track, trackInfo, cancellationSource);
	}

	public static LyricSearchResult DownloadLyricBySource(TrackSearchResult track, TrackSearchContext trackInfo, CancellationTokenSource cancellation)
	{
		// 按候选曲目自身的来源加载歌词;未知源(即原 switch 的 default)返回 null。trackInfo 形参在各源实现中
		// 均未使用,保留以维持静态方法签名不变(AutoMatchTagsDialog 复用)。原 4 case 均不注入 StatusReporter,
		// 故工厂此处不传 statusReporter。酷我经显式接口实现转发到单数名 LoadLyricForTrack。
		using ITrackLyricLoader provider = SearchProviderFactory.CreateLyricLoader(track.SearchSource, cancellation);
		if (provider == null)
		{
			return null;
		}
		return provider.LoadLyricsForTrack(track);
	}

	private async void StartLyricSearch()
	{
		LyricSearchSession searchSession = new LyricSearchSession(this);
		CancellationToken cancellationToken = cancellationSource.Token;
		BeginSearchStatus();
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
			if (!cancellationToken.IsCancellationRequested)
			{
				ReportFinalSearchOutcomes();
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
				taskbarProgress.SetProgressState(TaskbarProgressBarStatus.NoProgress);
				searchStatusIndicator.End();
			}
		}
	}

	// 本轮要搜索的已勾选源集合(与 LyricSearchSession 的遍历口径一致):
	// 选定单源时仅该源,否则按排序取所有启用源。
	private List<SearchSource> GetEnabledLyricSearchSources()
	{
		if (selectedSource.HasValue)
		{
			return new List<SearchSource> { selectedSource.Value };
		}
		return LyricSearchResult.GetSortedLyricSourceSettings().Where(sourceItem => sourceItem.Enabled).Select(sourceItem => sourceItem.SearchSource).ToList();
	}

	// 开始一轮搜索(UI 线程):清空上轮统计、建状态通道、先把各源标记为"搜索中"。
	private void BeginSearchStatus()
	{
		outcomeTracker.Clear();
		// 状态通道:Progress<T> 在 UI 线程构造,后台 provider 的上报(如 QQ 限流重试)经此编组回 UI 线程。
		searchStatusReporter = searchStatusIndicator.BeginReporting();
		foreach (SearchSource source in GetEnabledLyricSearchSources())
		{
			searchStatusIndicator.Report(new SourceSearchStatus
			{
				Source = source,
				Phase = SourceSearchPhase.Searching
			});
		}
	}

	// 搜索整体结束(UI 线程,await 后回到 UI 线程,后台写入的统计此时已可见):
	// 始终无结果且末次传输出错的源标记 Error,其余标记 Completed。
	private void ReportFinalSearchOutcomes()
	{
		outcomeTracker.ReportFinal(GetEnabledLyricSearchSources(), searchStatusIndicator.Report);
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
		footerPanel = new Panel();
		buttonPanel = new FlowLayoutPanel();
		okButton = new Button();
		cancelButton = new Button();
		searchStatusLabel = new Label();
		mainPanel.SuspendLayout();
		footerPanel.SuspendLayout();
		buttonPanel.SuspendLayout();
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
		footerPanel.Controls.Add(searchStatusLabel);
		footerPanel.Controls.Add(buttonPanel);
		footerPanel.Dock = DockStyle.Fill;
		footerPanel.Location = new Point(0, 549);
		footerPanel.Margin = new Padding(0);
		footerPanel.Name = "flowLayoutPanel2";
		footerPanel.Size = new Size(534, 60);
		footerPanel.TabIndex = 7;
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
		searchStatusLabel.AutoSize = false;
		searchStatusLabel.AutoEllipsis = true;
		searchStatusLabel.Name = "searchStatusLabel";
		searchStatusLabel.Size = new Size(200, 40);
		searchStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
		searchStatusLabel.ForeColor = SystemColors.GrayText;
		searchStatusLabel.Visible = false;
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
		ResumeLayout(performLayout: false);
	}

}





