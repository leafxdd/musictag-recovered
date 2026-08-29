using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
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
			if (Owner.IsDisposed || Owner.cancellationSource.IsCancellationRequested)
			{
				return;
			}
			ListItem.Selected = true;
			if (Owner.IsHandleCreated)
			{
				Owner.BeginInvoke(new Action(Owner.FocusResultList));
			}
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
			CoverDownloadOutcome outcome = CoverDownloadCore.LoadOrDownloadCover(Request.CoverResult, Request.Owner.queuedCoverDownloadPaths, Request.Owner.cancellationSource, Request.Owner.coverImageList.ImageSize);
			if (!outcome.PathReserved)
			{
				return null;
			}
			OriginalImageSize = outcome.OriginalSize;
			if (outcome.Bitmap == null)
			{
				Console.WriteLine(string.Concat("bmp fail:", Request.CoverResult.CoverUrl, ",", outcome.CoverPath, ",", outcome.DownloadedBytes));
				if (outcome.Status != RemoteTagProviderBase.DownloadStatus.NotFound)
				{
					return Request.Owner.coverImageList.Images["download_failed"];
				}
				return Request.Owner.coverImageList.Images["image_not_found"];
			}
			return outcome.Bitmap;
		}
	}

	private sealed class TrackIdLookupSourceOption
	{
		public SearchSource Source { get; }

		public TrackIdLookupSourceOption(SearchSource source)
		{
			Source = source;
		}

		public override string ToString()
		{
			return Source.GetDisplayName();
		}
	}

	private sealed class TrackIdLookupOperationResult
	{
		public TrackSearchResult Track;

		public HttpResult TransportResult;
	}

	private sealed class TrackIdLookupRequest
	{
		public CancellationTokenSource Cancellation;

		public string TrackId;

		public SearchSource Source;

		public int SourceOrder;

		internal TrackIdLookupOperationResult Execute()
		{
			using ITrackIdLookupProvider provider = SearchProviderFactory.CreateTrackIdLookup(Source, Cancellation);
			if (provider == null)
			{
				return new TrackIdLookupOperationResult();
			}
			return new TrackIdLookupOperationResult
			{
				Track = provider.LookupTrackById(TrackId, SourceOrder),
				TransportResult = provider.LastTransportResult
			};
		}
	}

	private sealed class TrackSearchCoordinator
	{
		public CombinedTagSearchDialog Owner;

		public CancellationTokenSource Cancellation;

		public IProgress<List<TrackSearchResult>> ProgressReporter;

		public Predicate<SourceItem> preferredSourcePredicate;

		internal void OnSearchResultsReported(List<TrackSearchResult> searchResults)
		{
			if (!Cancellation.IsCancellationRequested)
			{
				Owner.AddSearchResultsToList(searchResults, cacheNewResults: true);
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
			searchLimits.RemainingGlobalResults = TextUtilities.GetWebSearchResultLimit();
			searchLimits.RemainingResultsBySource = new Dictionary<SearchSource, int>();
			searchLimits.CurrentBatch = null;
			SearchSource? preferredSource = Owner.preferredSource;
			if (preferredSource.HasValue)
			{
				SourceItem preferredSourceItem = TrackSearchResult.GetTagSourceSettings().Find(preferredSourcePredicate ?? (preferredSourcePredicate = IsPreferredSource));
				searchLimits.RemainingResultsBySource[preferredSource.Value] = preferredSourceItem.GetEffectiveSearchResultLimit();
				ReportSearching(preferredSource.Value);
				if (!Cancellation.IsCancellationRequested && searchLimits.RemainingGlobalResults > 0 && Owner.currentSearchContext.LinkedMusicMetadata.musicId > 0L && preferredSource.Value == SearchSource.Music163)
				{
					searchLimits.CurrentBatch = Owner.SearchCurrentContextTracks(preferredSource.Value, useLinkedNetEaseId: true, searchLimits.AccumulatedResults, 0, Cancellation);
					searchLimits.RankLimitAndReportCurrentBatch(useProviderRanking: true);
				}
				if (!Cancellation.IsCancellationRequested && searchLimits.RemainingGlobalResults > 0 && searchLimits.RemainingResultsBySource[preferredSource.Value] > 0)
				{
					searchLimits.CurrentBatch = Owner.SearchCurrentContextTracks(preferredSource.Value, useLinkedNetEaseId: false, searchLimits.AccumulatedResults, 0, Cancellation);
					searchLimits.RankLimitAndReportCurrentBatch(useProviderRanking: true);
				}
				return !Cancellation.IsCancellationRequested;
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
			if (!Cancellation.IsCancellationRequested && searchLimits.RemainingGlobalResults > 0 && Owner.currentSearchContext.LinkedMusicMetadata.musicId > 0L && (netEaseSource = tagSources.Find(searchLimits.IsPrimaryNetEaseSourceAvailable)) != null)
			{
				searchLimits.CurrentBatch = new List<TrackSearchResult>();
				searchLimits.CurrentBatch.AddRange(Owner.SearchCurrentContextTracks(netEaseSource.SearchSource, useLinkedNetEaseId: true, searchLimits.AccumulatedResults, searchPass++, Cancellation));
				searchLimits.RankLimitAndReportCurrentBatch(useProviderRanking: true);
			}
			if (!Cancellation.IsCancellationRequested && searchLimits.RemainingGlobalResults > 0)
			{
				List<SourceItem> primarySources = tagSources.FindAll((SourceItem source) => source.Enabled && !source.IsSecondarySource && searchLimits.RemainingResultsBySource[source.SearchSource] > 0);
				searchLimits.CurrentBatch = SearchSourcesInParallel(primarySources, searchLimits.AccumulatedResults, useLinkedNetEaseId: false, searchPass);
				searchPass += primarySources.Count;
				searchLimits.RankLimitAndReportCurrentBatch(useProviderRanking: true);
			}
			if (!Cancellation.IsCancellationRequested && searchLimits.RemainingGlobalResults > 0)
			{
				List<SourceItem> secondarySources = tagSources.FindAll((SourceItem source) => source.Enabled && source.IsSecondarySource && searchLimits.RemainingResultsBySource[source.SearchSource] > 0);
				searchLimits.CurrentBatch = SearchSourcesInParallel(secondarySources, searchLimits.AccumulatedResults, useLinkedNetEaseId: false, searchPass);
				searchPass += secondarySources.Count;
				searchLimits.RankLimitAndReportCurrentBatch(useProviderRanking: false);
			}
			return !Cancellation.IsCancellationRequested;
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

		// 某源 Task 因未预期异常 faulted(provider 抛出且未被内部 catch)时上报"出错"。
		// 否则该源会停留在"搜索中",被收尾的 EndSearchStatusTracking 误置为"已完成"。
		private void ReportError(SearchSource source)
		{
			Owner.searchStatusReporter?.Invoke(new SourceSearchStatus
			{
				Source = source,
				Phase = SourceSearchPhase.Error
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
			CancellationToken cancellationToken = Cancellation.Token;
			Task<List<TrackSearchResult>>[] sourceTasks = new Task<List<TrackSearchResult>>[sources.Count];
			for (int taskIndex = 0; taskIndex < sources.Count; taskIndex++)
			{
				SearchSource source = sources[taskIndex].SearchSource;
				int searchPassForSource = baseSearchPass + taskIndex;
				sourceTasks[taskIndex] = Task.Run(() => Owner.SearchCurrentContextTracks(source, useLinkedNetEaseId, existingResults, searchPassForSource, Cancellation), cancellationToken);
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
			// faulted(provider 抛出未预期异常)的源单独上报出错,避免被误判为"已完成/无结果"。
			for (int taskIndex = 0; taskIndex < sourceTasks.Length; taskIndex++)
			{
				Task<List<TrackSearchResult>> sourceTask = sourceTasks[taskIndex];
				if (sourceTask.Status == TaskStatus.RanToCompletion && sourceTask.Result != null)
				{
					combinedResults.AddRange(sourceTask.Result);
				}
				else if (sourceTask.IsFaulted && !cancellationToken.IsCancellationRequested)
				{
					// 保留 provider 未预期异常的定位信息(UI 仍只显示"API错误(未知)")。
					Console.WriteLine("SearchSourceInParallel error:" + sourceTask.Exception?.GetBaseException().GetMessageChain());
					ReportError(sources[taskIndex].SearchSource);
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
			if (useProviderRanking)
			{
				Coordinator.Owner.RankCurrentSearchResults(CurrentBatch);
			}
			else
			{
				Coordinator.Owner.SortCurrentSearchResults(CurrentBatch);
			}
			List<TrackSearchResult> limitedResults = SelectResultsWithinSourceCaps(CurrentBatch, RemainingResultsBySource, ref RemainingGlobalResults);
			AccumulatedResults.AddRange(limitedResults);
			Coordinator.ProgressReporter.Report(limitedResults);
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

	// 按全局上限 + 每源上限过滤本批结果(原 TrackResultLimitCollector.AddIfWithinLimit 的纯计数器数学，
	// 提取为可测 static)：逐个结果，若全局余额 <= 0 或该源余额 <= 0 则跳过，否则收录并同时递减两个计数器。
	// remainingGlobalResults 以 ref 回写(原经 collector.SearchLimits 更新同一字段);remainingResultsBySource
	// 原地递减(同一 dict 引用)。全局余额的 || 短路保留 —— 全局耗尽时不索引 dict(保留原 KeyNotFound 边界不变)。
	internal static List<TrackSearchResult> SelectResultsWithinSourceCaps(List<TrackSearchResult> batch, Dictionary<SearchSource, int> remainingResultsBySource, ref int remainingGlobalResults)
	{
		List<TrackSearchResult> limitedResults = new List<TrackSearchResult>();
		foreach (TrackSearchResult trackResult in batch)
		{
			if (remainingGlobalResults <= 0 || remainingResultsBySource[trackResult.SearchSource] <= 0)
			{
				continue;
			}
			limitedResults.Add(trackResult);
			remainingResultsBySource[trackResult.SearchSource]--;
			remainingGlobalResults--;
		}
		return limitedResults;
	}

	private int activeMediaDownloadCount;

	private static bool cachedSearchCompleted;

	private static TrackSearchContext lastSearchContext;

	private static SearchSource? lastPreferredSource;

	private TrackSearchContext currentSearchContext;

	private SearchSource? preferredSource;

	// 联网搜索状态标识相关(均仅在 UI 线程访问)。searchStatusReporter 由后台搜索线程调用,
	// 内部经 Progress<T> 编组回 UI 线程;状态聚合 / 渲染 / QQ 重试倒计时统一交给共享渲染器
	// SearchStatusIndicator(与封面 / 歌词弹窗一致),详见 docs/SEARCH_STATUS_INDICATOR_DESIGN.md。
	private Action<SourceSearchStatus> searchStatusReporter;

	private SearchStatusIndicator searchStatusIndicator;

	private readonly CancellationTokenSource cancellationSource;

	private CancellationTokenSource automaticSearchCancellationSource;

	private static List<TrackSearchResult> cachedSearchResults;

	private readonly HashSet<string> queuedCoverDownloadPaths;

	private readonly Dictionary<string, Image> coverImageCache;

	private int columnWidthsDpi = 96;

	private readonly TaskbarProgressController taskbarProgress;

	private bool trackIdLookupInProgress;

	private IContainer components;

	private FlowLayoutPanel mainPanel;

	private TableLayoutPanel trackIdLookupPanel;

	private Label trackIdLabel;

	private ComboBox trackIdSourceComboBox;

	private TextBox trackIdTextBox;

	private Button trackIdLookupButton;

	private Label trackIdLookupStatusLabel;

	private ToolTip trackIdLookupToolTip;

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

	public void SetSearchContext(TrackSearchContext searchContext)
	{
		currentSearchContext = searchContext;
	}

	public void SetPreferredSource(SearchSource? source)
	{
		preferredSource = source;
		if (source.HasValue && trackIdSourceComboBox != null)
		{
			for (int index = 0; index < trackIdSourceComboBox.Items.Count; index++)
			{
				if (trackIdSourceComboBox.Items[index] is TrackIdLookupSourceOption option && option.Source == source.Value)
				{
					trackIdSourceComboBox.SelectedIndex = index;
					break;
				}
			}
		}
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
		InitializeTrackIdLookup();
		searchStatusIndicator = new SearchStatusIndicator(searchStatusLabel, () => searchResultsListView.Items.Count > 0, components);
		ApplyLocalizedText();
		UpdateSearchDialogLayout();
	}

	private void InitializeResultListImagesAndScaling()
	{
		ApplyDpiMetrics(DeviceDpi);
	}

	private void RebuildCoverImagesForDpi(int dpi)
	{
		Size targetSize = new Size(Math.Min(256, ImageUtilities.ScaleLogicalPixels(128f, dpi)), Math.Min(256, ImageUtilities.ScaleLogicalPixels(128f, dpi)));
		if (coverImageList.Images.Count > 0 && coverImageList.ImageSize == targetSize)
		{
			return;
		}

		coverImageList.Images.Clear();
		coverImageList.ImageSize = targetSize;
		coverImageList.ColorDepth = ColorDepth.Depth32Bit;
		coverImageList.TransparentColor = Color.Transparent;
		using (Bitmap failed = ImageUtilities.LoadResourceBitmap("download_failed", targetSize))
		using (Bitmap missing = ImageUtilities.LoadResourceBitmap("imagenotfound", targetSize))
		using (Bitmap loading = ImageUtilities.LoadResourceBitmap("downloading", targetSize))
		{
			coverImageList.Images.Add("download_failed", failed);
			coverImageList.Images.Add("image_not_found", missing);
			coverImageList.Images.Add("loading", loading);
			_ = coverImageList.Handle;
		}

		foreach (string coverPath in coverImageCache.Keys.ToList())
		{
			Image previousImage = coverImageCache[coverPath];
			Image replacement = CoverDownloadCore.LoadCachedCoverThumbnail(coverPath, targetSize);
			if (replacement == null)
			{
				continue;
			}
			replacement.Tag = previousImage.Tag;
			coverImageCache[coverPath] = replacement;
			previousImage.Dispose();
		}

		foreach (CoverImageListViewItem item in searchResultsListView.Items)
		{
			string imageKey = item.AssociatedValue as string;
			if (!string.IsNullOrWhiteSpace(imageKey) && coverImageCache.TryGetValue(imageKey, out Image cachedCover))
			{
				item.CoverImage = cachedCover;
			}
			else if (imageKey == "download_failed" || imageKey == "image_not_found")
			{
				item.CoverImage = coverImageList.Images[imageKey];
			}
			else
			{
				item.CoverImage = coverImageList.Images["loading"];
			}
		}
		searchResultsListView.Invalidate();
	}

	private void ApplyDpiMetrics(int dpi)
	{
		dpi = Math.Max(dpi, 96);
		RebuildCoverImagesForDpi(dpi);
		if (columnWidthsDpi != dpi)
		{
			foreach (ColumnHeader column in searchResultsListView.Columns)
			{
				column.Width = Math.Max(1, (int)Math.Round(column.Width * (double)dpi / columnWidthsDpi));
			}
			columnWidthsDpi = dpi;
		}

		okSplitButton.AutoSize = false;
		FontAwesome.Properties fontProperties = new FontAwesome.Properties
		{
			Size = ImageUtilities.ScaleLogicalPixels(24f, dpi),
			ShowBorder = false
		};
		okSplitButton.Size = new Size(ImageUtilities.ScaleLogicalPixels(100f, dpi), ImageUtilities.ScaleLogicalPixels(35f, dpi));
		Image oldOkImage = okSplitButton.Image;
		okSplitButton.Image = FontAwesome.Type.Check.AsImage(fontProperties);
		oldOkImage?.Dispose();
		trackIdLookupButton.MinimumSize = new Size(ImageUtilities.ScaleLogicalPixels(36f, dpi), ImageUtilities.ScaleLogicalPixels(28f, dpi));
		trackIdLookupButton.Size = trackIdLookupButton.MinimumSize;
		UpdateTrackIdLookupIconForDpi(dpi);
		UpdateSearchDialogLayout();
	}

	private void InitializeTrackIdLookup()
	{
		foreach (SearchSource source in new[] { SearchSource.Music163, SearchSource.QQ, SearchSource.Kuwo, SearchSource.Kugou })
		{
			trackIdSourceComboBox.Items.Add(new TrackIdLookupSourceOption(source));
		}
		trackIdSourceComboBox.SelectedIndex = 0;
		UpdateTrackIdLookupIconForDpi(DeviceDpi);
		UpdateTrackIdInputHint();
	}

	private void UpdateTrackIdLookupIconForDpi(int dpi)
	{
		if (trackIdLookupButton == null || trackIdLookupButton.IsDisposed)
		{
			return;
		}
		int iconSize = Math.Max(1, (int)Math.Round(18d * Math.Max(dpi, 96) / 96d));
		FontAwesome.Properties iconProperties = new FontAwesome.Properties
		{
			Size = iconSize,
			ShowBorder = false
		};
		Image oldImage = trackIdLookupButton.Image;
		trackIdLookupButton.Image = FontAwesome.Type.Search.AsImage(iconProperties);
		oldImage?.Dispose();
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
		trackIdLabel.Text = UiText.Get("Track ID", "歌曲 ID", "歌曲 ID");
		trackIdLookupToolTip.SetToolTip(trackIdLookupButton, UiText.Get("Look up track by ID", "按歌曲 ID 查询", "按歌曲 ID 查詢"));
		UpdateTrackIdInputHint();
	}

	protected override void OnHandleCreated(EventArgs e)
	{
		base.OnHandleCreated(e);
		ApplyDpiMetrics(DeviceDpi);
	}

	protected override void OnShown(EventArgs param)
	{
		base.OnShown(param);
		UpdateTrackIdLookupIconForDpi(DeviceDpi);
		TrackSearchContext searchContext = currentSearchContext;
		bool canReuseCachedResults = cachedSearchCompleted && cachedSearchResults != null && lastSearchContext != null && cachedSearchResults.Any() && lastPreferredSource == preferredSource && searchContext.Title == lastSearchContext.Title && searchContext.Artist == lastSearchContext.Artist && searchContext.Album == lastSearchContext.Album;
		if (canReuseCachedResults)
		{
			// 缓存复用:不联网搜索,缓存结果非空,状态标识保持隐藏(无空态)。
			searchStatusIndicator.Reset();
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

	protected override void OnDpiChanged(DpiChangedEventArgs e)
	{
		base.OnDpiChanged(e);
		ApplyDpiMetrics(e.DeviceDpiNew);
		if (IsHandleCreated && !IsDisposed)
		{
			BeginInvoke(new Action(() => ApplyDpiMetrics(DeviceDpi)));
		}
	}

	protected override void OnClosed(EventArgs spec)
	{
		cachedResultsTimer.Stop();
		searchStatusIndicator.StopCountdown();
		automaticSearchCancellationSource?.Cancel();
		cancellationSource.Cancel();
		base.OnClosed(spec);
		string selectedCoverPath = null;
		if (base.DialogResult == DialogResult.OK)
		{
			selectedCoverPath = GetSelectedTrackResult().Cover?.LocalCoverPath;
		}
		PathFileUtilities.TrimDirectorySize(PathFileUtilities.GetPictureCacheDirectory(), selectedCoverPath, 31457280L, 62914560L);
	}

	private SearchSource GetSelectedTrackIdSource()
	{
		return (trackIdSourceComboBox.SelectedItem as TrackIdLookupSourceOption)?.Source ?? SearchSource.Music163;
	}

	private void TrackIdSourceSelectedIndexChanged(object sender, EventArgs args)
	{
		UpdateTrackIdInputHint();
		trackIdLookupStatusLabel.Visible = false;
	}

	private void UpdateTrackIdInputHint()
	{
		if (trackIdTextBox == null || trackIdSourceComboBox == null)
		{
			return;
		}
		string hint = GetTrackIdInputHint(GetSelectedTrackIdSource(), CultureInfo.CurrentUICulture);
		trackIdTextBox.PlaceholderText = hint;
		trackIdLookupToolTip?.SetToolTip(trackIdTextBox, hint);
	}

	internal static string GetTrackIdInputHint(SearchSource source, CultureInfo culture)
	{
		switch (source)
		{
		case SearchSource.QQ:
			return UiText.Get("songmid, songid, or song link", "songmid、songid 或歌曲链接", "songmid、songid 或歌曲連結", culture);
		case SearchSource.Kuwo:
			return UiText.Get("musicId or song link", "musicId 或歌曲链接", "musicId 或歌曲連結", culture);
		case SearchSource.Kugou:
			return UiText.Get("MixSongID, hash, or song link", "MixSongID、hash 或歌曲链接", "MixSongID、hash 或歌曲連結", culture);
		default:
			return UiText.Get("Numeric ID or song link", "数字 ID 或歌曲链接", "數字 ID 或歌曲連結", culture);
		}
	}

	private void TrackIdTextBoxKeyDown(object sender, KeyEventArgs args)
	{
		if (args.KeyCode != Keys.Enter)
		{
			return;
		}
		args.SuppressKeyPress = true;
		args.Handled = true;
		LookupTrackByIdAsync();
	}

	private void TrackIdLookupButtonClick(object sender, EventArgs args)
	{
		LookupTrackByIdAsync();
	}

	private async void LookupTrackByIdAsync()
	{
		if (trackIdLookupInProgress || cancellationSource.IsCancellationRequested)
		{
			return;
		}
		CancelAutomaticSearchForManualLookup(automaticSearchCancellationSource);
		cachedSearchCompleted = false;
		trackIdLookupInProgress = true;
		SetTrackIdLookupControlsEnabled(enabled: false);
		ShowTrackIdLookupStatus(UiText.Get("Looking up...", "正在查询...", "正在查詢..."), isError: false);
		try
		{
			SearchSource source = GetSelectedTrackIdSource();
			string input = trackIdTextBox.Text?.Trim();
			if (Uri.TryCreate(input, UriKind.Absolute, out _) && !TrackLinkClassifier.TryDetect(input, out source, out Uri detectedUri))
			{
				ShowTrackIdLookupStatus(UiText.Get("Unsupported music link", "不支持的音乐链接", "不支援的音樂連結"), isError: true);
				return;
			}
			if (TrackLinkClassifier.TryDetect(input, out source, out detectedUri))
			{
				SetPreferredSource(source);
				if (TrackLinkClassifier.IsQqShortLink(detectedUri))
				{
					using QqShortLinkResolver resolver = QqShortLinkResolver.CreateDefault();
					detectedUri = await resolver.ResolveAsync(detectedUri, cancellationSource.Token);
					if (detectedUri == null)
					{
						ShowTrackIdLookupStatus(UiText.Get("QQ short link could not be resolved", "QQ 短链接解析失败", "QQ 短連結解析失敗"), isError: true);
						return;
					}
					input = detectedUri.AbsoluteUri;
				}
			}
			if (!TrackIdInput.TryNormalize(source, input, out string normalizedId))
			{
				ShowTrackIdLookupStatus(UiText.Get("Invalid ID format", "歌曲 ID 格式不正确", "歌曲 ID 格式不正確"), isError: true);
				return;
			}
			TrackIdLookupRequest request = new TrackIdLookupRequest
			{
				Cancellation = cancellationSource,
				TrackId = normalizedId,
				Source = source,
				SourceOrder = trackIdSourceComboBox.SelectedIndex
			};
			TrackIdLookupOperationResult outcome = await Task.Run((Func<TrackIdLookupOperationResult>)request.Execute, cancellationSource.Token);
			if (IsDisposed || cancellationSource.IsCancellationRequested)
			{
				return;
			}
			if (outcome.Track == null)
			{
				ShowTrackIdLookupStatus(GetTrackIdLookupFailureText(outcome.TransportResult, CultureInfo.CurrentUICulture), isError: true);
				return;
			}

			ListViewItem existingItem = FindSearchResultItem(outcome.Track);
			if (existingItem != null)
			{
				existingItem.Selected = true;
				existingItem.EnsureVisible();
				ShowTrackIdLookupStatus(UiText.Get("Already in results", "结果中已存在该歌曲", "結果中已存在該歌曲"), isError: false);
				return;
			}

			AddSearchResultsToList(new List<TrackSearchResult> { outcome.Track }, cacheNewResults: true);
			ListViewItem addedItem = FindSearchResultItem(outcome.Track);
			if (addedItem != null)
			{
				addedItem.Selected = true;
				addedItem.EnsureVisible();
			}
			ShowTrackIdLookupStatus(UiText.Get("Track added", "已添加查询结果", "已加入查詢結果"), isError: false);
		}
		catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
		{
		}
		catch (Exception ex)
		{
			LogService.WriteExceptionDetails(ex, "CombinedTagSearchDialog.LookupTrackByIdAsync");
			if (!IsDisposed)
			{
				ShowTrackIdLookupStatus(UiText.Get("Lookup failed", "查询失败", "查詢失敗"), isError: true);
			}
		}
		finally
		{
			trackIdLookupInProgress = false;
			if (!IsDisposed)
			{
				SetTrackIdLookupControlsEnabled(enabled: true);
			}
		}
	}

	private void SetTrackIdLookupControlsEnabled(bool enabled)
	{
		trackIdSourceComboBox.Enabled = enabled;
		trackIdTextBox.Enabled = enabled;
		trackIdLookupButton.Enabled = enabled;
	}

	private void ShowTrackIdLookupStatus(string text, bool isError)
	{
		trackIdLookupStatusLabel.Text = text;
		trackIdLookupStatusLabel.ForeColor = isError ? Color.Firebrick : SystemColors.GrayText;
		trackIdLookupStatusLabel.Visible = true;
	}

	internal static string GetTrackIdLookupFailureText(HttpResult transportResult, CultureInfo culture)
	{
		if (transportResult == null || transportResult.IsSuccess)
		{
			return UiText.Get("Track not found", "未找到该歌曲", "未找到該歌曲", culture);
		}
		string message;
		switch (transportResult.Error)
		{
		case RemoteErrorKind.CredentialsExpired:
			message = UiText.Get("QQ Music Cookie expired, please update it", "QQ 音乐 Cookie 已过期，请重新复制", "QQ 音樂 Cookie 已過期，請重新複製", culture);
			break;
		case RemoteErrorKind.RateLimited:
			message = UiText.Get("Request rate limited", "请求被限流", "請求被限流", culture);
			break;
		case RemoteErrorKind.Timeout:
			message = UiText.Get("Request timed out", "请求超时", "請求逾時", culture);
			break;
		case RemoteErrorKind.Network:
			message = UiText.Get("Network error", "网络错误", "網路錯誤", culture);
			break;
		case RemoteErrorKind.HttpStatus:
			message = UiText.Get("HTTP error", "HTTP 错误", "HTTP 錯誤", culture);
			break;
		default:
			message = UiText.Get("Invalid provider response", "接口响应格式错误", "介面回應格式錯誤", culture);
			break;
		}
		return string.IsNullOrWhiteSpace(transportResult.ErrorCode) ? message : message + " (" + transportResult.ErrorCode + ")";
	}

	private void UpdateSearchDialogLayout()
	{
		trackIdLookupPanel.Width = mainPanel.Width;
		searchResultsListView.Width = mainPanel.Width;
		searchResultsListView.Height = Math.Max(0, mainPanel.Height - trackIdLookupPanel.Height - footerPanel.Height);
		SearchStatusIndicator.LayoutFooterStatus(footerPanel, buttonPanel, searchStatusLabel);
	}

	private void SearchPanelSizeChanged(object sender, EventArgs args)
	{
		UpdateSearchDialogLayout();
	}

	private ListViewItem FindSearchResultItem(TrackSearchResult searchResult)
	{
		return searchResultsListView.Items.Cast<ListViewItem>().FirstOrDefault(item => item.Tag is TrackSearchResult existingResult && existingResult.SearchSource == searchResult.SearchSource && string.Equals(existingResult.SourceTrackId, searchResult.SourceTrackId, StringComparison.OrdinalIgnoreCase));
	}

	private void AddSearchResultsToList(List<TrackSearchResult> results, bool cacheNewResults = false)
	{
		if (results == null || base.IsDisposed)
		{
			return;
		}
		searchResultsListView.BeginUpdate();
		foreach (TrackSearchResult searchResult in results)
		{
			if (FindSearchResultItem(searchResult) != null)
			{
				continue;
			}
			if (cacheNewResults)
			{
				cachedSearchResults ??= new List<TrackSearchResult>();
				cachedSearchResults.Add(searchResult);
			}
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
			if (IsDisposed || cancellationSource.IsCancellationRequested)
			{
				activeMediaDownloadCount--;
				return;
			}
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
		catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
		{
		}
		catch (System.Exception v)
		{
			LogService.WriteExceptionDetails(v, "CombinedTagSearchDialog.DownloadLyricAsync");
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
			if (IsDisposed || cancellationSource.IsCancellationRequested)
			{
				result?.Dispose();
				activeMediaDownloadCount--;
				return;
			}
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
		catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
		{
			activeMediaDownloadCount--;
		}
		catch (System.Exception v)
		{
			LogService.WriteExceptionDetails(v, "CombinedTagSearchDialog.DownloadCoverAsync");
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

	private List<TrackSearchResult> SearchCurrentContextTracks(SearchSource source, bool useLinkedNetEaseId, List<TrackSearchResult> existingResults, int searchPass, CancellationTokenSource searchCancellation)
	{
		return SearchTracksFromSource(source, useLinkedNetEaseId, existingResults, searchPass, currentSearchContext, searchCancellation, searchStatusReporter);
	}

	public static List<TrackSearchResult> SearchTracksFromSource(SearchSource source, bool useLinkedNetEaseId, List<TrackSearchResult> existingResults, int searchPass, TrackSearchContext searchContext, CancellationTokenSource cancellationSource, Action<SourceSearchStatus> statusReporter = null)
	{
		using ICombinedTrackSearch combinedTrackSearch = SearchProviderFactory.CreateCombinedTrackSearch(source, cancellationSource, statusReporter);
		if (combinedTrackSearch == null)
		{
			return new List<TrackSearchResult>();
		}
		List<TrackSearchResult> results = combinedTrackSearch.SearchTracks(useLinkedNetEaseId, existingResults, searchPass, searchContext);
		if (statusReporter != null && !useLinkedNetEaseId && !cancellationSource.IsCancellationRequested)
		{
			ReportSourceOutcome(statusReporter, source, results, combinedTrackSearch.LastTransportResult);
		}
		return results;
	}

	// 依据本源结果数量与最近一次传输结果,判定本源是"完成"还是"出错"并上报。
	// 有结果即视为完成(即便末次子请求出错);仅当 0 结果且末次传输为错误时标记出错。
	internal static void ReportSourceOutcome(Action<SourceSearchStatus> statusReporter, SearchSource source, List<TrackSearchResult> results, HttpResult lastTransportResult)
	{
		if (results.Count == 0 && lastTransportResult != null && !lastTransportResult.IsSuccess)
		{
			statusReporter(new SourceSearchStatus
			{
				Source = source,
				Phase = lastTransportResult.Error == RemoteErrorKind.CredentialsExpired ? SourceSearchPhase.CredentialsExpired : SourceSearchPhase.Error,
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
		CancellationTokenSource searchCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationSource.Token);
		automaticSearchCancellationSource = searchCancellation;
		TrackSearchCoordinator trackSearchCoordinator = new TrackSearchCoordinator();
		trackSearchCoordinator.Owner = this;
		trackSearchCoordinator.Cancellation = searchCancellation;
		cachedSearchResults = new List<TrackSearchResult>();
		trackSearchCoordinator.ProgressReporter = new Progress<List<TrackSearchResult>>(trackSearchCoordinator.OnSearchResultsReported);
		// 状态通道:Progress<T> 在 UI 线程构造,Report 自动编组回 UI 线程;
		// 后台搜索线程通过 searchStatusReporter 推送,UI 线程聚合渲染。
		searchStatusReporter = searchStatusIndicator.BeginReporting();
		taskbarProgress.SetProgressState(TaskbarProgressBarStatus.Indeterminate);
		try
		{
			cachedSearchCompleted = await Task.Run((Func<bool>)trackSearchCoordinator.SearchAllSources, searchCancellation.Token);
		}
		catch (System.Exception ex) when (!(ex is OperationCanceledException && searchCancellation.IsCancellationRequested))
		{
			LogService.WriteExceptionDetails(ex, "CombinedTagSearchDialog.SearchCombinedTagsAsync");
			searchStatusIndicator.ReportUnexpectedError();
		}
		finally
		{
			if (ReferenceEquals(automaticSearchCancellationSource, searchCancellation))
			{
				automaticSearchCancellationSource = null;
			}
			searchCancellation.Dispose();
			if (!IsDisposed)
			{
				taskbarProgress.SetProgressState(TaskbarProgressBarStatus.NoProgress);
				searchStatusIndicator.End();
			}
		}
	}

	internal static void CancelAutomaticSearchForManualLookup(CancellationTokenSource automaticSearchCancellation)
	{
		automaticSearchCancellation?.Cancel();
	}

	private void SortCurrentSearchResults(List<TrackSearchResult> results)
	{
		SortBySearchContextSimilarity(results, currentSearchContext);
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

	// 文件名 "艺术家 - 标题" 拆分后的回退排序结果是否足够可信而采用(否则改用默认排序结果)。
	// 两条接受通道,任一满足即采用:
	//   通道一(宽松双确认):标题与艺术家均(大小写不敏感)互含 target,且各自相似度均 >= 0.5;
	//   通道二(强分数):标题与艺术家相似度各 >= 0.8(无需互含)。
	internal static bool ShouldAcceptFilenameFallbackMatch(string filenameTitle, string filenameArtist, TrackSearchResult bestFilenameFallbackResult)
	{
		return (TrackSearchResult.ContainsEitherWay(filenameTitle.ToLower(), bestFilenameFallbackResult.Title.ToLower()) && (double)bestFilenameFallbackResult.TitleSimilarityScore >= 0.5 && TrackSearchResult.ContainsEitherWay(filenameArtist.ToLower(), bestFilenameFallbackResult.Artist.ToLower()) && (double)bestFilenameFallbackResult.ArtistSimilarityScore >= 0.5) || ((double)bestFilenameFallbackResult.TitleSimilarityScore >= 0.8 && (double)bestFilenameFallbackResult.ArtistSimilarityScore >= 0.8);
	}

	public static void RankSearchResults(List<TrackSearchResult> results, TrackSearchContext searchContext)
	{
		if (!results.Any())
		{
			return;
		}
		string normalizedTitle = TextUtilities.CoalesceNonBlank(searchContext.Title).Trim();
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
					if (ShouldAcceptFilenameFallbackMatch(filenameTitle, filenameArtist, bestFilenameFallbackResult))
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
		if (!IsDisposed && IsHandleCreated)
		{
			BeginInvoke(new Action(() =>
			{
				if (!IsDisposed)
				{
					searchResultsListView.SelectedItems.Cast<ListViewItem>().ForEachItem((ListViewItem item) => UpdateSelectionHighlight(item, searchResultsListView.Focused));
				}
			}));
		}
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
		DialogService.ShowErrorMessage(Resources.Msg_PleaseSelectItem);
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
		using CombinedTagOverwriteOptionsDialog optionsDialog = new CombinedTagOverwriteOptionsDialog();
		optionsDialog.ShowDialog();
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
		CoverImageActions.LoadAndUseCoverImage(selectedItem.AssociatedValue, imageAction);
	}

	private void OpenCoverMenuClick(object sender, EventArgs args)
	{
		WithSelectedCoverImageData(CoverImageActions.OpenCoverImage);
	}

	private void ExtractCoverMenuClick(object sender, EventArgs args)
	{
		WithSelectedCoverImageData(ExtractCoverImage);
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			if (trackIdLookupButton != null)
			{
				Image trackIdLookupImage = trackIdLookupButton.Image;
				trackIdLookupButton.Image = null;
				trackIdLookupImage?.Dispose();
			}
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
		trackIdLookupPanel = new TableLayoutPanel();
		trackIdLabel = new Label();
		trackIdSourceComboBox = new ComboBox();
		trackIdTextBox = new TextBox();
		trackIdLookupButton = new Button();
		trackIdLookupStatusLabel = new Label();
		trackIdLookupToolTip = new ToolTip(components);
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
		coverContextMenu = new ContextMenuStrip(components);
		openCoverMenuItem = new ToolStripMenuItem();
		extractCoverMenuItem = new ToolStripMenuItem();
		saveCoverDialog = new SaveFileDialog();
		mainPanel.SuspendLayout();
		trackIdLookupPanel.SuspendLayout();
		footerPanel.SuspendLayout();
		buttonPanel.SuspendLayout();
		okButtonMenu.SuspendLayout();
		coverContextMenu.SuspendLayout();
		SuspendLayout();
		mainPanel.Controls.Add(trackIdLookupPanel);
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
		trackIdLookupPanel.ColumnCount = 4;
		trackIdLookupPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62f));
		trackIdLookupPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112f));
		trackIdLookupPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		trackIdLookupPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42f));
		trackIdLookupPanel.Controls.Add(trackIdLabel, 0, 0);
		trackIdLookupPanel.Controls.Add(trackIdSourceComboBox, 1, 0);
		trackIdLookupPanel.Controls.Add(trackIdTextBox, 2, 0);
		trackIdLookupPanel.Controls.Add(trackIdLookupButton, 3, 0);
		trackIdLookupPanel.Controls.Add(trackIdLookupStatusLabel, 1, 1);
		trackIdLookupPanel.SetColumnSpan(trackIdLookupStatusLabel, 3);
		trackIdLookupPanel.Location = new Point(0, 0);
		trackIdLookupPanel.Margin = new Padding(0);
		trackIdLookupPanel.Name = "trackIdLookupPanel";
		trackIdLookupPanel.Padding = new Padding(6, 4, 6, 0);
		trackIdLookupPanel.RowCount = 2;
		trackIdLookupPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
		trackIdLookupPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20f));
		trackIdLookupPanel.Size = new Size(534, 58);
		trackIdLookupPanel.TabIndex = 0;
		trackIdLabel.Dock = DockStyle.Fill;
		trackIdLabel.Location = new Point(6, 4);
		trackIdLabel.Margin = new Padding(0);
		trackIdLabel.Name = "trackIdLabel";
		trackIdLabel.Size = new Size(62, 32);
		trackIdLabel.TabIndex = 0;
		trackIdLabel.Text = "Track ID";
		trackIdLabel.TextAlign = ContentAlignment.MiddleLeft;
		trackIdSourceComboBox.Dock = DockStyle.Fill;
		trackIdSourceComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
		trackIdSourceComboBox.FormattingEnabled = true;
		trackIdSourceComboBox.Location = new Point(71, 7);
		trackIdSourceComboBox.Margin = new Padding(3);
		trackIdSourceComboBox.Name = "trackIdSourceComboBox";
		trackIdSourceComboBox.Size = new Size(106, 22);
		trackIdSourceComboBox.TabIndex = 1;
		trackIdSourceComboBox.SelectedIndexChanged += TrackIdSourceSelectedIndexChanged;
		trackIdTextBox.Dock = DockStyle.Fill;
		trackIdTextBox.Location = new Point(183, 7);
		trackIdTextBox.Margin = new Padding(3);
		trackIdTextBox.Name = "trackIdTextBox";
		trackIdTextBox.Size = new Size(303, 22);
		trackIdTextBox.TabIndex = 2;
		trackIdTextBox.KeyDown += TrackIdTextBoxKeyDown;
		trackIdLookupButton.Dock = DockStyle.Fill;
		trackIdLookupButton.Location = new Point(492, 6);
		trackIdLookupButton.Margin = new Padding(3, 2, 3, 2);
		trackIdLookupButton.Name = "trackIdLookupButton";
		trackIdLookupButton.Size = new Size(36, 28);
		trackIdLookupButton.TabIndex = 3;
		trackIdLookupButton.UseVisualStyleBackColor = true;
		trackIdLookupButton.Click += TrackIdLookupButtonClick;
		trackIdLookupStatusLabel.AutoEllipsis = true;
		trackIdLookupStatusLabel.Dock = DockStyle.Fill;
		trackIdLookupStatusLabel.ForeColor = SystemColors.GrayText;
		trackIdLookupStatusLabel.Location = new Point(71, 36);
		trackIdLookupStatusLabel.Margin = new Padding(3, 0, 3, 0);
		trackIdLookupStatusLabel.Name = "trackIdLookupStatusLabel";
		trackIdLookupStatusLabel.Size = new Size(457, 20);
		trackIdLookupStatusLabel.TabIndex = 4;
		trackIdLookupStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
		trackIdLookupStatusLabel.Visible = false;
		searchResultsListView.Columns.AddRange(new ColumnHeader[6] { coverColumn, sourceColumn, titleColumn, artistColumn, albumColumn, commentColumn });
		searchResultsListView.EmbeddedControlInset = 4;
		searchResultsListView.FullRowSelect = true;
		searchResultsListView.HeaderStyle = ColumnHeaderStyle.Nonclickable;
		searchResultsListView.HideSelection = false;
		searchResultsListView.Location = new Point(0, 58);
		searchResultsListView.Margin = new Padding(0);
		searchResultsListView.MultiSelect = false;
		searchResultsListView.Name = "listView1";
		searchResultsListView.OwnerDraw = true;
		searchResultsListView.Size = new Size(534, 373);
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
		CancelButton = cancelButton;
		AcceptButton = okSplitButton;
		searchStatusLabel.AutoSize = false;
		searchStatusLabel.AutoEllipsis = true;
		searchStatusLabel.Margin = new Padding(0, 10, 0, 0);
		searchStatusLabel.Name = "searchStatusLabel";
		searchStatusLabel.Size = new Size(200, 40);
		searchStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
		searchStatusLabel.ForeColor = SystemColors.GrayText;
		searchStatusLabel.Visible = false;
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
		trackIdLookupPanel.ResumeLayout(performLayout: false);
		trackIdLookupPanel.PerformLayout();
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
		CoverImageActions.SaveCoverImageAs(saveCoverDialog, imageData);
	}

}



