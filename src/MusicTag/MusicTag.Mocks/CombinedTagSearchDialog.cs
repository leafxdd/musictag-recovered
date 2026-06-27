using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MusicTag.Consumers;
using MusicTag.Importers;
using MusicTag.Readers;
using MusicTag.Serialization;
using MusicTag.Services;
using MusicTag.States;
using MusicTagWinApp.Adapter;
using MusicTagWinApp.Containers;
using MusicTagWinApp.Exporters;
using MusicTagWinApp.Instances;
using MusicTagWinApp.Listeners;
using MusicTagWinApp.Properties;
using MusicTagWinApp.Roles;
using MusicTagWinApp.Stubs;
using MusicTagWinApp.Web;
using MusicTagWinApp.Win32.Taskbar;
using MusicTagWinApp.Writers;
using wyDay.Controls;

namespace MusicTag.Mocks;

internal class CombinedTagSearchDialog : Form
{
	private sealed class SearchResultSelectionContext
	{
		public CoverImageListViewItem ListItem;

		public CombinedTagSearchDialog Owner;

		internal void SelectResult()
		{
			ListItem.Selected = true;
			Owner.BeginInvoke(new Action(Owner.FocusResultList));
		}

	}

	private sealed class DeferredLyricDownloadContext
	{
		public LyricSearchResult LyricResult;

		public CombinedTagSearchDialog Owner;

		internal LyricSearchResult LoadLyric()
		{
			return LyricResult.DeferredLyricLoader(Owner.cancellationSource);
		}
	}

	private sealed class CoverDownloadRequestContext
	{
		public CoverSearchResult CoverResult;

		public CombinedTagSearchDialog Owner;
	}

	private sealed class CoverImageLoadTask
	{
		public Size? OriginalImageSize;

		public CoverDownloadRequestContext Request;

		internal Image LoadOrDownloadImage()
		{
			CoverDownloadFile coverDownload = new CoverDownloadFile
			{
				LoadTask = this,
				LocalCoverPath = Request.CoverResult.LocalCoverPath ?? DatabaseMapper.GetPictureCacheDirectory() + DatabaseMapper.ComputeMd5HashString(Request.CoverResult.CoverUrl, "UTF-8").Replace("-", ""),
				DownloadedBytes = 0L
			};
			Request.CoverResult.LocalCoverPath = coverDownload.LocalCoverPath;
			if (coverDownload.IsAlreadyDownloading())
			{
				return null;
			}
			RemoteTagProviderBase.DownloadStatus dlStatus = RemoteTagProviderBase.DownloadStatus.Error;
			Bitmap bitmap = null;
			if (File.Exists(coverDownload.LocalCoverPath))
			{
				bitmap = coverDownload.DecodeAndResizeImage();
			}
			if (bitmap == null)
			{
				dlStatus = coverDownload.DownloadCoverFile();
				if (dlStatus == RemoteTagProviderBase.DownloadStatus.Success)
				{
					bitmap = coverDownload.DecodeAndResizeImage();
				}
				else
				{
					coverDownload.DeleteFailedDownload();
				}
			}
			if (bitmap == null)
			{
				Console.WriteLine(string.Concat("bmp fail:", Request.CoverResult.CoverUrl, ",", coverDownload.LocalCoverPath, ",", coverDownload.DownloadedBytes));
				if (dlStatus != RemoteTagProviderBase.DownloadStatus.NotFound)
				{
					return Request.Owner.coverImageList.Images["download_failed"];
				}
				return Request.Owner.coverImageList.Images["image_not_found"];
			}
			return bitmap;
		}
	}

	private sealed class CoverDownloadFile
	{
		public string LocalCoverPath;

		public long DownloadedBytes;

		public CoverImageLoadTask LoadTask;

		internal bool IsAlreadyDownloading()
		{
			HashSet<string> queuedCoverDownloadPaths = LoadTask.Request.Owner.queuedCoverDownloadPaths;
			bool lockTaken = false;
			try
			{
				Monitor.Enter(queuedCoverDownloadPaths, ref lockTaken);
				if (!queuedCoverDownloadPaths.Contains(LocalCoverPath))
				{
					queuedCoverDownloadPaths.Add(LocalCoverPath);
					return false;
				}
				return true;
			}
			finally
			{
				if (lockTaken)
				{
					Monitor.Exit(queuedCoverDownloadPaths);
				}
			}
		}

		internal RemoteTagProviderBase.DownloadStatus DownloadCoverFile()
		{
			try
			{
				var (result, downloadedBytes) = LoadTask.Request.CoverResult.CoverDownloader(LoadTask.Request.Owner.cancellationSource, LocalCoverPath, 300000);
				DownloadedBytes = downloadedBytes;
				return result;
			}
			catch (System.Exception ex)
			{
				Console.WriteLine("downloadfile fail:" + ex.Message);
			}
			return RemoteTagProviderBase.DownloadStatus.Error;
		}

		internal void DeleteFailedDownload()
		{
			try
			{
				if (File.Exists(LocalCoverPath))
				{
					File.Delete(LocalCoverPath);
				}
			}
			catch (System.Exception ex)
			{
				Console.WriteLine("deletefile fail:" + ex.Message);
			}
		}

		internal Bitmap DecodeAndResizeImage()
		{
			try
			{
				using Bitmap bitmap = new Bitmap(LocalCoverPath);
				LoadTask.OriginalImageSize = bitmap.Size;
				return DatabaseMapper.ResizeImageToFit(bitmap, LoadTask.Request.Owner.coverImageList.ImageSize, centerOnCanvas: true);
			}
			catch (System.Exception ex)
			{
				Console.WriteLine("decode bitmap fail " + ex.Message);
			}
			return null;
		}
	}

	private sealed class TrackSearchCoordinator
	{
		public CombinedTagSearchDialog Owner;

		public IProgress<List<TrackSearchResult>> ProgressReporter;

		public Predicate<SourceItem> preferredSourcePredicate;

		internal void OnSearchResultsReported(List<TrackSearchResult> searchResults)
		{
			if (!Owner.cancellationSource.IsCancellationRequested)
			{
				cachedSearchResults.AddRange(searchResults);
				Owner.AddSearchResultsToList(searchResults);
			}
		}

