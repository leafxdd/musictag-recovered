using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;
using MusicTag.Candidates;
using MusicTag.Mocks;
using MusicTag.Readers;
using MusicTag.Serialization;
using MusicTag.States;
using MusicTagWinApp.Common;
using MusicTagWinApp.Containers;
using MusicTagWinApp.Exporters;
using MusicTagWinApp.Instances;
using MusicTagWinApp.Listeners;
using MusicTagWinApp.Properties;
using MusicTagWinApp.Roles;
using MusicTagWinApp.Web;
using Newtonsoft.Json;

namespace MusicTagWinApp.Adapter;

internal class AutoMatchTagsDialog : Form
{
	private class FilePathQueue
	{
		private readonly string[] paths;

		private int nextPathIndex;

		public FilePathQueue(string[] paths)
		{
			this.paths = paths;
		}

		[MethodImpl(MethodImplOptions.Synchronized)]
		public string GetNextPath()
		{
			if (nextPathIndex >= paths.Length)
			{
				return null;
			}
			return paths[nextPathIndex++];
		}

		[MethodImpl(MethodImplOptions.Synchronized)]
		public bool HasMorePaths()
		{
			return nextPathIndex < paths.Length;
		}
	}

	private class ActiveWorkerCounter
	{
		private int activeCount;

		private readonly object syncRoot = new object();

		public void Increment()
		{
			lock (syncRoot)
			{
				activeCount++;
			}
		}

		public bool ReleaseAndIsIdle()
		{
			lock (syncRoot)
			{
				if (activeCount == 0)
				{
					return true;
				}
				activeCount--;
				return activeCount == 0;
			}
		}

		public bool IsIdle()
		{
			lock (syncRoot)
			{
				return activeCount == 0;
			}
		}
	}

	private class CoverTempFileCache
	{
		public class CoverTempFileLease : ActiveWorkerCounter
		{
			public string CoverUrl;

			public RemoteTagProviderBase.DownloadStatus DownloadStatus = RemoteTagProviderBase.DownloadStatus.NotStarted;
		}

		private readonly Dictionary<string, CoverTempFileLease> leasesByPath = new Dictionary<string, CoverTempFileLease>();

		[MethodImpl(MethodImplOptions.Synchronized)]
		public (string path, CoverTempFileLease info) Reserve(string coverUrl)
		{
			foreach (KeyValuePair<string, CoverTempFileLease> item in leasesByPath)
			{
				string tempPath = item.Key;
				CoverTempFileLease lease = item.Value;
				if (lease.CoverUrl == coverUrl)
				{
					lease.Increment();
					return (path: tempPath, info: lease);
				}
			}
			foreach (KeyValuePair<string, CoverTempFileLease> item in leasesByPath)
			{
				string tempPath = item.Key;
				CoverTempFileLease lease = item.Value;
				if (lease.IsIdle())
				{
					lease.CoverUrl = coverUrl;
					lease.DownloadStatus = RemoteTagProviderBase.DownloadStatus.NotStarted;
					lease.Increment();
					return (path: tempPath, info: lease);
				}
			}
			int suffix = 0;
			string newTempPath;
			while (true)
			{
				newTempPath = DatabaseMapper.GetPictureCacheDirectory() + "tempCover" + ((suffix <= 0) ? "" : suffix.ToString());
				if (!leasesByPath.ContainsKey(newTempPath))
				{
					break;
				}
				suffix++;
			}
			CoverTempFileLease newLease = new CoverTempFileLease
			{
				CoverUrl = coverUrl
			};
			newLease.Increment();
			leasesByPath.Add(newTempPath, newLease);
			return (path: newTempPath, info: newLease);
		}

		[MethodImpl(MethodImplOptions.Synchronized)]
		public void Release(string tempPath)
		{
			if (tempPath != null && leasesByPath.TryGetValue(tempPath, out var lease))
			{
				lease.ReleaseAndIsIdle();
			}
		}
	}

	private class AutoMatchWorker
	{
		private sealed class AutoMatchFileSearchTask
		{
			public AutoMatchWorker worker;

			public string filePath;

			internal void RunSearch()
			{
				if (worker.GetCancellationSource().IsCancellationRequested)
				{
					return;
				}

				LoadedTagContext loadedTagContext = new LoadedTagContext
				{
					searchTask = this
				};
				worker.GetActiveFilePathMap().TryAdd(filePath, value: true);
				try
				{
					loadedTagContext.tagFile = worker.LoadCurrentTagFile();
					if (!loadedTagContext.tagFile.IsLoadedSuccessfully())
					{
						worker.loadErrorMessage = loadedTagContext.tagFile.GetLoadError();
						return;
					}
					if (worker.GetCancellationSource().IsCancellationRequested)
					{
						return;
					}

					TextTagUpdateFilter textTagUpdateFilter = new TextTagUpdateFilter
					{
						loadedTag = loadedTagContext
					};
					MarkCoverTargets(textTagUpdateFilter);
					MarkLyricTargets(textTagUpdateFilter);
					worker.GetTextTagMatchKeys().ForEachWhile(textTagUpdateFilter.loadedTag.MarkTextTagNeededIfMissing);

					Dictionary<string, object> searchResults = worker.SearchAutoMatchMetadata(textTagUpdateFilter.loadedTag.tagFile, worker.shouldSaveLyricToTag || worker.shouldSaveLyricToFile, worker.shouldSaveCoverToTag || worker.shouldSaveCoverToFile, worker.shouldUpdateTextTags);
					if (worker.GetCancellationSource().IsCancellationRequested)
					{
						return;
					}
					ApplySearchResults(searchResults, textTagUpdateFilter);
				}
				catch (Exception ex)
				{
					worker.loadErrorMessage = string.IsNullOrWhiteSpace(ex.Message) ? Resources.Msg_SaveFail : ex.Message;
				}
				finally
				{
					loadedTagContext.Dispose();
					FinishSearch();
				}
			}

			private void MarkCoverTargets(TextTagUpdateFilter textTagUpdateFilter)
			{
				if (!worker.MatchConditionSettings.TryGetValue("cover", out (string writeMode, bool overwrite) coverSetting))
				{
					return;
				}
				if (coverSetting.writeMode == "SaveToTag" || coverSetting.writeMode == "SaveToTagAndFile")
				{
					object hasPicture = textTagUpdateFilter.loadedTag.tagFile["haspicture"];
					if ((hasPicture is bool && !(bool)hasPicture) || coverSetting.overwrite)
					{
						worker.shouldSaveCoverToTag = true;
					}
				}
				if ((coverSetting.writeMode == "SaveToFile" || coverSetting.writeMode == "SaveToTagAndFile") && (DatabaseMapper.FindExistingSiblingImageFile(filePath) == null || coverSetting.overwrite))
				{
					worker.shouldSaveCoverToFile = true;
				}
			}

			private void MarkLyricTargets(TextTagUpdateFilter textTagUpdateFilter)
			{
				if (!worker.MatchConditionSettings.TryGetValue("lyrics", out (string writeMode, bool overwrite) lyricSetting))
				{
					return;
				}
				if (lyricSetting.writeMode == "SaveToTag" || lyricSetting.writeMode == "SaveToTagAndFile")
				{
					bool hasBlankEmbeddedLyrics = textTagUpdateFilter.loadedTag.tagFile["lyrics"] is string embeddedLyrics && string.IsNullOrWhiteSpace(embeddedLyrics);
					if (hasBlankEmbeddedLyrics || lyricSetting.overwrite)
					{
						worker.shouldSaveLyricToTag = true;
					}
				}
				if ((lyricSetting.writeMode == "SaveToFile" || lyricSetting.writeMode == "SaveToTagAndFile") && (DatabaseMapper.FindExistingLyricFile(filePath, textTagUpdateFilter.loadedTag.tagFile, allowLocalFallback: false) == null || lyricSetting.overwrite))
				{
					worker.shouldSaveLyricToFile = true;
				}
			}

			private void ApplySearchResults(Dictionary<string, object> searchResults, TextTagUpdateFilter textTagUpdateFilter)
			{
				if (searchResults.TryGetValue("coverFile", out object coverFileValue) && coverFileValue is string coverFilePath)
				{
					worker.downloadedCoverFilePath = coverFilePath;
				}
				else
				{
					worker.shouldSaveCoverToTag = false;
					worker.shouldSaveCoverToFile = false;
				}
				if (searchResults.TryGetValue("lyric", out object lyricValue) && lyricValue is string lyricText && !string.IsNullOrWhiteSpace(lyricText))
				{
					worker.downloadedLyricText = lyricText;
				}
				else
				{
					worker.shouldSaveLyricToTag = false;
					worker.shouldSaveLyricToFile = false;
				}
				if (searchResults.TryGetValue("textTags", out object textTagsValue))
				{
					textTagUpdateFilter.candidateTextTags = textTagsValue as Dictionary<string, object>;
					if (textTagUpdateFilter.candidateTextTags != null)
					{
						worker.GetTextTagMatchKeys().ForEachItem(textTagUpdateFilter.RemoveUnchangedOrBlockedField);
						if (worker.GetTextTagMatchKeys().Any())
						{
							worker.textTagUpdates = textTagUpdateFilter.candidateTextTags;
							return;
						}
					}
				}
				worker.shouldUpdateTextTags = false;
			}

