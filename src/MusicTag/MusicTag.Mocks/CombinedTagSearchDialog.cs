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

	[StructLayout(LayoutKind.Auto)]
	[CompilerGenerated]
	private struct _003CDownloadLyric_003Ed__40 : IAsyncStateMachine
	{
		public int _003C_003E1__state;

		public AsyncVoidMethodBuilder _003C_003Et__builder;

		public LyricSearchResult li;

		public CombinedTagSearchDialog _003C_003E4__this;

		private DeferredLyricDownloadContext lyricDownloadContext;

		public int taskNo;

		public int taskSubNo;

		private TaskAwaiter<LyricSearchResult> _003C_003Eu__1;

		private void MoveNext()
		{
			int num = default(int);
			num = _003C_003E1__state;
			CombinedTagSearchDialog creatorListenerMock = _003C_003E4__this;
			try
			{
				if (num != 0)
				{
					lyricDownloadContext = new DeferredLyricDownloadContext();
					lyricDownloadContext.LyricResult = li;
					lyricDownloadContext.Owner = _003C_003E4__this;
				}
				try
				{
					TaskAwaiter<LyricSearchResult> awaiter;
					if (num != 0)
					{
						awaiter = Task.Run((Func<LyricSearchResult>)lyricDownloadContext.LoadLyric, creatorListenerMock.cancellationSource.Token).GetAwaiter();
						if (!awaiter.IsCompleted)
						{
							_003C_003E1__state = 0;
							_003C_003Eu__1 = awaiter;
							_003C_003Et__builder.AwaitUnsafeOnCompleted(ref awaiter, ref this);
							return;
						}
					}
					else
					{
						awaiter = _003C_003Eu__1;
						_003C_003Eu__1 = default(TaskAwaiter<LyricSearchResult>);
						num = -1;
						_003C_003E1__state = -1;
					}
					LyricSearchResult result = awaiter.GetResult();
					if (result != null && result.HasDownloadableLyric())
					{
						lyricDownloadContext.LyricResult.Lyric = result.Lyric;
						lyricDownloadContext.LyricResult.TranslatedLyric = result.TranslatedLyric;
						((creatorListenerMock.searchResultsListView.Items[lyricDownloadContext.LyricResult.ListItemIndex].SubItems[creatorListenerMock.sourceColumn.Index] as EmbeddedControlSubItem).EmbeddedControl as TagSearchCandidatePanel).Lyric = "Y";
					}
					List<TrackSearchResult>.Enumerator enumerator = cachedSearchResults.GetEnumerator();
					try
					{
						while (enumerator.MoveNext())
						{
							TrackSearchResult current = enumerator.Current;
							if (creatorListenerMock.cancellationSource.IsCancellationRequested)
							{
								break;
							}
							lyricDownloadContext.LyricResult = current.LyricResult;
							if (lyricDownloadContext.LyricResult == null || lyricDownloadContext.LyricResult.IsLoaded)
							{
								continue;
							}
							lyricDownloadContext.LyricResult.IsLoaded = true;
							creatorListenerMock.DownloadLyricAsync(lyricDownloadContext.LyricResult, taskNo, ++taskSubNo);
							goto end_IL_001a;
						}
					}
					finally
					{
						if (num < 0)
						{
							((IDisposable)enumerator/*cast due to constrained. prefix*/).Dispose();
						}
					}
				}
				catch (System.Exception v)
				{
					Console.WriteLine("DownloadLyric error:" + v.GetMessageChain());
				}
				creatorListenerMock.activeMediaDownloadCount--;
				end_IL_001a:;
			}
			catch (System.Exception exception)
			{
				_003C_003E1__state = -2;
				_003C_003Et__builder.SetException(exception);
				return;
			}
			_003C_003E1__state = -2;
			_003C_003Et__builder.SetResult();
		}

		void IAsyncStateMachine.MoveNext()
		{
			//ILSpy generated this explicit interface implementation from .override directive in MoveNext
			this.MoveNext();
		}

		[DebuggerHidden]
		private void SetStateMachine(IAsyncStateMachine stateMachine)
		{
			_003C_003Et__builder.SetStateMachine(stateMachine);
		}

		void IAsyncStateMachine.SetStateMachine(IAsyncStateMachine stateMachine)
		{
			//ILSpy generated this explicit interface implementation from .override directive in SetStateMachine
			this.SetStateMachine(stateMachine);
		}
	}

	[CompilerGenerated]
	private sealed class CoverDownloadRequestContext
	{
		public CoverSearchResult CoverResult;

		public CombinedTagSearchDialog Owner;
	}

	[CompilerGenerated]
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

	[CompilerGenerated]
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

	[StructLayout(LayoutKind.Auto)]
	[CompilerGenerated]
	private struct _003CDownloadPicture_003Ed__41 : IAsyncStateMachine
	{
		public int _003C_003E1__state;

		public AsyncVoidMethodBuilder _003C_003Et__builder;

		public CoverSearchResult pi;

		public CombinedTagSearchDialog _003C_003E4__this;

		private CoverImageLoadTask coverLoadTask;

		public int taskNo;

		public int taskSubNo;

		private TaskAwaiter<Image> _003C_003Eu__1;

		private void MoveNext()
		{
			int num = _003C_003E1__state;
			CombinedTagSearchDialog creatorListenerMock = _003C_003E4__this;
			try
			{
					CoverDownloadRequestContext coverDownloadRequest = default(CoverDownloadRequestContext);
					if (num != 0)
					{
						coverDownloadRequest = new CoverDownloadRequestContext();
						coverDownloadRequest.CoverResult = pi;
						coverDownloadRequest.Owner = _003C_003E4__this;
					}
				try
				{
					TaskAwaiter<Image> awaiter;
					if (num != 0)
					{
						coverLoadTask = new CoverImageLoadTask();
						coverLoadTask.Request = coverDownloadRequest;
						coverLoadTask.OriginalImageSize = null;
						awaiter = Task.Run((Func<Image>)coverLoadTask.LoadOrDownloadImage, creatorListenerMock.cancellationSource.Token).GetAwaiter();
						if (!awaiter.IsCompleted)
						{
							_003C_003E1__state = 0;
							_003C_003Eu__1 = awaiter;
							_003C_003Et__builder.AwaitUnsafeOnCompleted(ref awaiter, ref this);
							return;
						}
					}
					else
					{
						awaiter = _003C_003Eu__1;
						_003C_003Eu__1 = default(TaskAwaiter<Image>);
						num = -1;
						_003C_003E1__state = -1;
					}
					Image result = awaiter.GetResult();
					creatorListenerMock.searchResultsListView.BeginUpdate();
					CoverImageListViewItem coverImageListViewItem = creatorListenerMock.searchResultsListView.Items[coverLoadTask.Request.CoverResult.ListViewIndex] as CoverImageListViewItem;
					if (result != null)
					{
						creatorListenerMock.coverImageCache.Add(coverLoadTask.Request.CoverResult.LocalCoverPath, result);
						coverImageListViewItem.AssociatedValue = coverLoadTask.Request.CoverResult.LocalCoverPath;
						IEnumerator enumerator = creatorListenerMock.searchResultsListView.Items.GetEnumerator();
						try
						{
							while (enumerator.MoveNext())
							{
								CoverImageListViewItem matchingCoverItem = (CoverImageListViewItem)enumerator.Current;
								if ((string)matchingCoverItem.AssociatedValue != (string)coverImageListViewItem.AssociatedValue)
								{
									continue;
								}
								matchingCoverItem.CoverImage = creatorListenerMock.coverImageCache[coverLoadTask.Request.CoverResult.LocalCoverPath];
								if (coverLoadTask.OriginalImageSize.HasValue)
								{
									matchingCoverItem.CoverImage.Tag = coverLoadTask.OriginalImageSize?.Width + "x" + coverLoadTask.OriginalImageSize?.Height;
								}
								else
								{
									matchingCoverItem.CoverImage.Tag = "";
								}
								((matchingCoverItem.SubItems[creatorListenerMock.sourceColumn.Index] as EmbeddedControlSubItem).EmbeddedControl as TagSearchCandidatePanel).PictureSize = matchingCoverItem.CoverImage.Tag as string;
							}
						}
						finally
						{
							if (num < 0 && enumerator is IDisposable disposable)
							{
								disposable.Dispose();
							}
						}
					}
					else
					{
						coverImageListViewItem.AssociatedValue = coverLoadTask.Request.CoverResult.LocalCoverPath;
						if (creatorListenerMock.coverImageCache.ContainsKey(coverLoadTask.Request.CoverResult.LocalCoverPath))
						{
							coverImageListViewItem.CoverImage = creatorListenerMock.coverImageCache[coverLoadTask.Request.CoverResult.LocalCoverPath];
							TagSearchCandidatePanel searchCandidatePanel = (coverImageListViewItem.SubItems[creatorListenerMock.sourceColumn.Index] as EmbeddedControlSubItem).EmbeddedControl as TagSearchCandidatePanel;
							IEnumerator enumerator = creatorListenerMock.searchResultsListView.Items.GetEnumerator();
							try
							{
								while (enumerator.MoveNext())
								{
									CoverImageListViewItem matchingCoverItem = (CoverImageListViewItem)enumerator.Current;
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
							finally
							{
								if (num < 0 && enumerator is IDisposable disposable2)
								{
									disposable2.Dispose();
								}
							}
						}
					}
					creatorListenerMock.searchResultsListView.EndUpdate();
					bool queuedNextCoverDownload = false;
					List<TrackSearchResult>.Enumerator enumerator2 = cachedSearchResults.GetEnumerator();
					try
					{
						while (enumerator2.MoveNext())
						{
							TrackSearchResult current = enumerator2.Current;
							if (creatorListenerMock.cancellationSource.IsCancellationRequested)
							{
								break;
							}
							CoverSearchResult filterDescriptor = current.Cover;
							if (filterDescriptor == null || filterDescriptor.CoverDownloadQueued)
							{
								continue;
							}
							filterDescriptor.CoverDownloadQueued = true;
							creatorListenerMock.DownloadCoverAsync(filterDescriptor, taskNo, ++taskSubNo);
							queuedNextCoverDownload = true;
							break;
						}
					}
					finally
					{
						if (num < 0)
						{
							((IDisposable)enumerator2/*cast due to constrained. prefix*/).Dispose();
						}
					}
					if (!queuedNextCoverDownload)
					{
						coverLoadTask = null;
						creatorListenerMock.activeMediaDownloadCount--;
					}
				}
				catch (System.Exception v)
				{
					Console.WriteLine("DownloadPicture error:" + v.GetMessageChain());
					creatorListenerMock.activeMediaDownloadCount--;
				}
			}
			catch (System.Exception exception)
			{
				_003C_003E1__state = -2;
				_003C_003Et__builder.SetException(exception);
				return;
			}
			_003C_003E1__state = -2;
			_003C_003Et__builder.SetResult();
		}

		void IAsyncStateMachine.MoveNext()
		{
			//ILSpy generated this explicit interface implementation from .override directive in MoveNext
			this.MoveNext();
		}

		[DebuggerHidden]
		private void SetStateMachine(IAsyncStateMachine stateMachine)
		{
			_003C_003Et__builder.SetStateMachine(stateMachine);
		}

		void IAsyncStateMachine.SetStateMachine(IAsyncStateMachine stateMachine)
		{
			//ILSpy generated this explicit interface implementation from .override directive in SetStateMachine
			this.SetStateMachine(stateMachine);
		}

	}

	[CompilerGenerated]
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
				if (!Owner.cancellationSource.IsCancellationRequested && searchLimits.RemainingGlobalResults > 0 && searchLimits.RemainingResultsBySource[preferredSource.Value] > 0)
				{
					searchLimits.CurrentBatch = Owner.SearchCurrentContextAlbumFallback(preferredSource.Value, searchLimits.AccumulatedResults, 0);
					searchLimits.RankLimitAndReportCurrentBatch(useProviderRanking: false);
				}
				return !Owner.cancellationSource.IsCancellationRequested;
			}
			List<SourceItem> tagSources = TrackSearchResult.GetSortedTagSourceSettings();
			tagSources.ForEach(searchLimits.InitializeSourceLimit);
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
				searchLimits.CurrentBatch = new List<TrackSearchResult>();
				foreach (SourceItem primarySource in tagSources)
				{
					if (!Owner.cancellationSource.IsCancellationRequested && primarySource.Enabled && !primarySource.IsSecondarySource && searchLimits.RemainingResultsBySource[primarySource.SearchSource] > 0)
					{
						searchLimits.CurrentBatch.AddRange(Owner.SearchCurrentContextTracks(primarySource.SearchSource, useLinkedNetEaseId: false, searchLimits.AccumulatedResults, searchPass++));
					}
				}
				searchLimits.RankLimitAndReportCurrentBatch(useProviderRanking: true);
			}
			if (!Owner.cancellationSource.IsCancellationRequested && searchLimits.RemainingGlobalResults > 0)
			{
				searchLimits.CurrentBatch = new List<TrackSearchResult>();
				foreach (SourceItem secondarySource in tagSources)
				{
					if (Owner.cancellationSource.IsCancellationRequested || !secondarySource.Enabled || !secondarySource.IsSecondarySource || searchLimits.RemainingResultsBySource[secondarySource.SearchSource] <= 0)
					{
						continue;
					}
					searchLimits.CurrentBatch.AddRange(Owner.SearchCurrentContextTracks(secondarySource.SearchSource, useLinkedNetEaseId: false, searchLimits.AccumulatedResults, searchPass++));
				}
				searchLimits.RankLimitAndReportCurrentBatch(useProviderRanking: false);
			}
			if (!Owner.cancellationSource.IsCancellationRequested && searchLimits.RemainingGlobalResults > 0)
			{
				searchLimits.CurrentBatch = new List<TrackSearchResult>();
				foreach (SourceItem albumFallbackSource in tagSources)
				{
					if (!Owner.cancellationSource.IsCancellationRequested && albumFallbackSource.Enabled && albumFallbackSource.IsSecondarySource && searchLimits.RemainingResultsBySource[albumFallbackSource.SearchSource] > 0)
					{
						searchLimits.CurrentBatch.AddRange(Owner.SearchCurrentContextAlbumFallback(albumFallbackSource.SearchSource, searchLimits.AccumulatedResults, searchPass++));
					}
				}
				searchLimits.RankLimitAndReportCurrentBatch(useProviderRanking: false);
			}
			return !Owner.cancellationSource.IsCancellationRequested;
		}

		internal bool IsPreferredSource(SourceItem sourceItem)
		{
			return sourceItem.SearchSource == Owner.preferredSource;
		}
	}

	[CompilerGenerated]
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

	[CompilerGenerated]
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

	[StructLayout(LayoutKind.Auto)]
	[CompilerGenerated]
	private struct _003CSearchCombTags_003Ed__46 : IAsyncStateMachine
	{
		public int _003C_003E1__state;

		public AsyncVoidMethodBuilder _003C_003Et__builder;

		public CombinedTagSearchDialog _003C_003E4__this;

		private TaskAwaiter<bool> _003C_003Eu__1;

		private void MoveNext()
		{
			int state = _003C_003E1__state;
			CombinedTagSearchDialog creatorListenerMock = _003C_003E4__this;
			try
			{
				TaskAwaiter<bool> awaiter;
				if (state == 0)
				{
					awaiter = _003C_003Eu__1;
					_003C_003Eu__1 = default(TaskAwaiter<bool>);
					state = -1;
					_003C_003E1__state = -1;
				}
				else
				{
					TrackSearchCoordinator trackSearchCoordinator = new TrackSearchCoordinator();
					trackSearchCoordinator.Owner = creatorListenerMock;
					cachedSearchResults = new List<TrackSearchResult>();
					trackSearchCoordinator.ProgressReporter = new Progress<List<TrackSearchResult>>(trackSearchCoordinator.OnSearchResultsReported);
					creatorListenerMock.taskbarProgress.SetProgressState(TaskbarProgressBarStatus.Indeterminate);
					awaiter = Task.Run((Func<bool>)trackSearchCoordinator.SearchAllSources, creatorListenerMock.cancellationSource.Token).GetAwaiter();
					if (!awaiter.IsCompleted)
					{
						_003C_003E1__state = 0;
						_003C_003Eu__1 = awaiter;
						_003C_003Et__builder.AwaitUnsafeOnCompleted(ref awaiter, ref this);
						return;
					}
				}
				cachedSearchCompleted = awaiter.GetResult();
				creatorListenerMock.progressPictureBox.Hide();
				creatorListenerMock.taskbarProgress.SetProgressState(TaskbarProgressBarStatus.NoProgress);
			}
			catch (System.Exception exception)
			{
				_003C_003E1__state = -2;
				_003C_003Et__builder.SetException(exception);
				return;
			}
			_003C_003E1__state = -2;
			_003C_003Et__builder.SetResult();
		}

		void IAsyncStateMachine.MoveNext()
		{
			//ILSpy generated this explicit interface implementation from .override directive in MoveNext
			this.MoveNext();
		}

		[DebuggerHidden]
		private void SetStateMachine(IAsyncStateMachine stateMachine)
		{
			_003C_003Et__builder.SetStateMachine(stateMachine);
		}

		void IAsyncStateMachine.SetStateMachine(IAsyncStateMachine stateMachine)
		{
			//ILSpy generated this explicit interface implementation from .override directive in SetStateMachine
			this.SetStateMachine(stateMachine);
		}
	}

		private int activeMediaDownloadCount;

	private static bool cachedSearchCompleted;

	private static TrackSearchContext lastSearchContext;

	private static SearchSource? lastPreferredSource;

	[CompilerGenerated]
	private TrackSearchContext currentSearchContext;

	[CompilerGenerated]
	private SearchSource? preferredSource;

	[CompilerGenerated]
	private readonly CancellationTokenSource cancellationSource;

	[CompilerGenerated]
	private static List<TrackSearchResult> cachedSearchResults;

	[CompilerGenerated]
	private readonly HashSet<string> queuedCoverDownloadPaths;

	[CompilerGenerated]
	private readonly Dictionary<string, Image> coverImageCache;

	[CompilerGenerated]
	private readonly TaskbarProgressController taskbarProgress;

	private IContainer components;

	private FlowLayoutPanel mainPanel;

	private ImageList coverImageList;

	private FlowLayoutPanel footerPanel;

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

	private PictureBox progressPictureBox;

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
		progressPictureBox.Image = DatabaseMapper.LoadResourceBitmap("img_wait");
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
		int left = (footerPanel.Width - buttonPanel.Width) / 2;
		buttonPanel.Margin = new Padding(left, buttonPanel.Margin.Top, 0, buttonPanel.Margin.Bottom);
		left = footerPanel.Width - progressPictureBox.Width - buttonPanel.Location.X - buttonPanel.Width - progressPictureBox.Margin.Top;
		progressPictureBox.Margin = new Padding(left, progressPictureBox.Margin.Top, 0, progressPictureBox.Margin.Bottom);
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

	[AsyncStateMachine(typeof(_003CDownloadLyric_003Ed__40))]
	private void DownloadLyricAsync(LyricSearchResult lyricResult, int taskNo, int taskSubNo)
	{
		_003CDownloadLyric_003Ed__40 stateMachine = default(_003CDownloadLyric_003Ed__40);
		stateMachine._003C_003E4__this = this;
		stateMachine.li = lyricResult;
		stateMachine.taskNo = taskNo;
		stateMachine.taskSubNo = taskSubNo;
		stateMachine._003C_003Et__builder = AsyncVoidMethodBuilder.Create();
		stateMachine._003C_003E1__state = -1;
		AsyncVoidMethodBuilder asyncVoidMethodBuilder = stateMachine._003C_003Et__builder;
		asyncVoidMethodBuilder.Start(ref stateMachine);
	}

	[AsyncStateMachine(typeof(_003CDownloadPicture_003Ed__41))]
	private void DownloadCoverAsync(CoverSearchResult coverResult, int taskNo, int taskSubNo)
	{
		_003CDownloadPicture_003Ed__41 stateMachine = default(_003CDownloadPicture_003Ed__41);
		stateMachine._003C_003E4__this = this;
		stateMachine.pi = coverResult;
		stateMachine.taskNo = taskNo;
		stateMachine.taskSubNo = taskSubNo;
		stateMachine._003C_003Et__builder = AsyncVoidMethodBuilder.Create();
		stateMachine._003C_003E1__state = -1;
		AsyncVoidMethodBuilder asyncVoidMethodBuilder = stateMachine._003C_003Et__builder;
		asyncVoidMethodBuilder.Start(ref stateMachine);
	}

	private List<TrackSearchResult> SearchCurrentContextTracks(SearchSource source, bool useLinkedNetEaseId, List<TrackSearchResult> existingResults, int searchPass)
	{
		return SearchTracksFromSource(source, useLinkedNetEaseId, existingResults, searchPass, currentSearchContext, cancellationSource);
	}

	public static List<TrackSearchResult> SearchTracksFromSource(SearchSource source, bool useLinkedNetEaseId, List<TrackSearchResult> existingResults, int searchPass, TrackSearchContext searchContext, CancellationTokenSource cancellationSource)
	{
		List<TrackSearchResult> results = new List<TrackSearchResult>();
		switch (source)
		{
		case SearchSource.Music163:
			{
				using NetEaseMusicTagProvider netEaseProvider = new NetEaseMusicTagProvider(cancellationSource);
				if (useLinkedNetEaseId)
				{
					results.AddRange(netEaseProvider.SearchTracks("", 0, searchContext.LinkedMusicMetadata.musicId, 0, searchPass, existingResults, results));
					return results;
				}
				results.AddRange(netEaseProvider.SearchTracks((searchContext.Title + " " + searchContext.Artist).Trim(), 15, 0L, 0, searchPass, existingResults, results));
				if (cancellationSource.IsCancellationRequested)
				{
					return results;
				}
				if (!string.IsNullOrWhiteSpace(searchContext.Artist))
				{
					results.AddRange(netEaseProvider.SearchTracks(searchContext.Title.Trim(), 10, 0L, 1, searchPass, existingResults, results));
				}
				if (cancellationSource.IsCancellationRequested)
				{
					return results;
				}
				if (!string.IsNullOrWhiteSpace(searchContext.Album) && searchContext.Album != searchContext.Title)
				{
					results.AddRange(netEaseProvider.SearchTracks((searchContext.Album + " " + searchContext.Artist).Trim(), 8, 0L, 2, searchPass, existingResults, results));
				}
				return results;
			}
		case SearchSource.QQ:
			{
				using QqMusicTagProvider qqProvider = new QqMusicTagProvider(cancellationSource);
				results.AddRange(qqProvider.SearchTracks((searchContext.Title + " " + searchContext.Artist).Trim(), 15, 0, searchPass, existingResults, results));
				if (cancellationSource.IsCancellationRequested)
				{
					return results;
				}
				if (!string.IsNullOrWhiteSpace(searchContext.Artist))
				{
					results.AddRange(qqProvider.SearchTracks(searchContext.Title.Trim(), 10, 1, searchPass, existingResults, results));
				}
				if (cancellationSource.IsCancellationRequested)
				{
					return results;
				}
				if (!string.IsNullOrWhiteSpace(searchContext.Album) && searchContext.Album != searchContext.Title)
				{
					results.AddRange(qqProvider.SearchTracks((searchContext.Album + " " + searchContext.Artist).Trim(), 8, 2, searchPass, existingResults, results));
				}
				return results;
			}
		case SearchSource.Xiami:
			{
				using XiamiTagProvider xiamiTagProvider = new XiamiTagProvider(cancellationSource);
				results.AddRange(xiamiTagProvider.SearchTracks((searchContext.Title + " " + searchContext.Artist).Trim(), 8, 0, searchPass, existingResults, results));
				if (cancellationSource.IsCancellationRequested)
				{
					return results;
				}
				if (!string.IsNullOrWhiteSpace(searchContext.Artist))
				{
					results.AddRange(xiamiTagProvider.SearchTracks(searchContext.Title.Trim(), 5, 1, searchPass, existingResults, results));
				}
				if (cancellationSource.IsCancellationRequested)
				{
					return results;
				}
				if (!string.IsNullOrWhiteSpace(searchContext.Album) && searchContext.Album != searchContext.Title)
				{
					results.AddRange(xiamiTagProvider.SearchTracks((searchContext.Album + " " + searchContext.Artist).Trim(), 5, 2, searchPass, existingResults, results));
				}
				return results;
			}
		case SearchSource.ITunes:
			{
				using ItunesTagProvider itunesTagProvider = new ItunesTagProvider(cancellationSource);
				results.AddRange(itunesTagProvider.SearchTracks((searchContext.Title + " " + searchContext.Artist).Trim(), 8, 0, searchPass, existingResults, results));
				if (cancellationSource.IsCancellationRequested)
				{
					return results;
				}
				if (!results.Any() && !string.IsNullOrWhiteSpace(searchContext.Album) && searchContext.Album != searchContext.Title)
				{
					results.AddRange(itunesTagProvider.SearchTracks((searchContext.Album + " " + searchContext.Artist).Trim(), 8, 1, searchPass, existingResults, results));
				}
				return results;
			}
		default:
			return new List<TrackSearchResult>();
		case SearchSource.Brainz:
			{
				using MusicBrainzTagProvider musicBrainzProvider = new MusicBrainzTagProvider(cancellationSource);
				results.AddRange(musicBrainzProvider.SearchTracks(searchContext.Title, searchContext.Artist, searchContext.Album, 10, 0, searchPass, existingResults, results));
				if (cancellationSource.IsCancellationRequested)
				{
					return results;
				}
				if (!string.IsNullOrWhiteSpace(searchContext.Artist))
				{
					results.AddRange(musicBrainzProvider.SearchTracks(searchContext.Title, "", searchContext.Album, 10, 0, searchPass, existingResults, results));
				}
				return results;
			}
		case SearchSource.Vgmdb:
			{
				using VgmdbTagProvider vgmdbTagProvider = new VgmdbTagProvider(cancellationSource);
				results.AddRange(vgmdbTagProvider.SearchTracks(searchContext.Title, searchContext.Artist, searchContext.Album, 10, 0, searchPass, existingResults, results));
				if (cancellationSource.IsCancellationRequested)
				{
					return results;
				}
				if (!string.IsNullOrWhiteSpace(searchContext.Artist))
				{
					results.AddRange(vgmdbTagProvider.SearchTracks(searchContext.Title, "", searchContext.Album, 10, 0, searchPass, existingResults, results));
				}
				return results;
			}
		case SearchSource.Kuwo:
			{
				using KuwoTagProvider kuwoTagProvider = new KuwoTagProvider(cancellationSource);
				results.AddRange(kuwoTagProvider.SearchTracks((searchContext.Title + " " + searchContext.Artist).Trim(), 8, 0, searchPass, existingResults, results));
				if (cancellationSource.IsCancellationRequested)
				{
					return results;
				}
				if (!results.Any() && !string.IsNullOrWhiteSpace(searchContext.Album) && searchContext.Album != searchContext.Title)
				{
					results.AddRange(kuwoTagProvider.SearchTracks((searchContext.Album + " " + searchContext.Artist).Trim(), 8, 1, searchPass, existingResults, results));
				}
				return results;
			}
		}
	}

	private List<TrackSearchResult> SearchCurrentContextAlbumFallback(SearchSource source, List<TrackSearchResult> existingResults, int searchPass)
	{
		return SearchAlbumFallbackTracks(source, existingResults, searchPass, currentSearchContext, cancellationSource);
	}

	public static List<TrackSearchResult> SearchAlbumFallbackTracks(SearchSource source, List<TrackSearchResult> existingResults, int searchPass, TrackSearchContext searchContext, CancellationTokenSource cancellationSource)
	{
		List<TrackSearchResult> results = new List<TrackSearchResult>();
		switch (source)
		{
		default:
			return new List<TrackSearchResult>();
		case SearchSource.Vgmdb:
			{
				using VgmdbTagProvider vgmdbTagProvider = new VgmdbTagProvider(cancellationSource);
				if (!string.IsNullOrWhiteSpace(searchContext.Album))
				{
					results.AddRange(vgmdbTagProvider.SearchTracks("", searchContext.Artist, searchContext.Album, 10, 0, searchPass, existingResults, results));
				}
				return results;
			}
		case SearchSource.Brainz:
			{
				using MusicBrainzTagProvider musicBrainzProvider = new MusicBrainzTagProvider(cancellationSource);
				if (!string.IsNullOrWhiteSpace(searchContext.Album))
				{
					results.AddRange(musicBrainzProvider.SearchTracks("", searchContext.Artist, searchContext.Album, 10, 0, searchPass, existingResults, results));
				}
				return results;
			}
		}
	}

	[AsyncStateMachine(typeof(_003CSearchCombTags_003Ed__46))]
	private void SearchCombinedTagsAsync()
	{
		_003CSearchCombTags_003Ed__46 stateMachine = default(_003CSearchCombTags_003Ed__46);
		stateMachine._003C_003E4__this = this;
		stateMachine._003C_003Et__builder = AsyncVoidMethodBuilder.Create();
		stateMachine._003C_003E1__state = -1;
		AsyncVoidMethodBuilder asyncVoidMethodBuilder = stateMachine._003C_003Et__builder;
		asyncVoidMethodBuilder.Start(ref stateMachine);
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
		searchResultsListView = new MusicTagWinApp.Roles.EditableListView();
		coverColumn = new ColumnHeader();
		sourceColumn = new ColumnHeader();
		titleColumn = new ColumnHeader();
		artistColumn = new ColumnHeader();
		albumColumn = new ColumnHeader();
		commentColumn = new ColumnHeader();
		coverImageList = new ImageList(components);
		footerPanel = new FlowLayoutPanel();
		buttonPanel = new FlowLayoutPanel();
		okSplitButton = new SplitButton();
		okButtonMenu = new ContextMenuStrip(components);
		overwriteOptionsMenuItem = new ToolStripMenuItem();
		cancelButton = new Button();
		progressPictureBox = new PictureBox();
		cachedResultsTimer = new System.Windows.Forms.Timer(components);
		coverContextMenu = new ContextMenuStrip(components);
		openCoverMenuItem = new ToolStripMenuItem();
		extractCoverMenuItem = new ToolStripMenuItem();
		saveCoverDialog = new SaveFileDialog();
		mainPanel.SuspendLayout();
		footerPanel.SuspendLayout();
		buttonPanel.SuspendLayout();
		okButtonMenu.SuspendLayout();
		((ISupportInitialize)progressPictureBox).BeginInit();
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
		footerPanel.Controls.Add(buttonPanel);
		footerPanel.Controls.Add(progressPictureBox);
		footerPanel.Dock = DockStyle.Fill;
		footerPanel.Location = new Point(0, 431);
		footerPanel.Margin = new Padding(0);
		footerPanel.Name = "flowLayoutPanel2";
		footerPanel.Size = new Size(534, 60);
		footerPanel.TabIndex = 8;
		footerPanel.WrapContents = false;
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
		progressPictureBox.Image = Resources.img_wait;
		progressPictureBox.Location = new Point(220, 13);
		progressPictureBox.Margin = new Padding(0, 13, 0, 0);
		progressPictureBox.Name = "pbProgress";
		progressPictureBox.Size = new Size(32, 32);
		progressPictureBox.SizeMode = PictureBoxSizeMode.Zoom;
		progressPictureBox.TabIndex = 7;
		progressPictureBox.TabStop = false;
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
		((ISupportInitialize)progressPictureBox).EndInit();
		coverContextMenu.ResumeLayout(performLayout: false);
		ResumeLayout(performLayout: false);
	}

	[CompilerGenerated]
	private void AddCachedResultsOnTimerTick(object sender, EventArgs args)
	{
		AddSearchResultsToList(cachedSearchResults);
		progressPictureBox.Hide();
		cachedResultsTimer.Stop();
	}

	[CompilerGenerated]
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