		internal bool SearchAllSources()
		{
			TrackSearchLimitState searchLimits = new TrackSearchLimitState();
			searchLimits.Coordinator = this;
			if (!Owner.currentSearchContext.HasTitle())
			{
				return true;
			}
			searchLimits.AccumulatedResults = new List<TrackSearchResult>();
			searchLimits.RemainingGlobalResults = DatabaseMapper.GetWebSearchResultLimit();
			searchLimits.RemainingResultsBySource = new Dictionary<SearchSource, int>();
			searchLimits.CurrentBatch = null;
			SearchSource? preferredSource = Owner.preferredSource;
			if (preferredSource.HasValue)
			{
				SourceItem preferredSourceItem = TrackSearchResult.GetTagSourceSettings().Find(preferredSourcePredicate ?? (preferredSourcePredicate = IsPreferredSource));
				searchLimits.RemainingResultsBySource[preferredSource.Value] = preferredSourceItem.GetEffectiveSearchResultLimit();
				ReportSearching(preferredSource.Value);
				if (!Owner.cancellationSource.IsCancellationRequested && searchLimits.RemainingGlobalResults > 0 && Owner.currentSearchContext.LinkedMusicMetadata.musicId > 0L && preferredSource.Value == SearchSource.Music163)
				{
					searchLimits.CurrentBatch = Owner.SearchCurrentContextTracks(preferredSource.Value, useLinkedNetEaseId: true, searchLimits.AccumulatedResults, 0);
					searchLimits.RankLimitAndReportCurrentBatch(useProviderRanking: true);
				}
				if (!Owner.cancellationSource.IsCancellationRequested && searchLimits.RemainingGlobalResults > 0 && searchLimits.RemainingResultsBySource[preferredSource.Value] > 0)
				{
					searchLimits.CurrentBatch = Owner.SearchCurrentContextTracks(preferredSource.Value, useLinkedNetEaseId: false, searchLimits.AccumulatedResults, 0);
					searchLimits.RankLimitAndReportCurrentBatch(useProviderRanking: true);
				}
				return !Owner.cancellationSource.IsCancellationRequested;
			}
			List<SourceItem> tagSources = TrackSearchResult.GetSortedTagSourceSettings();
			tagSources.ForEach(searchLimits.InitializeSourceLimit);
			foreach (SourceItem enabledSource in tagSources)
			{
				if (enabledSource.Enabled && searchLimits.RemainingResultsBySource[enabledSource.SearchSource] > 0)
				{
					ReportSearching(enabledSource.SearchSource);
				}
			}
			int searchPass = 0;
			SourceItem netEaseSource;
			if (!Owner.cancellationSource.IsCancellationRequested && searchLimits.RemainingGlobalResults > 0 && Owner.currentSearchContext.LinkedMusicMetadata.musicId > 0L && (netEaseSource = tagSources.Find(searchLimits.IsPrimaryNetEaseSourceAvailable)) != null)
			{
				searchLimits.CurrentBatch = new List<TrackSearchResult>();
				searchLimits.CurrentBatch.AddRange(Owner.SearchCurrentContextTracks(netEaseSource.SearchSource, useLinkedNetEaseId: true, searchLimits.AccumulatedResults, searchPass++));
				searchLimits.RankLimitAndReportCurrentBatch(useProviderRanking: true);
			}
			if (!Owner.cancellationSource.IsCancellationRequested && searchLimits.RemainingGlobalResults > 0)
			{
				List<SourceItem> primarySources = tagSources.FindAll((SourceItem source) => source.Enabled && !source.IsSecondarySource && searchLimits.RemainingResultsBySource[source.SearchSource] > 0);
				searchLimits.CurrentBatch = SearchSourcesInParallel(primarySources, searchLimits.AccumulatedResults, useLinkedNetEaseId: false, searchPass);
				searchPass += primarySources.Count;
				searchLimits.RankLimitAndReportCurrentBatch(useProviderRanking: true);
			}
			if (!Owner.cancellationSource.IsCancellationRequested && searchLimits.RemainingGlobalResults > 0)
			{
				List<SourceItem> secondarySources = tagSources.FindAll((SourceItem source) => source.Enabled && source.IsSecondarySource && searchLimits.RemainingResultsBySource[source.SearchSource] > 0);
				searchLimits.CurrentBatch = SearchSourcesInParallel(secondarySources, searchLimits.AccumulatedResults, useLinkedNetEaseId: false, searchPass);
				searchPass += secondarySources.Count;
				searchLimits.RankLimitAndReportCurrentBatch(useProviderRanking: false);
			}
			return !Owner.cancellationSource.IsCancellationRequested;
		}

		internal bool IsPreferredSource(SourceItem sourceItem)
		{
			return sourceItem.SearchSource == Owner.preferredSource;
		}

		// 在源开始搜索前上报"搜索中",使状态行立即列出所有已勾选源(尚未完成者)。
		private void ReportSearching(SearchSource source)
		{
			Owner.searchStatusReporter?.Invoke(new SourceSearchStatus
			{
				Source = source,
				Phase = SourceSearchPhase.Searching
			});
		}

		// 并行搜索一组源:每个源各开一个 Task 跑各自的网络请求,全部完成(或取消)后按源顺序合并。
		// 各源使用独立 provider 实例、只读访问 existingResults 做同源去重,并行期间不修改任何共享状态,
		// 故无需加锁(详见 docs/SEARCH_STATUS_INDICATOR_DESIGN.md 阶段2)。searchPass 按源在列表中的
		// 次序确定性分配,与原串行版本逐源自增完全一致,保证结果元数据不变。
		private List<TrackSearchResult> SearchSourcesInParallel(List<SourceItem> sources, List<TrackSearchResult> existingResults, bool useLinkedNetEaseId, int baseSearchPass)
		{
			List<TrackSearchResult> combinedResults = new List<TrackSearchResult>();
			if (sources == null || sources.Count == 0)
			{
				return combinedResults;
			}
			CancellationToken cancellationToken = Owner.cancellationSource.Token;
			Task<List<TrackSearchResult>>[] sourceTasks = new Task<List<TrackSearchResult>>[sources.Count];
			for (int taskIndex = 0; taskIndex < sources.Count; taskIndex++)
			{
				SearchSource source = sources[taskIndex].SearchSource;
				int searchPassForSource = baseSearchPass + taskIndex;
				sourceTasks[taskIndex] = Task.Run(() => Owner.SearchCurrentContextTracks(source, useLinkedNetEaseId, existingResults, searchPassForSource), cancellationToken);
			}
			try
			{
				Task.WaitAll(sourceTasks, cancellationToken);
			}
			catch (OperationCanceledException)
			{
			}
			catch (AggregateException)
			{
			}
			// 仅收割已正常完成的源;被取消而仍在后台运行的源 Status 非 RanToCompletion,
			// 跳过即可(不访问 .Result,避免阻塞),它们会通过共享 token 自行中断并释放。
			foreach (Task<List<TrackSearchResult>> sourceTask in sourceTasks)
			{
				if (sourceTask.Status == TaskStatus.RanToCompletion && sourceTask.Result != null)
				{
					combinedResults.AddRange(sourceTask.Result);
				}
			}
			return combinedResults;
		}
	}

	private sealed class TrackSearchLimitState
	{
		public List<TrackSearchResult> CurrentBatch;

		public int RemainingGlobalResults;

		public Dictionary<SearchSource, int> RemainingResultsBySource;

		public List<TrackSearchResult> AccumulatedResults;

		public TrackSearchCoordinator Coordinator;

		internal void RankLimitAndReportCurrentBatch(bool useProviderRanking)
		{
			TrackResultLimitCollector resultLimiter = new TrackResultLimitCollector();
			resultLimiter.SearchLimits = this;
			if (useProviderRanking)
			{
				Coordinator.Owner.RankCurrentSearchResults(CurrentBatch);
			}
			else
			{
				Coordinator.Owner.SortCurrentSearchResults(CurrentBatch);
			}
			resultLimiter.LimitedResults = new List<TrackSearchResult>();
			CurrentBatch.ForEach(resultLimiter.AddIfWithinLimit);
			AccumulatedResults.AddRange(resultLimiter.LimitedResults);
			Coordinator.ProgressReporter.Report(resultLimiter.LimitedResults);
		}

		internal void InitializeSourceLimit(SourceItem sourceItem)
		{
			RemainingResultsBySource[sourceItem.SearchSource] = sourceItem.GetEffectiveSearchResultLimit();
		}

		internal bool IsPrimaryNetEaseSourceAvailable(SourceItem sourceItem)
		{
			return sourceItem.Enabled && !sourceItem.IsSecondarySource && RemainingResultsBySource[sourceItem.SearchSource] > 0 && sourceItem.SearchSource == SearchSource.Music163;
		}
	}

	private sealed class TrackResultLimitCollector
	{
		public List<TrackSearchResult> LimitedResults;

		public TrackSearchLimitState SearchLimits;

		internal void AddIfWithinLimit(TrackSearchResult trackResult)
		{
			if (SearchLimits.RemainingGlobalResults <= 0 || SearchLimits.RemainingResultsBySource[trackResult.SearchSource] <= 0)
			{
				return;
			}
			LimitedResults.Add(trackResult);
			SearchLimits.RemainingResultsBySource[trackResult.SearchSource]--;
			int remainingResults = SearchLimits.RemainingGlobalResults;
			SearchLimits.RemainingGlobalResults = remainingResults - 1;
		}
	}

	private int activeMediaDownloadCount;

	private static bool cachedSearchCompleted;

	private static TrackSearchContext lastSearchContext;

	private static SearchSource? lastPreferredSource;

	private TrackSearchContext currentSearchContext;

	private SearchSource? preferredSource;

	// 联网搜索状态标识相关(均仅在 UI 线程访问)。searchStatusReporter 由后台搜索线程调用,
	// 内部经 Progress<T> 编组回 UI 线程,详见 docs/SEARCH_STATUS_INDICATOR_DESIGN.md。
	private Action<SourceSearchStatus> searchStatusReporter;