			private void FinishSearch()
			{
				worker.GetActiveFilePathMap().TryRemove(filePath, out var _);
				if (worker.IsParallelWorker())
				{
					WaitForProcessorQueueSlot();
					new AutoMatchWorker(worker.GetOwnerDialog(), isParallelWorker: true, worker.GetCancellationSource(), worker.CanCancelReadonlyFile());
				}
				worker.activeWorkerCounter.ReleaseAndIsIdle();
			}

			private void WaitForProcessorQueueSlot()
			{
				while (!worker.GetCancellationSource().IsCancellationRequested)
				{
					lock (worker.GetProcessorQueue())
					{
						if (worker.GetProcessorQueue().IsEmpty)
						{
							worker.GetProcessorQueue().Enqueue(worker);
							Monitor.Pulse(worker.GetProcessorQueue());
							return;
						}
					}
					object processorQueueSignal = worker.GetProcessorQueueSignal();
					bool lockTaken = false;
					try
					{
						Monitor.Enter(processorQueueSignal, ref lockTaken);
						Monitor.Wait(processorQueueSignal, 50);
					}
					finally
					{
						if (lockTaken)
						{
							Monitor.Exit(processorQueueSignal);
						}
					}
				}
			}
		}

		private sealed class LoadedTagContext : IDisposable
		{
			public ConfigDescriptorState tagFile;

			public AutoMatchFileSearchTask searchTask;

			public void Dispose()
			{
				tagFile?.Dispose();
			}

			internal bool MarkTextTagNeededIfMissing(string fieldName)
			{
				bool overwrite = searchTask.worker.MatchConditionSettings[fieldName].overwrite;
				if (tagFile[fieldName] is string text && (string.IsNullOrWhiteSpace(text) || overwrite))
				{
					searchTask.worker.shouldUpdateTextTags = true;
					return false;
				}
				return true;
			}
		}

		private sealed class TextTagUpdateFilter
		{
			public Dictionary<string, object> candidateTextTags;

			public LoadedTagContext loadedTag;

			internal void RemoveUnchangedOrBlockedField(string fieldName)
			{
				bool overwrite = loadedTag.searchTask.worker.MatchConditionSettings[fieldName].overwrite;
				string newValue = candidateTextTags[fieldName] as string;
				string currentValue = loadedTag.tagFile[fieldName] as string;
				if (newValue == null || currentValue == null)
				{
					return;
				}
				if (string.IsNullOrWhiteSpace(newValue))
				{
					loadedTag.searchTask.worker.MatchConditionSettings.Remove(fieldName);
					return;
				}
				if (!string.IsNullOrWhiteSpace(currentValue) && !overwrite)
				{
					loadedTag.searchTask.worker.MatchConditionSettings.Remove(fieldName);
					return;
				}
				if (string.Equals(currentValue, newValue))
				{
					loadedTag.searchTask.worker.MatchConditionSettings.Remove(fieldName);
				}
			}
		}

		private static bool IsTextTagMatchKey(string fieldName)
		{
			return fieldName != "cover" && fieldName != "lyrics";
		}

		private static bool HasProcessingFailed(ConfigDescriptorState.PictureData coverData)
		{
			return coverData.ProcessingFailed;
		}

		private static bool IsYearFieldName(string fieldName)
		{
			return fieldName == "year";
		}

		private sealed class TagSaveContext
		{
			public ConfigDescriptorState tagFile;

			public AutoMatchWorker worker;

			internal void ApplyTextTagUpdate(string fieldName)
			{
				bool overwrite = worker.MatchConditionSettings[fieldName].overwrite;
				if (worker.textTagUpdates[fieldName] is string text && tagFile[fieldName] is string value && !string.IsNullOrEmpty(text) && (string.IsNullOrEmpty(value) || overwrite))
				{
					tagFile[fieldName] = text;
				}
			}
		}

		private sealed class MetadataSearchState
		{
			public Dictionary<SearchSource, int> remainingResultsBySource;

			public List<TrackSearchResult> candidateTracks;

			public TrackSearchContext searchContext;

			public int remainingGlobalResults;

			public List<TrackSearchResult> rankedTracks;

			public AutoMatchWorker worker;

			public Dictionary<string, object> resultValues;

			internal void InitializeSourceLimit(SourceItem sourceItem)
			{
				remainingResultsBySource[sourceItem.SearchSource] = sourceItem.GetEffectiveSearchResultLimit();
			}

			internal void AddRankedCandidates(bool useProviderRanking)
			{
				TrackResultLimiter resultLimiter = new TrackResultLimiter
				{
					searchState = this
				};
				if (!useProviderRanking)
				{
					CombinedTagSearchDialog.SortBySearchContextSimilarity(candidateTracks, searchContext);
				}
				else
				{
					CombinedTagSearchDialog.RankSearchResults(candidateTracks, searchContext);
				}
				resultLimiter.limitedResults = new List<TrackSearchResult>();
				candidateTracks.ForEach(resultLimiter.AddIfWithinLimit);
				rankedTracks.AddRange(resultLimiter.limitedResults);
			}

			internal bool IsPrimaryNetEaseSourceAvailable(SourceItem sourceItem)
			{
				if (sourceItem.Enabled && !sourceItem.IsSecondarySource && remainingResultsBySource[sourceItem.SearchSource] > 0)
				{
					return sourceItem.SearchSource == SearchSource.Music163;
				}
				return false;
			}

			internal void LoadDeferredLyric(TrackSearchResult searchResult)
			{
				LyricSearchResult lyricResult;
				while ((lyricResult = searchResult.LyricResult.DeferredLyricLoader(worker.GetCancellationSource())) != null)
				{
					if (lyricResult.HasDownloadableLyric())
					{
						resultValues.Add("lyric", lyricResult.GetFormattedLyricText());
					}
					break;
				}
			}

			internal bool DownloadCoverToTempFile(TrackSearchResult searchResult)
			{
				var (tempCoverPath, coverLease) = worker.coverTempFileCache.Reserve(searchResult.Cover.CoverUrl);
				bool lockTaken = default(bool);
				try
				{
					Monitor.Enter(coverLease, ref lockTaken);
					RemoteTagProviderBase.DownloadStatus currentStatus = coverLease.DownloadStatus;
					if (currentStatus == RemoteTagProviderBase.DownloadStatus.Success)
					{
						SetCoverFileResult(tempCoverPath);
						return true;
					}
					if (currentStatus == RemoteTagProviderBase.DownloadStatus.NotStarted)
					{
						(RemoteTagProviderBase.DownloadStatus downloadedStatus, long downloadedByteCount) = searchResult.Cover.CoverDownloader(worker.GetCancellationSource(), tempCoverPath, 100000);
						coverLease.DownloadStatus = downloadedStatus;
						if (coverLease.DownloadStatus == RemoteTagProviderBase.DownloadStatus.Success && downloadedByteCount > 0L)
						{
							SetCoverFileResult(tempCoverPath);
							return true;
						}
					}
				}
				finally
				{
					if (lockTaken)
					{
						Monitor.Exit(coverLease);
					}
				}
				worker.coverTempFileCache.Release(tempCoverPath);
				return false;
			}

			private void SetCoverFileResult(string tempCoverPath)
			{
				if (resultValues.TryGetValue("coverFile", out object previousCoverPath))
				{
					worker.coverTempFileCache.Release(previousCoverPath as string);
				}
				worker.tempCoverFilePath = tempCoverPath;
				resultValues["coverFile"] = tempCoverPath;
			}
			}

		private sealed class TrackResultLimiter
		{
			public List<TrackSearchResult> limitedResults;

			public MetadataSearchState searchState;

			internal void AddIfWithinLimit(TrackSearchResult searchResult)
			{
				if (searchState.remainingGlobalResults <= 0)
				{
					return;
				}
				if (searchState.remainingResultsBySource[searchResult.SearchSource] <= 0)
				{
					return;
				}
				limitedResults.Add(searchResult);
				searchState.remainingResultsBySource[searchResult.SearchSource]--;
				int remainingResults = searchState.remainingGlobalResults;
				searchState.remainingGlobalResults = remainingResults - 1;
			}
		}

		private sealed class LyricSearchState
		{
			private List<LyricSearchResult> limitedResults;

			public Dictionary<SourceItem, int> remainingResultsBySourceItem;

			public int remainingGlobalResults;

			internal void InitializeSourceLimit(SourceItem sourceItem)
			{
				remainingResultsBySourceItem[sourceItem] = sourceItem.GetEffectiveSearchResultLimit();
			}

			internal void AddLimitedLyricResults(SourceItem sourceItem, List<LyricSearchResult> candidates, List<LyricSearchResult> accumulatedResults)
			{
				limitedResults = candidates.Take(Math.Min(Math.Min(remainingGlobalResults, candidates.Count), remainingResultsBySourceItem[sourceItem])).ToList();
				accumulatedResults.AddRange(limitedResults);
				remainingGlobalResults -= limitedResults.Count;
				remainingResultsBySourceItem[sourceItem] -= limitedResults.Count;
			}

