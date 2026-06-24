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
using System.Net;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Formatters.Binary;
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

internal class StateFieldInstance : Form
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

	private class ListViewItemNaturalComparer : IComparer
	{
		[Serializable]
		public class ListViewSortSetting
		{
			public int? Column { get; set; }

			public SortOrder SortOrder { get; set; }
		}

		private readonly ListViewSortSetting sortSetting;

		private static readonly Regex naturalSortSegmentRegex = new Regex("(\\D*)(\\d*)");

		public ListViewItemNaturalComparer(ListViewSortSetting sortSetting)
		{
			this.sortSetting = sortSetting;
		}

		public int Compare(object left, object right)
		{
			ListViewItem leftItem = left as ListViewItem;
			ListViewItem rightItem = right as ListViewItem;
			int columnIndex = sortSetting.Column.Value;
			string leftText = leftItem.SubItems[columnIndex].Text;
			string rightText = rightItem.SubItems[columnIndex].Text;
			string columnName = configuredColumnHeaders[columnIndex].Name;
			bool useNaturalSort = columnName == "trackstr" || columnName == "discstr";
			return sortSetting.SortOrder switch
			{
				SortOrder.Descending => useNaturalSort ? CompareNaturalText(rightText, leftText) : string.Compare(rightText, leftText),
				SortOrder.Ascending => useNaturalSort ? CompareNaturalText(leftText, rightText) : string.Compare(leftText, rightText),
				_ => 0,
			};
		}

		private int CompareNaturalText(string left, string right)
		{
			List<(string Text, string Number)> leftSegments = SplitNaturalSortSegments(left);
			List<(string Text, string Number)> rightSegments = SplitNaturalSortSegments(right);
			int segmentIndex = 0;
			for (; segmentIndex < leftSegments.Count && segmentIndex < rightSegments.Count; segmentIndex++)
			{
				(string leftText, string leftNumberText) = leftSegments[segmentIndex];
				(string rightText, string rightNumberText) = rightSegments[segmentIndex];
				if (leftText == rightText)
				{
					if (leftNumberText == rightNumberText)
					{
						continue;
					}
					if (long.TryParse(leftNumberText, out long leftNumber) && long.TryParse(rightNumberText, out long rightNumber))
					{
						int numericCompare = leftNumber.CompareTo(rightNumber);
						if (numericCompare != 0)
						{
							return numericCompare;
						}
					}
					return string.Compare(leftNumberText, rightNumberText);
				}
				return string.Compare(leftText + leftNumberText, rightText + rightNumberText);
			}
			return leftSegments.Count - rightSegments.Count;
		}

		private List<(string Text, string Number)> SplitNaturalSortSegments(string value)
		{
			List<(string Text, string Number)> segments = new List<(string, string)>();
			foreach (Match match in naturalSortSegmentRegex.Matches(value))
			{
				string text = match.Groups[1].Value;
				string number = match.Groups[2].Value;
				if (!string.IsNullOrEmpty(text) || !string.IsNullOrEmpty(number))
				{
					segments.Add((text ?? "", number ?? ""));
				}
			}
			return segments;
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
			DatabaseMapper.ShowErrorMessage(ErrorMessage);
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
				ListViewItem listViewItem = null;
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
					if ((column.Name == "lyrics" || column.Name == "comment") && value.Length > 20)
					{
						value = value.Substring(0, 20).Trim();
					}
					if (column.Name == columns[0].Name)
					{
						listViewItem = new ListViewItem(value)
						{
							Tag = filePath,
							ImageKey = Path.GetExtension(filePath).ToLower()
						};
					}
					else if (listViewItem != null)
					{
						ListViewItem.ListViewSubItem subItem = listViewItem.SubItems.Add(value);
						if (column.Name == "durationinms" && value != "")
						{
							subItem.Tag = tagFile[column.Name];
						}
						if (column.Name == "comment")
						{
							subItem.Tag = fullValueLength;
						}
					}
				}
				if (listViewItem != null)
				{
					if (tagFile == null || !tagFile.IsLoadedSuccessfully())
					{
						listViewItem.ForeColor = Color.Red;
					}
					Owner.fileListView.Items.Add(listViewItem);
					Owner.cachedFileListItems.Add((listViewItem, false));
					if (anyFileMode)
					{
						Owner.FileSettings.AddForAnyFile(filePath);
					}
				}
			}
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
					if (!Owner.fileTypeImageList.Images.ContainsKey(extension))
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
			Bitmap sourceIcon = DatabaseMapper.GetSmallFileIcon(filePath).ToBitmap();
			try
			{
				Bitmap listIcon = new Bitmap(FileIconSize.Width, FileIconSize.Height);
				try
				{
					using (Graphics graphics = Graphics.FromImage(listIcon))
					{
						graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
						graphics.DrawImage(sourceIcon, new Point((FileIconSize.Width - sourceIcon.Width) / 2, (FileIconSize.Height - sourceIcon.Height) / 2));
					}
					Owner.fileTypeImageList.Images.Add(extension, listIcon);
				}
				finally
				{
					listIcon.Dispose();
				}
			}
			finally
			{
				sourceIcon.Dispose();
			}
		}

		public void ShowLoadErrors()
		{
			DatabaseMapper.ShowErrorMessage(LoadErrors.ToString());
		}
	}

	private sealed class RefreshItemsTaskContext
	{
		public CancellationTokenSource cancellationSource;

		public ProgressDialog progressDialog;

		public FileInfo currentFile;

		public int processedCount;

		public SelectedListViewItemInfo[] itemInfos;

		public bool updateCachedListItems;

		public StateFieldInstance owner;

		public Page loadErrors;

		public IProgress<List<(SelectedListViewItemInfo ItemInfo, ConfigDescriptorState TagState, Dictionary<string, string> DisplayValues)>> progressReporter;

		public IComparer originalListViewSorter;

		public (string msg, bool isErr)? previousMessage;

		public bool showLoadErrors;

		public Action<(SelectedListViewItemInfo ItemInfo, ConfigDescriptorState TagState, Dictionary<string, string> DisplayValues)> updateListViewItemAction;

		internal void Cancel()
		{
			if (!cancellationSource.IsCancellationRequested)
			{
				cancellationSource.Cancel();
			}
		}

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
			ListViewItem listViewItem = updateCachedListItems ? owner.cachedFileListItems[info.ItemInfo.Index].listViewItem : owner.fileListView.Items[info.ItemInfo.Index];
			owner.UpdateListViewItemValues(listViewItem, info.TagState, info.DisplayValues);
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
			owner.fileListView.ListViewItemSorter = originalListViewSorter;
			if (previousMessage.HasValue)
			{
				if (previousMessage.Value.isErr)
				{
					DatabaseMapper.ShowErrorMessage(previousMessage?.msg);
				}
				else
				{
					DatabaseMapper.ShowInformationMessage(previousMessage?.msg);
				}
			}
			if (!showLoadErrors || loadErrors.LineCount <= 0)
			{
				return;
			}
			DatabaseMapper.ShowErrorMessage(loadErrors.ToString());
		}
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

	private sealed class FilterValueCollector
	{
		public ListViewItem listViewItem;

		public FileListFilterContext filterContext;

		internal void CountFilterValue(KeyValuePair<string, (Dictionary<string, int>, List<(string, bool)>)> filterState)
		{
			string key = filterState.Key;
			var (valueCounts, filterOptions) = filterState.Value;
			string filterValue = filterContext.owner.GetSelectedFilterValue(listViewItem, key);
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

	private sealed class FileListFilterItemContext
	{
		public ListViewItem listViewItem;

		public FileListFilterContext filterContext;

		internal bool MatchesFilterText(int columnIndex)
		{
			return listViewItem.SubItems[columnIndex].Text.IndexOf(filterContext.filterText, StringComparison.OrdinalIgnoreCase) >= 0;
		}

		internal void CountSelectedFilterValue(KeyValuePair<string, (Dictionary<string, int>, List<(string, bool)>)> filterState)
		{
			string key = filterState.Key;
			var (valueCounts, filterOptions) = filterState.Value;
			string filterValue = filterContext.owner.GetSelectedFilterValue(listViewItem, key);
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

	private sealed class SelectedItemFilterValueCounter
	{
		public ListViewItem listViewItem;

		public StateFieldInstance owner;

		internal void CountFilterValue(KeyValuePair<string, (Dictionary<string, int>, List<(string, bool)>)> filterState)
		{
			string key = filterState.Key;
			var (valueCounts, filterOptions) = filterState.Value;
			string filterValue = owner.GetSelectedFilterValue(listViewItem, key);
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
			byte[] jpegBytes = DatabaseMapper.EncodeJpeg(workingImage, quality);
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
			Bitmap bitmap = DatabaseMapper.ScaleImage(workingImage, 0.75f);
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
			Bitmap bitmap = DatabaseMapper.ResizeImageToFit(workingImage, new Size(maxSideLength, maxSideLength));
			if (bitmap != null)
			{
				workingImage.Dispose();
				workingImage = bitmap;
				return encodeWorkingImageAsJpeg(quality);
			}
			return false;
		}

		internal bool ResizeTo1200Quality75()
		{
			return resizeWorkingImageAndEncode(1200, 75L);
		}

		internal bool ResizeTo1200Quality55()
		{
			return resizeWorkingImageAndEncode(1200, 55L);
		}

		internal bool ResizeTo800Quality75()
		{
			return resizeWorkingImageAndEncode(800, 75L);
		}

		internal bool ResizeTo800Quality55()
		{
			return resizeWorkingImageAndEncode(800, 55L);
		}

		internal bool ResizeTo500Quality75()
		{
			return resizeWorkingImageAndEncode(500, 75L);
		}

		internal bool ResizeTo500Quality55()
		{
			return resizeWorkingImageAndEncode(500, 55L);
		}

		internal bool ResizeTo500Quality30()
		{
			return resizeWorkingImageAndEncode(500, 30L);
		}

		internal bool ResizeTo300Quality50()
		{
			return resizeWorkingImageAndEncode(300, 50L);
		}

		internal bool ResizeTo300Quality30()
		{
			return resizeWorkingImageAndEncode(300, 30L);
		}

		internal bool ResizeTo100Quality30()
		{
			return resizeWorkingImageAndEncode(100, 30L);
		}

		internal bool ResizeTo50Quality10()
		{
			return resizeWorkingImageAndEncode(50, 10L);
		}

		internal bool ResizeToConfiguredLimitQuality75()
		{
			return resizeWorkingImageAndEncode(compressionItem.options.maxResolution, 75L);
		}

		internal bool ResizeToConfiguredLimitQuality50()
		{
			return resizeWorkingImageAndEncode(compressionItem.options.maxResolution, 50L);
		}

		internal bool ResizeToConfiguredLimitQuality30()
		{
			return resizeWorkingImageAndEncode(compressionItem.options.maxResolution, 30L);
		}

		internal bool ResizeToConfiguredLimitQuality10()
		{
			return resizeWorkingImageAndEncode(compressionItem.options.maxResolution, 10L);
		}

		internal bool CompressPicture()
		{
			using (MemoryStream stream = new MemoryStream(compressionItem.pictureData.ImageBytes))
			{
				workingImage = Image.FromStream(stream);
				int retryStepIndex = 0;
				int compressionPassCount = 0;
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
							else
							{
								if (retryStepIndex >= compressionRetrySteps.Length)
								{
									return false;
								}
								if (!compressionRetrySteps[retryStepIndex++]())
								{
									return false;
								}
							}
						}
						else if (!encodeWorkingImageAsJpeg(85L))
						{
							return false;
						}
					}
					else
					{
						if (retryStepIndex >= compressionRetrySteps.Length)
						{
							return false;
						}
						if (!compressionRetrySteps[retryStepIndex++]())
						{
							return false;
						}
					}
					compressionPassCount++;
				}
			}
			return true;
		}
	}

	private sealed class ConvertFilenameChineseBatchContext
	{
		public CancellationTokenSource cancellationSource;

		public ProgressDialog progressDialog;

		public FileInfo currentFile;

		public int processedCount;

		public (string Path, string NewPath, int ListViewIndex)[] renameItems;

		public int renamedCount;

		public int failedCount;

		public int skippedCount;

		public Page messageLog;

		public bool convertSimplifiedToTraditional;

		internal void Cancel()
		{
			if (!cancellationSource.IsCancellationRequested)
			{
				cancellationSource.Cancel();
			}
		}

		internal void UpdateProgress()
		{
			progressDialog.UpdateStatisticsProgress(Resources.Msg_Savetag + currentFile?.Name, processedCount, renameItems.Length, renamedCount, failedCount, skippedCount, renameItems.Length);
		}

		internal void ConvertFilenames()
		{
			TagHistoryRepository tagHistoryRepository = new TagHistoryRepository(useTransaction: true);
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
						File.Move(renameFailureRecorder.CurrentPath, destinationPath);
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
			tagHistoryRepository.Dispose();
		}
	}

	private sealed class RenameFailureRecorder
	{
		public string CurrentPath;

		public ConvertFilenameChineseBatchContext batchContext;

		internal void ReportFailure(string message)
		{
			string text = message ?? Resources.Msg_SaveFail;
			DatabaseMapper.WriteRenameLog(CurrentPath + ": " + text);
			batchContext.messageLog.AddLine(batchContext.currentFile.Name);
			batchContext.messageLog.AddLine(text);
		}
	}

	private sealed class SaveTagsTaskContext
	{
		public CancellationTokenSource cancellationSource;

		public ProgressDialog progressDialog;

		public FileInfo currentFile;

		public int processedCount;

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

		internal void Cancel()
		{
			if (!cancellationSource.IsCancellationRequested)
			{
				cancellationSource.Cancel();
			}
		}

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
					DatabaseMapper.WriteSaveTagsLog(Resources.Msg_CompressPictureFail);
					messageLog.AddLine(Resources.Msg_CompressPictureFail);
					return;
				}
				compressedInputPictures = true;
			}
			Func<ConfigDescriptorState, string, bool> applyLyrics = applyLyricsAction ?? (applyLyricsAction = ApplyLyricsBatchAction);
			Func<ConfigDescriptorState, bool, bool> convertChineseText = convertChineseTextAction ?? (convertChineseTextAction = ConvertChineseTextFields);
			TagHistoryRepository tagHistoryRepository = new TagHistoryRepository(useTransaction: true);
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
				DatabaseMapper.ClearReadOnlyIfAllowed(currentFile, canCancelReadOnly);
				DateTime lastWriteTime = currentFile.LastWriteTime;
				bool tagFileLoaded = false;
				SaveTagFailureReporter failureReporter = new SaveTagFailureReporter();
				failureReporter.fileContext = fileContext;
				failureReporter.tagState = new ConfigDescriptorState(failureReporter.fileContext.filePath);
				try
				{
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
									if (text == "<blank>")
									{
										failureReporter.tagState[tagValueEntry.Key] = "";
									}
									else if (text != "<keep>")
									{
										if (text.Contains("<keep>"))
										{
											string newValue = ((failureReporter.tagState[tagValueEntry.Key] != null) ? failureReporter.tagState[tagValueEntry.Key].ToString() : "");
											failureReporter.tagState[tagValueEntry.Key] = text.Replace("<keep>", newValue);
										}
										else
										{
											failureReporter.tagState[tagValueEntry.Key] = text;
										}
									}
									continue;
								}
								if (tagValueEntry.Key == "allpicturedata")
								{
									failureReporter.tagState.LoadAllPictures();
									List<ConfigDescriptorState.PictureData> newPictures = failureReporter.tagState["allpicturedata"] as List<ConfigDescriptorState.PictureData>;
									foreach (ConfigDescriptorState.PictureData picture in newPictures)
									{
										ConfigDescriptorState.LoadPictureImage(picture);
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
									ConfigDescriptorState.LoadPictureImage(picture);
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
									File.WriteAllText(DatabaseMapper.BuildLyricSavePath(failureReporter.fileContext.filePath, failureReporter.tagState), lyricsText, Encoding.GetEncoding(saveLrcFileDefaultEncoding));
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
						DatabaseMapper.WriteSaveTagsLog(fileContext.filePath + ": " + ex2.Message);
						messageLog.AddLine(currentFile.Name);
						messageLog.AddLine(ex2.Message);
					}
				}
				processedCount++;
			}
			tagHistoryRepository.Dispose();
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

	private sealed class SaveTagFailureReporter
	{
		public ConfigDescriptorState tagState;

		public SaveTagsFileContext fileContext;

		internal void ReportFailure(string message)
		{
			string text = message ?? Resources.Msg_SaveFail;
			if (!string.IsNullOrWhiteSpace(tagState.GetLoadError()))
			{
				text = tagState.GetLoadError();
			}
			DatabaseMapper.WriteSaveTagsLog(fileContext.filePath + ": " + text);
			fileContext.batchContext.messageLog.AddLine(fileContext.batchContext.currentFile.Name);
			fileContext.batchContext.messageLog.AddLine(text);
		}
	}

	private sealed class UndoSaveTagsTaskContext
	{
		public CancellationTokenSource cancellationSource;

		public int processedCount;

		public List<ConfigDescriptorState> undoTagSnapshots;

		public ProgressDialog progressDialog;

		public FileInfo currentFile;

		public int restoredCount;

		public int failedCount;

		public int skippedCount;

		public Page messageLog;

		internal void Cancel()
		{
			if (!cancellationSource.IsCancellationRequested)
			{
				cancellationSource.Cancel();
			}
		}

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
							DatabaseMapper.WriteSaveTagsLog(fileContext.filePath + ": " + ex.Message);
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
						File.WriteAllText(DatabaseMapper.BuildLyricSavePath(failureReporter.fileContext.filePath, failureReporter.tagState), lyricsText, Encoding.GetEncoding(saveLrcFileDefaultEncoding));
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
			string errorMessage = fallbackMessage ?? Resources.Msg_SaveFail;
			string saveError = tagState.GetLoadError();
			if (!string.IsNullOrWhiteSpace(saveError))
			{
				errorMessage = saveError;
			}

			DatabaseMapper.WriteSaveTagsLog(fileContext.filePath + ": " + errorMessage);
			fileContext.taskContext.messageLog.AddLine(fileContext.taskContext.currentFile.Name);
			fileContext.taskContext.messageLog.AddLine(errorMessage);
		}
	}

	private sealed class UndoSaveTagsListItemMatcher
	{
		public ListViewItem listViewItem;

		internal bool MatchesSnapshotPath(ConfigDescriptorState reference)
		{
			return reference["filepath"] == listViewItem.Tag;
		}
	}

	private sealed class UndoRenameTaskContext
	{
		public CancellationTokenSource cancellationSource;

		public int processedCount;

		public List<(string oldPath, string newPath, bool failed)> renameUndoOperations;

		public ProgressDialog progressDialog;

		public FileInfo currentFile;

		public int successCount;

		public int failedCount;

		public int skippedCount;

		public Page errorLog;

		public StateFieldInstance owner;

		internal void Cancel()
		{
			if (!cancellationSource.IsCancellationRequested)
			{
				cancellationSource.Cancel();
			}
		}

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
						currentFile.MoveTo(originalPath);
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

			tagHistoryRepository.Dispose();
		}
	}

	private sealed class RenameUndoErrorRecorder
	{
		public string CurrentPath;

		public UndoRenameTaskContext TaskContext;

		internal void RecordError(string errorMessage)
		{
			DatabaseMapper.WriteSaveTagsLog(CurrentPath + ": " + errorMessage);
			TaskContext.errorLog.AddLine(TaskContext.currentFile.Name);
			TaskContext.errorLog.AddLine(errorMessage);
		}
	}

	private sealed class RenameUndoListItemMatcher
	{
		public ListViewItem ListViewItem;

		internal bool MatchesCurrentPath((string oldPath, string newPath, bool failed) operation)
		{
			return operation.newPath == ListViewItem.Tag as string;
		}
	}

	private sealed class ClearTagsTaskContext
	{
		public CancellationTokenSource cancellationSource;

		public ProgressDialog progressDialog;

		public FileInfo currentFile;

		public int processedCount;

		public SelectedListViewItemInfo[] itemsToClear;

		public int successCount;

		public int failedCount;

		public bool canCancelFileReadonly;

		public Page errorLog;

		internal void Cancel()
		{
			if (!cancellationSource.IsCancellationRequested)
			{
				cancellationSource.Cancel();
			}
		}

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
				DatabaseMapper.ClearReadOnlyIfAllowed(currentFile, canCancelFileReadonly);
				DateTime lastWriteTime = currentFile.LastWriteTime;
				bool savedTags = false;
				TagSaveFailureReporter failureReporter = new TagSaveFailureReporter();
				failureReporter.FileContext = tagSaveFileContext;
				failureReporter.TagFile = new ConfigDescriptorState(failureReporter.FileContext.FilePath);
				try
				{
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
						DatabaseMapper.WriteClearTagsLog(tagSaveFileContext.FilePath + ": " + ex.Message);
						errorLog.AddLine(currentFile.Name);
						errorLog.AddLine(ex.Message);
					}
				}

				processedCount++;
			}
			tagHistoryRepository.Dispose();
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
			string message = TagFile.GetLoadError();
			if (string.IsNullOrWhiteSpace(message))
			{
				message = fallbackMessage ?? Resources.Msg_SaveFail;
			}
			DatabaseMapper.WriteClearTagsLog(FileContext.FilePath + ": " + message);
			FileContext.Owner.errorLog.AddLine(FileContext.Owner.currentFile.Name);
			FileContext.Owner.errorLog.AddLine(message);
		}
	}

	private sealed class DeleteFilesTaskContext
	{
		public CancellationTokenSource cancellationSource;

		public ProgressDialog progressDialog;

		public FileInfo currentFile;

		public int processedCount;

		public SelectedListViewItemInfo[] itemsToDelete;

		public int deletedCount;

		public Page errorLog;

		public int failedCount;

		public StateFieldInstance owner;

		internal void Cancel()
		{
			if (!cancellationSource.IsCancellationRequested)
			{
				cancellationSource.Cancel();
			}
		}

		internal void UpdateProgress()
		{
			progressDialog.UpdateCountProgress(Resources.Msg_Deletefile + currentFile?.Name, processedCount, itemsToDelete.Length);
		}

		internal void DeleteFiles()
		{
			TagHistoryRepository tagHistoryRepository = new TagHistoryRepository(useTransaction: true);
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

			tagHistoryRepository.Dispose();
		}

		internal void ShowCompletionResult()
		{
			if (itemsToDelete.Length <= 1)
			{
				if (deletedCount > 0)
				{
					DatabaseMapper.ShowInformationMessage(Resources.Msg_DeleteFilesCompleted);
				}
				else
				{
					DatabaseMapper.ShowErrorMessage(errorLog.ToString());
				}
			}
			else
			{
				DatabaseMapper.ShowInformationMessage(string.Format(Resources.Msg_DeleteFilesCompleted + "\n" + Resources.Msg_OK_Fail_Count, deletedCount, failedCount, processedCount) + "\n" + errorLog.ToString());
			}

			owner.removeItemsMenuItem.PerformClick();
		}
	}

	private sealed class SaveLrcFilesTaskContext
	{
		public CancellationTokenSource cancellationSource;

		public ProgressDialog progressDialog;

		public FileInfo currentFile;

		public int processedCount;

		public SelectedListViewItemInfo[] itemsToSave;

		public Page errorLog;

		public int savedCount;

		public int failedCount;

		public int skippedCount;

		internal void Cancel()
		{
			if (!cancellationSource.IsCancellationRequested)
			{
				cancellationSource.Cancel();
			}
		}

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
								File.WriteAllText(DatabaseMapper.BuildLyricSavePath(errorRecorder.filePath, configDescriptorState), text, Encoding.GetEncoding(saveLrcFileDefaultEncoding));
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
				DatabaseMapper.ShowInformationMessage(string.Format(Resources.Msg_SaveLrcFilesComplete1, DatabaseMapper.GetLyricSaveDirectoryDisplayName()) + "\n" + string.Format(Resources.Msg_OK_Fail_Skip_Count, savedCount, failedCount, skippedCount, processedCount) + "\n" + errorLog.ToString());
				return;
			}
			if (savedCount <= 0)
			{
				DatabaseMapper.ShowErrorMessage(errorLog.ToString());
				return;
			}
			DatabaseMapper.ShowInformationMessage(string.Format(Resources.Msg_SaveLrcFilesComplete1, DatabaseMapper.GetLyricSaveDirectoryDisplayName()));
		}
	}

	private sealed class SaveLrcErrorRecorder
	{
		public string filePath;

		public SaveLrcFilesTaskContext taskContext;

		internal void RecordError(string errorMessage)
		{
			string message = string.IsNullOrWhiteSpace(errorMessage) ? Resources.Msg_SaveFail : errorMessage;
			DatabaseMapper.WriteSaveLyricsLog(filePath + ": " + message);
			taskContext.errorLog.AddLine(taskContext.currentFile.Name);
			taskContext.errorLog.AddLine(message);
		}
	}

	private sealed class ExtractCoversTaskContext
	{
		public CancellationTokenSource cancellationSource;

		public ProgressDialog progressDialog;

		public FileInfo currentFile;

		public int processedCount;

		public SelectedListViewItemInfo[] itemsToExtract;

		public Page errorLog;

		public int extractedCount;

		public int skippedCount;

		public int failedCount;

		internal void Cancel()
		{
			if (!cancellationSource.IsCancellationRequested)
			{
				cancellationSource.Cancel();
			}
		}

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
								ConfigDescriptorState.LoadPictureImage(pictureData);
								if (pictureData.MimeType != null && pictureData.Width > 0 && pictureData.Height > 0)
								{
									string imageExtension = DatabaseMapper.GetImageExtensionForMimeType(pictureData.MimeType, ".jpg");
									File.WriteAllBytes(DatabaseMapper.GetSiblingPathWithExtension(errorRecorder.FilePath, imageExtension), array);
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
			if (itemsToExtract.Length <= 1)
			{
				if (extractedCount > 0)
				{
					DatabaseMapper.ShowInformationMessage(Resources.Msg_ExtractCoversComplete);
				}
				else
				{
					DatabaseMapper.ShowErrorMessage(errorLog.ToString());
				}
			}
			else
			{
				DatabaseMapper.ShowInformationMessage(string.Format(Resources.Msg_ExtractCoversComplete + "\n" + Resources.Msg_OK_Fail_Skip_Count, extractedCount, failedCount, skippedCount, processedCount) + "\n" + errorLog.ToString());
			}
		}
	}

	private sealed class ExtractCoverErrorRecorder
	{
		public string FilePath;

		public ExtractCoversTaskContext TaskContext;

		internal void RecordError(string errorMessage)
		{
			string text = string.IsNullOrWhiteSpace(errorMessage) ? Resources.Msg_SaveFail : errorMessage;
			DatabaseMapper.WriteSaveCoversLog(FilePath + ": " + text);
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
					owner.fileListView.Items[renameItem.Index].Tag = renameItem.NewPath;
				}
			}
			owner.RefreshSelectedItems(showErrorMessageBox: false, showProgressDialog: true, refreshStatusAllInfo: true, previousMessage: message);
		}
	}

	private sealed class AppSettingsSaveTask
	{
		public AppSettingData appSettingsData;

		public StateFieldInstance owner;

		internal void SaveSettings()
		{
			using (FileStream fileStream = new FileStream(AppSettingData.AppSettingDataPath, FileMode.Create))
			{
				new BinaryFormatter().Serialize(fileStream, appSettingsData);
			}

			Settings.Default.SortSetting = JsonConvert.SerializeObject(owner.SortSetting);
			Settings.Default.MainFormPosSizeInfo = JsonConvert.SerializeObject(owner.MainFormPosSizeInfo);
			Settings.Default.FilterListViewType = owner.filterTypeDropDownButton.Tag as string;
			Settings.Default.FilterListViewKeyword = owner.filterTextBox.Text;
			Settings.Default.LastVersionCode = 17;
			CustomColumnsDialog.SaveColumnHeaderSettings();
			Settings.Default.Save();
			TagHistoryRepository.ClearUndoState();
			TagHistoryRepository.CloseSharedConnection();
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
			DatabaseMapper.ShowErrorMessage((NewPath ?? RequestedFileName) + "\n" + Resources.Msg_FileNotFound);
		}

		internal void ShowRenameError()
		{
			DatabaseMapper.ShowErrorMessage((NewPath ?? RequestedFileName) + "\n" + RenameError.Message);
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

	private HeaderAwareListView fileListView;

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

	private List<(ListViewItem listViewItem, bool isHidden)> cachedFileListItems { get; }

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
		ServicePointManager.SecurityProtocol = SecurityProtocolType.Ssl3 | SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
	}

	public StateFieldInstance(params string[] args)
	{
		localizedResources = new ComponentResourceManager(typeof(StateFieldInstance));
		fileTypeImageList = new ImageList();
		tagComboBoxes = new Dictionary<string, ComboBox>();
		tagFieldTextHandlers = new Dictionary<string, (Label, EventHandler)>();
		selectedFilterValueStates = new Dictionary<string, (Dictionary<string, int> valueCounts, List<(string value, bool wasRemoved)> changedValues)>();
		cachedFileListItems = new List<(ListViewItem, bool)>();
		editableTagFieldNames = new string[14]
		{
			"filename", "filedir", "tagtypes", "title", "artist", "album", "year", "trackstr", "discstr", "genre",
			"albumartist", "composer", "lyricist", "comment"
		};
		lastFileListFilterText = "";
		startupFileArgs = args;
		taskbarProgress = new TaskbarProgressController(this);
		InitializeComponent();
		RegisterEditableTagFields();
		ApplyToolbarImagesAndScaling();
		InitializeTagEditorControls();
		InitializeFileListColumnsAndIcons();
		InitializeFileListFilterMenu();
		ApplyLanguageResources();
		InitializeSourceMenus();
		ApplyTagPanelLayout();
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
		if (DatabaseMapper.GetDpiScale() == 1f)
		{
			return;
		}
			Image image = (changeDirectoryToolStripButton.Image = DatabaseMapper.LoadResourceBitmap("chgDirToolStripMenuItem_Image", scaleSmallIconForDpi: true));
			changeDirectoryMenuItem.Image = image;
			addDirectoriesToolStripButton.Image = image = DatabaseMapper.LoadResourceBitmap("addDirsToolStripMenuItem_Image", scaleSmallIconForDpi: true);
			addDirectoryMenuItem.Image = image;
			image = (manageDirectoriesToolStripButton.Image = DatabaseMapper.LoadResourceBitmap("manageDirsToolStripMenuItem_Image", scaleSmallIconForDpi: true));
			manageDirectoriesMenuItem.Image = image;
			image = (saveTagsToolStripButton.Image = DatabaseMapper.LoadResourceBitmap("saveToolStripMenuItem_Image", scaleSmallIconForDpi: true));
			saveTagsMenuItem.Image = image;
			removeTagsToolStripButton.Image = image = DatabaseMapper.LoadResourceBitmap("removeTagToolStripMenuItem_Image", scaleSmallIconForDpi: true);
			removeTagsMenuItem.Image = image;
			image = (undoToolStripButton.Image = DatabaseMapper.LoadResourceBitmap("undoToolStripMenuItem_Image", scaleSmallIconForDpi: true));
			undoMenuItem.Image = image;
			readTagsToolStripButton.Image = image = DatabaseMapper.LoadResourceBitmap("readTagsToolStripMenuItem_Image", scaleSmallIconForDpi: true);
			readTagsMenuItem.Image = image;
			image = (characterSetToolStripButton.Image = DatabaseMapper.LoadResourceBitmap("characterSetToolStripMenuItem_Image", scaleSmallIconForDpi: true));
			characterSetMenuItem.Image = image;
			image = (chineseConversionToolStripDropDownButton.Image = DatabaseMapper.LoadResourceBitmap("chschtToolStripMenuItem_Image", scaleSmallIconForDpi: true));
			chineseConversionMenuItem.Image = image;
			image = (tagHistoryToolStripButton.Image = DatabaseMapper.LoadResourceBitmap("tagsHistoryToolStripMenuItem_Image", scaleSmallIconForDpi: true));
			tagHistoryMenuItem.Image = image;
			exitMenuItem.Image = DatabaseMapper.LoadResourceBitmap("exitToolStripMenuItem_Image", scaleSmallIconForDpi: true);
			image = (selectAllFilesToolStripButton.Image = DatabaseMapper.LoadResourceBitmap("selallfilesToolStripMenuItem_Image", scaleSmallIconForDpi: true));
			selectAllFilesMenuItem.Image = image;
			image = (unselectAllFilesToolStripButton.Image = DatabaseMapper.LoadResourceBitmap("unselectAllToolStripMenuItem_Image", scaleSmallIconForDpi: true));
			unselectAllFilesMenuItem.Image = image;
			refreshToolStripButton.Image = image = DatabaseMapper.LoadResourceBitmap("refreshToolStripMenuItem_Image", scaleSmallIconForDpi: true);
			refreshMenuItem.Image = image;
			image = (coverSourceToolStripSplitButton.Image = DatabaseMapper.LoadResourceBitmap("picSrcToolStripMenuItem_Image", scaleSmallIconForDpi: true));
			coverSourceMenuItem.Image = image;
			image = (lyricSourceToolStripSplitButton.Image = DatabaseMapper.LoadResourceBitmap("lyricSrcToolStripMenuItem_Image", scaleSmallIconForDpi: true));
			lyricSourceMenuItem.Image = image;
			image = (combinedTagSourceToolStripSplitButton.Image = DatabaseMapper.LoadResourceBitmap("combTagsSrcToolStripMenuItem_Image", scaleSmallIconForDpi: true));
			combinedTagSourceMenuItem.Image = image;
			image = (batchAutoMatchTagsToolStripButton.Image = DatabaseMapper.LoadResourceBitmap("batchAutoMatchTagsToolStripButton_Image", scaleSmallIconForDpi: true));
			batchAutoMatchTagsMenuItem.Image = image;
			image = (batchExtractCoverToolStripButton.Image = DatabaseMapper.LoadResourceBitmap("batchExtractCoverToolStripButton_Image", scaleSmallIconForDpi: true));
			batchExtractCoverMenuItem.Image = image;
			image = (batchSaveAsLrcToolStripSplitButton.Image = DatabaseMapper.LoadResourceBitmap("batchSaveAsLrcFileToolStripSplitButton_Image", scaleSmallIconForDpi: true));
			saveLyricsMenuItem.Image = image;
			image = (batchChineseConversionToolStripDropDownButton.Image = DatabaseMapper.LoadResourceBitmap("chschtToolStripMenuItem_Image", scaleSmallIconForDpi: true));
			batchChineseConversionMenuItem.Image = image;
			image = (batchFilenameRelatedToolStripButton.Image = DatabaseMapper.LoadResourceBitmap("batchFilenameRelToolStripButton_Image", scaleSmallIconForDpi: true));
			batchFilenameRelatedMenuItem.Image = image;
		image = (optionsToolStripButton.Image = DatabaseMapper.LoadResourceBitmap("optionsToolStripMenuItem_Image", scaleSmallIconForDpi: true));
		optionsMenuItem.Image = image;
		Size scaledImageSize = new Size(DatabaseMapper.ScaleByDpi(16f), DatabaseMapper.ScaleByDpi(16f));
		coverContextMenu.ImageScalingSize = scaledImageSize;
		fileListItemContextMenu.ImageScalingSize = scaledImageSize;
		mainToolStrip.ImageScalingSize = scaledImageSize;
		mainMenuStrip.ImageScalingSize = scaledImageSize;
		foreach (ToolStripItem toolStripItem in mainToolStrip.Items)
		{
			toolStripItem.AutoSize = false;
			if (toolStripItem is ToolStripSplitButton toolStripSplitButton)
			{
				toolStripSplitButton.Size = new Size(DatabaseMapper.ScaleByDpi(32f), DatabaseMapper.ScaleByDpi(22f));
				toolStripSplitButton.DropDownButtonWidth = DatabaseMapper.ScaleByDpi(11f);
			}
			else if (toolStripItem is ToolStripButton toolStripButton)
			{
				toolStripButton.Size = new Size(DatabaseMapper.ScaleByDpi(23f), DatabaseMapper.ScaleByDpi(22f));
			}
			else if (toolStripItem is ToolStripSeparator toolStripSeparator)
			{
				toolStripSeparator.Size = new Size(DatabaseMapper.ScaleByDpi(6f), DatabaseMapper.ScaleByDpi(25f));
			}
		}
	}

	private void InitializeTagEditorControls()
	{
		TagEncodingLayoutContext layoutContext = new TagEncodingLayoutContext
		{
			Owner = this
		};
		mainSplitContainer.Panel1MinSize = DatabaseMapper.ScaleByDpi(320f);
		FontAwesome.SetFontFileDirectory(DatabaseMapper.GetApplicationDirectory() + "font");
		FontAwesome.DefaultProperties.Size = DatabaseMapper.ScaleByDpi(18f);
		FontAwesome.DefaultProperties.ShowBorder = false;

		Image editEncodingImage = FontAwesome.Type.Wrench.AsImage();
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
				button.Image = editEncodingImage;
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
		previousCoverButton.Image = FontAwesome.Type.AngleLeft.AsImage();
		nextCoverButton.Image = FontAwesome.Type.AngleRight.AsImage();
		editLyricsButton.Image = FontAwesome.Type.Edit.AsImage();
		previousCoverButton.Text = "";
		nextCoverButton.Text = "";
		editLyricsButton.Text = "";
		coverPictureBox.Image = DatabaseMapper.LoadCachedResourceBitmap("no_cover", new Size(DatabaseMapper.ScaleByDpi(96f), DatabaseMapper.ScaleByDpi(96f)));
		coverPictureBox.SizeMode = PictureBoxSizeMode.CenterImage;
		overwriteCoverCheckBox.Checked = Settings.Default.OverwritePictureboxPicture;
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
		filterTypeDropDownButton.Width = DatabaseMapper.ScaleByDpi(100f);
		selectedFilesStatusLabel.Width = DatabaseMapper.ScaleByDpi(190f);
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
			if (selectedTagState != null && fileListView.SelectedItems.Count == 1 && selectedTagState[fieldName] is string currentValue && currentValue != comboBox.Text)
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
				Settings.Default.Save();
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
		foreach (ColumnHeader columnHeader in fileListView.Columns)
		{
			columnHeader.Text = Resources.ResourceManager.GetString(columnHeader.Name);
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

	private void InitializeFileListColumnsAndIcons()
	{
		ListView.ColumnHeaderCollection columns = fileListView.Columns;
		foreach (CustomColumnsDialog.ColumnHeaderInfo columnInfo in configuredColumnHeaders)
		{
			ColumnHeader columnHeader = new ColumnHeader
			{
				Name = columnInfo.Name,
				Width = (columnInfo.isShow ? columnInfo.width : 0),
				TextAlign = columnInfo.textAlign,
				Tag = columnInfo
			};
			columns.Add(columnHeader);
		}
		foreach (CustomColumnsDialog.ColumnHeaderInfo columnInfo in configuredColumnHeaders)
		{
			columns[columnInfo.Name].DisplayIndex = columnInfo.displayIndex;
		}
		fileTypeImageList.ColorDepth = ColorDepth.Depth32Bit;
		fileTypeImageList.ImageSize = new Size(DatabaseMapper.ScaleByDpi(20f), DatabaseMapper.ScaleByDpi(20f));
		fileListView.SmallImageList = fileTypeImageList;
	}

	private SelectedListViewItemInfo[] CollectSelectedListViewItemInfos()
	{
		return fileListView.SelectedItems.Cast<ListViewItem>().Select(CreateSelectedListViewItemInfo).ToArray();
	}

	private static SelectedListViewItemInfo CreateSelectedListViewItemInfo(ListViewItem listViewItem)
	{
		return new SelectedListViewItemInfo
		{
			Index = listViewItem.Index,
			FilePath = listViewItem.Tag as string
		};
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
			return DatabaseMapper.ConfirmYesNoCancel(string.Format(Resources.Msg_WantAllowRemoveReadonlyAttribute, readOnlyFilePath)) switch
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

		trackDiscGroupPanel.Width = titleRowPanel.Width - titleEncodingButton.Width - DatabaseMapper.ScaleByDpi(5f);
		int tagPairHeight = trackLabel.Height + trackRowPanel.Height + DatabaseMapper.ScaleByDpi(6f, roundUp: true);
		discColumnPanel.Height = tagPairHeight;
		trackColumnPanel.Height = tagPairHeight;
		trackDiscGroupPanel.Height = tagPairHeight;
		trackRowPanel.Width = trackDiscGroupPanel.Width / 2;
		trackColumnPanel.Width = trackRowPanel.Width;
		discRowPanel.Width = trackDiscGroupPanel.Width / 2 - DatabaseMapper.ScaleByDpi(5f);
		discColumnPanel.Width = discRowPanel.Width;
		discColumnPanel.Margin = new Padding(DatabaseMapper.ScaleByDpi(5f), 0, 0, 0);
		overwriteCoverCheckBox.Margin = new Padding((statusLabelsPanel.Width - overwriteCoverCheckBox.Width) / 2, DatabaseMapper.ScaleByDpi(50f), 0, 0);

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

		if (coverPanelSize > DatabaseMapper.ScaleByDpi(500f))
		{
			coverPanelSize = DatabaseMapper.ScaleByDpi(500f);
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
		HashSet<string> filePaths = new HashSet<string>();
		foreach (var cachedItem in cachedFileListItems)
		{
			ListViewItem listViewItem = cachedItem.listViewItem;
			filePaths.Add(listViewItem.Tag as string);
		}
		return filePaths;
	}

	private void ClearLoadedFileList()
	{
		fileListView.Items.Clear();
		cachedFileListItems.Clear();
		FileList_ItemSelectionChanged(null, null);
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
		progressDialog.AddCancelRequestedHandler(cancelRequestedHandler);
		progressDialog.AddProgressUpdateHandler(progressUpdateHandler);
		await Task.Run((Action)fileCollector.CollectFilePaths);
		progressDialog.RemoveCancelRequestedHandler(cancelRequestedHandler);
		progressDialog.RemoveProgressUpdateHandler(progressUpdateHandler);
		if (fileCollector.CancellationTokenSource.IsCancellationRequested)
		{
			progressDialog.CloseAfterCompletion();
		}
		else
		{
			StartAddFiles(fileCollector.FilePaths, progressDialog);
		}
		if (fileCollector.ErrorMessage != null)
		{
			BeginInvoke(new Action(fileCollector.ShowErrorMessage));
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

	private void RefreshStatusLabelsFromCachedTotals()
	{
		(long selectedDurationMs, long selectedFileSizeBytes) = GetCachedDurationAndFileSize(selectedFilesStatusLabel.Tag);
		selectedFilesStatusLabel.Text = $"{fileListView.SelectedItems.Count} ({ConfigDescriptorState.FormatDurationHms(selectedDurationMs)} | {DatabaseMapper.FormatFileSize(selectedFileSizeBytes)})";
		(long allDurationMs, long allFileSizeBytes) = GetCachedDurationAndFileSize(totalFilesStatusLabel.Tag);
		totalFilesStatusLabel.Text = $"{fileListView.Items.Count} ({ConfigDescriptorState.FormatDurationHms(allDurationMs)} | {DatabaseMapper.FormatFileSize(allFileSizeBytes)})";
	}

	private static (long DurationMs, long FileSizeBytes) GetCachedDurationAndFileSize(object cachedValue)
	{
		return cachedValue is ValueTuple<long, long> totals ? totals : (0L, 0L);
	}

	private void GetListViewItemDurationAndFileSize(ListViewItem listViewItem, out long durationMs, out long fileSizeBytes)
	{
		int durationColumnIndex = fileListView.Columns["durationinms"].Index;
		FileInfo fileInfo = new FileInfo(listViewItem.Tag as string);
		fileSizeBytes = fileInfo.Exists ? fileInfo.Length : 0L;
		durationMs = listViewItem.SubItems[durationColumnIndex].Tag is int duration ? duration : 0L;
	}

	private void UpdateFileListStatusSummary(bool selectedItemsOnly)
	{
		long allDurationMs = 0L;
		long allFileSizeBytes = 0L;
		long selectedDurationMs = 0L;
		long selectedFileSizeBytes = 0L;
		if (selectedItemsOnly)
		{
			foreach (ListViewItem selectedItem in fileListView.SelectedItems)
			{
				GetListViewItemDurationAndFileSize(selectedItem, out long itemDurationMs, out long itemFileSizeBytes);
				selectedDurationMs += itemDurationMs;
				selectedFileSizeBytes += itemFileSizeBytes;
			}
		}
		else
		{
			foreach (ListViewItem listViewItem in fileListView.Items)
			{
				GetListViewItemDurationAndFileSize(listViewItem, out long itemDurationMs, out long itemFileSizeBytes);
				allDurationMs += itemDurationMs;
				allFileSizeBytes += itemFileSizeBytes;
				if (listViewItem.Selected)
				{
					selectedDurationMs += itemDurationMs;
					selectedFileSizeBytes += itemFileSizeBytes;
				}
			}
			totalFilesStatusLabel.Text = $"{fileListView.Items.Count} ({ConfigDescriptorState.FormatDurationHms(allDurationMs)} | {DatabaseMapper.FormatFileSize(allFileSizeBytes)})";
			totalFilesStatusLabel.Tag = (allDurationMs, allFileSizeBytes);
		}
		selectedFilesStatusLabel.Text = $"{fileListView.SelectedItems.Count} ({ConfigDescriptorState.FormatDurationHms(selectedDurationMs)} | {DatabaseMapper.FormatFileSize(selectedFileSizeBytes)})";
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
		progressDialog.AddCancelRequestedHandler(addFilesWorker.Cancel);
		progressDialog.AddProgressUpdateHandler(addFilesWorker.UpdateProgress);
		addFilesWorker.LoadedFilesProgress = new Progress<List<(ConfigDescriptorState TagFile, Dictionary<string, string> DisplayValues, string FilePath)>>(addFilesWorker.AddLoadedFilesToListView);
		IComparer savedSorter = fileListView.ListViewItemSorter;
		fileListView.ListViewItemSorter = null;
		fileListView.BeginUpdate();
		await Task.Run((Action)addFilesWorker.LoadFiles, addFilesWorker.CancellationTokenSource.Token);
		ApplyFileListFilter(requireFilterText: true, suspendListSorting: false);
		fileListView.EndUpdate();
		fileListView.ListViewItemSorter = savedSorter;
		progressDialog.CloseAfterCompletion();
		UpdateFileListStatusSummary(selectedItemsOnly: false);
		if (addFilesWorker.LoadErrors.LineCount > 0)
		{
			BeginInvoke(new Action(addFilesWorker.ShowLoadErrors));
		}
	}

	private void UpdateListViewItemValues(ListViewItem listViewItem, ConfigDescriptorState tagFile, Dictionary<string, string> displayValues)
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
			if ((column.Name == "lyrics" || column.Name == "comment") && value.Length > 20)
			{
				value = value.Substring(0, 20).Trim();
			}
			ListViewItem.ListViewSubItem listViewSubItem = listViewItem.SubItems[columnIndex];
			if (listViewSubItem.Text != value)
			{
				listViewSubItem.Text = value;
				if (column.Name == "durationinms" && value != "")
				{
					listViewSubItem.Tag = tagFile[column.Name];
				}
				if (column.Name == "comment")
				{
					listViewSubItem.Tag = length;
				}
			}
			columnIndex++;
		}
		if (tagFile != null && tagFile.IsLoadedSuccessfully())
		{
			if (listViewItem.ForeColor == Color.Red)
			{
				listViewItem.ForeColor = SystemColors.WindowText;
			}
		}
		else if (listViewItem.ForeColor != Color.Red)
		{
			listViewItem.ForeColor = Color.Red;
		}
	}

	private void RefreshListViewItemFromFile(ListViewItem listViewItem, ConfigDescriptorState tagFile)
	{
		UpdateListViewItemValues(listViewItem, tagFile, BuildBasicFileDisplayValues(new FileInfo(tagFile.GetFilePath())));
	}

	private Dictionary<string, string> BuildBasicFileDisplayValues(FileInfo fileInfo)
	{
		if (fileInfo.Exists)
		{
			return new Dictionary<string, string>
			{
				{ "filename", fileInfo.Name },
				{ "filedir", fileInfo.DirectoryName },
				{
					"updatetime",
					fileInfo.LastWriteTime.ToString("yyyy/MM/dd HH:mm:ss")
				}
			};
		}
		return new Dictionary<string, string>
		{
			{ "filename", fileInfo.Name },
			{ "filedir", fileInfo.DirectoryName },
			{ "updatetime", "" }
		};
	}

	private async void StartRefreshItems(SelectedListViewItemInfo[] itemInfos, ProgressDialog progressDialog, bool showErrorMessageBox, bool refreshStatusAllInfo, (string msg, bool isErr)? previousMessage = null, bool listForMirror = false)
	{
		RefreshItemsTaskContext refreshContext = new RefreshItemsTaskContext();
		refreshContext.progressDialog = progressDialog;
		refreshContext.itemInfos = itemInfos;
		refreshContext.updateCachedListItems = listForMirror;
		refreshContext.owner = this;
		refreshContext.previousMessage = previousMessage;
		refreshContext.showLoadErrors = showErrorMessageBox;
		refreshContext.cancellationSource = new CancellationTokenSource();
		refreshContext.currentFile = null;
		refreshContext.processedCount = 0;
		if (refreshContext.progressDialog != null)
		{
			refreshContext.progressDialog.AddCancelRequestedHandler(refreshContext.Cancel);
			refreshContext.progressDialog.AddProgressUpdateHandler(refreshContext.UpdateProgress);
		}
		refreshContext.progressReporter = new Progress<List<(SelectedListViewItemInfo, ConfigDescriptorState, Dictionary<string, string>)>>(refreshContext.ApplyRefreshedItems);
		refreshContext.loadErrors = new Page();
		refreshContext.originalListViewSorter = fileListView.ListViewItemSorter;
		fileListView.ListViewItemSorter = null;
		fileListView.BeginUpdate();
		Task task = Task.Run(new Action(refreshContext.RefreshItems), refreshContext.cancellationSource.Token);
		if (refreshContext.progressDialog == null)
		{
			task.Wait();
		}
		else
		{
			await task;
		}
		ApplyFileSelectionMode(FileSelectionMode.RefreshOnly, refreshStatusAllInfo);
		fileListView.EndUpdate();
		refreshContext.progressDialog?.CloseAfterCompletion();
		BeginInvoke(new Action(refreshContext.ShowCompletionMessages));
	}

	private void FileList_ItemSelectionChanged(object sender, ListViewItemSelectionChangedEventArgs e)
	{
		int selectedItemCount = fileListView.SelectedItems.Count;
		if (selectedItemCount == 1)
		{
			selectionStatusUpdateTimer.Stop();
			ClearSelectedFilterSummaries();
			ListViewItem selectedItem = fileListView.SelectedItems[0];
			using (ConfigDescriptorState tagFile = new ConfigDescriptorState(selectedItem.Tag as string))
			{
				if (tagFile.IsLoadedSuccessfully())
				{
					tagFile.LoadBasicTagFields();
					tagFile.LoadAudioProperties();
					tagFile.LoadRawTextFieldData();
					tagFile.LoadAllPictures();
					tagFile.LoadLyrics();
				}
				RefreshListViewItemFromFile(selectedItem, tagFile);
				AddSelectedFilterValues(selectedItem);
				LoadTagEditorState(tagFile);
				UpdateFileListStatusSummary(selectedItemsOnly: true);
			}
		}
		else if (selectedItemCount <= 0)
		{
			ClearSelectedFilterSummaries();
			selectedFilesStatusLabel.Tag = (0L, 0L);
			StartSelectionStatusUpdateTimer();
		}
		else if (e != null)
		{
			UpdateSelectedFilterValues(e.Item, e.IsSelected);
			UpdateSelectedDurationAndSize(e.Item, e.IsSelected);
			StartSelectionStatusUpdateTimer();
		}
		UpdateSelectionCommandState();
	}

	private void ClearSelectedFilterSummaries()
	{
		foreach (var selectedFilter in selectedFilterValueStates.Values)
		{
			selectedFilter.Item1.Clear();
			selectedFilter.Item2.Clear();
		}
	}

	private void AddSelectedFilterValues(ListViewItem listViewItem)
	{
		foreach (var selectedFilter in selectedFilterValueStates)
		{
			AddSelectedFilterValue(selectedFilter.Value.Item1, selectedFilter.Value.Item2, GetSelectedFilterValue(listViewItem, selectedFilter.Key));
		}
	}

	private void UpdateSelectedFilterValues(ListViewItem listViewItem, bool isSelected)
	{
		foreach (var selectedFilter in selectedFilterValueStates)
		{
			Dictionary<string, int> valueCounts = selectedFilter.Value.Item1;
			List<(string, bool)> changedValues = selectedFilter.Value.Item2;
			string value = GetSelectedFilterValue(listViewItem, selectedFilter.Key);
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
	}

	private static void AddSelectedFilterValue(Dictionary<string, int> valueCounts, List<(string, bool)> changedValues, string value)
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

	private void UpdateSelectedDurationAndSize(ListViewItem listViewItem, bool isSelected)
	{
		(long selectedDurationMs, long selectedFileSizeBytes) = selectedFilesStatusLabel.Tag is ValueTuple<long, long> cachedTotals ? cachedTotals : (0L, 0L);
		GetListViewItemDurationAndFileSize(listViewItem, out var itemDurationMs, out var itemFileSizeBytes);
		if (isSelected)
		{
			selectedDurationMs += itemDurationMs;
			selectedFileSizeBytes += itemFileSizeBytes;
		}
		else
		{
			selectedDurationMs -= itemDurationMs;
			selectedFileSizeBytes -= itemFileSizeBytes;
		}
		if (selectedDurationMs < 0L)
		{
			selectedDurationMs = 0L;
		}
		if (selectedFileSizeBytes < 0L)
		{
			selectedFileSizeBytes = 0L;
		}
		selectedFilesStatusLabel.Tag = (selectedDurationMs, selectedFileSizeBytes);
	}

	private void UpdateSelectionCommandState()
	{
		int selectedItemCount = fileListView.SelectedItems.Count;
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
		IComparer listViewItemSorter = default(IComparer);
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
		try
		{
			if (suspendListSorting)
			{
				fileListView.BeginUpdate();
				listViewItemSorter = fileListView.ListViewItemSorter;
				fileListView.ListViewItemSorter = null;
			}
			fileListView.ItemSelectionChanged -= FileList_ItemSelectionChanged;
			foreach (KeyValuePair<string, (Label, EventHandler)> handlerEntry in tagFieldTextHandlers)
			{
				tagComboBoxes[handlerEntry.Key].TextChanged -= handlerEntry.Value.Item2;
			}
			tagComboBoxes.ForEachItem(activeFilterContext.ResetFilterComboState);
			if (activeFilterContext.filterText.Any())
			{
				string[] filterColumnNames = filterTypeDropDownButton.Tag as string == "any" ? GetEditableTagFieldNames() : new string[1] { filterTypeDropDownButton.Tag as string };
				IEnumerable<int> filterColumnIndexes = filterColumnNames.Select(FindColumnHeaderIndexByName);
				for (int index = 0; index < cachedFileListItems.Count; index++)
				{
					FileListFilterItemContext filterMatcher = new FileListFilterItemContext
					{
						filterContext = activeFilterContext
					};
					var (listViewItem, isHidden) = cachedFileListItems[index];
					filterMatcher.listViewItem = listViewItem;
					if (filterColumnIndexes.Any(filterMatcher.MatchesFilterText))
					{
						if (isHidden)
						{
							cachedFileListItems[index] = (filterMatcher.listViewItem, false);
						}
						if (filterMatcher.listViewItem.Selected)
						{
							selectedFilterValueStates.ForEachItem(filterMatcher.CountSelectedFilterValue);
						}
					}
					else
					{
						if (!isHidden)
						{
							cachedFileListItems[index] = (filterMatcher.listViewItem, true);
						}
						if (filterMatcher.listViewItem.Selected)
						{
							filterMatcher.listViewItem.Selected = false;
						}
					}
				}
				fileListView.Items.Clear();
				fileListView.Items.AddRange(cachedFileListItems.Where(IsVisibleCachedListViewItem).Select(GetCachedListViewItem).ToArray());
			}
			else
			{
				fileListView.Items.AddRange(cachedFileListItems.Where(IsHiddenCachedListViewItem).Select(GetCachedListViewItem).ToArray());
				for (int index = 0; index < cachedFileListItems.Count; index++)
				{
					FilterValueCollector selectedFilterRestorer = new FilterValueCollector
					{
						filterContext = activeFilterContext
					};
					var (listViewItem, isHidden) = cachedFileListItems[index];
					selectedFilterRestorer.listViewItem = listViewItem;
					if (isHidden)
					{
						cachedFileListItems[index] = (selectedFilterRestorer.listViewItem, false);
					}
					else if (selectedFilterRestorer.listViewItem.Selected)
					{
						selectedFilterValueStates.ForEachItem(selectedFilterRestorer.CountFilterValue);
					}
				}
			}

			lastFileListFilterText = activeFilterContext.filterText;
			fileListView.ItemSelectionChanged += FileList_ItemSelectionChanged;
			foreach (KeyValuePair<string, (Label, EventHandler)> handlerEntry in tagFieldTextHandlers)
			{
				tagComboBoxes[handlerEntry.Key].TextChanged += handlerEntry.Value.Item2;
			}
			ScheduleSelectionStatusUpdate(refreshStatusAllInfo: false);
			if (fileListView.SelectedItems.Count == 1)
			{
				FileList_ItemSelectionChanged(null, null);
			}
			else
			{
				StartSelectionStatusUpdateTimer();
			}
			UpdateSelectionCommandState();
		}
		finally
		{
			if (suspendListSorting)
			{
				fileListView.EndUpdate();
				fileListView.ListViewItemSorter = listViewItemSorter;
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
		if (fileListView.SelectedItems.Count <= 1)
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
			coverPictureBox.Image = DatabaseMapper.LoadCachedResourceBitmap("no_cover", new Size(DatabaseMapper.ScaleByDpi(96f), DatabaseMapper.ScaleByDpi(96f)));
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
				coverPictureBox.Image = image;
				coverPictureBox.SizeMode = PictureBoxSizeMode.Zoom;
				coverMimeTypeLabel.Text = selectedCover.MimeType;
				coverDimensionsLabel.Text = image.Width + "x" + image.Height;
				coverFileSizeLabel.Text = DatabaseMapper.FormatFileSize(selectedCover.ImageBytes.Length);
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
		string result = await Task.Run((Func<string>)lyricDownloadContext.DownloadLyricText, lyricDownloadContext.cancellationSource.Token);
		lyricTextComboBox.Text = result;
		lyricTextComboBox = null;
		lyricDownloadContext.progressDialog.CloseProgressDialog();
	}

	private void ApplyFileSelectionMode(FileSelectionMode selectionMode, bool refreshStatusAllInfo)
	{
		tagComboBoxes.Values.ForEachItem(BeginComboBoxUpdate);
		fileListView.Focus();
		fileListView.ItemSelectionChanged -= FileList_ItemSelectionChanged;
		foreach (KeyValuePair<string, (Label, EventHandler)> handlerEntry in tagFieldTextHandlers)
		{
			tagComboBoxes[handlerEntry.Key].TextChanged -= handlerEntry.Value.Item2;
		}
		tagComboBoxes.ForEachItem(ClearTagFieldSelectionState);

		foreach (ListViewItem listViewItem in fileListView.Items)
		{
			switch (selectionMode)
			{
			case FileSelectionMode.SelectAll:
				listViewItem.Selected = true;
				break;
			case FileSelectionMode.UnselectAll:
				listViewItem.Selected = false;
				break;
			case FileSelectionMode.Invert:
				listViewItem.Selected = !listViewItem.Selected;
				break;
			}

			if (!listViewItem.Selected)
			{
				continue;
			}

			SelectedItemFilterValueCounter selectedItemCounter = new SelectedItemFilterValueCounter
			{
				owner = this,
				listViewItem = listViewItem
			};
			selectedFilterValueStates.ForEachItem(selectedItemCounter.CountFilterValue);
		}

		fileListView.ItemSelectionChanged += FileList_ItemSelectionChanged;
		foreach (KeyValuePair<string, (Label, EventHandler)> handlerEntry in tagFieldTextHandlers)
		{
			tagComboBoxes[handlerEntry.Key].TextChanged += handlerEntry.Value.Item2;
		}
		if (fileListView.SelectedItems.Count == 1)
		{
			if (refreshStatusAllInfo)
			{
				ScheduleSelectionStatusUpdate(refreshStatusAllInfo: true);
			}
			FileList_ItemSelectionChanged(null, null);
		}
		else
		{
			ScheduleSelectionStatusUpdate(!refreshStatusAllInfo);
			StartSelectionStatusUpdateTimer();
		}
		UpdateSelectionCommandState();
		tagComboBoxes.Values.ForEachItem(EndComboBoxUpdate);
	}

	private static void BeginComboBoxUpdate(ComboBox comboBox)
	{
		comboBox.BeginUpdate();
	}

	private static void EndComboBoxUpdate(ComboBox comboBox)
	{
		comboBox.EndUpdate();
	}

	private static bool IsHiddenCachedListViewItem((ListViewItem listViewItem, bool isHidden) cachedItem)
	{
		return cachedItem.isHidden;
	}

	private static bool IsVisibleCachedListViewItem((ListViewItem listViewItem, bool isHidden) cachedItem)
	{
		return !cachedItem.isHidden;
	}

	private static ListViewItem GetCachedListViewItem((ListViewItem listViewItem, bool isHidden) cachedItem)
	{
		return cachedItem.listViewItem;
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

	private string GetSelectedFilterValue(ListViewItem listViewItem, string columnName)
	{
		int index = fileListView.Columns[columnName].Index;
		ListViewItem.ListViewSubItem subItem = listViewItem.SubItems[index];
		string value = subItem.Text;
		if (columnName == "comment" && subItem.Tag is int fullLength && fullLength != value.Length)
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

	private void SelectAllFiles_Click(object sender, EventArgs e)
	{
		fileListView.BeginUpdate();
		if (fileListView.Items.Count > 1)
		{
			ApplyFileSelectionMode(FileSelectionMode.SelectAll, refreshStatusAllInfo: false);
		}
		else if (fileListView.Items.Count == 1)
		{
			fileListView.Items[0].Selected = true;
		}
		fileListView.EndUpdate();
	}

	private void UnselectAllFiles_Click(object sender, EventArgs e)
	{
		fileListView.BeginUpdate();
		ApplyFileSelectionMode(FileSelectionMode.UnselectAll, refreshStatusAllInfo: false);
		fileListView.EndUpdate();
	}

	private void InvertFileSelection_Click(object sender, EventArgs e)
	{
		fileListView.BeginUpdate();
		int unselectedItemCount = 0;
		ListViewItem singleUnselectedItem = null;
		foreach (ListViewItem listViewItem in fileListView.Items)
		{
			if (!listViewItem.Selected)
			{
				singleUnselectedItem = listViewItem;
				if (++unselectedItemCount > 1)
				{
					break;
				}
			}
		}
		if (unselectedItemCount == 1)
		{
			singleUnselectedItem.Selected = true;
		}
		else
		{
			ApplyFileSelectionMode(FileSelectionMode.Invert, refreshStatusAllInfo: false);
		}
		fileListView.EndUpdate();
	}

	private void RemoveSelectedItemsFromList_Click(object sender, EventArgs e)
	{
		(long selectedDurationMs, long selectedFileSizeBytes) = GetCachedDurationAndFileSize(selectedFilesStatusLabel.Tag);
		(long allDurationMs, long allFileSizeBytes) = GetCachedDurationAndFileSize(totalFilesStatusLabel.Tag);
		bool anyFileMode = FileSettings.IsAnyFileMode();
		ListViewItem[] selectedItems = fileListView.SelectedItems.Cast<ListViewItem>().ToArray();
		var selectedItemSet = new HashSet<ListViewItem>(selectedItems);
		fileListView.BeginUpdate();
		foreach (ListViewItem selectedItem in selectedItems)
		{
			GetListViewItemDurationAndFileSize(selectedItem, out var itemDurationMs, out var itemFileSizeBytes);
			selectedDurationMs -= itemDurationMs;
			selectedFileSizeBytes -= itemFileSizeBytes;
			allDurationMs -= itemDurationMs;
			allFileSizeBytes -= itemFileSizeBytes;
			selectedItem.Remove();
			if (anyFileMode)
			{
				FileSettings.RemoveForAnyFile(selectedItem.Tag as string);
			}
		}
		cachedFileListItems.RemoveAll(itemState => selectedItemSet.Contains(itemState.listViewItem));
		selectedFilesStatusLabel.Tag = (Math.Max(0L, selectedDurationMs), Math.Max(0L, selectedFileSizeBytes));
		totalFilesStatusLabel.Tag = (Math.Max(0L, allDurationMs), Math.Max(0L, allFileSizeBytes));
		RefreshStatusLabelsFromCachedTotals();
		fileListView.EndUpdate();
	}

	private void DeleteSelectedFiles_Click(object sender, EventArgs e)
	{
		if (!DatabaseMapper.ConfirmYesNo(string.Format(Resources.Msg_ConfirmRemoveFiles, fileListView.SelectedItems.Count) + "\n" + BuildSelectedFilePreview()))
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

	private void FileList_ColumnClick(object sender, ColumnClickEventArgs e)
	{
		int? column = default(int?);
		if (e.Column == SortSetting.Column && SortSetting.SortOrder != SortOrder.None && SortSetting.SortOrder != SortOrder.Descending)
		{
			SortSetting.SortOrder = SortOrder.Descending;
		}
		else
		{
			SortSetting.SortOrder = SortOrder.Ascending;
		}
		UpdateFileListSortGlyph(e.Column, SortSetting.SortOrder);
		column = SortSetting.Column;
		if (column.HasValue && SortSetting.Column != e.Column)
		{
			UpdateFileListSortGlyph(SortSetting.Column, SortOrder.None);
		}
		SortSetting.Column = e.Column;
		fileListView.ListViewItemSorter = new ListViewItemNaturalComparer(SortSetting);
		fileListView.Sort();
	}

	private void UpdateFileListSortGlyph(int? columnIndex, SortOrder sortOrder)
	{
		if (columnIndex.HasValue)
		{
			IntPtr headerHandle = NativeMethods.SendMessage(fileListView.Handle, 4127, IntPtr.Zero, IntPtr.Zero);
			NativeMethods.ListViewColumnInfo columnInfo = default(NativeMethods.ListViewColumnInfo);
			IntPtr columnPointer = new IntPtr(columnIndex.Value);
			columnInfo.Mask = 4;
			NativeMethods.SendListViewColumnMessage(headerHandle, 4619, columnPointer, ref columnInfo);
			switch (sortOrder)
			{
			default:
				columnInfo.Format &= -1537;
				break;
			case SortOrder.Descending:
				columnInfo.Format &= -1025;
				columnInfo.Format |= 512;
				break;
			case SortOrder.Ascending:
				columnInfo.Format &= -513;
				columnInfo.Format |= 1024;
				break;
			}
			NativeMethods.SendListViewColumnMessage(headerHandle, 4620, columnPointer, ref columnInfo);
		}
	}

	private void ExitApplication_Click(object sender, EventArgs e)
	{
		Close();
	}

	private void FileList_MouseUp(object sender, MouseEventArgs e)
	{
		if (e.Button == MouseButtons.Right)
		{
			fileListItemContextMenu.Show(fileListView, e.Location);
		}
	}

	private void FileListHeader_RightClick(object sender, ColumnClickEventArgs e)
	{
		customizeColumnsContextMenuItem.Text = Resources.customcolumns;
		fileListHeaderContextMenu.Show(fileListView, fileListView.PointToClient(Cursor.Position));
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
		addCoverMenuItem.Enabled = fileListView.SelectedItems.Count > 0;
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
			if (fileListView.SelectedItems.Count > 1)
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

		if (fileListView.SelectedItems.Count == 1)
		{
			searchCoverFromNetworkMenuItem.Enabled = true;
		}
		if (fileListView.SelectedItems.Count > 1)
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
			if (coverList == null && fileListView.SelectedItems.Count > 1)
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
			DatabaseMapper.ShowErrorMessage($"{Resources.Msg_Readfilefail}, {Resources.Msg_ErrorMessage}: {ex.Message}");
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
		if (fileListView.SelectedItems.Count <= 1)
		{
			return;
		}
		List<string> list = new List<string>();
		foreach (ListViewItem listViewItem in fileListView.SelectedItems)
		{
			list.Add(listViewItem.Tag as string);
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
		if (value is string text && !string.IsNullOrWhiteSpace(text))
		{
			comboBox.Text = text;
		}
		else if (value is int number && number > 0)
		{
			comboBox.Text = number.ToString();
		}
	}

	private async void StartSearchYearLookup(TrackSearchResult searchResult, SimpleProgressDialog progressDialog)
	{
		ReleaseYearSearchTaskContext releaseYearContext = new ReleaseYearSearchTaskContext();
		releaseYearContext.progressDialog = progressDialog;
		releaseYearContext.trackResult = searchResult;
		releaseYearContext.cancellationSource = new CancellationTokenSource();
		releaseYearContext.progressDialog.SetCancelAction(releaseYearContext.Cancel);
		string result = await Task.Run((Func<string>)releaseYearContext.FetchReleaseYear, releaseYearContext.cancellationSource.Token);
		if (!string.IsNullOrWhiteSpace(result))
		{
			yearComboBox.Text = result;
		}
		releaseYearContext.progressDialog.CloseProgressDialog();
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
		SaveCurrentCover(showSaveDialog: true);
	}

	private void OpenCurrentCover_Click(object sender, EventArgs e)
	{
		try
		{
			ConfigDescriptorState.PictureData pictureInfo = (selectedTagState["allpicturedata"] as List<ConfigDescriptorState.PictureData>)[currentCoverIndex];
			if (pictureInfo.MimeType != null && pictureInfo.Width > 0 && pictureInfo.Height > 0)
			{
				string extension = DatabaseMapper.GetImageExtensionForMimeType(pictureInfo.MimeType, "");
				string tempCoverPath = (string)DatabaseMapper.GetPictureCacheDirectory() + "tempcover" + extension;
				File.WriteAllBytes(tempCoverPath, pictureInfo.ImageBytes);
				Process.Start(tempCoverPath);
			}
		}
		catch (System.Exception ex)
		{
			DatabaseMapper.ShowErrorMessage($"{Resources.Msg_ExtractCoverFail}, {Resources.Msg_ErrorMessage}: {ex.Message}");
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
		foreach (ColumnHeader columnHeader in fileListView.Columns)
		{
			CustomColumnsDialog.ColumnHeaderInfo columnHeaderInfo = columnHeader.Tag as CustomColumnsDialog.ColumnHeaderInfo;
			if (columnHeader.Width > 0)
			{
				columnHeaderInfo.width = columnHeader.Width;
			}
			else if (columnHeaderInfo.width == 0)
			{
				columnHeaderInfo.width = DatabaseMapper.ScaleByDpi(100f);
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

		foreach (ColumnHeader columnHeader in fileListView.Columns)
		{
			CustomColumnsDialog.ColumnHeaderInfo columnHeaderInfo = columnHeader.Tag as CustomColumnsDialog.ColumnHeaderInfo;
			columnHeader.Width = columnHeaderInfo.isShow ? columnHeaderInfo.tempWidth : 0;
			columnHeader.DisplayIndex = columnHeaderInfo.displayIndex;
		}
		CustomColumnsDialog.SaveColumnHeaderSettings();
		Settings.Default.Save();
		fileListView.Refresh();
	}

	public static void CompressPictures(List<ConfigDescriptorState.PictureData> pictures, bool useRestoreLimits)
	{
		PictureCompressionOptions pictureCompressionOptions = new PictureCompressionOptions();
		pictureCompressionOptions.maxByteLength = (useRestoreLimits ? 10240000 : (Settings.Default.PictureSizeLimitsKB * 1024));
		pictureCompressionOptions.maxResolution = ((!useRestoreLimits) ? Settings.Default.PictureResolutionLimits : 0);
		pictureCompressionOptions.formatMode = (useRestoreLimits ? "AUTO" : Settings.Default.PictureFormatLimits);
		if (pictureCompressionOptions.maxByteLength <= 0L)
		{
			pictureCompressionOptions.maxByteLength = long.MaxValue;
		}
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
				pictureCompressionWorker.compressionRetrySteps = new Func<bool>[11]
				{
					pictureCompressionWorker.ResizeTo1200Quality75, pictureCompressionWorker.ResizeTo1200Quality55, pictureCompressionWorker.ResizeTo800Quality75, pictureCompressionWorker.ResizeTo800Quality55, pictureCompressionWorker.ResizeTo500Quality75, pictureCompressionWorker.ResizeTo500Quality55, pictureCompressionWorker.ResizeTo500Quality30, pictureCompressionWorker.ResizeTo300Quality50, pictureCompressionWorker.ResizeTo300Quality30, pictureCompressionWorker.ResizeTo100Quality30,
					pictureCompressionWorker.ResizeTo50Quality10
				};
			}
			else
			{
				pictureCompressionWorker.compressionRetrySteps = new Func<bool>[4] { pictureCompressionWorker.ResizeToConfiguredLimitQuality75, pictureCompressionWorker.ResizeToConfiguredLimitQuality50, pictureCompressionWorker.ResizeToConfiguredLimitQuality30, pictureCompressionWorker.ResizeToConfiguredLimitQuality10 };
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

	private int ParseLeadingNumber(string value)
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
		if (fileListView.SelectedItems.Count > 1 && !DatabaseMapper.ConfirmYesNo(string.Format(Resources.Msg_ConfirmSaveTags, fileListView.SelectedItems.Count) + "\n" + BuildSelectedFilePreview()))
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
		if (TagHistoryRepository.UndoTags.Any())
		{
			if (DatabaseMapper.ConfirmYesNo(string.Format(Resources.Msg_ConfirmUndoTags, TagHistoryRepository.UndoTags.Count) + "\n" + TagHistoryRepository.BuildUndoTagsPreview()))
			{
				ProgressDialog progressDialog = new ProgressDialog(taskbarProgress);
				StartUndoSaveTags(progressDialog);
				progressDialog.ShowDialogIfNotDisposed();
			}
		}
		else if (TagHistoryRepository.RenameUndoOperations.Any())
		{
			if (DatabaseMapper.ConfirmYesNo(string.Format(Resources.Msg_ConfirmUndoRename, TagHistoryRepository.RenameUndoOperations.Count) + "\n" + TagHistoryRepository.BuildRenameUndoPreview()))
			{
				ProgressDialog progressDialog = new ProgressDialog(taskbarProgress);
				StartUndoRename(progressDialog);
				progressDialog.ShowDialogIfNotDisposed();
			}
		}
		else
		{
			undoMenuItem.Enabled = false;
			undoToolStripButton.Enabled = false;
		}
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
		if (!DatabaseMapper.ConfirmYesNo(string.Format(Resources.Msg_ConfirmClearTags, fileListView.SelectedItems.Count) + "\n" + BuildSelectedFilePreview()))
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
		StringBuilder stringBuilder = new StringBuilder();
		int lineCount = 0;
		foreach (ListViewItem listViewItem in fileListView.SelectedItems)
		{
			if (lineCount < 10)
			{
				stringBuilder.Append(listViewItem.Text + "\n");
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
		batchContext.progressDialog.AddCancelRequestedHandler(batchContext.Cancel);
		batchContext.renamedCount = 0;
		batchContext.failedCount = 0;
		batchContext.skippedCount = 0;
		batchContext.processedCount = 0;
		batchContext.currentFile = null;
		batchContext.progressDialog.AddProgressUpdateHandler(batchContext.UpdateProgress);
		batchContext.messageLog = new Page();
		await Task.Run((Action)batchContext.ConvertFilenames, batchContext.cancellationSource.Token);
		batchContext.progressDialog.CloseAfterCompletion();
		(string Path, string NewPath, int ListViewIndex)[] completedRenameItems = batchContext.renameItems;
		for (int i = 0; i < completedRenameItems.Length; i++)
		{
			(string Path, string NewPath, int ListViewIndex) renameItem = completedRenameItems[i];
			if (renameItem.NewPath != null)
			{
				fileListView.Items[renameItem.ListViewIndex].Tag = renameItem.NewPath;
			}
		}
		(string, bool) value = default((string, bool));
		if (batchContext.renameItems.Length > 1)
		{
			value.Item1 = string.Format(Resources.Msg_SaveCompleted + "\n" + Resources.Msg_OK_Fail_Skip_Count, batchContext.renamedCount, batchContext.failedCount, batchContext.skippedCount, batchContext.processedCount) + "\n" + batchContext.messageLog.ToString();
		}
		else if (batchContext.renamedCount > 0)
		{
			value.Item1 = Resources.Msg_SaveCompleted + "\n" + batchContext.messageLog.ToString();
		}
		else if (batchContext.skippedCount > 0)
		{
			value.Item1 = Resources.Msg_Skipped + "\n" + batchContext.messageLog.ToString();
		}
		else
		{
			value.Item1 = batchContext.messageLog.ToString();
			value.Item2 = true;
		}
		GC.Collect();
		RefreshSelectedItems(showErrorMessageBox: false, showProgressDialog: true, refreshStatusAllInfo: true, previousMessage: value);
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
		saveTagsContext.progressDialog.AddCancelRequestedHandler(saveTagsContext.Cancel);
		saveTagsContext.savedCount = 0;
		saveTagsContext.failedCount = 0;
		saveTagsContext.skippedCount = 0;
		saveTagsContext.processedCount = 0;
		saveTagsContext.currentFile = null;
		saveTagsContext.progressDialog.AddProgressUpdateHandler(saveTagsContext.UpdateProgress);
		Stopwatch stopWatch = new Stopwatch();
		saveTagsContext.messageLog = new Page();
		stopWatch.Start();
		await Task.Run((Action)saveTagsContext.SaveTags, saveTagsContext.cancellationSource.Token);
		saveTagsContext.progressDialog.CloseAfterCompletion();
		(string, bool) value = default((string, bool));
		if (saveTagsContext.itemsToSave.Length > 1)
		{
			value.Item1 = string.Format(Resources.Msg_SaveCompleted + "\n" + Resources.Msg_OK_Fail_Skip_Count, saveTagsContext.savedCount, saveTagsContext.failedCount, saveTagsContext.skippedCount, saveTagsContext.processedCount) + "\n" + saveTagsContext.messageLog.ToString();
		}
		else if (saveTagsContext.savedCount > 0)
		{
			value.Item1 = Resources.Msg_SaveCompleted + "\n" + saveTagsContext.messageLog.ToString();
		}
		else if (saveTagsContext.skippedCount > 0)
		{
			value.Item1 = Resources.Msg_Skipped + "\n" + saveTagsContext.messageLog.ToString();
		}
		else
		{
			value.Item1 = saveTagsContext.messageLog.ToString();
			value.Item2 = true;
		}
		GC.Collect();
		stopWatch.Stop();
		RefreshSelectedItems(showErrorMessageBox: false, showProgressDialog: true, refreshStatusAllInfo: true, previousMessage: value);
	}

	private async void StartUndoSaveTags(ProgressDialog progressDialog)
	{
		UndoSaveTagsTaskContext undoSaveTagsContext = new UndoSaveTagsTaskContext();
		undoSaveTagsContext.progressDialog = progressDialog;
		undoSaveTagsContext.cancellationSource = new CancellationTokenSource();
		undoSaveTagsContext.progressDialog.AddCancelRequestedHandler(undoSaveTagsContext.Cancel);
		undoSaveTagsContext.undoTagSnapshots = TagHistoryRepository.UndoTags;
		undoSaveTagsContext.restoredCount = 0;
		undoSaveTagsContext.failedCount = 0;
		undoSaveTagsContext.skippedCount = 0;
		undoSaveTagsContext.processedCount = 0;
		undoSaveTagsContext.currentFile = null;
		undoSaveTagsContext.progressDialog.AddProgressUpdateHandler(undoSaveTagsContext.UpdateProgress);
		Stopwatch stopWatch = new Stopwatch();
		undoSaveTagsContext.messageLog = new Page();
		stopWatch.Start();
		await Task.Run((Action)undoSaveTagsContext.RestoreSavedTags, undoSaveTagsContext.cancellationSource.Token);
		List<SelectedListViewItemInfo> refreshedItems = new List<SelectedListViewItemInfo>();
		undoSaveTagsContext.processedCount = 0;
		while (undoSaveTagsContext.processedCount < cachedFileListItems.Count)
		{
			UndoSaveTagsListItemMatcher listItemMatcher = new UndoSaveTagsListItemMatcher();
			listItemMatcher.listViewItem = cachedFileListItems[undoSaveTagsContext.processedCount].listViewItem;
			if (undoSaveTagsContext.undoTagSnapshots.Find(listItemMatcher.MatchesSnapshotPath) != null)
			{
				refreshedItems.Add(new SelectedListViewItemInfo
				{
					Index = undoSaveTagsContext.processedCount,
					FilePath = (listItemMatcher.listViewItem.Tag as string)
				});
			}
			undoSaveTagsContext.processedCount++;
		}
		undoSaveTagsContext.progressDialog.CloseAfterCompletion();
		(string, bool) value = default((string, bool));
		if (undoSaveTagsContext.undoTagSnapshots.Count > 1)
		{
			value.Item1 = string.Format(Resources.Msg_UndoCompleted + "\n" + Resources.Msg_OK_Fail_Skip_Count, undoSaveTagsContext.restoredCount, undoSaveTagsContext.failedCount, undoSaveTagsContext.skippedCount, undoSaveTagsContext.processedCount) + "\n" + undoSaveTagsContext.messageLog.ToString();
		}
		else if (undoSaveTagsContext.restoredCount > 0)
		{
			value.Item1 = Resources.Msg_UndoCompleted + "\n" + undoSaveTagsContext.messageLog.ToString();
		}
		else
		{
			value.Item1 = undoSaveTagsContext.messageLog.ToString();
			value.Item2 = true;
		}
		TagHistoryRepository.ClearUndoState();
		stopWatch.Stop();
		RefreshItemsWithOptionalProgressDialog(refreshedItems.ToArray(), showErrorMessageBox: false, showProgressDialog: true, refreshStatusAllInfo: true, previousMessage: value, listForMirror: true);
	}

	private async void StartUndoRename(ProgressDialog progressDialog)
	{
		UndoRenameTaskContext undoRenameContext = new UndoRenameTaskContext();
		undoRenameContext.progressDialog = progressDialog;
		undoRenameContext.owner = this;
		undoRenameContext.cancellationSource = new CancellationTokenSource();
		undoRenameContext.progressDialog.AddCancelRequestedHandler(undoRenameContext.Cancel);
		undoRenameContext.renameUndoOperations = TagHistoryRepository.RenameUndoOperations;
		undoRenameContext.successCount = 0;
		undoRenameContext.failedCount = 0;
		undoRenameContext.skippedCount = 0;
		undoRenameContext.processedCount = 0;
		undoRenameContext.currentFile = null;
		undoRenameContext.progressDialog.AddProgressUpdateHandler(undoRenameContext.UpdateProgress);
		Stopwatch stopWatch = new Stopwatch();
		undoRenameContext.errorLog = new Page();
		stopWatch.Start();
		await Task.Run((Action)undoRenameContext.UndoRenames, undoRenameContext.cancellationSource.Token);
		List<SelectedListViewItemInfo> list = new List<SelectedListViewItemInfo>();
		undoRenameContext.processedCount = 0;
		while (undoRenameContext.processedCount < cachedFileListItems.Count)
		{
			RenameUndoListItemMatcher listItemMatcher = new RenameUndoListItemMatcher();
			listItemMatcher.ListViewItem = cachedFileListItems[undoRenameContext.processedCount].listViewItem;
			(string oldPath, string newPath, bool failed) operation = undoRenameContext.renameUndoOperations.Find(listItemMatcher.MatchesCurrentPath);
			if (!string.IsNullOrWhiteSpace(operation.oldPath))
			{
				if (!operation.failed)
				{
					listItemMatcher.ListViewItem.Tag = operation.oldPath;
				}
				list.Add(new SelectedListViewItemInfo
				{
					Index = undoRenameContext.processedCount,
					FilePath = (listItemMatcher.ListViewItem.Tag as string)
				});
			}
			undoRenameContext.processedCount++;
		}
		undoRenameContext.progressDialog.CloseAfterCompletion();
		(string, bool) value = default((string, bool));
		if (undoRenameContext.renameUndoOperations.Count > 1)
		{
			value.Item1 = string.Format(Resources.Msg_UndoCompleted + "\n" + Resources.Msg_OK_Fail_Skip_Count, undoRenameContext.successCount, undoRenameContext.failedCount, undoRenameContext.skippedCount, undoRenameContext.processedCount) + "\n" + undoRenameContext.errorLog.ToString();
		}
		else if (undoRenameContext.successCount > 0)
		{
			value.Item1 = Resources.Msg_UndoCompleted + "\n" + undoRenameContext.errorLog.ToString();
		}
		else
		{
			value.Item1 = undoRenameContext.errorLog.ToString();
			value.Item2 = true;
		}
		TagHistoryRepository.ClearUndoState();
		stopWatch.Stop();
		RefreshItemsWithOptionalProgressDialog(list.ToArray(), showErrorMessageBox: false, showProgressDialog: true, refreshStatusAllInfo: true, previousMessage: value, listForMirror: true);
	}

	private async void StartClearTags(SelectedListViewItemInfo[] itemInfos, ProgressDialog progressDialog, bool canCancelFileReadonly)
	{
		ClearTagsTaskContext clearTagsContext = new ClearTagsTaskContext();
		clearTagsContext.progressDialog = progressDialog;
		clearTagsContext.itemsToClear = itemInfos;
		clearTagsContext.canCancelFileReadonly = canCancelFileReadonly;
		clearTagsContext.cancellationSource = new CancellationTokenSource();
		clearTagsContext.progressDialog.AddCancelRequestedHandler(clearTagsContext.Cancel);
		clearTagsContext.successCount = 0;
		clearTagsContext.failedCount = 0;
		clearTagsContext.processedCount = 0;
		clearTagsContext.currentFile = null;
		clearTagsContext.progressDialog.AddProgressUpdateHandler(clearTagsContext.UpdateProgress);
		clearTagsContext.errorLog = new Page();
		await Task.Run((Action)clearTagsContext.ClearTags, clearTagsContext.cancellationSource.Token);
		clearTagsContext.progressDialog.CloseAfterCompletion();
		(string, bool) value = default((string, bool));
		if (clearTagsContext.itemsToClear.Length > 1)
		{
			value.Item1 = string.Format(Resources.Msg_CleartagsCompleted + "\n" + Resources.Msg_OK_Fail_Count, clearTagsContext.successCount, clearTagsContext.failedCount, clearTagsContext.processedCount) + "\n" + clearTagsContext.errorLog.ToString();
		}
		else if (clearTagsContext.successCount > 0)
		{
			value.Item1 = Resources.Msg_CleartagsCompleted;
		}
		else
		{
			value.Item1 = clearTagsContext.errorLog.ToString();
			value.Item2 = true;
		}
		GC.Collect();
		RefreshSelectedItems(showErrorMessageBox: false, showProgressDialog: true, refreshStatusAllInfo: true, previousMessage: value);
	}

	private void StartBatchLyricsOperation(string lyricsOperationResourceKey)
	{
		int count = fileListView.SelectedItems.Count;
		if (count != 0 && DatabaseMapper.ConfirmYesNo(string.Format(Resources.Msg_ConfirmSaveTags, count) + "(" + localizedResources.GetString(lyricsOperationResourceKey) + ")\n" + BuildSelectedFilePreview()))
		{
			Dictionary<string, object> comp = new Dictionary<string, object> { { "lyrics_handle", lyricsOperationResourceKey } };
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

	private async void StartRemoveFiles(SelectedListViewItemInfo[] itemInfos, ProgressDialog progressDialog)
	{
		DeleteFilesTaskContext deleteFilesContext = new DeleteFilesTaskContext();
		deleteFilesContext.progressDialog = progressDialog;
		deleteFilesContext.itemsToDelete = itemInfos;
		deleteFilesContext.owner = this;
		deleteFilesContext.cancellationSource = new CancellationTokenSource();
		deleteFilesContext.progressDialog.AddCancelRequestedHandler(deleteFilesContext.Cancel);
		deleteFilesContext.processedCount = 0;
		deleteFilesContext.currentFile = null;
		deleteFilesContext.progressDialog.AddProgressUpdateHandler(deleteFilesContext.UpdateProgress);
		deleteFilesContext.deletedCount = 0;
		deleteFilesContext.failedCount = 0;
		deleteFilesContext.errorLog = new Page();
		await Task.Run((Action)deleteFilesContext.DeleteFiles, deleteFilesContext.cancellationSource.Token);
		deleteFilesContext.progressDialog.CloseAfterCompletion();
		BeginInvoke(new Action(deleteFilesContext.ShowCompletionResult));
	}

	private async void StartSaveLrcFiles(SelectedListViewItemInfo[] itemInfos, ProgressDialog progressDialog)
	{
		SaveLrcFilesTaskContext saveLrcContext = new SaveLrcFilesTaskContext();
		saveLrcContext.progressDialog = progressDialog;
		saveLrcContext.itemsToSave = itemInfos;
		saveLrcContext.cancellationSource = new CancellationTokenSource();
		saveLrcContext.progressDialog.AddCancelRequestedHandler(saveLrcContext.Cancel);
		saveLrcContext.processedCount = 0;
		saveLrcContext.currentFile = null;
		saveLrcContext.progressDialog.AddProgressUpdateHandler(saveLrcContext.UpdateProgress);
		saveLrcContext.savedCount = 0;
		saveLrcContext.failedCount = 0;
		saveLrcContext.skippedCount = 0;
		saveLrcContext.errorLog = new Page();
		await Task.Run((Action)saveLrcContext.SaveLrcFiles, saveLrcContext.cancellationSource.Token);
		saveLrcContext.progressDialog.CloseAfterCompletion();
		BeginInvoke(new Action(saveLrcContext.ShowCompletionResult));
	}

	private async void StartExtractCovers(SelectedListViewItemInfo[] itemInfos, ProgressDialog progressDialog)
	{
		ExtractCoversTaskContext extractCoversContext = new ExtractCoversTaskContext();
		extractCoversContext.progressDialog = progressDialog;
		extractCoversContext.itemsToExtract = itemInfos;
		extractCoversContext.cancellationSource = new CancellationTokenSource();
		extractCoversContext.progressDialog.AddCancelRequestedHandler(extractCoversContext.Cancel);
		extractCoversContext.processedCount = 0;
		extractCoversContext.currentFile = null;
		extractCoversContext.progressDialog.AddProgressUpdateHandler(extractCoversContext.UpdateProgress);
		extractCoversContext.extractedCount = 0;
		extractCoversContext.failedCount = 0;
		extractCoversContext.skippedCount = 0;
		extractCoversContext.errorLog = new Page();
		await Task.Run((Action)extractCoversContext.ExtractCovers, extractCoversContext.cancellationSource.Token);
		extractCoversContext.progressDialog.CloseAfterCompletion();
		BeginInvoke(new Action(extractCoversContext.ShowCompletionResult));
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

	private void ConvertTagsTraditionalToSimplified_Click(object sender, EventArgs e)
	{
		foreach (ComboBox comboBox in tagComboBoxes.Values)
		{
			comboBox.Text = ChineseTextConverter.TraditionalToSimplified().ConvertText(comboBox.Text);
		}
	}

	private void ConvertTagsSimplifiedToTraditional_Click(object sender, EventArgs e)
	{
		foreach (ComboBox comboBox in tagComboBoxes.Values)
		{
			comboBox.Text = ChineseTextConverter.SimplifiedToTraditional().ConvertText(comboBox.Text);
		}
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
		if (fileListView.SelectedItems.Count == 1)
		{
			if (selectedTagState == null || !selectedTagState.IsLoadedSuccessfully() || !File.Exists(selectedTagState.GetFilePath()) || string.IsNullOrWhiteSpace(lyricsComboBox.Text))
			{
				DatabaseMapper.ShowErrorMessage(Resources.Msg_LyricNotFound);
				return;
			}
			try
			{
				LyricSaveFileDialog lyricSaveDialog = new LyricSaveFileDialog();
				FileInfo fileInfo = new FileInfo(selectedTagState.GetFilePath());
				lyricSaveDialog.InitialDirectory = DatabaseMapper.GetLyricSaveDirectory(fileInfo.FullName);
				lyricSaveDialog.FileName = DatabaseMapper.BuildLyricFileName(fileInfo.FullName, selectedTagState);
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
				DatabaseMapper.ShowInformationMessage(string.Format(Resources.Msg_FilesSavedInSpecPath, defaultLrcPath));
			}
			catch (System.Exception ex)
			{
				DatabaseMapper.ShowErrorMessage(ex.Message);
			}
			return;
		}
		if (fileListView.SelectedItems.Count > 1 && DatabaseMapper.ConfirmYesNo(string.Format(Resources.Msg_ConfirmSaveLrcFiles, fileListView.SelectedItems.Count) + "\n" + BuildSelectedFilePreview()))
		{
			SelectedListViewItemInfo[] selectedItems = CollectSelectedListViewItemInfos();
			ProgressDialog progressDialog = new ProgressDialog(taskbarProgress);
			StartSaveLrcFiles(selectedItems, progressDialog);
			progressDialog.ShowDialogIfNotDisposed();
		}
	}

	private void ExtractCovers_Click(object sender, EventArgs e)
	{
		if (fileListView.SelectedItems.Count <= 1)
		{
			if (fileListView.SelectedItems.Count == 1)
			{
				SaveCurrentCover(showSaveDialog: true);
			}
		}
		else if (DatabaseMapper.ConfirmYesNo(string.Format(Resources.Msg_ConfirmExtractCovers, fileListView.SelectedItems.Count) + "\n" + BuildSelectedFilePreview()))
		{
			SelectedListViewItemInfo[] selectedItems = CollectSelectedListViewItemInfos();
			ProgressDialog progressDialog = new ProgressDialog(taskbarProgress);
			StartExtractCovers(selectedItems, progressDialog);
			progressDialog.ShowDialogIfNotDisposed();
		}
	}

	private void SaveCurrentCover(bool showSaveDialog)
	{
		if (selectedTagState == null || !selectedTagState.IsLoadedSuccessfully())
		{
			DatabaseMapper.ShowErrorMessage(Resources.Msg_CoverNotFound);
			return;
		}
		List<ConfigDescriptorState.PictureData> coverList = selectedTagState["allpicturedata"] as List<ConfigDescriptorState.PictureData>;
		if (coverList == null || currentCoverIndex >= coverList.Count)
		{
			DatabaseMapper.ShowErrorMessage(Resources.Msg_CoverNotFound);
			return;
		}
		try
		{
			ConfigDescriptorState.PictureData selectedCover = coverList[currentCoverIndex];
			if (selectedCover.MimeType == null || selectedCover.Width <= 0 || selectedCover.Height <= 0)
			{
				DatabaseMapper.ShowErrorMessage(Resources.Msg_CoverNotFound);
				return;
			}
			string directoryName = Path.GetDirectoryName(selectedTagState.GetFilePath());
			string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(selectedTagState.GetFilePath());
			string defaultCoverPath = directoryName + "\\" + fileNameWithoutExtension + DatabaseMapper.GetImageExtensionForMimeType(selectedCover.MimeType, ".jpg");
			if (!showSaveDialog && !File.Exists(defaultCoverPath))
			{
				File.WriteAllBytes(defaultCoverPath, selectedCover.ImageBytes);
				DatabaseMapper.ShowInformationMessage(Resources.Msg_FilesSavedInLocalDir);
				return;
			}

			string filter = DatabaseMapper.GetImageFileDialogFilterForMimeType(selectedCover.MimeType);
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
			DatabaseMapper.ShowErrorMessage($"{Resources.Msg_ExtractCoverFail}, {Resources.Msg_ErrorMessage}: {ex.Message}");
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
		int count = fileListView.SelectedItems.Count;
		if (count != 0 && DatabaseMapper.ConfirmYesNo(string.Format(Resources.Msg_ConfirmSaveTags, count) + "(" + localizedResources.GetString("menuStrip1.Batch.TagsChtToChs") + ")\n" + BuildSelectedFilePreview()))
		{
			Dictionary<string, object> comp = new Dictionary<string, object> { { "chscht_handle", false } };
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

	private void ConvertSelectedTagsSimplifiedToTraditional_Click(object sender, EventArgs e)
	{
		int count = fileListView.SelectedItems.Count;
		if (count == 0)
		{
			return;
		}
		if (!DatabaseMapper.ConfirmYesNo(string.Format(Resources.Msg_ConfirmSaveTags, count) + "(" + localizedResources.GetString("menuStrip1.Batch.TagsChsToCht") + ")\n" + BuildSelectedFilePreview()))
		{
			return;
		}
		Dictionary<string, object> comp = new Dictionary<string, object> { { "chscht_handle", true } };
		ProgressDialog progressDialog = new ProgressDialog(taskbarProgress);
		SelectedListViewItemInfo[] selectedItems = CollectSelectedListViewItemInfos();
		bool? canCancelReadOnly = ConfirmReadOnlyFileHandling(selectedItems);
		if (canCancelReadOnly.HasValue)
		{
			StartCommonSaveTags(selectedItems, progressDialog, comp, canCancelReadOnly == true);
			progressDialog.ShowDialogIfNotDisposed();
		}
	}

	private void ConvertSelectedFilenamesTraditionalToSimplified_Click(object sender, EventArgs e)
	{
		int count = fileListView.SelectedItems.Count;
		if (count != 0 && DatabaseMapper.ConfirmYesNo(string.Format(Resources.Msg_ConfirmRenameFiles, count) + "(" + localizedResources.GetString("menuStrip1.Batch.FilenameChtToChs") + ")\n" + BuildSelectedFilePreview()))
		{
			ProgressDialog progressDialog = new ProgressDialog(taskbarProgress);
				(string Path, string NewPath, int ListViewIndex)[] itemInfos = CollectSelectedListViewItemInfos().Select(CreateRenameItemInfo).ToArray();
			StartRenameFiles(itemInfos, progressDialog, isChsToCht: false);
			progressDialog.ShowDialogIfNotDisposed();
		}
	}

	private void ConvertSelectedFilenamesSimplifiedToTraditional_Click(object sender, EventArgs e)
	{
		int count = fileListView.SelectedItems.Count;
		if (count != 0 && DatabaseMapper.ConfirmYesNo(string.Format(Resources.Msg_ConfirmRenameFiles, count) + "(" + localizedResources.GetString("menuStrip1.Batch.FilenameChsToCht") + ")\n" + BuildSelectedFilePreview()))
		{
			ProgressDialog progressDialog = new ProgressDialog(taskbarProgress);
				(string Path, string NewPath, int ListViewIndex)[] itemInfos = CollectSelectedListViewItemInfos().Select(CreateRenameItemInfo).ToArray();
			StartRenameFiles(itemInfos, progressDialog, isChsToCht: true);
			progressDialog.ShowDialogIfNotDisposed();
		}
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
		Settings.Default.Save();
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
				if (MainFormPosSizeInfo.Size.HasValue)
				{
					base.Size = MainFormPosSizeInfo.Size.Value;
				}
				if (MainFormPosSizeInfo.Location.HasValue)
				{
					base.Location = MainFormPosSizeInfo.Location.Value;
				}

				Rectangle workingArea = SystemInformation.WorkingArea;
				Size maximumVisibleLocation = new Size(Math.Min(Math.Max(workingArea.Width - 20, 20), workingArea.Width), workingArea.Height);
				base.Size = new Size(Math.Min(base.Size.Width, workingArea.Width), Math.Min(base.Size.Height, workingArea.Height));
				if (base.Location.X > maximumVisibleLocation.Width)
				{
					base.Location = new Point(maximumVisibleLocation.Width, base.Location.Y);
				}
				if (base.Location.Y > maximumVisibleLocation.Height)
				{
					base.Location = new Point(base.Location.X, maximumVisibleLocation.Height);
				}
				if (base.Location.X + base.Size.Width < 20 || base.Location.Y + base.Size.Height < 20)
				{
					base.Location = new Point(0, 0);
				}
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
			fileListView.ListViewItemSorter = new ListViewItemNaturalComparer(SortSetting);
			fileListView.Sort();
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
	}

	protected override void OnShown(EventArgs e)
	{
		base.OnShown(e);
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
			DatabaseMapper.ShowErrorMessage(string.Format(Resources.Msg_InitDatabaseFail, databaseErrorMessage));
			Close();
			return;
		}

		if (File.Exists(AppSettingData.AppSettingDataPath))
		{
			ProgressDialog progressDialog = null;
			try
			{
				using FileStream serializationStream = new FileStream(AppSettingData.AppSettingDataPath, FileMode.Open);
				AppSettingData appSettingData = new BinaryFormatter().Deserialize(serializationStream) as AppSettingData;
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
				Console.WriteLine("open appsettingdatapath fail " + ex.Message);
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
		appSettingsSaveTask.owner = this;
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
		await Task.Run((Action)appSettingsSaveTask.SaveSettings);
		progressDialog.CloseProgressDialog();
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

	private static bool IsEnabledConfiguredDirectory(ListViewFileSettingFileInfo directoryInfo)
	{
		return !directoryInfo.Disabled && !directoryInfo.IsAnyFile();
	}

	private void BeginRenameSelectedFile_Click(object sender, EventArgs e)
	{
		if (fileListView.SelectedItems.Count > 0)
		{
			fileListView.SelectedItems[0].BeginEdit();
		}
	}

	private void RevealSelectedFileInExplorer_Click(object sender, EventArgs e)
	{
		if (fileListView.SelectedItems.Count > 0)
		{
			DatabaseMapper.ShowInExplorer(fileListView.SelectedItems[0].Tag as string);
		}
	}

	private void FileList_BeforeLabelEdit(object sender, LabelEditEventArgs e)
	{
		NativeMethods.SendMessage(NativeMethods.SendMessage(fileListView.Handle, 4120, IntPtr.Zero, IntPtr.Zero), 177, IntPtr.Zero, (IntPtr)Path.GetFileNameWithoutExtension(fileListView.Items[e.Item].Text).Length);
	}

	private void FileList_AfterLabelEdit(object sender, LabelEditEventArgs e)
	{
		ListViewItem listViewItem = fileListView.Items[e.Item];
		FileListLabelEditContext editContext = new FileListLabelEditContext
		{
			OriginalPath = listViewItem.Tag as string,
			RequestedFileName = e.Label
		};
		FileInfo fileInfo = new FileInfo(editContext.OriginalPath);
		if (!fileInfo.Exists)
		{
			e.CancelEdit = true;
			BeginInvoke(new Action(editContext.ShowFileNotFoundMessage));
			return;
		}

		try
		{
			if (string.IsNullOrWhiteSpace(editContext.RequestedFileName)
				|| string.IsNullOrWhiteSpace(Path.GetFileNameWithoutExtension(editContext.RequestedFileName))
				|| fileInfo.Name == editContext.RequestedFileName)
			{
				e.CancelEdit = true;
				return;
			}

			editContext.NewPath = fileInfo.DirectoryName + "\\" + editContext.RequestedFileName;
			fileInfo.MoveTo(editContext.NewPath);
			listViewItem.Tag = editContext.NewPath;
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
			e.CancelEdit = true;
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

		fileListView.SetDoubleBuffered(enabled: false);
		fileFilterStatusStrip.Location = new Point(0, fileListHeight);
		fileSummaryStatusStrip.Location = new Point(0, fileListHeight + fileFilterStatusStrip.Height);
		fileListView.Height = fileListHeight;
		fileListView.SetDoubleBuffered(enabled: true);
	}

	private void FilterBar_SizeChanged(object sender, EventArgs e)
	{
		filterTextBox.Width = fileFilterStatusStrip.Width - filterStatusLabel.Width - filterTypeDropDownButton.Width - DatabaseMapper.ScaleByDpi(4f);
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

	private static string NormalizeDroppedFilePath(string path)
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

	private void InitializeComponent()
	{
		components = new System.ComponentModel.Container();
		ComponentResourceManager componentResourceManager = new ComponentResourceManager(typeof(StateFieldInstance));
		mainMenuStrip = new MenuStrip();
		fileMenuItem = new ToolStripMenuItem();
		changeDirectoryMenuItem = new ToolStripMenuItem();
		addDirectoryMenuItem = new ToolStripMenuItem();
		manageDirectoriesMenuItem = new ToolStripMenuItem();
		fileMenuTagActionsSeparator = new ToolStripSeparator();
		saveTagsMenuItem = new ToolStripMenuItem();
		removeTagsMenuItem = new ToolStripMenuItem();
		readTagsMenuItem = new ToolStripMenuItem();
		characterSetMenuItem = new ToolStripMenuItem();
		chineseConversionMenuItem = new ToolStripMenuItem();
		convertTagsTraditionalToSimplifiedMenuItem = new ToolStripMenuItem();
		convertTagsSimplifiedToTraditionalMenuItem = new ToolStripMenuItem();
		tagHistoryMenuItem = new ToolStripMenuItem();
		fileMenuExitSeparator = new ToolStripSeparator();
		exitMenuItem = new ToolStripMenuItem();
		editMenuItem = new ToolStripMenuItem();
		selectAllFilesMenuItem = new ToolStripMenuItem();
		unselectAllFilesMenuItem = new ToolStripMenuItem();
		invertFileSelectionMenuItem = new ToolStripMenuItem();
		editSelectionSeparator = new ToolStripSeparator();
		undoMenuItem = new ToolStripMenuItem();
		renameFileMenuItem = new ToolStripMenuItem();
		removeItemsMenuItem = new ToolStripMenuItem();
		removeFilesMenuItem = new ToolStripMenuItem();
		openDirectoryMenuItem = new ToolStripMenuItem();
		viewMenuItem = new ToolStripMenuItem();
		refreshMenuItem = new ToolStripMenuItem();
		customizeColumnsMenuItem = new ToolStripMenuItem();
		tagSourcesMenuItem = new ToolStripMenuItem();
		coverSourceMenuItem = new ToolStripMenuItem();
		defaultCoverSourceMenuItem = new ToolStripMenuItem();
		lyricSourceMenuItem = new ToolStripMenuItem();
		defaultLyricSourceMenuItem = new ToolStripMenuItem();
		combinedTagSourceMenuItem = new ToolStripMenuItem();
		defaultCombinedTagSourceMenuItem = new ToolStripMenuItem();
		batchMenuItem = new ToolStripMenuItem();
		batchAutoMatchTagsMenuItem = new ToolStripMenuItem();
		batchLyricMenuItem = new ToolStripMenuItem();
		reformatLyricTimeTagsMenuItem = new ToolStripMenuItem();
		removeLyricTimeTagsMenuItem = new ToolStripMenuItem();
		deleteBlankLyricLinesMenuItem = new ToolStripMenuItem();
		deleteLyricHeaderTagsMenuItem = new ToolStripMenuItem();
		saveLyricsMenuItem = new ToolStripMenuItem();
		importLrcFilesMenuItem = new ToolStripMenuItem();
		batchExtractCoverMenuItem = new ToolStripMenuItem();
		batchFilenameRelatedMenuItem = new ToolStripMenuItem();
		batchChineseConversionMenuItem = new ToolStripMenuItem();
		batchTagsTraditionalToSimplifiedMenuItem = new ToolStripMenuItem();
		batchTagsSimplifiedToTraditionalMenuItem = new ToolStripMenuItem();
		batchFilenameTraditionalToSimplifiedMenuItem = new ToolStripMenuItem();
		batchFilenameSimplifiedToTraditionalMenuItem = new ToolStripMenuItem();
		toolsMenuItem = new ToolStripMenuItem();
		optionsMenuItem = new ToolStripMenuItem();
		languageMenuItem = new ToolStripMenuItem();
		englishLanguageMenuItem = new ToolStripMenuItem();
		simplifiedChineseLanguageMenuItem = new ToolStripMenuItem();
		traditionalChineseLanguageMenuItem = new ToolStripMenuItem();
		helpMenuItem = new ToolStripMenuItem();
		checkForUpdatesMenuItem = new ToolStripMenuItem();
		musicTagWebsiteMenuItem = new ToolStripMenuItem();
		aboutMenuItem = new ToolStripMenuItem();
		mainToolStrip = new ToolStrip();
		changeDirectoryToolStripButton = new ToolStripButton();
		addDirectoriesToolStripButton = new ToolStripButton();
		manageDirectoriesToolStripButton = new ToolStripButton();
		directoryToolbarSeparator = new ToolStripSeparator();
		saveTagsToolStripButton = new ToolStripButton();
		removeTagsToolStripButton = new ToolStripButton();
		undoToolStripButton = new ToolStripButton();
		readTagsToolStripButton = new ToolStripButton();
		characterSetToolStripButton = new ToolStripButton();
		chineseConversionToolStripDropDownButton = new ToolStripDropDownButton();
		convertTagsTraditionalToSimplifiedToolStripMenuItem = new ToolStripMenuItem();
		convertTagsSimplifiedToTraditionalToolStripMenuItem = new ToolStripMenuItem();
		tagHistoryToolStripButton = new ToolStripButton();
		tagActionsToolbarSeparator = new ToolStripSeparator();
		selectAllFilesToolStripButton = new ToolStripButton();
		unselectAllFilesToolStripButton = new ToolStripButton();
		selectionToolbarSeparator = new ToolStripSeparator();
		refreshToolStripButton = new ToolStripButton();
		sourceToolbarSeparator = new ToolStripSeparator();
		coverSourceToolStripSplitButton = new ToolStripSplitButton();
		lyricSourceToolStripSplitButton = new ToolStripSplitButton();
		combinedTagSourceToolStripSplitButton = new ToolStripSplitButton();
		batchToolbarStartSeparator = new ToolStripSeparator();
		batchAutoMatchTagsToolStripButton = new ToolStripButton();
		batchSaveAsLrcToolStripSplitButton = new ToolStripSplitButton();
		reformatLyricTimeTagsToolStripMenuItem = new ToolStripMenuItem();
		removeLyricTimeTagsToolStripMenuItem = new ToolStripMenuItem();
		deleteBlankLyricLinesToolStripMenuItem = new ToolStripMenuItem();
		deleteLyricHeaderTagsToolStripMenuItem = new ToolStripMenuItem();
		importLrcFilesToolStripMenuItem = new ToolStripMenuItem();
		batchExtractCoverToolStripButton = new ToolStripButton();
		batchChineseConversionToolStripDropDownButton = new ToolStripDropDownButton();
		batchTagsTraditionalToSimplifiedToolStripMenuItem = new ToolStripMenuItem();
		batchTagsSimplifiedToTraditionalToolStripMenuItem = new ToolStripMenuItem();
		batchFilenameTraditionalToSimplifiedToolStripMenuItem = new ToolStripMenuItem();
		batchFilenameSimplifiedToTraditionalToolStripMenuItem = new ToolStripMenuItem();
		batchFilenameRelatedToolStripButton = new ToolStripButton();
		batchToolbarOptionsSeparator = new ToolStripSeparator();
		optionsToolStripButton = new ToolStripButton();
		localCoverFileDialog = new OpenFileDialog();
		coverContextMenu = new ContextMenuStrip(components);
		addCoverMenuItem = new ToolStripMenuItem();
		chooseLocalCoverMenuItem = new ToolStripMenuItem();
		searchCoverFromNetworkMenuItem = new ToolStripMenuItem();
		chooseCoverFromTagsMenuItem = new ToolStripMenuItem();
		changeCoverResolutionMenuItem = new ToolStripMenuItem();
		removeCoverMenuItem = new ToolStripMenuItem();
		openCoverMenuItem = new ToolStripMenuItem();
		extractCoverMenuItem = new ToolStripMenuItem();
		coverContextMenuSeparator = new ToolStripSeparator();
		coverTypeMenuItem = new ToolStripMenuItem();
		extractCoverSaveFileDialog = new SaveFileDialog();
		controlToolTip = new ToolTip(components);
		titleEncodingButton = new Button();
		artistEncodingButton = new Button();
		albumEncodingButton = new Button();
		yearEncodingButton = new Button();
		trackEncodingButton = new Button();
		discEncodingButton = new Button();
		genreEncodingButton = new Button();
		albumArtistEncodingButton = new Button();
		composerEncodingButton = new Button();
		lyricistEncodingButton = new Button();
		commentEncodingButton = new Button();
		editLyricsButton = new Button();
		lyricsEncodingButton = new Button();
		overwriteCoverCheckBox = new CheckBox();
		fileListHeaderContextMenu = new ContextMenuStrip(components);
		customizeColumnsContextMenuItem = new ToolStripMenuItem();
		fileListItemContextMenu = new ContextMenuStrip(components);
		saveTagsContextMenuItem = new ToolStripMenuItem();
		removeTagsContextMenuItem = new ToolStripMenuItem();
		readTagsContextMenuItem = new ToolStripMenuItem();
		characterSetContextMenuItem = new ToolStripMenuItem();
		tagHistoryContextMenuItem = new ToolStripMenuItem();
		fileListItemContextSeparator = new ToolStripSeparator();
		renameFileContextMenuItem = new ToolStripMenuItem();
		removeItemsContextMenuItem = new ToolStripMenuItem();
		removeFilesContextMenuItem = new ToolStripMenuItem();
		openDirectoryContextMenuItem = new ToolStripMenuItem();
		selectionStatusUpdateTimer = new System.Windows.Forms.Timer(components);
		fileListStatusTimer = new System.Windows.Forms.Timer(components);
		filterInputTimer = new System.Windows.Forms.Timer(components);
		renamedFilesRefreshTimer = new System.Windows.Forms.Timer(components);
		comboBoxSelectionResetTimer = new System.Windows.Forms.Timer(components);
		notifyIcon = new NotifyIcon(components);
		notifyContextMenu = new ContextMenuStrip(components);
		notifyExitMenuItem = new ToolStripMenuItem();
		mainSplitContainer = new DoubleBufferedSplitContainer();
		tagEditorPanel = new FlowLayoutPanel();
		titleLabel = new Label();
		titleRowPanel = new FlowLayoutPanel();
		titleComboBox = new ComboBox();
		artistLabel = new Label();
		artistRowPanel = new FlowLayoutPanel();
		artistComboBox = new ComboBox();
		albumLabel = new Label();
		albumRowPanel = new FlowLayoutPanel();
		albumComboBox = new ComboBox();
		yearLabel = new Label();
		yearRowPanel = new FlowLayoutPanel();
		yearComboBox = new ComboBox();
		trackDiscGroupPanel = new FlowLayoutPanel();
		trackColumnPanel = new FlowLayoutPanel();
		trackLabel = new Label();
		trackRowPanel = new FlowLayoutPanel();
		trackComboBox = new ComboBox();
		discColumnPanel = new FlowLayoutPanel();
		discLabel = new Label();
		discRowPanel = new FlowLayoutPanel();
		discComboBox = new ComboBox();
		genreLabel = new Label();
		genreRowPanel = new FlowLayoutPanel();
		genreComboBox = new ComboBox();
		albumArtistLabel = new Label();
		albumArtistRowPanel = new FlowLayoutPanel();
		albumArtistComboBox = new ComboBox();
		composerLabel = new Label();
		composerRowPanel = new FlowLayoutPanel();
		composerComboBox = new ComboBox();
		lyricistLabel = new Label();
		lyricistRowPanel = new FlowLayoutPanel();
		lyricistComboBox = new ComboBox();
		commentLabel = new Label();
		commentRowPanel = new FlowLayoutPanel();
		commentComboBox = new ComboBox();
		lyricsLabel = new Label();
		lyricsRowPanel = new FlowLayoutPanel();
		lyricsComboBox = new ComboBox();
		coverPanel = new FlowLayoutPanel();
		coverPictureBox = new PictureBox();
		statusLabelsPanel = new FlowLayoutPanel();
		coverMimeTypeLabel = new Label();
		coverDimensionsLabel = new Label();
		coverFileSizeLabel = new Label();
		coverPictureTypeLabel = new Label();
		coverNavigationPanel = new FlowLayoutPanel();
		previousCoverButton = new Button();
		coverIndexLabel = new Label();
		nextCoverButton = new Button();
		tagEditorBottomSpacerPanel = new Panel();
		fileListView = new HeaderAwareListView();
		fileFilterStatusStrip = new StatusStrip();
		filterStatusLabel = new ToolStripStatusLabel();
		filterTextBox = new ToolStripTextBox();
		filterTypeDropDownButton = new ToolStripDropDownButton();
		fileSummaryStatusStrip = new StatusStrip();
		selectedFilesStatusLabel = new ToolStripStatusLabel();
		fileSummaryStatusSeparator = new ToolStripSeparator();
		totalFilesStatusLabel = new ToolStripStatusLabel();
		mainMenuStrip.SuspendLayout();
		mainToolStrip.SuspendLayout();
		coverContextMenu.SuspendLayout();
		fileListHeaderContextMenu.SuspendLayout();
		fileListItemContextMenu.SuspendLayout();
		notifyContextMenu.SuspendLayout();
		((ISupportInitialize)mainSplitContainer).BeginInit();
		mainSplitContainer.Panel1.SuspendLayout();
		mainSplitContainer.Panel2.SuspendLayout();
		mainSplitContainer.SuspendLayout();
		tagEditorPanel.SuspendLayout();
		titleRowPanel.SuspendLayout();
		artistRowPanel.SuspendLayout();
		albumRowPanel.SuspendLayout();
		yearRowPanel.SuspendLayout();
		trackDiscGroupPanel.SuspendLayout();
		trackColumnPanel.SuspendLayout();
		trackRowPanel.SuspendLayout();
		discColumnPanel.SuspendLayout();
		discRowPanel.SuspendLayout();
		genreRowPanel.SuspendLayout();
		albumArtistRowPanel.SuspendLayout();
		composerRowPanel.SuspendLayout();
		lyricistRowPanel.SuspendLayout();
		commentRowPanel.SuspendLayout();
		lyricsRowPanel.SuspendLayout();
		coverPanel.SuspendLayout();
		((ISupportInitialize)coverPictureBox).BeginInit();
		statusLabelsPanel.SuspendLayout();
		coverNavigationPanel.SuspendLayout();
		fileFilterStatusStrip.SuspendLayout();
		fileSummaryStatusStrip.SuspendLayout();
		SuspendLayout();
		mainMenuStrip.Items.AddRange(new ToolStripItem[8] { fileMenuItem, editMenuItem, viewMenuItem, tagSourcesMenuItem, batchMenuItem, toolsMenuItem, languageMenuItem, helpMenuItem });
		mainMenuStrip.Location = new Point(0, 0);
		mainMenuStrip.Name = "menuStrip1";
		mainMenuStrip.Padding = new Padding(7, 2, 0, 2);
		mainMenuStrip.Size = new Size(1589, 25);
		mainMenuStrip.TabIndex = 1;
		mainMenuStrip.Text = "menuStrip1";
		fileMenuItem.DropDownItems.AddRange(new ToolStripItem[12]
		{
		changeDirectoryMenuItem, addDirectoryMenuItem, manageDirectoriesMenuItem, fileMenuTagActionsSeparator, saveTagsMenuItem, removeTagsMenuItem, readTagsMenuItem, characterSetMenuItem, chineseConversionMenuItem, tagHistoryMenuItem,
		fileMenuExitSeparator, exitMenuItem
		});
		fileMenuItem.Name = "fileToolStripMenuItem";
		fileMenuItem.ShortcutKeyDisplayString = "";
		fileMenuItem.Size = new Size(39, 21);
		fileMenuItem.Text = "&File";
		changeDirectoryMenuItem.Image = Resources.chgDirToolStripMenuItem_Image;
		changeDirectoryMenuItem.Name = "chgDirToolStripMenuItem";
		changeDirectoryMenuItem.ShortcutKeys = Keys.D | Keys.Control;
		changeDirectoryMenuItem.Size = new Size(222, 22);
		changeDirectoryMenuItem.Text = "Change &directory";
		changeDirectoryMenuItem.Click += ChangeDirectoryButton_Click;
		addDirectoryMenuItem.Image = Resources.addDirsToolStripMenuItem_Image;
		addDirectoryMenuItem.ImageTransparentColor = Color.Magenta;
		addDirectoryMenuItem.Name = "addDirsToolStripMenuItem";
		addDirectoryMenuItem.Size = new Size(222, 22);
		addDirectoryMenuItem.Text = "&Add directory";
		addDirectoryMenuItem.Click += AddFolderButton_Click;
		manageDirectoriesMenuItem.Image = Resources.manageDirsToolStripMenuItem_Image;
		manageDirectoriesMenuItem.Name = "manageDirsToolStripMenuItem";
		manageDirectoriesMenuItem.Size = new Size(222, 22);
		manageDirectoriesMenuItem.Text = "&Manage directorys";
		manageDirectoriesMenuItem.Click += ManageDirectoriesButton_Click;
		fileMenuTagActionsSeparator.Name = "toolStripSeparator";
		fileMenuTagActionsSeparator.Size = new Size(219, 6);
		saveTagsMenuItem.Enabled = false;
		saveTagsMenuItem.Image = Resources.saveToolStripMenuItem_Image;
		saveTagsMenuItem.ImageTransparentColor = Color.Magenta;
		saveTagsMenuItem.Name = "saveToolStripMenuItem";
		saveTagsMenuItem.ShortcutKeys = Keys.S | Keys.Control;
		saveTagsMenuItem.Size = new Size(222, 22);
		saveTagsMenuItem.Text = "&Save tags";
		saveTagsMenuItem.Click += SaveTags_Click;
		removeTagsMenuItem.Enabled = false;
		removeTagsMenuItem.Image = Resources.removeTagToolStripMenuItem_Image;
		removeTagsMenuItem.Name = "removeTagToolStripMenuItem";
		removeTagsMenuItem.ShortcutKeys = Keys.R | Keys.Control;
		removeTagsMenuItem.Size = new Size(222, 22);
		removeTagsMenuItem.Text = "&Remove tags";
		removeTagsMenuItem.Click += ClearTags_Click;
		readTagsMenuItem.Enabled = false;
		readTagsMenuItem.Image = Resources.readTagsToolStripMenuItem_Image;
		readTagsMenuItem.Name = "readTagsToolStripMenuItem";
		readTagsMenuItem.ShortcutKeys = Keys.T | Keys.Control;
		readTagsMenuItem.Size = new Size(222, 22);
		readTagsMenuItem.Text = "Read &tags";
		readTagsMenuItem.Click += RefreshSelectedFiles_Click;
		characterSetMenuItem.Enabled = false;
		characterSetMenuItem.Image = Resources.characterSetToolStripMenuItem_Image;
		characterSetMenuItem.Name = "characterSetToolStripMenuItem";
		characterSetMenuItem.Size = new Size(222, 22);
		characterSetMenuItem.Text = "&Character Set";
		characterSetMenuItem.Click += EditAllTagFieldEncodings_Click;
		chineseConversionMenuItem.DropDownItems.AddRange(new ToolStripItem[2] { convertTagsTraditionalToSimplifiedMenuItem, convertTagsSimplifiedToTraditionalMenuItem });
		chineseConversionMenuItem.Enabled = false;
		chineseConversionMenuItem.Image = Resources.chschtToolStripMenuItem_Image;
		chineseConversionMenuItem.Name = "chschtToolStripMenuItem";
		chineseConversionMenuItem.Size = new Size(222, 22);
		chineseConversionMenuItem.Text = "CHS and CHT c&onversion";
		convertTagsTraditionalToSimplifiedMenuItem.Name = "chtToChsToolStripMenuItem1";
		convertTagsTraditionalToSimplifiedMenuItem.Size = new Size(144, 22);
		convertTagsTraditionalToSimplifiedMenuItem.Text = "CHT to CHS";
		convertTagsTraditionalToSimplifiedMenuItem.Click += ConvertTagsTraditionalToSimplified_Click;
		convertTagsSimplifiedToTraditionalMenuItem.Name = "chsToChtToolStripMenuItem1";
		convertTagsSimplifiedToTraditionalMenuItem.Size = new Size(144, 22);
		convertTagsSimplifiedToTraditionalMenuItem.Text = "CHS to CHT";
		convertTagsSimplifiedToTraditionalMenuItem.Click += ConvertTagsSimplifiedToTraditional_Click;
		tagHistoryMenuItem.Enabled = false;
		tagHistoryMenuItem.Image = Resources.tagsHistoryToolStripMenuItem_Image;
		tagHistoryMenuItem.Name = "tagsHistoryToolStripMenuItem";
		tagHistoryMenuItem.Size = new Size(222, 22);
		tagHistoryMenuItem.Text = "Tags &History";
		tagHistoryMenuItem.Click += RestoreTagsFromHistory_Click;
		fileMenuExitSeparator.Name = "toolStripSeparator1";
		fileMenuExitSeparator.Size = new Size(219, 6);
		exitMenuItem.Image = Resources.exitToolStripMenuItem_Image;
		exitMenuItem.Name = "exitToolStripMenuItem";
		exitMenuItem.Size = new Size(222, 22);
		exitMenuItem.Text = "&Exit";
		exitMenuItem.Click += ExitApplication_Click;
		editMenuItem.DropDownItems.AddRange(new ToolStripItem[9] { selectAllFilesMenuItem, unselectAllFilesMenuItem, invertFileSelectionMenuItem, editSelectionSeparator, undoMenuItem, renameFileMenuItem, removeItemsMenuItem, removeFilesMenuItem, openDirectoryMenuItem });
		editMenuItem.Name = "editToolStripMenuItem";
		editMenuItem.Size = new Size(42, 21);
		editMenuItem.Text = "&Edit";
		selectAllFilesMenuItem.Image = Resources.selallfilesToolStripMenuItem_Image;
		selectAllFilesMenuItem.Name = "selallfilesToolStripMenuItem";
		selectAllFilesMenuItem.ShortcutKeys = Keys.A | Keys.Control;
		selectAllFilesMenuItem.Size = new Size(243, 22);
		selectAllFilesMenuItem.Text = "Select all";
		selectAllFilesMenuItem.Click += SelectAllFiles_Click;
		unselectAllFilesMenuItem.Image = Resources.unselectAllToolStripMenuItem_Image;
		unselectAllFilesMenuItem.Name = "unselectAllToolStripMenuItem";
		unselectAllFilesMenuItem.ShortcutKeys = Keys.U | Keys.Control;
		unselectAllFilesMenuItem.Size = new Size(243, 22);
		unselectAllFilesMenuItem.Text = "&Unselect all";
		unselectAllFilesMenuItem.Click += UnselectAllFiles_Click;
		invertFileSelectionMenuItem.Name = "invertSelectToolStripMenuItem";
		invertFileSelectionMenuItem.ShortcutKeys = Keys.A | Keys.Shift | Keys.Control;
		invertFileSelectionMenuItem.Size = new Size(243, 22);
		invertFileSelectionMenuItem.Text = "&Invert selection";
		invertFileSelectionMenuItem.Click += InvertFileSelection_Click;
		editSelectionSeparator.Name = "toolStripSeparator2";
		editSelectionSeparator.Size = new Size(240, 6);
		undoMenuItem.Enabled = false;
		undoMenuItem.Image = Resources.undoToolStripMenuItem_Image;
		undoMenuItem.Name = "undoToolStripMenuItem";
		undoMenuItem.Size = new Size(243, 22);
		undoMenuItem.Text = "Undo";
		undoMenuItem.Click += UndoLastOperation_Click;
		renameFileMenuItem.Enabled = false;
		renameFileMenuItem.Name = "renameFileToolStripMenuItem";
		renameFileMenuItem.Size = new Size(243, 22);
		renameFileMenuItem.Text = "Rename";
		renameFileMenuItem.Click += BeginRenameSelectedFile_Click;
		removeItemsMenuItem.Enabled = false;
		removeItemsMenuItem.Name = "removeItemToolStripMenuItem";
		removeItemsMenuItem.ShortcutKeys = Keys.Delete;
		removeItemsMenuItem.Size = new Size(243, 22);
		removeItemsMenuItem.Text = "&Remove items";
		removeItemsMenuItem.Click += RemoveSelectedItemsFromList_Click;
		removeFilesMenuItem.Enabled = false;
		removeFilesMenuItem.Name = "removeFileToolStripMenuItem";
		removeFilesMenuItem.Size = new Size(243, 22);
		removeFilesMenuItem.Text = "Remove &files";
		removeFilesMenuItem.Click += DeleteSelectedFiles_Click;
		openDirectoryMenuItem.Enabled = false;
		openDirectoryMenuItem.Name = "openDirectoryToolStripMenuItem";
		openDirectoryMenuItem.Size = new Size(243, 22);
		openDirectoryMenuItem.Text = "&Open directory";
		openDirectoryMenuItem.Click += RevealSelectedFileInExplorer_Click;
		viewMenuItem.DropDownItems.AddRange(new ToolStripItem[2] { refreshMenuItem, customizeColumnsMenuItem });
		viewMenuItem.Name = "viewToolStripMenuItem";
		viewMenuItem.Size = new Size(47, 21);
		viewMenuItem.Text = "&View";
		refreshMenuItem.Image = Resources.refreshToolStripMenuItem_Image;
		refreshMenuItem.Name = "refreshToolStripMenuItem";
		refreshMenuItem.ShortcutKeys = Keys.F5;
		refreshMenuItem.Size = new Size(188, 22);
		refreshMenuItem.Text = "&Refresh";
		refreshMenuItem.Click += RefreshConfiguredFileList_Click;
		customizeColumnsMenuItem.Name = "customColToolStripMenuItem";
		customizeColumnsMenuItem.Size = new Size(188, 22);
		customizeColumnsMenuItem.Text = "&Customize columns";
		customizeColumnsMenuItem.Click += ConfigureFileListColumns_Click;
		tagSourcesMenuItem.DropDownItems.AddRange(new ToolStripItem[3] { coverSourceMenuItem, lyricSourceMenuItem, combinedTagSourceMenuItem });
		tagSourcesMenuItem.Enabled = false;
		tagSourcesMenuItem.Name = "tagSrcToolStripMenuItem";
		tagSourcesMenuItem.Size = new Size(92, 21);
		tagSourcesMenuItem.Text = "Tag &Sources";
		coverSourceMenuItem.DropDownItems.AddRange(new ToolStripItem[1] { defaultCoverSourceMenuItem });
		coverSourceMenuItem.Image = Resources.picSrcToolStripMenuItem_Image;
		coverSourceMenuItem.Name = "picSrcToolStripMenuItem";
		coverSourceMenuItem.Size = new Size(150, 22);
		coverSourceMenuItem.Text = "&Picture";
		defaultCoverSourceMenuItem.Name = "defaultPicSrcToolStripMenuItem";
		defaultCoverSourceMenuItem.Size = new Size(117, 22);
		defaultCoverSourceMenuItem.Text = "Default";
		defaultCoverSourceMenuItem.Click += CoverSourceMenuItem_Click;
		lyricSourceMenuItem.DropDownItems.AddRange(new ToolStripItem[1] { defaultLyricSourceMenuItem });
		lyricSourceMenuItem.Image = Resources.lyricSrcToolStripMenuItem_Image;
		lyricSourceMenuItem.Name = "lyricSrcToolStripMenuItem";
		lyricSourceMenuItem.Size = new Size(150, 22);
		lyricSourceMenuItem.Text = "&Lyric";
		defaultLyricSourceMenuItem.Name = "defaultLyricSrcToolStripMenuItem";
		defaultLyricSourceMenuItem.Size = new Size(117, 22);
		defaultLyricSourceMenuItem.Text = "Default";
		defaultLyricSourceMenuItem.Click += LyricSourceMenuItem_Click;
		combinedTagSourceMenuItem.DropDownItems.AddRange(new ToolStripItem[1] { defaultCombinedTagSourceMenuItem });
		combinedTagSourceMenuItem.Image = Resources.combTagsSrcToolStripMenuItem_Image;
		combinedTagSourceMenuItem.Name = "combTagsSrcToolStripMenuItem";
		combinedTagSourceMenuItem.Size = new Size(150, 22);
		combinedTagSourceMenuItem.Text = "&Combination";
		defaultCombinedTagSourceMenuItem.Name = "defaultCombTagsSrcToolStripMenuItem";
		defaultCombinedTagSourceMenuItem.Size = new Size(117, 22);
		defaultCombinedTagSourceMenuItem.Text = "Default";
		defaultCombinedTagSourceMenuItem.Click += TagSourceMenuItem_Click;
		batchMenuItem.DropDownItems.AddRange(new ToolStripItem[5] { batchAutoMatchTagsMenuItem, batchLyricMenuItem, batchExtractCoverMenuItem, batchFilenameRelatedMenuItem, batchChineseConversionMenuItem });
		batchMenuItem.Enabled = false;
		batchMenuItem.Name = "batchToolStripMenuItem";
		batchMenuItem.Size = new Size(52, 21);
		batchMenuItem.Text = "&Batch";
		batchAutoMatchTagsMenuItem.Image = Resources.batchAutoMatchTagsToolStripButton_Image;
		batchAutoMatchTagsMenuItem.Name = "batchAutoMatchTagsToolStripMenuItem";
		batchAutoMatchTagsMenuItem.Size = new Size(221, 22);
		batchAutoMatchTagsMenuItem.Text = "&Auto match tags";
		batchAutoMatchTagsMenuItem.Click += BatchAutoMatchTags_Click;
		batchLyricMenuItem.DropDownItems.AddRange(new ToolStripItem[6] { reformatLyricTimeTagsMenuItem, removeLyricTimeTagsMenuItem, deleteBlankLyricLinesMenuItem, deleteLyricHeaderTagsMenuItem, saveLyricsMenuItem, importLrcFilesMenuItem });
		batchLyricMenuItem.Name = "batchLyricToolStripMenuItem";
		batchLyricMenuItem.Size = new Size(221, 22);
		batchLyricMenuItem.Text = "&Lyric";
		reformatLyricTimeTagsMenuItem.Name = "batchReformatTimetagToolStripMenuItem";
		reformatLyricTimeTagsMenuItem.Size = new Size(220, 22);
		reformatLyricTimeTagsMenuItem.Text = "Reformat timetag";
		reformatLyricTimeTagsMenuItem.Click += ReformatLyricTimeTags_Click;
		removeLyricTimeTagsMenuItem.Name = "batchRemoveTimetagToolStripMenuItem";
		removeLyricTimeTagsMenuItem.Size = new Size(220, 22);
		removeLyricTimeTagsMenuItem.Text = "Remove timetag";
		removeLyricTimeTagsMenuItem.Click += RemoveLyricTimeTags_Click;
		deleteBlankLyricLinesMenuItem.Name = "batchDeleteLinesOfBlankTextToolStripMenuItem";
		deleteBlankLyricLinesMenuItem.Size = new Size(220, 22);
		deleteBlankLyricLinesMenuItem.Text = "Delete lines of blank text";
		deleteBlankLyricLinesMenuItem.Click += DeleteBlankLyricLines_Click;
		deleteLyricHeaderTagsMenuItem.Name = "batchDeleteHeadTagsToolStripMenuItem";
		deleteLyricHeaderTagsMenuItem.Size = new Size(220, 22);
		deleteLyricHeaderTagsMenuItem.Text = "Delete head tags";
		deleteLyricHeaderTagsMenuItem.Click += DeleteLyricHeaderTags_Click;
		saveLyricsMenuItem.Image = Resources.batchSaveAsLrcFileToolStripSplitButton_Image;
		saveLyricsMenuItem.Name = "batchSaveAsLrcFileToolStripMenuItem";
		saveLyricsMenuItem.Size = new Size(220, 22);
		saveLyricsMenuItem.Text = "Save as Lrc file";
		saveLyricsMenuItem.Click += SaveLyrics_Click;
		importLrcFilesMenuItem.Name = "batchImportLrcFileToolStripMenuItem";
		importLrcFilesMenuItem.Size = new Size(220, 22);
		importLrcFilesMenuItem.Text = "Import Lrc file";
		importLrcFilesMenuItem.Click += ImportLrcFiles_Click;
		batchExtractCoverMenuItem.Image = Resources.batchExtractCoverToolStripButton_Image;
		batchExtractCoverMenuItem.Name = "batchExtractCoverToolStripMenuItem";
		batchExtractCoverMenuItem.Size = new Size(221, 22);
		batchExtractCoverMenuItem.Text = "&Extract cover";
		batchExtractCoverMenuItem.Click += ExtractCovers_Click;
		batchFilenameRelatedMenuItem.Image = Resources.batchFilenameRelToolStripButton_Image;
		batchFilenameRelatedMenuItem.Name = "batchFilenameRelToolStripMenuItem";
		batchFilenameRelatedMenuItem.Size = new Size(221, 22);
		batchFilenameRelatedMenuItem.Text = "&File name-related";
		batchFilenameRelatedMenuItem.Click += BatchFilenameOrTagsFromPattern_Click;
		batchChineseConversionMenuItem.DropDownItems.AddRange(new ToolStripItem[4] { batchTagsTraditionalToSimplifiedMenuItem, batchTagsSimplifiedToTraditionalMenuItem, batchFilenameTraditionalToSimplifiedMenuItem, batchFilenameSimplifiedToTraditionalMenuItem });
		batchChineseConversionMenuItem.Image = Resources.chschtToolStripMenuItem_Image;
		batchChineseConversionMenuItem.Name = "batchChschtToolStripMenuItem";
		batchChineseConversionMenuItem.Size = new Size(221, 22);
		batchChineseConversionMenuItem.Text = "CHS and CHT c&onversion";
		batchTagsTraditionalToSimplifiedMenuItem.Name = "batchTagsChtToChsToolStripMenuItem";
		batchTagsTraditionalToSimplifiedMenuItem.Size = new Size(249, 22);
		batchTagsTraditionalToSimplifiedMenuItem.Text = "Convert tags: CHT to CHS";
		batchTagsTraditionalToSimplifiedMenuItem.Click += ConvertSelectedTagsTraditionalToSimplified_Click;
		batchTagsSimplifiedToTraditionalMenuItem.Name = "batchTagsChsToChtToolStripMenuItem";
		batchTagsSimplifiedToTraditionalMenuItem.Size = new Size(249, 22);
		batchTagsSimplifiedToTraditionalMenuItem.Text = "Convert tags: CHS to CHT";
		batchTagsSimplifiedToTraditionalMenuItem.Click += ConvertSelectedTagsSimplifiedToTraditional_Click;
		batchFilenameTraditionalToSimplifiedMenuItem.Name = "batchFilenameChtToChsToolStripMenuItem";
		batchFilenameTraditionalToSimplifiedMenuItem.Size = new Size(249, 22);
		batchFilenameTraditionalToSimplifiedMenuItem.Text = "Convert filename: CHT to CHS";
		batchFilenameTraditionalToSimplifiedMenuItem.Click += ConvertSelectedFilenamesTraditionalToSimplified_Click;
		batchFilenameSimplifiedToTraditionalMenuItem.Name = "batchFilenameChsToChtToolStripMenuItem";
		batchFilenameSimplifiedToTraditionalMenuItem.Size = new Size(249, 22);
		batchFilenameSimplifiedToTraditionalMenuItem.Text = "Convert filename: CHS to CHT";
		batchFilenameSimplifiedToTraditionalMenuItem.Click += ConvertSelectedFilenamesSimplifiedToTraditional_Click;
		toolsMenuItem.DropDownItems.AddRange(new ToolStripItem[1] { optionsMenuItem });
		toolsMenuItem.Name = "toolsToolStripMenuItem";
		toolsMenuItem.Size = new Size(52, 21);
		toolsMenuItem.Text = "&Tools";
		optionsMenuItem.Image = Resources.optionsToolStripMenuItem_Image;
		optionsMenuItem.Name = "optionsToolStripMenuItem";
		optionsMenuItem.ShortcutKeys = Keys.O | Keys.Control;
		optionsMenuItem.Size = new Size(169, 22);
		optionsMenuItem.Text = "&Options";
		optionsMenuItem.Click += OpenOptions_Click;
		languageMenuItem.DropDownItems.AddRange(new ToolStripItem[3] { englishLanguageMenuItem, simplifiedChineseLanguageMenuItem, traditionalChineseLanguageMenuItem });
		languageMenuItem.Name = "languageToolStripMenuItem";
		languageMenuItem.Size = new Size(77, 21);
		languageMenuItem.Text = "&Language";
		englishLanguageMenuItem.Name = "langenglishToolStripMenuItem";
		englishLanguageMenuItem.Size = new Size(187, 22);
		englishLanguageMenuItem.Text = "&English";
		englishLanguageMenuItem.Click += SetLanguageEnglish_Click;
		simplifiedChineseLanguageMenuItem.Name = "langchsToolStripMenuItem";
		simplifiedChineseLanguageMenuItem.Size = new Size(187, 22);
		simplifiedChineseLanguageMenuItem.Text = "&Simplified Chinese";
		simplifiedChineseLanguageMenuItem.Click += SetLanguageSimplifiedChinese_Click;
		traditionalChineseLanguageMenuItem.Name = "langchtToolStripMenuItem";
		traditionalChineseLanguageMenuItem.Size = new Size(187, 22);
		traditionalChineseLanguageMenuItem.Text = "&Traditional Chinese";
		traditionalChineseLanguageMenuItem.Click += SetLanguageTraditionalChinese_Click;
		helpMenuItem.DropDownItems.AddRange(new ToolStripItem[3] { checkForUpdatesMenuItem, musicTagWebsiteMenuItem, aboutMenuItem });
		helpMenuItem.Name = "helpToolStripMenuItem";
		helpMenuItem.Size = new Size(47, 21);
		helpMenuItem.Text = "&Help";
		checkForUpdatesMenuItem.Name = "checkNewVerToolStripMenuItem";
		checkForUpdatesMenuItem.Size = new Size(205, 22);
		checkForUpdatesMenuItem.Text = "&Check for new version";
		checkForUpdatesMenuItem.Click += CheckForUpdates_Click;
		musicTagWebsiteMenuItem.Name = "musicTagWebsiteToolStripMenuItem";
		musicTagWebsiteMenuItem.Size = new Size(205, 22);
		musicTagWebsiteMenuItem.Text = "Music Tag &Website";
		musicTagWebsiteMenuItem.Click += OpenMusicTagWebsite_Click;
		aboutMenuItem.Name = "aboutToolStripMenuItem";
		aboutMenuItem.Size = new Size(205, 22);
		aboutMenuItem.Text = "&About";
		aboutMenuItem.Click += ShowAboutDialog_Click;
		mainToolStrip.Items.AddRange(new ToolStripItem[28]
		{
		changeDirectoryToolStripButton, addDirectoriesToolStripButton, manageDirectoriesToolStripButton, directoryToolbarSeparator, saveTagsToolStripButton, removeTagsToolStripButton, undoToolStripButton, readTagsToolStripButton, characterSetToolStripButton, chineseConversionToolStripDropDownButton,
		tagHistoryToolStripButton, tagActionsToolbarSeparator, selectAllFilesToolStripButton, unselectAllFilesToolStripButton, selectionToolbarSeparator, refreshToolStripButton, sourceToolbarSeparator, coverSourceToolStripSplitButton, lyricSourceToolStripSplitButton, combinedTagSourceToolStripSplitButton,
		batchToolbarStartSeparator, batchAutoMatchTagsToolStripButton, batchSaveAsLrcToolStripSplitButton, batchExtractCoverToolStripButton, batchChineseConversionToolStripDropDownButton, batchFilenameRelatedToolStripButton, batchToolbarOptionsSeparator, optionsToolStripButton
		});
		mainToolStrip.Location = new Point(0, 25);
		mainToolStrip.Name = "toolStrip1";
		mainToolStrip.Size = new Size(1589, 25);
		mainToolStrip.TabIndex = 2;
		mainToolStrip.Text = "toolStrip1";
		changeDirectoryToolStripButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
		changeDirectoryToolStripButton.Image = Resources.chgDirToolStripMenuItem_Image;
		changeDirectoryToolStripButton.ImageTransparentColor = Color.Magenta;
		changeDirectoryToolStripButton.Name = "chgDirToolStripButton";
		changeDirectoryToolStripButton.Size = new Size(23, 22);
		changeDirectoryToolStripButton.Text = "Change &directory";
		changeDirectoryToolStripButton.Click += ChangeDirectoryButton_Click;
		addDirectoriesToolStripButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
		addDirectoriesToolStripButton.Image = Resources.addDirsToolStripMenuItem_Image;
		addDirectoriesToolStripButton.ImageTransparentColor = Color.Magenta;
		addDirectoriesToolStripButton.Name = "addDirsToolStripButton";
		addDirectoriesToolStripButton.Size = new Size(23, 22);
		addDirectoriesToolStripButton.Text = "add &Directories";
		addDirectoriesToolStripButton.Click += AddFolderButton_Click;
		manageDirectoriesToolStripButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
		manageDirectoriesToolStripButton.Image = Resources.manageDirsToolStripMenuItem_Image;
		manageDirectoriesToolStripButton.ImageTransparentColor = Color.Magenta;
		manageDirectoriesToolStripButton.Name = "manageDirsToolStripButton";
		manageDirectoriesToolStripButton.Size = new Size(23, 22);
		manageDirectoriesToolStripButton.Text = "&Manage directorys";
		manageDirectoriesToolStripButton.Click += ManageDirectoriesButton_Click;
		directoryToolbarSeparator.Name = "toolStripSeparator8";
		directoryToolbarSeparator.Size = new Size(6, 25);
		saveTagsToolStripButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
		saveTagsToolStripButton.Enabled = false;
		saveTagsToolStripButton.Image = Resources.saveToolStripMenuItem_Image;
		saveTagsToolStripButton.ImageTransparentColor = Color.Magenta;
		saveTagsToolStripButton.Name = "saveToolStripButton";
		saveTagsToolStripButton.Size = new Size(23, 22);
		saveTagsToolStripButton.Text = "Save(&S)";
		saveTagsToolStripButton.Click += SaveTags_Click;
		removeTagsToolStripButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
		removeTagsToolStripButton.Enabled = false;
		removeTagsToolStripButton.Image = Resources.removeTagToolStripMenuItem_Image;
		removeTagsToolStripButton.ImageTransparentColor = Color.Magenta;
		removeTagsToolStripButton.Name = "removeTagToolStripButton";
		removeTagsToolStripButton.Size = new Size(23, 22);
		removeTagsToolStripButton.Text = "&Remove tags";
		removeTagsToolStripButton.Click += ClearTags_Click;
		undoToolStripButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
		undoToolStripButton.Enabled = false;
		undoToolStripButton.Image = Resources.undoToolStripMenuItem_Image;
		undoToolStripButton.ImageTransparentColor = Color.Magenta;
		undoToolStripButton.Name = "undoToolStripButton";
		undoToolStripButton.Size = new Size(23, 22);
		undoToolStripButton.Text = "&Undo";
		undoToolStripButton.Click += UndoLastOperation_Click;
		readTagsToolStripButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
		readTagsToolStripButton.Enabled = false;
		readTagsToolStripButton.Image = Resources.readTagsToolStripMenuItem_Image;
		readTagsToolStripButton.ImageTransparentColor = Color.Magenta;
		readTagsToolStripButton.Name = "readTagsToolStripButton";
		readTagsToolStripButton.Size = new Size(23, 22);
		readTagsToolStripButton.Text = "Read &tags";
		readTagsToolStripButton.Click += RefreshSelectedFiles_Click;
		characterSetToolStripButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
		characterSetToolStripButton.Enabled = false;
		characterSetToolStripButton.Image = Resources.characterSetToolStripMenuItem_Image;
		characterSetToolStripButton.ImageTransparentColor = Color.Magenta;
		characterSetToolStripButton.Name = "characterSetToolStripButton";
		characterSetToolStripButton.Size = new Size(23, 22);
		characterSetToolStripButton.Text = "&Character Set";
		characterSetToolStripButton.Click += EditAllTagFieldEncodings_Click;
		chineseConversionToolStripDropDownButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
		chineseConversionToolStripDropDownButton.DropDownItems.AddRange(new ToolStripItem[2] { convertTagsTraditionalToSimplifiedToolStripMenuItem, convertTagsSimplifiedToTraditionalToolStripMenuItem });
		chineseConversionToolStripDropDownButton.Enabled = false;
		chineseConversionToolStripDropDownButton.Image = Resources.chschtToolStripMenuItem_Image;
		chineseConversionToolStripDropDownButton.ImageTransparentColor = Color.Magenta;
		chineseConversionToolStripDropDownButton.Name = "chschtToolStripButton";
		chineseConversionToolStripDropDownButton.Size = new Size(29, 22);
		chineseConversionToolStripDropDownButton.Text = "toolStripDropDownButton1";
		convertTagsTraditionalToSimplifiedToolStripMenuItem.Name = "chtToChsToolStripMenuItem";
		convertTagsTraditionalToSimplifiedToolStripMenuItem.Size = new Size(144, 22);
		convertTagsTraditionalToSimplifiedToolStripMenuItem.Text = "CHT to CHS";
		convertTagsTraditionalToSimplifiedToolStripMenuItem.Click += ConvertTagsTraditionalToSimplified_Click;
		convertTagsSimplifiedToTraditionalToolStripMenuItem.Name = "chsToChtToolStripMenuItem";
		convertTagsSimplifiedToTraditionalToolStripMenuItem.Size = new Size(144, 22);
		convertTagsSimplifiedToTraditionalToolStripMenuItem.Text = "CHS to CHT";
		convertTagsSimplifiedToTraditionalToolStripMenuItem.Click += ConvertTagsSimplifiedToTraditional_Click;
		tagHistoryToolStripButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
		tagHistoryToolStripButton.Enabled = false;
		tagHistoryToolStripButton.Image = Resources.tagsHistoryToolStripMenuItem_Image;
		tagHistoryToolStripButton.ImageTransparentColor = Color.Magenta;
		tagHistoryToolStripButton.Name = "tagsHistoryToolStripButton";
		tagHistoryToolStripButton.Size = new Size(23, 22);
		tagHistoryToolStripButton.Text = "Tags &History";
		tagHistoryToolStripButton.Click += RestoreTagsFromHistory_Click;
		tagActionsToolbarSeparator.Name = "toolStripSeparator6";
		tagActionsToolbarSeparator.Size = new Size(6, 25);
		selectAllFilesToolStripButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
		selectAllFilesToolStripButton.Image = Resources.selallfilesToolStripMenuItem_Image;
		selectAllFilesToolStripButton.ImageTransparentColor = Color.Magenta;
		selectAllFilesToolStripButton.Name = "selallfilesToolStripButton";
		selectAllFilesToolStripButton.Size = new Size(23, 22);
		selectAllFilesToolStripButton.Text = "Select all &files";
		selectAllFilesToolStripButton.Click += SelectAllFiles_Click;
		unselectAllFilesToolStripButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
		unselectAllFilesToolStripButton.Image = Resources.unselectAllToolStripMenuItem_Image;
		unselectAllFilesToolStripButton.ImageTransparentColor = Color.Magenta;
		unselectAllFilesToolStripButton.Name = "unselectAllToolStripButton";
		unselectAllFilesToolStripButton.Size = new Size(23, 22);
		unselectAllFilesToolStripButton.Text = "&Unselect all";
		unselectAllFilesToolStripButton.Click += UnselectAllFiles_Click;
		selectionToolbarSeparator.Name = "toolStripSeparator3";
		selectionToolbarSeparator.Size = new Size(6, 25);
		refreshToolStripButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
		refreshToolStripButton.Image = Resources.refreshToolStripMenuItem_Image;
		refreshToolStripButton.ImageTransparentColor = Color.Magenta;
		refreshToolStripButton.Name = "refreshToolStripButton";
		refreshToolStripButton.Size = new Size(23, 22);
		refreshToolStripButton.Text = "&Refresh";
		refreshToolStripButton.Click += RefreshConfiguredFileList_Click;
		sourceToolbarSeparator.Name = "toolStripSeparator5";
		sourceToolbarSeparator.Size = new Size(6, 25);
		coverSourceToolStripSplitButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
		coverSourceToolStripSplitButton.Enabled = false;
		coverSourceToolStripSplitButton.Image = Resources.picSrcToolStripMenuItem_Image;
		coverSourceToolStripSplitButton.ImageTransparentColor = Color.Magenta;
		coverSourceToolStripSplitButton.Name = "picSrcToolStripSplitButton";
		coverSourceToolStripSplitButton.Size = new Size(32, 22);
		coverSourceToolStripSplitButton.Text = "&Picture Sources";
		coverSourceToolStripSplitButton.ButtonClick += CoverSourceMenuItem_Click;
		lyricSourceToolStripSplitButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
		lyricSourceToolStripSplitButton.Enabled = false;
		lyricSourceToolStripSplitButton.Image = Resources.lyricSrcToolStripMenuItem_Image;
		lyricSourceToolStripSplitButton.ImageTransparentColor = Color.Magenta;
		lyricSourceToolStripSplitButton.Name = "lyricSrcToolStripSplitButton";
		lyricSourceToolStripSplitButton.Size = new Size(32, 22);
		lyricSourceToolStripSplitButton.Text = "&Lyric Sources";
		lyricSourceToolStripSplitButton.ButtonClick += LyricSourceMenuItem_Click;
		combinedTagSourceToolStripSplitButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
		combinedTagSourceToolStripSplitButton.Enabled = false;
		combinedTagSourceToolStripSplitButton.Image = Resources.combTagsSrcToolStripMenuItem_Image;
		combinedTagSourceToolStripSplitButton.ImageTransparentColor = Color.Magenta;
		combinedTagSourceToolStripSplitButton.Name = "combTagsSrcToolStripSplitButton";
		combinedTagSourceToolStripSplitButton.Size = new Size(32, 22);
		combinedTagSourceToolStripSplitButton.Text = "Combination tags";
		combinedTagSourceToolStripSplitButton.ButtonClick += TagSourceMenuItem_Click;
		batchToolbarStartSeparator.Name = "toolStripSeparator10";
		batchToolbarStartSeparator.Size = new Size(6, 25);
		batchAutoMatchTagsToolStripButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
		batchAutoMatchTagsToolStripButton.Enabled = false;
		batchAutoMatchTagsToolStripButton.Image = Resources.batchAutoMatchTagsToolStripButton_Image;
		batchAutoMatchTagsToolStripButton.ImageTransparentColor = Color.Magenta;
		batchAutoMatchTagsToolStripButton.Name = "batchAutoMatchTagsToolStripButton";
		batchAutoMatchTagsToolStripButton.Size = new Size(23, 22);
		batchAutoMatchTagsToolStripButton.Text = "&Auto match tags";
		batchAutoMatchTagsToolStripButton.Click += BatchAutoMatchTags_Click;
		batchSaveAsLrcToolStripSplitButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
		batchSaveAsLrcToolStripSplitButton.DropDownItems.AddRange(new ToolStripItem[5] { reformatLyricTimeTagsToolStripMenuItem, removeLyricTimeTagsToolStripMenuItem, deleteBlankLyricLinesToolStripMenuItem, deleteLyricHeaderTagsToolStripMenuItem, importLrcFilesToolStripMenuItem });
		batchSaveAsLrcToolStripSplitButton.Enabled = false;
		batchSaveAsLrcToolStripSplitButton.Image = Resources.batchSaveAsLrcFileToolStripSplitButton_Image;
		batchSaveAsLrcToolStripSplitButton.ImageTransparentColor = Color.Magenta;
		batchSaveAsLrcToolStripSplitButton.Name = "batchSaveAsLrcFileToolStripSplitButton";
		batchSaveAsLrcToolStripSplitButton.Size = new Size(32, 22);
		batchSaveAsLrcToolStripSplitButton.Text = "Save as Lrc file";
		batchSaveAsLrcToolStripSplitButton.ButtonClick += SaveLyrics_Click;
		reformatLyricTimeTagsToolStripMenuItem.Name = "batchReformatTimetagToolStripMenuItem1";
		reformatLyricTimeTagsToolStripMenuItem.Size = new Size(220, 22);
		reformatLyricTimeTagsToolStripMenuItem.Text = "Reformat timetag";
		reformatLyricTimeTagsToolStripMenuItem.Click += ReformatLyricTimeTags_Click;
		removeLyricTimeTagsToolStripMenuItem.Name = "batchRemoveTimetagToolStripMenuItem1";
		removeLyricTimeTagsToolStripMenuItem.Size = new Size(220, 22);
		removeLyricTimeTagsToolStripMenuItem.Text = "Remove timetag";
		removeLyricTimeTagsToolStripMenuItem.Click += RemoveLyricTimeTags_Click;
		deleteBlankLyricLinesToolStripMenuItem.Name = "batchDeleteLinesOfBlankTextToolStripMenuItem1";
		deleteBlankLyricLinesToolStripMenuItem.Size = new Size(220, 22);
		deleteBlankLyricLinesToolStripMenuItem.Text = "Delete lines of blank text";
		deleteBlankLyricLinesToolStripMenuItem.Click += DeleteBlankLyricLines_Click;
		deleteLyricHeaderTagsToolStripMenuItem.Name = "batchDeleteHeadTagsToolStripMenuItem1";
		deleteLyricHeaderTagsToolStripMenuItem.Size = new Size(220, 22);
		deleteLyricHeaderTagsToolStripMenuItem.Text = "Delete head tags";
		deleteLyricHeaderTagsToolStripMenuItem.Click += DeleteLyricHeaderTags_Click;
		importLrcFilesToolStripMenuItem.Name = "batchImportLrcFileToolStripMenuItem1";
		importLrcFilesToolStripMenuItem.Size = new Size(220, 22);
		importLrcFilesToolStripMenuItem.Text = "Import Lrc file";
		importLrcFilesToolStripMenuItem.Click += ImportLrcFiles_Click;
		batchExtractCoverToolStripButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
		batchExtractCoverToolStripButton.Enabled = false;
		batchExtractCoverToolStripButton.Image = Resources.batchExtractCoverToolStripButton_Image;
		batchExtractCoverToolStripButton.ImageTransparentColor = Color.Magenta;
		batchExtractCoverToolStripButton.Name = "batchExtractCoverToolStripButton";
		batchExtractCoverToolStripButton.Size = new Size(23, 22);
		batchExtractCoverToolStripButton.Text = "Extract cover";
		batchExtractCoverToolStripButton.Click += ExtractCovers_Click;
		batchChineseConversionToolStripDropDownButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
		batchChineseConversionToolStripDropDownButton.DropDownItems.AddRange(new ToolStripItem[4] { batchTagsTraditionalToSimplifiedToolStripMenuItem, batchTagsSimplifiedToTraditionalToolStripMenuItem, batchFilenameTraditionalToSimplifiedToolStripMenuItem, batchFilenameSimplifiedToTraditionalToolStripMenuItem });
		batchChineseConversionToolStripDropDownButton.Enabled = false;
		batchChineseConversionToolStripDropDownButton.Image = Resources.chschtToolStripMenuItem_Image;
		batchChineseConversionToolStripDropDownButton.ImageTransparentColor = Color.Magenta;
		batchChineseConversionToolStripDropDownButton.Name = "batchChschtToolStripButton";
		batchChineseConversionToolStripDropDownButton.Size = new Size(29, 22);
		batchChineseConversionToolStripDropDownButton.Text = "toolStripDropDownButton1";
		batchTagsTraditionalToSimplifiedToolStripMenuItem.Name = "batchTagsChtToChsToolStripMenuItem1";
		batchTagsTraditionalToSimplifiedToolStripMenuItem.Size = new Size(249, 22);
		batchTagsTraditionalToSimplifiedToolStripMenuItem.Text = "Convert tags: CHT to CHS";
		batchTagsTraditionalToSimplifiedToolStripMenuItem.Click += ConvertSelectedTagsTraditionalToSimplified_Click;
		batchTagsSimplifiedToTraditionalToolStripMenuItem.Name = "batchTagsChsToChtToolStripMenuItem1";
		batchTagsSimplifiedToTraditionalToolStripMenuItem.Size = new Size(249, 22);
		batchTagsSimplifiedToTraditionalToolStripMenuItem.Text = "Convert tags: CHS to CHT";
		batchTagsSimplifiedToTraditionalToolStripMenuItem.Click += ConvertSelectedTagsSimplifiedToTraditional_Click;
		batchFilenameTraditionalToSimplifiedToolStripMenuItem.Name = "batchFilenameChtToChsToolStripMenuItem1";
		batchFilenameTraditionalToSimplifiedToolStripMenuItem.Size = new Size(249, 22);
		batchFilenameTraditionalToSimplifiedToolStripMenuItem.Text = "Convert filename: CHT to CHS";
		batchFilenameTraditionalToSimplifiedToolStripMenuItem.Click += ConvertSelectedFilenamesTraditionalToSimplified_Click;
		batchFilenameSimplifiedToTraditionalToolStripMenuItem.Name = "batchFilenameChsToChtToolStripMenuItem1";
		batchFilenameSimplifiedToTraditionalToolStripMenuItem.Size = new Size(249, 22);
		batchFilenameSimplifiedToTraditionalToolStripMenuItem.Text = "Convert filename: CHS to CHT";
		batchFilenameSimplifiedToTraditionalToolStripMenuItem.Click += ConvertSelectedFilenamesSimplifiedToTraditional_Click;
		batchFilenameRelatedToolStripButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
		batchFilenameRelatedToolStripButton.Enabled = false;
		batchFilenameRelatedToolStripButton.Image = Resources.batchFilenameRelToolStripButton_Image;
		batchFilenameRelatedToolStripButton.ImageTransparentColor = Color.Magenta;
		batchFilenameRelatedToolStripButton.Name = "batchFilenameRelToolStripButton";
		batchFilenameRelatedToolStripButton.Size = new Size(23, 22);
		batchFilenameRelatedToolStripButton.Text = "File name-related";
		batchFilenameRelatedToolStripButton.Click += BatchFilenameOrTagsFromPattern_Click;
		batchToolbarOptionsSeparator.Name = "toolStripSeparator7";
		batchToolbarOptionsSeparator.Size = new Size(6, 25);
		optionsToolStripButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
		optionsToolStripButton.Image = Resources.optionsToolStripMenuItem_Image;
		optionsToolStripButton.ImageTransparentColor = Color.Magenta;
		optionsToolStripButton.Name = "optionsToolStripButton";
		optionsToolStripButton.Size = new Size(23, 22);
		optionsToolStripButton.Text = "&Options";
		optionsToolStripButton.Click += OpenOptions_Click;
		localCoverFileDialog.Filter = componentResourceManager.GetString("openFileDialog1.Filter");
		localCoverFileDialog.Multiselect = true;
		localCoverFileDialog.RestoreDirectory = true;
		coverContextMenu.Items.AddRange(new ToolStripItem[7] { addCoverMenuItem, changeCoverResolutionMenuItem, removeCoverMenuItem, openCoverMenuItem, extractCoverMenuItem, coverContextMenuSeparator, coverTypeMenuItem });
		coverContextMenu.Name = "pictureBoxContextMenuStrip";
		coverContextMenu.Size = new Size(186, 142);
		addCoverMenuItem.DropDownItems.AddRange(new ToolStripItem[3] { chooseLocalCoverMenuItem, searchCoverFromNetworkMenuItem, chooseCoverFromTagsMenuItem });
		addCoverMenuItem.Name = "addCoverToolStripMenuItem";
		addCoverMenuItem.Size = new Size(185, 22);
		addCoverMenuItem.Text = "Add cover";
		chooseLocalCoverMenuItem.Name = "chooseLocalFileToolStripMenuItem";
		chooseLocalCoverMenuItem.Size = new Size(202, 22);
		chooseLocalCoverMenuItem.Text = "Choose Local file";
		chooseLocalCoverMenuItem.Click += ChooseLocalCover_Click;
		searchCoverFromNetworkMenuItem.Name = "searchFromWebToolStripMenuItem";
		searchCoverFromNetworkMenuItem.Size = new Size(202, 22);
		searchCoverFromNetworkMenuItem.Text = "Search from network";
		searchCoverFromNetworkMenuItem.Click += SearchCoverFromNetwork_Click;
		chooseCoverFromTagsMenuItem.Name = "chooseFromFileTagsToolStripMenuItem";
		chooseCoverFromTagsMenuItem.Size = new Size(202, 22);
		chooseCoverFromTagsMenuItem.Text = "Choose from file tags";
		chooseCoverFromTagsMenuItem.Click += ChooseCoverFromFileTags_Click;
		changeCoverResolutionMenuItem.Name = "changeResolutionCoverToolStripMenuItem";
		changeCoverResolutionMenuItem.Size = new Size(185, 22);
		changeCoverResolutionMenuItem.Text = "Change Resolution";
		changeCoverResolutionMenuItem.Click += ToggleCoverResolutionLimit_Click;
		removeCoverMenuItem.Name = "removeCoverToolStripMenuItem";
		removeCoverMenuItem.Size = new Size(185, 22);
		removeCoverMenuItem.Text = "Remove cover";
		removeCoverMenuItem.Click += RemoveCurrentCover_Click;
		openCoverMenuItem.Name = "openCoverToolStripMenuItem";
		openCoverMenuItem.Size = new Size(185, 22);
		openCoverMenuItem.Text = "Open Cover";
		openCoverMenuItem.Click += OpenCurrentCover_Click;
		extractCoverMenuItem.Name = "extractCoverToolStripMenuItem";
		extractCoverMenuItem.Size = new Size(185, 22);
		extractCoverMenuItem.Text = "Extract cover";
		extractCoverMenuItem.Click += ExtractCover_Click;
		coverContextMenuSeparator.Name = "toolStripSeparator4";
		coverContextMenuSeparator.Size = new Size(182, 6);
		coverTypeMenuItem.Name = "coverTypeToolStripMenuItem";
		coverTypeMenuItem.Size = new Size(185, 22);
		coverTypeMenuItem.Text = "Cover Type";
		extractCoverSaveFileDialog.RestoreDirectory = true;
		controlToolTip.AutoPopDelay = 5000;
		controlToolTip.InitialDelay = 500;
		controlToolTip.ReshowDelay = 100;
		controlToolTip.ShowAlways = true;
		titleEncodingButton.Location = new Point(145, 0);
		titleEncodingButton.Margin = new Padding(5, 0, 0, 0);
		titleEncodingButton.Name = "btnETitle";
		titleEncodingButton.Size = new Size(30, 23);
		titleEncodingButton.TabIndex = 2;
		titleEncodingButton.Text = "A";
		controlToolTip.SetToolTip(titleEncodingButton, "Character Set");
		titleEncodingButton.UseVisualStyleBackColor = true;
		artistEncodingButton.Location = new Point(145, 0);
		artistEncodingButton.Margin = new Padding(5, 0, 0, 0);
		artistEncodingButton.Name = "btnEArtist";
		artistEncodingButton.Size = new Size(30, 23);
		artistEncodingButton.TabIndex = 4;
		artistEncodingButton.Text = "A";
		controlToolTip.SetToolTip(artistEncodingButton, "Character Set");
		artistEncodingButton.UseVisualStyleBackColor = true;
		albumEncodingButton.Location = new Point(145, 0);
		albumEncodingButton.Margin = new Padding(5, 0, 0, 0);
		albumEncodingButton.Name = "btnEAlbum";
		albumEncodingButton.Size = new Size(30, 23);
		albumEncodingButton.TabIndex = 6;
		albumEncodingButton.Text = "A";
		controlToolTip.SetToolTip(albumEncodingButton, "Character Set");
		albumEncodingButton.UseVisualStyleBackColor = true;
		yearEncodingButton.Location = new Point(145, 0);
		yearEncodingButton.Margin = new Padding(5, 0, 0, 0);
		yearEncodingButton.Name = "btnEYear";
		yearEncodingButton.Size = new Size(30, 23);
		yearEncodingButton.TabIndex = 8;
		yearEncodingButton.Text = "A";
		controlToolTip.SetToolTip(yearEncodingButton, "Character Set");
		yearEncodingButton.UseVisualStyleBackColor = true;
		trackEncodingButton.Location = new Point(127, 0);
		trackEncodingButton.Margin = new Padding(0);
		trackEncodingButton.Name = "btnETrack";
		trackEncodingButton.Size = new Size(0, 23);
		trackEncodingButton.TabIndex = 10;
		trackEncodingButton.Text = "A";
		controlToolTip.SetToolTip(trackEncodingButton, "Character Set");
		trackEncodingButton.UseVisualStyleBackColor = true;
		trackEncodingButton.Visible = false;
		discEncodingButton.Location = new Point(127, 0);
		discEncodingButton.Margin = new Padding(0);
		discEncodingButton.Name = "btnEDisc";
		discEncodingButton.Size = new Size(0, 23);
		discEncodingButton.TabIndex = 12;
		discEncodingButton.Text = "A";
		controlToolTip.SetToolTip(discEncodingButton, "Character Set");
		discEncodingButton.UseVisualStyleBackColor = true;
		discEncodingButton.Visible = false;
		genreEncodingButton.Location = new Point(145, 0);
		genreEncodingButton.Margin = new Padding(5, 0, 0, 0);
		genreEncodingButton.Name = "btnEGenre";
		genreEncodingButton.Size = new Size(30, 23);
		genreEncodingButton.TabIndex = 14;
		genreEncodingButton.Text = "A";
		controlToolTip.SetToolTip(genreEncodingButton, "Character Set");
		genreEncodingButton.UseVisualStyleBackColor = true;
		albumArtistEncodingButton.Location = new Point(145, 0);
		albumArtistEncodingButton.Margin = new Padding(5, 0, 0, 0);
		albumArtistEncodingButton.Name = "btnEAlbumartist";
		albumArtistEncodingButton.Size = new Size(30, 23);
		albumArtistEncodingButton.TabIndex = 16;
		albumArtistEncodingButton.Text = "A";
		controlToolTip.SetToolTip(albumArtistEncodingButton, "Character Set");
		albumArtistEncodingButton.UseVisualStyleBackColor = true;
		composerEncodingButton.Location = new Point(145, 0);
		composerEncodingButton.Margin = new Padding(5, 0, 0, 0);
		composerEncodingButton.Name = "btnEComposer";
		composerEncodingButton.Size = new Size(30, 23);
		composerEncodingButton.TabIndex = 18;
		composerEncodingButton.Text = "A";
		controlToolTip.SetToolTip(composerEncodingButton, "Character Set");
		composerEncodingButton.UseVisualStyleBackColor = true;
		lyricistEncodingButton.Location = new Point(145, 0);
		lyricistEncodingButton.Margin = new Padding(5, 0, 0, 0);
		lyricistEncodingButton.Name = "btnELyricist";
		lyricistEncodingButton.Size = new Size(30, 23);
		lyricistEncodingButton.TabIndex = 20;
		lyricistEncodingButton.Text = "A";
		controlToolTip.SetToolTip(lyricistEncodingButton, "Character Set");
		lyricistEncodingButton.UseVisualStyleBackColor = true;
		commentEncodingButton.Location = new Point(145, 0);
		commentEncodingButton.Margin = new Padding(5, 0, 0, 0);
		commentEncodingButton.Name = "btnEComment";
		commentEncodingButton.Size = new Size(30, 23);
		commentEncodingButton.TabIndex = 22;
		commentEncodingButton.Text = "A";
		controlToolTip.SetToolTip(commentEncodingButton, "Character Set");
		commentEncodingButton.UseVisualStyleBackColor = true;
		editLyricsButton.Location = new Point(145, 0);
		editLyricsButton.Margin = new Padding(5, 0, 0, 0);
		editLyricsButton.Name = "btnLyric";
		editLyricsButton.Size = new Size(30, 23);
		editLyricsButton.TabIndex = 24;
		editLyricsButton.Text = "...";
		controlToolTip.SetToolTip(editLyricsButton, "Edit lyric");
		editLyricsButton.UseVisualStyleBackColor = true;
		editLyricsButton.Click += EditLyrics_Click;
		lyricsEncodingButton.Location = new Point(180, 0);
		lyricsEncodingButton.Margin = new Padding(5, 0, 0, 0);
		lyricsEncodingButton.Name = "btnELyric";
		lyricsEncodingButton.Size = new Size(30, 23);
		lyricsEncodingButton.TabIndex = 25;
		lyricsEncodingButton.Text = "A";
		controlToolTip.SetToolTip(lyricsEncodingButton, "Character Set");
		lyricsEncodingButton.UseVisualStyleBackColor = true;
		overwriteCoverCheckBox.AutoSize = true;
		overwriteCoverCheckBox.Location = new Point(3, 88);
		overwriteCoverCheckBox.Margin = new Padding(0, 5, 0, 3);
		overwriteCoverCheckBox.Name = "cbOverwritePicture";
		overwriteCoverCheckBox.RightToLeft = RightToLeft.No;
		overwriteCoverCheckBox.Size = new Size(80, 18);
		overwriteCoverCheckBox.TabIndex = 30;
		overwriteCoverCheckBox.Text = "Overwrite";
		controlToolTip.SetToolTip(overwriteCoverCheckBox, "Overwrite existing image when adding");
		overwriteCoverCheckBox.UseVisualStyleBackColor = true;
		overwriteCoverCheckBox.CheckedChanged += SaveOverwriteCoverSetting_Click;
		fileListHeaderContextMenu.Items.AddRange(new ToolStripItem[1] { customizeColumnsContextMenuItem });
		fileListHeaderContextMenu.Name = "listViewColumnContextMenuStrip";
		fileListHeaderContextMenu.Size = new Size(175, 26);
		customizeColumnsContextMenuItem.Name = "customColumnsToolStripMenuItem";
		customizeColumnsContextMenuItem.Size = new Size(174, 22);
		customizeColumnsContextMenuItem.Text = "Custom Columns";
		customizeColumnsContextMenuItem.Click += ConfigureFileListColumns_Click;
		fileListItemContextMenu.Items.AddRange(new ToolStripItem[10] { saveTagsContextMenuItem, removeTagsContextMenuItem, readTagsContextMenuItem, characterSetContextMenuItem, tagHistoryContextMenuItem, fileListItemContextSeparator, renameFileContextMenuItem, removeItemsContextMenuItem, removeFilesContextMenuItem, openDirectoryContextMenuItem });
		fileListItemContextMenu.Name = "listViewItemContextMenuStrip";
		fileListItemContextMenu.Size = new Size(165, 208);
		saveTagsContextMenuItem.Enabled = false;
		saveTagsContextMenuItem.Name = "saveToolStripMenuItem1";
		saveTagsContextMenuItem.Size = new Size(164, 22);
		saveTagsContextMenuItem.Text = "Save tags";
		saveTagsContextMenuItem.Click += SaveTags_Click;
		removeTagsContextMenuItem.Enabled = false;
		removeTagsContextMenuItem.Name = "removeTagToolStripMenuItem1";
		removeTagsContextMenuItem.Size = new Size(164, 22);
		removeTagsContextMenuItem.Text = "Remove tags";
		removeTagsContextMenuItem.Click += ClearTags_Click;
		readTagsContextMenuItem.Enabled = false;
		readTagsContextMenuItem.Name = "readTagsToolStripMenuItem1";
		readTagsContextMenuItem.Size = new Size(164, 22);
		readTagsContextMenuItem.Text = "Read &tags";
		readTagsContextMenuItem.Click += RefreshSelectedFiles_Click;
		characterSetContextMenuItem.Enabled = false;
		characterSetContextMenuItem.Name = "characterSetToolStripMenuItem1";
		characterSetContextMenuItem.Size = new Size(164, 22);
		characterSetContextMenuItem.Text = "&Character Set";
		characterSetContextMenuItem.Click += EditAllTagFieldEncodings_Click;
		tagHistoryContextMenuItem.Enabled = false;
		tagHistoryContextMenuItem.Name = "tagsHistoryToolStripMenuItem1";
		tagHistoryContextMenuItem.Size = new Size(164, 22);
		tagHistoryContextMenuItem.Text = "Tags &History";
		tagHistoryContextMenuItem.Click += RestoreTagsFromHistory_Click;
		fileListItemContextSeparator.Name = "toolStripSeparator9";
		fileListItemContextSeparator.Size = new Size(161, 6);
		renameFileContextMenuItem.Enabled = false;
		renameFileContextMenuItem.Name = "renameFileToolStripMenuItem1";
		renameFileContextMenuItem.Size = new Size(164, 22);
		renameFileContextMenuItem.Text = "Rename";
		renameFileContextMenuItem.Click += BeginRenameSelectedFile_Click;
		removeItemsContextMenuItem.Enabled = false;
		removeItemsContextMenuItem.Name = "removeItemToolStripMenuItem1";
		removeItemsContextMenuItem.Size = new Size(164, 22);
		removeItemsContextMenuItem.Text = "Remove item";
		removeItemsContextMenuItem.Click += RemoveSelectedItemsFromList_Click;
		removeFilesContextMenuItem.Enabled = false;
		removeFilesContextMenuItem.Name = "removeFiletoolStripMenuItem1";
		removeFilesContextMenuItem.Size = new Size(164, 22);
		removeFilesContextMenuItem.Text = "Remove files";
		removeFilesContextMenuItem.Click += DeleteSelectedFiles_Click;
		openDirectoryContextMenuItem.Enabled = false;
		openDirectoryContextMenuItem.Name = "openDirectoryToolStripMenuItem1";
		openDirectoryContextMenuItem.Size = new Size(164, 22);
		openDirectoryContextMenuItem.Text = "Open directory";
		openDirectoryContextMenuItem.Click += RevealSelectedFileInExplorer_Click;
		selectionStatusUpdateTimer.Tick += SelectionStatusUpdateTimer_Tick;
		fileListStatusTimer.Tick += FileListStatusTimer_Tick;
		filterInputTimer.Interval = 500;
		filterInputTimer.Tick += FilterInputTimer_Tick;
		renamedFilesRefreshTimer.Interval = 500;
		renamedFilesRefreshTimer.Tick += RefreshRenamedFilesTimer_Tick;
		comboBoxSelectionResetTimer.Interval = 10;
		comboBoxSelectionResetTimer.Tick += ResetComboBoxSelectionTimer_Tick;
		notifyIcon.ContextMenuStrip = notifyContextMenu;
		notifyIcon.Text = "notifyIcon1";
		notifyIcon.Visible = true;
		notifyIcon.MouseClick += NotifyIcon_MouseClick;
		notifyContextMenu.Items.AddRange(new ToolStripItem[1] { notifyExitMenuItem });
		notifyContextMenu.Name = "notifyContextMenuStrip";
		notifyContextMenu.Size = new Size(97, 26);
		notifyExitMenuItem.Name = "notifyExitToolStripMenuItem";
		notifyExitMenuItem.Size = new Size(96, 22);
		notifyExitMenuItem.Text = "&Exit";
		mainSplitContainer.Dock = DockStyle.Fill;
		mainSplitContainer.FixedPanel = FixedPanel.Panel1;
		mainSplitContainer.Location = new Point(0, 50);
		mainSplitContainer.Name = "splitContainer1";
		mainSplitContainer.Panel1.Controls.Add(tagEditorPanel);
		mainSplitContainer.Panel1MinSize = 320;
		mainSplitContainer.Panel2.Controls.Add(fileListView);
		mainSplitContainer.Panel2.Controls.Add(fileFilterStatusStrip);
		mainSplitContainer.Panel2.Controls.Add(fileSummaryStatusStrip);
		mainSplitContainer.Panel2.SizeChanged += FileListPanel_SizeChanged;
		mainSplitContainer.Size = new Size(1589, 856);
		mainSplitContainer.SplitterDistance = 320;
		mainSplitContainer.SplitterWidth = 6;
		mainSplitContainer.TabIndex = 38;
		mainSplitContainer.SplitterMoved += RestartComboBoxSelectionResetTimer;
		tagEditorPanel.AutoScroll = true;
		tagEditorPanel.BackColor = SystemColors.Control;
		tagEditorPanel.Controls.Add(titleLabel);
		tagEditorPanel.Controls.Add(titleRowPanel);
		tagEditorPanel.Controls.Add(artistLabel);
		tagEditorPanel.Controls.Add(artistRowPanel);
		tagEditorPanel.Controls.Add(albumLabel);
		tagEditorPanel.Controls.Add(albumRowPanel);
		tagEditorPanel.Controls.Add(yearLabel);
		tagEditorPanel.Controls.Add(yearRowPanel);
		tagEditorPanel.Controls.Add(trackDiscGroupPanel);
		tagEditorPanel.Controls.Add(genreLabel);
		tagEditorPanel.Controls.Add(genreRowPanel);
		tagEditorPanel.Controls.Add(albumArtistLabel);
		tagEditorPanel.Controls.Add(albumArtistRowPanel);
		tagEditorPanel.Controls.Add(composerLabel);
		tagEditorPanel.Controls.Add(composerRowPanel);
		tagEditorPanel.Controls.Add(lyricistLabel);
		tagEditorPanel.Controls.Add(lyricistRowPanel);
		tagEditorPanel.Controls.Add(commentLabel);
		tagEditorPanel.Controls.Add(commentRowPanel);
		tagEditorPanel.Controls.Add(lyricsLabel);
		tagEditorPanel.Controls.Add(lyricsRowPanel);
		tagEditorPanel.Controls.Add(coverPanel);
		tagEditorPanel.Controls.Add(coverNavigationPanel);
		tagEditorPanel.Controls.Add(tagEditorBottomSpacerPanel);
		tagEditorPanel.Dock = DockStyle.Fill;
		tagEditorPanel.FlowDirection = FlowDirection.TopDown;
		tagEditorPanel.Location = new Point(0, 0);
		tagEditorPanel.Margin = new Padding(0);
		tagEditorPanel.Name = "flowLayoutPanel1";
		tagEditorPanel.Padding = new Padding(15);
		tagEditorPanel.Size = new Size(320, 856);
		tagEditorPanel.TabIndex = 0;
		tagEditorPanel.WrapContents = false;
		tagEditorPanel.SizeChanged += TagEditorPanel_SizeChanged;
		titleLabel.AutoSize = true;
		titleLabel.Location = new Point(18, 15);
		titleLabel.Name = "lblTitle";
		titleLabel.Size = new Size(39, 14);
		titleLabel.TabIndex = 0;
		titleLabel.Text = "Title: ";
		titleRowPanel.Controls.Add(titleComboBox);
		titleRowPanel.Controls.Add(titleEncodingButton);
		titleRowPanel.Location = new Point(18, 35);
		titleRowPanel.Margin = new Padding(3, 6, 3, 3);
		titleRowPanel.Name = "panelTitle";
		titleRowPanel.Size = new Size(284, 23);
		titleRowPanel.TabIndex = 26;
		titleComboBox.FormattingEnabled = true;
		titleComboBox.Location = new Point(0, 0);
		titleComboBox.Margin = new Padding(0);
		titleComboBox.Name = "cbTitle";
		titleComboBox.Size = new Size(140, 22);
		titleComboBox.TabIndex = 1;
		artistLabel.AutoSize = true;
		artistLabel.Location = new Point(18, 67);
		artistLabel.Margin = new Padding(3, 6, 3, 0);
		artistLabel.Name = "lblArtist";
		artistLabel.Size = new Size(44, 14);
		artistLabel.TabIndex = 2;
		artistLabel.Text = "Artist: ";
		artistRowPanel.Controls.Add(artistComboBox);
		artistRowPanel.Controls.Add(artistEncodingButton);
		artistRowPanel.Location = new Point(18, 87);
		artistRowPanel.Margin = new Padding(3, 6, 3, 3);
		artistRowPanel.Name = "panelArtist";
		artistRowPanel.Size = new Size(284, 23);
		artistRowPanel.TabIndex = 27;
		artistComboBox.FormattingEnabled = true;
		artistComboBox.Location = new Point(0, 0);
		artistComboBox.Margin = new Padding(0);
		artistComboBox.Name = "cbArtist";
		artistComboBox.Size = new Size(140, 22);
		artistComboBox.TabIndex = 3;
		albumLabel.AutoSize = true;
		albumLabel.Location = new Point(18, 119);
		albumLabel.Margin = new Padding(3, 6, 3, 0);
		albumLabel.Name = "lblAlbum";
		albumLabel.Size = new Size(49, 14);
		albumLabel.TabIndex = 4;
		albumLabel.Text = "Album: ";
		albumRowPanel.Controls.Add(albumComboBox);
		albumRowPanel.Controls.Add(albumEncodingButton);
		albumRowPanel.Location = new Point(18, 139);
		albumRowPanel.Margin = new Padding(3, 6, 3, 3);
		albumRowPanel.Name = "panelAlbum";
		albumRowPanel.Size = new Size(284, 23);
		albumRowPanel.TabIndex = 28;
		albumComboBox.FormattingEnabled = true;
		albumComboBox.Location = new Point(0, 0);
		albumComboBox.Margin = new Padding(0);
		albumComboBox.Name = "cbAlbum";
		albumComboBox.Size = new Size(140, 22);
		albumComboBox.TabIndex = 5;
		yearLabel.AutoSize = true;
		yearLabel.Location = new Point(18, 171);
		yearLabel.Margin = new Padding(3, 6, 3, 0);
		yearLabel.Name = "lblYear";
		yearLabel.Size = new Size(40, 14);
		yearLabel.TabIndex = 6;
		yearLabel.Text = "Year: ";
		yearRowPanel.Controls.Add(yearComboBox);
		yearRowPanel.Controls.Add(yearEncodingButton);
		yearRowPanel.Location = new Point(18, 191);
		yearRowPanel.Margin = new Padding(3, 6, 3, 3);
		yearRowPanel.Name = "panelYear";
		yearRowPanel.Size = new Size(284, 23);
		yearRowPanel.TabIndex = 29;
		yearComboBox.FormattingEnabled = true;
		yearComboBox.Location = new Point(0, 0);
		yearComboBox.Margin = new Padding(0);
		yearComboBox.Name = "cbYear";
		yearComboBox.Size = new Size(140, 22);
		yearComboBox.TabIndex = 7;
		trackDiscGroupPanel.Controls.Add(trackColumnPanel);
		trackDiscGroupPanel.Controls.Add(discColumnPanel);
		trackDiscGroupPanel.Location = new Point(18, 223);
		trackDiscGroupPanel.Margin = new Padding(3, 6, 3, 3);
		trackDiscGroupPanel.Name = "panelTrackDisc";
		trackDiscGroupPanel.Size = new Size(284, 42);
		trackDiscGroupPanel.TabIndex = 30;
		trackColumnPanel.Controls.Add(trackLabel);
		trackColumnPanel.Controls.Add(trackRowPanel);
		trackColumnPanel.FlowDirection = FlowDirection.TopDown;
		trackColumnPanel.Location = new Point(0, 0);
		trackColumnPanel.Margin = new Padding(0);
		trackColumnPanel.Name = "panelTrack1";
		trackColumnPanel.Size = new Size(142, 43);
		trackColumnPanel.TabIndex = 40;
		trackLabel.AutoSize = true;
		trackLabel.Location = new Point(0, 0);
		trackLabel.Margin = new Padding(0);
		trackLabel.Name = "lblTrack";
		trackLabel.Size = new Size(45, 14);
		trackLabel.TabIndex = 31;
		trackLabel.Text = "Track: ";
		trackRowPanel.Controls.Add(trackComboBox);
		trackRowPanel.Controls.Add(trackEncodingButton);
		trackRowPanel.Location = new Point(0, 20);
		trackRowPanel.Margin = new Padding(0, 6, 0, 0);
		trackRowPanel.Name = "panelTrack";
		trackRowPanel.Size = new Size(142, 23);
		trackRowPanel.TabIndex = 32;
		trackComboBox.FormattingEnabled = true;
		trackComboBox.Location = new Point(0, 0);
		trackComboBox.Margin = new Padding(0);
		trackComboBox.Name = "cbTrack";
		trackComboBox.Size = new Size(127, 22);
		trackComboBox.TabIndex = 9;
		discColumnPanel.Controls.Add(discLabel);
		discColumnPanel.Controls.Add(discRowPanel);
		discColumnPanel.FlowDirection = FlowDirection.TopDown;
		discColumnPanel.Location = new Point(142, 0);
		discColumnPanel.Margin = new Padding(0);
		discColumnPanel.Name = "panelDisc1";
		discColumnPanel.Size = new Size(142, 43);
		discColumnPanel.TabIndex = 41;
		discLabel.AutoSize = true;
		discLabel.Location = new Point(0, 0);
		discLabel.Margin = new Padding(0);
		discLabel.Name = "lblDisc";
		discLabel.Size = new Size(36, 14);
		discLabel.TabIndex = 32;
		discLabel.Text = "Disc: ";
		discRowPanel.Controls.Add(discComboBox);
		discRowPanel.Controls.Add(discEncodingButton);
		discRowPanel.Location = new Point(0, 20);
		discRowPanel.Margin = new Padding(0, 6, 0, 0);
		discRowPanel.Name = "panelDisc";
		discRowPanel.Size = new Size(142, 23);
		discRowPanel.TabIndex = 33;
		discComboBox.FormattingEnabled = true;
		discComboBox.Location = new Point(0, 0);
		discComboBox.Margin = new Padding(0);
		discComboBox.Name = "cbDisc";
		discComboBox.Size = new Size(127, 22);
		discComboBox.TabIndex = 11;
		genreLabel.AutoSize = true;
		genreLabel.Location = new Point(18, 274);
		genreLabel.Margin = new Padding(3, 6, 3, 0);
		genreLabel.Name = "lblGenre";
		genreLabel.Size = new Size(48, 14);
		genreLabel.TabIndex = 12;
		genreLabel.Text = "Genre: ";
		genreRowPanel.Controls.Add(genreComboBox);
		genreRowPanel.Controls.Add(genreEncodingButton);
		genreRowPanel.Location = new Point(18, 294);
		genreRowPanel.Margin = new Padding(3, 6, 3, 3);
		genreRowPanel.Name = "panelGenre";
		genreRowPanel.Size = new Size(284, 23);
		genreRowPanel.TabIndex = 32;
		genreComboBox.FormattingEnabled = true;
		genreComboBox.Location = new Point(0, 0);
		genreComboBox.Margin = new Padding(0);
		genreComboBox.Name = "cbGenre";
		genreComboBox.Size = new Size(140, 22);
		genreComboBox.TabIndex = 13;
		albumArtistLabel.AutoSize = true;
		albumArtistLabel.Location = new Point(18, 326);
		albumArtistLabel.Margin = new Padding(3, 6, 3, 0);
		albumArtistLabel.Name = "lblAlbumartist";
		albumArtistLabel.Size = new Size(76, 14);
		albumArtistLabel.TabIndex = 14;
		albumArtistLabel.Text = "Albumartist: ";
		albumArtistRowPanel.Controls.Add(albumArtistComboBox);
		albumArtistRowPanel.Controls.Add(albumArtistEncodingButton);
		albumArtistRowPanel.Location = new Point(18, 346);
		albumArtistRowPanel.Margin = new Padding(3, 6, 3, 3);
		albumArtistRowPanel.Name = "panelAlbumartist";
		albumArtistRowPanel.Size = new Size(284, 23);
		albumArtistRowPanel.TabIndex = 33;
		albumArtistComboBox.FormattingEnabled = true;
		albumArtistComboBox.Location = new Point(0, 0);
		albumArtistComboBox.Margin = new Padding(0);
		albumArtistComboBox.Name = "cbAlbumartist";
		albumArtistComboBox.Size = new Size(140, 22);
		albumArtistComboBox.TabIndex = 15;
		composerLabel.AutoSize = true;
		composerLabel.Location = new Point(18, 378);
		composerLabel.Margin = new Padding(3, 6, 3, 0);
		composerLabel.Name = "lblComposer";
		composerLabel.Size = new Size(69, 14);
		composerLabel.TabIndex = 16;
		composerLabel.Text = "Composer: ";
		composerRowPanel.Controls.Add(composerComboBox);
		composerRowPanel.Controls.Add(composerEncodingButton);
		composerRowPanel.Location = new Point(18, 398);
		composerRowPanel.Margin = new Padding(3, 6, 3, 3);
		composerRowPanel.Name = "panelComposer";
		composerRowPanel.Size = new Size(284, 23);
		composerRowPanel.TabIndex = 34;
		composerComboBox.FormattingEnabled = true;
		composerComboBox.Location = new Point(0, 0);
		composerComboBox.Margin = new Padding(0);
		composerComboBox.Name = "cbComposer";
		composerComboBox.Size = new Size(140, 22);
		composerComboBox.TabIndex = 17;
		lyricistLabel.AutoSize = true;
		lyricistLabel.Location = new Point(18, 430);
		lyricistLabel.Margin = new Padding(3, 6, 3, 0);
		lyricistLabel.Name = "lblLyricist";
		lyricistLabel.Size = new Size(51, 14);
		lyricistLabel.TabIndex = 39;
		lyricistLabel.Text = "Lyricist: ";
		lyricistRowPanel.Controls.Add(lyricistComboBox);
		lyricistRowPanel.Controls.Add(lyricistEncodingButton);
		lyricistRowPanel.Location = new Point(18, 450);
		lyricistRowPanel.Margin = new Padding(3, 6, 3, 3);
		lyricistRowPanel.Name = "panelLyricist";
		lyricistRowPanel.Size = new Size(284, 23);
		lyricistRowPanel.TabIndex = 35;
		lyricistComboBox.FormattingEnabled = true;
		lyricistComboBox.Location = new Point(0, 0);
		lyricistComboBox.Margin = new Padding(0);
		lyricistComboBox.Name = "cbLyricist";
		lyricistComboBox.Size = new Size(140, 22);
		lyricistComboBox.TabIndex = 19;
		commentLabel.AutoSize = true;
		commentLabel.Location = new Point(18, 482);
		commentLabel.Margin = new Padding(3, 6, 3, 0);
		commentLabel.Name = "lblComment";
		commentLabel.Size = new Size(68, 14);
		commentLabel.TabIndex = 18;
		commentLabel.Text = "Comment: ";
		commentRowPanel.Controls.Add(commentComboBox);
		commentRowPanel.Controls.Add(commentEncodingButton);
		commentRowPanel.Location = new Point(18, 502);
		commentRowPanel.Margin = new Padding(3, 6, 3, 3);
		commentRowPanel.Name = "panelComment";
		commentRowPanel.Size = new Size(284, 23);
		commentRowPanel.TabIndex = 36;
		commentComboBox.FormattingEnabled = true;
		commentComboBox.Location = new Point(0, 0);
		commentComboBox.Margin = new Padding(0);
		commentComboBox.Name = "cbComment";
		commentComboBox.Size = new Size(140, 22);
		commentComboBox.TabIndex = 21;
		lyricsLabel.AutoSize = true;
		lyricsLabel.Location = new Point(18, 534);
		lyricsLabel.Margin = new Padding(3, 6, 3, 0);
		lyricsLabel.Name = "lblLyric";
		lyricsLabel.Size = new Size(39, 14);
		lyricsLabel.TabIndex = 21;
		lyricsLabel.Text = "Lyric: ";
		lyricsRowPanel.Controls.Add(lyricsComboBox);
		lyricsRowPanel.Controls.Add(editLyricsButton);
		lyricsRowPanel.Controls.Add(lyricsEncodingButton);
		lyricsRowPanel.Location = new Point(18, 554);
		lyricsRowPanel.Margin = new Padding(3, 6, 3, 3);
		lyricsRowPanel.Name = "panelLyric";
		lyricsRowPanel.Size = new Size(284, 23);
		lyricsRowPanel.TabIndex = 37;
		lyricsComboBox.FormattingEnabled = true;
		lyricsComboBox.Location = new Point(0, 0);
		lyricsComboBox.Margin = new Padding(0);
		lyricsComboBox.Name = "cbLyric";
		lyricsComboBox.Size = new Size(140, 22);
		lyricsComboBox.TabIndex = 23;
		coverPanel.BackColor = SystemColors.Control;
		coverPanel.Controls.Add(coverPictureBox);
		coverPanel.Controls.Add(statusLabelsPanel);
		coverPanel.Location = new Point(18, 595);
		coverPanel.Margin = new Padding(3, 15, 3, 3);
		coverPanel.Name = "panelPicture";
		coverPanel.Size = new Size(284, 163);
		coverPanel.TabIndex = 37;
		coverPictureBox.BackColor = SystemColors.Window;
		coverPictureBox.BorderStyle = BorderStyle.Fixed3D;
		coverPictureBox.Location = new Point(0, 0);
		coverPictureBox.Margin = new Padding(0);
		coverPictureBox.Name = "picBoxPicture";
		coverPictureBox.Size = new Size(163, 163);
		coverPictureBox.SizeMode = PictureBoxSizeMode.CenterImage;
		coverPictureBox.TabIndex = 24;
		coverPictureBox.TabStop = false;
		coverPictureBox.MouseUp += CoverPicture_MouseUp;
		statusLabelsPanel.Controls.Add(coverMimeTypeLabel);
		statusLabelsPanel.Controls.Add(coverDimensionsLabel);
		statusLabelsPanel.Controls.Add(coverFileSizeLabel);
		statusLabelsPanel.Controls.Add(coverPictureTypeLabel);
		statusLabelsPanel.Controls.Add(overwriteCoverCheckBox);
		statusLabelsPanel.Dock = DockStyle.Fill;
		statusLabelsPanel.FlowDirection = FlowDirection.TopDown;
		statusLabelsPanel.Location = new Point(163, 0);
		statusLabelsPanel.Margin = new Padding(0);
		statusLabelsPanel.Name = "flowLayoutPanel2";
		statusLabelsPanel.Padding = new Padding(3);
		statusLabelsPanel.Size = new Size(121, 163);
		statusLabelsPanel.TabIndex = 21;
		statusLabelsPanel.WrapContents = false;
		statusLabelsPanel.SizeChanged += StatusLabelsPanel_SizeChanged;
		statusLabelsPanel.SetFlowBreak(coverMimeTypeLabel, value: true);
		coverMimeTypeLabel.Location = new Point(6, 3);
		coverMimeTypeLabel.Name = "lblPicMimeType";
		coverMimeTypeLabel.Size = new Size(115, 20);
		coverMimeTypeLabel.TabIndex = 0;
		coverMimeTypeLabel.TextAlign = ContentAlignment.MiddleCenter;
		statusLabelsPanel.SetFlowBreak(coverDimensionsLabel, value: true);
		coverDimensionsLabel.Location = new Point(6, 23);
		coverDimensionsLabel.Name = "lblPicRatio";
		coverDimensionsLabel.Size = new Size(115, 20);
		coverDimensionsLabel.TabIndex = 1;
		coverDimensionsLabel.TextAlign = ContentAlignment.MiddleCenter;
		statusLabelsPanel.SetFlowBreak(coverFileSizeLabel, value: true);
		coverFileSizeLabel.Location = new Point(6, 43);
		coverFileSizeLabel.Name = "lblPicSize";
		coverFileSizeLabel.Size = new Size(115, 20);
		coverFileSizeLabel.TabIndex = 2;
		coverFileSizeLabel.TextAlign = ContentAlignment.MiddleCenter;
		statusLabelsPanel.SetFlowBreak(coverPictureTypeLabel, value: true);
		coverPictureTypeLabel.Location = new Point(6, 63);
		coverPictureTypeLabel.Name = "lblPicType";
		coverPictureTypeLabel.Size = new Size(115, 20);
		coverPictureTypeLabel.TabIndex = 3;
		coverPictureTypeLabel.TextAlign = ContentAlignment.MiddleCenter;
		coverNavigationPanel.AutoSize = true;
		coverNavigationPanel.Controls.Add(previousCoverButton);
		coverNavigationPanel.Controls.Add(coverIndexLabel);
		coverNavigationPanel.Controls.Add(nextCoverButton);
		coverNavigationPanel.Location = new Point(18, 764);
		coverNavigationPanel.Name = "flowLayoutPanel3";
		coverNavigationPanel.Size = new Size(168, 29);
		coverNavigationPanel.TabIndex = 24;
		coverNavigationPanel.Visible = false;
		previousCoverButton.Location = new Point(3, 3);
		previousCoverButton.Name = "btnPrePic";
		previousCoverButton.Size = new Size(30, 23);
		previousCoverButton.TabIndex = 0;
		previousCoverButton.Text = "<";
		previousCoverButton.UseVisualStyleBackColor = true;
		previousCoverButton.Click += PreviousCover_Click;
		coverIndexLabel.Location = new Point(39, 3);
		coverIndexLabel.Margin = new Padding(3, 3, 3, 0);
		coverIndexLabel.Name = "lblPicIdx";
		coverIndexLabel.Size = new Size(90, 23);
		coverIndexLabel.TabIndex = 1;
		coverIndexLabel.Text = "1/1";
		coverIndexLabel.TextAlign = ContentAlignment.MiddleCenter;
		nextCoverButton.Location = new Point(135, 3);
		nextCoverButton.Name = "btnNextPic";
		nextCoverButton.Size = new Size(30, 23);
		nextCoverButton.TabIndex = 2;
		nextCoverButton.Text = ">";
		nextCoverButton.UseVisualStyleBackColor = true;
		nextCoverButton.Click += NextCover_Click;
		tagEditorBottomSpacerPanel.Location = new Point(15, 796);
		tagEditorBottomSpacerPanel.Margin = new Padding(0);
		tagEditorBottomSpacerPanel.Name = "panelDummy";
		tagEditorBottomSpacerPanel.Size = new Size(143, 20);
		tagEditorBottomSpacerPanel.TabIndex = 38;
		fileListView.AllowDrop = true;
		fileListView.Dock = DockStyle.Fill;
		fileListView.FullRowSelect = true;
		fileListView.HideSelection = false;
		fileListView.LabelEdit = true;
		fileListView.Location = new Point(0, 0);
		fileListView.Margin = new Padding(0);
		fileListView.Name = "listView1";
		fileListView.Size = new Size(1263, 806);
		fileListView.TabIndex = 31;
		fileListView.UseCompatibleStateImageBehavior = false;
		fileListView.View = View.Details;
		fileListView.HeaderRightClick += FileListHeader_RightClick;
		fileListView.AfterLabelEdit += FileList_AfterLabelEdit;
		fileListView.BeforeLabelEdit += FileList_BeforeLabelEdit;
		fileListView.ColumnClick += FileList_ColumnClick;
		fileListView.ItemSelectionChanged += FileList_ItemSelectionChanged;
		fileListView.DragDrop += FileList_DragDrop;
		fileListView.DragEnter += FileList_DragEnter;
		fileListView.DragLeave += FileList_DragLeave;
		fileListView.MouseUp += FileList_MouseUp;
		fileFilterStatusStrip.Items.AddRange(new ToolStripItem[3] { filterStatusLabel, filterTextBox, filterTypeDropDownButton });
		fileFilterStatusStrip.Location = new Point(0, 806);
		fileFilterStatusStrip.Name = "statusStrip2";
		fileFilterStatusStrip.Size = new Size(1263, 27);
		fileFilterStatusStrip.SizingGrip = false;
		fileFilterStatusStrip.TabIndex = 32;
		fileFilterStatusStrip.Text = "statusStrip2";
		fileFilterStatusStrip.SizeChanged += FilterBar_SizeChanged;
		filterStatusLabel.Name = "filterToolStripStatusLabel";
		filterStatusLabel.Size = new Size(39, 22);
		filterStatusLabel.Text = "Filter:";
		filterTextBox.AutoSize = false;
		filterTextBox.Margin = new Padding(1, 3, 1, 3);
		filterTextBox.Name = "filterToolStripTextBox";
		filterTextBox.Size = new Size(500, 21);
		filterTextBox.TextChanged += FilterInput_TextChanged;
		filterTypeDropDownButton.AutoSize = false;
		filterTypeDropDownButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
		filterTypeDropDownButton.Image = (Image)componentResourceManager.GetObject("filterToolStripDropDownButton.Image");
		filterTypeDropDownButton.ImageTransparentColor = Color.Magenta;
		filterTypeDropDownButton.Margin = new Padding(0, 3, 0, 3);
		filterTypeDropDownButton.Name = "filterToolStripDropDownButton";
		filterTypeDropDownButton.Size = new Size(100, 21);
		fileSummaryStatusStrip.Font = new Font("Tahoma", 9f, FontStyle.Regular, GraphicsUnit.Point, 0);
		fileSummaryStatusStrip.Items.AddRange(new ToolStripItem[3] { selectedFilesStatusLabel, fileSummaryStatusSeparator, totalFilesStatusLabel });
		fileSummaryStatusStrip.Location = new Point(0, 833);
		fileSummaryStatusStrip.Name = "statusStrip1";
		fileSummaryStatusStrip.Size = new Size(1263, 23);
		fileSummaryStatusStrip.TabIndex = 1;
		fileSummaryStatusStrip.Text = "statusStrip1";
		selectedFilesStatusLabel.AutoSize = false;
		selectedFilesStatusLabel.Name = "seledFilesToolStripStatusLabel";
		selectedFilesStatusLabel.Size = new Size(190, 18);
		selectedFilesStatusLabel.Text = "0 (00:00:00 0Byte) ";
		selectedFilesStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
		fileSummaryStatusSeparator.BackColor = SystemColors.Control;
		fileSummaryStatusSeparator.Name = "toolSeparator1";
		fileSummaryStatusSeparator.Size = new Size(6, 23);
		totalFilesStatusLabel.Name = "allFilesToolStripStatusLabel";
		totalFilesStatusLabel.Size = new Size(118, 18);
		totalFilesStatusLabel.Text = "0 (00:00:00 0Byte) ";
		totalFilesStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
		base.AutoScaleDimensions = new SizeF(96f, 96f);
		base.AutoScaleMode = AutoScaleMode.Dpi;
		base.ClientSize = new Size(1589, 906);
		Controls.Add(mainSplitContainer);
		base.Controls.Add(mainToolStrip);
		base.Controls.Add(mainMenuStrip);
		Font = new Font("Tahoma", 9f, FontStyle.Regular, GraphicsUnit.Point, 0);
		IsMdiContainer = true;
		base.MainMenuStrip = mainMenuStrip;
		base.Name = "FormMain";
		Text = "Music Tag";
		base.SizeChanged += MainForm_SizeChanged;
		mainMenuStrip.ResumeLayout(performLayout: false);
		mainMenuStrip.PerformLayout();
		mainToolStrip.ResumeLayout(performLayout: false);
		mainToolStrip.PerformLayout();
		coverContextMenu.ResumeLayout(performLayout: false);
		fileListHeaderContextMenu.ResumeLayout(performLayout: false);
		fileListItemContextMenu.ResumeLayout(performLayout: false);
		notifyContextMenu.ResumeLayout(performLayout: false);
		mainSplitContainer.Panel1.ResumeLayout(performLayout: false);
		mainSplitContainer.Panel2.ResumeLayout(performLayout: false);
		mainSplitContainer.Panel2.PerformLayout();
		((ISupportInitialize)mainSplitContainer).EndInit();
		mainSplitContainer.ResumeLayout(performLayout: false);
		tagEditorPanel.ResumeLayout(performLayout: false);
		tagEditorPanel.PerformLayout();
		titleRowPanel.ResumeLayout(performLayout: false);
		artistRowPanel.ResumeLayout(performLayout: false);
		albumRowPanel.ResumeLayout(performLayout: false);
		yearRowPanel.ResumeLayout(performLayout: false);
		trackDiscGroupPanel.ResumeLayout(performLayout: false);
		trackColumnPanel.ResumeLayout(performLayout: false);
		trackColumnPanel.PerformLayout();
		trackRowPanel.ResumeLayout(performLayout: false);
		discColumnPanel.ResumeLayout(performLayout: false);
		discColumnPanel.PerformLayout();
		discRowPanel.ResumeLayout(performLayout: false);
		genreRowPanel.ResumeLayout(performLayout: false);
		albumArtistRowPanel.ResumeLayout(performLayout: false);
		composerRowPanel.ResumeLayout(performLayout: false);
		lyricistRowPanel.ResumeLayout(performLayout: false);
		commentRowPanel.ResumeLayout(performLayout: false);
		lyricsRowPanel.ResumeLayout(performLayout: false);
		coverPanel.ResumeLayout(performLayout: false);
		((ISupportInitialize)coverPictureBox).EndInit();
		statusLabelsPanel.ResumeLayout(performLayout: false);
		statusLabelsPanel.PerformLayout();
		coverNavigationPanel.ResumeLayout(performLayout: false);
		fileFilterStatusStrip.ResumeLayout(performLayout: false);
		fileFilterStatusStrip.PerformLayout();
		fileSummaryStatusStrip.ResumeLayout(performLayout: false);
		fileSummaryStatusStrip.PerformLayout();
		ResumeLayout(performLayout: false);
		PerformLayout();
	}

	private void ApplyTagPanelLayoutAfterResize()
	{
		ApplyTagPanelLayout();
	}

	private void ResizeStatusLabels()
	{
		Size statusLabelSize = new Size(statusLabelsPanel.Width - statusLabelsPanel.Padding.Left - statusLabelsPanel.Padding.Right, DatabaseMapper.ScaleByDpi(20f));
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

	private bool ValidateNumberedTagField(string fieldName, string message)
	{
		ComboBox comboBox = tagComboBoxes[fieldName];
		string text = comboBox.Text;
		if (string.IsNullOrWhiteSpace(text) || !(text != "<keep>") || !(text != "<blank>") || ParseLeadingNumber(text) > 0)
		{
			return true;
		}
		DatabaseMapper.ShowErrorMessage(message);
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