	private readonly Dictionary<SearchSource, SourceSearchStatus> sourceSearchStatuses = new Dictionary<SearchSource, SourceSearchStatus>();

	private bool searchInProgress;

	private bool searchHasRun;

	private readonly CancellationTokenSource cancellationSource;

	private static List<TrackSearchResult> cachedSearchResults;

	private readonly HashSet<string> queuedCoverDownloadPaths;

	private readonly Dictionary<string, Image> coverImageCache;

	private readonly TaskbarProgressController taskbarProgress;

	private IContainer components;

	private FlowLayoutPanel mainPanel;

	private ImageList coverImageList;

	private Panel footerPanel;

	private FlowLayoutPanel buttonPanel;

	private Button cancelButton;

	private MusicTagWinApp.Roles.EditableListView searchResultsListView;

	private ColumnHeader coverColumn;

	private ColumnHeader sourceColumn;

	private ColumnHeader titleColumn;

	private ColumnHeader artistColumn;

	private ColumnHeader albumColumn;

	private ColumnHeader commentColumn;

	private SplitButton okSplitButton;

	private ContextMenuStrip okButtonMenu;

	private ToolStripMenuItem overwriteOptionsMenuItem;

	private System.Windows.Forms.Timer cachedResultsTimer;

	private ContextMenuStrip coverContextMenu;

	private ToolStripMenuItem openCoverMenuItem;

	private ToolStripMenuItem extractCoverMenuItem;

	private SaveFileDialog saveCoverDialog;

	private Label searchStatusLabel;

	private System.Windows.Forms.Timer retryCountdownTimer;

	public void SetSearchContext(TrackSearchContext searchContext)
	{
		currentSearchContext = searchContext;
	}

	public void SetPreferredSource(SearchSource? source)
	{
		preferredSource = source;
	}

	public TrackSearchResult GetSelectedTrackResult()
	{
		return searchResultsListView.SelectedItems[0].Tag as TrackSearchResult;
	}

	public static void ClearCachedSearchResults()
	{
		cachedSearchResults?.Clear();
	}

	public CombinedTagSearchDialog()
	{
		cancellationSource = new CancellationTokenSource();
		queuedCoverDownloadPaths = new HashSet<string>();
		coverImageCache = new Dictionary<string, Image>();
		taskbarProgress = new TaskbarProgressController(this);
		InitializeComponent();
		InitializeResultListImagesAndScaling();
		ApplyLocalizedText();
		UpdateSearchDialogLayout();
	}

	private void InitializeResultListImagesAndScaling()
	{
		coverImageList.Images.Clear();
		coverImageList.ImageSize = new Size(DatabaseMapper.ScaleByDpi(coverImageList.ImageSize.Width), DatabaseMapper.ScaleByDpi(coverImageList.ImageSize.Height));
		coverImageList.ColorDepth = ColorDepth.Depth24Bit;
		coverImageList.TransparentColor = Color.Transparent;
		coverImageList.Images.Add("download_failed", DatabaseMapper.LoadResourceBitmap("download_failed", coverImageList.ImageSize));
		coverImageList.Images.Add("image_not_found", DatabaseMapper.LoadResourceBitmap("imagenotfound", coverImageList.ImageSize));
		coverImageList.Images.Add("loading", DatabaseMapper.LoadResourceBitmap("downloading", coverImageList.ImageSize));
		foreach (ColumnHeader column in searchResultsListView.Columns)
		{
			column.Width = DatabaseMapper.ScaleByDpi(column.Width);
		}
		okSplitButton.AutoSize = false;
		FontAwesome.Properties fontProperties = new FontAwesome.Properties
		{
			Size = DatabaseMapper.ScaleByDpi(24f),
			ShowBorder = false
		};
		okSplitButton.Size = new Size(DatabaseMapper.ScaleByDpi(100f), DatabaseMapper.ScaleByDpi(35f));
		okSplitButton.Image = FontAwesome.Type.Check.AsImage(fontProperties);
	}

	private void ApplyLocalizedText()
	{
		okSplitButton.Text = Resources.OK;
		cancelButton.Text = Resources.Cancel;
		sourceColumn.Text = Resources.source;
		titleColumn.Text = Resources.title;
		artistColumn.Text = Resources.artist;
		albumColumn.Text = Resources.album;
		commentColumn.Text = Resources.comment;
		overwriteOptionsMenuItem.Text = Resources.OverwriteOptions;
	}

	protected override void OnShown(EventArgs param)
	{
		base.OnShown(param);
		TrackSearchContext searchContext = currentSearchContext;
		bool canReuseCachedResults = cachedSearchCompleted && cachedSearchResults != null && lastSearchContext != null && cachedSearchResults.Any() && lastPreferredSource == preferredSource && searchContext.Title == lastSearchContext.Title && searchContext.Artist == lastSearchContext.Artist && searchContext.Album == lastSearchContext.Album;
		if (canReuseCachedResults)
		{
			// 缓存复用:不联网搜索,缓存结果非空,状态标识保持隐藏(无空态)。
			ResetSearchStatusDisplay();
			foreach (TrackSearchResult result in cachedSearchResults)
			{
				if (result.Cover != null)
				{
					result.Cover.CoverDownloadQueued = false;
				}
				if (result.LyricResult != null)
				{
					result.LyricResult.IsLoaded = false;
				}
			}
			cachedResultsTimer.Tick += AddCachedResultsOnTimerTick;
			cachedResultsTimer.Start();
		}
		else
		{
			SearchCombinedTagsAsync();
		}
		lastSearchContext = searchContext;
		lastPreferredSource = preferredSource;
		Text = searchContext.Title + " | " + searchContext.Artist + " | " + searchContext.Album;
	}

	protected override void OnClosed(EventArgs spec)
	{
		base.OnClosed(spec);
		cachedResultsTimer.Stop();
		retryCountdownTimer.Stop();
		cancellationSource.Cancel();
		string selectedCoverPath = null;
		if (base.DialogResult == DialogResult.OK)
		{
			selectedCoverPath = GetSelectedTrackResult().Cover?.LocalCoverPath;
		}
		DatabaseMapper.TrimDirectorySize(DatabaseMapper.GetPictureCacheDirectory(), selectedCoverPath, 31457280L, 62914560L);
	}

	private void UpdateSearchDialogLayout()
	{
		searchResultsListView.Width = mainPanel.Width;
		searchResultsListView.Height = mainPanel.Height - footerPanel.Height;
		// footerPanel 为普通 Panel,子控件绝对定位:按钮恒定居中(与状态标签显隐无关),
		// 状态标签置于按钮右侧、垂直中线与按钮对齐。详见 docs/SEARCH_STATUS_INDICATOR_DESIGN.md。
		int buttonLeft = Math.Max(0, (footerPanel.Width - buttonPanel.Width) / 2);
		int buttonTop = Math.Max(0, (footerPanel.Height - buttonPanel.Height) / 2);
		buttonPanel.Location = new Point(buttonLeft, buttonTop);
		int statusGap = 12;
		int statusLeft = buttonPanel.Location.X + buttonPanel.Width + statusGap;
		int statusTop = buttonPanel.Location.Y + buttonPanel.Height / 2 - searchStatusLabel.Height / 2;
		searchStatusLabel.Location = new Point(statusLeft, statusTop);
		searchStatusLabel.Width = Math.Max(0, footerPanel.Width - statusLeft - 8);
	}

	private void SearchPanelSizeChanged(object sender, EventArgs args)
	{
		UpdateSearchDialogLayout();
	}

