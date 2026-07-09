using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.Layout;
using MusicTag.Composer;
using MusicTag.Consumers;
using MusicTag.Importers;
using MusicTag.Mocks;
using MusicTag.Readers;
using MusicTag.Schemes;
using MusicTag.Serialization;
using MusicTag.Services;
using MusicTag.States;
using MusicTagWinApp.Adapter;
using MusicTagWinApp.Common;
using MusicTagWinApp.Containers;
using MusicTagWinApp.Exporters;
using MusicTagWinApp.Listeners;
using MusicTagWinApp.Properties;
using MusicTagWinApp.Roles;
using MusicTagWinApp.Structs;
using MusicTagWinApp.Web;
using Newtonsoft.Json;

namespace MusicTagWinApp.Instances;

internal partial class StateFieldInstance : Form
{
	private enum FileSelectionMode
	{
		SelectAll,
		UnselectAll,
		Invert,
		RefreshOnly
	}

	private class SelectedListViewItemInfo
	{
		public int Index;

		public string FilePath;
	}

	// 文件列表的一行数据 —— DataGridView VirtualMode 下的唯一数据真源(取代原先"用 ListViewItem 当数据容器")。
	private sealed class FileRow
	{
		public string FilePath;

		// 按 configuredColumnHeaders 的逻辑列序存各列显示文本(lyrics/comment 已按 20 字截断),
		// CellValueNeeded 据此向 DGV 供值,过滤/排序也消费它。
		public string[] CellTexts;

		// 时长(毫秒),状态栏汇总用;无有效值为 null。
		public int? DurationMs;

		// 备注(comment)完整长度,过滤长备注分组用。
		public int CommentFullLength;

		// 加载失败 —— CellFormatting 据此标红。
		public bool LoadFailed;

		// 文件类型图标键(扩展名),第一列 CellPainting 据此取图标。
		public string IconKey;

		// 文件类型图标对象缓存,避免滚动重绘时反复查 ImageList。
		public Image IconImage;

		// 过滤隐藏位:true 表示当前过滤条件下不进 visibleRows(不显示)。
		public bool IsHidden;

		// 选中态(模型真源)。DGV VirtualMode 的行选区会随过滤/排序重建而失效,
		// 故选中态以此字段为准,过滤/排序后据它恢复 DGV 行选区。
		public bool Selected;
	}

	private class ListViewItemNaturalComparer : IComparer
	{
		[Serializable]
		public class ListViewSortSetting
		{
			public int? Column { get; set; }

			public SortOrder SortOrder { get; set; }
		}

		private readonly ListViewSortSetting sortSetting;

		public ListViewItemNaturalComparer(ListViewSortSetting sortSetting)
		{
			this.sortSetting = sortSetting;
		}

		public int Compare(object left, object right)
		{
			FileRow leftItem = left as FileRow;
			FileRow rightItem = right as FileRow;
			int columnIndex = sortSetting.Column.Value;
			string leftText = leftItem.CellTexts[columnIndex];
			string rightText = rightItem.CellTexts[columnIndex];
			string columnName = configuredColumnHeaders[columnIndex].Name;
			return CompareColumnText(leftText, rightText, columnName, sortSetting.SortOrder);
		}
	}

	private sealed class TagEncodingLayoutContext
	{
		public ComboBox[] TagComboBoxes;

		public FlowLayoutPanel[] TagRows;

		public StateFieldInstance Owner;

		internal void UpdateLyricsComboWidth(object sender, EventArgs e)
		{
			Owner.lyricsComboBox.Width = Owner.lyricsRowPanel.Width - Owner.editLyricsButton.Width - Owner.editLyricsButton.Margin.Left - Owner.editLyricsButton.Margin.Right - Owner.lyricsEncodingButton.Width - Owner.lyricsEncodingButton.Margin.Left - Owner.lyricsEncodingButton.Margin.Right;
		}
	}

	private sealed class TagComboBoxWidthUpdater
	{
		public int RowIndex;

		public TagEncodingLayoutContext LayoutContext;

		internal void UpdateComboBoxWidth(object sender, EventArgs e)
		{
			Button button = LayoutContext.Owner.tagEncodingButtons[RowIndex];
			LayoutContext.TagComboBoxes[RowIndex].Width = LayoutContext.TagRows[RowIndex].Width - button.Width - button.Margin.Left - button.Margin.Right;
		}
	}

	private sealed class AddAnyFileCollector
	{
		public CancellationTokenSource CancellationTokenSource;

		public ProgressDialog ProgressDialog;

		public List<string> FilePaths;

		public IEnumerable<object> InputFileInfos;

		public string ErrorMessage;

		public void Cancel()
		{
			if (!CancellationTokenSource.IsCancellationRequested)
			{
				CancellationTokenSource.Cancel();
			}
		}

		public void ShowScanningProgress()
		{
			ProgressDialog.ShowIndeterminateProgress(Resources.Msg_CollectingData);
		}

		public void CollectFilePaths()
		{
			foreach (object inputItem in InputFileInfos)
			{
				if (CancellationTokenSource.IsCancellationRequested)
				{
					break;
				}

				if (inputItem is ListViewFileSettingFileInfo directoryInfo)
				{
					try
					{
						if (Directory.Exists(directoryInfo.DirPath) && !CollectDirectoryFiles(directoryInfo))
						{
							break;
						}
					}
					catch (System.Exception ex)
					{
						ErrorMessage = "Open folder " + directoryInfo.DirPath + " fail: " + ex.GetMessageChain();
						break;
					}
				}
				else if (inputItem is string path)
				{
					try
					{
						if (Directory.Exists(path))
						{
							var droppedDirectoryInfo = new ListViewFileSettingFileInfo
							{
								DirPath = path,
								IncludeSubDir = true
							};
							if (!CollectDirectoryFiles(droppedDirectoryInfo))
							{
								break;
							}
						}
						else if (!AddSupportedFile(path))
						{
							break;
						}
					}
					catch (System.Exception ex)
					{
						ErrorMessage = path + ": " + ex.GetMessageChain();
						break;
					}
				}
			}
		}

		private bool CollectDirectoryFiles(ListViewFileSettingFileInfo directoryInfo)
		{
			SearchOption searchOption = directoryInfo.IncludeSubDir ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
			foreach (string filePath in Directory.EnumerateFiles(directoryInfo.DirPath, "*.*", searchOption))
			{
				if (CancellationTokenSource.IsCancellationRequested)
				{
					return false;
				}

				if (!AddSupportedFile(filePath))
				{
					return false;
				}
			}

			return true;
		}

		private bool AddSupportedFile(string filePath)
		{
			foreach (string extension in EnabledTagTypesByExtension.Keys)
			{
				if (CancellationTokenSource.IsCancellationRequested)
				{
					return false;
				}

				if (filePath.ToLower().EndsWith(extension))
				{
					FilePaths.Add(filePath);
					break;
				}
			}

			return true;
		}

		public void ShowErrorMessage()
		{
			DialogService.ShowErrorMessage(ErrorMessage);
		}
	}

	private sealed class AddFilesWorker
	{
		public CancellationTokenSource CancellationTokenSource;

		public ProgressDialog ProgressDialog;

		public FileInfo CurrentFileInfo;

		public int CurrentIndex;

		public List<string> FileNames;

		public StateFieldInstance Owner;

		public Page LoadErrors;

		public HashSet<string> ExistingFilePaths;

		public Size FileIconSize;

		public IProgress<List<(ConfigDescriptorState TagFile, Dictionary<string, string> DisplayValues, string FilePath)>> LoadedFilesProgress;

		public void Cancel()
		{
			if (!CancellationTokenSource.IsCancellationRequested)
			{
				CancellationTokenSource.Cancel();
			}
		}

		public void UpdateProgress()
		{
			ProgressDialog.UpdateCountProgress(CurrentFileInfo?.Name, CurrentIndex, FileNames.Count);
		}

		public void AddLoadedFilesToListView(List<(ConfigDescriptorState TagFile, Dictionary<string, string> DisplayValues, string FilePath)> loadedFiles)
		{
			bool anyFileMode = Owner.FileSettings.IsAnyFileMode();
			var columns = configuredColumnHeaders;
			foreach (var loadedFile in loadedFiles)
			{
				ConfigDescriptorState tagFile = loadedFile.TagFile;
				Dictionary<string, string> displayValues = loadedFile.DisplayValues;
				string filePath = loadedFile.FilePath;
				string[] cellTexts = new string[columns.Count];
				int? durationMs = null;
				int commentFullLength = 0;
				int columnIndex = 0;
				foreach (CustomColumnsDialog.ColumnHeaderInfo column in columns)
				{
					if (!displayValues.TryGetValue(column.Name, out var value) && tagFile != null && tagFile.IsLoadedSuccessfully())
					{
						value = tagFile.GetDisplayValue(column.Name);
					}
					if (value == null)
					{
						value = "";
					}
					int fullValueLength = value.Length;
					value = TruncateLyricsOrCommentDisplayValue(column.Name, value);
					cellTexts[columnIndex] = value;
					if (column.Name == "durationinms" && value != "" && tagFile[column.Name] is int duration)
					{
						durationMs = duration;
					}
					if (column.Name == "comment")
					{
						commentFullLength = fullValueLength;
					}
					columnIndex++;
				}
				bool loadFailed = tagFile == null || !tagFile.IsLoadedSuccessfully();
				string iconKey = Path.GetExtension(filePath).ToLower();
				Image iconImage = Owner.GetFileTypeIcon(iconKey);
				Owner.cachedFileListItems.Add(new FileRow
				{
					IsHidden = false,
					Selected = false,
					FilePath = filePath,
					CellTexts = cellTexts,
					DurationMs = durationMs,
					CommentFullLength = commentFullLength,
					LoadFailed = loadFailed,
					IconKey = iconKey,
					IconImage = iconImage
				});
				if (anyFileMode)
				{
					Owner.FileSettings.AddForAnyFile(filePath);
				}
			}
			Owner.RebuildVisibleRows();
		}

		public void LoadFiles()
		{
			Stopwatch stopwatch = Stopwatch.StartNew();
			List<(ConfigDescriptorState TagFile, Dictionary<string, string> DisplayValues, string FilePath)> pendingFiles = new List<(ConfigDescriptorState, Dictionary<string, string>, string)>();
			while (CurrentIndex < FileNames.Count && !CancellationTokenSource.IsCancellationRequested)
			{
				string inputPath = FileNames[CurrentIndex];
				string fullPath;
				try
				{
					fullPath = Path.GetFullPath(inputPath);
				}
				catch (System.Exception ex)
				{
					LoadErrors.AddLine(inputPath);
					LoadErrors.AddLine(ex.Message);
					CurrentIndex++;
					continue;
				}

				CurrentFileInfo = new FileInfo(fullPath);
				string extension = CurrentFileInfo.Extension.ToLower();
				if (CurrentFileInfo.Exists && EnabledTagTypesByExtension.ContainsKey(extension) && !ExistingFilePaths.Contains(fullPath))
				{
					if (!Owner.HasFileTypeIcon(extension))
					{
						AddFileTypeIcon(extension, fullPath);
					}
					Dictionary<string, string> displayValues = Owner.BuildBasicFileDisplayValues(CurrentFileInfo);
					using (ConfigDescriptorState tagFile = new ConfigDescriptorState(fullPath))
					{
						bool addToList = true;
						if (!tagFile.IsLoadedSuccessfully())
						{
							LoadErrors.AddLine(CurrentFileInfo.Name);
							LoadErrors.AddLine(tagFile.GetLoadError());
						}
						else
						{
							tagFile.LoadBasicTagFields();
							tagFile.LoadAudioProperties();
							tagFile.LoadPictureSummary(flagOnly: true);
							tagFile.LoadLyrics();
							if (tagFile.IsExcludedByFileFilter())
							{
								addToList = false;
							}
						}

						if (addToList)
						{
							pendingFiles.Add((TagFile: tagFile, DisplayValues: displayValues, FilePath: fullPath));
							if (stopwatch.ElapsedMilliseconds >= 500L)
							{
								LoadedFilesProgress.Report(pendingFiles);
								stopwatch.Restart();
								pendingFiles = new List<(ConfigDescriptorState, Dictionary<string, string>, string)>();
							}
						}
					}
				}
				CurrentIndex++;
			}

			if (pendingFiles.Any())
			{
				LoadedFilesProgress.Report(pendingFiles);
			}
			stopwatch.Stop();
		}

		private void AddFileTypeIcon(string extension, string filePath)
		{
			using Icon smallFileIcon = ImageUtilities.GetSmallFileIcon(filePath);
			Bitmap sourceIcon = smallFileIcon.ToBitmap();
			try
			{
				Bitmap listIcon = new Bitmap(FileIconSize.Width, FileIconSize.Height);
				using (Graphics graphics = Graphics.FromImage(listIcon))
				{
					graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
					graphics.DrawImage(sourceIcon, new Point((FileIconSize.Width - sourceIcon.Width) / 2, (FileIconSize.Height - sourceIcon.Height) / 2));
				}
				Owner.CacheFileTypeIcon(extension, listIcon);
			}
			finally
			{
				sourceIcon.Dispose();
			}
		}

		public void ShowLoadErrors()
		{
			DialogService.ShowErrorMessage(LoadErrors.ToString());
		}
	}

	// 收敛 9 个批文件处理 TaskContext 的公共脚手架:取消令牌 / 进度对话框 / 当前文件 / 已处理计数 + 幂等 Cancel()。
	// 这 4 字段与 guarded Cancel()(9 个逐字节相同)在 RefreshItems/ConvertFilenameChinese/SaveTags/UndoSaveTags/
	// UndoRename/ClearTags/DeleteFiles/SaveLrcFiles/ExtractCovers 全部出现;各 context 特有字段(items 数组 / 各类
	// 计数 / log / owner 等)保留派生类。LyricDownload/ReleaseYearSearch 的 Cancel() 语义不同(无 guard + 关闭对话框),
	// 不属此基类。
	private class BatchFileTaskContext
	{
		public CancellationTokenSource cancellationSource;

		public ProgressDialog progressDialog;

		public FileInfo currentFile;

		public int processedCount;

		internal void Cancel()
		{
			if (!cancellationSource.IsCancellationRequested)
			{
				cancellationSource.Cancel();
			}
		}
	}

	private sealed class RefreshItemsTaskContext : BatchFileTaskContext
	{
		public SelectedListViewItemInfo[] itemInfos;

		public StateFieldInstance owner;

		public Page loadErrors;

		public IProgress<List<(SelectedListViewItemInfo ItemInfo, ConfigDescriptorState TagState, Dictionary<string, string> DisplayValues)>> progressReporter;

		public (string msg, bool isErr)? previousMessage;

		public bool showLoadErrors;

		public Action<(SelectedListViewItemInfo ItemInfo, ConfigDescriptorState TagState, Dictionary<string, string> DisplayValues)> updateListViewItemAction;

		internal void UpdateProgress()
		{
			progressDialog.UpdateCountProgress(Resources.Msg_Readtag + currentFile?.Name, processedCount, itemInfos.Length);
		}

		internal void ApplyRefreshedItems(List<(SelectedListViewItemInfo ItemInfo, ConfigDescriptorState TagState, Dictionary<string, string> DisplayValues)> refreshedItems)
		{
			refreshedItems.ForEach(updateListViewItemAction ?? (updateListViewItemAction = UpdateSingleListViewItem));
		}

		internal void UpdateSingleListViewItem((SelectedListViewItemInfo ItemInfo, ConfigDescriptorState TagState, Dictionary<string, string> DisplayValues) info)
		{
			// Index 统一为 cachedFileListItems(主表)下标 —— 刷新选中项与撤销镜像两条路径共用同一寻址。
			FileRow fileRow = owner.cachedFileListItems[info.ItemInfo.Index];
			owner.UpdateListViewItemValues(fileRow, info.TagState, info.DisplayValues);
		}

		internal void RefreshItems()
		{
			Stopwatch stopwatch = new Stopwatch();
			stopwatch.Start();
			List<(SelectedListViewItemInfo ItemInfo, ConfigDescriptorState TagState, Dictionary<string, string> DisplayValues)> refreshedItems = new List<(SelectedListViewItemInfo, ConfigDescriptorState, Dictionary<string, string>)>();
			while (processedCount < itemInfos.Length && !cancellationSource.IsCancellationRequested)
			{
				SelectedListViewItemInfo selectedItemInfo = itemInfos[processedCount];
				string fullPath = Path.GetFullPath(selectedItemInfo.FilePath);
				currentFile = new FileInfo(fullPath);
				Dictionary<string, string> displayValues = owner.BuildBasicFileDisplayValues(currentFile);
				using (ConfigDescriptorState tagState = new ConfigDescriptorState(fullPath))
				{
					if (tagState.IsLoadedSuccessfully())
					{
						tagState.LoadBasicTagFields();
						tagState.LoadAudioProperties();
						tagState.LoadPictureSummary(flagOnly: true);
						tagState.LoadLyrics();
					}
					else
					{
						loadErrors.AddLine(currentFile.Name);
						loadErrors.AddLine(tagState.GetLoadError());
					}
					refreshedItems.Add((selectedItemInfo, tagState, displayValues));
					if (stopwatch.ElapsedMilliseconds >= 500L)
					{
						progressReporter.Report(refreshedItems);
						stopwatch.Restart();
						refreshedItems = new List<(SelectedListViewItemInfo, ConfigDescriptorState, Dictionary<string, string>)>();
					}
				}
				processedCount++;
			}
			if (refreshedItems.Any())
			{
				progressReporter.Report(refreshedItems);
			}
			stopwatch.Stop();
		}

		internal void ShowCompletionMessages()
		{
			if (previousMessage.HasValue)
			{
				if (previousMessage.Value.isErr)
				{
					DialogService.ShowErrorMessage(previousMessage?.msg);
				}
				else
				{
					DialogService.ShowInformationMessage(previousMessage?.msg);
				}
			}
			if (!showLoadErrors || loadErrors.LineCount <= 0)
			{
				return;
			}
			DialogService.ShowErrorMessage(loadErrors.ToString());
		}
	}

	private void ReportAsyncOperationError(System.Exception exception, string context)
	{
		System.Exception displayException = UnwrapAsyncOperationException(exception);
		LogService.WriteExceptionDetails(displayException, context);
		DialogService.ShowErrorMessage(displayException.GetMessageChain());
	}

	private void ReportAsyncOperationErrorIfNotCancellation(System.Exception exception, CancellationTokenSource cancellationSource, string context)
	{
		if (!IsCancellationException(exception, cancellationSource))
		{
			ReportAsyncOperationError(exception, context);
		}
	}

	internal static (string, bool) BuildBatchResultMessage(int totalCount, string completedMessage, int primaryCount, int failedCount, int skippedCount, int processedCount, string logText, bool includeSkippedBranch)
	{
		(string, bool) value = default((string, bool));
		if (totalCount > 1)
		{
			value.Item1 = string.Format(completedMessage + "\n" + Resources.Msg_OK_Fail_Skip_Count, primaryCount, failedCount, skippedCount, processedCount) + "\n" + logText;
		}
		else if (primaryCount > 0)
		{
			value.Item1 = completedMessage + "\n" + logText;
		}
		else if (includeSkippedBranch && skippedCount > 0)
		{
			value.Item1 = Resources.Msg_Skipped + "\n" + logText;
		}
		else
		{
			value.Item1 = logText;
			value.Item2 = true;
		}
		return value;
	}

	internal static bool IsCancellationException(System.Exception exception, CancellationTokenSource cancellationSource)
	{
		if (cancellationSource == null || !cancellationSource.IsCancellationRequested)
		{
			return false;
		}
		if (exception is OperationCanceledException)
		{
			return true;
		}
		if (exception is AggregateException aggregateException && aggregateException.InnerExceptions.Count > 0)
		{
			return aggregateException.InnerExceptions.All(innerException => IsCancellationException(innerException, cancellationSource));
		}
		return false;
	}

	internal static System.Exception UnwrapAsyncOperationException(System.Exception exception)
	{
		if (exception is AggregateException aggregateException && aggregateException.InnerExceptions.Count == 1)
		{
			return aggregateException.InnerExceptions[0];
		}
		return exception;
	}

	private sealed class FileListFilterContext
	{
		public StateFieldInstance owner;

		public string filterText;

		internal void ResetFilterComboState(KeyValuePair<string, ComboBox> def)
		{
			string key = def.Key;
			ComboBox comboBox = def.Value;
			var (valueCounts, filterOptions) = owner.selectedFilterValueStates[key];
			owner.ResetKeepBlankComboBoxItems(comboBox);
			comboBox.Text = "";
			valueCounts.Clear();
			filterOptions.Clear();
		}
	}

	private sealed class FileListFilterItemContext
	{
		public FileRow fileRow;

		public FileListFilterContext filterContext;

		internal bool MatchesFilterText(int columnIndex)
		{
			return fileRow.CellTexts[columnIndex].IndexOf(filterContext.filterText, StringComparison.OrdinalIgnoreCase) >= 0;
		}

		internal void CountSelectedFilterValue(KeyValuePair<string, (Dictionary<string, int>, List<(string, bool)>)> filterState)
		{
			string key = filterState.Key;
			var (valueCounts, filterOptions) = filterState.Value;
			string filterValue = filterContext.owner.GetSelectedFilterValue(fileRow, key);
			if (valueCounts.TryGetValue(filterValue, out var count))
			{
				count++;
				valueCounts[filterValue] = count;
			}
			else
			{
				valueCounts.Add(filterValue, 1);
				filterOptions.Add((filterValue, false));
			}
		}

	}

	private sealed class TagEditorStateLoadContext
	{
		public StateFieldInstance owner;

		public ConfigDescriptorState selectedTag;

		internal void LoadSingleFileTagField(KeyValuePair<string, ComboBox> tagField)
		{
			string fieldName = tagField.Key;
			ComboBox comboBox = tagField.Value;
			List<(string, bool)> filterOptions = owner.selectedFilterValueStates[fieldName].Item2;
			owner.ResetKeepBlankComboBoxItems(comboBox);
			comboBox.Text = selectedTag.GetDisplayValue(tagField.Key);
			if (!string.IsNullOrWhiteSpace(comboBox.Text))
			{
				comboBox.Items.Add(comboBox.Text);
			}
			else if (comboBox.Text == "")
			{
				comboBox.Text = "x";
				comboBox.Text = "";
			}
			filterOptions.Clear();
		}

		internal void LoadMultiFileTagField(KeyValuePair<string, ComboBox> tagField)
		{
			ComboBoxFilterOptionUpdater optionUpdater = new ComboBoxFilterOptionUpdater();
			string fieldName = tagField.Key;
			optionUpdater.comboBox = tagField.Value;
			var (valueCounts, filterOptions) = owner.selectedFilterValueStates[fieldName];
			if (fieldName != "lyrics")
			{
				filterOptions.ForEach(optionUpdater.UpdateOption);
				filterOptions.Clear();
				if (valueCounts.Count == 1)
				{
					string onlyValue = valueCounts.First().Key;
					if (onlyValue == "Y\tT" && fieldName == "comment")
					{
						if (optionUpdater.comboBox.Text != "<keep>")
						{
							optionUpdater.comboBox.Text = "<keep>";
						}
					}
					else
					{
						optionUpdater.comboBox.Text = onlyValue;
					}
				}
				else if (optionUpdater.comboBox.Text != "<keep>")
				{
					optionUpdater.comboBox.Text = "<keep>";
				}
				return;
			}
			filterOptions.Clear();
			owner.ResetKeepBlankComboBoxItems(optionUpdater.comboBox);
			if (valueCounts.Count == 1 && valueCounts.First().Key == "")
			{
				if (optionUpdater.comboBox.Text != "")
				{
					optionUpdater.comboBox.Text = "";
				}
			}
			else if (optionUpdater.comboBox.Text != "<keep>")
			{
				optionUpdater.comboBox.Text = "<keep>";
			}
		}

		internal void ClearTagField(KeyValuePair<string, ComboBox> tagField)
		{
			string fieldName = tagField.Key;
			ComboBox comboBox = tagField.Value;
			List<(string, bool)> filterOptions = owner.selectedFilterValueStates[fieldName].Item2;
			owner.ResetKeepBlankComboBoxItems(comboBox);
			comboBox.Text = "";
			filterOptions.Clear();
		}
	}

	private sealed class ComboBoxFilterOptionUpdater
	{
		public ComboBox comboBox;

		internal void UpdateOption((string, bool) option)
		{
			var (text, remove) = option;
			if (!string.IsNullOrEmpty(text))
			{
				if (remove)
				{
					comboBox.Items.Remove(text);
				}
				else
				{
					comboBox.Items.Add(text);
				}
			}
		}
	}

	private sealed class LyricDownloadTaskContext
	{
		public CancellationTokenSource cancellationSource;

		public SimpleProgressDialog progressDialog;

		public LyricSearchResult lyricResult;

		internal void Cancel()
		{
			cancellationSource.Cancel();
			progressDialog.CloseProgressDialog();
		}

		internal string DownloadLyricText()
		{
			LyricSearchResult loadedLyric = lyricResult.DeferredLyricLoader(cancellationSource);
			return loadedLyric?.GetFormattedLyricText() ?? "";
		}
	}

	private sealed class CoverTypeMenuContext
	{
		public string coverType;

		public StateFieldInstance owner;
	}

	private sealed class CoverTypeMenuItemClickContext
	{
		public ConfigDescriptorState.PictureData pictureData;

		public CoverTypeMenuContext menuContext;

		internal void ApplyCoverType(object sender, EventArgs e)
		{
			pictureData.PictureType = menuContext.coverType;
			menuContext.owner.coverPictureTypeLabel.Text = menuContext.coverType;
		}
	}

	private sealed class ReleaseYearSearchTaskContext
	{
		public CancellationTokenSource cancellationSource;

		public SimpleProgressDialog progressDialog;

		public TrackSearchResult trackResult;

		internal void Cancel()
		{
			cancellationSource.Cancel();
			progressDialog.CloseProgressDialog();
		}

		internal string FetchReleaseYear()
		{
			return CombinedTagSearchDialog.FetchMissingNetEaseReleaseYear(trackResult, cancellationSource);
		}

	}

	private sealed class PictureCompressionOptions
	{
		public int maxResolution;

		public string formatMode;

		public long maxByteLength;
	}

	private sealed class PictureCompressionItem
	{
		public ConfigDescriptorState.PictureData pictureData;

		public PictureCompressionOptions options;
	}

	private sealed class PictureCompressionWorker
	{
		public Image workingImage;

		public Func<long, bool> encodeWorkingImageAsJpeg;

		public Func<int, long, bool> resizeWorkingImageAndEncode;

		public bool isAlreadyWithinLimits;

		public Func<bool> scaleDownWorkingImageAndEncode;

		public Func<bool>[] compressionRetrySteps;

		public PictureCompressionItem compressionItem;

		internal bool EncodeCurrentImageAsJpeg(long quality)
		{
			byte[] jpegBytes = ImageUtilities.EncodeJpeg(workingImage, quality);
			if (jpegBytes != null)
			{
				compressionItem.pictureData.ImageBytes = jpegBytes;
				compressionItem.pictureData.MimeType = "image/jpeg";
				compressionItem.pictureData.Width = workingImage.Width;
				compressionItem.pictureData.Height = workingImage.Height;
				return true;
			}
			return false;
		}

		internal bool ScaleCurrentImageAndEncode()
		{
			Bitmap bitmap = ImageUtilities.ScaleImage(workingImage, 0.75f);
			if (bitmap == null)
			{
				return false;
			}
			workingImage.Dispose();
			workingImage = bitmap;
			return encodeWorkingImageAsJpeg(85L);
		}

		internal bool ResizeCurrentImageAndEncode(int maxSideLength, long quality)
		{
			Bitmap bitmap = ImageUtilities.ResizeImageToFit(workingImage, new Size(maxSideLength, maxSideLength));
			if (bitmap != null)
			{
				workingImage.Dispose();
				workingImage = bitmap;
				return encodeWorkingImageAsJpeg(quality);
			}
			return false;
		}

		// 压缩重试梯度表(替代原 15 个 ResizeToNNNQualityMM / ResizeToConfiguredLimitQualityMM 纯转发方法)。
		// CompressPictures 按 maxResolution 场景选表,Array.ConvertAll 成 Func<bool>[](每个 lambda 调用时
		// 执行 resizeWorkingImageAndEncode(分辨率, 质量),等价于原对应方法)。
		// 固定分辨率梯度(maxResolution==0):从高到低逐级压。
		internal static readonly (int Resolution, long Quality)[] fixedResolutionRetrySteps =
		{
			(1200, 75L), (1200, 55L), (800, 75L), (800, 55L), (500, 75L), (500, 55L),
			(500, 30L), (300, 50L), (300, 30L), (100, 30L), (50, 10L),
		};

		// 配置上限场景(maxResolution!=0):分辨率固定为 options.maxResolution(lambda 调用时读),仅质量递降。
		internal static readonly long[] configuredLimitRetryQualities = { 75L, 50L, 30L, 10L };

		internal bool CompressPicture()
		{
			using (MemoryStream stream = new MemoryStream(compressionItem.pictureData.ImageBytes))
			{
				workingImage = Image.FromStream(stream);
				int retryStepIndex = 0;
				int compressionPassCount = 0;
				// 重试步梯队(两处循环分支共用):越界即失败;否则后置自增并执行当前步。
				bool tryNextRetryStep()
				{
					return retryStepIndex < compressionRetrySteps.Length && compressionRetrySteps[retryStepIndex++]();
				}
				if (compressionItem.options.maxResolution != 0 && compressionItem.options.maxResolution != Math.Max(workingImage.Width, workingImage.Height))
				{
					if (!resizeWorkingImageAndEncode(compressionItem.options.maxResolution, 85L))
					{
						return false;
					}
				}
				else
				{
					if (isAlreadyWithinLimits)
					{
						return true;
					}
					if ((!ConfigDescriptorState.SupportedPictureMimeTypes().Contains(compressionItem.pictureData.MimeType) || (compressionItem.options.formatMode == "JPG" && compressionItem.pictureData.MimeType != "image/jpeg")) && !encodeWorkingImageAsJpeg(85L))
					{
						return false;
					}
				}
				while (compressionItem.pictureData.ImageBytes.Length > compressionItem.options.maxByteLength)
				{
					if (compressionItem.options.maxResolution == 0)
					{
						if (compressionPassCount > 0)
						{
							if (workingImage.Width > 1200 && workingImage.Height > 1200)
							{
								if (!scaleDownWorkingImageAndEncode())
								{
									return false;
								}
							}
							else if (!tryNextRetryStep())
							{
								return false;
							}
						}
						else if (!encodeWorkingImageAsJpeg(85L))
						{
							return false;
						}
					}
					else if (!tryNextRetryStep())
					{
						return false;
					}
					compressionPassCount++;
				}
			}
			return true;
		}
	}

	private sealed class ConvertFilenameChineseBatchContext : BatchFileTaskContext
	{
		public (string Path, string NewPath, int ListViewIndex)[] renameItems;

		public int renamedCount;

		public int failedCount;

		public int skippedCount;

		public Page messageLog;

		public bool convertSimplifiedToTraditional;

		internal void UpdateProgress()
		{
			progressDialog.UpdateStatisticsProgress(Resources.Msg_Savetag + currentFile?.Name, processedCount, renameItems.Length, renamedCount, failedCount, skippedCount, renameItems.Length);
		}

		internal void ConvertFilenames()
		{
			TagHistoryRepository tagHistoryRepository = new TagHistoryRepository(useTransaction: true);
			try
			{
			TagHistoryRepository.ClearUndoState();
			string directoryName = default(string);
			while (processedCount < renameItems.Length)
			{
				RenameFailureRecorder renameFailureRecorder = new RenameFailureRecorder();
				renameFailureRecorder.batchContext = this;
				if (cancellationSource.IsCancellationRequested)
				{
					break;
				}
				renameFailureRecorder.CurrentPath = Path.GetFullPath(renameItems[processedCount].Path);
				directoryName = Path.GetDirectoryName(renameFailureRecorder.CurrentPath);
				currentFile = new FileInfo(renameFailureRecorder.CurrentPath);
				Action<string> reportFailure = renameFailureRecorder.ReportFailure;
				string name = currentFile.Name;
				string text = (convertSimplifiedToTraditional ? ChineseTextConverter.SimplifiedToTraditional().ConvertText(name) : ChineseTextConverter.TraditionalToSimplified().ConvertText(name));
					if (name != text)
					{
						string destinationPath = Path.Combine(directoryName, text);
						try
						{
							PathFileUtilities.MoveFileAllowingCaseOnlyRename(renameFailureRecorder.CurrentPath, destinationPath);
							TagHistoryRepository.UpdateHistoryFilePath(renameFailureRecorder.CurrentPath, destinationPath, tagHistoryRepository);
							TagHistoryRepository.AddRenameUndoRecord(renameFailureRecorder.CurrentPath, destinationPath);
							renameItems[processedCount].NewPath = destinationPath;
						renamedCount++;
					}
					catch (System.Exception ex)
					{
						reportFailure(ex.Message);
						failedCount++;
					}
				}
				else
				{
					skippedCount++;
				}
				processedCount++;
			}
			}
			finally
			{
				tagHistoryRepository.Dispose();
			}
		}
	}

	private sealed class RenameFailureRecorder
	{
		public string CurrentPath;

		public ConvertFilenameChineseBatchContext batchContext;

		internal void ReportFailure(string message)
		{
			string text = message ?? Resources.Msg_SaveFail;
			LogService.WriteRenameLog(CurrentPath + ": " + text);
			batchContext.messageLog.AddLine(batchContext.currentFile.Name);
			batchContext.messageLog.AddLine(text);
		}
	}

	private sealed class SaveTagsTaskContext : BatchFileTaskContext
	{
		public SelectedListViewItemInfo[] itemsToSave;

		public int savedCount;

		public int failedCount;

		public int skippedCount;

		public Dictionary<string, object> tagValues;

		public Page messageLog;

		public StateFieldInstance owner;

		public bool canCancelReadOnly;

		public bool shouldRefreshPictureResolution;

		public Func<ConfigDescriptorState, string, bool> applyLyricsAction;

		public Func<ConfigDescriptorState, bool, bool> convertChineseTextAction;

		internal void UpdateProgress()
		{
			progressDialog.UpdateStatisticsProgress(Resources.Msg_Savetag + currentFile?.Name, processedCount, itemsToSave.Length, savedCount, failedCount, skippedCount, itemsToSave.Length);
		}