			internal bool IsNetEaseSourceAvailable(SourceItem sourceItem)
			{
				if (sourceItem.Enabled && remainingResultsBySourceItem[sourceItem] > 0)
				{
					return GetSourceFromItem(sourceItem) == SearchSource.Music163;
				}
				return false;
			}
			internal static SearchSource GetSourceFromItem(object sourceItem)
			{
				return ((SourceItem)sourceItem).SearchSource;
			}
		}

		private sealed class LyricSourceMatchPredicate
		{
			public TrackSearchResult trackResult;

			internal bool MatchesTrackSource(SourceItem sourceItem)
			{
				return sourceItem.SearchSource == trackResult.SearchSource;
			}
		}

		private readonly AutoMatchTagsDialog ownerDialog;

		private readonly Dictionary<string, (string writeMode, bool overwrite)> matchConditionSettings = new Dictionary<string, (string, bool)>();

		private readonly ConcurrentDictionary<string, bool> activeFilePathMap;

		private readonly ActiveWorkerCounter activeWorkerCounter;

		private readonly bool isParallelWorker;

		private readonly ConcurrentQueue<AutoMatchWorker> processorQueue;

		private readonly object processorQueueSignal;

		private readonly CancellationTokenSource cancellationSource;

		private readonly CoverTempFileCache coverTempFileCache;

		private string currentFilePath;

		private string loadErrorMessage;

		private bool shouldSaveCoverToTag;

		private bool shouldSaveCoverToFile;

		private bool shouldSaveLyricToTag;

		private bool shouldSaveLyricToFile;

		private bool shouldUpdateTextTags;

		private string tempCoverFilePath;

		private string downloadedCoverFilePath;

		private string downloadedLyricText;

		private string lyricTitle;

		private string lyricArtist;

		private ConfigDescriptorState originalTagSnapshot;

		private Dictionary<string, object> textTagUpdates;

		private readonly bool canCancelReadonlyFile;

		private Dictionary<string, (string writeMode, bool overwrite)> MatchConditionSettings
		{
			get
			{
				return matchConditionSettings;
			}
		}

		private AutoMatchTagsDialog GetOwnerDialog()
		{
			return ownerDialog;
		}

		private ConcurrentDictionary<string, bool> GetActiveFilePathMap()
		{
			return activeFilePathMap;
		}

		private bool IsParallelWorker()
		{
			return isParallelWorker;
		}

		private ConcurrentQueue<AutoMatchWorker> GetProcessorQueue()
		{
			return processorQueue;
		}

		private object GetProcessorQueueSignal()
		{
			return processorQueueSignal;
		}

		private CancellationTokenSource GetCancellationSource()
		{
			return cancellationSource;
		}

		public string GetCurrentFilePath()
		{
			return currentFilePath;
		}

		private void SetCurrentFilePath(string filePath)
		{
			currentFilePath = filePath;
		}

		private bool CanCancelReadonlyFile()
		{
			return canCancelReadonlyFile;
		}

		public AutoMatchWorker(AutoMatchTagsDialog owner, bool isParallelWorker, CancellationTokenSource cancellationSource, bool canCancelReadonlyFile)
		{
			this.isParallelWorker = isParallelWorker;
			ownerDialog = owner;
			MatchConditionSettings.AddEntriesFrom(owner.SelectedMatchConditions);
			activeFilePathMap = owner.activeFilePaths;
			activeWorkerCounter = owner.activeWorkerCounter;
			processorQueue = owner.parallelProcessorQueue;
			processorQueueSignal = owner.processorQueueSignal;
			coverTempFileCache = owner.coverTempFileCache;
			this.cancellationSource = cancellationSource;
			this.canCancelReadonlyFile = canCancelReadonlyFile;
			activeWorkerCounter.Increment();
			string filePath;
			if ((filePath = owner.pendingPathQueue.GetNextPath()) != null)
			{
				StartProcessingFile(filePath);
			}
			else
			{
				activeWorkerCounter.ReleaseAndIsIdle();
			}
		}

		private void StartProcessingFile(string filePath)
		{
			AutoMatchFileSearchTask searchTask = new AutoMatchFileSearchTask
			{
				worker = this,
				filePath = filePath
			};
			SetCurrentFilePath(searchTask.filePath);

			ThreadStart runSearch = searchTask.RunSearch;
			if (!IsParallelWorker())
			{
				runSearch();
				return;
			}

			Thread searchThread = new Thread(runSearch);
			searchThread.Priority = GetOwnerDialog().hasStartedParallelWorker ? ThreadPriority.BelowNormal : ThreadPriority.Normal;
			searchThread.Start();
			GetOwnerDialog().hasStartedParallelWorker = true;
		}

		public void ProcessCurrentFile()
		{
			if (GetCancellationSource().IsCancellationRequested)
			{
				return;
			}
			if (loadErrorMessage == null)
			{
				ConfigDescriptorState.PictureData downloadedCoverPicture = null;
				if (downloadedCoverFilePath != null)
				{
					downloadedCoverPicture = LoadDownloadedCoverPicture();
				}
				if (downloadedCoverPicture == null)
				{
					shouldSaveCoverToTag = false;
					shouldSaveCoverToFile = false;
				}
				coverTempFileCache.Release(tempCoverFilePath);
				if (!shouldSaveLyricToTag && !shouldSaveCoverToTag && !shouldUpdateTextTags)
				{
					if (!shouldSaveCoverToFile && !shouldSaveLyricToFile)
					{
						GetOwnerDialog().skippedCount++;
					}
					else
					{
						bool allFileSavesSucceeded = true;
						string coverSaveError;
						if (shouldSaveCoverToFile && (coverSaveError = SaveCoverToFile(downloadedCoverPicture)) != null)
						{
							allFileSavesSucceeded = false;
							RecordAutoMatchError(coverSaveError);
						}
						string lyricSaveError;
						if (shouldSaveLyricToFile && (lyricSaveError = SaveLyricToFile()) != null)
						{
							allFileSavesSucceeded = false;
							RecordAutoMatchError(lyricSaveError);
						}
						if (allFileSavesSucceeded)
						{
							GetOwnerDialog().successCount++;
						}
						else
						{
							GetOwnerDialog().failedCount++;
						}
					}
				}
				else
				{
					string tagSaveError;
					if ((tagSaveError = SaveTagsToFile(shouldSaveCoverToTag ? downloadedCoverPicture : null)) != null)
					{
						RecordAutoMatchError(tagSaveError);
						GetOwnerDialog().failedCount++;
					}
					else
					{
						GetOwnerDialog().successCount++;
					}
					string coverSaveError;
					if (shouldSaveCoverToFile && (coverSaveError = SaveCoverToFile(downloadedCoverPicture)) != null)
					{
						RecordAutoMatchError(coverSaveError);
					}
					string lyricSaveError;
					if (shouldSaveLyricToFile && (lyricSaveError = SaveLyricToFile()) != null)
					{
						RecordAutoMatchError(lyricSaveError);
					}
				}
			}
			else
			{
				RecordAutoMatchError(loadErrorMessage);
				GetOwnerDialog().failedCount++;
			}
			GetOwnerDialog().processedCount++;
		}

		private IEnumerable<string> GetTextTagMatchKeys()
		{
			return MatchConditionSettings.Keys.Where(IsTextTagMatchKey).ToList();
		}

		private void RecordAutoMatchError(string errorMessage)
		{
			string message = string.IsNullOrWhiteSpace(errorMessage) ? Resources.Msg_SaveFail : errorMessage;
			DatabaseMapper.WriteAutoMatchLog(GetCurrentFilePath() + ": " + message);
			GetOwnerDialog().autoMatchLog.AddLine(Path.GetFileName(GetCurrentFilePath()));
			GetOwnerDialog().autoMatchLog.AddLine(message);
		}

		private ConfigDescriptorState LoadCurrentTagFile()
		{
			AutoMatchTagsDialog owner = GetOwnerDialog();
			bool lockTaken = false;
			try
			{
				Monitor.Enter(owner, ref lockTaken);
				ConfigDescriptorState configDescriptorState = new ConfigDescriptorState(GetCurrentFilePath());
				if (configDescriptorState.IsLoadedSuccessfully())
				{
					CaptureLyricFileNameParts(configDescriptorState);
					configDescriptorState.LoadLyrics();
					configDescriptorState.LoadAudioProperties();
					configDescriptorState.LoadAllPictures();
					originalTagSnapshot = TagHistoryRepository.CreateTagSnapshot(configDescriptorState, includePictures: true);
				}
				return configDescriptorState;
			}
			finally
			{
				if (lockTaken)
				{
					Monitor.Exit(owner);
				}
			}
		}

		private void CaptureLyricFileNameParts(ConfigDescriptorState tagFile)
		{
			tagFile.LoadBasicTagFields();
			lyricTitle = tagFile["title"] as string;
			lyricArtist = tagFile["artist"] as string;
		}