	private void AddSearchResultsToList(List<TrackSearchResult> results)
	{
		if (results == null || base.IsDisposed)
		{
			return;
		}
		searchResultsListView.BeginUpdate();
		foreach (TrackSearchResult searchResult in results)
		{
			SearchResultSelectionContext selectionContext = new SearchResultSelectionContext();
			selectionContext.Owner = this;
			if (cancellationSource.IsCancellationRequested)
			{
				break;
			}
			CoverImageListViewItem coverImageListViewItem = new CoverImageListViewItem("");
			coverImageListViewItem.CoverImage = coverImageList.Images["loading"];
			coverImageListViewItem.Tag = searchResult;
			selectionContext.ListItem = coverImageListViewItem;
			EmbeddedControlSubItem embeddedControlSubItem = new EmbeddedControlSubItem();
			selectionContext.ListItem.SubItems.Add(embeddedControlSubItem);
			selectionContext.ListItem.SubItems.AddRange(new string[4] { searchResult.Title, searchResult.Artist, searchResult.Album, searchResult.Comment });
			searchResultsListView.Items.Add(selectionContext.ListItem);
			TagSearchCandidatePanel searchCandidatePanel = new TagSearchCandidatePanel();
			searchCandidatePanel.Source = searchResult.SearchSource.GetDisplayName();
			searchCandidatePanel.Year = searchResult.Year;
			searchCandidatePanel.Track = searchResult.TrackLabel;
			searchCandidatePanel.Genre = searchResult.Genre;
			searchCandidatePanel.SelectionRequested = selectionContext.SelectResult;
			searchCandidatePanel.ContextMenuRequested = ShowSelectedCoverContextMenu;
			searchResultsListView.AttachEmbeddedControl(searchCandidatePanel, embeddedControlSubItem);
			if (searchResult.Cover != null)
			{
				searchResult.Cover.ListViewIndex = selectionContext.ListItem.Index;
				if (activeMediaDownloadCount < 15)
				{
					searchResult.Cover.CoverDownloadQueued = true;
					DownloadCoverAsync(searchResult.Cover, activeMediaDownloadCount++, 0);
				}
			}
			else
			{
				selectionContext.ListItem.AssociatedValue = "image_not_found";
				selectionContext.ListItem.CoverImage = coverImageList.Images[selectionContext.ListItem.AssociatedValue];
			}
			if (searchResult.LyricResult != null)
			{
				searchResult.LyricResult.ListItemIndex = selectionContext.ListItem.Index;
				if (activeMediaDownloadCount < 15)
				{
					searchResult.LyricResult.IsLoaded = true;
					DownloadLyricAsync(searchResult.LyricResult, activeMediaDownloadCount++, 0);
				}
			}
		}
		searchResultsListView.EndUpdate();
	}

	private async void DownloadLyricAsync(LyricSearchResult lyricResult, int taskNo, int taskSubNo)
	{
		DeferredLyricDownloadContext lyricDownloadContext = new DeferredLyricDownloadContext();
		lyricDownloadContext.LyricResult = lyricResult;
		lyricDownloadContext.Owner = this;
		try
		{
			LyricSearchResult result = await Task.Run((Func<LyricSearchResult>)lyricDownloadContext.LoadLyric, cancellationSource.Token);
			if (result != null && result.HasDownloadableLyric())
			{
				lyricDownloadContext.LyricResult.Lyric = result.Lyric;
				lyricDownloadContext.LyricResult.TranslatedLyric = result.TranslatedLyric;
				((searchResultsListView.Items[lyricDownloadContext.LyricResult.ListItemIndex].SubItems[sourceColumn.Index] as EmbeddedControlSubItem).EmbeddedControl as TagSearchCandidatePanel).Lyric = "Y";
			}
			foreach (TrackSearchResult current in cachedSearchResults)
			{
				if (cancellationSource.IsCancellationRequested)
				{
					break;
				}
				lyricDownloadContext.LyricResult = current.LyricResult;
				if (lyricDownloadContext.LyricResult == null || lyricDownloadContext.LyricResult.IsLoaded)
				{
					continue;
				}
				lyricDownloadContext.LyricResult.IsLoaded = true;
				DownloadLyricAsync(lyricDownloadContext.LyricResult, taskNo, ++taskSubNo);
				return;
			}
		}
		catch (System.Exception v)
		{
			Console.WriteLine("DownloadLyric error:" + v.GetMessageChain());
		}
		activeMediaDownloadCount--;
	}

	private async void DownloadCoverAsync(CoverSearchResult coverResult, int taskNo, int taskSubNo)
	{
		CoverDownloadRequestContext coverDownloadRequest = new CoverDownloadRequestContext();
		coverDownloadRequest.CoverResult = coverResult;
		coverDownloadRequest.Owner = this;
		bool listUpdateStarted = false;
		try
		{
			CoverImageLoadTask coverLoadTask = new CoverImageLoadTask();
			coverLoadTask.Request = coverDownloadRequest;
			coverLoadTask.OriginalImageSize = null;
			Image result = await Task.Run((Func<Image>)coverLoadTask.LoadOrDownloadImage, cancellationSource.Token);
			searchResultsListView.BeginUpdate();
			listUpdateStarted = true;
			CoverImageListViewItem coverImageListViewItem = searchResultsListView.Items[coverLoadTask.Request.CoverResult.ListViewIndex] as CoverImageListViewItem;
			if (result != null)
			{
				coverImageCache.Add(coverLoadTask.Request.CoverResult.LocalCoverPath, result);
				coverImageListViewItem.AssociatedValue = coverLoadTask.Request.CoverResult.LocalCoverPath;
				foreach (CoverImageListViewItem matchingCoverItem in searchResultsListView.Items)
				{
					if ((string)matchingCoverItem.AssociatedValue != (string)coverImageListViewItem.AssociatedValue)
					{
						continue;
					}
					matchingCoverItem.CoverImage = coverImageCache[coverLoadTask.Request.CoverResult.LocalCoverPath];
					if (coverLoadTask.OriginalImageSize.HasValue)
					{
						matchingCoverItem.CoverImage.Tag = coverLoadTask.OriginalImageSize?.Width + "x" + coverLoadTask.OriginalImageSize?.Height;
					}
					else
					{
						matchingCoverItem.CoverImage.Tag = "";
					}
					((matchingCoverItem.SubItems[sourceColumn.Index] as EmbeddedControlSubItem).EmbeddedControl as TagSearchCandidatePanel).PictureSize = matchingCoverItem.CoverImage.Tag as string;
				}
			}
			else
			{
				coverImageListViewItem.AssociatedValue = coverLoadTask.Request.CoverResult.LocalCoverPath;
				if (coverImageCache.ContainsKey(coverLoadTask.Request.CoverResult.LocalCoverPath))
				{
					coverImageListViewItem.CoverImage = coverImageCache[coverLoadTask.Request.CoverResult.LocalCoverPath];
					TagSearchCandidatePanel searchCandidatePanel = (coverImageListViewItem.SubItems[sourceColumn.Index] as EmbeddedControlSubItem).EmbeddedControl as TagSearchCandidatePanel;
					foreach (CoverImageListViewItem matchingCoverItem in searchResultsListView.Items)
					{
						if (!(matchingCoverItem.AssociatedValue == coverImageListViewItem.AssociatedValue))
						{
							continue;
						}
						Image image = matchingCoverItem.CoverImage;
						if (image == null || image.Tag == null)
						{
							continue;
						}
						searchCandidatePanel.PictureSize = matchingCoverItem.CoverImage.Tag as string;
						break;
					}
				}
			}
			bool queuedNextCoverDownload = false;
			foreach (TrackSearchResult current in cachedSearchResults)
			{
				if (cancellationSource.IsCancellationRequested)
				{
					break;
				}
				CoverSearchResult nextCover = current.Cover;
				if (nextCover == null || nextCover.CoverDownloadQueued)
				{
					continue;
				}
				nextCover.CoverDownloadQueued = true;
				DownloadCoverAsync(nextCover, taskNo, ++taskSubNo);
				queuedNextCoverDownload = true;
				break;
			}
			if (!queuedNextCoverDownload)
			{
				coverLoadTask = null;
				activeMediaDownloadCount--;
			}
		}
		catch (System.Exception v)
		{
			Console.WriteLine("DownloadPicture error:" + v.GetMessageChain());
			activeMediaDownloadCount--;
		}
		finally
		{
			if (listUpdateStarted)
			{
				searchResultsListView.EndUpdate();
			}
		}
	}

	private List<TrackSearchResult> SearchCurrentContextTracks(SearchSource source, bool useLinkedNetEaseId, List<TrackSearchResult> existingResults, int searchPass)
	{
		return SearchTracksFromSource(source, useLinkedNetEaseId, existingResults, searchPass, currentSearchContext, cancellationSource, searchStatusReporter);
	}