		internal void SaveTags()
		{
			bool compressedInputPictures = false;
			if (tagValues.TryGetValue("allpicturedata", out var value))
			{
				List<ConfigDescriptorState.PictureData> pictures = value as List<ConfigDescriptorState.PictureData>;
				CompressPictures(pictures, useRestoreLimits: false);
				if (pictures.Exists(HasPictureProcessingFailure))
				{
					LogService.WriteSaveTagsLog(Resources.Msg_CompressPictureFail);
					messageLog.AddLine(Resources.Msg_CompressPictureFail);
					return;
				}
				compressedInputPictures = true;
			}
			Func<ConfigDescriptorState, string, bool> applyLyrics = applyLyricsAction ?? (applyLyricsAction = ApplyLyricsBatchAction);
			Func<ConfigDescriptorState, bool, bool> convertChineseText = convertChineseTextAction ?? (convertChineseTextAction = ConvertChineseTextFields);
			TagHistoryRepository tagHistoryRepository = new TagHistoryRepository(useTransaction: true);
			try
			{
				TagHistoryRepository.ClearUndoState();
				while (processedCount < itemsToSave.Length)
				{
					if (cancellationSource.IsCancellationRequested)
					{
						break;
					}
					SaveTagsFileContext fileContext = new SaveTagsFileContext();
					fileContext.batchContext = this;
					fileContext.filePath = Path.GetFullPath(itemsToSave[processedCount].FilePath);
					currentFile = new FileInfo(fileContext.filePath);
					DateTime lastWriteTime = default(DateTime);
					bool tagFileLoaded = false;
					SaveTagFailureReporter failureReporter = new SaveTagFailureReporter();
					failureReporter.fileContext = fileContext;
					try
					{
						PathFileUtilities.ClearReadOnlyIfAllowed(currentFile, canCancelReadOnly);
						lastWriteTime = currentFile.LastWriteTime;
						failureReporter.tagState = new ConfigDescriptorState(failureReporter.fileContext.filePath);
						Action<string> action = failureReporter.ReportFailure;
						if (failureReporter.tagState.IsLoadedSuccessfully())
						{
							tagFileLoaded = true;
							failureReporter.tagState.LoadBasicTagFields();
							failureReporter.tagState.LoadLyrics();
							ConfigDescriptorState originalTagSnapshot = TagHistoryRepository.CreateTagSnapshot(failureReporter.tagState, includePictures: false);
							object lyricsImportValue;
							if (tagValues.TryGetValue("chscht_handle", out var convertToTraditionalValue))
							{
								if (!convertChineseText(failureReporter.tagState, (bool)convertToTraditionalValue))
								{
									processedCount++;
									continue;
								}
							}
							else if (tagValues.TryGetValue("lyrics_handle", out lyricsImportValue))
							{
								if (!applyLyrics(failureReporter.tagState, lyricsImportValue as string))
								{
									processedCount++;
									continue;
								}
							}
							else
							{
								foreach (KeyValuePair<string, object> tagValueEntry in tagValues)
								{
									if (tagValueEntry.Value is string text)
									{
										var (shouldAssign, resolvedValue) = ResolveTagFieldTemplateValue(text, () => (failureReporter.tagState[tagValueEntry.Key] != null) ? failureReporter.tagState[tagValueEntry.Key].ToString() : "");
										if (shouldAssign)
										{
											failureReporter.tagState[tagValueEntry.Key] = resolvedValue;
										}
										continue;
									}
									if (tagValueEntry.Key == "allpicturedata")
									{
										failureReporter.tagState.LoadAllPictures();
										List<ConfigDescriptorState.PictureData> newPictures = failureReporter.tagState["allpicturedata"] as List<ConfigDescriptorState.PictureData>;
										foreach (ConfigDescriptorState.PictureData picture in newPictures)
										{
											using (ConfigDescriptorState.LoadPictureImage(picture))
											{
											}
										}
										originalTagSnapshot["allpicturedata"] = newPictures;
									}
									failureReporter.tagState[tagValueEntry.Key] = tagValueEntry.Value;
								}
								if (!compressedInputPictures && shouldRefreshPictureResolution)
								{
									failureReporter.tagState.LoadAllPictures();
									List<ConfigDescriptorState.PictureData> currentPictures = failureReporter.tagState["allpicturedata"] as List<ConfigDescriptorState.PictureData>;
									List<ConfigDescriptorState.PictureData> originalPictureCopies = new List<ConfigDescriptorState.PictureData>();
									foreach (ConfigDescriptorState.PictureData picture in currentPictures)
									{
										using (ConfigDescriptorState.LoadPictureImage(picture))
										{
										}
										originalPictureCopies.Add(new ConfigDescriptorState.PictureData
										{
											ImageBytes = (byte[])picture.ImageBytes.Clone(),
											PictureType = picture.PictureType,
											MimeType = picture.MimeType,
											Width = picture.Width,
											Height = picture.Height
										});
									}
									originalTagSnapshot["allpicturedata"] = originalPictureCopies;
									CompressPictures(currentPictures, useRestoreLimits: false);
								}
							}
							if (failureReporter.tagState.SaveTagFields())
							{
								if (Settings.Default.SaveLrcWhileSaveTags && failureReporter.tagState["lyrics"] is string lyricsText && !string.IsNullOrWhiteSpace(lyricsText))
								{
									try
									{
										string saveLrcFileDefaultEncoding = Settings.Default.SaveLrcFileDefaultEncoding;
										File.WriteAllText(PathFileUtilities.BuildLyricSavePath(failureReporter.fileContext.filePath, failureReporter.tagState), lyricsText, Encoding.GetEncoding(saveLrcFileDefaultEncoding));
									}
									catch (System.Exception ex)
									{
										action(Resources.Msg_WriteLrcFileFail + ", " + ex.Message);
									}
								}
								var (historyError, selection) = TagHistoryRepository.AddHistoryRecordIfChanged(failureReporter.fileContext.filePath, originalTagSnapshot, failureReporter.tagState, tagHistoryRepository);
								if (historyError != null)
								{
									action(historyError);
								}
								string undoError = TagHistoryRepository.AddUndoRecord(originalTagSnapshot, selection, tagHistoryRepository);
								if (undoError != null)
								{
									action(undoError);
								}
								savedCount++;
							}
							else
							{
								action(null);
								failedCount++;
							}
						}
						else
						{
							action(null);
							failedCount++;
						}
					}
					catch (System.Exception ex)
					{
						LogService.WriteSaveTagsLog(fileContext.filePath + ": " + ex.Message);
						messageLog.AddLine(currentFile.Name);
						messageLog.AddLine(ex.Message);
						failedCount++;
					}
					finally
					{
						if (failureReporter.tagState != null)
						{
							((IDisposable)failureReporter.tagState).Dispose();
						}
					}
					if (tagFileLoaded && Settings.Default.SaveTagsKeepUpdateTime)
					{
						try
						{
							currentFile.LastWriteTime = lastWriteTime;
						}
						catch (System.Exception ex2)
						{
							LogService.WriteSaveTagsLog(fileContext.filePath + ": " + ex2.Message);
							messageLog.AddLine(currentFile.Name);
							messageLog.AddLine(ex2.Message);
						}
					}
					processedCount++;
				}
			}
			finally
			{
				tagHistoryRepository.Dispose();
			}
		}

		internal bool ApplyLyricsBatchAction(ConfigDescriptorState i, string counter)
		{
			if (counter == "menuStrip1.Batch.ImportLrcFile")
			{
				string importedLyrics = LyricEditorDialog.ImportLrcText(i.GetFilePath(), i, null);
				if (importedLyrics != null && !string.IsNullOrWhiteSpace(importedLyrics))
				{
					i["lyrics"] = importedLyrics;
					return true;
				}

				skippedCount++;
				return false;
			}

			string lyrics = i["lyrics"] as string;
			if (string.IsNullOrWhiteSpace(lyrics))
			{
				skippedCount++;
				return false;
			}

			switch (counter)
			{
				case "menuStrip1.Batch.DeleteHeadTags":
					i["lyrics"] = LyricTextProcessor.ReformatLyric(lyrics, removeBlankLines: false, removeHeaderTags: true);
					return true;
				case "menuStrip1.Batch.DeleteLinesOfBlankText":
					i["lyrics"] = LyricTextProcessor.ReformatLyric(lyrics, removeBlankLines: true, removeHeaderTags: false);
					return true;
				case "menuStrip1.Batch.RemoveTimetag":
					i["lyrics"] = LyricTextProcessor.RemoveTimestamps(lyrics);
					return true;
				case "menuStrip1.Batch.ReformatTimetag":
					i["lyrics"] = LyricTextProcessor.ReformatLyric(lyrics, removeBlankLines: false, removeHeaderTags: false);
					return true;
				default:
					skippedCount++;
					return false;
			}
		}

		internal bool ConvertChineseTextFields(ConfigDescriptorState tagState, bool convertSimplifiedToTraditional)
		{
			bool convertedAny = false;
			foreach (string fieldName in owner.tagFieldTextHandlers.Keys)
			{
				if (tagState[fieldName] is string value && !string.IsNullOrWhiteSpace(value))
				{
					tagState[fieldName] = convertSimplifiedToTraditional ? ChineseTextConverter.SimplifiedToTraditional().ConvertText(value) : ChineseTextConverter.TraditionalToSimplified().ConvertText(value);
					convertedAny = true;
				}
			}

			if (!convertedAny)
			{
				skippedCount++;
				return false;
			}

			return true;
		}

	}

	private sealed class SaveTagsFileContext
	{
		public string filePath;

		public SaveTagsTaskContext batchContext;
	}

	// 收敛下方 3 个 *FailureReporter 的失败消息优先级:非空 loadError 优先,否则回退 fallbackMessage ?? Msg_SaveFail。
	// 三处原写法等价——SaveTag/UndoSaveTag 版"先算 fallback 再被非空 loadError 覆盖";TagSave 版"先取 loadError,
	// 空则回退 fallback",同归此三元式。GetLoadError() 为纯 getter(return loadError;),SaveTag 版原 2 次调用坍缩
	// 为 1 次不可观测。
	internal static string ResolveFailureMessage(string loadError, string fallbackMessage)
	{
		return !string.IsNullOrWhiteSpace(loadError) ? loadError : (fallbackMessage ?? Resources.Msg_SaveFail);
	}

	// 单参重载:7 处内联「空白(或 null)则回退 Msg_SaveFail」的收敛(errorMessage / ex.Message 均经此)。
	internal static string ResolveFailureMessage(string message)
	{
		return string.IsNullOrWhiteSpace(message) ? Resources.Msg_SaveFail : message;
	}

	private sealed class SaveTagFailureReporter
	{
		public ConfigDescriptorState tagState;

		public SaveTagsFileContext fileContext;

		internal void ReportFailure(string message)
		{
			string text = ResolveFailureMessage(tagState.GetLoadError(), message);
			LogService.WriteSaveTagsLog(fileContext.filePath + ": " + text);
			fileContext.batchContext.messageLog.AddLine(fileContext.batchContext.currentFile.Name);
			fileContext.batchContext.messageLog.AddLine(text);
		}
	}

	private sealed class UndoSaveTagsTaskContext : BatchFileTaskContext
	{
		public List<ConfigDescriptorState> undoTagSnapshots;

		public int restoredCount;

		public int failedCount;

		public int skippedCount;

		public Page messageLog;

		internal void UpdateProgress()
		{
			if (processedCount < undoTagSnapshots.Count)
			{
				progressDialog.UpdateStatisticsProgress(Resources.Msg_Savetag + currentFile?.Name, processedCount, undoTagSnapshots.Count, restoredCount, failedCount, skippedCount, undoTagSnapshots.Count);
			}
		}

		internal void RestoreSavedTags()
		{
			using (TagHistoryRepository tagHistoryRepository = new TagHistoryRepository(useTransaction: true))
			{
				while (processedCount < undoTagSnapshots.Count)
				{
					if (cancellationSource.IsCancellationRequested)
					{
						break;
					}

					ConfigDescriptorState undoSnapshot = undoTagSnapshots[processedCount];
					UndoSaveTagsFileContext fileContext = new UndoSaveTagsFileContext();
					fileContext.taskContext = this;
					fileContext.filePath = undoSnapshot["filepath"] as string;
					currentFile = new FileInfo(fileContext.filePath);
					DateTime lastWriteTime = currentFile.LastWriteTime;
					bool shouldRestoreLastWriteTime = false;
					UndoSaveTagFailureReporter failureReporter = new UndoSaveTagFailureReporter();
					failureReporter.fileContext = fileContext;
					using (failureReporter.tagState = new ConfigDescriptorState(failureReporter.fileContext.filePath))
					{
						Action<string> reportFailure = failureReporter.ReportFailure;
						if (failureReporter.tagState.IsLoadedSuccessfully())
						{
							failureReporter.tagState.LoadBasicTagFields();
							failureReporter.tagState.LoadLyrics();
							TagHistoryRepository.CopyEditableTagFields(undoSnapshot, failureReporter.tagState);
							string restoreError = TagHistoryRepository.RestoreUndoPayloads(undoSnapshot);
							if (restoreError != null)
							{
								reportFailure(Resources.Msg_RestoreLyricsOrCoverFail + ":" + restoreError);
								failedCount++;
							}
							else if (undoSnapshot.TryGetRawValue("allpicturedata", out var rawPictures))
							{
								List<ConfigDescriptorState.PictureData> restoredPictures = rawPictures as List<ConfigDescriptorState.PictureData>;
								CompressPictures(restoredPictures, useRestoreLimits: true);
								if (restoredPictures.Exists(HasPictureProcessingFailure))
								{
									reportFailure(Resources.Msg_CompressPictureFail);
									failedCount++;
								}
								else
								{
									failureReporter.tagState["allpicturedata"] = restoredPictures;
									shouldRestoreLastWriteTime = SaveRestoredTags(undoSnapshot, tagHistoryRepository, failureReporter, reportFailure);
								}
							}
							else
							{
								shouldRestoreLastWriteTime = SaveRestoredTags(undoSnapshot, tagHistoryRepository, failureReporter, reportFailure);
							}
						}
						else
						{
							reportFailure(null);
							failedCount++;
						}
					}

					if (shouldRestoreLastWriteTime && Settings.Default.SaveTagsKeepUpdateTime)
					{
						try
						{
							currentFile.LastWriteTime = lastWriteTime;
						}
						catch (System.Exception ex)
						{
							LogService.WriteSaveTagsLog(fileContext.filePath + ": " + ex.Message);
							messageLog.AddLine(currentFile.Name);
							messageLog.AddLine(ex.Message);
						}
					}

					processedCount++;
				}
			}
		}

		private bool SaveRestoredTags(ConfigDescriptorState undoSnapshot, TagHistoryRepository tagHistoryRepository, UndoSaveTagFailureReporter failureReporter, Action<string> reportFailure)
		{
			bool shouldRestoreLastWriteTime = true;
			if (failureReporter.tagState.SaveTagFields())
			{
				if (Settings.Default.SaveLrcWhileSaveTags && failureReporter.tagState["lyrics"] is string lyricsText && !string.IsNullOrWhiteSpace(lyricsText))
				{
					try
					{
						string saveLrcFileDefaultEncoding = Settings.Default.SaveLrcFileDefaultEncoding;
						File.WriteAllText(PathFileUtilities.BuildLyricSavePath(failureReporter.fileContext.filePath, failureReporter.tagState), lyricsText, Encoding.GetEncoding(saveLrcFileDefaultEncoding));
					}
					catch (System.Exception ex)
					{
						reportFailure(Resources.Msg_WriteLrcFileFail + ", " + ex.Message);
					}
				}

				if (undoSnapshot["tags_history_serial"] is string historySerial)
				{
					TagHistoryRepository.DeleteHistoryBySerial(historySerial, tagHistoryRepository);
				}

				restoredCount++;
			}
			else
			{
				reportFailure(null);
				failedCount++;
			}

			return shouldRestoreLastWriteTime;
		}
	}

	private sealed class UndoSaveTagsFileContext
	{
		public string filePath;

		public UndoSaveTagsTaskContext taskContext;
	}

	private sealed class UndoSaveTagFailureReporter
	{
		public ConfigDescriptorState tagState;

		public UndoSaveTagsFileContext fileContext;

		internal void ReportFailure(string fallbackMessage)
		{
			string errorMessage = ResolveFailureMessage(tagState.GetLoadError(), fallbackMessage);
			LogService.WriteSaveTagsLog(fileContext.filePath + ": " + errorMessage);
			fileContext.taskContext.messageLog.AddLine(fileContext.taskContext.currentFile.Name);
			fileContext.taskContext.messageLog.AddLine(errorMessage);
		}
	}

	private sealed class UndoSaveTagsListItemMatcher
	{
		public FileRow fileRow;

		internal bool MatchesSnapshotPath(ConfigDescriptorState reference)
		{
			// 保持原行为:原 `reference["filepath"] == listViewItem.Tag` 为 object==object 引用比较,
			// 这里对 (object) 显式比较以维持完全相同的运行时语义(FilePath 与旧 Tag 是同一 string 实例)。
			return reference["filepath"] == (object)fileRow.FilePath;
		}
	}

	private sealed class UndoRenameTaskContext : BatchFileTaskContext
	{
		public List<(string oldPath, string newPath, bool failed)> renameUndoOperations;

		public int successCount;

		public int failedCount;

		public int skippedCount;

		public Page errorLog;

		public StateFieldInstance owner;

		internal void UpdateProgress()
		{
			if (processedCount < renameUndoOperations.Count)
			{
				progressDialog.UpdateStatisticsProgress(Resources.Msg_Rename + currentFile?.Name, processedCount, renameUndoOperations.Count, successCount, failedCount, skippedCount, renameUndoOperations.Count);
			}
		}

		internal void UndoRenames()
		{
			TagHistoryRepository tagHistoryRepository = new TagHistoryRepository(useTransaction: true);
			try
			{
			while (processedCount < renameUndoOperations.Count)
			{
				if (cancellationSource.IsCancellationRequested)
				{
					break;
				}

				RenameUndoErrorRecorder errorRecorder = new RenameUndoErrorRecorder();
				errorRecorder.TaskContext = this;
				(string oldPath, string newPath, bool failed) operation = renameUndoOperations[processedCount];
				errorRecorder.CurrentPath = operation.newPath;
				string originalPath = operation.oldPath;
				currentFile = new FileInfo(errorRecorder.CurrentPath);
				Action<string> action = errorRecorder.RecordError;
					if (currentFile.Exists)
					{
						try
						{
							PathFileUtilities.MoveFileAllowingCaseOnlyRename(errorRecorder.CurrentPath, originalPath);
							owner.FileSettings.UpdateForAnyFile(errorRecorder.CurrentPath, originalPath);
							TagHistoryRepository.UpdateHistoryFilePath(errorRecorder.CurrentPath, originalPath, tagHistoryRepository);
							successCount++;
					}
					catch (System.Exception ex)
					{
						action(ex.Message);
						failedCount++;
						renameUndoOperations[processedCount] = (operation.oldPath, operation.newPath, true);
					}
				}
				else
				{
					action(Resources.Msg_FileNotFound);
					failedCount++;
				}

				processedCount++;
			}

			}
			finally
			{
				tagHistoryRepository.Dispose();
			}
		}
	}

	private sealed class RenameUndoErrorRecorder
	{
		public string CurrentPath;

		public UndoRenameTaskContext TaskContext;

		internal void RecordError(string errorMessage)
		{
			LogService.WriteSaveTagsLog(CurrentPath + ": " + errorMessage);
			TaskContext.errorLog.AddLine(TaskContext.currentFile.Name);
			TaskContext.errorLog.AddLine(errorMessage);
		}
	}

	private sealed class RenameUndoListItemMatcher
	{
		public FileRow fileRow;

		internal bool MatchesCurrentPath((string oldPath, string newPath, bool failed) operation)
		{
			return operation.newPath == fileRow.FilePath;
		}
	}

	private sealed class ClearTagsTaskContext : BatchFileTaskContext
	{
		public SelectedListViewItemInfo[] itemsToClear;

		public int successCount;

		public int failedCount;

		public bool canCancelFileReadonly;

		public Page errorLog;

		internal void UpdateProgress()
		{
			ProgressDialog progressDialog = this.progressDialog;
			string text = Resources.Msg_Cleartag;
			FileInfo fileInfo = currentFile;
			progressDialog.UpdateListRangeProgress(text + fileInfo?.Name, processedCount, itemsToClear.Length, successCount, failedCount, itemsToClear.Length);
		}

		internal void ClearTags()
		{
			TagHistoryRepository tagHistoryRepository = new TagHistoryRepository(useTransaction: true);
			try
			{
				TagHistoryRepository.ClearUndoState();
				while (processedCount < itemsToClear.Length)
				{
					if (cancellationSource.IsCancellationRequested)
					{
						break;
					}

					TagSaveFileContext tagSaveFileContext = new TagSaveFileContext();
					tagSaveFileContext.Owner = this;
					tagSaveFileContext.FilePath = Path.GetFullPath(itemsToClear[processedCount].FilePath);
					currentFile = new FileInfo(tagSaveFileContext.FilePath);
					DateTime lastWriteTime = default(DateTime);
					bool savedTags = false;
					TagSaveFailureReporter failureReporter = new TagSaveFailureReporter();
					failureReporter.FileContext = tagSaveFileContext;
					try
					{
						PathFileUtilities.ClearReadOnlyIfAllowed(currentFile, canCancelFileReadonly);
						lastWriteTime = currentFile.LastWriteTime;
						failureReporter.TagFile = new ConfigDescriptorState(failureReporter.FileContext.FilePath);
						Action<string> action = failureReporter.ReportFailure;
						if (failureReporter.TagFile.IsLoadedSuccessfully())
						{
							savedTags = true;
							failureReporter.TagFile.LoadBasicTagFields();
							failureReporter.TagFile.LoadLyrics();
							failureReporter.TagFile.LoadAllPictures();
							ConfigDescriptorState configDescriptorState = TagHistoryRepository.CreateTagSnapshot(failureReporter.TagFile, includePictures: true);
							if (failureReporter.TagFile.SaveCurrentTagFile())
							{
								var (historyMessage, selection) = TagHistoryRepository.AddHistoryRecordIfChanged(failureReporter.FileContext.FilePath, configDescriptorState, null, tagHistoryRepository);
								if (historyMessage != null)
								{
									action(historyMessage);
								}
								string undoMessage = TagHistoryRepository.AddUndoRecord(configDescriptorState, selection, tagHistoryRepository);
								if (undoMessage != null)
								{
									action(undoMessage);
								}
								successCount++;
							}
							else
							{
								action(null);
								failedCount++;
							}
						}
						else
						{
							action(null);
							failedCount++;
						}
					}
					catch (System.Exception ex)
					{
						LogService.WriteClearTagsLog(tagSaveFileContext.FilePath + ": " + ex.Message);
						errorLog.AddLine(currentFile.Name);
						errorLog.AddLine(ex.Message);
						failedCount++;
					}
					finally
					{
						if (failureReporter.TagFile != null)
						{
							((IDisposable)failureReporter.TagFile).Dispose();
						}
					}

					if (savedTags && Settings.Default.SaveTagsKeepUpdateTime)
					{
						try
						{
							currentFile.LastWriteTime = lastWriteTime;
						}
						catch (System.Exception ex)
						{
							LogService.WriteClearTagsLog(tagSaveFileContext.FilePath + ": " + ex.Message);
							errorLog.AddLine(currentFile.Name);
							errorLog.AddLine(ex.Message);
						}
					}

					processedCount++;
				}
			}
			finally
			{
				tagHistoryRepository.Dispose();
			}
		}
	}

	private sealed class TagSaveFileContext
	{
		public string FilePath;

		public ClearTagsTaskContext Owner;
	}

	private sealed class TagSaveFailureReporter
	{
		public ConfigDescriptorState TagFile;

		public TagSaveFileContext FileContext;

		internal void ReportFailure(string fallbackMessage)
		{
			string message = ResolveFailureMessage(TagFile.GetLoadError(), fallbackMessage);
			LogService.WriteClearTagsLog(FileContext.FilePath + ": " + message);
			FileContext.Owner.errorLog.AddLine(FileContext.Owner.currentFile.Name);
			FileContext.Owner.errorLog.AddLine(message);
		}
	}

	private sealed class DeleteFilesTaskContext : BatchFileTaskContext
	{
		public SelectedListViewItemInfo[] itemsToDelete;

		public int deletedCount;

		public Page errorLog;

		public int failedCount;

		public StateFieldInstance owner;

		internal void UpdateProgress()
		{
			progressDialog.UpdateCountProgress(Resources.Msg_Deletefile + currentFile?.Name, processedCount, itemsToDelete.Length);
		}

		internal void DeleteFiles()
		{
			TagHistoryRepository tagHistoryRepository = new TagHistoryRepository(useTransaction: true);
			try
			{
			while (processedCount < itemsToDelete.Length && !cancellationSource.IsCancellationRequested)
			{
				string filePath = itemsToDelete[processedCount].FilePath;
				currentFile = new FileInfo(filePath);
				try
				{
					if (currentFile.Exists)
					{
						currentFile.Delete();
						deletedCount++;
						TagHistoryRepository.DeleteHistoryByFilePath(filePath, tagHistoryRepository);
						TagHistoryRepository.RemovePendingUndoForFile(filePath);
					}
					else
					{
						errorLog.AddLine(currentFile.Name);
						errorLog.AddLine(Resources.Msg_FileNotFound);
						failedCount++;
					}
				}
				catch (System.Exception ex)
				{
					errorLog.AddLine(currentFile.Name);
					errorLog.AddLine(ex.Message);
					failedCount++;
				}

				processedCount++;
			}

			}
			finally
			{
				tagHistoryRepository.Dispose();
			}
		}

		internal void ShowCompletionResult()
		{
			(string, bool) result = BuildDeleteFilesResultMessage(itemsToDelete.Length, deletedCount, failedCount, processedCount, errorLog);
			if (result.Item2)
			{
				DialogService.ShowErrorMessage(result.Item1);
			}
			else
			{
				DialogService.ShowInformationMessage(result.Item1);
			}

			owner.removeItemsMenuItem.PerformClick();
		}
	}

	private sealed class SaveLrcFilesTaskContext : BatchFileTaskContext
	{
		public SelectedListViewItemInfo[] itemsToSave;

		public Page errorLog;

		public int savedCount;

		public int failedCount;

		public int skippedCount;

		internal void UpdateProgress()
		{
			progressDialog.UpdateCountProgress(Resources.Msg_SaveLrcFile + currentFile?.Name, processedCount, itemsToSave.Length);
		}

		internal void SaveLrcFiles()
		{
			while (processedCount < itemsToSave.Length && !cancellationSource.IsCancellationRequested)
			{
				SaveLrcErrorRecorder errorRecorder = new SaveLrcErrorRecorder();
				errorRecorder.taskContext = this;
				errorRecorder.filePath = Path.GetFullPath(itemsToSave[processedCount].FilePath);
				currentFile = new FileInfo(errorRecorder.filePath);
				ConfigDescriptorState configDescriptorState = new ConfigDescriptorState(errorRecorder.filePath);
				try
				{
					Action<string> action = errorRecorder.RecordError;
					if (configDescriptorState.IsLoadedSuccessfully())
					{
						configDescriptorState.LoadBasicTagFields();
						configDescriptorState.LoadLyrics();
						if (configDescriptorState["lyrics"] is string text && !string.IsNullOrWhiteSpace(text))
						{
							try
							{
								string saveLrcFileDefaultEncoding = Settings.Default.SaveLrcFileDefaultEncoding;
								File.WriteAllText(PathFileUtilities.BuildLyricSavePath(errorRecorder.filePath, configDescriptorState), text, Encoding.GetEncoding(saveLrcFileDefaultEncoding));
								savedCount++;
							}
							catch (System.Exception ex)
							{
								action(ex.Message);
								failedCount++;
							}
						}
						else
						{
							skippedCount++;
						}
					}
					else
					{
						skippedCount++;
					}
				}
				finally
				{
					if (configDescriptorState != null)
					{
						((IDisposable)configDescriptorState).Dispose();
					}
				}

				processedCount++;
			}
		}

		internal void ShowCompletionResult()
		{
			if (itemsToSave.Length > 1)
			{
				DialogService.ShowInformationMessage(string.Format(Resources.Msg_SaveLrcFilesComplete1, PathFileUtilities.GetLyricSaveDirectoryDisplayName()) + "\n" + string.Format(Resources.Msg_OK_Fail_Skip_Count, savedCount, failedCount, skippedCount, processedCount) + "\n" + errorLog.ToString());
				return;
			}
			if (savedCount <= 0)
			{
				DialogService.ShowErrorMessage(errorLog.ToString());
				return;
			}
			DialogService.ShowInformationMessage(string.Format(Resources.Msg_SaveLrcFilesComplete1, PathFileUtilities.GetLyricSaveDirectoryDisplayName()));
		}
	}

	private sealed class SaveLrcErrorRecorder
	{
		public string filePath;

		public SaveLrcFilesTaskContext taskContext;

		internal void RecordError(string errorMessage)
		{
			string message = ResolveFailureMessage(errorMessage);
			LogService.WriteSaveLyricsLog(filePath + ": " + message);
			taskContext.errorLog.AddLine(taskContext.currentFile.Name);
			taskContext.errorLog.AddLine(message);
		}
	}

	private sealed class ExtractCoversTaskContext : BatchFileTaskContext
	{
		public SelectedListViewItemInfo[] itemsToExtract;

		public Page errorLog;

		public int extractedCount;

		public int skippedCount;

		public int failedCount;

		internal void UpdateProgress()
		{
			progressDialog.UpdateCountProgress(Resources.Msg_ExtractCover + currentFile?.Name, processedCount, itemsToExtract.Length);
		}

		internal void ExtractCovers()
		{
			while (processedCount < itemsToExtract.Length && !cancellationSource.IsCancellationRequested)
			{
				ExtractCoverErrorRecorder errorRecorder = new ExtractCoverErrorRecorder();
				errorRecorder.TaskContext = this;
				errorRecorder.FilePath = Path.GetFullPath(itemsToExtract[processedCount].FilePath);
				currentFile = new FileInfo(errorRecorder.FilePath);
				using (ConfigDescriptorState configDescriptorState = new ConfigDescriptorState(errorRecorder.FilePath))
				{
					Action<string> action = errorRecorder.RecordError;
					if (configDescriptorState.IsLoadedSuccessfully())
					{
						configDescriptorState.LoadPictureSummary(flagOnly: false);
						if (configDescriptorState["haspicture"] is bool hasPicture && hasPicture && configDescriptorState["picturedata"] is byte[] array)
						{
							try
							{
								ConfigDescriptorState.PictureData pictureData = new ConfigDescriptorState.PictureData
								{
									ImageBytes = array
								};
								using (ConfigDescriptorState.LoadPictureImage(pictureData))
								{
								}
								if (pictureData.MimeType != null && pictureData.Width > 0 && pictureData.Height > 0)
								{
									string imageExtension = ImageUtilities.GetImageExtensionForMimeType(pictureData.MimeType, ".jpg");
									File.WriteAllBytes(PathFileUtilities.GetSiblingPathWithExtension(errorRecorder.FilePath, imageExtension), array);
									extractedCount++;
								}
								else
								{
									skippedCount++;
								}
							}
							catch (System.Exception ex)
							{
								action(ex.Message);
								failedCount++;
							}
						}
						else
						{
							skippedCount++;
						}
					}
					else
					{
						skippedCount++;
					}
				}

				processedCount++;
			}
		}

		internal void ShowCompletionResult()
		{
			(string, bool) result = BuildExtractCoversResultMessage(itemsToExtract.Length, extractedCount, failedCount, skippedCount, processedCount, errorLog);
			if (result.Item2)
			{
				DialogService.ShowErrorMessage(result.Item1);
			}
			else
			{
				DialogService.ShowInformationMessage(result.Item1);
			}
		}
	}

	private sealed class ExtractCoverErrorRecorder
	{
		public string FilePath;

		public ExtractCoversTaskContext TaskContext;

		internal void RecordError(string errorMessage)
		{
			string text = ResolveFailureMessage(errorMessage);
			LogService.WriteSaveCoversLog(FilePath + ": " + text);
			TaskContext.errorLog.AddLine(TaskContext.currentFile.Name);
			TaskContext.errorLog.AddLine(text);
		}
	}

	private sealed class RestoreHistoryTagsContext
	{
		public ConfigDescriptorState selectedTagState;

		public StateFieldInstance owner;

		internal void ApplyTagField(string fieldName)
		{
			owner.tagComboBoxes[fieldName].Text = selectedTagState.GetDisplayValue(fieldName);
		}
	}

	private sealed class RenameFilesCompletionContext
	{
		public (string Path, string NewPath, int Index)[] renameItems;

		public StateFieldInstance owner;

		internal void RefreshAfterRename((string msg, bool isErr) message)
		{
			foreach (var renameItem in renameItems)
			{
				if (renameItem.NewPath != null)
				{
					FileRow fileRow = owner.cachedFileListItems[renameItem.Index];
					fileRow.FilePath = renameItem.NewPath;
					owner.InvalidateFileRow(fileRow);
				}
			}
			owner.RefreshSelectedItems(showErrorMessageBox: false, showProgressDialog: true, refreshStatusAllInfo: true, previousMessage: message);
		}
	}

	private sealed class AppSettingsSaveTask
	{
		public AppSettingData appSettingsData;

		public string sortSettingJson;

		public string mainFormPosSizeInfoJson;

		public string filterListViewType;

		public string filterListViewKeyword;

		internal void SaveSettings()
		{
			SaveAppSettingData();

			Settings.Default.SortSetting = sortSettingJson;
			Settings.Default.MainFormPosSizeInfo = mainFormPosSizeInfoJson;
			Settings.Default.FilterListViewType = filterListViewType;
			Settings.Default.FilterListViewKeyword = filterListViewKeyword;
			Settings.Default.LastVersionCode = 17;
			CustomColumnsDialog.SaveColumnHeaderSettings();
			DialogService.TrySaveApplicationSettings();
			TagHistoryRepository.ClearUndoState();
			TagHistoryRepository.CloseSharedConnection();
		}

		private void SaveAppSettingData()
		{
			string appSettingDataPath = AppSettingData.AppSettingDataPath;
			string temporaryPath = appSettingDataPath + ".tmp";
			string backupPath = appSettingDataPath + ".bak";
			try
			{
				appSettingsData.Save(temporaryPath);
				if (File.Exists(appSettingDataPath))
				{
					File.Replace(temporaryPath, appSettingDataPath, backupPath);
				}
				else
				{
					File.Move(temporaryPath, appSettingDataPath);
				}
			}
			catch
			{
				TryDeleteFile(temporaryPath);
				throw;
			}
		}

		private static void TryDeleteFile(string filePath)
		{
			try
			{
				if (File.Exists(filePath))
				{
					File.Delete(filePath);
				}
			}
			catch (IOException)
			{
			}
			catch (UnauthorizedAccessException)
			{
			}
		}
	}

	private sealed class FileListLabelEditContext
	{
		public string OriginalPath;

		public string RequestedFileName;

		public string NewPath;

		public System.Exception RenameError;

		internal void UpdateHistoryFilePath()
		{
			using TagHistoryRepository history = new TagHistoryRepository(useTransaction: false);
			TagHistoryRepository.UpdateHistoryFilePath(OriginalPath, NewPath, history);
		}

		internal void ShowFileNotFoundMessage()
		{
			DialogService.ShowErrorMessage((NewPath ?? RequestedFileName) + "\n" + Resources.Msg_FileNotFound);
		}

		internal void ShowRenameError()
		{
			DialogService.ShowErrorMessage((NewPath ?? RequestedFileName) + "\n" + RenameError.Message);
		}
	}

	private sealed class MessageBoxWindowCollector
	{
		public int MainWindowThreadId;

		public StringBuilder ClassNameBuffer;

		public StringBuilder WindowTextBuffer;

		public List<IntPtr> MatchingWindowHandles;

		private NativeMethods.EnumThreadWindowsCallback enumThreadWindowsCallback;

		internal bool IsNotMainWindowThread(ProcessThread thread)
		{
			return thread.Id != MainWindowThreadId;
		}

		internal void CollectFromThread(ProcessThread thread)
		{
			NativeMethods.EnumThreadWindows(thread.Id, enumThreadWindowsCallback ?? (enumThreadWindowsCallback = CollectIfMatchingDialog), IntPtr.Zero);
		}