		private bool AddHistoryAndUndoRecord(ConfigDescriptorState tagFile)
		{
			if (!tagFile.SaveTagFields())
			{
				return false;
			}
			lyricTitle = tagFile["title"] as string;
			lyricArtist = tagFile["artist"] as string;
			var (text, selection) = TagHistoryRepository.AddHistoryRecordIfChanged(GetCurrentFilePath(), originalTagSnapshot, tagFile, GetOwnerDialog().tagHistoryTransaction);
			if (text != null)
			{
				RecordAutoMatchError(text);
			}
			string undoError = TagHistoryRepository.AddUndoRecord(originalTagSnapshot, selection, GetOwnerDialog().tagHistoryTransaction);
			if (undoError != null)
			{
				RecordAutoMatchError(undoError);
			}
			return true;
		}

		private string SaveTagsToFile(ConfigDescriptorState.PictureData coverPicture)
		{
			FileInfo fileInfo = new FileInfo(GetCurrentFilePath());
			DatabaseMapper.ClearReadOnlyIfAllowed(fileInfo, CanCancelReadonlyFile());
			DateTime lastWriteTime = fileInfo.LastWriteTime;
			string saveError;
			TagSaveContext tagSaveContext = new TagSaveContext();
			tagSaveContext.worker = this;
			tagSaveContext.tagFile = new ConfigDescriptorState(GetCurrentFilePath());
			try
			{
				if (!tagSaveContext.tagFile.IsLoadedSuccessfully())
				{
					saveError = Resources.Msg_SaveFail;
				}
				else
				{
					CaptureLyricFileNameParts(tagSaveContext.tagFile);
					tagSaveContext.tagFile.LoadLyrics();
					if (shouldSaveLyricToTag && !string.IsNullOrWhiteSpace(downloadedLyricText))
					{
						tagSaveContext.tagFile["lyrics"] = downloadedLyricText;
					}
					if (coverPicture != null)
					{
						List<ConfigDescriptorState.PictureData> map = new List<ConfigDescriptorState.PictureData> { coverPicture };
						tagSaveContext.tagFile["allpicturedata"] = map;
					}
					if (textTagUpdates != null)
					{
						GetTextTagMatchKeys().ForEachItem(tagSaveContext.ApplyTextTagUpdate);
					}
					if (AddHistoryAndUndoRecord(tagSaveContext.tagFile))
					{
						if (Settings.Default.SaveLrcWhileSaveTags && !string.IsNullOrWhiteSpace(downloadedLyricText))
						{
							if (shouldSaveLyricToTag && !shouldSaveLyricToFile)
							{
								SaveLyricToFile();
							}
						}
						saveError = null;
					}
					else
					{
						saveError = tagSaveContext.tagFile.GetLoadError() ?? Resources.Msg_SaveFail;
					}
				}
			}
			finally
			{
				if (tagSaveContext.tagFile != null)
				{
					((IDisposable)tagSaveContext.tagFile).Dispose();
				}
			}
			if (saveError == null && Settings.Default.SaveTagsKeepUpdateTime)
			{
				try
				{
					fileInfo.LastWriteTime = lastWriteTime;
				}
				catch (Exception ex)
				{
					RecordAutoMatchError(ex.Message);
				}
			}
			return saveError;
		}

		private string SaveCoverToFile(ConfigDescriptorState.PictureData picture)
		{
			try
			{
				string imageExtension = DatabaseMapper.GetImageExtensionForMimeType(picture.MimeType, ".jpg");
				string existingCoverPath = DatabaseMapper.FindExistingSiblingImageFile(GetCurrentFilePath());
				string newCoverPath = DatabaseMapper.GetSiblingPathWithExtension(GetCurrentFilePath(), imageExtension);
				bool shouldDeleteOriginalCover = existingCoverPath != null && newCoverPath != existingCoverPath;
				File.WriteAllBytes(newCoverPath, picture.ImageBytes);
				if (shouldDeleteOriginalCover)
				{
					File.Delete(existingCoverPath);
				}
				return null;
			}
			catch (Exception ex)
			{
				return Resources.Msg_WriteCoverFileFail + ", " + ex.Message;
			}
		}

		private string SaveLyricToFile()
		{
			try
			{
				File.WriteAllText(DatabaseMapper.BuildLyricSavePath(GetCurrentFilePath(), lyricTitle, lyricArtist), downloadedLyricText, Encoding.GetEncoding(Settings.Default.SaveLrcFileDefaultEncoding));
				return null;
			}
			catch (Exception ex)
			{
				return Resources.Msg_WriteLrcFileFail + ", " + ex.Message;
			}
		}