	public static List<TrackSearchResult> SearchTracksFromSource(SearchSource source, bool useLinkedNetEaseId, List<TrackSearchResult> existingResults, int searchPass, TrackSearchContext searchContext, CancellationTokenSource cancellationSource, Action<SourceSearchStatus> statusReporter = null)
	{
		List<TrackSearchResult> results = new List<TrackSearchResult>();
		RemoteTagProviderBase searchProvider = null;
		switch (source)
		{
			case SearchSource.Music163:
				{
					using NetEaseMusicTagProvider netEaseProvider = new NetEaseMusicTagProvider(cancellationSource);
					netEaseProvider.StatusReporter = statusReporter;
					searchProvider = netEaseProvider;
					if (useLinkedNetEaseId)
					{
						results.AddRange(netEaseProvider.SearchTracks("", 0, searchContext.LinkedMusicMetadata.musicId, 0, searchPass, existingResults, results));
						break;
					}
					results.AddRange(netEaseProvider.SearchTracks((searchContext.Title + " " + searchContext.Artist).Trim(), 15, 0L, 0, searchPass, existingResults, results));
					if (cancellationSource.IsCancellationRequested)
					{
						break;
					}
					if (!string.IsNullOrWhiteSpace(searchContext.Artist))
					{
						results.AddRange(netEaseProvider.SearchTracks(searchContext.Title.Trim(), 10, 0L, 1, searchPass, existingResults, results));
					}
					if (cancellationSource.IsCancellationRequested)
					{
						break;
					}
					if (!string.IsNullOrWhiteSpace(searchContext.Album) && searchContext.Album != searchContext.Title)
					{
						results.AddRange(netEaseProvider.SearchTracks((searchContext.Album + " " + searchContext.Artist).Trim(), 8, 0L, 2, searchPass, existingResults, results));
					}
					break;
				}
			case SearchSource.QQ:
				{
					using QqMusicTagProvider qqProvider = new QqMusicTagProvider(cancellationSource);
					qqProvider.StatusReporter = statusReporter;
					searchProvider = qqProvider;
					results.AddRange(qqProvider.SearchTracks((searchContext.Title + " " + searchContext.Artist).Trim(), 15, 0, searchPass, existingResults, results));
					if (cancellationSource.IsCancellationRequested)
					{
						break;
					}
					if (!string.IsNullOrWhiteSpace(searchContext.Artist))
					{
						results.AddRange(qqProvider.SearchTracks(searchContext.Title.Trim(), 10, 1, searchPass, existingResults, results));
					}
					if (cancellationSource.IsCancellationRequested)
					{
						break;
					}
					if (!string.IsNullOrWhiteSpace(searchContext.Album) && searchContext.Album != searchContext.Title)
					{
						results.AddRange(qqProvider.SearchTracks((searchContext.Album + " " + searchContext.Artist).Trim(), 8, 2, searchPass, existingResults, results));
					}
					break;
				}
			default:
				return results;
			case SearchSource.Kuwo:
				{
					using KuwoTagProvider kuwoTagProvider = new KuwoTagProvider(cancellationSource);
					kuwoTagProvider.StatusReporter = statusReporter;
					searchProvider = kuwoTagProvider;
					results.AddRange(kuwoTagProvider.SearchTracks((searchContext.Title + " " + searchContext.Artist).Trim(), 8, 0, searchPass, existingResults, results));
					if (cancellationSource.IsCancellationRequested)
					{
						break;
					}
					if (!results.Any() && !string.IsNullOrWhiteSpace(searchContext.Album) && searchContext.Album != searchContext.Title)
					{
						results.AddRange(kuwoTagProvider.SearchTracks((searchContext.Album + " " + searchContext.Artist).Trim(), 8, 1, searchPass, existingResults, results));
					}
					break;
				}
		}
		// 仅在非"网易 linkedId 中间子搜索"时上报本源最终结果:linkedId 是 pass A 的中间步骤,
		// 同源的常规搜索(pass B / 首选源非 linked)随后会给出真正的完成/出错状态,避免误报"已完成"。
		if (statusReporter != null && !useLinkedNetEaseId && !cancellationSource.IsCancellationRequested)
		{
			ReportSourceOutcome(statusReporter, source, results, searchProvider?.LastTransportResult);
		}
		return results;
	}

	// 依据本源结果数量与最近一次传输结果,判定本源是"完成"还是"出错"并上报。
	// 有结果即视为完成(即便末次子请求出错);仅当 0 结果且末次传输为错误时标记出错。
	private static void ReportSourceOutcome(Action<SourceSearchStatus> statusReporter, SearchSource source, List<TrackSearchResult> results, HttpResult lastTransportResult)
	{
		if (results.Count == 0 && lastTransportResult != null && !lastTransportResult.IsSuccess)
		{
			statusReporter(new SourceSearchStatus
			{
				Source = source,
				Phase = SourceSearchPhase.Error,
				ErrorCode = lastTransportResult.ErrorCode
			});
			return;
		}
		statusReporter(new SourceSearchStatus
		{
			Source = source,
			Phase = SourceSearchPhase.Completed
		});
	}

	private async void SearchCombinedTagsAsync()
	{
		TrackSearchCoordinator trackSearchCoordinator = new TrackSearchCoordinator();
		trackSearchCoordinator.Owner = this;
		cachedSearchResults = new List<TrackSearchResult>();
		trackSearchCoordinator.ProgressReporter = new Progress<List<TrackSearchResult>>(trackSearchCoordinator.OnSearchResultsReported);
		// 状态通道:Progress<T> 在 UI 线程构造,Report 自动编组回 UI 线程;
		// 后台搜索线程通过 searchStatusReporter 推送,UI 线程聚合渲染。
		Progress<SourceSearchStatus> statusProgress = new Progress<SourceSearchStatus>(OnSourceStatusReported);
		searchStatusReporter = (SourceSearchStatus status) => ((IProgress<SourceSearchStatus>)statusProgress).Report(status);
		BeginSearchStatusTracking();
		taskbarProgress.SetProgressState(TaskbarProgressBarStatus.Indeterminate);
		try
		{
			cachedSearchCompleted = await Task.Run((Func<bool>)trackSearchCoordinator.SearchAllSources, cancellationSource.Token);
		}
		catch (System.Exception ex) when (!(ex is OperationCanceledException && cancellationSource.IsCancellationRequested))
		{
			Console.WriteLine("SearchCombinedTags error:" + ex.GetMessageChain());
		}
		finally
		{
			if (!IsDisposed)
			{
				taskbarProgress.SetProgressState(TaskbarProgressBarStatus.NoProgress);
				EndSearchStatusTracking();
			}
		}
	}

	private void SortCurrentSearchResults(List<TrackSearchResult> results)
	{
		SortBySearchContextSimilarity(results, currentSearchContext);
	}

	// ===== 联网搜索状态标识(详见 docs/SEARCH_STATUS_INDICATOR_DESIGN.md) =====

	private void BeginSearchStatusTracking()
	{
		sourceSearchStatuses.Clear();
		searchInProgress = true;
		searchHasRun = true;
		retryCountdownTimer.Stop();
		RefreshSearchStatusDisplay();
	}

	private void EndSearchStatusTracking()
	{
		searchInProgress = false;
		retryCountdownTimer.Stop();
		// 搜索整体结束:任何仍处于"搜索中/重试中"的源都应视为已完成,避免某些边角路径
		// (如首选网易 linkedId 占满限额跳过常规搜索)导致"正在搜索"残留;错误状态保留(D3)。
		foreach (SourceSearchStatus status in sourceSearchStatuses.Values)
		{
			if (status.Phase != SourceSearchPhase.Error)
			{
				status.Phase = SourceSearchPhase.Completed;
			}
		}
		RefreshSearchStatusDisplay();
	}

	// 进入弹窗即重置:清空上一轮残留(出错行 / 重试倒计时 / 空态),且不视为"已搜索过"。
	private void ResetSearchStatusDisplay()
	{
		sourceSearchStatuses.Clear();
		searchInProgress = false;
		searchHasRun = false;
		retryCountdownTimer.Stop();
		RefreshSearchStatusDisplay();
	}