		internal bool CollectIfMatchingDialog(IntPtr windowHandle, int lParam)
		{
			NativeMethods.GetClassName(windowHandle, ClassNameBuffer, ClassNameBuffer.Capacity);
			if (ClassNameBuffer.ToString() != "#32770")
			{
				return true;
			}

			NativeMethods.SendTextBufferMessage(windowHandle, 13, WindowTextBuffer.Capacity, WindowTextBuffer);
			string windowText = WindowTextBuffer.ToString();
			if (windowText == Resources.Information || windowText == Resources.PolicyTokenExporter || windowText == Resources.Confirmation)
			{
				MatchingWindowHandles.Add(windowHandle);
			}
			return true;
		}
	}

	private ComponentResourceManager localizedResources;

	private static string currentLanguageCode;

	private static readonly List<CustomColumnsDialog.ColumnHeaderInfo> configuredColumnHeaders;

	public static Dictionary<string, string> EnabledTagTypesByExtension;

	private readonly ImageList fileTypeImageList;

	private readonly Dictionary<string, Image> fileTypeIconCache;

	private readonly object fileTypeIconCacheLock = new object();

	// 文件列表字体 —— 由字段持有,避免 ApplyFileListVisualStyle 每次 new 出的 Font 句柄无人释放。
	private readonly Font fileListFont = new Font("Tahoma", 9f, FontStyle.Regular, GraphicsUnit.Point, 0);

	private readonly Dictionary<string, ComboBox> tagComboBoxes;

	private readonly Dictionary<string, (Label label, EventHandler cbTextChangedEvent)> tagFieldTextHandlers;

	private readonly Dictionary<string, (Dictionary<string, int> valueCounts, List<(string value, bool wasRemoved)> changedValues)> selectedFilterValueStates;

	private readonly string[] editableTagFieldNames;

	private string lastFileListFilterText;

	private Button[] tagEncodingButtons;

	private ListViewFileSetting fileSettings;

	private ConfigDescriptorState selectedTagState;

	private List<ConfigDescriptorState.PictureData> multiSelectionCoverList;

	private bool compressCoverResolution;

	private int currentCoverIndex;

	private readonly TaskbarProgressController taskbarProgress;

	private bool hasShownMainForm;

	private bool skipSavingSettingsOnClose;

	private readonly string[] startupFileArgs;

	public static readonly Dictionary<string, string> KnownTagTypesByExtension;

	private int lastTagPanelSplitterDistance;

	private int lastCoverPreviewSize;

	private int lastTagEditorAvailableHeight;

	private bool lastTagEditorControlsOverflow;

	private FormWindowState restoreWindowState;

	private IContainer components;

	private MenuStrip mainMenuStrip;

	private ToolStripMenuItem fileMenuItem;

	private ToolStripSeparator fileMenuTagActionsSeparator;

	private ToolStripMenuItem saveTagsMenuItem;

	private ToolStripSeparator fileMenuExitSeparator;

	private ToolStripMenuItem exitMenuItem;

	private ToolStripMenuItem editMenuItem;

	private ToolStripMenuItem selectAllFilesMenuItem;

	private ToolStripMenuItem toolsMenuItem;

	private ToolStripMenuItem optionsMenuItem;

	private ToolStripMenuItem helpMenuItem;

	private ToolStripMenuItem aboutMenuItem;

	private ToolStrip mainToolStrip;

	private ToolStripButton saveTagsToolStripButton;

	private ToolStripSeparator tagActionsToolbarSeparator;

	private DoubleBufferedSplitContainer mainSplitContainer;

	private FlowLayoutPanel tagEditorPanel;

	private Label titleLabel;

	private ComboBox titleComboBox;

	private Label artistLabel;

	private ComboBox artistComboBox;

	private Label albumLabel;

	private ComboBox albumComboBox;

	private Label yearLabel;

	private ComboBox yearComboBox;

	private Label genreLabel;

	private ComboBox genreComboBox;

	private Label albumArtistLabel;

	private ComboBox albumArtistComboBox;

	private Label composerLabel;

	private ComboBox composerComboBox;

	private Label commentLabel;

	private ComboBox commentComboBox;

	private PictureBox coverPictureBox;

	private OpenFileDialog localCoverFileDialog;

	private Label lyricsLabel;

	private ComboBox lyricsComboBox;

	private FlowLayoutPanel coverPanel;

	private ToolStripMenuItem addDirectoryMenuItem;

	private ToolStripButton addDirectoriesToolStripButton;

	private FlowLayoutPanel statusLabelsPanel;

	private Label coverMimeTypeLabel;

	private Label coverDimensionsLabel;

	private Label coverFileSizeLabel;

	private Label coverPictureTypeLabel;

	private FlowLayoutPanel coverNavigationPanel;

	private Button previousCoverButton;

	private Label coverIndexLabel;

	private Button nextCoverButton;

	private ToolStripMenuItem languageMenuItem;

	private ToolStripMenuItem englishLanguageMenuItem;

	private ToolStripMenuItem simplifiedChineseLanguageMenuItem;

	private ToolStripMenuItem traditionalChineseLanguageMenuItem;

	private FlowLayoutPanel lyricsRowPanel;

	private Button editLyricsButton;

	private ContextMenuStrip coverContextMenu;

	private ToolStripMenuItem addCoverMenuItem;

	private ToolStripMenuItem removeCoverMenuItem;

	private ToolStripMenuItem extractCoverMenuItem;

	private SaveFileDialog extractCoverSaveFileDialog;

	private ToolStripMenuItem chooseLocalCoverMenuItem;

	private ToolStripMenuItem searchCoverFromNetworkMenuItem;

	private ToolStripMenuItem unselectAllFilesMenuItem;

	private ToolStripMenuItem invertFileSelectionMenuItem;

	private ToolStripButton selectAllFilesToolStripButton;

	private ToolStripSeparator editSelectionSeparator;

	private ToolStripMenuItem removeItemsMenuItem;

	private ToolStripButton unselectAllFilesToolStripButton;

	private ToolStripMenuItem readTagsMenuItem;

	private ToolStripMenuItem viewMenuItem;

	private ToolStripMenuItem refreshMenuItem;

	private ToolStripSeparator selectionToolbarSeparator;

	private ToolStripButton refreshToolStripButton;

	private ToolStripMenuItem customizeColumnsMenuItem;

	private ToolStripMenuItem removeTagsMenuItem;

	private ToolStripButton removeTagsToolStripButton;

	private ToolStripSeparator coverContextMenuSeparator;

	private ToolStripMenuItem coverTypeMenuItem;

	private FlowLayoutPanel titleRowPanel;

	private Button titleEncodingButton;

	private FlowLayoutPanel artistRowPanel;

	private Button artistEncodingButton;

	private FlowLayoutPanel albumRowPanel;

	private Button albumEncodingButton;

	private FlowLayoutPanel yearRowPanel;

	private Button yearEncodingButton;

	private FlowLayoutPanel genreRowPanel;

	private Button genreEncodingButton;

	private FlowLayoutPanel albumArtistRowPanel;

	private Button albumArtistEncodingButton;

	private FlowLayoutPanel composerRowPanel;

	private Button composerEncodingButton;

	private FlowLayoutPanel commentRowPanel;

	private Button commentEncodingButton;

	private Button lyricsEncodingButton;

	private ToolStripButton characterSetToolStripButton;

	private ToolStripSeparator sourceToolbarSeparator;

	private CheckBox overwriteCoverCheckBox;

	private ToolTip controlToolTip;

	private ToolStripMenuItem tagSourcesMenuItem;

	private ToolStripMenuItem coverSourceMenuItem;

	private ToolStripMenuItem defaultCoverSourceMenuItem;

	private ToolStripMenuItem lyricSourceMenuItem;

	private ToolStripMenuItem defaultLyricSourceMenuItem;

	private ToolStripSplitButton coverSourceToolStripSplitButton;

	private ToolStripSplitButton lyricSourceToolStripSplitButton;

	private ToolStripMenuItem combinedTagSourceMenuItem;

	private ToolStripSplitButton combinedTagSourceToolStripSplitButton;

	private ToolStripMenuItem defaultCombinedTagSourceMenuItem;

	private ToolStripSeparator batchToolbarOptionsSeparator;

	private ToolStripButton optionsToolStripButton;

	private ToolStripMenuItem changeDirectoryMenuItem;

	private ToolStripButton changeDirectoryToolStripButton;

	private ToolStripMenuItem manageDirectoriesMenuItem;

	private ToolStripButton manageDirectoriesToolStripButton;

	private ToolStripSeparator directoryToolbarSeparator;

	private ContextMenuStrip fileListHeaderContextMenu;

	private ToolStripMenuItem customizeColumnsContextMenuItem;

	private ContextMenuStrip fileListItemContextMenu;

	private ToolStripMenuItem saveTagsContextMenuItem;

	private ToolStripMenuItem removeTagsContextMenuItem;

	private ToolStripSeparator fileListItemContextSeparator;

	private ToolStripMenuItem removeItemsContextMenuItem;

	private ToolStripMenuItem removeFilesMenuItem;

	private ToolStripMenuItem removeFilesContextMenuItem;

	private ToolStripMenuItem renameFileMenuItem;

	private ToolStripMenuItem renameFileContextMenuItem;

	private System.Windows.Forms.Timer selectionStatusUpdateTimer;

	private ToolStripMenuItem readTagsContextMenuItem;

	private StatusStrip fileSummaryStatusStrip;

	private ToolStripStatusLabel selectedFilesStatusLabel;

	private ToolStripStatusLabel totalFilesStatusLabel;

	private ToolStripSeparator fileSummaryStatusSeparator;

	private BufferedDataGridView fileListView;

	private ToolStripMenuItem characterSetMenuItem;

	private ToolStripMenuItem characterSetContextMenuItem;

	private ToolStripMenuItem chooseCoverFromTagsMenuItem;

	private ToolStripMenuItem openCoverMenuItem;

	private ToolStripMenuItem openDirectoryMenuItem;

	private ToolStripMenuItem openDirectoryContextMenuItem;

	private ToolStripMenuItem batchMenuItem;

	private ToolStripMenuItem batchLyricMenuItem;

	private ToolStripMenuItem reformatLyricTimeTagsMenuItem;

	private ToolStripMenuItem removeLyricTimeTagsMenuItem;

	private ToolStripMenuItem deleteBlankLyricLinesMenuItem;

	private ToolStripMenuItem deleteLyricHeaderTagsMenuItem;

	private ToolStripMenuItem saveLyricsMenuItem;

	private ToolStripMenuItem importLrcFilesMenuItem;

	private ToolStripSeparator batchToolbarStartSeparator;

	private ToolStripSplitButton batchSaveAsLrcToolStripSplitButton;

	private ToolStripMenuItem reformatLyricTimeTagsToolStripMenuItem;

	private ToolStripMenuItem removeLyricTimeTagsToolStripMenuItem;

	private ToolStripMenuItem deleteBlankLyricLinesToolStripMenuItem;

	private ToolStripMenuItem deleteLyricHeaderTagsToolStripMenuItem;

	private ToolStripMenuItem importLrcFilesToolStripMenuItem;

	private ToolStripMenuItem batchExtractCoverMenuItem;

	private ToolStripButton batchExtractCoverToolStripButton;

	private ToolStripMenuItem batchAutoMatchTagsMenuItem;

	private ToolStripButton batchAutoMatchTagsToolStripButton;

	private ToolStripButton readTagsToolStripButton;

	private System.Windows.Forms.Timer fileListStatusTimer;

	private Panel tagEditorBottomSpacerPanel;

	private ToolStripButton batchFilenameRelatedToolStripButton;

	private ToolStripMenuItem batchFilenameRelatedMenuItem;

	private ToolStripMenuItem checkForUpdatesMenuItem;

	private ToolStripMenuItem tagHistoryMenuItem;

	private ToolStripButton tagHistoryToolStripButton;

	private ToolStripMenuItem tagHistoryContextMenuItem;

	private ToolStripMenuItem undoMenuItem;

	private ToolStripButton undoToolStripButton;

	private FlowLayoutPanel trackDiscGroupPanel;

	private FlowLayoutPanel trackColumnPanel;

	private Label trackLabel;

	private FlowLayoutPanel trackRowPanel;

	private ComboBox trackComboBox;

	private Button trackEncodingButton;

	private FlowLayoutPanel discColumnPanel;

	private Label discLabel;

	private FlowLayoutPanel discRowPanel;

	private ComboBox discComboBox;

	private Button discEncodingButton;

	private Label lyricistLabel;

	private FlowLayoutPanel lyricistRowPanel;

	private ComboBox lyricistComboBox;

	private Button lyricistEncodingButton;

	private StatusStrip fileFilterStatusStrip;

	private ToolStripStatusLabel filterStatusLabel;

	private ToolStripTextBox filterTextBox;

	private ToolStripDropDownButton filterTypeDropDownButton;

	private System.Windows.Forms.Timer filterInputTimer;

	private ToolStripMenuItem musicTagWebsiteMenuItem;

	private System.Windows.Forms.Timer renamedFilesRefreshTimer;

	private System.Windows.Forms.Timer comboBoxSelectionResetTimer;

	private ToolStripDropDownButton chineseConversionToolStripDropDownButton;

	private ToolStripMenuItem convertTagsTraditionalToSimplifiedToolStripMenuItem;

	private ToolStripMenuItem convertTagsSimplifiedToTraditionalToolStripMenuItem;

	private ToolStripMenuItem chineseConversionMenuItem;

	private ToolStripMenuItem convertTagsTraditionalToSimplifiedMenuItem;

	private ToolStripMenuItem convertTagsSimplifiedToTraditionalMenuItem;

	private ToolStripDropDownButton batchChineseConversionToolStripDropDownButton;

	private ToolStripMenuItem batchChineseConversionMenuItem;

	private ToolStripMenuItem batchTagsTraditionalToSimplifiedMenuItem;

	private ToolStripMenuItem batchTagsSimplifiedToTraditionalMenuItem;

	private ToolStripMenuItem batchFilenameTraditionalToSimplifiedMenuItem;

	private ToolStripMenuItem batchFilenameSimplifiedToTraditionalMenuItem;

	private ToolStripMenuItem batchTagsTraditionalToSimplifiedToolStripMenuItem;

	private ToolStripMenuItem batchTagsSimplifiedToTraditionalToolStripMenuItem;

	private ToolStripMenuItem batchFilenameTraditionalToSimplifiedToolStripMenuItem;

	private ToolStripMenuItem batchFilenameSimplifiedToTraditionalToolStripMenuItem;

	private NotifyIcon notifyIcon;

	private ContextMenuStrip notifyContextMenu;

	private ToolStripMenuItem notifyExitMenuItem;

	private ToolStripMenuItem changeCoverResolutionMenuItem;

	private List<FileRow> cachedFileListItems { get; }

	// DataGridView VirtualMode 的 RowCount 数据源:cachedFileListItems 中未被过滤隐藏的子集(保持主表顺序)。
	private List<FileRow> visibleRows = new List<FileRow>();

	// visibleRows 的 行 → 下标 反查(InvalidateRow / 恢复选区 用),随 visibleRows 重建。
	private readonly Dictionary<FileRow, int> visibleRowIndex = new Dictionary<FileRow, int>();

	private readonly HashSet<FileRow> selectedVisibleRows = new HashSet<FileRow>();

	private int selectedFileCount;

	private FileRow singleSelectedVisibleRow;

	private int fileListIconSize;

	private int fileListIconPadding;

	// 抑制 DGV SelectionChanged 处理(过滤/排序/全选反选等程序化改选区时置 true,避免重入)。
	private bool suppressFileListSelectionEvents;

	// 就地重命名状态:正在编辑的行 + CellValuePushed 捕获的新文件名(VirtualMode 下编辑值不入模型,需自存)。
	private FileRow renamingRow;

	private string renameEditedValue;

	private FormPosSizeInfo MainFormPosSizeInfo { get; set; }

	private ListViewItemNaturalComparer.ListViewSortSetting SortSetting { get; set; }

	public static string CurrentLanguageCode => currentLanguageCode;

	private string[] GetEditableTagFieldNames()
	{
		return editableTagFieldNames;
	}

	private ListViewFileSetting FileSettings
	{
		get => fileSettings;
		set => fileSettings = value;
	}

	private bool HasFileTypeIcon(string extension)
	{
		lock (fileTypeIconCacheLock)
		{
			return fileTypeIconCache.ContainsKey(extension);
		}
	}

	private Image GetFileTypeIcon(string extension)
	{
		lock (fileTypeIconCacheLock)
		{
			fileTypeIconCache.TryGetValue(extension, out Image iconImage);
			return iconImage;
		}
	}

	private void CacheFileTypeIcon(string extension, Image iconImage)
	{
		lock (fileTypeIconCacheLock)
		{
			if (fileTypeIconCache.ContainsKey(extension))
			{
				iconImage.Dispose();
				return;
			}
			fileTypeIconCache.Add(extension, iconImage);
		}
	}

	static StateFieldInstance()
	{
		configuredColumnHeaders = CustomColumnsDialog.GetColumnHeaderSettings();
		KnownTagTypesByExtension = new Dictionary<string, string>
		{
			{ ".aac", "AAC" },
			{ ".aiff", "AIFF" },
			{ ".aif", "AIFF" },
			{ ".aifc", "AIFF" },
			{ ".ape", "APE" },
			{ ".dff", "DFF" },
			{ ".dsf", "DSF" },
			{ ".flac", "FLAC" },
			{ ".mpc", "MPC" },
			{ ".mp3", "MPEG" },
			{ ".mp4", "MP4" },
			{ ".m4a", "MP4" },
			{ ".ogg", "OGG" },
			{ ".opus", "OPUS" },
			{ ".tak", "TAK" },
			{ ".wav", "WAV" },
			{ ".wma", "ASF" },
			{ ".wv", "WAVPACK" }
		};
		EnabledTagTypesByExtension = new Dictionary<string, string>();
		if (Settings.Default.LastVersionCode < 12)
		{
			List<string> restrictedExtensions = Settings.Default.RestrictFileExts.Split(new char[1] { ';' }, StringSplitOptions.RemoveEmptyEntries).ToList();
			if (!restrictedExtensions.Contains(".aac"))
			{
				restrictedExtensions.Add(".aac");
			}
			if (!restrictedExtensions.Contains(".tak"))
			{
				restrictedExtensions.Add(".tak");
			}
			restrictedExtensions.Sort();
			restrictedExtensions.Add("");
			Settings.Default.RestrictFileExts = string.Join(";", restrictedExtensions);
		}
		foreach (string extension in Settings.Default.RestrictFileExts.Split(new char[1] { ';' }, StringSplitOptions.RemoveEmptyEntries))
		{
			if (KnownTagTypesByExtension.TryGetValue(extension, out string tagType) && !EnabledTagTypesByExtension.ContainsKey(extension))
			{
				EnabledTagTypesByExtension.Add(extension, tagType);
			}
		}
		ThreadPool.SetMinThreads(20, 20);
		// net8 迁移:移除 netfx 遗留的 ServicePointManager.SecurityProtocol 设置(Ssl3|Tls|Tls11|Tls12)。
		// core 上包含 Ssl3 会抛 NotSupportedException,且 HttpClient 默认即系统 TLS 策略(1.2+)。
	}

	public StateFieldInstance(params string[] args)
	{
		localizedResources = new ComponentResourceManager(typeof(StateFieldInstance));
		fileTypeImageList = new ImageList();
		fileTypeIconCache = new Dictionary<string, Image>();
		tagComboBoxes = new Dictionary<string, ComboBox>();
		tagFieldTextHandlers = new Dictionary<string, (Label, EventHandler)>();
		selectedFilterValueStates = new Dictionary<string, (Dictionary<string, int> valueCounts, List<(string value, bool wasRemoved)> changedValues)>();
		cachedFileListItems = new List<FileRow>();
		editableTagFieldNames = new string[14]
		{
			"filename", "filedir", "tagtypes", "title", "artist", "album", "year", "trackstr", "discstr", "genre",
			"albumartist", "composer", "lyricist", "comment"
		};
		lastFileListFilterText = "";
		startupFileArgs = args;
		taskbarProgress = new TaskbarProgressController(this);
		InitializeComponent();
		// 构造期 DeviceDpi = 启动屏刻度,与下方各 Initialize* 使用的静态 ScaleByDpi 同基准。
		// startupDpi 供列宽持久化归一(见 SaveCurrentFileListColumnWidths);customAssetsDpi
		// 台账有初值后,首次跨屏 WM_DPICHANGED 才能按 previous→target 比率补缩 DGV 列宽。
		startupDpi = DeviceDpi;
		customAssetsDpi = DeviceDpi;
		RegisterEditableTagFields();
		ApplyToolbarImagesAndScaling();
		InitializeTagEditorControls();
		InitializeFileListColumnsAndIcons();
		InitializeFileListFilterMenu();
		ApplyLanguageResources();
		InitializeSourceMenus();
		ApplyTagPanelLayout();
		DpiTrace("ctor.done");
	}

	private void InitializeSourceMenus()
	{
		Icon icon = Resources.AppIcon;
		notifyIcon.Icon = icon;
		base.Icon = icon;
		foreach (SourceItem coverSourceItem in CoverSearchResult.GetCoverSourceSettings())
		{
			ToolStripItem contextMenuItem = coverSourceMenuItem.DropDownItems.Add(coverSourceItem.SearchSource.GetDisplayName());
			contextMenuItem.Tag = coverSourceItem.SearchSource;
			contextMenuItem.Click += CoverSourceMenuItem_Click;
			ToolStripItem toolbarMenuItem = coverSourceToolStripSplitButton.DropDownItems.Add(coverSourceItem.SearchSource.GetDisplayName());
			toolbarMenuItem.Tag = coverSourceItem.SearchSource;
			toolbarMenuItem.Click += CoverSourceMenuItem_Click;
		}
		foreach (SourceItem lyricSourceItem in LyricSearchResult.GetLyricSourceSettings())
		{
			ToolStripItem contextMenuItem = lyricSourceMenuItem.DropDownItems.Add(lyricSourceItem.SearchSource.GetDisplayName());
			contextMenuItem.Tag = lyricSourceItem.SearchSource;
			contextMenuItem.Click += LyricSourceMenuItem_Click;
			ToolStripItem toolbarMenuItem = lyricSourceToolStripSplitButton.DropDownItems.Add(lyricSourceItem.SearchSource.GetDisplayName());
			toolbarMenuItem.Tag = lyricSourceItem.SearchSource;
			toolbarMenuItem.Click += LyricSourceMenuItem_Click;
		}
		foreach (SourceItem tagSourceItem in TrackSearchResult.GetTagSourceSettings())
		{
			ToolStripItem contextMenuItem = combinedTagSourceMenuItem.DropDownItems.Add(tagSourceItem.SearchSource.GetDisplayName());
			contextMenuItem.Tag = tagSourceItem.SearchSource;
			contextMenuItem.Click += TagSourceMenuItem_Click;
			ToolStripItem toolbarMenuItem = combinedTagSourceToolStripSplitButton.DropDownItems.Add(tagSourceItem.SearchSource.GetDisplayName());
			toolbarMenuItem.Tag = tagSourceItem.SearchSource;
			toolbarMenuItem.Click += TagSourceMenuItem_Click;
		}
		notifyExitMenuItem.Click += ExitApplication_Click;
	}

	private void ApplyToolbarImagesAndScaling()
	{
		if (ImageUtilities.GetDpiScale() == 1f)
		{
			return;
		}
		Image image = (changeDirectoryToolStripButton.Image = ImageUtilities.LoadResourceBitmap("chgDirToolStripMenuItem_Image", scaleSmallIconForDpi: true));
		changeDirectoryMenuItem.Image = image;
		addDirectoriesToolStripButton.Image = image = ImageUtilities.LoadResourceBitmap("addDirsToolStripMenuItem_Image", scaleSmallIconForDpi: true);
		addDirectoryMenuItem.Image = image;
		image = (manageDirectoriesToolStripButton.Image = ImageUtilities.LoadResourceBitmap("manageDirsToolStripMenuItem_Image", scaleSmallIconForDpi: true));
		manageDirectoriesMenuItem.Image = image;
		image = (saveTagsToolStripButton.Image = ImageUtilities.LoadResourceBitmap("saveToolStripMenuItem_Image", scaleSmallIconForDpi: true));
		saveTagsMenuItem.Image = image;
		removeTagsToolStripButton.Image = image = ImageUtilities.LoadResourceBitmap("removeTagToolStripMenuItem_Image", scaleSmallIconForDpi: true);
		removeTagsMenuItem.Image = image;
		image = (undoToolStripButton.Image = ImageUtilities.LoadResourceBitmap("undoToolStripMenuItem_Image", scaleSmallIconForDpi: true));
		undoMenuItem.Image = image;
		readTagsToolStripButton.Image = image = ImageUtilities.LoadResourceBitmap("readTagsToolStripMenuItem_Image", scaleSmallIconForDpi: true);
		readTagsMenuItem.Image = image;
		image = (characterSetToolStripButton.Image = ImageUtilities.LoadResourceBitmap("characterSetToolStripMenuItem_Image", scaleSmallIconForDpi: true));
		characterSetMenuItem.Image = image;
		image = (chineseConversionToolStripDropDownButton.Image = ImageUtilities.LoadResourceBitmap("chschtToolStripMenuItem_Image", scaleSmallIconForDpi: true));
		chineseConversionMenuItem.Image = image;
		image = (tagHistoryToolStripButton.Image = ImageUtilities.LoadResourceBitmap("tagsHistoryToolStripMenuItem_Image", scaleSmallIconForDpi: true));
		tagHistoryMenuItem.Image = image;
		exitMenuItem.Image = ImageUtilities.LoadResourceBitmap("exitToolStripMenuItem_Image", scaleSmallIconForDpi: true);
		image = (selectAllFilesToolStripButton.Image = ImageUtilities.LoadResourceBitmap("selallfilesToolStripMenuItem_Image", scaleSmallIconForDpi: true));
		selectAllFilesMenuItem.Image = image;
		image = (unselectAllFilesToolStripButton.Image = ImageUtilities.LoadResourceBitmap("unselectAllToolStripMenuItem_Image", scaleSmallIconForDpi: true));
		unselectAllFilesMenuItem.Image = image;
		refreshToolStripButton.Image = image = ImageUtilities.LoadResourceBitmap("refreshToolStripMenuItem_Image", scaleSmallIconForDpi: true);
		refreshMenuItem.Image = image;
		image = (coverSourceToolStripSplitButton.Image = ImageUtilities.LoadResourceBitmap("picSrcToolStripMenuItem_Image", scaleSmallIconForDpi: true));
		coverSourceMenuItem.Image = image;
		image = (lyricSourceToolStripSplitButton.Image = ImageUtilities.LoadResourceBitmap("lyricSrcToolStripMenuItem_Image", scaleSmallIconForDpi: true));
		lyricSourceMenuItem.Image = image;
		image = (combinedTagSourceToolStripSplitButton.Image = ImageUtilities.LoadResourceBitmap("combTagsSrcToolStripMenuItem_Image", scaleSmallIconForDpi: true));
		combinedTagSourceMenuItem.Image = image;
		image = (batchAutoMatchTagsToolStripButton.Image = ImageUtilities.LoadResourceBitmap("batchAutoMatchTagsToolStripButton_Image", scaleSmallIconForDpi: true));
		batchAutoMatchTagsMenuItem.Image = image;
		image = (batchExtractCoverToolStripButton.Image = ImageUtilities.LoadResourceBitmap("batchExtractCoverToolStripButton_Image", scaleSmallIconForDpi: true));
		batchExtractCoverMenuItem.Image = image;
		image = (batchSaveAsLrcToolStripSplitButton.Image = ImageUtilities.LoadResourceBitmap("batchSaveAsLrcFileToolStripSplitButton_Image", scaleSmallIconForDpi: true));
		saveLyricsMenuItem.Image = image;
		image = (batchChineseConversionToolStripDropDownButton.Image = ImageUtilities.LoadResourceBitmap("chschtToolStripMenuItem_Image", scaleSmallIconForDpi: true));
		batchChineseConversionMenuItem.Image = image;
		image = (batchFilenameRelatedToolStripButton.Image = ImageUtilities.LoadResourceBitmap("batchFilenameRelToolStripButton_Image", scaleSmallIconForDpi: true));
		batchFilenameRelatedMenuItem.Image = image;
		image = (optionsToolStripButton.Image = ImageUtilities.LoadResourceBitmap("optionsToolStripMenuItem_Image", scaleSmallIconForDpi: true));
		optionsMenuItem.Image = image;
		ApplyToolbarItemSizesForDpi();
	}

	// PMv2(实验分支):工具栏/菜单的 ImageScalingSize 与各项固定尺寸按窗体当前所在屏刻度设置;
	// 构造期(上方,高 DPI 启动时)与 WM_DPICHANGED 后各调一次 —— ToolStripItem 不是 Control,
	// 框架跨屏缩放不覆盖其固定 Size。图像本身不重建(ImageScaling=SizeToFit 按 ImageScalingSize
	// 绘制,仅位图分辨率与目标不完全匹配时略降清晰度)。
	private void ApplyToolbarItemSizesForDpi()
	{
		Size scaledImageSize = new Size(ImageUtilities.ScaleByDpi(16f, this), ImageUtilities.ScaleByDpi(16f, this));
		coverContextMenu.ImageScalingSize = scaledImageSize;
		fileListItemContextMenu.ImageScalingSize = scaledImageSize;
		mainToolStrip.ImageScalingSize = scaledImageSize;
		mainMenuStrip.ImageScalingSize = scaledImageSize;
		foreach (ToolStripItem toolStripItem in mainToolStrip.Items)
		{
			toolStripItem.AutoSize = false;
			if (toolStripItem is ToolStripSplitButton toolStripSplitButton)
			{
				toolStripSplitButton.Size = new Size(ImageUtilities.ScaleByDpi(32f, this), ImageUtilities.ScaleByDpi(22f, this));
				toolStripSplitButton.DropDownButtonWidth = ImageUtilities.ScaleByDpi(11f, this);
			}
			else if (toolStripItem is ToolStripButton toolStripButton)
			{
				toolStripButton.Size = new Size(ImageUtilities.ScaleByDpi(23f, this), ImageUtilities.ScaleByDpi(22f, this));
			}
			else if (toolStripItem is ToolStripSeparator toolStripSeparator)
			{
				toolStripSeparator.Size = new Size(ImageUtilities.ScaleByDpi(6f, this), ImageUtilities.ScaleByDpi(25f, this));
			}
		}
	}

	private void InitializeTagEditorControls()
	{
		TagEncodingLayoutContext layoutContext = new TagEncodingLayoutContext
		{
			Owner = this
		};
		mainSplitContainer.Panel1MinSize = ImageUtilities.ScaleByDpi(320f);
		FontAwesome.SetFontFileDirectory(PathFileUtilities.GetApplicationDirectory() + "font");
		// 进程级默认保留(对话框构造期取启动刻度);主窗体自身的按钮图标改用局部 Properties
		// 按当前屏刻度生成,见 RefreshTagEditorButtonImages(PMv2 实验分支)。
		FontAwesome.DefaultProperties.Size = ImageUtilities.ScaleByDpi(18f);
		FontAwesome.DefaultProperties.ShowBorder = false;

		layoutContext.TagRows = new FlowLayoutPanel[11]
		{
				titleRowPanel, artistRowPanel, albumRowPanel, yearRowPanel, trackRowPanel, discRowPanel, genreRowPanel, albumArtistRowPanel, composerRowPanel, lyricistRowPanel,
			commentRowPanel
		};
		layoutContext.TagComboBoxes = new ComboBox[11]
		{
				titleComboBox, artistComboBox, albumComboBox, yearComboBox, trackComboBox, discComboBox, genreComboBox, albumArtistComboBox, composerComboBox, lyricistComboBox,
			commentComboBox
		};
		IEnumerable<ComboBox> editableTagComboBoxes = layoutContext.TagComboBoxes.Union(new ComboBox[1] { lyricsComboBox });
		tagEncodingButtons = new Button[12]
		{
			titleEncodingButton, artistEncodingButton, albumEncodingButton, yearEncodingButton, trackEncodingButton, discEncodingButton, genreEncodingButton, albumArtistEncodingButton, composerEncodingButton, lyricistEncodingButton,
			commentEncodingButton, lyricsEncodingButton
		};

		using (IEnumerator<ComboBox> comboBoxEnumerator = editableTagComboBoxes.GetEnumerator())
		{
			foreach (Button button in tagEncodingButtons)
			{
				comboBoxEnumerator.MoveNext();
				ComboBox comboBox = comboBoxEnumerator.Current;
				button.Text = "";
				button.Tag = tagComboBoxes.First(tagField => tagField.Value == comboBox).Key;
				button.Click += EditSingleFieldEncoding_Click;
			}
		}
		for (int rowIndex = 0; rowIndex < layoutContext.TagRows.Length; rowIndex++)
		{
			TagComboBoxWidthUpdater widthUpdater = new TagComboBoxWidthUpdater
			{
				LayoutContext = layoutContext,
				RowIndex = rowIndex
			};
			layoutContext.TagRows[rowIndex].SizeChanged += widthUpdater.UpdateComboBoxWidth;
		}
		lyricsRowPanel.SizeChanged += layoutContext.UpdateLyricsComboWidth;
		RefreshTagEditorButtonImages();
		previousCoverButton.Text = "";
		nextCoverButton.Text = "";
		editLyricsButton.Text = "";
		coverPictureBox.Image = ImageUtilities.LoadCachedResourceBitmap("no_cover", new Size(ImageUtilities.ScaleByDpi(96f, this), ImageUtilities.ScaleByDpi(96f, this)));
		coverPictureBox.SizeMode = PictureBoxSizeMode.CenterImage;
		overwriteCoverCheckBox.Checked = Settings.Default.OverwritePictureboxPicture;
	}

	// PMv2(实验分支):标签面板按钮的 FontAwesome 图标按窗体当前所在屏刻度生成;构造期与
	// WM_DPICHANGED 后各调一次(框架跨屏只缩按钮 bounds,不缩已生成的位图 → 图标显大/溢出)。
	// 用局部 Properties 而非改 FontAwesome.DefaultProperties —— 后者是进程级默认,别处构造期
	// 仍应取启动刻度(局部 Properties 的先例见 SourceOrderControl.ConfigureButtonImages)。
	private void RefreshTagEditorButtonImages()
	{
		FontAwesome.Properties iconProperties = new FontAwesome.Properties
		{
			Size = ImageUtilities.ScaleByDpi(18f, this),
			ShowBorder = false
		};
		Image oldEncodingImage = tagEncodingButtons[0].Image;
		Image editEncodingImage = FontAwesome.Type.Wrench.AsImage(iconProperties);
		foreach (Button button in tagEncodingButtons)
		{
			button.Image = editEncodingImage;
		}
		oldEncodingImage?.Dispose();
		ReplaceButtonImage(previousCoverButton, FontAwesome.Type.AngleLeft.AsImage(iconProperties));
		ReplaceButtonImage(nextCoverButton, FontAwesome.Type.AngleRight.AsImage(iconProperties));
		ReplaceButtonImage(editLyricsButton, FontAwesome.Type.Edit.AsImage(iconProperties));
	}

	private static void ReplaceButtonImage(Button button, Image newImage)
	{
		Image oldImage = button.Image;
		button.Image = newImage;
		oldImage?.Dispose();
	}