		private ConfigDescriptorState.PictureData LoadDownloadedCoverPicture()
		{
			try
			{
				byte[] imageBytes = File.ReadAllBytes(downloadedCoverFilePath);
				List<ConfigDescriptorState.PictureData> pictures = new List<ConfigDescriptorState.PictureData>
				{
					new ConfigDescriptorState.PictureData
					{
						ImageBytes = imageBytes,
						PictureType = "Front Cover"
					}
				};
				StateFieldInstance.CompressPictures(pictures, useRestoreLimits: false);
				if (!pictures.Exists(HasProcessingFailed))
				{
					return pictures[0];
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine("CompressCover error:" + ex.Message);
			}
			return null;
		}

		private Dictionary<string, object> SearchAutoMatchMetadata(ConfigDescriptorState tagFile, bool shouldSearchLyrics, bool shouldSearchCover, bool shouldSearchTextTags)
		{
			MetadataSearchState metadataSearch = new MetadataSearchState();
			metadataSearch.worker = this;
			metadataSearch.resultValues = new Dictionary<string, object>();
			if (!shouldSearchLyrics && !shouldSearchCover && !shouldSearchTextTags)
			{
				return metadataSearch.resultValues;
			}
			if (!shouldSearchCover && !shouldSearchTextTags)
			{
				return SearchFallbackLyrics(tagFile, int.MaxValue);
			}
			metadataSearch.searchContext = new TrackSearchContext(tagFile);
			if (!metadataSearch.searchContext.HasTitle())
			{
				return metadataSearch.resultValues;
			}
			metadataSearch.remainingGlobalResults = DatabaseMapper.GetWebSearchResultLimit();
			metadataSearch.remainingResultsBySource = new Dictionary<SearchSource, int>();
			List<SourceItem> tagSources = TrackSearchResult.GetSortedTagSourceSettings();
			metadataSearch.rankedTracks = new List<TrackSearchResult>();
			tagSources.ForEach(metadataSearch.InitializeSourceLimit);
			int sourceOrderIndex = 0;
			metadataSearch.candidateTracks = null;
			SourceItem netEaseSource;
			if (!metadataSearch.rankedTracks.Any() && !GetCancellationSource().IsCancellationRequested && metadataSearch.remainingGlobalResults > 0 && metadataSearch.searchContext.LinkedMusicMetadata.musicId > 0L && (netEaseSource = tagSources.Find(metadataSearch.IsPrimaryNetEaseSourceAvailable)) != null)
			{
				metadataSearch.candidateTracks = new List<TrackSearchResult>();
				metadataSearch.candidateTracks.AddRange(CombinedTagSearchDialog.SearchTracksFromSource(netEaseSource.SearchSource, useLinkedNetEaseId: false, metadataSearch.rankedTracks, sourceOrderIndex++, metadataSearch.searchContext, GetCancellationSource()));
				metadataSearch.AddRankedCandidates(useProviderRanking: true);
			}
			if (!metadataSearch.rankedTracks.Any() && !GetCancellationSource().IsCancellationRequested && metadataSearch.remainingGlobalResults > 0)
			{
				metadataSearch.candidateTracks = new List<TrackSearchResult>();
				foreach (SourceItem primarySource in tagSources)
				{
					if (!GetCancellationSource().IsCancellationRequested && primarySource.Enabled && !primarySource.IsSecondarySource && metadataSearch.remainingResultsBySource[primarySource.SearchSource] > 0)
					{
						metadataSearch.candidateTracks.AddRange(CombinedTagSearchDialog.SearchTracksFromSource(primarySource.SearchSource, useLinkedNetEaseId: false, metadataSearch.rankedTracks, sourceOrderIndex++, metadataSearch.searchContext, GetCancellationSource()));
					}
				}
				metadataSearch.AddRankedCandidates(useProviderRanking: true);
			}
			if (!metadataSearch.rankedTracks.Any() && !GetCancellationSource().IsCancellationRequested && metadataSearch.remainingGlobalResults > 0)
			{
				metadataSearch.candidateTracks = new List<TrackSearchResult>();
				foreach (SourceItem secondarySource in tagSources)
				{
					if (!GetCancellationSource().IsCancellationRequested && secondarySource.Enabled && secondarySource.IsSecondarySource && metadataSearch.remainingResultsBySource[secondarySource.SearchSource] > 0)
					{
						metadataSearch.candidateTracks.AddRange(CombinedTagSearchDialog.SearchTracksFromSource(secondarySource.SearchSource, useLinkedNetEaseId: false, metadataSearch.rankedTracks, sourceOrderIndex++, metadataSearch.searchContext, GetCancellationSource()));
					}
				}
				metadataSearch.AddRankedCandidates(useProviderRanking: false);
			}
			if (metadataSearch.rankedTracks.Any())
			{
				TrackSearchResult bestTrack = metadataSearch.rankedTracks[0];
				if (!GetCancellationSource().IsCancellationRequested && shouldSearchLyrics)
				{
					Action<TrackSearchResult> loadDeferredLyric = metadataSearch.LoadDeferredLyric;
					if (bestTrack.LyricResult != null)
					{
						loadDeferredLyric(bestTrack);
					}
					if (!GetCancellationSource().IsCancellationRequested && !metadataSearch.resultValues.ContainsKey("lyric"))
					{
						TrackSearchResult alternateTrack = ((metadataSearch.rankedTracks.Count > 1) ? metadataSearch.rankedTracks[1] : null);
						if (alternateTrack != null && alternateTrack.LyricResult != null && alternateTrack.Title == bestTrack.Title && alternateTrack.Artist == bestTrack.Artist && alternateTrack.Album == bestTrack.Album)
						{
							loadDeferredLyric(alternateTrack);
						}
					}
				}
				if (!GetCancellationSource().IsCancellationRequested && shouldSearchCover)
				{
					Func<TrackSearchResult, bool> downloadCoverToTempFile = metadataSearch.DownloadCoverToTempFile;
					if (bestTrack.Cover != null)
					{
						downloadCoverToTempFile(bestTrack);
					}
					if (!GetCancellationSource().IsCancellationRequested && !metadataSearch.resultValues.ContainsKey("coverFile"))
					{
						TrackSearchResult alternateTrack = ((metadataSearch.rankedTracks.Count > 1) ? metadataSearch.rankedTracks[1] : null);
						if (alternateTrack != null && alternateTrack.Cover != null && alternateTrack.Title == bestTrack.Title && alternateTrack.Artist == bestTrack.Artist && alternateTrack.Album == bestTrack.Album && downloadCoverToTempFile(alternateTrack))
						{
							bestTrack = alternateTrack;
						}
					}
				}
				if (!GetCancellationSource().IsCancellationRequested && shouldSearchTextTags)
				{
					if (string.IsNullOrWhiteSpace(bestTrack.Year) && GetTextTagMatchKeys().Any(IsYearFieldName))
					{
						bestTrack.Year = CombinedTagSearchDialog.FetchMissingNetEaseReleaseYear(bestTrack, GetCancellationSource());
					}
					metadataSearch.resultValues.Add("textTags", new Dictionary<string, object>
					{
						{
							"title",
							(bestTrack.Title ?? "").Trim()
						},
						{
							"artist",
							(bestTrack.Artist ?? "").Trim()
						},
						{
							"album",
							(bestTrack.Album ?? "").Trim()
						},
						{
							"year",
							(bestTrack.Year ?? "").Trim()
						},
						{ "track", bestTrack.Track },
						{
							"trackstr",
							(bestTrack.Track > 0) ? bestTrack.Track.ToString() : ""
						},
						{ "disc", bestTrack.Disc },
						{
							"discstr",
							(bestTrack.Disc > 0) ? bestTrack.Disc.ToString() : ""
						},
						{
							"genre",
							(bestTrack.Genre ?? "").Trim()
						},
						{
							"comment",
							(bestTrack.Comment ?? "").Trim()
						}
					});
				}
				if (!GetCancellationSource().IsCancellationRequested && shouldSearchLyrics && !metadataSearch.resultValues.ContainsKey("lyric") && !TrackSearchResult.IsInstrumentalTitle(DatabaseMapper.CoalesceNonBlank(metadataSearch.searchContext.Title).ToLower()))
				{
					metadataSearch.resultValues.AddEntriesFrom(SearchFallbackLyrics(tagFile, 3));
				}
			}
			return metadataSearch.resultValues;
		}

		private Dictionary<string, object> SearchFallbackLyrics(ConfigDescriptorState tagFile, int maxResults)
		{
			LyricSearchState lyricSearch = new LyricSearchState();
			Dictionary<string, object> resultValues = new Dictionary<string, object>();
			TrackSearchContext searchContext = new TrackSearchContext(tagFile);
			if (searchContext.HasTitle() && (!GetOwnerDialog().skipInstrumentalLyrics || !TrackSearchResult.IsInstrumentalTitle(DatabaseMapper.CoalesceNonBlank(searchContext.Title).ToLower())))
			{
				List<LyricSearchResult> lyricResults = new List<LyricSearchResult>();
				lyricSearch.remainingGlobalResults = DatabaseMapper.GetWebSearchResultLimit();
				lyricSearch.remainingResultsBySourceItem = new Dictionary<SourceItem, int>();
				List<SourceItem> lyricSources = LyricSearchResult.GetSortedLyricSourceSettings();
				lyricSources.ForEach(lyricSearch.InitializeSourceLimit);
				Action<SourceItem, List<LyricSearchResult>, List<LyricSearchResult>> addLimitedLyricResults = lyricSearch.AddLimitedLyricResults;
				SourceItem netEaseLyricSource;
				if (searchContext.LinkedMusicMetadata.musicId > 0L && (netEaseLyricSource = lyricSources.Find(lyricSearch.IsNetEaseSourceAvailable)) != null)
				{
					addLimitedLyricResults(netEaseLyricSource, LyricSearchDialog.SearchLyricsBySource(netEaseLyricSource.SearchSource, useKnownMusicId: true, searchContext, maxResults, lyricResults, 0, GetCancellationSource(), searchCandidateTracks: false), lyricResults);
				}
				if (!lyricResults.Any())
				{
					int searchPass = 0;
					if (lyricSearch.remainingGlobalResults < int.MaxValue)
					{
						List<TrackSearchResult> candidateTracks = new List<TrackSearchResult>();
						foreach (SourceItem lyricSource in lyricSources)
						{
							if (!GetCancellationSource().IsCancellationRequested && lyricSource.Enabled)
							{
								candidateTracks.AddRange(LyricSearchDialog.SearchTracksBySource(lyricSource.SearchSource, searchContext, searchPass++, GetCancellationSource(), fromCandidateSearch: false));
							}
						}
						foreach (TrackSearchResult candidateTrack in candidateTracks)
						{
							candidateTrack.UpdateSimilarityScores(searchContext.Title, searchContext.Artist, searchContext.Album);
						}
						TrackSearchResult.SortBySimilarity(candidateTracks);
						foreach (TrackSearchResult candidateTrack in candidateTracks)
						{
							LyricSourceMatchPredicate sourceMatch = new LyricSourceMatchPredicate();
							sourceMatch.trackResult = candidateTrack;
							SourceItem matchedLyricSource = LyricSearchResult.GetLyricSourceSettings().Find(sourceMatch.MatchesTrackSource);
							LyricSearchResult downloadedLyric;
							if (!GetCancellationSource().IsCancellationRequested && lyricSearch.remainingGlobalResults > 0 && lyricSearch.remainingResultsBySourceItem[matchedLyricSource] > 0 && (downloadedLyric = LyricSearchDialog.DownloadLyricBySource(sourceMatch.trackResult, searchContext, GetCancellationSource())) != null)
							{
								addLimitedLyricResults(matchedLyricSource, new List<LyricSearchResult> { downloadedLyric }, lyricResults);
							}
						}
					}
					else
					{
						foreach (SourceItem lyricSource in lyricSources)
						{
							if (!GetCancellationSource().IsCancellationRequested && lyricSource.Enabled && lyricSearch.remainingGlobalResults > 0 && lyricSearch.remainingResultsBySourceItem[lyricSource] > 0)
							{
								addLimitedLyricResults(lyricSource, LyricSearchDialog.SearchLyricsBySource(lyricSource.SearchSource, useKnownMusicId: false, searchContext, maxResults, lyricResults, searchPass++, GetCancellationSource(), searchCandidateTracks: false), lyricResults);
							}
						}
						foreach (LyricSearchResult lyricResult in lyricResults)
						{
							lyricResult.UpdateSimilarityScores(searchContext.Title, searchContext.Artist, searchContext.Album);
						}
						LyricSearchResult.SortLyricResults(lyricResults);
					}
				}
				if (!GetCancellationSource().IsCancellationRequested && lyricResults.Any())
				{
					LyricSearchResult lyricResult = lyricResults[0];
					if (lyricResult.DeferredLyricLoader != null)
					{
						lyricResult = lyricResult.DeferredLyricLoader(GetCancellationSource());
					}
					if (lyricResult != null && lyricResult.HasDownloadableLyric())
					{
						resultValues.Add("lyric", lyricResult.GetFormattedLyricText());
					}
				}
				return resultValues;
			}
			return resultValues;
		}

	}

	private sealed class MatchConditionListBuilder
	{
		private readonly AutoMatchTagsDialog owner;

		internal Dictionary<string, (string writeMode, bool overwrite)> SavedConditions;

		internal MatchConditionListBuilder(AutoMatchTagsDialog owner)
		{
			this.owner = owner;
		}

		internal void UpdateOverwriteHeader()
		{
			if (owner.tagListView.Items.Cast<ListViewItem>().Any(IsOverwriteDisabledForEditableMetadata))
			{
				return;
			}
			owner.tagListView.Columns[owner.overwriteColumn.Index].Tag = true;
			owner.tagListView.Invalidate(invalidateChildren: true);
		}