	// 后台搜索线程经 Progress<T> 编组到 UI 线程后回调。
	private void OnSourceStatusReported(SourceSearchStatus status)
	{
		if (IsDisposed || status == null)
		{
			return;
		}
		sourceSearchStatuses[status.Source] = status;
		if (status.Phase == SourceSearchPhase.Retrying && !retryCountdownTimer.Enabled)
		{
			retryCountdownTimer.Start();
		}
		RefreshSearchStatusDisplay();
	}

	private void RetryCountdownTimerTick(object sender, EventArgs e)
	{
		if (IsDisposed)
		{
			retryCountdownTimer.Stop();
			return;
		}
		bool anyRetrying = false;
		foreach (SourceSearchStatus status in sourceSearchStatuses.Values)
		{
			if (status.Phase == SourceSearchPhase.Retrying)
			{
				anyRetrying = true;
				if (status.RetrySecondsLeft > 0)
				{
					status.RetrySecondsLeft--;
				}
			}
		}
		if (!anyRetrying)
		{
			retryCountdownTimer.Stop();
		}
		RefreshSearchStatusDisplay();
	}

	private void RefreshSearchStatusDisplay()
	{
		if (IsDisposed || searchStatusLabel == null)
		{
			return;
		}
		string searchingLine = BuildSearchingLine();
		string errorLine = BuildErrorOrRetryLine();
		string text;
		if (searchingLine != null && errorLine != null)
		{
			text = searchingLine + "\n" + errorLine;
		}
		else if (searchingLine != null)
		{
			text = searchingLine;
		}
		else if (errorLine != null)
		{
			text = errorLine;
		}
		else if (!searchInProgress && searchHasRun && searchResultsListView.Items.Count == 0)
		{
			text = "未找到匹配结果";
		}
		else
		{
			text = "";
		}
		searchStatusLabel.Text = text;
		searchStatusLabel.Visible = text.Length > 0;
	}

	// 仍在搜索(尚未完成)的源:出错 / 重试中的源不出现在此行。
	private string BuildSearchingLine()
	{
		List<string> sourceNames = new List<string>();
		foreach (SourceSearchStatus status in GetStatusesInDisplayOrder())
		{
			if (status.Phase == SourceSearchPhase.Searching || status.Phase == SourceSearchPhase.Pending)
			{
				sourceNames.Add(GetSourceDisplayName(status.Source));
			}
		}
		if (sourceNames.Count == 0)
		{
			return null;
		}
		return "正在搜索: " + string.Join("/", sourceNames);
	}

	// 错误 / 重试行:重试中优先;多个普通错误时合并源名(D2:最多一行)。
	private string BuildErrorOrRetryLine()
	{
		foreach (SourceSearchStatus status in GetStatusesInDisplayOrder())
		{
			if (status.Phase == SourceSearchPhase.Retrying)
			{
				return GetSourceDisplayName(status.Source) + " API错误(" + FormatErrorCode(status.ErrorCode) + "), " + Math.Max(0, status.RetrySecondsLeft) + " 秒后重试 (" + status.RetryAttempt + "/" + status.RetryTotal + ")";
			}
		}
		List<SourceSearchStatus> erroredSources = new List<SourceSearchStatus>();
		foreach (SourceSearchStatus status in GetStatusesInDisplayOrder())
		{
			if (status.Phase == SourceSearchPhase.Error)
			{
				erroredSources.Add(status);
			}
		}
		if (erroredSources.Count == 0)
		{
			return null;
		}
		if (erroredSources.Count == 1)
		{
			return GetSourceDisplayName(erroredSources[0].Source) + " API错误(" + FormatErrorCode(erroredSources[0].ErrorCode) + ")";
		}
		List<string> erroredNames = new List<string>();
		foreach (SourceSearchStatus status in erroredSources)
		{
			erroredNames.Add(GetSourceDisplayName(status.Source));
		}
		return string.Join("/", erroredNames) + " API错误";
	}

	// 按固定显示顺序(网易云/QQ/酷狗/酷我)枚举已上报状态,保证渲染稳定。
	private IEnumerable<SourceSearchStatus> GetStatusesInDisplayOrder()
	{
		SearchSource[] displayOrder = new SearchSource[4] { SearchSource.Music163, SearchSource.QQ, SearchSource.Kugou, SearchSource.Kuwo };
		foreach (SearchSource source in displayOrder)
		{
			if (sourceSearchStatuses.TryGetValue(source, out SourceSearchStatus status))
			{
				yield return status;
			}
		}
	}

	private static string FormatErrorCode(string errorCode)
	{
		return string.IsNullOrEmpty(errorCode) ? "未知" : errorCode;
	}

	private static string GetSourceDisplayName(SearchSource source)
	{
		switch (source)
		{
			case SearchSource.Music163:
				return "网易云";
			case SearchSource.QQ:
				return "QQ";
			case SearchSource.Kugou:
				return "酷狗";
			case SearchSource.Kuwo:
				return "酷我";
			default:
				return source.ToString();
		}
	}

	public static void SortBySearchContextSimilarity(List<TrackSearchResult> results, TrackSearchContext searchContext)
	{
		foreach (TrackSearchResult result in results)
		{
			result.UpdateSimilarityScores(searchContext.Title, searchContext.Artist, searchContext.Album);
		}
		TrackSearchResult.SortBySimilarity(results);
	}

	public static void PromoteBestSearchMatch(List<TrackSearchResult> results, TrackSearchContext searchContext)
	{
		TrackSearchResult.PromoteBestMatch(searchContext.Title, searchContext.Artist, searchContext.Album, results);
	}

	private void RankCurrentSearchResults(List<TrackSearchResult> results)
	{
		RankSearchResults(results, currentSearchContext);
	}

	public static void RankSearchResults(List<TrackSearchResult> results, TrackSearchContext searchContext)
	{
		if (!results.Any())
		{
			return;
		}
		string normalizedTitle = DatabaseMapper.CoalesceNonBlank(searchContext.Title).Trim();
		if (searchContext.UsedFileNameForTitle && normalizedTitle.Contains(" - "))
		{
			List<TrackSearchResult> defaultRankedResults = new List<TrackSearchResult>(results);
			SortBySearchContextSimilarity(defaultRankedResults, searchContext);
			PromoteBestSearchMatch(defaultRankedResults, searchContext);
			if (defaultRankedResults[0].Title.Trim().Contains("-"))
			{
				List<TrackSearchResult> filenameFallbackResults = new List<TrackSearchResult>(results);
				string[] filenameParts = normalizedTitle.Split(new string[1] { " - " }, 2, StringSplitOptions.None);
				if (filenameParts.Length >= 2 && !string.IsNullOrWhiteSpace(filenameParts[0]) && !string.IsNullOrWhiteSpace(filenameParts[1]))
				{
					string filenameArtist = filenameParts[0].Trim();
					string filenameTitle = filenameParts[1].Trim();
					foreach (TrackSearchResult result in filenameFallbackResults)
					{
						result.UpdateSimilarityScores(filenameTitle, filenameArtist, "");
					}
					TrackSearchResult.SortBySimilarity(filenameFallbackResults);
					TrackSearchResult.PromoteBestMatch(filenameTitle, filenameArtist, "", filenameFallbackResults);
					TrackSearchResult bestFilenameFallbackResult = filenameFallbackResults[0];
					results.Clear();
					if ((TrackSearchResult.ContainsEitherWay(filenameTitle.ToLower(), bestFilenameFallbackResult.Title.ToLower()) && (double)bestFilenameFallbackResult.TitleSimilarityScore >= 0.5 && TrackSearchResult.ContainsEitherWay(filenameArtist.ToLower(), bestFilenameFallbackResult.Artist.ToLower()) && (double)bestFilenameFallbackResult.ArtistSimilarityScore >= 0.5) || ((double)bestFilenameFallbackResult.TitleSimilarityScore >= 0.8 && (double)bestFilenameFallbackResult.ArtistSimilarityScore >= 0.8))
					{
						results.AddRange(filenameFallbackResults);
					}
					else
					{
						results.AddRange(defaultRankedResults);
					}
				}
				else
				{
					results.Clear();
					results.AddRange(defaultRankedResults);
				}
			}
			else
			{
				results.Clear();
				results.AddRange(defaultRankedResults);
			}
		}
		else
		{
			SortBySearchContextSimilarity(results, searchContext);
			PromoteBestSearchMatch(results, searchContext);
		}
	}