	private void InitializeFileListFilterMenu()
	{
		ToolStripItemCollection filterMenuItems = filterTypeDropDownButton.DropDownItems;
		IEnumerable<string> filterTypes = new string[1] { "any" }.Union(GetEditableTagFieldNames());
		foreach (string filterType in filterTypes)
		{
			ToolStripMenuItem menuItem = new ToolStripMenuItem
			{
				Tag = filterType,
				Text = Resources.ResourceManager.GetString(filterType)
			};
			menuItem.Click += FilterListViewTypeMenuItem_Click;
			filterMenuItems.Add(menuItem);
		}

		string selectedFilterType = Settings.Default.FilterListViewType;
		string filterListViewKeyword = Settings.Default.FilterListViewKeyword;
		if (string.IsNullOrWhiteSpace(selectedFilterType))
		{
			selectedFilterType = filterMenuItems[0].Tag as string;
		}
		filterTypeDropDownButton.Tag = selectedFilterType;
		filterTypeDropDownButton.Text = Resources.ResourceManager.GetString(selectedFilterType);
		filterMenuItems.Cast<ToolStripMenuItem>().First(menuItem => menuItem.Tag as string == selectedFilterType).Checked = true;
		filterTextBox.Text = filterListViewKeyword;
		filterTypeDropDownButton.Width = ImageUtilities.ScaleByDpi(100f);
		selectedFilesStatusLabel.Width = ImageUtilities.ScaleByDpi(190f);
	}

	private void FilterListViewTypeMenuItem_Click(object sender, EventArgs e)
	{
		if (!(sender is ToolStripMenuItem selectedMenuItem))
		{
			return;
		}
		foreach (ToolStripMenuItem menuItem in filterTypeDropDownButton.DropDownItems)
		{
			menuItem.Checked = false;
		}
		selectedMenuItem.Checked = true;
		filterTypeDropDownButton.Text = selectedMenuItem.Text;
		filterTypeDropDownButton.Tag = selectedMenuItem.Tag as string;
		BeginInvoke(new Action(RefreshFilteredFileList));
	}

	private void RefreshFilteredFileList()
	{
		ApplyFileListFilter(requireFilterText: true, suspendListSorting: true);
	}

	private void RegisterEditableTagFields()
	{
		Action<string, ComboBox, Label> action = RegisterEditableTagField;
		action("title", titleComboBox, titleLabel);
		action("artist", artistComboBox, artistLabel);
		action("album", albumComboBox, albumLabel);
		action("year", yearComboBox, yearLabel);
		action("trackstr", trackComboBox, trackLabel);
		action("discstr", discComboBox, discLabel);
		action("genre", genreComboBox, genreLabel);
		action("albumartist", albumArtistComboBox, albumArtistLabel);
		action("composer", composerComboBox, composerLabel);
		action("lyricist", lyricistComboBox, lyricistLabel);
		action("comment", commentComboBox, commentLabel);
		action("lyrics", lyricsComboBox, lyricsLabel);
	}

	private void RegisterEditableTagField(string fieldName, ComboBox comboBox, Label label)
	{
		tagComboBoxes.Add(fieldName, comboBox);
		selectedFilterValueStates.Add(fieldName, (new Dictionary<string, int>(), new List<(string, bool)>()));
		comboBox.Sorted = true;
		comboBox.Items.Add("<keep>");
		comboBox.Items.Add("<blank>");

		EventHandler textChangedHandler = (sender, e) =>
		{
			string displayName = Resources.ResourceManager.GetString(fieldName);
			if (selectedTagState != null && SelectedFileCount == 1 && selectedTagState[fieldName] is string currentValue && currentValue != comboBox.Text)
			{
				label.Text = displayName + "(" + Resources.Changed + ")";
			}
			else
			{
				label.Text = displayName;
			}
		};

		tagFieldTextHandlers.Add(fieldName, (label, textChangedHandler));
		comboBox.TextChanged += textChangedHandler;
	}

	private void ApplyLanguageResources(string languageCode = null)
	{
		if (languageCode == null)
		{
			languageCode = Settings.Default.Language;
		}
		else
		{
			Settings.Default.Language = languageCode;
			DialogService.TrySaveApplicationSettings();
		}
		if (string.IsNullOrEmpty(languageCode))
		{
			switch (CultureInfo.InstalledUICulture.Name)
			{
				case "zh-TW":
				case "zh-HK":
				case "zh-MO":
					languageCode = "zh-CHT";
					break;
				default:
					languageCode = CultureInfo.InstalledUICulture.Name.StartsWith("zh") ? "zh-CHS" : "en";
					break;
			}
		}

		currentLanguageCode = languageCode;
		englishLanguageMenuItem.Checked = false;
		simplifiedChineseLanguageMenuItem.Checked = false;
		traditionalChineseLanguageMenuItem.Checked = false;
		if (languageCode == "zh-CHS")
		{
			simplifiedChineseLanguageMenuItem.Checked = true;
		}
		else if (languageCode == "zh-CHT")
		{
			traditionalChineseLanguageMenuItem.Checked = true;
		}
		else
		{
			englishLanguageMenuItem.Checked = true;
		}

		Thread.CurrentThread.CurrentUICulture = CultureInfo.CreateSpecificCulture(languageCode);
		Thread.CurrentThread.CurrentCulture = CultureInfo.CreateSpecificCulture(languageCode);
		Text = Resources.AppName;
		notifyIcon.Text = Resources.AppName;
		fileMenuItem.Text = localizedResources.GetString("menuStrip1.File");
		changeDirectoryMenuItem.Text = localizedResources.GetString("menuStrip1.ChgDirs");
		changeDirectoryToolStripButton.Text = localizedResources.GetString("menuStrip1.ChgDirs");
		addDirectoryMenuItem.Text = localizedResources.GetString("menuStrip1.AddDirs");
		addDirectoriesToolStripButton.Text = localizedResources.GetString("menuStrip1.AddDirs");
		manageDirectoriesMenuItem.Text = localizedResources.GetString("menuStrip1.ManageDirs");
		manageDirectoriesToolStripButton.Text = localizedResources.GetString("menuStrip1.ManageDirs");
		removeTagsMenuItem.Text = localizedResources.GetString("menuStrip1.Removetags");
		removeTagsToolStripButton.Text = localizedResources.GetString("menuStrip1.Removetags");
		readTagsMenuItem.Text = localizedResources.GetString("menuStrip1.Readtags");
		renameFileMenuItem.Text = localizedResources.GetString("menuStrip1.Rename");
		openDirectoryMenuItem.Text = localizedResources.GetString("menuStrip1.OpenDirectory");
		saveTagsMenuItem.Text = localizedResources.GetString("menuStrip1.Save");
		exitMenuItem.Text = localizedResources.GetString("menuStrip1.Exit");
		notifyExitMenuItem.Text = localizedResources.GetString("menuStrip1.Exit");
		editMenuItem.Text = localizedResources.GetString("menuStrip1.Edit");
		selectAllFilesMenuItem.Text = localizedResources.GetString("menuStrip1.SelectAllFiles");
		selectAllFilesToolStripButton.Text = localizedResources.GetString("menuStrip1.SelectAllFiles");
		unselectAllFilesMenuItem.Text = localizedResources.GetString("menuStrip1.UnSelectAllFiles");
		unselectAllFilesToolStripButton.Text = localizedResources.GetString("menuStrip1.UnSelectAllFiles");
		invertFileSelectionMenuItem.Text = localizedResources.GetString("menuStrip1.InvertSelectFiles");
		undoMenuItem.Text = localizedResources.GetString("menuStrip1.Undo");
		undoToolStripButton.Text = localizedResources.GetString("menuStrip1.Undo");
		removeItemsMenuItem.Text = localizedResources.GetString("menuStrip1.RemoveItems");
		removeFilesMenuItem.Text = localizedResources.GetString("menuStrip1.RemoveFiles");
		characterSetMenuItem.Text = localizedResources.GetString("menuStrip1.Characterset");
		characterSetToolStripButton.Text = localizedResources.GetString("menuStrip1.Characterset");
		chineseConversionMenuItem.Text = localizedResources.GetString("menuStrip1.chschtToolStripMenuItem");
		chineseConversionToolStripDropDownButton.Text = localizedResources.GetString("menuStrip1.chschtToolStripMenuItem");
		convertTagsTraditionalToSimplifiedToolStripMenuItem.Text = localizedResources.GetString("menuStrip1.chtToChsToolStripMenuItem");
		convertTagsTraditionalToSimplifiedMenuItem.Text = localizedResources.GetString("menuStrip1.chtToChsToolStripMenuItem");
		convertTagsSimplifiedToTraditionalToolStripMenuItem.Text = localizedResources.GetString("menuStrip1.chsToChtToolStripMenuItem");
		convertTagsSimplifiedToTraditionalMenuItem.Text = localizedResources.GetString("menuStrip1.chsToChtToolStripMenuItem");
		tagHistoryMenuItem.Text = localizedResources.GetString("menuStrip1.TagsHistory");
		tagHistoryToolStripButton.Text = localizedResources.GetString("menuStrip1.TagsHistory");
		toolsMenuItem.Text = localizedResources.GetString("menuStrip1.Tools");
		optionsMenuItem.Text = localizedResources.GetString("menuStrip1.Options");
		optionsToolStripButton.Text = localizedResources.GetString("menuStrip1.Options");
		helpMenuItem.Text = localizedResources.GetString("menuStrip1.Help");
		musicTagWebsiteMenuItem.Text = localizedResources.GetString("menuStrip1.MusicTagWebsite");
		checkForUpdatesMenuItem.Text = localizedResources.GetString("menuStrip1.CheckForNewVersion");
		aboutMenuItem.Text = localizedResources.GetString("menuStrip1.About");
		saveTagsToolStripButton.Text = localizedResources.GetString("menuStrip1.Save");
		languageMenuItem.Text = localizedResources.GetString("menuStrip1.Language");
		englishLanguageMenuItem.Text = localizedResources.GetString("menuStrip1.English");
		simplifiedChineseLanguageMenuItem.Text = localizedResources.GetString("menuStrip1.CHS");
		traditionalChineseLanguageMenuItem.Text = localizedResources.GetString("menuStrip1.CHT");
		tagSourcesMenuItem.Text = localizedResources.GetString("menuStrip1.TagSources");
		coverSourceMenuItem.Text = localizedResources.GetString("menuStrip1.TagSources.Picture");
		coverSourceToolStripSplitButton.Text = localizedResources.GetString("menuStrip1.TagSources.PictureDefault");
		lyricSourceMenuItem.Text = localizedResources.GetString("menuStrip1.TagSources.Lyric");
		lyricSourceToolStripSplitButton.Text = localizedResources.GetString("menuStrip1.TagSources.LyricDefault");
		combinedTagSourceMenuItem.Text = localizedResources.GetString("menuStrip1.TagSources.Combination");
		combinedTagSourceToolStripSplitButton.Text = localizedResources.GetString("menuStrip1.TagSources.CombinationDefault");
		defaultCoverSourceMenuItem.Text = localizedResources.GetString("menuStrip1.TagSources.Default");
		defaultLyricSourceMenuItem.Text = localizedResources.GetString("menuStrip1.TagSources.Default");
		defaultCombinedTagSourceMenuItem.Text = localizedResources.GetString("menuStrip1.TagSources.Default");
		viewMenuItem.Text = localizedResources.GetString("menuStrip1.View");
		refreshMenuItem.Text = localizedResources.GetString("menuStrip1.Refresh");
		refreshToolStripButton.Text = localizedResources.GetString("menuStrip1.Refresh");
		customizeColumnsMenuItem.Text = localizedResources.GetString("menuStrip1.Customizecolumns");
		overwriteCoverCheckBox.Text = Resources.overwrite;
		controlToolTip.SetToolTip(overwriteCoverCheckBox, localizedResources.GetString("panel1.OverwritePictureAfterAdding"));
		controlToolTip.SetToolTip(editLyricsButton, localizedResources.GetString("panel1.EditLyric"));
		batchMenuItem.Text = localizedResources.GetString("menuStrip1.Batch");
		batchLyricMenuItem.Text = localizedResources.GetString("menuStrip1.Batch.Lyric");
		reformatLyricTimeTagsMenuItem.Text = localizedResources.GetString("menuStrip1.Batch.ReformatTimetag");
		removeLyricTimeTagsMenuItem.Text = localizedResources.GetString("menuStrip1.Batch.RemoveTimetag");
		deleteLyricHeaderTagsMenuItem.Text = localizedResources.GetString("menuStrip1.Batch.DeleteHeadTags");
		deleteBlankLyricLinesMenuItem.Text = localizedResources.GetString("menuStrip1.Batch.DeleteLinesOfBlankText");
		saveLyricsMenuItem.Text = localizedResources.GetString("menuStrip1.Batch.SaveAsLrcFile");
		importLrcFilesMenuItem.Text = localizedResources.GetString("menuStrip1.Batch.ImportLrcFile");
		batchExtractCoverMenuItem.Text = localizedResources.GetString("menuStrip1.Batch.ExtractCover");
		batchAutoMatchTagsMenuItem.Text = localizedResources.GetString("menuStrip1.Batch.AutoMatchTags");
		batchChineseConversionMenuItem.Text = localizedResources.GetString("menuStrip1.Batch.Chscht");
		batchTagsTraditionalToSimplifiedMenuItem.Text = localizedResources.GetString("menuStrip1.Batch.TagsChtToChs");
		batchTagsSimplifiedToTraditionalMenuItem.Text = localizedResources.GetString("menuStrip1.Batch.TagsChsToCht");
		batchFilenameTraditionalToSimplifiedMenuItem.Text = localizedResources.GetString("menuStrip1.Batch.FilenameChtToChs");
		batchFilenameSimplifiedToTraditionalMenuItem.Text = localizedResources.GetString("menuStrip1.Batch.FilenameChsToCht");
		batchFilenameRelatedMenuItem.Text = localizedResources.GetString("menuStrip1.Batch.FilenameRel");
		filterStatusLabel.Text = localizedResources.GetString("statusStrip2.FilterLabel");
		readTagsToolStripButton.Text = readTagsMenuItem.Text;
		reformatLyricTimeTagsToolStripMenuItem.Text = reformatLyricTimeTagsMenuItem.Text;
		removeLyricTimeTagsToolStripMenuItem.Text = removeLyricTimeTagsMenuItem.Text;
		deleteLyricHeaderTagsToolStripMenuItem.Text = deleteLyricHeaderTagsMenuItem.Text;
		deleteBlankLyricLinesToolStripMenuItem.Text = deleteBlankLyricLinesMenuItem.Text;
		importLrcFilesToolStripMenuItem.Text = importLrcFilesMenuItem.Text;
		batchSaveAsLrcToolStripSplitButton.Text = saveLyricsMenuItem.Text;
		batchExtractCoverToolStripButton.Text = batchExtractCoverMenuItem.Text;
		batchAutoMatchTagsToolStripButton.Text = batchAutoMatchTagsMenuItem.Text;
		batchChineseConversionToolStripDropDownButton.Text = batchChineseConversionMenuItem.Text;
		batchTagsTraditionalToSimplifiedToolStripMenuItem.Text = batchTagsTraditionalToSimplifiedMenuItem.Text;
		batchTagsSimplifiedToTraditionalToolStripMenuItem.Text = batchTagsSimplifiedToTraditionalMenuItem.Text;
		batchFilenameTraditionalToSimplifiedToolStripMenuItem.Text = batchFilenameTraditionalToSimplifiedMenuItem.Text;
		batchFilenameSimplifiedToTraditionalToolStripMenuItem.Text = batchFilenameSimplifiedToTraditionalMenuItem.Text;
		batchFilenameRelatedToolStripButton.Text = batchFilenameRelatedMenuItem.Text;
		saveTagsContextMenuItem.Text = saveTagsMenuItem.Text;
		saveTagsContextMenuItem.Image = saveTagsMenuItem.Image;
		removeTagsContextMenuItem.Text = removeTagsMenuItem.Text;
		removeTagsContextMenuItem.Image = removeTagsMenuItem.Image;
		readTagsContextMenuItem.Text = readTagsMenuItem.Text;
		readTagsContextMenuItem.Image = readTagsMenuItem.Image;
		characterSetContextMenuItem.Text = characterSetMenuItem.Text;
		characterSetContextMenuItem.Image = characterSetMenuItem.Image;
		tagHistoryContextMenuItem.Text = tagHistoryMenuItem.Text;
		tagHistoryContextMenuItem.Image = tagHistoryMenuItem.Image;
		renameFileContextMenuItem.Text = renameFileMenuItem.Text;
		renameFileContextMenuItem.Image = renameFileMenuItem.Image;
		removeItemsContextMenuItem.Text = removeItemsMenuItem.Text;
		removeItemsContextMenuItem.Image = removeItemsMenuItem.Image;
		removeFilesContextMenuItem.Text = removeFilesMenuItem.Text;
		removeFilesContextMenuItem.Image = removeFilesMenuItem.Image;
		openDirectoryContextMenuItem.Text = openDirectoryMenuItem.Text;
		openDirectoryContextMenuItem.Image = openDirectoryMenuItem.Image;

		foreach (KeyValuePair<string, (Label, EventHandler)> tagField in tagFieldTextHandlers)
		{
			tagField.Value.Item1.Text = Resources.ResourceManager.GetString(tagField.Key);
		}
		foreach (DataGridViewColumn columnHeader in fileListView.Columns)
		{
			columnHeader.HeaderText = Resources.ResourceManager.GetString(columnHeader.Name);
		}
		foreach (Button button in tagEncodingButtons)
		{
			controlToolTip.SetToolTip(button, Resources.characterset);
		}
		foreach (ToolStripItem menuItem in filterTypeDropDownButton.DropDownItems)
		{
			menuItem.Text = Resources.ResourceManager.GetString(menuItem.Tag as string);
		}
		filterTypeDropDownButton.Text = Resources.ResourceManager.GetString(filterTypeDropDownButton.Tag as string);
	}

	// 存储/默认列宽的刻度语义是"启动屏像素"(SaveCurrentFileListColumnWidths 归一化、
	// CustomColumnsDialog 默认表为静态 ScaleByDpi);窗口跨屏后上屏前换算为当前屏刻度。
	// 启动屏上恒等返回(customAssetsDpi == startupDpi),行为与历史版本一致。
	private int ScaleStoredColumnWidthToCurrentDpi(int storedWidth)
	{
		if (customAssetsDpi > 0 && startupDpi > 0 && customAssetsDpi != startupDpi)
		{
			return Math.Max(5, (int)Math.Round((float)storedWidth * customAssetsDpi / startupDpi));
		}
		return storedWidth;
	}

	private void InitializeFileListColumnsAndIcons()
	{
		fileListView.Columns.Clear();
		foreach (CustomColumnsDialog.ColumnHeaderInfo columnInfo in configuredColumnHeaders)
		{
			DataGridViewTextBoxColumn column = new DataGridViewTextBoxColumn
			{
				Name = columnInfo.Name,
				HeaderText = columnInfo.Name,
				Width = ScaleStoredColumnWidthToCurrentDpi(columnInfo.width),
				Visible = columnInfo.isShow,
				SortMode = DataGridViewColumnSortMode.Programmatic,
				ReadOnly = true,
				Resizable = DataGridViewTriState.True,
				Tag = columnInfo
			};
			column.DefaultCellStyle.Alignment = MapColumnAlignment(columnInfo.textAlign);
			fileListView.Columns.Add(column);
		}
		// DisplayIndex 必须是 0..N-1 的排列;按存储的 displayIndex 升序依次赋值,既忠实顺序又避免 DGV 重排冲突。
		int order = 0;
		foreach (CustomColumnsDialog.ColumnHeaderInfo columnInfo in configuredColumnHeaders.OrderBy(c => c.displayIndex))
		{
			fileListView.Columns[columnInfo.Name].DisplayIndex = order++;
		}
		fileTypeImageList.ColorDepth = ColorDepth.Depth32Bit;
		fileTypeImageList.ImageSize = new Size(ImageUtilities.ScaleByDpi(20f), ImageUtilities.ScaleByDpi(20f));
		fileListIconSize = fileTypeImageList.ImageSize.Width;
		fileListIconPadding = ImageUtilities.ScaleByDpi(2f);
		fileListView.RowTemplate.Height = Math.Max(ImageUtilities.ScaleByDpi(22f), fileTypeImageList.ImageSize.Height + fileListIconPadding);
	}

	internal static DataGridViewContentAlignment MapColumnAlignment(HorizontalAlignment textAlign)
	{
		switch (textAlign)
		{
			case HorizontalAlignment.Center:
				return DataGridViewContentAlignment.MiddleCenter;
			case HorizontalAlignment.Right:
				return DataGridViewContentAlignment.MiddleRight;
			default:
				return DataGridViewContentAlignment.MiddleLeft;
		}
	}

	private void ApplyFileListVisualStyle()
	{
		Font listFont = fileListFont;
		fileListView.Font = listFont;
		fileListView.BackgroundColor = Color.White;
		fileListView.GridColor = SystemColors.ControlLight;
		fileListView.CellBorderStyle = DataGridViewCellBorderStyle.None;
		fileListView.AdvancedCellBorderStyle.All = DataGridViewAdvancedCellBorderStyle.None;
		fileListView.AdvancedColumnHeadersBorderStyle.All = DataGridViewAdvancedCellBorderStyle.None;
		fileListView.AdvancedColumnHeadersBorderStyle.Right = DataGridViewAdvancedCellBorderStyle.Single;
		fileListView.EnableHeadersVisualStyles = false;
		fileListView.DefaultCellStyle.BackColor = Color.White;
		fileListView.DefaultCellStyle.ForeColor = SystemColors.WindowText;
		fileListView.DefaultCellStyle.Font = listFont;
		fileListView.DefaultCellStyle.SelectionBackColor = SystemColors.Highlight;
		fileListView.DefaultCellStyle.SelectionForeColor = SystemColors.HighlightText;
		fileListView.RowsDefaultCellStyle.BackColor = Color.White;
		fileListView.AlternatingRowsDefaultCellStyle.BackColor = Color.White;
		fileListView.ColumnHeadersDefaultCellStyle.BackColor = Color.White;
		fileListView.ColumnHeadersDefaultCellStyle.ForeColor = SystemColors.ControlText;
		fileListView.ColumnHeadersDefaultCellStyle.Font = listFont;
		fileListView.ColumnHeadersDefaultCellStyle.Padding = Padding.Empty;
		fileListView.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
		fileListView.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.White;
		fileListView.ColumnHeadersDefaultCellStyle.SelectionForeColor = SystemColors.ControlText;
		fileListView.ColumnHeadersHeight = Math.Max(fileListView.ColumnHeadersHeight, ImageUtilities.ScaleByDpi(24f));
	}

	// 依 cachedFileListItems(主表 = 显示顺序)的 IsHidden 重建可见行集合与下标反查,并把 RowCount 同步给 DGV。
	private void RebuildVisibleRows()
	{
		visibleRows = cachedFileListItems.Where((FileRow r) => !r.IsHidden).ToList();
		visibleRowIndex.Clear();
		selectedVisibleRows.Clear();
		selectedFileCount = 0;
		singleSelectedVisibleRow = null;
		for (int i = 0; i < visibleRows.Count; i++)
		{
			FileRow fileRow = visibleRows[i];
			visibleRowIndex[fileRow] = i;
			if (fileRow.Selected)
			{
				selectedVisibleRows.Add(fileRow);
				selectedFileCount++;
				singleSelectedVisibleRow = selectedFileCount == 1 ? fileRow : null;
			}
		}
		bool previous = suppressFileListSelectionEvents;
		suppressFileListSelectionEvents = true;
		try
		{
			fileListView.RowCount = visibleRows.Count;
		}
		finally
		{
			suppressFileListSelectionEvents = previous;
		}
		fileListView.Invalidate();
	}

	private void SetFileRowSelected(FileRow fileRow, bool selected)
	{
		if (fileRow.Selected == selected)
		{
			return;
		}

		fileRow.Selected = selected;
		bool isVisible = visibleRowIndex.ContainsKey(fileRow);
		if (selected && isVisible)
		{
			if (selectedVisibleRows.Add(fileRow))
			{
				selectedFileCount++;
			}
		}
		else if (isVisible)
		{
			if (selectedVisibleRows.Remove(fileRow) && selectedFileCount > 0)
			{
				selectedFileCount--;
			}
		}
		RefreshSingleSelectedVisibleRow();
	}

	private void RefreshSingleSelectedVisibleRow()
	{
		if (selectedFileCount == 1)
		{
			foreach (FileRow selectedRow in selectedVisibleRows)
			{
				singleSelectedVisibleRow = selectedRow;
				return;
			}
		}
		singleSelectedVisibleRow = null;
	}

	private FileRow GetSingleSelectedFileRow()
	{
		return selectedFileCount == 1 ? singleSelectedVisibleRow : null;
	}

	// 据 FileRow.Selected 把 DGV 行选区恢复成与模型一致(VirtualMode 行选区随 RowCount 重建而失效)。
	private void RestoreDgvSelectionFromModel()
	{
		bool previous = suppressFileListSelectionEvents;
		suppressFileListSelectionEvents = true;
		try
		{
			for (int i = 0; i < visibleRows.Count; i++)
			{
				DataGridViewRow row = fileListView.Rows[i];
				bool selected = visibleRows[i].Selected;
				if (row.Selected != selected)
				{
					row.Selected = selected;
				}
			}
		}
		finally
		{
			suppressFileListSelectionEvents = previous;
		}
	}

	// 该 FileRow 若在可见集合内则只重绘其行(不可见则跳过)。
	private void InvalidateFileRow(FileRow fileRow)
	{
		if (visibleRowIndex.TryGetValue(fileRow, out int visibleIndex) && visibleIndex < fileListView.RowCount)
		{
			fileListView.InvalidateRow(visibleIndex);
		}
	}

	private void FileList_CellValueNeeded(object sender, DataGridViewCellValueEventArgs e)
	{
		if (e.RowIndex < 0 || e.RowIndex >= visibleRows.Count)
		{
			return;
		}
		string[] cellTexts = visibleRows[e.RowIndex].CellTexts;
		e.Value = (e.ColumnIndex >= 0 && e.ColumnIndex < cellTexts.Length) ? cellTexts[e.ColumnIndex] : "";
	}

	private void FileList_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
	{
		if (e.RowIndex < 0 || e.RowIndex >= visibleRows.Count)
		{
			return;
		}
		e.CellStyle.ForeColor = visibleRows[e.RowIndex].LoadFailed ? Color.Red : fileListView.DefaultCellStyle.ForeColor;
	}

	// 首列自绘"图标 + 文字"(DGV 无内建图文同格);其余列/表头不接管,走默认绘制。
	private void FileList_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
	{
		if (e.ColumnIndex != 0 || e.RowIndex < 0 || e.RowIndex >= visibleRows.Count)
		{
			return;
		}
		FileRow fileRow = visibleRows[e.RowIndex];
		if (fileRow.IconImage == null)
		{
			return;
		}
		bool isSelected = (e.State & DataGridViewElementStates.Selected) != 0;
		e.PaintBackground(e.CellBounds, isSelected);
		int iconSize = fileListIconSize;
		int padding = fileListIconPadding;
		int iconX = e.CellBounds.Left + padding;
		int iconY = e.CellBounds.Top + (e.CellBounds.Height - iconSize) / 2;
		e.Graphics.DrawImage(fileRow.IconImage, iconX, iconY, iconSize, iconSize);
		int textLeft = iconX + iconSize + padding;
		Rectangle textBounds = new Rectangle(textLeft, e.CellBounds.Top, Math.Max(0, e.CellBounds.Right - textLeft), e.CellBounds.Height);
		Color foreColor = isSelected ? e.CellStyle.SelectionForeColor : e.CellStyle.ForeColor;
		TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
		string text = e.FormattedValue as string ?? fileRow.CellTexts[0];
		TextRenderer.DrawText(e.Graphics, text, e.CellStyle.Font, textBounds, foreColor, flags);
		e.Handled = true;
	}

	private SelectedListViewItemInfo[] CollectSelectedListViewItemInfos()
	{
		// 按主表(= 显示)顺序收集选中行,Index 统一为 cachedFileListItems 下标。
		List<SelectedListViewItemInfo> selected = new List<SelectedListViewItemInfo>();
		for (int index = 0; index < cachedFileListItems.Count; index++)
		{
			FileRow row = cachedFileListItems[index];
			if (row.Selected)
			{
				selected.Add(new SelectedListViewItemInfo
				{
					Index = index,
					FilePath = row.FilePath
				});
			}
		}
		return selected.ToArray();
	}

	private static string GetSelectedItemFilePath(SelectedListViewItemInfo itemInfo)
	{
		return itemInfo.FilePath;
	}

	private static (string Path, string NewPath, int Index) CreateRenameItemInfo(SelectedListViewItemInfo itemInfo)
	{
		return (Path: itemInfo.FilePath, NewPath: null, Index: itemInfo.Index);
	}

	private bool? ConfirmReadOnlyFileHandling(SelectedListViewItemInfo[] selectedItems)
	{
		string readOnlyFilePath = null;
		foreach (SelectedListViewItemInfo selectedItemInfo in selectedItems)
		{
			FileInfo fileInfo = new FileInfo(selectedItemInfo.FilePath);
			if (fileInfo.Exists && fileInfo.IsReadOnly)
			{
				readOnlyFilePath = selectedItemInfo.FilePath;
				break;
			}
		}
		if (readOnlyFilePath != null)
		{
			return DialogService.ConfirmYesNoCancel(string.Format(Resources.Msg_WantAllowRemoveReadonlyAttribute, readOnlyFilePath)) switch
			{
				DialogResult.No => false,
				DialogResult.Yes => true,
				_ => null,
			};
		}
		return false;
	}

	private void TagEditorPanel_SizeChanged(object sender, EventArgs e)
	{
		if (hasShownMainForm)
		{
			BeginInvoke(new Action(ApplyTagPanelLayoutAfterResize));
		}
	}

	private void StatusLabelsPanel_SizeChanged(object sender, EventArgs e)
	{
		if (hasShownMainForm)
		{
			BeginInvoke(new Action(ResizeStatusLabels));
		}
	}

	private void ApplyTagPanelLayout()
	{
		Size clientSize = tagEditorPanel.ClientSize;
		int tagPanelWidth = clientSize.Width - tagEditorPanel.Margin.Left - tagEditorPanel.Margin.Right - tagEditorPanel.Padding.Left - tagEditorPanel.Padding.Right;
		coverPanel.Width = tagPanelWidth;
		lyricsRowPanel.Width = tagPanelWidth;
		commentRowPanel.Width = tagPanelWidth;
		lyricistRowPanel.Width = tagPanelWidth;
		composerRowPanel.Width = tagPanelWidth;
		albumArtistRowPanel.Width = tagPanelWidth;
		genreRowPanel.Width = tagPanelWidth;
		yearRowPanel.Width = tagPanelWidth;
		albumRowPanel.Width = tagPanelWidth;
		artistRowPanel.Width = tagPanelWidth;
		titleRowPanel.Width = tagPanelWidth;

		trackDiscGroupPanel.Width = titleRowPanel.Width - titleEncodingButton.Width - ImageUtilities.ScaleByDpi(5f, this);
		int tagPairHeight = trackLabel.Height + trackRowPanel.Height + ImageUtilities.ScaleByDpi(6f, this, roundUp: true);
		discColumnPanel.Height = tagPairHeight;
		trackColumnPanel.Height = tagPairHeight;
		trackDiscGroupPanel.Height = tagPairHeight;
		trackRowPanel.Width = trackDiscGroupPanel.Width / 2;
		trackColumnPanel.Width = trackRowPanel.Width;
		discRowPanel.Width = trackDiscGroupPanel.Width / 2 - ImageUtilities.ScaleByDpi(5f, this);
		discColumnPanel.Width = discRowPanel.Width;
		discColumnPanel.Margin = new Padding(ImageUtilities.ScaleByDpi(5f, this), 0, 0, 0);
		overwriteCoverCheckBox.Margin = new Padding((statusLabelsPanel.Width - overwriteCoverCheckBox.Width) / 2, ImageUtilities.ScaleByDpi(50f, this), 0, 0);

		int coverPanelSize = coverPanel.Width - statusLabelsPanel.Width;
		int availableHeight = tagEditorPanel.Height;
		int controlsBottom = tagEditorBottomSpacerPanel.Location.Y + tagEditorBottomSpacerPanel.Height;
		bool controlsOverflow = controlsBottom > availableHeight;
		bool resizedControlsWouldOverflow = controlsBottom + (coverPanelSize - lastCoverPreviewSize) > availableHeight;

		if (lastTagPanelSplitterDistance == mainSplitContainer.SplitterDistance && coverPanelSize >= lastCoverPreviewSize)
		{
			bool widthGrowthIsUnsafe = coverPanelSize <= lastCoverPreviewSize || controlsOverflow || resizedControlsWouldOverflow;
			bool heightGrowthIsUnsafe = availableHeight <= lastTagEditorAvailableHeight || !lastTagEditorControlsOverflow || controlsOverflow;
			if (widthGrowthIsUnsafe && heightGrowthIsUnsafe)
			{
				return;
			}
		}

		if (coverPanelSize > ImageUtilities.ScaleByDpi(500f, this))
		{
			coverPanelSize = ImageUtilities.ScaleByDpi(500f, this);
		}
		coverPictureBox.Height = coverPanelSize;
		coverPictureBox.Width = coverPanelSize;
		coverPanel.Height = coverPictureBox.Height;
		lastTagPanelSplitterDistance = mainSplitContainer.SplitterDistance;
		lastCoverPreviewSize = coverPanelSize;
		lastTagEditorAvailableHeight = availableHeight;
		lastTagEditorControlsOverflow = controlsOverflow;
	}

	private HashSet<string> GetLoadedFilePaths()
	{
		return new HashSet<string>(cachedFileListItems.Select(cachedItem => cachedItem.FilePath));
	}

	private void ClearLoadedFileList()
	{
		cachedFileListItems.Clear();
		RebuildVisibleRows();
		OnFileSelectionSettled();
		LoadTagEditorState(null);
	}

	private void ChangeDirectoryButton_Click(object sender, EventArgs e)
	{
		FolderSelectionDialog folderDialog = new FolderSelectionDialog();
		if (folderDialog.ShowDialog(allowMultiSelect: true) != DialogResult.OK)
		{
			return;
		}

		FileSettings.Clear();
		ClearLoadedFileList();
		List<ListViewFileSettingFileInfo> selectedDirectories = AddSelectedDirectoriesToSettings(folderDialog);
		UpdateWindowTitle();
		ProgressDialog progressDialog = new ProgressDialog(taskbarProgress);
		StartAddAnyFiles(selectedDirectories, progressDialog);
		progressDialog.ShowDialogIfNotDisposed();
	}

	private void AddFolderButton_Click(object sender, EventArgs e)
	{
		FolderSelectionDialog folderDialog = new FolderSelectionDialog();
		if (folderDialog.ShowDialog(allowMultiSelect: true) != DialogResult.OK)
		{
			return;
		}

		if (FileSettings.IsAnyFileMode())
		{
			ClearLoadedFileList();
			UpdateFileListStatusSummary(selectedItemsOnly: false);
		}

		List<ListViewFileSettingFileInfo> selectedDirectories = AddSelectedDirectoriesToSettings(folderDialog);
		UpdateWindowTitle();
		ProgressDialog progressDialog = new ProgressDialog(taskbarProgress);
		StartAddAnyFiles(selectedDirectories, progressDialog);
		progressDialog.ShowDialogIfNotDisposed();
	}

	private List<ListViewFileSettingFileInfo> AddSelectedDirectoriesToSettings(FolderSelectionDialog folderDialog)
	{
		var selectedDirectories = new List<ListViewFileSettingFileInfo>();
		foreach (string directoryPath in folderDialog.SelectedPaths)
		{
			var directoryInfo = new ListViewFileSettingFileInfo
			{
				DirPath = directoryPath,
				IncludeSubDir = folderDialog.IncludeSubdirectories
			};
			FileSettings.AddForDir(directoryInfo);
			selectedDirectories.Add(directoryInfo);
		}

		return selectedDirectories;
	}