		internal void AddFieldRow(string fieldKey, bool defaultChecked)
		{
			MatchConditionRowControls rowControls = new MatchConditionRowControls(owner, fieldKey, UpdateOverwriteHeader);
			rowControls.WriteModeSubItem = new EmbeddedControlSubItem();
			rowControls.OverwriteSubItem = new EmbeddedControlSubItem();
			AssociatedValueListViewItem row = new AssociatedValueListViewItem("");
			row.SubItems.Add(Resources.ResourceManager.GetString(fieldKey)).Name = "name";
			row.SubItems.Add(rowControls.WriteModeSubItem).Name = "writemode";
			row.SubItems.Add(rowControls.OverwriteSubItem).Name = "overwrite";
			owner.tagListView.Items.Add(row);
			rowControls.WriteModeComboBox = new ComboBox
			{
				DropDownStyle = ComboBoxStyle.DropDownList
			};
			owner.tagListView.AttachEmbeddedControl(rowControls.WriteModeComboBox, rowControls.WriteModeSubItem);
			rowControls.OverwriteCheckBox = new CheckBox
			{
				CheckAlign = System.Drawing.ContentAlignment.MiddleCenter
			};
			owner.tagListView.AttachEmbeddedControl(rowControls.OverwriteCheckBox, rowControls.OverwriteSubItem);
			AddWriteModeOptions(fieldKey, rowControls.WriteModeComboBox);
			if (SavedConditions != null && SavedConditions.TryGetValue(fieldKey, out var savedCondition))
			{
				row.Checked = true;
				rowControls.WriteModeComboBox.SelectedItem = owner.writeModeValueByDisplayText.First((KeyValuePair<string, string> option) => option.Value == savedCondition.writeMode).Key;
				rowControls.OverwriteCheckBox.Checked = savedCondition.overwrite;
			}
			else
			{
				row.Checked = defaultChecked;
				rowControls.WriteModeComboBox.SelectedIndex = 0;
			}
			row.Tag = fieldKey;
			rowControls.WriteModeSubItem.Tag = owner.writeModeValueByDisplayText[rowControls.WriteModeComboBox.SelectedItem.ToString()];
			rowControls.OverwriteSubItem.Tag = rowControls.OverwriteCheckBox.Checked;
			rowControls.WriteModeComboBox.SelectedIndexChanged += rowControls.WriteModeComboBoxSelectedIndexChanged;
			rowControls.OverwriteCheckBox.CheckedChanged += rowControls.OverwriteCheckBoxCheckedChanged;
		}

		private static void AddWriteModeOptions(string fieldKey, ComboBox writeModeComboBox)
		{
			writeModeComboBox.Items.Add(Resources.SaveToTag);
			if (fieldKey == "cover" || fieldKey == "lyrics")
			{
				writeModeComboBox.Items.Add(Resources.SaveToFile);
			}
			if (fieldKey == "cover")
			{
				writeModeComboBox.Items.Add(Resources.SaveToTagAndFile);
			}
		}

		private static bool IsOverwriteDisabledForEditableMetadata(ListViewItem item)
		{
			return !(bool)item.SubItems["overwrite"].Tag && item.Tag as string != "title" && item.Tag as string != "artist";
		}
	}

	private sealed class MatchConditionRowControls
	{
		private readonly AutoMatchTagsDialog owner;

		private readonly string fieldKey;

		private readonly Action updateOverwriteHeader;

		internal EmbeddedControlSubItem WriteModeSubItem;

		internal ComboBox WriteModeComboBox;

		internal EmbeddedControlSubItem OverwriteSubItem;

		internal CheckBox OverwriteCheckBox;

		internal MatchConditionRowControls(AutoMatchTagsDialog owner, string fieldKey, Action updateOverwriteHeader)
		{
			this.owner = owner;
			this.fieldKey = fieldKey;
			this.updateOverwriteHeader = updateOverwriteHeader;
		}

		internal void WriteModeComboBoxSelectedIndexChanged(object sender, EventArgs args)
		{
			WriteModeSubItem.Tag = owner.writeModeValueByDisplayText[WriteModeComboBox.SelectedItem.ToString()];
		}

		internal void OverwriteCheckBoxCheckedChanged(object sender, EventArgs args)
		{
			OverwriteSubItem.Tag = OverwriteCheckBox.Checked;
			if (OverwriteCheckBox.Checked)
			{
				updateOverwriteHeader();
				return;
			}
			if (fieldKey != "title" && fieldKey != "artist")
			{
				owner.tagListView.Columns[owner.overwriteColumn.Index].Tag = false;
				owner.tagListView.Invalidate(invalidateChildren: true);
			}
		}
	}

	private sealed class AutoMatchTagsWorker
	{
		private readonly AutoMatchTagsDialog owner;

		private readonly ProgressDialog progressDialog;

		private readonly string[] paths;

		private readonly bool canCancelReadonlyFile;

		private readonly CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

		private string currentFileName;

		internal AutoMatchTagsWorker(AutoMatchTagsDialog owner, string[] paths, ProgressDialog progressDialog, bool canCancelReadonlyFile)
		{
			this.owner = owner;
			this.paths = paths;
			this.progressDialog = progressDialog;
			this.canCancelReadonlyFile = canCancelReadonlyFile;
		}

		internal CancellationToken CancellationToken
		{
			get
			{
				return cancellationTokenSource.Token;
			}
		}

		internal void RegisterProgressCallbacks()
		{
			progressDialog.AddCancelRequestedHandler(Cancel);
			progressDialog.AddProgressUpdateHandler(UpdateProgress);
		}

		internal void Run()
		{
			owner.pendingPathQueue = new FilePathQueue(paths);
			owner.tagHistoryTransaction = new TagHistoryRepository(useTransaction: true);
			TagHistoryRepository.ClearUndoState();
			try
			{
				if (owner.webSearchThreadCount > 1)
				{
					for (int workerIndex = 0; workerIndex < owner.webSearchThreadCount; workerIndex++)
					{
						new AutoMatchWorker(owner, isParallelWorker: true, cancellationTokenSource, canCancelReadonlyFile);
					}
					ProcessParallelQueue();
					return;
				}
				ProcessSequentially();
			}
			finally
			{
				owner.tagHistoryTransaction.Dispose();
			}
		}

		private void Cancel()
		{
			if (!cancellationTokenSource.IsCancellationRequested)
			{
				cancellationTokenSource.Cancel();
			}
		}

		private void UpdateProgress()
		{
			if (currentFileName != null)
			{
				progressDialog.UpdateStatisticsProgress(Resources.Msg_Savetag + currentFileName, owner.processedCount, paths.Length, owner.successCount, owner.failedCount, owner.skippedCount, paths.Length);
				return;
			}
			StringBuilder message = new StringBuilder(Resources.Msg_Searching);
			int pendingCount = owner.activeFilePaths.Count;
			string activeFilePath = owner.activeFilePaths.Keys.FirstOrDefault();
			if (activeFilePath != null)
			{
				message.Append(Path.GetFileName(activeFilePath));
				if (pendingCount > 1)
				{
					message.Append($"...({pendingCount})");
				}
			}
			progressDialog.UpdateStatisticsProgress(message.ToString(), owner.processedCount, paths.Length, owner.successCount, owner.failedCount, owner.skippedCount, paths.Length);
		}

		private void ProcessSequentially()
		{
			while (!cancellationTokenSource.IsCancellationRequested && owner.pendingPathQueue.HasMorePaths())
			{
				AutoMatchWorker processor = new AutoMatchWorker(owner, isParallelWorker: false, cancellationTokenSource, canCancelReadonlyFile);
				if (cancellationTokenSource.IsCancellationRequested)
				{
					break;
				}
				ProcessOne(processor);
			}
		}

		private void ProcessParallelQueue()
		{
			while (!cancellationTokenSource.IsCancellationRequested)
			{
				if (!owner.activeWorkerCounter.IsIdle())
				{
					if (owner.parallelProcessorQueue.IsEmpty)
					{
						WaitForQueuedProcessor();
						continue;
					}
				}
				else if (owner.parallelProcessorQueue.IsEmpty)
				{
					break;
				}
				if (!owner.parallelProcessorQueue.TryDequeue(out var processor))
				{
					continue;
				}
				lock (owner.processorQueueSignal)
				{
					Monitor.Pulse(owner.processorQueueSignal);
				}
				ProcessOne(processor);
			}
		}

		private void WaitForQueuedProcessor()
		{
			lock (owner.parallelProcessorQueue)
			{
				Monitor.Wait(owner.parallelProcessorQueue, 50);
			}
		}

		private void ProcessOne(AutoMatchWorker processor)
		{
			currentFileName = Path.GetFileName(processor.GetCurrentFilePath());
			processor.ProcessCurrentFile();
			currentFileName = null;
		}
	}

	private readonly Dictionary<string, (string writeMode, bool overwrite)> selectedMatchConditions;

	private int webSearchThreadCount;

	private bool skipInstrumentalLyrics;

	private FilePathQueue pendingPathQueue;

	private readonly ActiveWorkerCounter activeWorkerCounter;

	private readonly ConcurrentQueue<AutoMatchWorker> parallelProcessorQueue;

	private readonly ConcurrentDictionary<string, bool> activeFilePaths;