	private void UpdateSelectionHighlight(ListViewItem item, bool hasFocus)
	{
		Control embeddedControl = (item.SubItems[sourceColumn.Index] as EmbeddedControlSubItem).EmbeddedControl;
		if (!item.Selected)
		{
			embeddedControl.BackColor = Color.Transparent;
			embeddedControl.ForeColor = SystemColors.WindowText;
			return;
		}
		embeddedControl.BackColor = hasFocus ? SystemColors.Highlight : SystemColors.Control;
		embeddedControl.ForeColor = hasFocus ? SystemColors.HighlightText : SystemColors.WindowText;
	}

	private void SearchResultSelectionChanged(object sender, ListViewItemSelectionChangedEventArgs args)
	{
		UpdateSelectionHighlight(args.Item, hasFocus: true);
	}

	private void SearchResultListLeave(object sender, EventArgs args)
	{
		searchResultsListView.SelectedItems.Cast<ListViewItem>().ForEachItem((ListViewItem item) => UpdateSelectionHighlight(item, hasFocus: false));
	}

	private void SearchResultListEnter(object sender, EventArgs args)
	{
		searchResultsListView.SelectedItems.Cast<ListViewItem>().ForEachItem((ListViewItem item) => UpdateSelectionHighlight(item, hasFocus: true));
	}

	private void DialogDeactivate(object sender, EventArgs args)
	{
		searchResultsListView.SelectedItems.Cast<ListViewItem>().ForEachItem((ListViewItem item) => UpdateSelectionHighlight(item, hasFocus: false));
	}

	private void DialogActivated(object sender, EventArgs args)
	{
		BeginInvoke(new Action(() => searchResultsListView.SelectedItems.Cast<ListViewItem>().ForEachItem((ListViewItem item) => UpdateSelectionHighlight(item, searchResultsListView.Focused))));
	}

	public static string FetchMissingNetEaseReleaseYear(TrackSearchResult searchResult, CancellationTokenSource cancellationSource)
	{
		if (searchResult.SearchSource == SearchSource.Music163 && string.IsNullOrWhiteSpace(searchResult.Year) && !string.IsNullOrWhiteSpace(searchResult.NetEaseAlbumId))
		{
			using (NetEaseMusicTagProvider netEaseProvider = new NetEaseMusicTagProvider(cancellationSource))
			{
				return netEaseProvider.GetAlbumReleaseYear(Convert.ToInt64(searchResult.NetEaseAlbumId));
			}
		}
		return null;
	}

	private void OkButtonClick(object sender, EventArgs args)
	{
		if (searchResultsListView.SelectedItems.Count > 0)
		{
			base.DialogResult = DialogResult.OK;
			Close();
			return;
		}
		DatabaseMapper.ShowErrorMessage(Resources.Msg_PleaseSelectItem);
	}

	private void CancelButtonClick(object sender, EventArgs args)
	{
		base.DialogResult = DialogResult.Cancel;
		Close();
	}

	private void SearchResultListDoubleClick(object sender, EventArgs args)
	{
		if (searchResultsListView.FocusedItem != null && searchResultsListView.SelectedItems.Count > 0)
		{
			okSplitButton.PerformClick();
		}
	}

	private void OverwriteOptionsMenuClick(object sender, EventArgs args)
	{
		new CombinedTagOverwriteOptionsDialog().ShowDialog();
	}

	private void SearchResultListMouseUp(object sender, MouseEventArgs args)
	{
		if (args.Button == MouseButtons.Right)
		{
			ShowSelectedCoverContextMenu();
		}
	}

	private void ShowSelectedCoverContextMenu()
	{
		if (searchResultsListView.SelectedItems.Count <= 0)
		{
			return;
		}
		openCoverMenuItem.Text = Resources.OpenCover;
		extractCoverMenuItem.Text = Resources.ExtractCover;
		CoverImageListViewItem selectedItem = searchResultsListView.SelectedItems[0] as CoverImageListViewItem;
		openCoverMenuItem.Enabled = !string.IsNullOrWhiteSpace(selectedItem.AssociatedValue) && selectedItem.AssociatedValue != "image_not_found" && selectedItem.CoverImage != null && selectedItem.CoverImage.Tag != null && !string.IsNullOrWhiteSpace(selectedItem.CoverImage.Tag as string);
		coverContextMenu.Show(searchResultsListView, PointToClient(Control.MousePosition));
	}

	private void WithSelectedCoverImageData(Action<ConfigDescriptorState.PictureData> imageAction)
	{
		if (searchResultsListView.SelectedItems.Count <= 0)
		{
			return;
		}
		CoverImageListViewItem selectedItem = searchResultsListView.SelectedItems[0] as CoverImageListViewItem;
		if (string.IsNullOrWhiteSpace(selectedItem.AssociatedValue) || selectedItem.AssociatedValue == "image_not_found" || selectedItem.CoverImage == null || selectedItem.CoverImage.Tag == null || string.IsNullOrWhiteSpace(selectedItem.CoverImage.Tag as string))
		{
			return;
		}
		ConfigDescriptorState.PictureData imageData = new ConfigDescriptorState.PictureData
		{
			ImageBytes = File.ReadAllBytes(selectedItem.AssociatedValue)
		};
		using (ConfigDescriptorState.LoadPictureImage(imageData))
		{
			if (imageData.MimeType != null && imageData.Width > 0 && imageData.Height > 0)
			{
				imageAction(imageData);
			}
		}
	}

	private void OpenCoverMenuClick(object sender, EventArgs args)
	{
		WithSelectedCoverImageData(OpenCoverImage);
	}

	private static void OpenCoverImage(ConfigDescriptorState.PictureData pictureData)
	{
		string extension = DatabaseMapper.GetImageExtensionForMimeType(pictureData.MimeType, "");
		string tempCoverPath = DatabaseMapper.GetPictureCacheDirectory() + "tempcover" + extension;
		File.WriteAllBytes(tempCoverPath, pictureData.ImageBytes);
		Process.Start(tempCoverPath);
	}