	private void ManageDirectoriesButton_Click(object sender, EventArgs e)
	{
		DirectoryManagerDialog directoryManagerDialog = new DirectoryManagerDialog();
		directoryManagerDialog.SetFileSetting(FileSettings);
		if (directoryManagerDialog.ShowDialog() != DialogResult.OK)
		{
			return;
		}

		if (directoryManagerDialog.HasChanges && !FileSettings.IsAnyFileMode())
		{
			refreshMenuItem.PerformClick();
		}
	}

	private async void StartAddAnyFiles(IEnumerable<object> fileInfos, ProgressDialog progressDialog)
	{
		var fileCollector = new AddAnyFileCollector
		{
			ProgressDialog = progressDialog,
			InputFileInfos = fileInfos,
			CancellationTokenSource = new CancellationTokenSource(),
			FilePaths = new List<string>(),
			ErrorMessage = null
		};
		ProgressDialog.ProgressDialogCallback cancelRequestedHandler = fileCollector.Cancel;
		ProgressDialog.ProgressDialogCallback progressUpdateHandler = fileCollector.ShowScanningProgress;
		progressDialog.CancelRequested += cancelRequestedHandler;
		progressDialog.ProgressUpdate += progressUpdateHandler;
		bool continueAddingFiles = false;
		try
		{
			await Task.Run((Action)fileCollector.CollectFilePaths);
			continueAddingFiles = !fileCollector.CancellationTokenSource.IsCancellationRequested;
			if (fileCollector.ErrorMessage != null)
			{
				BeginInvoke(new Action(fileCollector.ShowErrorMessage));
			}
		}
		catch (System.Exception ex)
		{
			ReportAsyncOperationErrorIfNotCancellation(ex, fileCollector.CancellationTokenSource, nameof(StartAddAnyFiles));
		}
		finally
		{
			progressDialog.CancelRequested -= cancelRequestedHandler;
			progressDialog.ProgressUpdate -= progressUpdateHandler;
			if (!continueAddingFiles)
			{
				progressDialog.CloseAfterCompletion();
			}
		}
		if (continueAddingFiles)
		{
			StartAddFiles(fileCollector.FilePaths, progressDialog);
		}
	}

	private void StartAddConfiguredFileList(ProgressDialog progressDialog)
	{
		if (FileSettings.IsAnyFileMode())
		{
			StartAddAnyFiles(FileSettings.ToAnyFilePathList(), progressDialog);
		}
		else
		{
			StartAddAnyFiles(FileSettings.ToDirPathList(), progressDialog);
		}
	}

	private void AddFilesFromPaths(IEnumerable<string> filePaths = null)
	{
		try
		{
			ClearLoadedFileList();
			ProgressDialog progressDialog = new ProgressDialog(taskbarProgress);
			FileSettings.DisableForDir();
			FileSettings.EnableForAnyFile();
			UpdateWindowTitle();
			StartAddAnyFiles(filePaths ?? startupFileArgs, progressDialog);
			progressDialog.ShowDialogIfNotDisposed();
		}
		catch (System.Exception ex)
		{
			Console.WriteLine("AddFilesFromParams error:" + ex.Message);
		}
	}

	// 选中态以 FileRow.Selected 为模型真源(隐藏行恒为 false),按 visibleRows 顺序读取与显示顺序一致。
	private int SelectedFileCount => selectedFileCount;

	private IEnumerable<FileRow> SelectedFileRows => visibleRows.Where(r => selectedVisibleRows.Contains(r));

	internal static string FormatCountDurationSize(int count, long durationMs, long fileSizeBytes)
	{
		return $"{count} ({ConfigDescriptorState.FormatDurationHms(durationMs)} | {TextUtilities.FormatFileSize(fileSizeBytes)})";
	}

	private void RefreshStatusLabelsFromCachedTotals()
	{
		(long selectedDurationMs, long selectedFileSizeBytes) = GetCachedDurationAndFileSize(selectedFilesStatusLabel.Tag);
		selectedFilesStatusLabel.Text = FormatCountDurationSize(SelectedFileCount, selectedDurationMs, selectedFileSizeBytes);
		(long allDurationMs, long allFileSizeBytes) = GetCachedDurationAndFileSize(totalFilesStatusLabel.Tag);
		totalFilesStatusLabel.Text = FormatCountDurationSize(visibleRows.Count, allDurationMs, allFileSizeBytes);
	}

	internal static (long DurationMs, long FileSizeBytes) GetCachedDurationAndFileSize(object cachedValue)
	{
		return cachedValue is ValueTuple<long, long> totals ? totals : (0L, 0L);
	}

	private void GetListViewItemDurationAndFileSize(FileRow fileRow, out long durationMs, out long fileSizeBytes)
	{
		FileInfo fileInfo = new FileInfo(fileRow.FilePath);
		fileSizeBytes = fileInfo.Exists ? fileInfo.Length : 0L;
		durationMs = fileRow.DurationMs ?? 0L;
	}

	private void UpdateFileListStatusSummary(bool selectedItemsOnly)
	{
		long allDurationMs = 0L;
		long allFileSizeBytes = 0L;
		long selectedDurationMs = 0L;
		long selectedFileSizeBytes = 0L;
		if (selectedItemsOnly)
		{
			foreach (FileRow selectedItem in SelectedFileRows)
			{
				GetListViewItemDurationAndFileSize(selectedItem, out long itemDurationMs, out long itemFileSizeBytes);
				selectedDurationMs += itemDurationMs;
				selectedFileSizeBytes += itemFileSizeBytes;
			}
		}
		else
		{
			foreach (FileRow fileRow in visibleRows)
			{
				GetListViewItemDurationAndFileSize(fileRow, out long itemDurationMs, out long itemFileSizeBytes);
				allDurationMs += itemDurationMs;
				allFileSizeBytes += itemFileSizeBytes;
				if (fileRow.Selected)
				{
					selectedDurationMs += itemDurationMs;
					selectedFileSizeBytes += itemFileSizeBytes;
				}
			}
			totalFilesStatusLabel.Text = FormatCountDurationSize(visibleRows.Count, allDurationMs, allFileSizeBytes);
			totalFilesStatusLabel.Tag = (allDurationMs, allFileSizeBytes);
		}
		selectedFilesStatusLabel.Text = FormatCountDurationSize(SelectedFileCount, selectedDurationMs, selectedFileSizeBytes);
		selectedFilesStatusLabel.Tag = (selectedDurationMs, selectedFileSizeBytes);
	}

	private async void StartAddFiles(List<string> fileNames, ProgressDialog progressDialog)
	{
		var addFilesWorker = new AddFilesWorker
		{
			ProgressDialog = progressDialog,
			FileNames = fileNames,
			Owner = this,
			ExistingFilePaths = GetLoadedFilePaths(),
			CancellationTokenSource = new CancellationTokenSource(),
			CurrentIndex = 0,
			CurrentFileInfo = null,
			FileIconSize = new Size(fileTypeImageList.ImageSize.Width, fileTypeImageList.ImageSize.Height),
			LoadErrors = new Page()
		};
		progressDialog.CancelRequested += addFilesWorker.Cancel;
		progressDialog.ProgressUpdate += addFilesWorker.UpdateProgress;
		addFilesWorker.LoadedFilesProgress = new Progress<List<(ConfigDescriptorState TagFile, Dictionary<string, string> DisplayValues, string FilePath)>>(addFilesWorker.AddLoadedFilesToListView);
		try
		{
			await Task.Run((Action)addFilesWorker.LoadFiles, addFilesWorker.CancellationTokenSource.Token);
			ApplyFileListSort();
			ApplyFileListFilter(requireFilterText: true, suspendListSorting: false);
			UpdateFileListStatusSummary(selectedItemsOnly: false);
			if (addFilesWorker.LoadErrors.LineCount > 0)
			{
				BeginInvoke(new Action(addFilesWorker.ShowLoadErrors));
			}
		}
		catch (System.Exception ex)
		{
			ReportAsyncOperationErrorIfNotCancellation(ex, addFilesWorker.CancellationTokenSource, nameof(StartAddFiles));
		}
		finally
		{
			progressDialog.CloseAfterCompletion();
		}
	}

	private void UpdateListViewItemValues(FileRow fileRow, ConfigDescriptorState tagFile, Dictionary<string, string> displayValues)
	{
		int columnIndex = 0;
		foreach (CustomColumnsDialog.ColumnHeaderInfo column in configuredColumnHeaders)
		{
			if (!displayValues.TryGetValue(column.Name, out var value) && tagFile != null && tagFile.IsLoadedSuccessfully())
			{
				value = tagFile.GetDisplayValue(column.Name);
			}
			if (value == null)
			{
				value = "";
			}
			int length = value.Length;
			value = TruncateLyricsOrCommentDisplayValue(column.Name, value);
			if (fileRow.CellTexts[columnIndex] != value)
			{
				fileRow.CellTexts[columnIndex] = value;
				if (column.Name == "durationinms" && value != "")
				{
					fileRow.DurationMs = tagFile[column.Name] is int duration ? duration : (int?)null;
				}
				if (column.Name == "comment")
				{
					fileRow.CommentFullLength = length;
				}
			}
			columnIndex++;
		}
		fileRow.LoadFailed = tagFile == null || !tagFile.IsLoadedSuccessfully();
		InvalidateFileRow(fileRow);
	}

	private void RefreshListViewItemFromFile(FileRow fileRow, ConfigDescriptorState tagFile)
	{
		UpdateListViewItemValues(fileRow, tagFile, BuildBasicFileDisplayValues(new FileInfo(tagFile.GetFilePath())));
	}

	private Dictionary<string, string> BuildBasicFileDisplayValues(FileInfo fileInfo)
	{
		return new Dictionary<string, string>
		{
			{ "filename", fileInfo.Name },
			{ "filedir", fileInfo.DirectoryName },
			{ "updatetime", fileInfo.Exists ? fileInfo.LastWriteTime.ToString("yyyy/MM/dd HH:mm:ss") : "" }
		};
	}

	private async void StartRefreshItems(SelectedListViewItemInfo[] itemInfos, ProgressDialog progressDialog, bool showErrorMessageBox, bool refreshStatusAllInfo, (string msg, bool isErr)? previousMessage = null, bool listForMirror = false)
	{
		RefreshItemsTaskContext refreshContext = new RefreshItemsTaskContext();
		refreshContext.progressDialog = progressDialog;
		refreshContext.itemInfos = itemInfos;
		refreshContext.owner = this;
		refreshContext.previousMessage = previousMessage;
		refreshContext.showLoadErrors = showErrorMessageBox;
		refreshContext.cancellationSource = new CancellationTokenSource();
		refreshContext.currentFile = null;
		refreshContext.processedCount = 0;
		if (refreshContext.progressDialog != null)
		{
			refreshContext.progressDialog.CancelRequested += refreshContext.Cancel;
			refreshContext.progressDialog.ProgressUpdate += refreshContext.UpdateProgress;
		}
		refreshContext.progressReporter = new Progress<List<(SelectedListViewItemInfo, ConfigDescriptorState, Dictionary<string, string>)>>(refreshContext.ApplyRefreshedItems);
		refreshContext.loadErrors = new Page();
		Task task = Task.Run(new Action(refreshContext.RefreshItems), refreshContext.cancellationSource.Token);
		try
		{
			await task;
			ApplyFileSelectionMode(FileSelectionMode.RefreshOnly, refreshStatusAllInfo);
			BeginInvoke(new Action(refreshContext.ShowCompletionMessages));
		}
		catch (System.Exception ex)
		{
			ReportAsyncOperationErrorIfNotCancellation(ex, refreshContext.cancellationSource, nameof(StartRefreshItems));
		}
		finally
		{
			refreshContext.progressDialog?.CloseAfterCompletion();
		}
	}

	// DGV 选区变化(用户驱动):与模型(FileRow.Selected)做差分,复刻原 ListView 的 per-item 增量语义,
	// 再按最终选中数走 1/0/多 三分支(多次中间态被 DGV 合并为一次事件;最终 count==1/0 时按原逻辑重算
	// 而非增量,故合并无损)。
	private void FileList_SelectionChanged(object sender, EventArgs e)
	{
		if (suppressFileListSelectionEvents)
		{
			return;
		}

		HashSet<FileRow> currentSelectedRows = new HashSet<FileRow>();
		foreach (DataGridViewRow selectedRow in fileListView.SelectedRows)
		{
			int rowIndex = selectedRow.Index;
			if (rowIndex >= 0 && rowIndex < visibleRows.Count)
			{
				currentSelectedRows.Add(visibleRows[rowIndex]);
			}
		}

		List<(FileRow row, bool nowSelected)> toggles = new List<(FileRow, bool)>();
		foreach (FileRow row in currentSelectedRows)
		{
			if (!selectedVisibleRows.Contains(row))
			{
				toggles.Add((row, true));
			}
		}
		foreach (FileRow row in selectedVisibleRows.ToArray())
		{
			if (!currentSelectedRows.Contains(row))
			{
				toggles.Add((row, false));
			}
		}
		if (toggles.Count == 0)
		{
			return;
		}
		foreach (var toggle in toggles)
		{
			SetFileRowSelected(toggle.row, toggle.nowSelected);
		}
		if (SelectedFileCount == 1)
		{
			FileRow selectedRow = GetSingleSelectedFileRow();
			if (selectedRow != null)
			{
				LoadSingleSelectedFile(selectedRow);
			}
		}
		else if (SelectedFileCount <= 0)
		{
			ClearSelectedFilterSummaries();
			selectedFilesStatusLabel.Tag = (0L, 0L);
			StartSelectionStatusUpdateTimer();
		}
		else
		{
			foreach (var toggle in toggles)
			{
				UpdateSelectedFilterValues(toggle.row, toggle.nowSelected);
				UpdateSelectedDurationAndSize(toggle.row, toggle.nowSelected);
			}
			StartSelectionStatusUpdateTimer();
		}
		UpdateSelectionCommandState();
	}

	// 选区"settled"(过滤/全选反选/清空后程序化定型)——对应原 FileList_ItemSelectionChanged(null, null):
	// 只走 count==1/0 分支(原 else 分支需 e!=null,这里不触发)。
	private void OnFileSelectionSettled()
	{
		int selectedCount = SelectedFileCount;
		if (selectedCount == 1)
		{
			FileRow selectedRow = GetSingleSelectedFileRow();
			if (selectedRow != null)
			{
				LoadSingleSelectedFile(selectedRow);
			}
		}
		else if (selectedCount <= 0)
		{
			ClearSelectedFilterSummaries();
			selectedFilesStatusLabel.Tag = (0L, 0L);
			StartSelectionStatusUpdateTimer();
		}
		UpdateSelectionCommandState();
	}

	private void LoadSingleSelectedFile(FileRow row)
	{
		selectionStatusUpdateTimer.Stop();
		ClearSelectedFilterSummaries();
		using (ConfigDescriptorState tagFile = new ConfigDescriptorState(row.FilePath))
		{
			if (tagFile.IsLoadedSuccessfully())
			{
				tagFile.LoadBasicTagFields();
				tagFile.LoadAudioProperties();
				tagFile.LoadRawTextFieldData();
				tagFile.LoadAllPictures();
				tagFile.LoadLyrics();
			}
			RefreshListViewItemFromFile(row, tagFile);
			AddSelectedFilterValues(row);
			LoadTagEditorState(tagFile);
			UpdateFileListStatusSummary(selectedItemsOnly: true);
		}
	}

	private void ClearSelectedFilterSummaries()
	{
		foreach (var selectedFilter in selectedFilterValueStates.Values)
		{
			selectedFilter.Item1.Clear();
			selectedFilter.Item2.Clear();
		}
	}

	private void SubscribeTagFieldTextHandlers()
	{
		foreach (KeyValuePair<string, (Label, EventHandler)> handlerEntry in tagFieldTextHandlers)
		{
			tagComboBoxes[handlerEntry.Key].TextChanged += handlerEntry.Value.Item2;
		}
	}

	private void UnsubscribeTagFieldTextHandlers()
	{
		foreach (KeyValuePair<string, (Label, EventHandler)> handlerEntry in tagFieldTextHandlers)
		{
			tagComboBoxes[handlerEntry.Key].TextChanged -= handlerEntry.Value.Item2;
		}
	}

	private void AddSelectedFilterValues(FileRow fileRow)
	{
		foreach (var selectedFilter in selectedFilterValueStates)
		{
			AddSelectedFilterValue(selectedFilter.Value.Item1, selectedFilter.Value.Item2, GetSelectedFilterValue(fileRow, selectedFilter.Key));
		}
	}

	private void UpdateSelectedFilterValues(FileRow fileRow, bool isSelected)
	{
		foreach (var selectedFilter in selectedFilterValueStates)
		{
			UpdateSelectedFilterValue(selectedFilter.Value.Item1, selectedFilter.Value.Item2, GetSelectedFilterValue(fileRow, selectedFilter.Key), isSelected);
		}
	}