	private readonly object processorQueueSignal;

	private bool hasStartedParallelWorker;

	private volatile int successCount;

	private volatile int failedCount;

	private volatile int skippedCount;

	private volatile int processedCount;

	private readonly CoverTempFileCache coverTempFileCache;

	private readonly Page autoMatchLog;

	private readonly Dictionary<string, string> writeModeValueByDisplayText;

	private TagHistoryRepository tagHistoryTransaction;

	private IContainer components;

	private FlowLayoutPanel mainPanel;

	private MusicTagWinApp.Roles.EditableListView tagListView;

	private ColumnHeader checkColumn;

	private ColumnHeader itemColumn;

	private ColumnHeader overwriteColumn;

	private FlowLayoutPanel footerPanel;

	private FlowLayoutPanel buttonPanel;

	private Button okButton;

	private Button cancelButton;

	private ImageList rowImageList;

	private TrackBar webSearchThreadCountTrackBar;

	private Label webSearchThreadCountLabel;

	private ColumnHeader writeModeColumn;

	private CheckBox skipInstrumentalLyricsCheckBox;

	private Dictionary<string, (string writeMode, bool overwrite)> SelectedMatchConditions
	{
		get
		{
			return selectedMatchConditions;
		}
	}

	public AutoMatchTagsDialog()
	{
		selectedMatchConditions = new Dictionary<string, (string, bool)>();
		activeWorkerCounter = new ActiveWorkerCounter();
		parallelProcessorQueue = new ConcurrentQueue<AutoMatchWorker>();
		activeFilePaths = new ConcurrentDictionary<string, bool>();
		processorQueueSignal = new object();
		coverTempFileCache = new CoverTempFileCache();
		autoMatchLog = new Page();
		writeModeValueByDisplayText = new Dictionary<string, string>
		{
			{
				Resources.SaveToTag,
				"SaveToTag"
			},
			{
				Resources.SaveToFile,
				"SaveToFile"
			},
			{
				Resources.SaveToTagAndFile,
				"SaveToTagAndFile"
			}
		};
		InitializeComponent();
		LayoutControls();
		ApplyLocalizedText();
		InitializeMatchConditionList();
		webSearchThreadCountTrackBar.Value = Settings.Default.AutoMatchTagsWebSearchThreadCount;
		skipInstrumentalLyricsCheckBox.Checked = Settings.Default.DontDownloadLyricWithInstrumentInTitle;
		UpdateThreadCountLabel(null, null);
	}

	private void InitializeMatchConditionList()
	{
		MatchConditionListBuilder listBuilder = new MatchConditionListBuilder(this);
		rowImageList.ImageSize = new Size(1, DatabaseMapper.ScaleByDpi(40f));
		foreach (ColumnHeader column in tagListView.Columns)
		{
			column.Width = DatabaseMapper.ScaleByDpi(column.Width);
		}
		try
		{
			listBuilder.SavedConditions = JsonConvert.DeserializeObject<Dictionary<string, (string, bool)>>(Settings.Default.AutoMatchTagsCondition);
		}
		catch (Exception ex)
		{
			Console.WriteLine("DeserializeObject autoMatchTagsCondition fail " + ex.Message);
		}
		Action<string, bool> addMatchConditionRow = listBuilder.AddFieldRow;
		addMatchConditionRow("cover", true);
		addMatchConditionRow("lyrics", true);
		addMatchConditionRow("title", false);
		addMatchConditionRow("artist", false);
		addMatchConditionRow("album", false);
		addMatchConditionRow("year", false);
		addMatchConditionRow("trackstr", false);
		addMatchConditionRow("discstr", false);
		addMatchConditionRow("genre", false);
		addMatchConditionRow("comment", false);
		listBuilder.UpdateOverwriteHeader();
		string language = StateFieldInstance.CurrentLanguageCode;
		if (language != "zh-CHS" && language != "zh-CHT")
		{
			overwriteColumn.Width = DatabaseMapper.ScaleByDpi(80f);
		}
		else
		{
			overwriteColumn.Width = DatabaseMapper.ScaleByDpi(55f);
		}
	}

	private void ApplyLocalizedText()
	{
		Text = Resources.AutoMatchTags;
		okButton.Text = Resources.OK;
		cancelButton.Text = Resources.Cancel;
		itemColumn.Text = Resources.Item;
		writeModeColumn.Text = Resources.WriteMode;
		overwriteColumn.Text = Resources.overwrite;
		webSearchThreadCountLabel.Text = Resources.WebSearchThreadCount;
		skipInstrumentalLyricsCheckBox.Text = Resources.DontDownloadLyricWithInstrumentInTitle;
	}

	private void LayoutControls()
	{
		tagListView.Width = mainPanel.Width;
		tagListView.Height = mainPanel.Height - footerPanel.Height - webSearchThreadCountTrackBar.Height - webSearchThreadCountLabel.Height - webSearchThreadCountLabel.Margin.Top - skipInstrumentalLyricsCheckBox.Height - skipInstrumentalLyricsCheckBox.Margin.Top;
		webSearchThreadCountTrackBar.Width = mainPanel.Width - webSearchThreadCountTrackBar.Margin.Left - webSearchThreadCountTrackBar.Margin.Right;
		int left = (footerPanel.Width - buttonPanel.Width) / 2;
		buttonPanel.Margin = new Padding(left, buttonPanel.Margin.Top, 0, buttonPanel.Margin.Bottom);
	}

	private void MainPanelSizeChanged(object sender, EventArgs args)
	{
		LayoutControls();
	}