	private void ExtractCoverMenuClick(object sender, EventArgs args)
	{
		WithSelectedCoverImageData(ExtractCoverImage);
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			if (coverImageCache != null)
			{
				// 释放缓存的缩放/占位封面位图副本,避免反复搜索累积 GDI 句柄泄漏。
				foreach (Image cachedCover in coverImageCache.Values)
				{
					cachedCover?.Dispose();
				}
				coverImageCache.Clear();
			}
			components?.Dispose();
		}
		base.Dispose(disposing);
	}

	private void InitializeComponent()
	{
		components = new System.ComponentModel.Container();
		mainPanel = new FlowLayoutPanel();
		searchResultsListView = new MusicTagWinApp.Roles.EditableListView();
		coverColumn = new ColumnHeader();
		sourceColumn = new ColumnHeader();
		titleColumn = new ColumnHeader();
		artistColumn = new ColumnHeader();
		albumColumn = new ColumnHeader();
		commentColumn = new ColumnHeader();
		coverImageList = new ImageList(components);
		footerPanel = new Panel();
		buttonPanel = new FlowLayoutPanel();
		okSplitButton = new SplitButton();
		okButtonMenu = new ContextMenuStrip(components);
		overwriteOptionsMenuItem = new ToolStripMenuItem();
		cancelButton = new Button();
		cachedResultsTimer = new System.Windows.Forms.Timer(components);
		searchStatusLabel = new Label();
		retryCountdownTimer = new System.Windows.Forms.Timer(components);
		coverContextMenu = new ContextMenuStrip(components);
		openCoverMenuItem = new ToolStripMenuItem();
		extractCoverMenuItem = new ToolStripMenuItem();
		saveCoverDialog = new SaveFileDialog();
		mainPanel.SuspendLayout();
		footerPanel.SuspendLayout();
		buttonPanel.SuspendLayout();
		okButtonMenu.SuspendLayout();
		coverContextMenu.SuspendLayout();
		SuspendLayout();
		mainPanel.Controls.Add(searchResultsListView);
		mainPanel.Controls.Add(footerPanel);
		mainPanel.Dock = DockStyle.Fill;
		mainPanel.FlowDirection = FlowDirection.TopDown;
		mainPanel.Location = new Point(0, 0);
		mainPanel.Margin = new Padding(0);
		mainPanel.Name = "flowLayoutPanel1";
		mainPanel.Size = new Size(844, 661);
		mainPanel.TabIndex = 1;
		mainPanel.WrapContents = false;
		mainPanel.SizeChanged += SearchPanelSizeChanged;
		searchResultsListView.Columns.AddRange(new ColumnHeader[6] { coverColumn, sourceColumn, titleColumn, artistColumn, albumColumn, commentColumn });
		searchResultsListView.EmbeddedControlInset = 4;
		searchResultsListView.FullRowSelect = true;
		searchResultsListView.HeaderStyle = ColumnHeaderStyle.Nonclickable;
		searchResultsListView.HideSelection = false;
		searchResultsListView.Location = new Point(0, 0);
		searchResultsListView.Margin = new Padding(0);
		searchResultsListView.MultiSelect = false;
		searchResultsListView.Name = "listView1";
		searchResultsListView.OwnerDraw = true;
		searchResultsListView.Size = new Size(534, 431);
		searchResultsListView.SmallImageList = coverImageList;
		searchResultsListView.TabIndex = 9;
		searchResultsListView.UseCompatibleStateImageBehavior = false;
		searchResultsListView.View = View.Details;
		searchResultsListView.ItemSelectionChanged += SearchResultSelectionChanged;
		searchResultsListView.DoubleClick += SearchResultListDoubleClick;
		searchResultsListView.Enter += SearchResultListEnter;
		searchResultsListView.Leave += SearchResultListLeave;
		searchResultsListView.MouseUp += SearchResultListMouseUp;
		coverColumn.Text = "";
		coverColumn.Width = 130;
		sourceColumn.TextAlign = HorizontalAlignment.Center;
		sourceColumn.Width = 90;
		titleColumn.Text = "Title";
		titleColumn.Width = 150;
		artistColumn.Text = "Artist";
		artistColumn.Width = 150;
		albumColumn.Text = "Album";
		albumColumn.Width = 150;
		commentColumn.Text = "Comment";
		commentColumn.Width = 150;
		coverImageList.ColorDepth = ColorDepth.Depth32Bit;
		coverImageList.ImageSize = new Size(128, 128);
		coverImageList.TransparentColor = Color.Transparent;
		footerPanel.Controls.Add(searchStatusLabel);
		footerPanel.Controls.Add(buttonPanel);
		footerPanel.Dock = DockStyle.Fill;
		footerPanel.Location = new Point(0, 431);
		footerPanel.Margin = new Padding(0);
		footerPanel.Name = "flowLayoutPanel2";
		footerPanel.Size = new Size(534, 60);
		footerPanel.TabIndex = 8;
		buttonPanel.Controls.Add(okSplitButton);
		buttonPanel.Controls.Add(cancelButton);
		buttonPanel.Location = new Point(0, 12);
		buttonPanel.Margin = new Padding(0, 12, 0, 0);
		buttonPanel.Name = "flowLayoutPanel3";
		buttonPanel.Size = new Size(220, 35);
		buttonPanel.TabIndex = 6;
		okSplitButton.ContextMenuStrip = okButtonMenu;
		okSplitButton.Location = new Point(0, 0);
		okSplitButton.Margin = new Padding(0);
		okSplitButton.Name = "btnOK";
		okSplitButton.Size = new Size(51, 24);
		okSplitButton.SplitMenuStrip = okButtonMenu;
		okSplitButton.TabIndex = 5;
		okSplitButton.Text = "OK";
		okSplitButton.TextAlign = ContentAlignment.MiddleRight;
		okSplitButton.TextImageRelation = TextImageRelation.ImageBeforeText;
		okSplitButton.UseVisualStyleBackColor = true;
		okSplitButton.Click += OkButtonClick;
		okButtonMenu.Items.AddRange(new ToolStripItem[1] { overwriteOptionsMenuItem });
		okButtonMenu.Name = "searchContextMenuStrip";
		okButtonMenu.Size = new Size(181, 26);
		overwriteOptionsMenuItem.Name = "overwriteOptionsToolStripMenuItem";
		overwriteOptionsMenuItem.Size = new Size(180, 22);
		overwriteOptionsMenuItem.Text = "Overwrite options";
		overwriteOptionsMenuItem.Click += OverwriteOptionsMenuClick;
		cancelButton.Location = new Point(71, 0);
		cancelButton.Margin = new Padding(20, 0, 0, 0);
		cancelButton.Name = "btnCancel";
		cancelButton.Size = new Size(100, 35);
		cancelButton.TabIndex = 2;
		cancelButton.Text = "Cancel";
		cancelButton.UseVisualStyleBackColor = true;
		cancelButton.Click += CancelButtonClick;
		searchStatusLabel.AutoSize = false;
		searchStatusLabel.AutoEllipsis = true;
		searchStatusLabel.Margin = new Padding(0, 10, 0, 0);
		searchStatusLabel.Name = "searchStatusLabel";
		searchStatusLabel.Size = new Size(200, 40);
		searchStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
		searchStatusLabel.ForeColor = SystemColors.GrayText;
		searchStatusLabel.Visible = false;
		retryCountdownTimer.Interval = 1000;
		retryCountdownTimer.Tick += RetryCountdownTimerTick;
		coverContextMenu.Items.AddRange(new ToolStripItem[2] { openCoverMenuItem, extractCoverMenuItem });
		coverContextMenu.Name = "pictureBoxContextMenuStrip";
		coverContextMenu.Size = new Size(152, 48);
		openCoverMenuItem.Name = "openCoverToolStripMenuItem";
		openCoverMenuItem.Size = new Size(151, 22);
		openCoverMenuItem.Text = "Open Cover";
		openCoverMenuItem.Click += OpenCoverMenuClick;
		extractCoverMenuItem.Name = "extractCoverToolStripMenuItem";
		extractCoverMenuItem.Size = new Size(151, 22);
		extractCoverMenuItem.Text = "Extract cover";
		extractCoverMenuItem.Click += ExtractCoverMenuClick;
		saveCoverDialog.RestoreDirectory = true;
		base.AutoScaleDimensions = new SizeF(96f, 96f);
		base.AutoScaleMode = AutoScaleMode.Dpi;
		base.ClientSize = new Size(844, 661);
		base.Controls.Add(mainPanel);
		Font = new Font("Tahoma", 9f, FontStyle.Regular, GraphicsUnit.Point, 0);
		MinimumSize = new Size(300, 300);
		base.Name = "FormCombTagsSearch";
		base.ShowIcon = false;
		base.StartPosition = FormStartPosition.CenterParent;
		Text = "Search Tags from network";
		base.Activated += DialogActivated;
		base.Deactivate += DialogDeactivate;
		mainPanel.ResumeLayout(performLayout: false);
		footerPanel.ResumeLayout(performLayout: false);
		buttonPanel.ResumeLayout(performLayout: false);
		okButtonMenu.ResumeLayout(performLayout: false);
		coverContextMenu.ResumeLayout(performLayout: false);
		ResumeLayout(performLayout: false);
	}

	private void AddCachedResultsOnTimerTick(object sender, EventArgs args)
	{
		AddSearchResultsToList(cachedSearchResults);
		cachedResultsTimer.Stop();
	}

	private void FocusResultList()
	{
		searchResultsListView.Focus();
	}

	private void ExtractCoverImage(ConfigDescriptorState.PictureData imageData)
	{
		string filter = DatabaseMapper.GetImageFileDialogFilterForMimeType(imageData.MimeType);
		if (!string.IsNullOrWhiteSpace(filter))
		{
			saveCoverDialog.Filter = filter;
		}
		if (saveCoverDialog.ShowDialog() == DialogResult.OK)
		{
			File.WriteAllBytes(saveCoverDialog.FileName, imageData.ImageBytes);
		}
	}

}