	// 纯核(从 UpdateSelectedFilterValues 循环体逐字节分离,behavior-preserving):选中/取消选中时维护筛选值计数。
	// AddSelectedFilterValue 的 decrement 对称体——isSelected=false 计数--、归零则移除并记 (value,true);
	// isSelected=true 同 AddSelectedFilterValue(计数++、首现 add 并记 (value,false));absent+deselect 为 no-op。
	// 空白 value 统一归一为 ""。
	internal static void UpdateSelectedFilterValue(Dictionary<string, int> valueCounts, List<(string, bool)> changedValues, string value, bool isSelected)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			value = "";
		}
		if (valueCounts.TryGetValue(value, out int count))
		{
			count = isSelected ? count + 1 : count - 1;
			if (count > 0)
			{
				valueCounts[value] = count;
			}
			else
			{
				valueCounts.Remove(value);
				changedValues.Add((value, true));
			}
		}
		else if (isSelected)
		{
			valueCounts.Add(value, 1);
			changedValues.Add((value, false));
		}
	}

	internal static void AddSelectedFilterValue(Dictionary<string, int> valueCounts, List<(string, bool)> changedValues, string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			value = "";
		}
		if (valueCounts.TryGetValue(value, out int count))
		{
			valueCounts[value] = count + 1;
		}
		else
		{
			valueCounts.Add(value, 1);
			changedValues.Add((value, false));
		}
	}

	private void UpdateSelectedDurationAndSize(FileRow fileRow, bool isSelected)
	{
		(long selectedDurationMs, long selectedFileSizeBytes) = selectedFilesStatusLabel.Tag is ValueTuple<long, long> cachedTotals ? cachedTotals : (0L, 0L);
		GetListViewItemDurationAndFileSize(fileRow, out var itemDurationMs, out var itemFileSizeBytes);
		selectedFilesStatusLabel.Tag = AccumulateClampedTotals(selectedDurationMs, selectedFileSizeBytes, itemDurationMs, itemFileSizeBytes, isSelected);
	}

	// 纯核(从 UpdateSelectedDurationAndSize 分离控件 Tag 读写 + FileInfo IO,behavior-preserving):选中累加 /
	// 取消选中扣减时长与字节数,两者各 clamp 到 >=0(防取消选中造成负漂移)。
	internal static (long DurationMs, long FileSizeBytes) AccumulateClampedTotals(long currentDurationMs, long currentFileSizeBytes, long itemDurationMs, long itemFileSizeBytes, bool isSelected)
	{
		long durationMs = isSelected ? currentDurationMs + itemDurationMs : currentDurationMs - itemDurationMs;
		long fileSizeBytes = isSelected ? currentFileSizeBytes + itemFileSizeBytes : currentFileSizeBytes - itemFileSizeBytes;
		if (durationMs < 0L)
		{
			durationMs = 0L;
		}
		if (fileSizeBytes < 0L)
		{
			fileSizeBytes = 0L;
		}
		return (durationMs, fileSizeBytes);
	}

	private void UpdateSelectionCommandState()
	{
		int selectedItemCount = SelectedFileCount;
		SetCommandsRequiringAnySelection(selectedItemCount > 0);
		SetCommandsRequiringSingleSelection(selectedItemCount == 1);
		undoMenuItem.Enabled = TagHistoryRepository.HasPendingUndoActions();
		undoToolStripButton.Enabled = undoMenuItem.Enabled;
	}

	private void SetCommandsRequiringAnySelection(bool enabled)
	{
		saveTagsMenuItem.Enabled = enabled;
		saveTagsContextMenuItem.Enabled = enabled;
		removeTagsMenuItem.Enabled = enabled;
		removeTagsContextMenuItem.Enabled = enabled;
		readTagsMenuItem.Enabled = enabled;
		readTagsContextMenuItem.Enabled = enabled;
		readTagsToolStripButton.Enabled = enabled;
		removeItemsMenuItem.Enabled = enabled;
		removeItemsContextMenuItem.Enabled = enabled;
		removeFilesMenuItem.Enabled = enabled;
		removeFilesContextMenuItem.Enabled = enabled;
		saveTagsToolStripButton.Enabled = enabled;
		removeTagsToolStripButton.Enabled = enabled;
		batchMenuItem.Enabled = enabled;
		batchSaveAsLrcToolStripSplitButton.Enabled = enabled;
		batchExtractCoverToolStripButton.Enabled = enabled;
		batchAutoMatchTagsToolStripButton.Enabled = enabled;
		batchChineseConversionToolStripDropDownButton.Enabled = enabled;
		batchFilenameRelatedToolStripButton.Enabled = enabled;
	}

	private void SetCommandsRequiringSingleSelection(bool enabled)
	{
		renameFileMenuItem.Enabled = enabled;
		renameFileContextMenuItem.Enabled = enabled;
		openDirectoryMenuItem.Enabled = enabled;
		openDirectoryContextMenuItem.Enabled = enabled;
		characterSetMenuItem.Enabled = enabled;
		characterSetContextMenuItem.Enabled = enabled;
		tagHistoryMenuItem.Enabled = enabled;
		tagHistoryContextMenuItem.Enabled = enabled;
		tagSourcesMenuItem.Enabled = enabled;
		characterSetToolStripButton.Enabled = enabled;
		chineseConversionMenuItem.Enabled = enabled;
		chineseConversionToolStripDropDownButton.Enabled = enabled;
		tagHistoryToolStripButton.Enabled = enabled;
		coverSourceToolStripSplitButton.Enabled = enabled;
		lyricSourceToolStripSplitButton.Enabled = enabled;
		combinedTagSourceToolStripSplitButton.Enabled = enabled;
	}

	private void StartSelectionStatusUpdateTimer()
	{
		if (!selectionStatusUpdateTimer.Enabled)
		{
			selectionStatusUpdateTimer.Start();
		}
	}

	private void SelectionStatusUpdateTimer_Tick(object sender, EventArgs e)
	{
		LoadTagEditorState(null);
		RefreshStatusLabelsFromCachedTotals();
		selectionStatusUpdateTimer.Stop();
	}

	private void RestartFilterInputTimer()
	{
		if (filterInputTimer.Enabled)
		{
			filterInputTimer.Stop();
		}
		filterInputTimer.Start();
	}

	private void ApplyFileListFilter(bool requireFilterText, bool suspendListSorting)
	{
		FileListFilterContext activeFilterContext = new FileListFilterContext
		{
			owner = this
		};
		filterInputTimer.Stop();
		activeFilterContext.filterText = filterTextBox.Text.Trim();
		if (requireFilterText)
		{
			if (!activeFilterContext.filterText.Any())
			{
				return;
			}
		}
		else if (activeFilterContext.filterText == lastFileListFilterText)
		{
			return;
		}

		tagComboBoxes.Values.ForEachItem(BeginComboBoxUpdate);
		bool textHandlersResubscribed = false;
		try
		{
			UnsubscribeTagFieldTextHandlers();
			tagComboBoxes.ForEachItem(activeFilterContext.ResetFilterComboState);
			if (activeFilterContext.filterText.Any())
			{
				string[] filterColumnNames = filterTypeDropDownButton.Tag as string == "any" ? GetEditableTagFieldNames() : new string[1] { filterTypeDropDownButton.Tag as string };
				IEnumerable<int> filterColumnIndexes = filterColumnNames.Select(FindColumnHeaderIndexByName);
				for (int index = 0; index < cachedFileListItems.Count; index++)
				{
					FileRow fileRow = cachedFileListItems[index];
					FileListFilterItemContext filterMatcher = new FileListFilterItemContext
					{
						filterContext = activeFilterContext,
						fileRow = fileRow
					};
					if (filterColumnIndexes.Any(filterMatcher.MatchesFilterText))
					{
						fileRow.IsHidden = false;
						if (fileRow.Selected)
						{
							selectedFilterValueStates.ForEachItem(filterMatcher.CountSelectedFilterValue);
						}
					}
					else
					{
						fileRow.IsHidden = true;
						if (fileRow.Selected)
						{
							SetFileRowSelected(fileRow, selected: false);
						}
					}
				}
			}
			else
			{
				for (int index = 0; index < cachedFileListItems.Count; index++)
				{
					FileRow fileRow = cachedFileListItems[index];
					if (fileRow.IsHidden)
					{
						fileRow.IsHidden = false;
					}
					else if (fileRow.Selected)
					{
						AddSelectedFilterValues(fileRow);
					}
				}
			}

			RebuildVisibleRows();
			RestoreDgvSelectionFromModel();
			lastFileListFilterText = activeFilterContext.filterText;
			SubscribeTagFieldTextHandlers();
			textHandlersResubscribed = true;
			ScheduleSelectionStatusUpdate(refreshStatusAllInfo: false);
			if (SelectedFileCount == 1)
			{
				FileRow selectedRow = GetSingleSelectedFileRow();
				if (selectedRow != null)
				{
					LoadSingleSelectedFile(selectedRow);
				}
			}
			else
			{
				StartSelectionStatusUpdateTimer();
			}
			UpdateSelectionCommandState();
		}
		finally
		{
			// 异常路径下也必须重订 TextChanged,否则字段变更监听会永久脱落。
			if (!textHandlersResubscribed)
			{
				SubscribeTagFieldTextHandlers();
			}
			tagComboBoxes.Values.ForEachItem(EndComboBoxUpdate);
		}
	}

	private void FilterInputTimer_Tick(object sender, EventArgs e)
	{
		ApplyFileListFilter(requireFilterText: false, suspendListSorting: true);
	}

	private void ScheduleSelectionStatusUpdate(bool refreshStatusAllInfo)
	{
		fileListStatusTimer.Tag = refreshStatusAllInfo;
		if (!fileListStatusTimer.Enabled)
		{
			fileListStatusTimer.Start();
		}
	}

	private void FileListStatusTimer_Tick(object sender, EventArgs e)
	{
		bool refreshStatusAllInfo = fileListStatusTimer.Tag is bool value && value;
		UpdateFileListStatusSummary(refreshStatusAllInfo);
		fileListStatusTimer.Stop();
	}

	private void LoadTagEditorState(ConfigDescriptorState selectedTag)
	{
		TagEditorStateLoadContext tagEditorState = new TagEditorStateLoadContext
		{
			owner = this,
			selectedTag = selectedTag
		};
		multiSelectionCoverList = null;
		compressCoverResolution = false;
		if (tagEditorState.selectedTag != null && tagEditorState.selectedTag.IsLoadedSuccessfully())
		{
			selectedTagState = tagEditorState.selectedTag;
			tagComboBoxes.ForEachItem(tagEditorState.LoadSingleFileTagField);
			currentCoverIndex = 0;
			RefreshCoverPreview();
			return;
		}
		selectedTagState = null;
		currentCoverIndex = 0;
		if (SelectedFileCount <= 1)
		{
			tagComboBoxes.ForEachItem(tagEditorState.ClearTagField);
			ClearCoverPreview();
			return;
		}
		tagComboBoxes.ForEachItem(tagEditorState.LoadMultiFileTagField);
		multiSelectionCoverList = new List<ConfigDescriptorState.PictureData>();
		RefreshCoverPreview();
	}

	private void ResetKeepBlankComboBoxItems(ComboBox comboBox)
	{
		if (comboBox.Items.Count > 2)
		{
			ComboBox.ObjectCollection items = comboBox.Items;
			items.Clear();
			items.Add("<keep>");
			items.Add("<blank>");
		}
	}

	private void ClearCoverPreview()
	{
		if (coverPictureBox.SizeMode != PictureBoxSizeMode.CenterImage)
		{
			SetCoverPreviewImage(GetNoCoverPreviewImage());
			coverPictureBox.SizeMode = PictureBoxSizeMode.CenterImage;
			coverDimensionsLabel.Text = "";
			coverFileSizeLabel.Text = "";
			coverPictureTypeLabel.Text = "";
			coverNavigationPanel.Visible = false;
		}
		if (coverMimeTypeLabel.Text != "")
		{
			coverMimeTypeLabel.Text = "";
		}
	}

	private List<ConfigDescriptorState.PictureData> GetCurrentCoverList()
	{
		if (selectedTagState == null)
		{
			return multiSelectionCoverList;
		}
		return selectedTagState["allpicturedata"] as List<ConfigDescriptorState.PictureData>;
	}

	private void RefreshCoverPreview()
	{
		List<ConfigDescriptorState.PictureData> coverList = GetCurrentCoverList();
		if (coverList == null)
		{
			ClearCoverPreview();
			return;
		}
		coverNavigationPanel.Visible = coverList.Count > 1;
		if (coverNavigationPanel.Visible)
		{
			coverIndexLabel.Text = currentCoverIndex + 1 + "/" + coverList.Count;
			previousCoverButton.Enabled = currentCoverIndex > 0;
			nextCoverButton.Enabled = currentCoverIndex < coverList.Count - 1;
		}
		if (currentCoverIndex >= 0 && currentCoverIndex < coverList.Count)
		{
			ConfigDescriptorState.PictureData selectedCover = coverList[currentCoverIndex];
			Image image = ConfigDescriptorState.LoadPictureImage(selectedCover);
			if (image != null)
			{
				SetCoverPreviewImage(image);
				coverPictureBox.SizeMode = PictureBoxSizeMode.Zoom;
				coverMimeTypeLabel.Text = selectedCover.MimeType;
				coverDimensionsLabel.Text = image.Width + "x" + image.Height;
				coverFileSizeLabel.Text = TextUtilities.FormatFileSize(selectedCover.ImageBytes.Length);
				coverPictureTypeLabel.Text = selectedCover.PictureType;
			}
		}
		else if (multiSelectionCoverList != null)
		{
			ClearCoverPreview();
			int pictureResolutionLimits = Settings.Default.PictureResolutionLimits;
			coverMimeTypeLabel.Text = (compressCoverResolution ? $"{pictureResolutionLimits}x{pictureResolutionLimits}" : "<keep>");
		}
		else
		{
			ClearCoverPreview();
		}
	}

	private Image GetNoCoverPreviewImage()
	{
		return ImageUtilities.LoadCachedResourceBitmap("no_cover", new Size(ImageUtilities.ScaleByDpi(96f, this), ImageUtilities.ScaleByDpi(96f, this)));
	}

	private void SetCoverPreviewImage(Image image)
	{
		Image oldImage = coverPictureBox.Image;
		coverPictureBox.Image = image;
		if (oldImage != null && oldImage != image && oldImage != GetNoCoverPreviewImage())
		{
			oldImage.Dispose();
		}
	}

	private void PreviousCover_Click(object sender, EventArgs e)
	{
		if (currentCoverIndex <= 0)
		{
			return;
		}
		currentCoverIndex--;
		RefreshCoverPreview();
	}

	private void NextCover_Click(object sender, EventArgs e)
	{
		List<ConfigDescriptorState.PictureData> coverList = GetCurrentCoverList();
		if (coverList == null || currentCoverIndex >= coverList.Count - 1)
		{
			return;
		}
		currentCoverIndex++;
		RefreshCoverPreview();
	}

	private void SetLanguageEnglish_Click(object sender, EventArgs e)
	{
		ApplyLanguageResources("en");
	}

	private void SetLanguageSimplifiedChinese_Click(object sender, EventArgs e)
	{
		ApplyLanguageResources("zh-CHS");
	}

	private void SetLanguageTraditionalChinese_Click(object sender, EventArgs e)
	{
		ApplyLanguageResources("zh-CHT");
	}

	private TrackSearchContext BuildTrackSearchContext()
	{
		return new TrackSearchContext(selectedTagState, tagComboBoxes["title"].Text, tagComboBoxes["artist"].Text, tagComboBoxes["album"].Text, tagComboBoxes["comment"].Text);
	}

	private void EditLyrics_Click(object sender, EventArgs e)
	{
		if (selectedTagState == null)
		{
			return;
		}
		LyricEditorDialog lyricEditor = new LyricEditorDialog();
		lyricEditor.SetSearchContext(BuildTrackSearchContext());
		lyricEditor.SetLyricText(lyricsComboBox.Text);
		if (lyricEditor.ShowDialog() != DialogResult.OK)
		{
			return;
		}
		lyricsComboBox.Text = lyricEditor.GetLyricText();
		if (lyricEditor.ShouldSaveAfterClose())
		{
			saveTagsMenuItem.PerformClick();
		}
	}

	private void SearchLyricsFromSource(SearchSource? source)
	{
		if (selectedTagState == null)
		{
			return;
		}
		LyricSearchDialog lyricSearchDialog = new LyricSearchDialog();
		lyricSearchDialog.SetTrackInfo(BuildTrackSearchContext());
		lyricSearchDialog.SetSelectedSource(source);
		if (lyricSearchDialog.ShowDialog() == DialogResult.OK)
		{
			LyricSearchResult globalDescriptorAdapter = lyricSearchDialog.GetSelectedLyric();
			if (globalDescriptorAdapter.DeferredLyricLoader != null)
			{
				SimpleProgressDialog progressDialog = new SimpleProgressDialog(taskbarProgress);
				progressDialog.SetMessage(Resources.Msg_Downloading);
				StartDownloadLyric(globalDescriptorAdapter, progressDialog);
				progressDialog.ShowProgressDialog();
			}
			else
			{
				lyricsComboBox.Text = globalDescriptorAdapter.GetFormattedLyricText();
			}
		}
	}

	private async void StartDownloadLyric(LyricSearchResult lyricResult, SimpleProgressDialog progressDialog)
	{
		LyricDownloadTaskContext lyricDownloadContext = new LyricDownloadTaskContext();
		lyricDownloadContext.progressDialog = progressDialog;
		lyricDownloadContext.lyricResult = lyricResult;
		lyricDownloadContext.cancellationSource = new CancellationTokenSource();
		lyricDownloadContext.progressDialog.SetCancelAction(lyricDownloadContext.Cancel);
		ComboBox lyricTextComboBox = lyricsComboBox;
		try
		{
			string result = await Task.Run((Func<string>)lyricDownloadContext.DownloadLyricText, lyricDownloadContext.cancellationSource.Token);
			lyricTextComboBox.Text = result;
		}
		catch (System.Exception ex)
		{
			ReportAsyncOperationErrorIfNotCancellation(ex, lyricDownloadContext.cancellationSource, nameof(StartDownloadLyric));
		}
		finally
		{
			lyricTextComboBox = null;
			lyricDownloadContext.progressDialog.CloseProgressDialog();
		}
	}

	private void ApplyFileSelectionMode(FileSelectionMode selectionMode, bool refreshStatusAllInfo)
	{
		tagComboBoxes.Values.ForEachItem(BeginComboBoxUpdate);
		bool textHandlersResubscribed = false;
		try
		{
		fileListView.Focus();
		UnsubscribeTagFieldTextHandlers();
		tagComboBoxes.ForEachItem(ClearTagFieldSelectionState);

		foreach (FileRow fileRow in visibleRows)
		{
			switch (selectionMode)
			{
				case FileSelectionMode.SelectAll:
					SetFileRowSelected(fileRow, selected: true);
					break;
				case FileSelectionMode.UnselectAll:
					SetFileRowSelected(fileRow, selected: false);
					break;
				case FileSelectionMode.Invert:
					SetFileRowSelected(fileRow, !fileRow.Selected);
					break;
			}

			if (!fileRow.Selected)
			{
				continue;
			}

			AddSelectedFilterValues(fileRow);
		}
		RestoreDgvSelectionFromModel();

		SubscribeTagFieldTextHandlers();
		textHandlersResubscribed = true;
		if (SelectedFileCount == 1)
		{
			if (refreshStatusAllInfo)
			{
				ScheduleSelectionStatusUpdate(refreshStatusAllInfo: true);
			}
			FileRow selectedRow = GetSingleSelectedFileRow();
			if (selectedRow != null)
			{
				LoadSingleSelectedFile(selectedRow);
			}
		}
		else
		{
			ScheduleSelectionStatusUpdate(!refreshStatusAllInfo);
			StartSelectionStatusUpdateTimer();
		}
		UpdateSelectionCommandState();
		}
		finally
		{
			// 异常路径下也必须重订 TextChanged 并结束更新,否则监听脱落、组合框停留在 BeginUpdate。
			if (!textHandlersResubscribed)
			{
				SubscribeTagFieldTextHandlers();
			}
			tagComboBoxes.Values.ForEachItem(EndComboBoxUpdate);
		}
	}

	private static void BeginComboBoxUpdate(ComboBox comboBox)
	{
		comboBox.BeginUpdate();
	}

	private static void EndComboBoxUpdate(ComboBox comboBox)
	{
		comboBox.EndUpdate();
	}

	private static int FindColumnHeaderIndexByName(string columnName)
	{
		List<CustomColumnsDialog.ColumnHeaderInfo> columnHeaderSettings = configuredColumnHeaders;
		for (int index = 0; index < columnHeaderSettings.Count; index++)
		{
			if (columnHeaderSettings[index].Name == columnName)
			{
				return index;
			}
		}
		return -1;
	}

	// 从 GetSelectedFilterValue 提取:列筛选值归一纯核。comment 列显示截断(commentFullLength != 已截断 value.Length)
	// -> "Y\tT" 哨兵;空白 -> "";lyrics 列 -> "Y";否则原值。commentFullLength 传参(原 public 字段读,无副作用,
	// 由 && 条件求值变 callsite 无条件求值不可观测);value==null 且 comment 列时 value.Length 的 latent NRE 保留。
	internal static string ResolveSelectedFilterValue(string columnName, string value, int commentFullLength)
	{
		if (columnName == "comment" && commentFullLength != value.Length)
		{
			value = "Y\tT";
		}
		if (string.IsNullOrWhiteSpace(value))
		{
			return "";
		}
		if (columnName == "lyrics")
		{
			return "Y";
		}
		return value;
	}

	private string GetSelectedFilterValue(FileRow fileRow, string columnName)
	{
		int index = FindColumnHeaderIndexByName(columnName);
		string value = fileRow.CellTexts[index];
		return ResolveSelectedFilterValue(columnName, value, fileRow.CommentFullLength);
	}

	private void SelectAllFiles_Click(object sender, EventArgs e)
	{
		if (visibleRows.Count > 1)
		{
			ApplyFileSelectionMode(FileSelectionMode.SelectAll, refreshStatusAllInfo: false);
		}
		else if (visibleRows.Count == 1)
		{
			SetFileRowSelected(visibleRows[0], selected: true);
			RestoreDgvSelectionFromModel();
			OnFileSelectionSettled();
		}
	}

	private void UnselectAllFiles_Click(object sender, EventArgs e)
	{
		ApplyFileSelectionMode(FileSelectionMode.UnselectAll, refreshStatusAllInfo: false);
	}

	private void InvertFileSelection_Click(object sender, EventArgs e)
	{
		int unselectedItemCount = 0;
		FileRow singleUnselectedItem = null;
		foreach (FileRow fileRow in visibleRows)
		{
			if (!fileRow.Selected)
			{
				singleUnselectedItem = fileRow;
				if (++unselectedItemCount > 1)
				{
					break;
				}
			}
		}
		if (unselectedItemCount == 1)
		{
			SetFileRowSelected(singleUnselectedItem, selected: true);
			RestoreDgvSelectionFromModel();
			OnFileSelectionSettled();
		}
		else
		{
			ApplyFileSelectionMode(FileSelectionMode.Invert, refreshStatusAllInfo: false);
		}
	}

	private void RemoveSelectedItemsFromList_Click(object sender, EventArgs e)
	{
		(long selectedDurationMs, long selectedFileSizeBytes) = GetCachedDurationAndFileSize(selectedFilesStatusLabel.Tag);
		(long allDurationMs, long allFileSizeBytes) = GetCachedDurationAndFileSize(totalFilesStatusLabel.Tag);
		bool anyFileMode = FileSettings.IsAnyFileMode();
		FileRow[] selectedItems = SelectedFileRows.ToArray();
		var selectedItemSet = new HashSet<FileRow>(selectedItems);
		foreach (FileRow selectedItem in selectedItems)
		{
			GetListViewItemDurationAndFileSize(selectedItem, out var itemDurationMs, out var itemFileSizeBytes);
			selectedDurationMs -= itemDurationMs;
			selectedFileSizeBytes -= itemFileSizeBytes;
			allDurationMs -= itemDurationMs;
			allFileSizeBytes -= itemFileSizeBytes;
			if (anyFileMode)
			{
				FileSettings.RemoveForAnyFile(selectedItem.FilePath);
			}
		}
		cachedFileListItems.RemoveAll(itemState => selectedItemSet.Contains(itemState));
		RebuildVisibleRows();
		selectedFilesStatusLabel.Tag = (Math.Max(0L, selectedDurationMs), Math.Max(0L, selectedFileSizeBytes));
		totalFilesStatusLabel.Tag = (Math.Max(0L, allDurationMs), Math.Max(0L, allFileSizeBytes));
		RefreshStatusLabelsFromCachedTotals();
	}

	private void DeleteSelectedFiles_Click(object sender, EventArgs e)
	{
		if (!DialogService.ConfirmYesNo(string.Format(Resources.Msg_ConfirmRemoveFiles, SelectedFileCount) + "\n" + BuildSelectedFilePreview()))
		{
			return;
		}
		SelectedListViewItemInfo[] instance = CollectSelectedListViewItemInfos();
		ProgressDialog progressDialog = new ProgressDialog(taskbarProgress);
		StartRemoveFiles(instance, progressDialog);
		progressDialog.ShowDialogIfNotDisposed();
	}

	private void FileList_DragDrop(object sender, DragEventArgs e)
	{
		try
		{
			if (!FileSettings.IsAnyFileMode())
			{
				ClearLoadedFileList();
			}

			string[] droppedFilePaths = e.Data.GetData(DataFormats.FileDrop, true) as string[];
			ProgressDialog progressDialog = new ProgressDialog(taskbarProgress);
			FileSettings.DisableForDir();
			FileSettings.EnableForAnyFile();
			UpdateWindowTitle();
			StartAddAnyFiles(droppedFilePaths, progressDialog);
			progressDialog.ShowDialogIfNotDisposed();
		}
		catch (System.Exception ex)
		{
			Console.WriteLine("dropdrop error:" + ex.Message);
		}
	}

	private void FileList_DragEnter(object sender, DragEventArgs e)
	{
		e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
	}

	private void FileList_DragLeave(object sender, EventArgs e)
	{
	}

	private void FileList_ColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
	{
		if (e.ColumnIndex < 0)
		{
			return;
		}
		if (e.Button == MouseButtons.Right)
		{
			customizeColumnsContextMenuItem.Text = Resources.customcolumns;
			fileListHeaderContextMenu.Show(fileListView, fileListView.PointToClient(Cursor.Position));
			return;
		}
		if (e.Button != MouseButtons.Left)
		{
			return;
		}
		// ColumnIndex 是建列顺序(逻辑列序),与 FileRow.CellTexts / SortSetting.Column 同序。
		if (e.ColumnIndex == SortSetting.Column && SortSetting.SortOrder != SortOrder.None && SortSetting.SortOrder != SortOrder.Descending)
		{
			SortSetting.SortOrder = SortOrder.Descending;
		}
		else
		{
			SortSetting.SortOrder = SortOrder.Ascending;
		}
		UpdateFileListSortGlyph(e.ColumnIndex, SortSetting.SortOrder);
		int? previousColumn = SortSetting.Column;
		if (previousColumn.HasValue && SortSetting.Column != e.ColumnIndex)
		{
			UpdateFileListSortGlyph(SortSetting.Column, SortOrder.None);
		}
		SortSetting.Column = e.ColumnIndex;
		ApplyFileListSort();
	}

	// 对主表 cachedFileListItems 手动排序(复用 ListViewItemNaturalComparer,改读 FileRow.CellTexts),
	// 再重建 visibleRows 并据模型恢复选区。duration 列仍按显示文本排序(comparer 读 CellTexts 即满足)。
	private void ApplyFileListSort()
	{
		if (!SortSetting.Column.HasValue)
		{
			return;
		}
		ListViewItemNaturalComparer comparer = new ListViewItemNaturalComparer(SortSetting);
		cachedFileListItems.Sort((FileRow left, FileRow right) => comparer.Compare(left, right));
		RebuildVisibleRows();
		RestoreDgvSelectionFromModel();
	}

	private void UpdateFileListSortGlyph(int? columnIndex, SortOrder sortOrder)
	{
		if (!columnIndex.HasValue || columnIndex.Value < 0 || columnIndex.Value >= fileListView.Columns.Count)
		{
			return;
		}
		// 建列已统一为 Programmatic,可直接设排序三角(NotSortable 列设 SortGlyphDirection 会抛异常)。
		fileListView.Columns[columnIndex.Value].HeaderCell.SortGlyphDirection = sortOrder;
	}

	private void ExitApplication_Click(object sender, EventArgs e)
	{
		Close();
	}

	private void FileList_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
	{
		if (e.Button == MouseButtons.Right && e.RowIndex >= 0 && e.RowIndex < visibleRows.Count)
		{
			if (!visibleRows[e.RowIndex].Selected)
			{
				fileListView.ClearSelection();
				fileListView.Rows[e.RowIndex].Selected = true;
			}
			fileListItemContextMenu.Show(fileListView, fileListView.PointToClient(Cursor.Position));
		}
	}

	private void CoverPicture_MouseUp(object sender, MouseEventArgs e)
	{
		if (e.Button != MouseButtons.Right)
		{
			return;
		}
		addCoverMenuItem.Text = localizedResources.GetString("panel1.AddCover");
		changeCoverResolutionMenuItem.Text = localizedResources.GetString(compressCoverResolution ? "panel1.CancelChangeResolutionCover" : "panel1.ChangeResolutionCover");
		removeCoverMenuItem.Text = localizedResources.GetString("panel1.RemoveCover");
		extractCoverMenuItem.Text = Resources.ExtractCover;
		coverTypeMenuItem.Text = localizedResources.GetString("panel1.CoverType");
		openCoverMenuItem.Text = Resources.OpenCover;
		chooseLocalCoverMenuItem.Text = localizedResources.GetString("panel1.AddCover.ChooseLocalFile");
		searchCoverFromNetworkMenuItem.Text = localizedResources.GetString("panel1.AddCover.SearchFromNetwork");
		chooseCoverFromTagsMenuItem.Text = Resources.ChooseFromFileTags;
		addCoverMenuItem.Enabled = SelectedFileCount > 0;
		changeCoverResolutionMenuItem.Enabled = false;
		removeCoverMenuItem.Enabled = false;
		extractCoverMenuItem.Enabled = false;
		openCoverMenuItem.Enabled = false;
		searchCoverFromNetworkMenuItem.Enabled = false;
		chooseCoverFromTagsMenuItem.Enabled = false;
		coverTypeMenuItem.Enabled = false;
		coverTypeMenuItem.DropDownItems.Clear();

		List<ConfigDescriptorState.PictureData> coverList = null;
		if (selectedTagState == null && multiSelectionCoverList == null)
		{
			if (SelectedFileCount > 1)
			{
				chooseCoverFromTagsMenuItem.Enabled = true;
			}
			coverContextMenu.Show(coverPictureBox, e.Location);
			return;
		}

		coverList = GetCurrentCoverList();
		if (coverList.Count > 0)
		{
			removeCoverMenuItem.Enabled = true;
			extractCoverMenuItem.Enabled = true;
			openCoverMenuItem.Enabled = true;
			coverTypeMenuItem.Enabled = true;
			foreach (string coverType in ConfigDescriptorState.PictureTypeNames())
			{
				CoverTypeMenuContext coverTypeMenu = new CoverTypeMenuContext
				{
					owner = this,
					coverType = coverType
				};
				CoverTypeMenuItemClickContext clickContext = new CoverTypeMenuItemClickContext
				{
					menuContext = coverTypeMenu,
					pictureData = coverList[currentCoverIndex]
				};
				ToolStripMenuItem toolStripMenuItem = new ToolStripMenuItem(clickContext.menuContext.coverType);
				if (clickContext.pictureData.PictureType == clickContext.menuContext.coverType)
				{
					toolStripMenuItem.Checked = true;
				}
				coverTypeMenuItem.DropDownItems.Add(toolStripMenuItem);
				toolStripMenuItem.Click += clickContext.ApplyCoverType;
			}
		}
		else if (multiSelectionCoverList != null)
		{
			removeCoverMenuItem.Enabled = true;
		}

		if (SelectedFileCount == 1)
		{
			searchCoverFromNetworkMenuItem.Enabled = true;
		}
		if (SelectedFileCount > 1)
		{
			chooseCoverFromTagsMenuItem.Enabled = true;
			if (!coverList.Any() && Settings.Default.PictureResolutionLimits > 0)
			{
				changeCoverResolutionMenuItem.Enabled = true;
			}
		}
		coverContextMenu.Show(coverPictureBox, e.Location);
	}

	private void AddCoverToCurrentSelection(ConfigDescriptorState.PictureData pictureInfo, bool replaceExisting = false)
	{
		try
		{
			List<ConfigDescriptorState.PictureData> coverList = GetCurrentCoverList();
			if (coverList == null && SelectedFileCount > 1)
			{
				multiSelectionCoverList = new List<ConfigDescriptorState.PictureData>();
				coverList = multiSelectionCoverList;
			}
			if (coverList != null)
			{
				if (!replaceExisting && !overwriteCoverCheckBox.Checked)
				{
					coverList.Add(pictureInfo);
					currentCoverIndex = coverList.Count - 1;
					RefreshCoverPreview();
					return;
				}
				coverList.Clear();
				coverList.Add(pictureInfo);
				currentCoverIndex = coverList.Count - 1;
				RefreshCoverPreview();
			}
		}
		catch (System.Exception ex)
		{
			DialogService.ShowErrorMessage($"{Resources.Msg_Readfilefail}, {Resources.Msg_ErrorMessage}: {ex.Message}");
		}
	}

	private void AddCoverFromFile(string filePath, bool replaceExisting = false)
	{
		ConfigDescriptorState.PictureData pictureInfo = new ConfigDescriptorState.PictureData
		{
			ImageBytes = File.ReadAllBytes(filePath),
			PictureType = "Front Cover"
		};
		AddCoverToCurrentSelection(pictureInfo, replaceExisting);
	}

	private void ChooseLocalCover_Click(object sender, EventArgs e)
	{
		localCoverFileDialog.Filter = localizedResources.GetString("openFileDialog1.PictureFilter");
		localCoverFileDialog.Multiselect = false;
		localCoverFileDialog.FileName = "";
		if (localCoverFileDialog.ShowDialog() != DialogResult.OK)
		{
			return;
		}
		AddCoverFromFile(localCoverFileDialog.FileName);
	}

	private void SearchCoverFromNetwork_Click(object sender, EventArgs e)
	{
		SearchCoverFromSource(null);
	}

	private void ChooseCoverFromFileTags_Click(object sender, EventArgs e)
	{
		if (SelectedFileCount <= 1)
		{
			return;
		}
		List<string> list = new List<string>();
		foreach (FileRow fileRow in SelectedFileRows)
		{
			list.Add(fileRow.FilePath);
		}
		PictureFromTagsDialog pictureFromTagsDialog = new PictureFromTagsDialog();
		pictureFromTagsDialog.SetAudioFilePaths(list);
		if (pictureFromTagsDialog.ShowDialog() == DialogResult.OK && pictureFromTagsDialog.GetSelectedPicture() != null)
		{
			pictureFromTagsDialog.GetSelectedPicture().PictureType = "Front Cover";
			AddCoverToCurrentSelection(pictureFromTagsDialog.GetSelectedPicture());
		}
	}

	private void SearchCoverFromSource(SearchSource? source)
	{
		if (selectedTagState != null)
		{
			CoverSearchDialog coverSearchDialog = new CoverSearchDialog();
			coverSearchDialog.SetCurrentTrack(BuildTrackSearchContext());
			coverSearchDialog.SetPreferredSource(source);
			if (coverSearchDialog.ShowDialog() == DialogResult.OK && File.Exists(coverSearchDialog.GetSelectedCandidate().LocalCoverPath))
			{
				AddCoverFromFile(coverSearchDialog.GetSelectedCandidate().LocalCoverPath);
			}
		}
	}

	private void SearchTagsFromSource(SearchSource? source)
	{
		if (selectedTagState == null)
		{
			return;
		}
		CombinedTagSearchDialog combinedCoverSearchDialog = new CombinedTagSearchDialog();
		combinedCoverSearchDialog.SetSearchContext(BuildTrackSearchContext());
		combinedCoverSearchDialog.SetPreferredSource(source);
		if (combinedCoverSearchDialog.ShowDialog() == DialogResult.OK)
		{
			TrackSearchResult tokenSystemRole = combinedCoverSearchDialog.GetSelectedTrackResult();
			Dictionary<string, bool> dictionary = CombinedTagOverwriteOptionsDialog.GetOverwriteOptions();
			if (dictionary["title"])
			{
				SetComboBoxSearchValue(titleComboBox, tokenSystemRole.Title);
			}
			if (dictionary["artist"])
			{
				SetComboBoxSearchValue(artistComboBox, tokenSystemRole.Artist);
			}
			if (dictionary["album"])
			{
				SetComboBoxSearchValue(albumComboBox, tokenSystemRole.Album);
			}
			if (dictionary["year"])
			{
				SetComboBoxSearchValue(yearComboBox, tokenSystemRole.Year);
			}
			if (dictionary["trackstr"])
			{
				SetComboBoxSearchValue(trackComboBox, tokenSystemRole.Track);
			}
			if (dictionary["discstr"])
			{
				SetComboBoxSearchValue(discComboBox, tokenSystemRole.Disc);
			}
			if (dictionary["genre"])
			{
				SetComboBoxSearchValue(genreComboBox, tokenSystemRole.Genre);
			}
			if (dictionary["comment"])
			{
				SetComboBoxSearchValue(commentComboBox, tokenSystemRole.Comment);
			}
			if (dictionary["lyrics"])
			{
				SetComboBoxSearchValue(lyricsComboBox, tokenSystemRole.LyricResult?.GetFormattedLyricText());
			}
			if (dictionary["picture"] && tokenSystemRole.Cover != null && !string.IsNullOrWhiteSpace(tokenSystemRole.Cover.LocalCoverPath) && File.Exists(tokenSystemRole.Cover.LocalCoverPath))
			{
				AddCoverFromFile(tokenSystemRole.Cover.LocalCoverPath, replaceExisting: true);
			}
			if (dictionary["year"] && string.IsNullOrWhiteSpace(tokenSystemRole.Year))
			{
				SimpleProgressDialog progressDialog = new SimpleProgressDialog(taskbarProgress);
				progressDialog.SetMessage(Resources.Msg_Downloading);
				StartSearchYearLookup(tokenSystemRole, progressDialog);
				progressDialog.ShowProgressDialog();
			}
		}
	}

	private static void SetComboBoxSearchValue(ComboBox comboBox, object value)
	{
		string resolvedText = ResolveSearchValue(value);
		if (resolvedText != null)
		{
			comboBox.Text = resolvedText;
		}
	}

	// 纯核(从 SetComboBoxSearchValue 分离控件副作用,behavior-preserving):解析搜索框应显示的文本——
	// 非空白 string 原样返回;int>0 返回其 ToString();其余(空白串 / int<=0 / 其他类型 / null)返回 null 表示不写。
	internal static string ResolveSearchValue(object value)
	{
		if (value is string text && !string.IsNullOrWhiteSpace(text))
		{
			return text;
		}
		if (value is int number && number > 0)
		{
			return number.ToString();
		}
		return null;
	}

	private async void StartSearchYearLookup(TrackSearchResult searchResult, SimpleProgressDialog progressDialog)
	{
		ReleaseYearSearchTaskContext releaseYearContext = new ReleaseYearSearchTaskContext();
		releaseYearContext.progressDialog = progressDialog;
		releaseYearContext.trackResult = searchResult;
		releaseYearContext.cancellationSource = new CancellationTokenSource();
		releaseYearContext.progressDialog.SetCancelAction(releaseYearContext.Cancel);
		try
		{
			string result = await Task.Run((Func<string>)releaseYearContext.FetchReleaseYear, releaseYearContext.cancellationSource.Token);
			if (!string.IsNullOrWhiteSpace(result))
			{
				yearComboBox.Text = result;
			}
		}
		catch (System.Exception ex)
		{
			ReportAsyncOperationErrorIfNotCancellation(ex, releaseYearContext.cancellationSource, nameof(StartSearchYearLookup));
		}
		finally
		{
			releaseYearContext.progressDialog.CloseProgressDialog();
		}
	}

	private void ToggleCoverResolutionLimit_Click(object sender, EventArgs e)
	{
		compressCoverResolution = !compressCoverResolution;
		int pictureResolutionLimits = Settings.Default.PictureResolutionLimits;
		coverMimeTypeLabel.Text = (compressCoverResolution ? $"{pictureResolutionLimits}x{pictureResolutionLimits}" : "<keep>");
	}

	private void RemoveCurrentCover_Click(object sender, EventArgs e)
	{
		List<ConfigDescriptorState.PictureData> coverList = GetCurrentCoverList();
		if (coverList.Any())
		{
			coverList.RemoveAt(currentCoverIndex);
			currentCoverIndex = 0;
		}
		else if (multiSelectionCoverList != null)
		{
			multiSelectionCoverList = null;
			compressCoverResolution = false;
		}
		RefreshCoverPreview();
	}

	private void ExtractCover_Click(object sender, EventArgs e)
	{
		SaveCurrentCover();
	}

	private void OpenCurrentCover_Click(object sender, EventArgs e)
	{
		try
		{
			ConfigDescriptorState.PictureData pictureInfo = (selectedTagState["allpicturedata"] as List<ConfigDescriptorState.PictureData>)[currentCoverIndex];
			if (pictureInfo.MimeType != null && pictureInfo.Width > 0 && pictureInfo.Height > 0)
			{
				string extension = ImageUtilities.GetImageExtensionForMimeType(pictureInfo.MimeType, "");
				string tempCoverPath = (string)PathFileUtilities.GetPictureCacheDirectory() + "tempcover" + extension;
				File.WriteAllBytes(tempCoverPath, pictureInfo.ImageBytes);
				Process.Start(tempCoverPath);
			}
		}
		catch (System.Exception ex)
		{
			DialogService.ShowErrorMessage($"{Resources.Msg_ExtractCoverFail}, {Resources.Msg_ErrorMessage}: {ex.Message}");
		}
	}

	private void RefreshSelectedFiles_Click(object sender, EventArgs e)
	{
		RefreshSelectedItems(showErrorMessageBox: true, showProgressDialog: true, refreshStatusAllInfo: true);
	}

	private void RefreshSelectedItems(bool showErrorMessageBox, bool showProgressDialog, bool refreshStatusAllInfo, (string, bool)? previousMessage = null)
	{
		RefreshItemsWithOptionalProgressDialog(CollectSelectedListViewItemInfos(), showErrorMessageBox, showProgressDialog, refreshStatusAllInfo, previousMessage);
	}

	private void RefreshItemsWithOptionalProgressDialog(SelectedListViewItemInfo[] itemInfos, bool showErrorMessageBox, bool showProgressDialog, bool refreshStatusAllInfo, (string, bool)? previousMessage = null, bool listForMirror = false)
	{
		ProgressDialog progressDialog = (showProgressDialog ? new ProgressDialog(taskbarProgress) : null);
		StartRefreshItems(itemInfos, progressDialog, showErrorMessageBox, refreshStatusAllInfo, previousMessage, listForMirror);
		progressDialog?.ShowDialogIfNotDisposed();
	}

	private void RefreshConfiguredFileList_Click(object sender, EventArgs e)
	{
		ClearLoadedFileList();
		UpdateWindowTitle();
		ProgressDialog progressDialog = new ProgressDialog(taskbarProgress);
		StartAddConfiguredFileList(progressDialog);
		progressDialog.ShowDialogIfNotDisposed();
	}

	private void SaveCurrentFileListColumnWidths()
	{
		foreach (DataGridViewColumn columnHeader in fileListView.Columns)
		{
			CustomColumnsDialog.ColumnHeaderInfo columnHeaderInfo = columnHeader.Tag as CustomColumnsDialog.ColumnHeaderInfo;
			if (columnHeader.Width > 0)
			{
				// 持久化语义 = 启动屏刻度(恢复端 InitializeFileListColumnsAndIcons 在启动屏
				// 原样使用)。窗口当前在其他 DPI 屏时,列宽已被 RescaleCustomAssetsForDpi 按屏
				// 缩放,换算回启动刻度再存,避免在低 DPI 屏退出→下次启动列宽整体缩水。
				int width = columnHeader.Width;
				if (customAssetsDpi > 0 && customAssetsDpi != startupDpi)
				{
					width = Math.Max(5, (int)Math.Round((float)width * startupDpi / customAssetsDpi));
				}
				columnHeaderInfo.width = width;
			}
			else if (columnHeaderInfo.width == 0)
			{
				columnHeaderInfo.width = ImageUtilities.ScaleByDpi(100f);
			}
		}
	}

	private void ConfigureFileListColumns_Click(object sender, EventArgs e)
	{
		SaveCurrentFileListColumnWidths();
		CustomColumnsDialog customColumnsDialog = new CustomColumnsDialog();
		if (customColumnsDialog.ShowDialog() != DialogResult.OK)
		{
			return;
		}

		int displayIndex = 0;
		foreach (ListViewItem listViewItem in customColumnsDialog.GetColumnListItems())
		{
			CustomColumnsDialog.ColumnHeaderInfo columnHeaderInfo = listViewItem.Tag as CustomColumnsDialog.ColumnHeaderInfo;
			columnHeaderInfo.isShow = listViewItem.Checked;
			if (listViewItem.Checked)
			{
				columnHeaderInfo.displayIndex = displayIndex++;
			}
			else
			{
				columnHeaderInfo.beforeHideDisplayIndex = displayIndex;
			}
		}

		foreach (CustomColumnsDialog.ColumnHeaderInfo columnHeaderInfo in CustomColumnsDialog.GetColumnHeaderSettings())
		{
			if (!columnHeaderInfo.isShow)
			{
				columnHeaderInfo.displayIndex = displayIndex++;
			}
		}

		foreach (DataGridViewColumn columnHeader in fileListView.Columns)
		{
			CustomColumnsDialog.ColumnHeaderInfo columnHeaderInfo = columnHeader.Tag as CustomColumnsDialog.ColumnHeaderInfo;
			columnHeader.Visible = columnHeaderInfo.isShow;
			if (columnHeaderInfo.isShow)
			{
				columnHeader.Width = ScaleStoredColumnWidthToCurrentDpi(columnHeaderInfo.tempWidth);
			}
		}
		// DisplayIndex 必须是 0..N-1 排列;按目标 displayIndex 升序顺次赋值,避免 DGV 中途重排冲突。
		int columnDisplayOrder = 0;
		foreach (DataGridViewColumn columnHeader in fileListView.Columns.Cast<DataGridViewColumn>().OrderBy((DataGridViewColumn c) => ((CustomColumnsDialog.ColumnHeaderInfo)c.Tag).displayIndex).ToList())
		{
			columnHeader.DisplayIndex = columnDisplayOrder++;
		}
		CustomColumnsDialog.SaveColumnHeaderSettings();
		DialogService.TrySaveApplicationSettings();
		fileListView.Refresh();
	}

	// 从 CompressPictures prologue 提取:图片压缩上限解算。还原模式用固定 10MB/无分辨率限制/AUTO;否则取 Settings
	// 配置值。关键:sizeLimitKB * 1024 保持 int 乘法(sizeLimitKB 为 int),溢出成负后经 <=0L 归一为 long.MaxValue
	// —— 严禁提升为 long 乘法(会消除溢出边界);Settings 值由调用点读入传参(getter 纯 (T)this[...],eager 读无副作用)。
	internal static (long MaxByteLength, int MaxResolution, string FormatMode) ResolvePictureCompressionLimits(bool useRestoreLimits, int sizeLimitKB, int resolutionLimit, string formatLimit)
	{
		long maxByteLength = useRestoreLimits ? 10240000 : (sizeLimitKB * 1024);
		int maxResolution = (!useRestoreLimits) ? resolutionLimit : 0;
		string formatMode = useRestoreLimits ? "AUTO" : formatLimit;
		if (maxByteLength <= 0L)
		{
			maxByteLength = long.MaxValue;
		}
		return (maxByteLength, maxResolution, formatMode);
	}

	public static void CompressPictures(List<ConfigDescriptorState.PictureData> pictures, bool useRestoreLimits)
	{
		var compressionLimits = ResolvePictureCompressionLimits(useRestoreLimits, Settings.Default.PictureSizeLimitsKB, Settings.Default.PictureResolutionLimits, Settings.Default.PictureFormatLimits);
		PictureCompressionOptions pictureCompressionOptions = new PictureCompressionOptions();
		pictureCompressionOptions.maxByteLength = compressionLimits.MaxByteLength;
		pictureCompressionOptions.maxResolution = compressionLimits.MaxResolution;
		pictureCompressionOptions.formatMode = compressionLimits.FormatMode;
		using List<ConfigDescriptorState.PictureData>.Enumerator enumerator = pictures.GetEnumerator();
		while (enumerator.MoveNext())
		{
			PictureCompressionItem pictureCompressionItem = new PictureCompressionItem();
			pictureCompressionItem.options = pictureCompressionOptions;
			pictureCompressionItem.pictureData = enumerator.Current;
			PictureCompressionWorker pictureCompressionWorker = new PictureCompressionWorker();
			pictureCompressionWorker.compressionItem = pictureCompressionItem;
			pictureCompressionWorker.compressionItem.pictureData.ProcessingFailed = false;
			pictureCompressionWorker.isAlreadyWithinLimits = pictureCompressionWorker.compressionItem.pictureData.ImageBytes.Length <= pictureCompressionWorker.compressionItem.options.maxByteLength && ConfigDescriptorState.SupportedPictureMimeTypes().Contains(pictureCompressionWorker.compressionItem.pictureData.MimeType) && (pictureCompressionWorker.compressionItem.options.formatMode == "AUTO" || pictureCompressionWorker.compressionItem.pictureData.MimeType == "image/jpeg");
			if (pictureCompressionWorker.isAlreadyWithinLimits && pictureCompressionWorker.compressionItem.options.maxResolution == 0)
			{
				continue;
			}
			pictureCompressionWorker.workingImage = null;
			pictureCompressionWorker.encodeWorkingImageAsJpeg = pictureCompressionWorker.EncodeCurrentImageAsJpeg;
			pictureCompressionWorker.scaleDownWorkingImageAndEncode = pictureCompressionWorker.ScaleCurrentImageAndEncode;
			pictureCompressionWorker.resizeWorkingImageAndEncode = pictureCompressionWorker.ResizeCurrentImageAndEncode;
			if (pictureCompressionWorker.compressionItem.options.maxResolution == 0)
			{
				pictureCompressionWorker.compressionRetrySteps = Array.ConvertAll(PictureCompressionWorker.fixedResolutionRetrySteps,
					step => (Func<bool>)(() => pictureCompressionWorker.resizeWorkingImageAndEncode(step.Resolution, step.Quality)));
			}
			else
			{
				pictureCompressionWorker.compressionRetrySteps = Array.ConvertAll(PictureCompressionWorker.configuredLimitRetryQualities,
					quality => (Func<bool>)(() => pictureCompressionWorker.resizeWorkingImageAndEncode(pictureCompressionWorker.compressionItem.options.maxResolution, quality)));
			}
			try
			{
				Func<bool> compressPicture = pictureCompressionWorker.CompressPicture;
				pictureCompressionWorker.compressionItem.pictureData.ProcessingFailed = !compressPicture();
			}
			catch (System.Exception ex)
			{
				pictureCompressionWorker.compressionItem.pictureData.ProcessingFailed = true;
				Console.WriteLine("compress picture fail: " + ex.Message);
			}
			finally
			{
				pictureCompressionWorker.workingImage?.Dispose();
			}
		}
	}

	private static bool HasPictureProcessingFailure(ConfigDescriptorState.PictureData pictureInfo)
	{
		return pictureInfo.ProcessingFailed;
	}

	internal static int ParseLeadingNumber(string value)
	{
		Match match = Regex.Match(value, "^\\d+");
		if (match.Success)
		{
			if (!int.TryParse(match.Groups[0].Value, out var result))
			{
				return 0;
			}
			return result;
		}
		return -1;
	}

	private void SaveTags_Click(object sender, EventArgs e)
	{
		Func<string, string, bool> validateField = ValidateNumberedTagField;
		if (!validateField("trackstr", Resources.Msg_InvalidTrackFormat) || !validateField("discstr", Resources.Msg_InvalidDiscFormat))
		{
			return;
		}
		if (SelectedFileCount > 1 && !DialogService.ConfirmYesNo(string.Format(Resources.Msg_ConfirmSaveTags, SelectedFileCount) + "\n" + BuildSelectedFilePreview()))
		{
			return;
		}

		Dictionary<string, object> valueMap = new Dictionary<string, object>();
		foreach (KeyValuePair<string, ComboBox> tagComboBoxEntry in tagComboBoxes)
		{
			valueMap.Add(tagComboBoxEntry.Key, tagComboBoxEntry.Value.Text);
		}

		SelectedListViewItemInfo[] selectedItems = CollectSelectedListViewItemInfos();
		bool? canCancelReadOnly = ConfirmReadOnlyFileHandling(selectedItems);
		if (!canCancelReadOnly.HasValue)
		{
			return;
		}

		if (selectedItems.Length == 1)
		{
			if (selectedTagState != null)
			{
				valueMap.Add("allpicturedata", selectedTagState["allpicturedata"] as List<ConfigDescriptorState.PictureData>);
			}
		}
		else if (selectedItems.Length > 1)
		{
			if (multiSelectionCoverList != null)
			{
				if (multiSelectionCoverList.Any())
				{
					valueMap.Add("allpicturedata", multiSelectionCoverList);
				}
			}
			else
			{
				valueMap.Add("allpicturedata", new List<ConfigDescriptorState.PictureData>());
			}
		}
		else
		{
			return;
		}

		ProgressDialog progressDialog = new ProgressDialog(taskbarProgress);
		StartCommonSaveTags(selectedItems, progressDialog, valueMap, canCancelReadOnly == true, compressCoverResolution);
		progressDialog.ShowDialogIfNotDisposed();
	}

	private void UndoLastOperation_Click(object sender, EventArgs e)
	{
		int undoTagsCount = TagHistoryRepository.UndoTagsCount();
		if (undoTagsCount > 0)
		{
			if (DialogService.ConfirmYesNo(string.Format(Resources.Msg_ConfirmUndoTags, undoTagsCount) + "\n" + TagHistoryRepository.BuildUndoTagsPreview()))
			{
				ProgressDialog progressDialog = new ProgressDialog(taskbarProgress);
				StartUndoSaveTags(progressDialog);
				progressDialog.ShowDialogIfNotDisposed();
			}
			return;
		}

		int renameUndoOperationsCount = TagHistoryRepository.RenameUndoOperationsCount();
		if (renameUndoOperationsCount > 0)
		{
			if (DialogService.ConfirmYesNo(string.Format(Resources.Msg_ConfirmUndoRename, renameUndoOperationsCount) + "\n" + TagHistoryRepository.BuildRenameUndoPreview()))
			{
				ProgressDialog progressDialog = new ProgressDialog(taskbarProgress);
				StartUndoRename(progressDialog);
				progressDialog.ShowDialogIfNotDisposed();
			}
			return;
		}

		undoMenuItem.Enabled = false;
		undoToolStripButton.Enabled = false;
	}

	private void ReformatLyricTimeTags_Click(object sender, EventArgs e)
	{
		StartBatchLyricsOperation("menuStrip1.Batch.ReformatTimetag");
	}

	private void RemoveLyricTimeTags_Click(object sender, EventArgs e)
	{
		StartBatchLyricsOperation("menuStrip1.Batch.RemoveTimetag");
	}

	private void DeleteBlankLyricLines_Click(object sender, EventArgs e)
	{
		StartBatchLyricsOperation("menuStrip1.Batch.DeleteLinesOfBlankText");
	}

	private void DeleteLyricHeaderTags_Click(object sender, EventArgs e)
	{
		StartBatchLyricsOperation("menuStrip1.Batch.DeleteHeadTags");
	}

	private void ImportLrcFiles_Click(object sender, EventArgs e)
	{
		StartBatchLyricsOperation("menuStrip1.Batch.ImportLrcFile");
	}

	private void ClearTags_Click(object sender, EventArgs e)
	{
		if (!DialogService.ConfirmYesNo(string.Format(Resources.Msg_ConfirmClearTags, SelectedFileCount) + "\n" + BuildSelectedFilePreview()))
		{
			return;
		}
		SelectedListViewItemInfo[] selectedItems = CollectSelectedListViewItemInfos();
		bool? canCancelReadOnly = ConfirmReadOnlyFileHandling(selectedItems);
		if (!canCancelReadOnly.HasValue)
		{
			return;
		}
		ProgressDialog progressDialog = new ProgressDialog(taskbarProgress);
		StartClearTags(selectedItems, progressDialog, canCancelReadOnly == true);
		progressDialog.ShowDialogIfNotDisposed();
	}

	private string BuildSelectedFilePreview()
	{
		return BuildSelectedFilePreview(SelectedFileRows.Select(fileRow => fileRow.CellTexts[0]));
	}

	// 纯核(从 BuildSelectedFilePreview 分离实例 SelectedFileRows 依赖,behavior-preserving):取每行首列文本,
	// 前 10 行各追加 "<text>\n";第 11 行起追加 "..." 并停止(故 >10 才出现 "...",且 "..." 前无换行)。空集 -> ""。
	internal static string BuildSelectedFilePreview(IEnumerable<string> firstColumnTexts)
	{
		StringBuilder stringBuilder = new StringBuilder();
		int lineCount = 0;
		foreach (string firstColumnText in firstColumnTexts)
		{
			if (lineCount < 10)
			{
				stringBuilder.Append(firstColumnText + "\n");
				lineCount++;
				continue;
			}
			stringBuilder.Append("...");
			break;
		}
		return stringBuilder.ToString();
	}

	private async void StartRenameFiles((string Path, string NewPath, int ListViewIndex)[] itemInfos, ProgressDialog progressDialog, bool isChsToCht)
	{
		ConvertFilenameChineseBatchContext batchContext = new ConvertFilenameChineseBatchContext();
		batchContext.progressDialog = progressDialog;
		batchContext.renameItems = itemInfos;
		batchContext.convertSimplifiedToTraditional = isChsToCht;
		batchContext.cancellationSource = new CancellationTokenSource();
		batchContext.progressDialog.CancelRequested += batchContext.Cancel;
		batchContext.renamedCount = 0;
		batchContext.failedCount = 0;
		batchContext.skippedCount = 0;
		batchContext.processedCount = 0;
		batchContext.currentFile = null;
		batchContext.progressDialog.ProgressUpdate += batchContext.UpdateProgress;
		batchContext.messageLog = new Page();
		try
		{
			await Task.Run((Action)batchContext.ConvertFilenames, batchContext.cancellationSource.Token);
			(string Path, string NewPath, int ListViewIndex)[] completedRenameItems = batchContext.renameItems;
			for (int i = 0; i < completedRenameItems.Length; i++)
			{
				(string Path, string NewPath, int ListViewIndex) renameItem = completedRenameItems[i];
				if (renameItem.NewPath != null)
				{
					FileRow fileRow = cachedFileListItems[renameItem.ListViewIndex];
					fileRow.FilePath = renameItem.NewPath;
					InvalidateFileRow(fileRow);
				}
			}
			(string, bool) value = BuildBatchResultMessage(batchContext.renameItems.Length, Resources.Msg_SaveCompleted, batchContext.renamedCount, batchContext.failedCount, batchContext.skippedCount, batchContext.processedCount, batchContext.messageLog.ToString(), includeSkippedBranch: true);
			batchContext.progressDialog.CloseAfterCompletion();
			GC.Collect();
			RefreshSelectedItems(showErrorMessageBox: false, showProgressDialog: true, refreshStatusAllInfo: true, previousMessage: value);
		}
		catch (System.Exception ex)
		{
			ReportAsyncOperationErrorIfNotCancellation(ex, batchContext.cancellationSource, nameof(StartRenameFiles));
		}
		finally
		{
			batchContext.progressDialog.CloseAfterCompletion();
		}
	}

	private async void StartCommonSaveTags(SelectedListViewItemInfo[] itemInfos, ProgressDialog progressDialog, Dictionary<string, object> valueMap, bool canCancelFileReadonly, bool needUpdatePictureResolution = false)
	{
		SaveTagsTaskContext saveTagsContext = new SaveTagsTaskContext();
		saveTagsContext.progressDialog = progressDialog;
		saveTagsContext.itemsToSave = itemInfos;
		saveTagsContext.tagValues = valueMap;
		saveTagsContext.owner = this;
		saveTagsContext.canCancelReadOnly = canCancelFileReadonly;
		saveTagsContext.shouldRefreshPictureResolution = needUpdatePictureResolution;
		saveTagsContext.cancellationSource = new CancellationTokenSource();
		saveTagsContext.progressDialog.CancelRequested += saveTagsContext.Cancel;
		saveTagsContext.savedCount = 0;
		saveTagsContext.failedCount = 0;
		saveTagsContext.skippedCount = 0;
		saveTagsContext.processedCount = 0;
		saveTagsContext.currentFile = null;
		saveTagsContext.progressDialog.ProgressUpdate += saveTagsContext.UpdateProgress;
		saveTagsContext.messageLog = new Page();
		try
		{
			await Task.Run((Action)saveTagsContext.SaveTags, saveTagsContext.cancellationSource.Token);
			(string, bool) value = BuildBatchResultMessage(saveTagsContext.itemsToSave.Length, Resources.Msg_SaveCompleted, saveTagsContext.savedCount, saveTagsContext.failedCount, saveTagsContext.skippedCount, saveTagsContext.processedCount, saveTagsContext.messageLog.ToString(), includeSkippedBranch: true);
			saveTagsContext.progressDialog.CloseAfterCompletion();
			GC.Collect();
			RefreshSelectedItems(showErrorMessageBox: false, showProgressDialog: true, refreshStatusAllInfo: true, previousMessage: value);
		}
		catch (System.Exception ex)
		{
			ReportAsyncOperationErrorIfNotCancellation(ex, saveTagsContext.cancellationSource, nameof(StartCommonSaveTags));
		}
		finally
		{
			saveTagsContext.progressDialog.CloseAfterCompletion();
		}
	}

	private async void StartUndoSaveTags(ProgressDialog progressDialog)
	{
		UndoSaveTagsTaskContext undoSaveTagsContext = new UndoSaveTagsTaskContext();
		undoSaveTagsContext.progressDialog = progressDialog;
		undoSaveTagsContext.cancellationSource = new CancellationTokenSource();
		undoSaveTagsContext.progressDialog.CancelRequested += undoSaveTagsContext.Cancel;
		undoSaveTagsContext.undoTagSnapshots = TagHistoryRepository.GetUndoTagSnapshots();
		undoSaveTagsContext.restoredCount = 0;
		undoSaveTagsContext.failedCount = 0;
		undoSaveTagsContext.skippedCount = 0;
		undoSaveTagsContext.processedCount = 0;
		undoSaveTagsContext.currentFile = null;
		undoSaveTagsContext.progressDialog.ProgressUpdate += undoSaveTagsContext.UpdateProgress;
		undoSaveTagsContext.messageLog = new Page();
		try
		{
			await Task.Run((Action)undoSaveTagsContext.RestoreSavedTags, undoSaveTagsContext.cancellationSource.Token);
			List<SelectedListViewItemInfo> refreshedItems = new List<SelectedListViewItemInfo>();
			undoSaveTagsContext.processedCount = 0;
			while (undoSaveTagsContext.processedCount < cachedFileListItems.Count)
			{
				UndoSaveTagsListItemMatcher listItemMatcher = new UndoSaveTagsListItemMatcher();
				listItemMatcher.fileRow = cachedFileListItems[undoSaveTagsContext.processedCount];
				if (undoSaveTagsContext.undoTagSnapshots.Find(listItemMatcher.MatchesSnapshotPath) != null)
				{
					refreshedItems.Add(new SelectedListViewItemInfo
					{
						Index = undoSaveTagsContext.processedCount,
						FilePath = listItemMatcher.fileRow.FilePath
					});
				}
				undoSaveTagsContext.processedCount++;
			}
			(string, bool) value = BuildBatchResultMessage(undoSaveTagsContext.undoTagSnapshots.Count, Resources.Msg_UndoCompleted, undoSaveTagsContext.restoredCount, undoSaveTagsContext.failedCount, undoSaveTagsContext.skippedCount, undoSaveTagsContext.processedCount, undoSaveTagsContext.messageLog.ToString(), includeSkippedBranch: false);
			undoSaveTagsContext.progressDialog.CloseAfterCompletion();
			TagHistoryRepository.ClearUndoState();
			RefreshItemsWithOptionalProgressDialog(refreshedItems.ToArray(), showErrorMessageBox: false, showProgressDialog: true, refreshStatusAllInfo: true, previousMessage: value, listForMirror: true);
		}
		catch (System.Exception ex)
		{
			ReportAsyncOperationErrorIfNotCancellation(ex, undoSaveTagsContext.cancellationSource, nameof(StartUndoSaveTags));
		}
		finally
		{
			undoSaveTagsContext.progressDialog.CloseAfterCompletion();
		}
	}

	private async void StartUndoRename(ProgressDialog progressDialog)
	{
		UndoRenameTaskContext undoRenameContext = new UndoRenameTaskContext();
		undoRenameContext.progressDialog = progressDialog;
		undoRenameContext.owner = this;
		undoRenameContext.cancellationSource = new CancellationTokenSource();
		undoRenameContext.progressDialog.CancelRequested += undoRenameContext.Cancel;
		undoRenameContext.renameUndoOperations = TagHistoryRepository.GetRenameUndoOperations();
		undoRenameContext.successCount = 0;
		undoRenameContext.failedCount = 0;
		undoRenameContext.skippedCount = 0;
		undoRenameContext.processedCount = 0;
		undoRenameContext.currentFile = null;
		undoRenameContext.progressDialog.ProgressUpdate += undoRenameContext.UpdateProgress;
		undoRenameContext.errorLog = new Page();
		try
		{
			await Task.Run((Action)undoRenameContext.UndoRenames, undoRenameContext.cancellationSource.Token);
			List<SelectedListViewItemInfo> list = new List<SelectedListViewItemInfo>();
			undoRenameContext.processedCount = 0;
			while (undoRenameContext.processedCount < cachedFileListItems.Count)
			{
				RenameUndoListItemMatcher listItemMatcher = new RenameUndoListItemMatcher();
				listItemMatcher.fileRow = cachedFileListItems[undoRenameContext.processedCount];
				(string oldPath, string newPath, bool failed) operation = undoRenameContext.renameUndoOperations.Find(listItemMatcher.MatchesCurrentPath);
				if (!string.IsNullOrWhiteSpace(operation.oldPath))
				{
					if (!operation.failed)
					{
						listItemMatcher.fileRow.FilePath = operation.oldPath;
						InvalidateFileRow(listItemMatcher.fileRow);
					}
					list.Add(new SelectedListViewItemInfo
					{
						Index = undoRenameContext.processedCount,
						FilePath = listItemMatcher.fileRow.FilePath
					});
				}
				undoRenameContext.processedCount++;
			}
			(string, bool) value = BuildBatchResultMessage(undoRenameContext.renameUndoOperations.Count, Resources.Msg_UndoCompleted, undoRenameContext.successCount, undoRenameContext.failedCount, undoRenameContext.skippedCount, undoRenameContext.processedCount, undoRenameContext.errorLog.ToString(), includeSkippedBranch: false);
			undoRenameContext.progressDialog.CloseAfterCompletion();
			TagHistoryRepository.ClearUndoState();
			RefreshItemsWithOptionalProgressDialog(list.ToArray(), showErrorMessageBox: false, showProgressDialog: true, refreshStatusAllInfo: true, previousMessage: value, listForMirror: true);
		}
		catch (System.Exception ex)
		{
			ReportAsyncOperationErrorIfNotCancellation(ex, undoRenameContext.cancellationSource, nameof(StartUndoRename));
		}
		finally
		{
			undoRenameContext.progressDialog.CloseAfterCompletion();
		}
	}

	// 从 AddLoadedFilesToListView / UpdateListViewItemValues 提取:lyrics/comment 列值超 20 字符时截断前 20 + Trim,
	// 其余列原样返回。columnName 为 ColumnHeaderInfo.Name(get-only auto-property,纯);value 调用点已 null 归一。
	internal static string TruncateLyricsOrCommentDisplayValue(string columnName, string value)
	{
		if ((columnName == "lyrics" || columnName == "comment") && value.Length > 20)
		{
			return value.Substring(0, 20).Trim();
		}
		return value;
	}

	// 从 ListViewItemNaturalComparer.Compare 提取:列排序比较纯核。按列名判定自然序(trackstr/discstr)抑或
	// 字典序,再按 SortOrder 方向分派(降序交换左右实参、升序原序、None/其它 -> 0)。Compare 保留 FileRow /
	// 列索引 / configuredColumnHeaders 解引用后调此(纯核仅依赖入参 + TextUtilities.CompareNaturalText / string.Compare)。
	internal static int CompareColumnText(string leftText, string rightText, string columnName, SortOrder sortOrder)
	{
		bool useNaturalSort = columnName == "trackstr" || columnName == "discstr";
		return sortOrder switch
		{
			SortOrder.Descending => useNaturalSort ? TextUtilities.CompareNaturalText(rightText, leftText) : string.Compare(rightText, leftText),
			SortOrder.Ascending => useNaturalSort ? TextUtilities.CompareNaturalText(leftText, rightText) : string.Compare(leftText, rightText),
			_ => 0,
		};
	}

	// 从 DeleteFilesTaskContext.ShowCompletionResult 提取:三路(单项成功/单项全败/多项)构造 (消息, 是否错误);
	// errorLog 传 Page 保 ToString 求值次数(单成功 0 / 单败 1 / 多项 1);UI 派发(isError?ShowError:ShowInfo)+ PerformClick 留原处。
	internal static (string, bool) BuildDeleteFilesResultMessage(int itemCount, int deletedCount, int failedCount, int processedCount, Page errorLog)
	{
		(string, bool) value = default((string, bool));
		if (itemCount <= 1)
		{
			if (deletedCount > 0)
			{
				value.Item1 = Resources.Msg_DeleteFilesCompleted;
			}
			else
			{
				value.Item1 = errorLog.ToString();
				value.Item2 = true;
			}
		}
		else
		{
			value.Item1 = string.Format(Resources.Msg_DeleteFilesCompleted + "\n" + Resources.Msg_OK_Fail_Count, deletedCount, failedCount, processedCount) + "\n" + errorLog.ToString();
		}
		return value;
	}

	// 从 ExtractCoversTaskContext.ShowCompletionResult 提取(with-skip 变体):三路构造 (消息, 是否错误);errorLog 传 Page。
	internal static (string, bool) BuildExtractCoversResultMessage(int itemCount, int extractedCount, int failedCount, int skippedCount, int processedCount, Page errorLog)
	{
		(string, bool) value = default((string, bool));
		if (itemCount <= 1)
		{
			if (extractedCount > 0)
			{
				value.Item1 = Resources.Msg_ExtractCoversComplete;
			}
			else
			{
				value.Item1 = errorLog.ToString();
				value.Item2 = true;
			}
		}
		else
		{
			value.Item1 = string.Format(Resources.Msg_ExtractCoversComplete + "\n" + Resources.Msg_OK_Fail_Skip_Count, extractedCount, failedCount, skippedCount, processedCount) + "\n" + errorLog.ToString();
		}
		return value;
	}

	// 从 StartClearTags 完成段提取:按待处理条目数 / 成功数分三路构造 (消息, 是否错误) 二元组。
	// 纯逻辑(仅字段读 + string.Format + Page.ToString);errorLog 传 Page 以保 ToString 调用位置/次数逐字节不变。
	internal static (string, bool) BuildClearTagsResultMessage(int itemCount, int successCount, int failedCount, int processedCount, Page errorLog)
	{
		(string, bool) value = default((string, bool));
		if (itemCount > 1)
		{
			value.Item1 = string.Format(Resources.Msg_CleartagsCompleted + "\n" + Resources.Msg_OK_Fail_Count, successCount, failedCount, processedCount) + "\n" + errorLog.ToString();
		}
		else if (successCount > 0)
		{
			value.Item1 = Resources.Msg_CleartagsCompleted;
		}
		else
		{
			value.Item1 = errorLog.ToString();
			value.Item2 = true;
		}
		return value;
	}

	private async void StartClearTags(SelectedListViewItemInfo[] itemInfos, ProgressDialog progressDialog, bool canCancelFileReadonly)
	{
		ClearTagsTaskContext clearTagsContext = new ClearTagsTaskContext();
		clearTagsContext.progressDialog = progressDialog;
		clearTagsContext.itemsToClear = itemInfos;
		clearTagsContext.canCancelFileReadonly = canCancelFileReadonly;
		clearTagsContext.cancellationSource = new CancellationTokenSource();
		clearTagsContext.progressDialog.CancelRequested += clearTagsContext.Cancel;
		clearTagsContext.successCount = 0;
		clearTagsContext.failedCount = 0;
		clearTagsContext.processedCount = 0;
		clearTagsContext.currentFile = null;
		clearTagsContext.progressDialog.ProgressUpdate += clearTagsContext.UpdateProgress;
		clearTagsContext.errorLog = new Page();
		try
		{
			await Task.Run((Action)clearTagsContext.ClearTags, clearTagsContext.cancellationSource.Token);
			(string, bool) value = BuildClearTagsResultMessage(clearTagsContext.itemsToClear.Length, clearTagsContext.successCount, clearTagsContext.failedCount, clearTagsContext.processedCount, clearTagsContext.errorLog);
			clearTagsContext.progressDialog.CloseAfterCompletion();
			GC.Collect();
			RefreshSelectedItems(showErrorMessageBox: false, showProgressDialog: true, refreshStatusAllInfo: true, previousMessage: value);
		}
		catch (System.Exception ex)
		{
			ReportAsyncOperationErrorIfNotCancellation(ex, clearTagsContext.cancellationSource, nameof(StartClearTags));
		}
		finally
		{
			clearTagsContext.progressDialog.CloseAfterCompletion();
		}
	}

	private void ConfirmAndSaveTagsWithOperation(string confirmLabelKey, Dictionary<string, object> comp)
	{
		int count = SelectedFileCount;
		if (count != 0 && DialogService.ConfirmYesNo(string.Format(Resources.Msg_ConfirmSaveTags, count) + "(" + localizedResources.GetString(confirmLabelKey) + ")\n" + BuildSelectedFilePreview()))
		{
			ProgressDialog progressDialog = new ProgressDialog(taskbarProgress);
			SelectedListViewItemInfo[] selectedItems = CollectSelectedListViewItemInfos();
			bool? canCancelReadOnly = ConfirmReadOnlyFileHandling(selectedItems);
			bool shouldCancelReadOnly = canCancelReadOnly == true;
			if (canCancelReadOnly.HasValue)
			{
				StartCommonSaveTags(selectedItems, progressDialog, comp, shouldCancelReadOnly);
				progressDialog.ShowDialogIfNotDisposed();
			}
		}
	}

	private void StartBatchLyricsOperation(string lyricsOperationResourceKey)
	{
		ConfirmAndSaveTagsWithOperation(lyricsOperationResourceKey, new Dictionary<string, object> { { "lyrics_handle", lyricsOperationResourceKey } });
	}

	private async void StartRemoveFiles(SelectedListViewItemInfo[] itemInfos, ProgressDialog progressDialog)
	{
		DeleteFilesTaskContext deleteFilesContext = new DeleteFilesTaskContext();
		deleteFilesContext.progressDialog = progressDialog;
		deleteFilesContext.itemsToDelete = itemInfos;
		deleteFilesContext.owner = this;
		deleteFilesContext.cancellationSource = new CancellationTokenSource();
		deleteFilesContext.progressDialog.CancelRequested += deleteFilesContext.Cancel;
		deleteFilesContext.processedCount = 0;
		deleteFilesContext.currentFile = null;
		deleteFilesContext.progressDialog.ProgressUpdate += deleteFilesContext.UpdateProgress;
		deleteFilesContext.deletedCount = 0;
		deleteFilesContext.failedCount = 0;
		deleteFilesContext.errorLog = new Page();
		try
		{
			await Task.Run((Action)deleteFilesContext.DeleteFiles, deleteFilesContext.cancellationSource.Token);
			BeginInvoke(new Action(deleteFilesContext.ShowCompletionResult));
		}
		catch (System.Exception ex)
		{
			ReportAsyncOperationErrorIfNotCancellation(ex, deleteFilesContext.cancellationSource, nameof(StartRemoveFiles));
		}
		finally
		{
			deleteFilesContext.progressDialog.CloseAfterCompletion();
		}
	}

	private async void StartSaveLrcFiles(SelectedListViewItemInfo[] itemInfos, ProgressDialog progressDialog)
	{
		SaveLrcFilesTaskContext saveLrcContext = new SaveLrcFilesTaskContext();
		saveLrcContext.progressDialog = progressDialog;
		saveLrcContext.itemsToSave = itemInfos;
		saveLrcContext.cancellationSource = new CancellationTokenSource();
		saveLrcContext.progressDialog.CancelRequested += saveLrcContext.Cancel;
		saveLrcContext.processedCount = 0;
		saveLrcContext.currentFile = null;
		saveLrcContext.progressDialog.ProgressUpdate += saveLrcContext.UpdateProgress;
		saveLrcContext.savedCount = 0;
		saveLrcContext.failedCount = 0;
		saveLrcContext.skippedCount = 0;
		saveLrcContext.errorLog = new Page();
		try
		{
			await Task.Run((Action)saveLrcContext.SaveLrcFiles, saveLrcContext.cancellationSource.Token);
			BeginInvoke(new Action(saveLrcContext.ShowCompletionResult));
		}
		catch (System.Exception ex)
		{
			ReportAsyncOperationErrorIfNotCancellation(ex, saveLrcContext.cancellationSource, nameof(StartSaveLrcFiles));
		}
		finally
		{
			saveLrcContext.progressDialog.CloseAfterCompletion();
		}
	}

	private async void StartExtractCovers(SelectedListViewItemInfo[] itemInfos, ProgressDialog progressDialog)
	{
		ExtractCoversTaskContext extractCoversContext = new ExtractCoversTaskContext();
		extractCoversContext.progressDialog = progressDialog;
		extractCoversContext.itemsToExtract = itemInfos;
		extractCoversContext.cancellationSource = new CancellationTokenSource();
		extractCoversContext.progressDialog.CancelRequested += extractCoversContext.Cancel;
		extractCoversContext.processedCount = 0;
		extractCoversContext.currentFile = null;
		extractCoversContext.progressDialog.ProgressUpdate += extractCoversContext.UpdateProgress;
		extractCoversContext.extractedCount = 0;
		extractCoversContext.failedCount = 0;
		extractCoversContext.skippedCount = 0;
		extractCoversContext.errorLog = new Page();
		try
		{
			await Task.Run((Action)extractCoversContext.ExtractCovers, extractCoversContext.cancellationSource.Token);
			BeginInvoke(new Action(extractCoversContext.ShowCompletionResult));
		}
		catch (System.Exception ex)
		{
			ReportAsyncOperationErrorIfNotCancellation(ex, extractCoversContext.cancellationSource, nameof(StartExtractCovers));
		}
		finally
		{
			extractCoversContext.progressDialog.CloseAfterCompletion();
		}
	}

	private void EditSingleFieldEncoding_Click(object sender, EventArgs e)
	{
		if (selectedTagState == null || !selectedTagState.IsLoadedSuccessfully())
		{
			return;
		}

		string fieldName = (string)(sender as Button).Tag;
		using CharacterSetSelectionDialog characterSetDialog = new CharacterSetSelectionDialog();
		characterSetDialog.SetTagState(selectedTagState);
		characterSetDialog.SetFieldNames(new string[1] { fieldName });
		characterSetDialog.SetCurrentValues(new string[1] { tagComboBoxes[fieldName].Text });
		if (characterSetDialog.ShowDialog() == DialogResult.OK)
		{
			tagComboBoxes[fieldName].Text = selectedTagState.DecodeFieldWithEncoding(fieldName, TagTextEncoding.GetEncodingNames()[characterSetDialog.SelectedEncodingIndex()], tagComboBoxes[fieldName].Text);
		}
	}

	private void EditAllTagFieldEncodings_Click(object sender, EventArgs e)
	{
		if (selectedTagState == null || !selectedTagState.IsLoadedSuccessfully())
		{
			return;
		}
		string[] editableFieldNames = new string[10] { "title", "artist", "album", "year", "genre", "albumartist", "composer", "lyricist", "comment", "lyrics" };
		using CharacterSetSelectionDialog characterSetDialog = new CharacterSetSelectionDialog();
		characterSetDialog.SetTagState(selectedTagState);
		characterSetDialog.SetFieldNames(editableFieldNames);
		characterSetDialog.SetCurrentValues(editableFieldNames.Select(GetCurrentTagFieldText).ToArray());
		if (characterSetDialog.ShowDialog() == DialogResult.OK)
		{
			foreach (string fieldName in editableFieldNames)
			{
				tagComboBoxes[fieldName].Text = selectedTagState.DecodeFieldWithEncoding(fieldName, TagTextEncoding.GetEncodingNames()[characterSetDialog.SelectedEncodingIndex()], tagComboBoxes[fieldName].Text);
			}
		}
	}

	private void ConvertAllTagFields(ChineseTextConverter converter)
	{
		foreach (ComboBox comboBox in tagComboBoxes.Values)
		{
			comboBox.Text = converter.ConvertText(comboBox.Text);
		}
	}

	private void ConvertTagsTraditionalToSimplified_Click(object sender, EventArgs e)
	{
		ConvertAllTagFields(ChineseTextConverter.TraditionalToSimplified());
	}

	private void ConvertTagsSimplifiedToTraditional_Click(object sender, EventArgs e)
	{
		ConvertAllTagFields(ChineseTextConverter.SimplifiedToTraditional());
	}

	private void RestoreTagsFromHistory_Click(object sender, EventArgs e)
	{
		if (selectedTagState == null)
		{
			return;
		}

		TagHistorySelectionDialog tagHistoryDialog = new TagHistorySelectionDialog();
		tagHistoryDialog.SetHistoryFilePath(selectedTagState.GetFilePath());
		if (tagHistoryDialog.ShowDialog() != DialogResult.OK)
		{
			return;
		}

		RestoreHistoryTagsContext restoreContext = new RestoreHistoryTagsContext
		{
			owner = this,
			selectedTagState = tagHistoryDialog.GetSelectedTagState()
		};
		TagHistoryRepository.HistoryTagFields.ForEachItem(restoreContext.ApplyTagField);
	}

	private void CoverSourceMenuItem_Click(object sender, EventArgs e)
	{
		SearchCoverFromSource((sender as ToolStripItem)?.Tag as SearchSource?);
	}

	private void LyricSourceMenuItem_Click(object sender, EventArgs e)
	{
		SearchLyricsFromSource((sender as ToolStripItem)?.Tag as SearchSource?);
	}

	private void TagSourceMenuItem_Click(object sender, EventArgs e)
	{
		SearchTagsFromSource((sender as ToolStripItem)?.Tag as SearchSource?);
	}

	private void SaveLyrics_Click(object sender, EventArgs e)
	{
		if (SelectedFileCount == 1)
		{
			if (selectedTagState == null || !selectedTagState.IsLoadedSuccessfully() || !File.Exists(selectedTagState.GetFilePath()) || string.IsNullOrWhiteSpace(lyricsComboBox.Text))
			{
				DialogService.ShowErrorMessage(Resources.Msg_LyricNotFound);
				return;
			}
			try
			{
				LyricSaveFileDialog lyricSaveDialog = new LyricSaveFileDialog();
				FileInfo fileInfo = new FileInfo(selectedTagState.GetFilePath());
				lyricSaveDialog.InitialDirectory = PathFileUtilities.GetLyricSaveDirectory(fileInfo.FullName);
				lyricSaveDialog.FileName = PathFileUtilities.BuildLyricFileName(fileInfo.FullName, selectedTagState);
				string defaultLrcPath = lyricSaveDialog.InitialDirectory + "\\" + lyricSaveDialog.FileName;
				if (File.Exists(defaultLrcPath))
				{
					if (lyricSaveDialog.ShowDialog() == DialogResult.OK)
					{
						File.WriteAllText(lyricSaveDialog.FileName, lyricsComboBox.Text, Encoding.GetEncoding(lyricSaveDialog.SelectedEncoding));
					}
					return;
				}
				File.WriteAllText(defaultLrcPath, lyricsComboBox.Text, Encoding.GetEncoding(Settings.Default.SaveLrcFileDefaultEncoding));
				DialogService.ShowInformationMessage(string.Format(Resources.Msg_FilesSavedInSpecPath, defaultLrcPath));
			}
			catch (System.Exception ex)
			{
				DialogService.ShowErrorMessage(ex.Message);
			}
			return;
		}
		if (SelectedFileCount > 1 && DialogService.ConfirmYesNo(string.Format(Resources.Msg_ConfirmSaveLrcFiles, SelectedFileCount) + "\n" + BuildSelectedFilePreview()))
		{
			SelectedListViewItemInfo[] selectedItems = CollectSelectedListViewItemInfos();
			ProgressDialog progressDialog = new ProgressDialog(taskbarProgress);
			StartSaveLrcFiles(selectedItems, progressDialog);
			progressDialog.ShowDialogIfNotDisposed();
		}
	}

	private void ExtractCovers_Click(object sender, EventArgs e)
	{
		if (SelectedFileCount <= 1)
		{
			if (SelectedFileCount == 1)
			{
				SaveCurrentCover();
			}
		}
		else if (DialogService.ConfirmYesNo(string.Format(Resources.Msg_ConfirmExtractCovers, SelectedFileCount) + "\n" + BuildSelectedFilePreview()))
		{
			SelectedListViewItemInfo[] selectedItems = CollectSelectedListViewItemInfos();
			ProgressDialog progressDialog = new ProgressDialog(taskbarProgress);
			StartExtractCovers(selectedItems, progressDialog);
			progressDialog.ShowDialogIfNotDisposed();
		}
	}

	private void SaveCurrentCover()
	{
		if (selectedTagState == null || !selectedTagState.IsLoadedSuccessfully())
		{
			DialogService.ShowErrorMessage(Resources.Msg_CoverNotFound);
			return;
		}
		List<ConfigDescriptorState.PictureData> coverList = selectedTagState["allpicturedata"] as List<ConfigDescriptorState.PictureData>;
		if (coverList == null || currentCoverIndex >= coverList.Count)
		{
			DialogService.ShowErrorMessage(Resources.Msg_CoverNotFound);
			return;
		}
		try
		{
			ConfigDescriptorState.PictureData selectedCover = coverList[currentCoverIndex];
			if (selectedCover.MimeType == null || selectedCover.Width <= 0 || selectedCover.Height <= 0)
			{
				DialogService.ShowErrorMessage(Resources.Msg_CoverNotFound);
				return;
			}
			string directoryName = Path.GetDirectoryName(selectedTagState.GetFilePath());
			string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(selectedTagState.GetFilePath());
			string defaultCoverPath = directoryName + "\\" + fileNameWithoutExtension + ImageUtilities.GetImageExtensionForMimeType(selectedCover.MimeType, ".jpg");

			string filter = ImageUtilities.GetImageFileDialogFilterForMimeType(selectedCover.MimeType);
			if (!string.IsNullOrWhiteSpace(filter))
			{
				extractCoverSaveFileDialog.Filter = filter;
			}
			extractCoverSaveFileDialog.InitialDirectory = directoryName;
			extractCoverSaveFileDialog.FileName = fileNameWithoutExtension;
			if (extractCoverSaveFileDialog.ShowDialog() == DialogResult.OK)
			{
				File.WriteAllBytes(extractCoverSaveFileDialog.FileName, selectedCover.ImageBytes);
			}
		}
		catch (System.Exception ex)
		{
			DialogService.ShowErrorMessage($"{Resources.Msg_ExtractCoverFail}, {Resources.Msg_ErrorMessage}: {ex.Message}");
		}
	}

	private void BatchAutoMatchTags_Click(object sender, EventArgs e)
	{
		AutoMatchTagsDialog autoMatchDialog = new AutoMatchTagsDialog();
		if (autoMatchDialog.ShowDialog() != DialogResult.OK)
		{
			return;
		}
		SelectedListViewItemInfo[] selectedItems = CollectSelectedListViewItemInfos();
		bool canCancelReadOnly = false;
		if (!autoMatchDialog.IsOnlyWriteFileModeSelected())
		{
			bool? readOnlyChoice = ConfirmReadOnlyFileHandling(selectedItems);
			if (!readOnlyChoice.HasValue)
			{
				return;
			}
			canCancelReadOnly = readOnlyChoice == true;
		}
		ProgressDialog progressDialog = new ProgressDialog(taskbarProgress);
		autoMatchDialog.StartAutoMatchTags(selectedItems.Select(GetSelectedItemFilePath).ToArray(), progressDialog, canCancelReadOnly, RefreshSelectedItemsAfterBatchMessage);
		progressDialog.ShowDialogIfNotDisposed();
	}

	private void BatchFilenameOrTagsFromPattern_Click(object sender, EventArgs e)
	{
		FilenameRelatedBatchDialog filenameRelatedBatchDialog = new FilenameRelatedBatchDialog();
		if (filenameRelatedBatchDialog.ShowDialog() != DialogResult.OK)
		{
			return;
		}
		SelectedListViewItemInfo[] selectedItems = CollectSelectedListViewItemInfos();
		bool? canCancelReadOnly = ConfirmReadOnlyFileHandling(selectedItems);
		if (!canCancelReadOnly.HasValue)
		{
			return;
		}
		ProgressDialog progressDialog = new ProgressDialog(taskbarProgress);
		if (ShouldChangeTagsFromFilenameDialog(filenameRelatedBatchDialog))
		{
			filenameRelatedBatchDialog.StartChangeTags(selectedItems.Select(GetSelectedItemFilePath).ToArray(), progressDialog, canCancelReadOnly == true, RefreshSelectedItemsAfterBatchMessage);
		}
		else
		{
			RenameFilesCompletionContext renameCompletion = new RenameFilesCompletionContext
			{
				owner = this,
				renameItems = selectedItems.Select(CreateRenameItemInfo).ToArray()
			};
			filenameRelatedBatchDialog.StartRenameFiles(renameCompletion.renameItems, progressDialog, FileSettings, renameCompletion.RefreshAfterRename);
		}
		progressDialog.ShowDialogIfNotDisposed();
	}

	private void ConvertSelectedTagsTraditionalToSimplified_Click(object sender, EventArgs e)
	{
		ConfirmAndSaveTagsWithOperation("menuStrip1.Batch.TagsChtToChs", new Dictionary<string, object> { { "chscht_handle", false } });
	}

	private void ConvertSelectedTagsSimplifiedToTraditional_Click(object sender, EventArgs e)
	{
		ConfirmAndSaveTagsWithOperation("menuStrip1.Batch.TagsChsToCht", new Dictionary<string, object> { { "chscht_handle", true } });
	}

	private void ConfirmAndConvertSelectedFilenames(string confirmLabelKey, bool isChsToCht)
	{
		int count = SelectedFileCount;
		if (count != 0 && DialogService.ConfirmYesNo(string.Format(Resources.Msg_ConfirmRenameFiles, count) + "(" + localizedResources.GetString(confirmLabelKey) + ")\n" + BuildSelectedFilePreview()))
		{
			ProgressDialog progressDialog = new ProgressDialog(taskbarProgress);
			(string Path, string NewPath, int ListViewIndex)[] itemInfos = CollectSelectedListViewItemInfos().Select(CreateRenameItemInfo).ToArray();
			StartRenameFiles(itemInfos, progressDialog, isChsToCht);
			progressDialog.ShowDialogIfNotDisposed();
		}
	}

	private void ConvertSelectedFilenamesTraditionalToSimplified_Click(object sender, EventArgs e)
	{
		ConfirmAndConvertSelectedFilenames("menuStrip1.Batch.FilenameChtToChs", isChsToCht: false);
	}

	private void ConvertSelectedFilenamesSimplifiedToTraditional_Click(object sender, EventArgs e)
	{
		ConfirmAndConvertSelectedFilenames("menuStrip1.Batch.FilenameChsToCht", isChsToCht: true);
	}

	private void OpenOptions_Click(object sender, EventArgs e)
	{
		OptionsDialog optionsDialog = new OptionsDialog();
		if (optionsDialog.ShowDialog() != DialogResult.OK)
		{
			return;
		}
		if (optionsDialog.FileFilterSettingsChanged)
		{
			refreshMenuItem.PerformClick();
		}
		if (optionsDialog.NotifyAreaSettingsChanged)
		{
			base.Visible = base.WindowState != FormWindowState.Minimized || !Settings.Default.MinimizeToNotiArea;
			notifyIcon.Visible = Settings.Default.AlwaysShowIconInNofiArea || (Settings.Default.MinimizeToNotiArea && base.WindowState == FormWindowState.Minimized);
		}
	}

	private void CheckForUpdates_Click(object sender, EventArgs e)
	{
		checkForUpdatesMenuItem.Enabled = false;
		ApplicationInfoService.CheckForUpdates(isStartup: false, EnableCheckForUpdatesMenuItem);
	}

	private void OpenMusicTagWebsite_Click(object sender, EventArgs e)
	{
		Process.Start(ApplicationInfoService.UpdatePageUrl);
	}

	private void ShowAboutDialog_Click(object sender, EventArgs e)
	{
		new AboutDialog().ShowDialog();
	}

	private void SaveOverwriteCoverSetting_Click(object sender, EventArgs e)
	{
		Settings.Default.OverwritePictureboxPicture = overwriteCoverCheckBox.Checked;
		DialogService.TrySaveApplicationSettings();
	}

	// 从 OnLoad 提取:窗口位置 clamp 到工作区纯几何(顺序依赖——先右/下越界回拉,再整体出界归零)。
	// 无 LocationChanged/Move handler 订阅或 override(已核实),故 BEFORE 的 0~3 次中间 base.Location 写与 AFTER
	// 单次写(局部 Point 模拟顺序读写)用户态等价:WinForms Control.Location setter 对相同值 no-op(SetBounds 内部
	// 值比较,无观察者观测中间态)。maximumVisibleLocation:宽留 20px 余量(至少 20)、高取满工作区。
	internal static Point ClampWindowToWorkingArea(Point location, Size size, Rectangle workingArea)
	{
		Size maximumVisibleLocation = new Size(Math.Min(Math.Max(workingArea.Width - 20, 20), workingArea.Width), workingArea.Height);
		if (location.X > maximumVisibleLocation.Width)
		{
			location = new Point(maximumVisibleLocation.Width, location.Y);
		}
		if (location.Y > maximumVisibleLocation.Height)
		{
			location = new Point(location.X, maximumVisibleLocation.Height);
		}
		if (location.X + size.Width < 20 || location.Y + size.Height < 20)
		{
			location = new Point(0, 0);
		}
		return location;
	}

	protected override void OnLoad(EventArgs e)
	{
		base.OnLoad(e);
		try
		{
			MainFormPosSizeInfo = JsonConvert.DeserializeObject<FormPosSizeInfo>(Settings.Default.MainFormPosSizeInfo) ?? new FormPosSizeInfo();
			if (MainFormPosSizeInfo.Maximized)
			{
				base.WindowState = FormWindowState.Maximized;
			}
			else
			{
				// net8:恢复位置直接在 OnLoad 执行。net481 时代"恢复到 DPI 不同的屏须推迟到
				// OnShown"的变通(不可见窗口不执行 DPI 缩放)已删——net8 框架在首显时正确
				// 处理跨屏刻度,推迟恢复反而制造"副屏 100% 首显保持主屏 150% 刻度"(实测)。
				ApplyRestoredPosSize();
			}
		}
		catch (System.Exception ex)
		{
			Console.WriteLine("mainformpossizeinfo fail " + ex.Message);
		}

		try
		{
			SortSetting = JsonConvert.DeserializeObject<ListViewItemNaturalComparer.ListViewSortSetting>(Settings.Default.SortSetting) ?? new ListViewItemNaturalComparer.ListViewSortSetting();
			if (!SortSetting.Column.HasValue)
			{
				SortSetting.Column = 0;
			}
			if (SortSetting.SortOrder == SortOrder.None)
			{
				SortSetting.SortOrder = SortOrder.Ascending;
			}
			UpdateFileListSortGlyph(SortSetting.Column, SortSetting.SortOrder);
			ApplyFileListSort();
			if (FileSettings == null)
			{
				FileSettings = new ListViewFileSetting();
				FileSettings.ResetListSet();
			}
		}
		catch (System.Exception ex)
		{
			Console.WriteLine("sortsetting fail " + ex.Message);
		}
		DpiTrace("OnLoad.done");
	}

	// OnLoad 恢复:设保存的位置/尺寸并钳制到工作区。位置先行 —— 跨 DPI 屏移动触发框架整树
	// 缩放并按比例调整窗口尺寸,随后再钉保存的尺寸(保存值本就是上次关闭所在屏的刻度)。
	private void ApplyRestoredPosSize()
	{
		if (MainFormPosSizeInfo.Location.HasValue)
		{
			base.Location = MainFormPosSizeInfo.Location.Value;
		}
		if (MainFormPosSizeInfo.Size.HasValue)
		{
			base.Size = MainFormPosSizeInfo.Size.Value;
		}
		Rectangle workingArea = SystemInformation.WorkingArea;
		base.Size = new Size(Math.Min(base.Size.Width, workingArea.Width), Math.Min(base.Size.Height, workingArea.Height));
		base.Location = ClampWindowToWorkingArea(base.Location, base.Size, workingArea);
	}

	// 自定义资产的当前刻度台账:同一 DPI 的重复 WM_DPICHANGED 幂等跳过(避免无谓重建位图);
	// 也是 DGV 列宽按 previous→target 比率补缩的"上一刻度"来源。构造期初始化为启动屏 DPI。
	private int customAssetsDpi;

	// 启动屏刻度:列宽持久化的历史语义是"启动屏像素"(net481/SystemAware 时代窗口恒为主屏
	// 刻度)。跨屏台账缩放后列宽变为"当前屏刻度",保存时须换算回启动刻度,下次启动才不缩水。
	private int startupDpi;

	// 诊断插桩(实验分支):MUSICTAG_DPI_TRACE=1 时把 DPI 相关几何/字体度量追加写 exe 旁
	// dpi-trace.log,配合外部移屏驱动脚本定位跨屏缩放问题。未开启时零行为影响。
	private static readonly bool dpiTraceEnabled = Environment.GetEnvironmentVariable("MUSICTAG_DPI_TRACE") == "1";

	private void DpiTrace(string eventName)
	{
		if (!dpiTraceEnabled)
		{
			return;
		}
		try
		{
			int columnWidthSum = 0;
			foreach (DataGridViewColumn column in fileListView.Columns)
			{
				columnWidthSum += column.Width;
			}
			string line = string.Format(
				"[{0:HH:mm:ss.fff}] {1,-26} dpi={2} bounds={3} client={4} font={5};{6:0.##} split={7} p1min={8} colSum={9} col0={10} tagPanelW={11} titleComboW={12} titleBtnRight={13} filterFont={14:0.##} filterH={15} summaryFont={16:0.##} menuFont={17:0.##} imgScale={18}",
				DateTime.Now, eventName, DeviceDpi, Bounds, ClientSize,
				Font.Name, Font.Size,
				mainSplitContainer.SplitterDistance, mainSplitContainer.Panel1MinSize,
				columnWidthSum,
				fileListView.Columns.Count > 0 ? fileListView.Columns[0].Width : -1,
				tagEditorPanel.ClientSize.Width,
				titleComboBox != null ? titleComboBox.Width : -1,
				titleEncodingButton != null ? titleEncodingButton.Right : -1,
				fileFilterStatusStrip.Font.Size, fileFilterStatusStrip.Height,
				fileSummaryStatusStrip.Font.Size,
				mainMenuStrip.Font.Size,
				mainToolStrip.ImageScalingSize)
				+ string.Format(" ftbH={0} fddH={1} flabH={2} prefH={3} autoSize={4}",
				filterTextBox.Height, filterTypeDropDownButton.Height, filterStatusLabel.Height,
				fileFilterStatusStrip.GetPreferredSize(Size.Empty).Height, fileFilterStatusStrip.AutoSize);
			File.AppendAllText(Path.Combine(PathFileUtilities.GetApplicationDirectory(), "dpi-trace.log"), line + Environment.NewLine);
		}
		catch (System.Exception)
		{
		}
	}

	protected override void OnDpiChanged(DpiChangedEventArgs e)
	{
		DpiTrace("OnDpiChanged.before " + e.DeviceDpiOld + "->" + e.DeviceDpiNew);
		base.OnDpiChanged(e);
		DpiTrace("OnDpiChanged.afterBase");
		RescaleCustomAssetsForDpi(e.DeviceDpiNew);
		DpiTrace("OnDpiChanged.afterCustom");
	}

	// net8(实测标定,见 dpi-trace 插桩):框架接管 WM_DPICHANGED 的整树 bounds 缩放(窗口、
	// 子控件 bounds、SplitterDistance、ToolStrip ImageScalingSize),但四类对象不被接管或被
	// 错误接管,在 OnDpiChanged(框架缩放完成之后触发)统一纠正:
	// ① 不接管——DGV 列宽(colSum 跨屏恒定)按 previous→target 比率补缩(列宽用户可拖,无
	//    设计基准值,只能相对缩;台账 customAssetsDpi 保证幂等);Panel1MinSize 绝对值重设。
	// ② 错误接管——显式字体的 pt 值被框架按 DPI 比率缩(实测 Tahoma 9→6):pt 本是 DPI 无关
	//    单位,恒 9pt 才物理尺寸恒定;且启动时框架并不缩 Font(9pt 原样),跨屏才缩,不对称。
	//    绝对值钉回设计 pt(后写覆盖,幂等)。
	// ③ 自绘资产——FontAwesome 位图按新刻度重生成;工具栏项/过滤条控件宽度绝对值重设。
	// ④ 时序错值——框架逐控件缩放有先后,SizeChanged 联动(TagComboBoxWidthUpdater/
	//    FilterBar_SizeChanged)在缩放中途按混合刻度算出错值并被框架二次缩放(实测 96→144 时
	//    titleComboBox 宽 598 超过面板宽,编码按钮被挤折行 = 用户截图 pic3);结尾在整树刻度
	//    一致后统一重算覆盖。
	private void RescaleCustomAssetsForDpi(int targetDpi)
	{
		if (targetDpi == customAssetsDpi)
		{
			return;
		}
		int previousDpi = customAssetsDpi;
		customAssetsDpi = targetDpi;

		Font = fileListFont;
		// ToolStrip 族(状态条/菜单条/工具栏)不吃 Form.Font 继承(ToolStripManager.DefaultFont
		// 体系)——各自钉 pt 并归位条带高度;保留各自字形(汇总条 Tahoma、其余系统菜单字体)。
		PinToolStripFontSize(fileSummaryStatusStrip);
		PinToolStripFontSize(fileFilterStatusStrip);
		PinToolStripFontSize(mainMenuStrip);
		PinToolStripFontSize(mainToolStrip);
		ApplyFileListVisualStyle();

		mainSplitContainer.Panel1MinSize = ImageUtilities.ScaleByDpi(320f, this);
		if (previousDpi > 0)
		{
			foreach (DataGridViewColumn column in fileListView.Columns)
			{
				// 下限贴 MinimumWidth(默认 5):更高的人为下限会把窄列(如 11px 的序号列)在
				// 缩小方向顶起,放大方向再 ×1.5,往返一圈列宽净膨胀。
				column.Width = Math.Max(column.MinimumWidth, (int)Math.Round((float)column.Width * targetDpi / previousDpi));
			}
		}
		fileListView.ColumnHeadersHeight = ImageUtilities.ScaleByDpi(24f, this);

		ApplyToolbarItemSizesForDpi();
		RefreshTagEditorButtonImages();
		filterTypeDropDownButton.Width = ImageUtilities.ScaleByDpi(100f, this);
		selectedFilesStatusLabel.Width = ImageUtilities.ScaleByDpi(190f, this);
		// 两条 StatusStrip 在中途字体放大(BEFOREPARENT ×ratio)时被撑高后自锁:拉伸布局把
		// items 顶到行高(item.Height 写入无效),GetPreferredSize 又按 items 现高计算,字体
		// 钉回后条带高度回不去(实测 31→42→63 渐增)。翻转 AutoSize 打破自锁,条带回到按
		// 9pt 内容重算的自然高(31px,比启动态的 Designer 项高 +padding 低 5px,两屏恒定)。
		ResetStatusStripHeight(fileFilterStatusStrip);
		ResetStatusStripHeight(fileSummaryStatusStrip);

		// no_cover 占位图按名字键缓存(尺寸实参命中时被忽略),作废后按新屏刻度重建;当前
		// 正显示占位图(CenterImage 原像素绘制)时立即换新实例,旧实例经 SetCoverPreviewImage
		// 的引用比对释放(缓存已不含它,无悬挂引用)。
		ImageUtilities.EvictCachedResourceBitmap("no_cover");
		if (coverPictureBox.SizeMode == PictureBoxSizeMode.CenterImage)
		{
			SetCoverPreviewImage(GetNoCoverPreviewImage());
		}

		ApplyTagPanelLayout();
		FilterBar_SizeChanged(fileFilterStatusStrip, EventArgs.Empty);
	}

	// 跨屏后把 ToolStrip 族条带的字体 pt 归回 9(设计基准):框架 WM_DPICHANGED 把字号按
	// DPI 比率缩(9→6@96),但 pt 是 DPI 无关单位,缩 pt = 物理尺寸漂移。保留原字形只改字号;
	// 已是 9pt 时无操作(幂等,不产生新 Font 对象)。
	private static void PinToolStripFontSize(ToolStrip strip)
	{
		Font currentFont = strip.Font;
		if (Math.Abs(currentFont.SizeInPoints - 9f) > 0.1f)
		{
			strip.Font = new Font(currentFont.FontFamily, 9f, currentFont.Style);
		}
	}

	// 见 RescaleCustomAssetsForDpi 内注释:打破 StatusStrip"items 被行高拉伸 ↔ preferred 按
	// items 现高计算"的自锁,字体归位后条带高度才能回到内容自然高。
	private static void ResetStatusStripHeight(StatusStrip strip)
	{
		strip.AutoSize = false;
		strip.Height = 0;
		strip.AutoSize = true;
	}

	protected override void OnShown(EventArgs e)
	{
		base.OnShown(e);
		DpiTrace("OnShown.begin");
		notifyIcon.Visible = Settings.Default.AlwaysShowIconInNofiArea;
		hasShownMainForm = true;
		if (tagEditorPanel.Height < tagEditorBottomSpacerPanel.Location.Y + tagEditorBottomSpacerPanel.Height)
		{
			ApplyTagPanelLayout();
		}

		checkForUpdatesMenuItem.Enabled = false;
		ApplicationInfoService.CheckForUpdates(isStartup: true, EnableCheckForUpdatesMenuItem);
		string databaseErrorMessage = TagHistoryRepository.InitializeDatabase();
		if (databaseErrorMessage != null)
		{
			skipSavingSettingsOnClose = true;
			DialogService.ShowErrorMessage(string.Format(Resources.Msg_InitDatabaseFail, databaseErrorMessage));
			Close();
			return;
		}

		if (File.Exists(AppSettingData.AppSettingDataPath))
		{
			ProgressDialog progressDialog = null;
			try
			{
				AppSettingData appSettingData = AppSettingData.Load(AppSettingData.AppSettingDataPath);
				FileSettings = appSettingData.ListViewFileSetting;
				FileSettings.ResetListSet();
				if (startupFileArgs.Any())
				{
					AddFilesFromPaths();
					return;
				}
				UpdateWindowTitle();
				progressDialog = new ProgressDialog(taskbarProgress);
				StartAddConfiguredFileList(progressDialog);
				progressDialog.ShowDialogIfNotDisposed();
				return;
			}
			catch (System.Exception ex)
			{
				// 配置无法读取时落盘日志便于诊断(WinForms 无控制台,Console 输出不可见)。
				LogService.WriteExceptionDetails(ex, "StateFieldInstance.LoadAppSettingData");
				if (ex is Newtonsoft.Json.JsonException)
				{
					// 格式不兼容(如旧版本二进制 .dat 用新版 JSON 解析失败)无法自行恢复:
					// 删除该文件自愈,避免每次启动都失败,下次退出会写出新的 JSON 格式。
					try
					{
						File.Delete(AppSettingData.AppSettingDataPath);
					}
					catch
					{
					}
				}
				progressDialog?.CloseAfterCompletion();
				return;
			}
		}

		if (startupFileArgs.Any())
		{
			AddFilesFromPaths();
		}
	}

	protected override void OnClosing(CancelEventArgs e)
	{
		base.OnClosing(e);
		CloseKnownMessageBoxes();
		if (skipSavingSettingsOnClose)
		{
			return;
		}

		SimpleProgressDialog progressDialog = new SimpleProgressDialog(taskbarProgress);
		progressDialog.SetMessage(Resources.Msg_SaveingData);
		progressDialog.SetCancelButtonHidden();
		StartSaveAppSettingData(progressDialog);
		progressDialog.ShowProgressDialog();
	}

	private async void StartSaveAppSettingData(SimpleProgressDialog progressDialog)
	{
		AppSettingsSaveTask appSettingsSaveTask = new AppSettingsSaveTask();
		SaveCurrentFileListColumnWidths();
		appSettingsSaveTask.appSettingsData = new AppSettingData
		{
			ListViewFileSetting = FileSettings
		};
		if (Visible)
		{
			MainFormPosSizeInfo.Location = Location;
			MainFormPosSizeInfo.Size = Size;
			MainFormPosSizeInfo.Maximized = WindowState == FormWindowState.Maximized;
		}
		appSettingsSaveTask.sortSettingJson = JsonConvert.SerializeObject(SortSetting);
		appSettingsSaveTask.mainFormPosSizeInfoJson = JsonConvert.SerializeObject(MainFormPosSizeInfo);
		appSettingsSaveTask.filterListViewType = filterTypeDropDownButton.Tag as string;
		appSettingsSaveTask.filterListViewKeyword = filterTextBox.Text;
		try
		{
			await Task.Run((Action)appSettingsSaveTask.SaveSettings);
		}
		catch (System.Exception ex)
		{
			LogService.WriteExceptionDetails(ex, "StateFieldInstance.StartSaveAppSettingData");
		}
		finally
		{
			progressDialog.CloseProgressDialog();
		}
	}

	private void UpdateWindowTitle()
	{
		string title = Resources.AppName;
		ListViewFileSettingFileInfo firstEnabledDirectory = FileSettings.List.Find(IsEnabledConfiguredDirectory);
		if (firstEnabledDirectory != null)
		{
			title = title + " - " + firstEnabledDirectory.DirPath;
		}
		Text = title;
	}

	internal static bool IsEnabledConfiguredDirectory(ListViewFileSettingFileInfo directoryInfo)
	{
		return !directoryInfo.Disabled && !directoryInfo.IsAnyFile();
	}

	private void BeginRenameSelectedFile_Click(object sender, EventArgs e)
	{
		if (SelectedFileCount <= 0)
		{
			return;
		}
		FileRow row = SelectedFileRows.First();
		if (!visibleRowIndex.TryGetValue(row, out int visIndex) || !fileListView.Columns[0].Visible)
		{
			return;
		}
		renamingRow = row;
		renameEditedValue = null;
		fileListView.Columns[0].ReadOnly = false;
		fileListView.CurrentCell = fileListView.Rows[visIndex].Cells[0];
		fileListView.BeginEdit(selectAll: true);
	}

	private void RevealSelectedFileInExplorer_Click(object sender, EventArgs e)
	{
		if (SelectedFileCount > 0)
		{
			DialogService.ShowInExplorer(SelectedFileRows.First().FilePath);
		}
	}

	private void FileList_CellBeginEdit(object sender, DataGridViewCellCancelEventArgs e)
	{
		// 仅允许程序化发起的重命名(EditMode=EditProgrammatically + 仅首列临时可写),其余一律拦下。
		if (renamingRow == null)
		{
			e.Cancel = true;
		}
	}

	private void FileList_EditingControlShowing(object sender, DataGridViewEditingControlShowingEventArgs e)
	{
		if (renamingRow != null && e.Control is TextBox textBox)
		{
			int selectionLength = Path.GetFileNameWithoutExtension(textBox.Text).Length;
			BeginInvoke((Action)delegate
			{
				try
				{
					textBox.Select(0, selectionLength);
				}
				catch
				{
				}
			});
		}
	}

	private void FileList_CellValuePushed(object sender, DataGridViewCellValueEventArgs e)
	{
		// VirtualMode 提交编辑时回灌新值 —— 这里只暂存,真正的改名/落盘在 CellEndEdit 做。
		if (renamingRow != null && e.ColumnIndex == 0)
		{
			renameEditedValue = e.Value as string;
		}
	}

	private void FileList_CellValidating(object sender, DataGridViewCellValidatingEventArgs e)
	{
	}

	private void FileList_CellEndEdit(object sender, DataGridViewCellEventArgs e)
	{
		if (renamingRow == null)
		{
			return;
		}
		FileRow row = renamingRow;
		string requestedFileName = renameEditedValue;
		renamingRow = null;
		renameEditedValue = null;
		fileListView.Columns[0].ReadOnly = true;
		if (requestedFileName == null)
		{
			return;
		}
		PerformInPlaceRename(row, requestedFileName);
	}

	// 从 PerformInPlaceRename 提取:就地改名的非法文件名谓词(含路径分量 -> GetFileName 不等自身,或含非法字符)。
	// net8 基线:Path.GetFileName 不再对 '<' '>' 等 InvalidPathChars 抛(netfx 抛 ArgumentException,由外层
	// catch 弹错拒绝);此类输入现落 IndexOfAny(InvalidFileNameChars) 支路返回 true,同为弹错拒绝(Msg_InvalidFile)。
	internal static bool IsInvalidRenameFileName(string requestedFileName)
	{
		return !string.Equals(Path.GetFileName(requestedFileName), requestedFileName, StringComparison.Ordinal)
			|| requestedFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0;
	}

	private void PerformInPlaceRename(FileRow row, string requestedFileName)
	{
		FileListLabelEditContext editContext = new FileListLabelEditContext
		{
			OriginalPath = row.FilePath,
			RequestedFileName = requestedFileName
		};
		FileInfo fileInfo = new FileInfo(editContext.OriginalPath);
		if (!fileInfo.Exists)
		{
			BeginInvoke(new Action(editContext.ShowFileNotFoundMessage));
			return;
		}

		try
		{
			if (string.IsNullOrWhiteSpace(editContext.RequestedFileName)
				|| string.IsNullOrWhiteSpace(Path.GetFileNameWithoutExtension(editContext.RequestedFileName))
				|| fileInfo.Name == editContext.RequestedFileName)
			{
				return;
			}

			if (IsInvalidRenameFileName(editContext.RequestedFileName))
			{
				editContext.RenameError = new ArgumentException(Resources.Msg_InvalidFile);
				BeginInvoke(new Action(editContext.ShowRenameError));
				return;
			}

			editContext.NewPath = Path.Combine(fileInfo.DirectoryName, editContext.RequestedFileName);
			PathFileUtilities.MoveFileAllowingCaseOnlyRename(editContext.OriginalPath, editContext.NewPath);
			row.FilePath = editContext.NewPath;
			InvalidateFileRow(row);
			FileSettings.UpdateForAnyFile(editContext.OriginalPath, editContext.NewPath);
			TagHistoryRepository.ClearUndoState();
			BeginInvoke(new Action(editContext.UpdateHistoryFilePath));
			TagHistoryRepository.AddRenameUndoRecord(editContext.OriginalPath, editContext.NewPath);
			if (renamedFilesRefreshTimer.Enabled)
			{
				renamedFilesRefreshTimer.Stop();
			}
			renamedFilesRefreshTimer.Start();
		}
		catch (System.Exception ex)
		{
			editContext.RenameError = ex;
			BeginInvoke(new Action(editContext.ShowRenameError));
		}
	}

	private void RefreshRenamedFilesTimer_Tick(object sender, EventArgs e)
	{
		renamedFilesRefreshTimer.Stop();
		RefreshSelectedItems(showErrorMessageBox: true, showProgressDialog: false, refreshStatusAllInfo: false);
	}

	protected override bool ProcessCmdKey(ref Message message, Keys keyData)
	{
		if (!fileListView.Focused)
		{
			switch (keyData)
			{
				case Keys.Delete:
				case Keys.A | Keys.Control:
				case Keys.U | Keys.Control:
				case Keys.A | Keys.Shift | Keys.Control:
					return false;
			}
		}
		return base.ProcessCmdKey(ref message, keyData);
	}

	private void RestartComboBoxSelectionResetTimer(object sender, SplitterEventArgs e)
	{
		if (!hasShownMainForm)
		{
			return;
		}
		if (comboBoxSelectionResetTimer.Enabled)
		{
			comboBoxSelectionResetTimer.Stop();
		}
		comboBoxSelectionResetTimer.Start();
	}

	private void ResetComboBoxSelectionTimer_Tick(object sender, EventArgs e)
	{
		comboBoxSelectionResetTimer.Stop();
		tagComboBoxes.Values.ForEachItem(ResetComboBoxSelectionStart);
	}

	private static void ResetComboBoxSelectionStart(ComboBox comboBox)
	{
		comboBox.Select(0, 0);
	}

	private void FileListPanel_SizeChanged(object sender, EventArgs e)
	{
		if (fileListView.Width < mainSplitContainer.Panel2.Width)
		{
			fileListView.Width = mainSplitContainer.Panel2.Width;
		}

		int fileListHeight = mainSplitContainer.Panel2.Height - fileSummaryStatusStrip.Height - fileFilterStatusStrip.Height;
		if (fileListView.Height >= fileListHeight)
		{
			return;
		}

		fileFilterStatusStrip.Location = new Point(0, fileListHeight);
		fileSummaryStatusStrip.Location = new Point(0, fileListHeight + fileFilterStatusStrip.Height);
		fileListView.Height = fileListHeight;
	}

	private void FilterBar_SizeChanged(object sender, EventArgs e)
	{
		filterTextBox.Width = fileFilterStatusStrip.Width - filterStatusLabel.Width - filterTypeDropDownButton.Width - ImageUtilities.ScaleByDpi(4f, this);
		DpiTrace("FilterBar_SizeChanged");
	}

	private void FilterInput_TextChanged(object sender, EventArgs e)
	{
		RestartFilterInputTimer();
	}

	private void MainForm_SizeChanged(object sender, EventArgs e)
	{
		if (base.WindowState != FormWindowState.Minimized)
		{
			restoreWindowState = base.WindowState;
			return;
		}

		if (!Settings.Default.MinimizeToNotiArea)
		{
			return;
		}

		Hide();
		notifyIcon.Visible = true;
	}

	private void NotifyIcon_MouseClick(object sender, MouseEventArgs e)
	{
		if (e.Button != MouseButtons.Left)
		{
			return;
		}

		if (base.Visible)
		{
			base.WindowState = (base.WindowState != FormWindowState.Minimized) ? FormWindowState.Minimized : restoreWindowState;
			return;
		}

		Show();
		notifyIcon.Visible = Settings.Default.AlwaysShowIconInNofiArea;
		base.WindowState = restoreWindowState;
	}

	protected override void DefWndProc(ref Message message)
	{
		const int WmCopyData = 74;
		if (message.Msg != WmCopyData)
		{
			base.DefWndProc(ref message);
			return;
		}

		NativeMethods.CopyDataStruct copyData = (NativeMethods.CopyDataStruct)message.GetLParam(typeof(NativeMethods.CopyDataStruct));
		string[] forwardedPaths = copyData.Data.Split(new string[1] { "\" \"" }, StringSplitOptions.RemoveEmptyEntries);
		if (forwardedPaths.Any())
		{
			AddFilesFromPaths(forwardedPaths.Select(NormalizeDroppedFilePath));
		}
	}

	internal static string NormalizeDroppedFilePath(string path)
	{
		return path.Replace("\"", "").Trim();
	}

	private void CloseKnownMessageBoxes()
	{
		MessageBoxWindowCollector windowCollector = new MessageBoxWindowCollector
		{
			MatchingWindowHandles = new List<IntPtr>(),
			WindowTextBuffer = new StringBuilder(1000),
			ClassNameBuffer = new StringBuilder(1000),
			MainWindowThreadId = NativeMethods.GetWindowThreadProcessId(base.Handle, out var _)
		};
		Process.GetCurrentProcess().Threads.Cast<ProcessThread>().Where(windowCollector.IsNotMainWindowThread).ForEachItem(windowCollector.CollectFromThread);
		windowCollector.MatchingWindowHandles.ForEach(CloseMessageBoxWindow);
	}

	private static void CloseMessageBoxWindow(IntPtr windowHandle)
	{
		NativeMethods.SendMessage(windowHandle, 16, IntPtr.Zero, IntPtr.Zero);
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing && components != null)
		{
			components.Dispose();
		}
		base.Dispose(disposing);
	}

	private void ApplyTagPanelLayoutAfterResize()
	{
		ApplyTagPanelLayout();
	}

	private void ResizeStatusLabels()
	{
		Size statusLabelSize = new Size(statusLabelsPanel.Width - statusLabelsPanel.Padding.Left - statusLabelsPanel.Padding.Right, ImageUtilities.ScaleByDpi(20f, this));
		coverPictureTypeLabel.Size = statusLabelSize;
		coverFileSizeLabel.Size = statusLabelSize;
		coverDimensionsLabel.Size = statusLabelSize;
		coverMimeTypeLabel.Size = statusLabelSize;
	}

	private void ClearTagFieldSelectionState(KeyValuePair<string, ComboBox> reference)
	{
		string key = reference.Key;
		ComboBox comboBox = reference.Value;
		var (valueCounts, filterOptions) = selectedFilterValueStates[key];
		ResetKeepBlankComboBoxItems(comboBox);
		comboBox.Text = "";
		valueCounts.Clear();
		filterOptions.Clear();
	}

	// 从 ValidateNumberedTagField 提取:track/disc 数字字段接受谓词(空白 / <keep> / <blank> 哨兵 / 前导正数)。
	// 保留短路序:IsNullOrWhiteSpace 先行 -> ParseLeadingNumber 不遇 null。
	internal static bool IsNumberedTagFieldValueValid(string text)
	{
		return string.IsNullOrWhiteSpace(text) || text == "<keep>" || text == "<blank>" || ParseLeadingNumber(text) > 0;
	}

	// 从 SaveTags 提取:批量写回时单个 tag 字段的模板值解析(behavior-preserving)。
	// <blank> -> 写空串;纯 <keep> -> 不写(ShouldAssign=false);含 <keep>(非纯 <keep>)-> 写 Replace 后的值;
	// 否则 -> 写原文。currentValueProvider 为 lazy Func:仅在"含 <keep>"分支调用(逐字节保留原
	// currentValue 只在该分支读 tagState 索引器的求值语义,规避 eager-eval 陷阱)。
	internal static (bool ShouldAssign, string Value) ResolveTagFieldTemplateValue(string text, Func<string> currentValueProvider)
	{
		if (text == "<blank>")
		{
			return (true, "");
		}
		if (text == "<keep>")
		{
			return (false, null);
		}
		if (text.Contains("<keep>"))
		{
			return (true, text.Replace("<keep>", currentValueProvider()));
		}
		return (true, text);
	}

	private bool ValidateNumberedTagField(string fieldName, string message)
	{
		ComboBox comboBox = tagComboBoxes[fieldName];
		string text = comboBox.Text;
		if (IsNumberedTagFieldValueValid(text))
		{
			return true;
		}
		DialogService.ShowErrorMessage(message);
		comboBox.Focus();
		return false;
	}

	private string GetCurrentTagFieldText(string fieldName)
	{
		return tagComboBoxes[fieldName].Text;
	}

	private void RefreshSelectedItemsAfterBatchMessage((string msg, bool isErr) msgWrapper)
	{
		RefreshSelectedItems(showErrorMessageBox: false, showProgressDialog: true, refreshStatusAllInfo: true, previousMessage: msgWrapper);
	}

	private void EnableCheckForUpdatesMenuItem()
	{
		checkForUpdatesMenuItem.Enabled = true;
	}

	private static bool ShouldChangeTagsFromFilenameDialog(FilenameRelatedBatchDialog dialog)
	{
		return dialog.IsChangeTagsModeSelected;
	}



}