	private void DrawOverwriteColumnHeader(object sender, DrawListViewColumnHeaderEventArgs args)
	{
		if (args.ColumnIndex != overwriteColumn.Index)
		{
			return;
		}
		args.DrawDefault = false;
		args.DrawBackground();
		bool isChecked = args.Header.Tag is bool value && value;
		CheckBoxRenderer.DrawCheckBox(args.Graphics, new Point(args.Bounds.Left + 4, args.Bounds.Top + DatabaseMapper.ScaleByDpi(4f)), isChecked ? CheckBoxState.CheckedNormal : CheckBoxState.UncheckedNormal);
		int textX = args.Bounds.Left + DatabaseMapper.ScaleByDpi(16f) + 4;
		int textWidth = args.Bounds.Right - textX;
		if (textWidth <= 0)
		{
			return;
		}
		TextRenderer.DrawText(args.Graphics, args.Header.Text, args.Font, new Rectangle(textX, args.Bounds.Top, textWidth, args.Bounds.Height), args.ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
	}

	private void ToggleOverwriteColumnForAllRows(object sender, ColumnClickEventArgs args)
	{
		if (args.Column != overwriteColumn.Index)
		{
			return;
		}
		bool currentValue = tagListView.Columns[args.Column].Tag is bool value && value;
		bool newValue = !currentValue;
		tagListView.Columns[args.Column].Tag = newValue;
		foreach (ListViewItem item in tagListView.Items)
		{
			string fieldName = item.Tag as string;
			if (fieldName == null || !(item.SubItems["overwrite"] is EmbeddedControlSubItem embeddedControlSubItem) || !(embeddedControlSubItem.EmbeddedControl is CheckBox checkBox))
			{
				continue;
			}
			if (!newValue || (fieldName != "title" && fieldName != "artist"))
			{
				checkBox.Checked = newValue;
			}
		}
		tagListView.Invalidate();
	}

	private void OkButtonClick(object sender, EventArgs args)
	{
		SelectedMatchConditions.Clear();
		foreach (ListViewItem item in tagListView.Items.Cast<ListViewItem>().Where((ListViewItem item) => item.Checked))
		{
			AddSelectedMatchCondition(item);
		}
		if (!SelectedMatchConditions.Any())
		{
			DatabaseMapper.ShowErrorMessage(Resources.Msg_PleaseSelectAtLeastOneItem);
			return;
		}
		webSearchThreadCount = webSearchThreadCountTrackBar.Value;
		skipInstrumentalLyrics = skipInstrumentalLyricsCheckBox.Checked;
		Settings.Default.AutoMatchTagsWebSearchThreadCount = webSearchThreadCountTrackBar.Value;
		Settings.Default.DontDownloadLyricWithInstrumentInTitle = skipInstrumentalLyricsCheckBox.Checked;
		Settings.Default.AutoMatchTagsCondition = JsonConvert.SerializeObject(SelectedMatchConditions);
		if (!DatabaseMapper.TrySaveApplicationSettings())
		{
			return;
		}
		base.DialogResult = DialogResult.OK;
		Close();
	}

	private void CancelButtonClick(object sender, EventArgs args)
	{
		base.DialogResult = DialogResult.Cancel;
		Close();
	}

	private void UpdateThreadCountLabel(object sender, EventArgs args)
	{
		webSearchThreadCountLabel.Text = Resources.WebSearchThreadCount + webSearchThreadCountTrackBar.Value;
	}

	private void TagListViewItemChecked(object sender, ItemCheckedEventArgs args)
	{
		if (!args.Item.Checked)
		{
			tagListView.Columns[0].Tag = false;
			tagListView.Invalidate(invalidateChildren: true);
			return;
		}
		if (tagListView.Items.Cast<ListViewItem>().Any((ListViewItem item) => !item.Checked))
		{
			return;
		}
		tagListView.Columns[0].Tag = true;
		tagListView.Invalidate(invalidateChildren: true);
	}

	internal async void StartAutoMatchTags(string[] paths, ProgressDialog progressDialog, bool canCancelReadonlyFile, Action<(string msg, bool isErr)> finallyCallback)
	{
		AutoMatchTagsWorker worker = new AutoMatchTagsWorker(this, paths, progressDialog, canCancelReadonlyFile);
		worker.RegisterProgressCallbacks();
		(string msg, bool isErr) result = default((string, bool));
		try
		{
			await Task.Run((Action)worker.Run, worker.CancellationToken);
			if (paths.Length > 1)
			{
				result = CreateBatchAutoMatchResult();
			}
			else if (successCount > 0)
			{
				result = (Resources.Msg_SaveCompleted + "\n" + autoMatchLog.ToString(), false);
			}
			else if (skippedCount > 0)
			{
				result = (Resources.Msg_Skipped + "\n" + autoMatchLog.ToString(), false);
			}
			else
			{
				result = (autoMatchLog.ToString(), true);
			}
		}
		catch (Exception ex)
		{
			DatabaseMapper.WriteAutoMatchLog(ex.Message);
			autoMatchLog.AddLine(ex.Message);
			result = (autoMatchLog.ToString(), true);
		}
		finally
		{
			progressDialog.CloseAfterCompletion();
			finallyCallback(result);
		}
	}

	private (string msg, bool isErr) CreateBatchAutoMatchResult()
	{
		string message = string.Format(Resources.Msg_SaveCompleted + "\n" + Resources.Msg_OK_Fail_Skip_Count, successCount, failedCount, skippedCount, processedCount) + "\n" + autoMatchLog.ToString();
		return (message, false);
	}

	internal bool IsOnlyWriteFileModeSelected()
	{
		if (SelectedMatchConditions.Any())
		{
			return SelectedMatchConditions.Values.All((value) => value.writeMode == "SaveToFile");
		}
		return false;
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
		tagListView = new MusicTagWinApp.Roles.EditableListView();
		checkColumn = new ColumnHeader();
		itemColumn = new ColumnHeader();
		writeModeColumn = new ColumnHeader();
		overwriteColumn = new ColumnHeader();
		rowImageList = new ImageList(components);
		webSearchThreadCountLabel = new Label();
		webSearchThreadCountTrackBar = new TrackBar();
		skipInstrumentalLyricsCheckBox = new CheckBox();
		footerPanel = new FlowLayoutPanel();
		buttonPanel = new FlowLayoutPanel();
		okButton = new Button();
		cancelButton = new Button();
		mainPanel.SuspendLayout();
		((ISupportInitialize)webSearchThreadCountTrackBar).BeginInit();
		footerPanel.SuspendLayout();
		buttonPanel.SuspendLayout();
		SuspendLayout();
		mainPanel.Controls.Add(tagListView);
		mainPanel.Controls.Add(webSearchThreadCountLabel);
		mainPanel.Controls.Add(webSearchThreadCountTrackBar);
		mainPanel.Controls.Add(skipInstrumentalLyricsCheckBox);
		mainPanel.Controls.Add(footerPanel);
		mainPanel.Dock = DockStyle.Fill;
		mainPanel.FlowDirection = FlowDirection.TopDown;
		mainPanel.Location = new Point(0, 0);
		mainPanel.Name = "flowLayoutPanel1";
		mainPanel.Size = new Size(484, 581);
		mainPanel.TabIndex = 1;
		mainPanel.WrapContents = false;
		mainPanel.SizeChanged += MainPanelSizeChanged;
		tagListView.CheckBoxes = true;
		tagListView.Columns.AddRange(new ColumnHeader[4] { checkColumn, itemColumn, writeModeColumn, overwriteColumn });
		tagListView.EmbeddedControlInset = 4;
		tagListView.GridLines = true;
		tagListView.HideSelection = false;
		tagListView.Location = new Point(0, 0);
		tagListView.Margin = new Padding(0);
		tagListView.MultiSelect = false;
		tagListView.Name = "listView1";
		tagListView.OwnerDraw = true;
		tagListView.Size = new Size(475, 390);
		tagListView.SmallImageList = rowImageList;
		tagListView.TabIndex = 0;
		tagListView.UseCompatibleStateImageBehavior = false;
		tagListView.View = View.Details;
		tagListView.ColumnClick += ToggleOverwriteColumnForAllRows;
		tagListView.DrawColumnHeader += DrawOverwriteColumnHeader;
		tagListView.ItemChecked += TagListViewItemChecked;
		checkColumn.Text = "";
		checkColumn.Width = 25;
		itemColumn.Text = "Item";
		itemColumn.Width = 100;
		writeModeColumn.Text = "Write Mode";
		writeModeColumn.Width = 120;
		overwriteColumn.Text = "Overwrite";
		overwriteColumn.TextAlign = HorizontalAlignment.Center;
		overwriteColumn.Width = 80;
		rowImageList.ColorDepth = ColorDepth.Depth8Bit;
		rowImageList.ImageSize = new Size(1, 40);
		rowImageList.TransparentColor = Color.Transparent;
		webSearchThreadCountLabel.AutoSize = true;
		webSearchThreadCountLabel.Location = new Point(10, 400);
		webSearchThreadCountLabel.Margin = new Padding(10, 10, 0, 0);
		webSearchThreadCountLabel.Name = "lblWebSearchThreadCount";
		webSearchThreadCountLabel.Size = new Size(148, 14);
		webSearchThreadCountLabel.TabIndex = 9;
		webSearchThreadCountLabel.Text = "Web search thread count";
		webSearchThreadCountTrackBar.LargeChange = 1;
		webSearchThreadCountTrackBar.Location = new Point(10, 414);
		webSearchThreadCountTrackBar.Margin = new Padding(10, 0, 10, 0);
		webSearchThreadCountTrackBar.Maximum = 12;
		webSearchThreadCountTrackBar.Minimum = 1;
		webSearchThreadCountTrackBar.Name = "tbWebSearchThreadCount";
		webSearchThreadCountTrackBar.Size = new Size(455, 45);
		webSearchThreadCountTrackBar.TabIndex = 8;
		webSearchThreadCountTrackBar.TickStyle = TickStyle.TopLeft;
		webSearchThreadCountTrackBar.Value = 4;
		webSearchThreadCountTrackBar.ValueChanged += UpdateThreadCountLabel;
		skipInstrumentalLyricsCheckBox.Dock = DockStyle.Top;
		skipInstrumentalLyricsCheckBox.Location = new Point(10, 459);
		skipInstrumentalLyricsCheckBox.Margin = new Padding(10, 0, 0, 0);
		skipInstrumentalLyricsCheckBox.Name = "cbDontDownloadLyricWithInstrumentInTitle";
		skipInstrumentalLyricsCheckBox.Size = new Size(465, 40);
		skipInstrumentalLyricsCheckBox.TabIndex = 10;
		skipInstrumentalLyricsCheckBox.Text = "Don't download lyrics with keywords such as \"instrumental\", \"off vocal\", \"伴奏\", \"纯音乐\" in the title";
		skipInstrumentalLyricsCheckBox.UseVisualStyleBackColor = true;
		footerPanel.Controls.Add(buttonPanel);
		footerPanel.Dock = DockStyle.Fill;
		footerPanel.Location = new Point(0, 499);
		footerPanel.Margin = new Padding(0);
		footerPanel.Name = "flowLayoutPanel2";
		footerPanel.Size = new Size(475, 60);
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
		okButton.Click += OkButtonClick;
		cancelButton.Location = new Point(120, 0);
		cancelButton.Margin = new Padding(20, 0, 0, 0);
		cancelButton.Name = "btnCancel";
		cancelButton.Size = new Size(100, 35);
		cancelButton.TabIndex = 2;
		cancelButton.Text = "Cancel";
		cancelButton.UseVisualStyleBackColor = true;
		cancelButton.Click += CancelButtonClick;
		base.AutoScaleDimensions = new SizeF(96f, 96f);
		base.AutoScaleMode = AutoScaleMode.Dpi;
		base.ClientSize = new Size(484, 581);
		base.Controls.Add(mainPanel);
		Font = new Font("Tahoma", 9f, FontStyle.Regular, GraphicsUnit.Point, 0);
		base.MinimumSize = new Size(300, 300);
		base.Name = "FormAutoMatchTags";
		base.ShowIcon = false;
		base.StartPosition = FormStartPosition.CenterParent;
		Text = "Auto match tags";
		mainPanel.ResumeLayout(performLayout: false);
		mainPanel.PerformLayout();
		((ISupportInitialize)webSearchThreadCountTrackBar).EndInit();
		footerPanel.ResumeLayout(performLayout: false);
		buttonPanel.ResumeLayout(performLayout: false);
		ResumeLayout(performLayout: false);
	}

	private void AddSelectedMatchCondition(ListViewItem item)
	{
		SelectedMatchConditions.Add(item.Tag as string, (item.SubItems["writemode"].Tag as string, (bool)item.SubItems["overwrite"].Tag));
	}

}
