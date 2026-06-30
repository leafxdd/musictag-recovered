using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Resources;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MusicTag.Candidates;
using MusicTag.Readers;
using MusicTag.States;
using MusicTagWinApp;
using MusicTagWinApp.Common;
using MusicTagWinApp.Containers;
using MusicTagWinApp.Instances;
using MusicTagWinApp.Listeners;
using MusicTagWinApp.Properties;
using MusicTagWinApp.Roles;
using Newtonsoft.Json;

namespace MusicTag.Schemes;

internal class FilenameRelatedBatchDialog : Form
{
		internal sealed class FilenameRegexCaptureExtractor
		{
		private sealed class MaskedFilenameVariant
		{
			public int MaskDepth;

			public string Text;

			public List<string> ProtectedSegments = new List<string>();
		}

			private static readonly Regex ProtectedSegmentRegex = new Regex("\\([^()]*\\)|\\[[^\\[\\]]*\\]|\\{[^{}]*\\}|<[^<>]*>|（[^（）]*）|【[^【】]*】|《[^《》]*》|“[^“”]*”|‘[^‘’]*’|『[^『』]*』|「[^「」]*」");

			private static readonly ConcurrentDictionary<string, Regex> RegexCache = new ConcurrentDictionary<string, Regex>(StringComparer.Ordinal);

		private readonly List<MaskedFilenameVariant> maskedVariants = new List<MaskedFilenameVariant>();

		public List<string> Captures { get; } = new List<string>();

			public FilenameRegexCaptureExtractor(string filename, string regexPattern)
			{
			int maskDepth = 0;
			string maskedFilename = filename;
			while ((maskedFilename = MaskProtectedSegments(maskedFilename, maskDepth++)) != null)
			{
			}
			if (!maskedVariants.Any())
			{
				maskedVariants.Add(new MaskedFilenameVariant
				{
					Text = filename,
					MaskDepth = 0
				});
				}
				maskedVariants.Reverse();
				Regex regex = RegexCache.GetOrAdd(regexPattern, pattern => new Regex(pattern));
				Match match = null;
			int matchedVariantIndex = -1;
			for (int index = 0; index < maskedVariants.Count; index++)
			{
				match = regex.Match(maskedVariants[index].Text);
				if (match.Success)
				{
					matchedVariantIndex = index;
					break;
				}
			}
			if (matchedVariantIndex < 0)
			{
				return;
			}
			for (int groupIndex = 1; groupIndex < match.Groups.Count; groupIndex++)
			{
				string capture = match.Groups[groupIndex].Value;
				// 与上面 variant.Text 修正耦合:Text 改存 mask 后串后,命中(最深)变体自身引入的占位符也须
				// 还原,故起点含 matchedVariantIndex 本身(原 +1 只适配旧的 mask-前-Text 语义)。
				for (int variantIndex = matchedVariantIndex; variantIndex < maskedVariants.Count; variantIndex++)
				{
					capture = RestoreProtectedSegments(capture, maskedVariants[variantIndex]);
				}
				Captures.Add(capture);
			}
		}

		private string MaskProtectedSegments(string filename, int maskDepth)
		{
			if (!ProtectedSegmentRegex.IsMatch(filename))
			{
				return null;
			}
			MaskedFilenameVariant variant = new MaskedFilenameVariant
			{
				MaskDepth = maskDepth
			};
			int segmentIndex = 0;
			string maskedFilename = ProtectedSegmentRegex.Replace(filename, match =>
			{
				variant.ProtectedSegments.Add(match.Value);
				// segmentIndex 同 maskDepth 定宽 D5:令占位符等长,杜绝 seg1 占位符成为 seg10 的前缀而被
				// String.Replace 串扰(同层 >=11 段;masking 生效后此还原路径才首次真正运行)。
				return $"\t{maskDepth:D5}{segmentIndex++:D5}";
			});
			// 行为修正(非逐字节等价):原 variant.Text 误存 mask 前原文,最深 masked 版本从未进入
			// maskedVariants、匹配退化到未屏蔽原文 -> 括号内分隔符未被保护。改存 mask 后文本(占位符
			// 版)使括号保护段对捕获分组真正生效;还原循环自最深变体起逐层把占位符恢复为原段。
			variant.Text = maskedFilename;
			maskedVariants.Add(variant);
			return maskedFilename;
		}

		private static string RestoreProtectedSegments(string capture, MaskedFilenameVariant variant)
		{
			for (int segmentIndex = 0; segmentIndex < variant.ProtectedSegments.Count; segmentIndex++)
			{
				capture = capture.Replace($"\t{variant.MaskDepth:D5}{segmentIndex:D5}", variant.ProtectedSegments[segmentIndex]);
			}
			return capture;
		}
	}

	private static bool IsCheckedRadioButton(Control control)
	{
		return control is RadioButton radioButton && radioButton.Checked;
	}

	private static ComboBox GetCaptureGroupComboBox(AssociatedValueListViewItem item)
	{
		return (item.SubItems["match"] as EmbeddedControlSubItem).EmbeddedControl as ComboBox;
	}

	private static void ResetComboBoxSelection(ComboBox comboBox)
	{
		comboBox.SelectedIndex = 0;
	}

	private static (int CaptureGroup, int SelectedIndex) CreateCaptureGroupSelection(AssociatedValueListViewItem item)
	{
		return (int.Parse(item.Text), GetCaptureGroupComboBox(item).SelectedIndex);
	}

	private static bool HasSelectedCaptureGroup((int CaptureGroup, int SelectedIndex) item)
	{
		return item.SelectedIndex > 0;
	}

	private static bool IsDigitCharacter(char character)
	{
		return char.IsDigit(character);
	}

	// 由文件名模板(@1..@8 占位符)与各 tag 字段渲染目标文件名,并清理为合法文件名:
	// 路径分隔符 \ / -> ;,其余文件名非法字符(空白及 " : * ? < > |)-> 空格。
	// 提取自 RenameFilesBatchWorker.RenameFiles 的内联逻辑(行为逐字保持),供 characterization 锁定。
	internal static string RenderRenameFilename(string filenamePattern, string title, string artist, string album, string disc, string trackNumber, string year, string comment, string albumArtist)
	{
		string newFilename = filenamePattern.Replace("@1", title).Replace("@2", artist).Replace("@3", album).Replace("@4", disc).Replace("@5", trackNumber)
			.Replace("@6", year)
			.Replace("@7", comment)
			.Replace("@8", albumArtist);
		newFilename = Regex.Replace(newFilename, "[\\\\/]", ";");
		newFilename = Regex.Replace(newFilename, "[\\s\":*?<>|]", " ");
		return newFilename;
	}

	// 由渲染后的目标文件名构造最终音频目标路径,并处理同名冲突:目标已存在【且】新文件名与原文件名
	// (不含扩展名,大小写不敏感)不同时,追加 " (N)"(N 从 1 递增)直到空位;若新旧同名(仅大小写/无变化)
	// 则原样返回不去重(避免把原地改名误判为冲突)。fileExists 注入存在性判定(生产端传 File.Exists,逐字节等价)。
	// 提取自 RenameFilesBatchWorker.RenameFiles 的内联逻辑(行为逐字保持),供 characterization 锁定。
	internal static string ResolveDestinationAudioPath(string originalPath, string newFilename, Func<string, bool> fileExists)
	{
		string destinationAudioPath = Path.GetDirectoryName(originalPath) + "\\" + newFilename + Path.GetExtension(originalPath);
		if (fileExists(destinationAudioPath) && !string.Equals(newFilename, Path.GetFileNameWithoutExtension(originalPath), StringComparison.OrdinalIgnoreCase))
		{
			int duplicateIndex = 1;
			while (true)
			{
				destinationAudioPath = Path.GetDirectoryName(originalPath) + "\\" + newFilename + " (" + duplicateIndex + ")" + Path.GetExtension(originalPath);
				if (!fileExists(destinationAudioPath))
				{
					break;
				}
				duplicateIndex++;
			}
		}
		return destinationAudioPath;
	}

	// @1/@2 必填校验:重命名模板含 @1(标题)/@2(艺术家)占位符却对应 tag 为空时,该文件跳过(计为失败,
	// 报 "Title or artist tags are empty")。else if 短路:@1 缺失即判定,不再查 @2。
	// 提取自 RenameFilesBatchWorker.RenameFiles 的内联逻辑(行为逐字保持),供 characterization 锁定。
	internal static bool IsRequiredTagMissing(string filenamePattern, string title, string artist)
	{
		bool isMissingRequiredTag = false;
		if (filenamePattern.Contains("@1") && !title.Any())
		{
			isMissingRequiredTag = true;
		}
		else if (filenamePattern.Contains("@2") && !artist.Any())
		{
			isMissingRequiredTag = true;
		}
		return isMissingRequiredTag;
	}

	// 把文件名模板(@1..@8 占位符 + 字面量)编译为匹配用正则:先把字面量里的正则元字符逐一转义,
	// 再把每段连续占位符 (@[0-8])+ 记为一个 token 并整体替换为捕获组 (.*)。返回 (正则, token 列表)。
	// 提取自 ChangeTags 的内联逻辑(行为逐字保持),供 characterization 锁定。
	internal static (string regex, List<string> tokens) BuildFilenameMatchRegex(string filenamePattern)
	{
		string filenamePatternRegex = Regex.Replace(filenamePattern, "\\s", " ");
		filenamePatternRegex = filenamePatternRegex.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)").Replace("[", "\\[").Replace("]", "\\]").Replace("{", "\\{").Replace("}", "\\}").Replace("^", "\\^").Replace("$", "\\$")
			.Replace("?", "\\?")
			.Replace("*", "\\*")
			.Replace("+", "\\+")
			.Replace(".", "\\.")
			.Replace("|", "\\|");
		List<string> patternTokens = new List<string>();
		foreach (Match tokenMatch in Regex.Matches(filenamePatternRegex, "(@[0-8])+"))
		{
			patternTokens.Add(tokenMatch.Value);
		}
		filenamePatternRegex = Regex.Replace(filenamePatternRegex, "(@[0-8])+", "(.*)");
		return (filenamePatternRegex, patternTokens);
	}

	// 处理"disc/track 组合占位符紧跟另一占位符"的连写 token(如 @4@5@1):把捕获文本按数字前缀
	// ^(\d*)(.*)$ 拆成(数字段 -> disc/track 组合占位符, 余下 -> 后一占位符),每段非空白才设置;
	// 非此形态的 token 原样设置。提取自 ChangeTags 内联逻辑(行为逐字保持),返回 (占位符, 值) 赋值序列。
	internal static List<(string parameter, string value)> SplitCombinedDiscTrackCapture(string patternToken, string capturedText)
	{
		List<(string parameter, string value)> assignments = new List<(string parameter, string value)>();
		Match combinedDiscTrackMatch = Regex.Match(patternToken, "^(@4@5|@4|@5)(@[0-8])$");
		Match numericPrefixMatch = Regex.Match(capturedText, "^(\\d*)(.*)$");
		if (combinedDiscTrackMatch.Success)
		{
			if (numericPrefixMatch.Success)
			{
				if (!string.IsNullOrWhiteSpace(numericPrefixMatch.Groups[1].Value))
				{
					assignments.Add((combinedDiscTrackMatch.Groups[1].Value, numericPrefixMatch.Groups[1].Value));
				}
				if (!string.IsNullOrWhiteSpace(numericPrefixMatch.Groups[2].Value))
				{
					assignments.Add((combinedDiscTrackMatch.Groups[2].Value, numericPrefixMatch.Groups[2].Value));
				}
			}
		}
		else
		{
			assignments.Add((patternToken, capturedText));
		}
		return assignments;
	}

	private sealed class RenameFilesBatchWorker
	{
		public CancellationTokenSource CancellationTokenSource;

		public string CurrentFileName;

		public ProgressDialog ProgressDialog;

		public FilenameRelatedBatchDialog Owner;

		public (string path, string _, int lvIndex)[] RenameItems;

		public ListViewFileSetting FileSettings;

		public Action<string, string> ReportFailure;

		internal void Cancel()
		{
			if (!CancellationTokenSource.IsCancellationRequested)
			{
				CancellationTokenSource.Cancel();
			}
		}

		internal void UpdateProgress()
		{
			if (CurrentFileName != null)
			{
				ProgressDialog.UpdateStatisticsProgress(Resources.Msg_Rename + CurrentFileName, Owner.processedCount, RenameItems.Length, Owner.successCount, Owner.failureCount, Owner.skippedCount, RenameItems.Length);
			}
		}

		internal void ReportRenameFailure(string path, string message)
		{
			string failureMessage = string.IsNullOrWhiteSpace(message) ? Resources.Msg_SaveFail : message;
			LogService.WriteRenameLog(path + ": " + failureMessage);
			Owner.batchMessages.AddLine(Path.GetFileName(path));
			Owner.batchMessages.AddLine(failureMessage);
		}

		internal void RenameFiles()
		{
			Owner.historyTransaction = new TagHistoryRepository(useTransaction: true);
			try
			{
				TagHistoryRepository.ClearUndoState();
				for (int index = 0; index < RenameItems.Length && !CancellationTokenSource.IsCancellationRequested; index++)
				{
				var (originalPath, _, listViewIndex) = RenameItems[index];
				using (ConfigDescriptorState configDescriptorState = new ConfigDescriptorState(originalPath))
				{
					if (configDescriptorState.IsLoadedSuccessfully())
					{
						configDescriptorState.LoadBasicTagFields();
						configDescriptorState.Dispose();
						string title = TextUtilities.CoalesceNonBlank(configDescriptorState.GetDisplayValue("title")).Trim();
						string artist = TextUtilities.CoalesceNonBlank(configDescriptorState.GetDisplayValue("artist")).Trim();
						string album = TextUtilities.CoalesceNonBlank(configDescriptorState.GetDisplayValue("album")).Trim();
						string disc = TextUtilities.CoalesceNonBlank(configDescriptorState.GetDisplayValue("disc")).Trim();
						string trackNumber = string.Format("{0:D2}", configDescriptorState["track"]);
						string year = TextUtilities.CoalesceNonBlank(configDescriptorState.GetDisplayValue("year")).Trim();
						string comment = TextUtilities.CoalesceNonBlank(configDescriptorState.GetDisplayValue("comment")).Trim();
						string albumArtist = TextUtilities.CoalesceNonBlank(configDescriptorState.GetDisplayValue("albumartist")).Trim();
						bool isMissingRequiredTag = IsRequiredTagMissing(Owner.selectedFilenamePattern, title, artist);
						if (!isMissingRequiredTag)
						{
							try
							{
								string newFilename = RenderRenameFilename(Owner.selectedFilenamePattern, title, artist, album, disc, trackNumber, year, comment, albumArtist);
								if (!string.IsNullOrWhiteSpace(newFilename))
								{
									string sourceLrcPath = null;
									string sourceImagePath = null;
									if (Owner.renameRelatedFiles)
									{
										sourceLrcPath = PathFileUtilities.GetSiblingPathWithExtension(originalPath, ".lrc");
										if (!File.Exists(sourceLrcPath))
										{
											sourceLrcPath = null;
										}
										sourceImagePath = ImageUtilities.FindExistingSiblingImageFile(originalPath);
									}
									string destinationAudioPath = ResolveDestinationAudioPath(originalPath, newFilename, File.Exists);
									string destinationLrcPath = null;
									if (sourceLrcPath != null)
									{
										destinationLrcPath = PathFileUtilities.GetSiblingPathWithExtension(destinationAudioPath, ".lrc");
										if (File.Exists(destinationLrcPath) && !string.Equals(destinationLrcPath, sourceLrcPath, StringComparison.OrdinalIgnoreCase))
										{
											destinationLrcPath = null;
										}
									}
									string destinationImagePath = null;
									if (sourceImagePath != null)
									{
										destinationImagePath = PathFileUtilities.GetSiblingPathWithExtension(destinationAudioPath, Path.GetExtension(sourceImagePath));
										if (File.Exists(destinationImagePath) && !string.Equals(destinationImagePath, sourceImagePath, StringComparison.OrdinalIgnoreCase))
										{
											destinationImagePath = null;
										}
									}
									newFilename = Path.GetFileName(destinationAudioPath);
									if (newFilename != Path.GetFileName(originalPath))
									{
										try
										{
											PathFileUtilities.MoveFileAllowingCaseOnlyRename(originalPath, destinationAudioPath);
											// 音频本体已移动到新路径,内部状态必须立即无条件回写,
											// 否则列表/历史/撤销会指向已不存在的旧路径(文件"失踪")。
											RenameItems[index] = (path: originalPath, _: destinationAudioPath, lvIndex: listViewIndex);
											FileSettings.UpdateForAnyFile(originalPath, destinationAudioPath);
											TagHistoryRepository.UpdateHistoryFilePath(originalPath, destinationAudioPath, Owner.historyTransaction);
											TagHistoryRepository.AddRenameUndoRecord(originalPath, destinationAudioPath);
											Owner.successCount++;
											// 关联文件(歌词/封面)为尽力而为:移动失败只记录告警,不回退已成功的音频改名。
											if (sourceLrcPath != null && destinationLrcPath != null)
											{
												try
												{
													PathFileUtilities.MoveFileAllowingCaseOnlyRename(sourceLrcPath, destinationLrcPath);
												}
												catch (Exception lrcEx)
												{
													ReportFailure(sourceLrcPath, lrcEx.Message);
												}
											}
											if (sourceImagePath != null && destinationImagePath != null)
											{
												try
												{
													PathFileUtilities.MoveFileAllowingCaseOnlyRename(sourceImagePath, destinationImagePath);
												}
												catch (Exception imageEx)
												{
													ReportFailure(sourceImagePath, imageEx.Message);
												}
											}
										}
										catch (Exception ex)
										{
											ReportFailure(originalPath, ex.Message);
											Owner.failureCount++;
										}
									}
									else
									{
										Owner.skippedCount++;
									}
								}
								else
								{
									ReportFailure(originalPath, "Rename fail");
									Owner.failureCount++;
								}
							}
							catch (Exception ex2)
							{
								ReportFailure(originalPath, ex2.Message);
								Owner.failureCount++;
							}
						}
						else
						{
							ReportFailure(originalPath, "Title or artist tags are empty");
							Owner.failureCount++;
						}
					}
					else
					{
						ReportFailure(originalPath, configDescriptorState.GetLoadError());
						Owner.failureCount++;
					}
				}
				Owner.processedCount++;
			}
			}
			finally
			{
				Owner.historyTransaction.Dispose();
			}
		}
	}

	private sealed class ChangeTagsBatchWorker
	{
		public CancellationTokenSource CancellationTokenSource;

		public string CurrentFileName;

		public ProgressDialog ProgressDialog;

		public FilenameRelatedBatchDialog Owner;

		public string[] FilePaths;

		public bool CanCancelReadOnly;

		public Action<string, string> ReportFailure;

		internal void Cancel()
		{
			if (!CancellationTokenSource.IsCancellationRequested)
			{
				CancellationTokenSource.Cancel();
			}
		}

		internal void UpdateProgress()
		{
			if (CurrentFileName != null)
			{
				ProgressDialog.UpdateStatisticsProgress(Resources.Msg_Savetag + CurrentFileName, Owner.processedCount, FilePaths.Length, Owner.successCount, Owner.failureCount, Owner.skippedCount, FilePaths.Length);
			}
		}

		internal void ReportTagSaveFailure(string path, string message)
		{
			string failureMessage = string.IsNullOrWhiteSpace(message) ? Resources.Msg_SaveFail : message;
			LogService.WriteSaveTagsLog(path + ": " + failureMessage);
			Owner.batchMessages.AddLine(Path.GetFileName(path));
			Owner.batchMessages.AddLine(failureMessage);
		}

		internal void ChangeTags()
		{
			Owner.historyTransaction = new TagHistoryRepository(useTransaction: true);
			try
			{
				TagHistoryRepository.ClearUndoState();
				foreach (string filePath in FilePaths)
				{
					if (CancellationTokenSource.IsCancellationRequested)
					{
						break;
					}
					FileInfo fileInfo = new FileInfo(filePath);
					DateTime lastWriteTime = default(DateTime);
					bool savedTags = false;
					ConfigDescriptorState tagState = null;
					try
					{
						PathFileUtilities.ClearReadOnlyIfAllowed(fileInfo, CanCancelReadOnly);
						lastWriteTime = fileInfo.LastWriteTime;
						tagState = new ConfigDescriptorState(filePath);
						if (tagState.IsLoadedSuccessfully())
						{
							PendingTagUpdate tagUpdate = new PendingTagUpdate(tagState);
							tagState.LoadBasicTagFields();
							tagState.LoadLyrics();
							ConfigDescriptorState originalTagSnapshot = TagHistoryRepository.CreateTagSnapshot(tagState, false);
							string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
							fileNameWithoutExtension = Regex.Replace(fileNameWithoutExtension, "\\s", " ");
							if (Owner.usesRegexCaptureGroups)
							{
								string pattern = Regex.Replace(Owner.filenameRegexPattern, "\\s", " ");
								Match match;
								if ((match = Regex.Match(fileNameWithoutExtension, pattern)) != null && match.Success)
								{
									for (int groupIndex = 1; groupIndex < match.Groups.Count; groupIndex++)
									{
										if (Owner.regexCaptureGroupMap.TryGetValue(groupIndex, out var selectedTagIndex))
										{
											string capturedValue = match.Groups[groupIndex].Value.Trim();
											tagUpdate.SetRegexCaptureTag(selectedTagIndex, capturedValue);
										}
									}
								}
							}
							else
							{
								var (filenamePatternRegex, patternTokens) = BuildFilenameMatchRegex(Owner.selectedFilenamePattern);
								FilenameRegexCaptureExtractor captureExtractor = new FilenameRegexCaptureExtractor(fileNameWithoutExtension, filenamePatternRegex);
								for (int captureIndex = 0; captureIndex < captureExtractor.Captures.Count; captureIndex++)
								{
									string patternToken = (captureIndex < patternTokens.Count) ? patternTokens[captureIndex] : null;
									string capturedText = captureExtractor.Captures[captureIndex].Trim();
									if (patternToken == null)
									{
										continue;
									}
									foreach (var (parameter, value) in SplitCombinedDiscTrackCapture(patternToken, capturedText))
									{
										tagUpdate.SetFilenamePatternTag(parameter, value);
									}
								}
							}
							if (tagUpdate.Changes.Any())
							{
								tagUpdate.ApplyChanges();
								if (tagState.SaveTagFields())
								{
									savedTags = true;
									var (historyError, selection) = TagHistoryRepository.AddHistoryRecordIfChanged(filePath, originalTagSnapshot, tagState, Owner.historyTransaction);
									if (historyError != null)
									{
										ReportFailure(filePath, historyError);
									}
									string undoError = TagHistoryRepository.AddUndoRecord(originalTagSnapshot, selection, Owner.historyTransaction);
									if (undoError != null)
									{
										ReportFailure(filePath, undoError);
									}
									Owner.successCount++;
								}
								else
								{
									ReportFailure(filePath, tagState.GetLoadError());
									Owner.failureCount++;
								}
							}
							else
							{
								Owner.skippedCount++;
							}
						}
						else
						{
							ReportFailure(filePath, tagState.GetLoadError());
							Owner.failureCount++;
						}
					}
					catch (Exception ex)
					{
						ReportFailure(filePath, ex.Message);
						Owner.failureCount++;
					}
					finally
					{
						if (tagState != null)
						{
							tagState.Dispose();
						}
					}
					if (savedTags && Settings.Default.SaveTagsKeepUpdateTime)
					{
						try
						{
							fileInfo.LastWriteTime = lastWriteTime;
						}
						catch (Exception ex)
						{
							ReportFailure(filePath, ex.Message);
						}
					}
					Owner.processedCount++;
				}
			}
			finally
			{
				Owner.historyTransaction.Dispose();
			}
		}
	}

	internal sealed class PendingTagUpdate
	{
		public PendingTagUpdate(ConfigDescriptorState tagState)
		{
			TagState = tagState;
		}

		public ConfigDescriptorState TagState { get; }

		public Dictionary<string, string> Changes { get; } = new Dictionary<string, string>();

		public void SetNumberedTag(string tagName, string value)
		{
			if (value.All(IsDigitCharacter) && int.TryParse(value, out var number))
			{
				Changes[tagName] = (number > 0) ? number.ToString() : "";
			}
			else
			{
				Changes[tagName] = value;
			}
		}

		public void SetFilenamePatternTag(string parameter, string value)
		{
			switch (parameter)
			{
				case "@1":
					SetTextTagIfChanged("title", value);
					break;
				case "@2":
					SetTextTagIfChanged("artist", value);
					break;
				case "@3":
					SetTextTagIfChanged("album", value);
					break;
				case "@4":
					if (int.TryParse(value, out var discNumber))
					{
						Changes["discstr"] = discNumber.ToString();
					}
					break;
				case "@5":
					if (int.TryParse(value, out var trackNumber))
					{
						Changes["trackstr"] = trackNumber.ToString();
					}
					break;
				case "@6":
					SetTextTagIfChanged("year", value);
					break;
				case "@7":
					SetTextTagIfChanged("comment", value);
					break;
				case "@8":
					SetTextTagIfChanged("albumartist", value);
					break;
				case "@4@5":
					if (int.TryParse(value, out var discAndTrack))
					{
						Changes["discstr"] = (discAndTrack / 100).ToString();
						Changes["trackstr"] = (discAndTrack % 100).ToString();
					}
					break;
			}
		}

		public void SetRegexCaptureTag(int selectedTagIndex, string capturedValue)
		{
			switch (selectedTagIndex)
			{
				case 1:
					Changes["title"] = capturedValue;
					break;
				case 2:
					Changes["artist"] = capturedValue;
					break;
				case 3:
					Changes["album"] = capturedValue;
					break;
				case 4:
					SetNumberedTag("discstr", capturedValue);
					break;
				case 5:
					SetNumberedTag("trackstr", capturedValue);
					break;
				case 6:
					Changes["year"] = capturedValue;
					break;
				case 7:
					Changes["comment"] = capturedValue;
					break;
				case 8:
					Changes["albumartist"] = capturedValue;
					break;
			}
		}

		public void ApplyChanges()
		{
			foreach (KeyValuePair<string, string> change in Changes)
			{
				TagState[change.Key] = change.Value;
			}
		}

		private void SetTextTagIfChanged(string tagName, string value)
		{
			if (!string.IsNullOrWhiteSpace(value) && value != TagState.GetDisplayValue(tagName))
			{
				Changes[tagName] = value;
			}
		}
	}

	private readonly ResourceManager resourceManager;

	private string selectedFilenamePattern;

	private string filenameRegexPattern;

	private Dictionary<int, int> regexCaptureGroupMap;

	internal bool IsChangeTagsModeSelected { get; private set; }

	private bool usesRegexCaptureGroups;

	private bool renameRelatedFiles;

	private int successCount;

	private int failureCount;

	private int skippedCount;

	private int processedCount;

	private readonly Page batchMessages;

	private TagHistoryRepository historyTransaction;

	private IContainer components;

	private TabControl tabControl;

	private TabPage patternTabPage;

	private FlowLayoutPanel patternTabPanel;

	private GroupBox patternGroupBox;

	private FlowLayoutPanel patternOptionsPanel;

	private RadioButton artistTitlePatternRadioButton;

	private RadioButton titleArtistPatternRadioButton;

	private RadioButton trackTitlePatternRadioButton;

	private RadioButton trackArtistTitlePatternRadioButton;

	private RadioButton discTrackTitlePatternRadioButton;

	private RadioButton discTrackArtistTitlePatternRadioButton;

	private GroupBox operationModeGroupBox;

	private FlowLayoutPanel operationModePanel;

	private RadioButton renameFilesModeRadioButton;

	private CheckBox renameRelatedFilesCheckBox;

	private RadioButton changeTagsModeRadioButton;

	private GroupBox patternDefinitionsGroupBox;

	private FlowLayoutPanel patternDefinitionsPanel;

	private Label titlePatternDefinitionLabel;

	private Label artistPatternDefinitionLabel;

	private Label albumPatternDefinitionLabel;

	private Label discPatternDefinitionLabel;

	private Label trackPatternDefinitionLabel;

	private Label yearPatternDefinitionLabel;

	private Label commentPatternDefinitionLabel;

	private FlowLayoutPanel patternCommandRowPanel;

	private FlowLayoutPanel patternButtonPanel;

	private Button patternOkButton;

	private Button patternCancelButton;

	private TabPage regexTabPage;

	private FlowLayoutPanel regexTabPanel;

	private GroupBox regexPatternGroupBox;

	private GroupBox captureGroupGroupBox;

	private GroupBox regexExampleGroupBox;

	private FlowLayoutPanel regexCommandRowPanel;

	private FlowLayoutPanel regexButtonPanel;

	private Button regexOkButton;

	private Button regexCancelButton;

	private ImageList captureGroupRowHeightImageList;

	private GroupBox regexOperationModeGroupBox;

	private TextBox regexPatternTextBox;

	private RadioButton regexChangeTagsModeRadioButton;

	private TextBox regexExampleTextBox;

	private Label unusedPatternDefinitionLabel;

	private Label albumArtistPatternDefinitionLabel;

	private FlowLayoutPanel captureGroupPanel;

	private MusicTagWinApp.Roles.EditableListView captureGroupListView;

	private ColumnHeader captureGroupColumn;

	private ColumnHeader captureGroupMatchColumn;

	private Button clearCaptureGroupsButton;

	private RadioButton customPatternRadioButton;

	private TextBox customPatternTextBox;

	public FilenameRelatedBatchDialog()
	{
		resourceManager = new ResourceManager("MusicTag.Schemes.EventRulesSchema", typeof(FilenameRelatedBatchDialog).Assembly);
		regexCaptureGroupMap = new Dictionary<int, int>();
		batchMessages = new Page();
		InitializeComponent();
		UpdateResponsiveLayout();
		InitializeCaptureGroupList();
		ApplyLocalizedText();
		customPatternTextBox.Text = Settings.Default.FilenameCustomPattern;
	}

	protected override void OnLoad(EventArgs e)
	{
		base.OnLoad(e);
		try
		{
			if (!string.IsNullOrWhiteSpace(Settings.Default.FilenameRelSelectedTab))
			{
				tabControl.SelectTab(Settings.Default.FilenameRelSelectedTab);
			}
			Dictionary<string, object> dictionary = JsonConvert.DeserializeObject<Dictionary<string, object>>(Settings.Default.FilenameRelRegexCondition);
			if (dictionary == null || !dictionary.Any())
			{
				return;
			}
			regexPatternTextBox.Text = dictionary["regex"] as string;
			foreach (KeyValuePair<int, int> item in JsonConvert.DeserializeObject<Dictionary<int, int>>(dictionary["match_group_map"].ToString()))
			{
				AssociatedValueListViewItem captureGroupItem = captureGroupListView.Items.Cast<AssociatedValueListViewItem>().FirstOrDefault(candidate => candidate.Text == item.Key.ToString());
				if (captureGroupItem?.SubItems["match"] is EmbeddedControlSubItem embeddedControlSubItem && embeddedControlSubItem.EmbeddedControl is ComboBox comboBox)
				{
					comboBox.SelectedIndex = item.Value;
				}
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine("DeserializeObject conditionMap fail:" + ex.Message);
		}
	}

	protected override void OnShown(EventArgs e)
	{
		base.OnShown(e);
		artistTitlePatternRadioButton.Checked = false;
		try
		{
			Dictionary<string, object> conditionMap = JsonConvert.DeserializeObject<Dictionary<string, object>>(Settings.Default.FilenameRelCondition);
			if (conditionMap == null || !conditionMap.Any())
			{
				return;
			}
			string pattern = conditionMap["pattern"] as string;
			if (patternOptionsPanel.Controls.Cast<Control>().FirstOrDefault(control => control.Text == pattern) is RadioButton radioButton)
			{
				radioButton.Checked = true;
			}
			else if (pattern == customPatternTextBox.Text)
			{
				customPatternRadioButton.Checked = true;
			}
			if ((bool)conditionMap["is_change_tags_mode"])
			{
				changeTagsModeRadioButton.Checked = true;
			}
			else
			{
				renameFilesModeRadioButton.Checked = true;
			}
			renameRelatedFilesCheckBox.Checked = (bool)conditionMap["is_rename_with_extra"];
		}
		catch (Exception ex)
		{
			Console.WriteLine("DeserializeObject conditionMap fail:" + ex.Message);
		}
	}

	private void OnTabControlSizeChanged(object sender, EventArgs e)
	{
		UpdateResponsiveLayout();
	}

	private void OnSelectedTabChanged(object sender, EventArgs e)
	{
		UpdateResponsiveLayout();
	}

	private void InitializeCaptureGroupList()
	{
		captureGroupRowHeightImageList.ImageSize = new Size(1, ImageUtilities.ScaleByDpi(23f));
		foreach (ColumnHeader column in captureGroupListView.Columns)
		{
			column.Width = ImageUtilities.ScaleByDpi(column.Width);
		}
		object[] captureGroupOptions = new string[8]
		{
			"",
			Resources.title,
			Resources.artist,
			Resources.album,
			Resources.discstr,
			Resources.trackstr,
			Resources.year,
			Resources.comment
		};
		for (int groupNumber = 1; groupNumber <= 20; groupNumber++)
		{
			EmbeddedControlSubItem matchSubItem = new EmbeddedControlSubItem();
			AssociatedValueListViewItem captureGroupItem = new AssociatedValueListViewItem(groupNumber.ToString());
			captureGroupItem.SubItems.Add(matchSubItem).Name = "match";
			captureGroupListView.Items.Add(captureGroupItem);
			ComboBox comboBox = new ComboBox
			{
				DropDownStyle = ComboBoxStyle.DropDownList
			};
			captureGroupListView.AttachEmbeddedControl(comboBox, matchSubItem);
			comboBox.Items.AddRange(captureGroupOptions);
			comboBox.SelectedIndex = 0;
		}
	}

	private void ApplyLocalizedText()
	{
		ResourceManager resources = resourceManager;
		Text = Resources.FilenameRelByBatch;
		patternOkButton.Text = Resources.OK;
		patternCancelButton.Text = Resources.Cancel;
		regexOkButton.Text = Resources.OK;
		regexCancelButton.Text = Resources.Cancel;
		customPatternRadioButton.Text = resources.GetString("rbPatternCustom");
		patternGroupBox.Text = resources.GetString("gbPattern");
		operationModeGroupBox.Text = resources.GetString("gbOptMode");
		regexOperationModeGroupBox.Text = resources.GetString("gbOptMode");
		renameFilesModeRadioButton.Text = resources.GetString("rbModeRenameFiles");
		renameRelatedFilesCheckBox.Text = resources.GetString("cbModeRenameExtra");
		changeTagsModeRadioButton.Text = resources.GetString("rbModeChangeTags");
		regexChangeTagsModeRadioButton.Text = resources.GetString("rbModeChangeTags");
		patternDefinitionsGroupBox.Text = resources.GetString("gbPatternDefs");
		titlePatternDefinitionLabel.Text = resources.GetString("lblPatternDef1");
		artistPatternDefinitionLabel.Text = resources.GetString("lblPatternDef2");
		albumPatternDefinitionLabel.Text = resources.GetString("lblPatternDef3");
		discPatternDefinitionLabel.Text = resources.GetString("lblPatternDef4");
		trackPatternDefinitionLabel.Text = resources.GetString("lblPatternDef5");
		yearPatternDefinitionLabel.Text = resources.GetString("lblPatternDef6");
		commentPatternDefinitionLabel.Text = resources.GetString("lblPatternDef7");
		albumArtistPatternDefinitionLabel.Text = resources.GetString("lblPatternDef8");
		unusedPatternDefinitionLabel.Text = resources.GetString("lblPatternDef0");
		patternTabPage.Text = resources.GetString("tabPattern");
		regexTabPage.Text = resources.GetString("tabRegex");
		regexPatternGroupBox.Text = resources.GetString("tabRegex");
		captureGroupColumn.Text = resources.GetString("chGroup");
		captureGroupMatchColumn.Text = resources.GetString("chMatch");
		clearCaptureGroupsButton.Text = resources.GetString("btnClearCaptureGroup");
		captureGroupGroupBox.Text = resources.GetString("gbCaptureGroup");
		regexExampleGroupBox.Text = resources.GetString("gbExample");
		regexExampleTextBox.Text = resources.GetString("tbExample");
	}

	private void UpdateResponsiveLayout()
	{
		int patternButtonLeftMargin = (patternCommandRowPanel.Width - patternButtonPanel.Width) / 2;
		patternButtonPanel.Margin = new Padding(patternButtonLeftMargin, patternButtonPanel.Margin.Top, 0, patternButtonPanel.Margin.Bottom);
		customPatternTextBox.Width = patternOptionsPanel.Width - customPatternRadioButton.Width - customPatternRadioButton.Margin.Left - customPatternRadioButton.Margin.Right - customPatternTextBox.Margin.Left - customPatternTextBox.Margin.Right - ImageUtilities.ScaleByDpi(20f);
		int regexButtonLeftMargin = (regexCommandRowPanel.Width - regexButtonPanel.Width) / 2;
		regexButtonPanel.Margin = new Padding(regexButtonLeftMargin, regexButtonPanel.Margin.Top, 0, regexButtonPanel.Margin.Bottom);

		int patternGroupWidth = patternTabPanel.Width - patternGroupBox.Margin.Left * 2;
		patternDefinitionsGroupBox.Width = patternGroupWidth;
		operationModeGroupBox.Width = patternGroupWidth;
		patternGroupBox.Width = patternGroupWidth;

		int regexGroupWidth = regexTabPanel.Width - regexPatternGroupBox.Margin.Left * 2;
		regexExampleGroupBox.Width = regexGroupWidth;
		regexOperationModeGroupBox.Width = regexGroupWidth;
		captureGroupGroupBox.Width = regexGroupWidth;
		regexPatternGroupBox.Width = regexGroupWidth;

		int patternRadioTopMargin = (patternOptionsPanel.Height - artistTitlePatternRadioButton.Height * 4) / 5;
		RadioButton[] patternRadioButtons = new RadioButton[6] { artistTitlePatternRadioButton, titleArtistPatternRadioButton, trackTitlePatternRadioButton, trackArtistTitlePatternRadioButton, discTrackTitlePatternRadioButton, discTrackArtistTitlePatternRadioButton };
		foreach (RadioButton radioButton in patternRadioButtons)
		{
			radioButton.Width = patternOptionsPanel.Width / 2 - artistTitlePatternRadioButton.Margin.Left - 5;
			radioButton.Margin = new Padding(artistTitlePatternRadioButton.Margin.Left, patternRadioTopMargin, 0, 0);
		}
		customPatternRadioButton.Margin = new Padding(artistTitlePatternRadioButton.Margin.Left, patternRadioTopMargin + 2, 0, 0);
		customPatternTextBox.Margin = new Padding(artistTitlePatternRadioButton.Margin.Left, patternRadioTopMargin, 0, 0);

		int patternDefinitionTopMargin = (patternDefinitionsPanel.Height - titlePatternDefinitionLabel.Height * 3) / 4;
		Label[] patternDefinitionLabels = new Label[9] { titlePatternDefinitionLabel, artistPatternDefinitionLabel, albumPatternDefinitionLabel, discPatternDefinitionLabel, trackPatternDefinitionLabel, yearPatternDefinitionLabel, commentPatternDefinitionLabel, albumArtistPatternDefinitionLabel, unusedPatternDefinitionLabel };
		foreach (Label label in patternDefinitionLabels)
		{
			label.Width = patternDefinitionsPanel.Width / 3 - titlePatternDefinitionLabel.Margin.Left - 5;
			label.Margin = new Padding(titlePatternDefinitionLabel.Margin.Left, patternDefinitionTopMargin, 0, 0);
		}
		regexPatternTextBox.Width = regexPatternGroupBox.Width - regexPatternGroupBox.Padding.Left * 2 - regexPatternTextBox.Margin.Left * 2;
		captureGroupListView.Width = captureGroupPanel.Width;
		captureGroupListView.Height = captureGroupPanel.Height - clearCaptureGroupsButton.Height - ImageUtilities.ScaleByDpi(3f);
		clearCaptureGroupsButton.Margin = new Padding(captureGroupPanel.Width - clearCaptureGroupsButton.Width, clearCaptureGroupsButton.Margin.Top, 0, 0);
		regexExampleTextBox.Width = regexExampleGroupBox.Width - regexExampleGroupBox.Padding.Left * 2 - regexExampleTextBox.Margin.Left * 2;
		regexExampleTextBox.Height = regexExampleGroupBox.Height - regexExampleGroupBox.Padding.Top * 2 - regexExampleTextBox.Margin.Top * 2 - ImageUtilities.ScaleByDpi(5f);
	}

	private void ConfirmPatternSettings(object sender, EventArgs e)
	{
		Control selectedPattern = patternOptionsPanel.Controls.Cast<Control>().FirstOrDefault(IsCheckedRadioButton);
		if (selectedPattern == null)
		{
			DialogService.ShowErrorMessage(Resources.Msg_PleaseChoosePattern);
			return;
		}
		if (!operationModePanel.Controls.Cast<Control>().Any(IsCheckedRadioButton))
		{
			DialogService.ShowErrorMessage(Resources.Msg_PleaseChooseOperationMode);
			return;
		}
		if (customPatternRadioButton.Checked)
		{
			if (string.IsNullOrWhiteSpace(customPatternTextBox.Text))
			{
				DialogService.ShowErrorMessage(Resources.Msg_PleaseInputCustomPattern);
				return;
			}
			selectedFilenamePattern = customPatternTextBox.Text;
		}
		else
		{
			selectedFilenamePattern = selectedPattern.Text;
		}
		if (!ValidateFilenamePattern(selectedFilenamePattern, changeTagsModeRadioButton.Checked))
		{
			return;
		}
		IsChangeTagsModeSelected = changeTagsModeRadioButton.Checked;
		renameRelatedFiles = renameRelatedFilesCheckBox.Checked;
		Dictionary<string, object> value = new Dictionary<string, object>
		{
			["pattern"] = selectedFilenamePattern,
			["is_change_tags_mode"] = changeTagsModeRadioButton.Checked,
			["is_rename_with_extra"] = renameRelatedFilesCheckBox.Checked
		};
		Settings.Default.FilenameRelCondition = JsonConvert.SerializeObject(value);
		Settings.Default.FilenameCustomPattern = customPatternTextBox.Text;
		Settings.Default.FilenameRelSelectedTab = tabControl.SelectedTab.Name;
		if (!DialogService.TrySaveApplicationSettings())
		{
			return;
		}
		base.DialogResult = DialogResult.OK;
		Close();
	}

	private void CancelPatternSettings(object sender, EventArgs e)
	{
		base.DialogResult = DialogResult.Cancel;
		Close();
	}

	private void ResetCaptureGroupSelections(object sender, EventArgs e)
	{
		captureGroupListView.Items.Cast<AssociatedValueListViewItem>().Select(GetCaptureGroupComboBox).ForEachItem(ResetComboBoxSelection);
	}

	private void ConfirmRegexSettings(object sender, EventArgs e)
	{
		if (!string.IsNullOrWhiteSpace(regexPatternTextBox.Text))
		{
			try
			{
				_ = new Regex(regexPatternTextBox.Text);
			}
			catch (ArgumentException ex)
			{
				DialogService.ShowErrorMessage(ex.Message);
				return;
			}
			List<(int CaptureGroup, int SelectedIndex)> selectedCaptureGroups = captureGroupListView.Items.Cast<AssociatedValueListViewItem>().Select(CreateCaptureGroupSelection).Where(HasSelectedCaptureGroup)
				.ToList();
			if (!selectedCaptureGroups.Any())
			{
				DialogService.ShowErrorMessage(Resources.Msg_CapturegroupCannotBeEmpty);
				return;
			}
			if (selectedCaptureGroups.GroupBy(group => group.SelectedIndex).Any(group => group.Count() > 1))
			{
				DialogService.ShowErrorMessage(Resources.Msg_CapturegroupCannotBeDuplicate);
				return;
			}
			filenameRegexPattern = regexPatternTextBox.Text;
			regexCaptureGroupMap.Clear();
			foreach (var (captureGroup, selectedIndex) in selectedCaptureGroups)
			{
				regexCaptureGroupMap.Add(captureGroup, selectedIndex);
			}
			Dictionary<string, object> regexCondition = new Dictionary<string, object>
			{
				["regex"] = filenameRegexPattern,
				["match_group_map"] = regexCaptureGroupMap
			};
			usesRegexCaptureGroups = true;
			IsChangeTagsModeSelected = true;
			Settings.Default.FilenameRelRegexCondition = JsonConvert.SerializeObject(regexCondition);
			Settings.Default.FilenameRelSelectedTab = tabControl.SelectedTab.Name;
			if (!DialogService.TrySaveApplicationSettings())
			{
				return;
			}
			base.DialogResult = DialogResult.OK;
			Close();
		}
		else
		{
			DialogService.ShowErrorMessage(Resources.Msg_PleaseInputRegularexpression);
		}
	}

	private void CancelRegexSettings(object sender, EventArgs e)
	{
		base.DialogResult = DialogResult.Cancel;
		Close();
	}

	private void OnRenameFilesModeChanged(object sender, EventArgs e)
	{
		renameRelatedFilesCheckBox.Enabled = renameFilesModeRadioButton.Checked;
		unusedPatternDefinitionLabel.Enabled = false;
	}

	private void OnChangeTagsModeChanged(object sender, EventArgs e)
	{
		unusedPatternDefinitionLabel.Enabled = true;
	}

	// pattern 校验的纯判定结果(无 UI),供 ValidateFilenamePattern 翻译为错误消息 + characterization 锁定。
	internal enum FilenamePatternValidation
	{
		Valid,
		Empty,
		DuplicateParam,
		NoParam,
		Pattern0NotAllowed,
		AdjacentParams
	}

	// 文件名模板校验核心(纯逻辑,无 UI):规整空白/路径分隔为空格后,要求 @1-8 不重复、至少一个参数、
	// 非改标签模式禁用 @0、且 disc/track 占位符不相邻(@5@4 直接拒;否则剥去首段 @4@5/@4/@5 并把
	// @4@5 视作 @4 后,任意 @x@y 相邻即拒)。提取自 ValidateFilenamePattern 的判定逻辑(行为逐字保持)。
	internal static FilenamePatternValidation ValidateFilenamePatternCore(string pattern, bool isChangeTagsMode)
	{
		pattern = Regex.Replace(pattern, "[\\s/\\\\]", " ");
		if (string.IsNullOrWhiteSpace(pattern))
		{
			return FilenamePatternValidation.Empty;
		}
		HashSet<string> parameters = new HashSet<string>();
		foreach (Match item in Regex.Matches(pattern, "@[1-8]"))
		{
			if (!parameters.Add(item.Value))
			{
				return FilenamePatternValidation.DuplicateParam;
			}
		}
		if (!parameters.Any())
		{
			return FilenamePatternValidation.NoParam;
		}
		if (!isChangeTagsMode && Regex.Match(pattern, "@0").Success)
		{
			return FilenamePatternValidation.Pattern0NotAllowed;
		}
		if (pattern.StartsWith("@5@4"))
		{
			return FilenamePatternValidation.AdjacentParams;
		}
		if (pattern.StartsWith("@4@5"))
		{
			pattern = Regex.Replace(pattern, "^@4@5", "");
		}
		else if (pattern.StartsWith("@4"))
		{
			pattern = Regex.Replace(pattern, "^@4", "");
		}
		else if (pattern.StartsWith("@5"))
		{
			pattern = Regex.Replace(pattern, "^@5", "");
		}
		pattern = pattern.Replace("@4@5", "@4");
		if (Regex.Match(pattern, "@[0-8]@[0-8]").Success)
		{
			return FilenamePatternValidation.AdjacentParams;
		}
		return FilenamePatternValidation.Valid;
	}

	private bool ValidateFilenamePattern(string pattern, bool isChangeTagsMode)
	{
		switch (ValidateFilenamePatternCore(pattern, isChangeTagsMode))
		{
			case FilenamePatternValidation.Empty:
				DialogService.ShowErrorMessage(Resources.Msg_PatternCannotBeEmpty);
				return false;
			case FilenamePatternValidation.DuplicateParam:
				DialogService.ShowErrorMessage(Resources.Msg_ParamsInPatternCannotDuplicate);
				return false;
			case FilenamePatternValidation.NoParam:
				DialogService.ShowErrorMessage(Resources.Msg_ParamsInPatternNotFound);
				return false;
			case FilenamePatternValidation.Pattern0NotAllowed:
				DialogService.ShowErrorMessage(Resources.Msg_ParamsInPatternCannotUsePattern0);
				return false;
			case FilenamePatternValidation.AdjacentParams:
				DialogService.ShowErrorMessage(Resources.Msg_ParamsInPatternCannotAdjacent);
				return false;
			default:
				return true;
		}
	}

	internal async void StartRenameFiles((string path, string _, int lvIndex)[] paths, ProgressDialog progressDialog, ListViewFileSetting listViewFileSetting, Action<(string msg, bool isErr)> finallyCallback)
	{
		RenameFilesBatchWorker worker = new RenameFilesBatchWorker
		{
			ProgressDialog = progressDialog,
			Owner = this,
			RenameItems = paths,
			FileSettings = listViewFileSetting,
			CancellationTokenSource = new CancellationTokenSource(),
			CurrentFileName = null
		};
		progressDialog.CancelRequested += worker.Cancel;
		progressDialog.ProgressUpdate += worker.UpdateProgress;
		worker.ReportFailure = worker.ReportRenameFailure;
		(string msg, bool isErr) result = default((string, bool));
		try
		{
			await Task.Run((Action)worker.RenameFiles, worker.CancellationTokenSource.Token);
			result = BuildBatchCompletionResult(paths.Length);
		}
		catch (Exception ex)
		{
			batchMessages.AddLine(ex.Message);
			result = (batchMessages.ToString(), true);
		}
		finally
		{
			progressDialog.CloseAfterCompletion();
			finallyCallback(result);
		}
	}

	internal async void StartChangeTags(string[] paths, ProgressDialog progressDialog, bool canCancelFileReadonly, Action<(string msg, bool isErr)> finallyCallback)
	{
		ChangeTagsBatchWorker worker = new ChangeTagsBatchWorker
		{
			ProgressDialog = progressDialog,
			Owner = this,
			FilePaths = paths,
			CanCancelReadOnly = canCancelFileReadonly,
			CancellationTokenSource = new CancellationTokenSource(),
			CurrentFileName = null
		};
		progressDialog.CancelRequested += worker.Cancel;
		progressDialog.ProgressUpdate += worker.UpdateProgress;
		worker.ReportFailure = worker.ReportTagSaveFailure;
		(string msg, bool isErr) result = default((string, bool));
		try
		{
			await Task.Run((Action)worker.ChangeTags, worker.CancellationTokenSource.Token);
			result = BuildBatchCompletionResult(paths.Length);
		}
		catch (Exception ex)
		{
			batchMessages.AddLine(ex.Message);
			result = (batchMessages.ToString(), true);
		}
		finally
		{
			progressDialog.CloseAfterCompletion();
			finallyCallback(result);
		}
	}

	private (string msg, bool isErr) BuildBatchCompletionResult(int totalCount)
	{
		if (totalCount <= 1)
		{
			if (successCount > 0)
			{
				return (Resources.Msg_SaveCompleted + "\n" + batchMessages.ToString(), false);
			}
			if (skippedCount > 0)
			{
				return (Resources.Msg_Skipped + "\n" + batchMessages.ToString(), false);
			}
			return (batchMessages.ToString(), true);
		}
		return (string.Format(Resources.Msg_SaveCompleted + "\n" + Resources.Msg_OK_Fail_Skip_Count, successCount, failureCount, skippedCount, processedCount) + "\n" + batchMessages.ToString(), false);
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
		tabControl = new TabControl();
		patternTabPage = new TabPage();
		patternTabPanel = new FlowLayoutPanel();
		patternGroupBox = new GroupBox();
		patternOptionsPanel = new FlowLayoutPanel();
		artistTitlePatternRadioButton = new RadioButton();
		titleArtistPatternRadioButton = new RadioButton();
		trackTitlePatternRadioButton = new RadioButton();
		trackArtistTitlePatternRadioButton = new RadioButton();
		discTrackTitlePatternRadioButton = new RadioButton();
		discTrackArtistTitlePatternRadioButton = new RadioButton();
		customPatternRadioButton = new RadioButton();
		customPatternTextBox = new TextBox();
		operationModeGroupBox = new GroupBox();
		operationModePanel = new FlowLayoutPanel();
		renameFilesModeRadioButton = new RadioButton();
		renameRelatedFilesCheckBox = new CheckBox();
		changeTagsModeRadioButton = new RadioButton();
		patternDefinitionsGroupBox = new GroupBox();
		patternDefinitionsPanel = new FlowLayoutPanel();
		titlePatternDefinitionLabel = new Label();
		artistPatternDefinitionLabel = new Label();
		albumPatternDefinitionLabel = new Label();
		discPatternDefinitionLabel = new Label();
		trackPatternDefinitionLabel = new Label();
		yearPatternDefinitionLabel = new Label();
		commentPatternDefinitionLabel = new Label();
		albumArtistPatternDefinitionLabel = new Label();
		unusedPatternDefinitionLabel = new Label();
		patternCommandRowPanel = new FlowLayoutPanel();
		patternButtonPanel = new FlowLayoutPanel();
		patternOkButton = new Button();
		patternCancelButton = new Button();
		regexTabPage = new TabPage();
		regexTabPanel = new FlowLayoutPanel();
		regexPatternGroupBox = new GroupBox();
		regexPatternTextBox = new TextBox();
		captureGroupGroupBox = new GroupBox();
		captureGroupPanel = new FlowLayoutPanel();
		captureGroupListView = new MusicTagWinApp.Roles.EditableListView();
		captureGroupColumn = new ColumnHeader();
		captureGroupMatchColumn = new ColumnHeader();
		captureGroupRowHeightImageList = new ImageList(components);
		clearCaptureGroupsButton = new Button();
		regexOperationModeGroupBox = new GroupBox();
		regexChangeTagsModeRadioButton = new RadioButton();
		regexExampleGroupBox = new GroupBox();
		regexExampleTextBox = new TextBox();
		regexCommandRowPanel = new FlowLayoutPanel();
		regexButtonPanel = new FlowLayoutPanel();
		regexOkButton = new Button();
		regexCancelButton = new Button();
		tabControl.SuspendLayout();
		patternTabPage.SuspendLayout();
		patternTabPanel.SuspendLayout();
		patternGroupBox.SuspendLayout();
		patternOptionsPanel.SuspendLayout();
		operationModeGroupBox.SuspendLayout();
		operationModePanel.SuspendLayout();
		patternDefinitionsGroupBox.SuspendLayout();
		patternDefinitionsPanel.SuspendLayout();
		patternCommandRowPanel.SuspendLayout();
		patternButtonPanel.SuspendLayout();
		regexTabPage.SuspendLayout();
		regexTabPanel.SuspendLayout();
		regexPatternGroupBox.SuspendLayout();
		captureGroupGroupBox.SuspendLayout();
		captureGroupPanel.SuspendLayout();
		regexOperationModeGroupBox.SuspendLayout();
		regexExampleGroupBox.SuspendLayout();
		regexCommandRowPanel.SuspendLayout();
		regexButtonPanel.SuspendLayout();
		SuspendLayout();
		tabControl.Controls.Add(patternTabPage);
		tabControl.Controls.Add(regexTabPage);
		tabControl.Dock = DockStyle.Fill;
		tabControl.Location = new Point(0, 0);
		tabControl.Margin = new Padding(0);
		tabControl.Name = "tabControl1";
		tabControl.Padding = new Point(0, 0);
		tabControl.SelectedIndex = 0;
		tabControl.Size = new Size(504, 521);
		tabControl.TabIndex = 0;
		tabControl.SelectedIndexChanged += OnSelectedTabChanged;
		tabControl.SizeChanged += OnTabControlSizeChanged;
		patternTabPage.Controls.Add(patternTabPanel);
		patternTabPage.Location = new Point(4, 23);
		patternTabPage.Margin = new Padding(0);
		patternTabPage.Name = "tabPattern";
		patternTabPage.Size = new Size(496, 494);
		patternTabPage.TabIndex = 0;
		patternTabPage.Text = "Pattern";
		patternTabPage.UseVisualStyleBackColor = true;
		patternTabPanel.Controls.Add(patternGroupBox);
		patternTabPanel.Controls.Add(operationModeGroupBox);
		patternTabPanel.Controls.Add(patternDefinitionsGroupBox);
		patternTabPanel.Controls.Add(patternCommandRowPanel);
		patternTabPanel.Dock = DockStyle.Fill;
		patternTabPanel.FlowDirection = FlowDirection.TopDown;
		patternTabPanel.Location = new Point(0, 0);
		patternTabPanel.Margin = new Padding(0);
		patternTabPanel.Name = "flowLayoutPanel1";
		patternTabPanel.Size = new Size(496, 494);
		patternTabPanel.TabIndex = 16;
		patternGroupBox.Controls.Add(patternOptionsPanel);
		patternGroupBox.Location = new Point(9, 9);
		patternGroupBox.Margin = new Padding(9, 9, 9, 0);
		patternGroupBox.Name = "gbPattern";
		patternGroupBox.Padding = new Padding(5);
		patternGroupBox.Size = new Size(479, 155);
		patternGroupBox.TabIndex = 0;
		patternGroupBox.TabStop = false;
		patternGroupBox.Text = "Pattern";
		patternOptionsPanel.Controls.Add(artistTitlePatternRadioButton);
		patternOptionsPanel.Controls.Add(titleArtistPatternRadioButton);
		patternOptionsPanel.Controls.Add(trackTitlePatternRadioButton);
		patternOptionsPanel.Controls.Add(trackArtistTitlePatternRadioButton);
		patternOptionsPanel.Controls.Add(discTrackTitlePatternRadioButton);
		patternOptionsPanel.Controls.Add(discTrackArtistTitlePatternRadioButton);
		patternOptionsPanel.Controls.Add(customPatternRadioButton);
		patternOptionsPanel.Controls.Add(customPatternTextBox);
		patternOptionsPanel.Dock = DockStyle.Fill;
		patternOptionsPanel.Location = new Point(5, 20);
		patternOptionsPanel.Margin = new Padding(0);
		patternOptionsPanel.Name = "panelPattern";
		patternOptionsPanel.Size = new Size(469, 130);
		patternOptionsPanel.TabIndex = 0;
		artistTitlePatternRadioButton.Location = new Point(9, 9);
		artistTitlePatternRadioButton.Margin = new Padding(9, 9, 0, 0);
		artistTitlePatternRadioButton.Name = "rbPattern1";
		artistTitlePatternRadioButton.Size = new Size(200, 17);
		artistTitlePatternRadioButton.TabIndex = 1;
		artistTitlePatternRadioButton.TabStop = true;
		artistTitlePatternRadioButton.Text = "@2 - @1";
		artistTitlePatternRadioButton.UseVisualStyleBackColor = true;
		titleArtistPatternRadioButton.Location = new Point(218, 9);
		titleArtistPatternRadioButton.Margin = new Padding(9, 9, 0, 0);
		titleArtistPatternRadioButton.Name = "rbPattern2";
		titleArtistPatternRadioButton.Size = new Size(200, 17);
		titleArtistPatternRadioButton.TabIndex = 1;
		titleArtistPatternRadioButton.TabStop = true;
		titleArtistPatternRadioButton.Text = "@1 - @2";
		titleArtistPatternRadioButton.UseVisualStyleBackColor = true;
		trackTitlePatternRadioButton.Location = new Point(9, 35);
		trackTitlePatternRadioButton.Margin = new Padding(9, 9, 0, 0);
		trackTitlePatternRadioButton.Name = "rbPattern3";
		trackTitlePatternRadioButton.Size = new Size(200, 17);
		trackTitlePatternRadioButton.TabIndex = 2;
		trackTitlePatternRadioButton.TabStop = true;
		trackTitlePatternRadioButton.Text = "@5. @1";
		trackTitlePatternRadioButton.UseVisualStyleBackColor = true;
		trackArtistTitlePatternRadioButton.Location = new Point(218, 35);
		trackArtistTitlePatternRadioButton.Margin = new Padding(9, 9, 0, 0);
		trackArtistTitlePatternRadioButton.Name = "rbPattern4";
		trackArtistTitlePatternRadioButton.Size = new Size(200, 17);
		trackArtistTitlePatternRadioButton.TabIndex = 3;
		trackArtistTitlePatternRadioButton.TabStop = true;
		trackArtistTitlePatternRadioButton.Text = "@5. @2 - @1";
		trackArtistTitlePatternRadioButton.UseVisualStyleBackColor = true;
		discTrackTitlePatternRadioButton.Location = new Point(9, 61);
		discTrackTitlePatternRadioButton.Margin = new Padding(9, 9, 0, 0);
		discTrackTitlePatternRadioButton.Name = "rbPattern5";
		discTrackTitlePatternRadioButton.Size = new Size(200, 17);
		discTrackTitlePatternRadioButton.TabIndex = 4;
		discTrackTitlePatternRadioButton.TabStop = true;
		discTrackTitlePatternRadioButton.Text = "@4@5. @1";
		discTrackTitlePatternRadioButton.UseVisualStyleBackColor = true;
		discTrackArtistTitlePatternRadioButton.Location = new Point(218, 61);
		discTrackArtistTitlePatternRadioButton.Margin = new Padding(9, 9, 0, 0);
		discTrackArtistTitlePatternRadioButton.Name = "rbPattern6";
		discTrackArtistTitlePatternRadioButton.Size = new Size(200, 17);
		discTrackArtistTitlePatternRadioButton.TabIndex = 5;
		discTrackArtistTitlePatternRadioButton.TabStop = true;
		discTrackArtistTitlePatternRadioButton.Text = "@4@5. @2 - @1";
		discTrackArtistTitlePatternRadioButton.UseVisualStyleBackColor = true;
		customPatternRadioButton.AutoSize = true;
		customPatternRadioButton.Location = new Point(9, 89);
		customPatternRadioButton.Margin = new Padding(9, 11, 0, 0);
		customPatternRadioButton.Name = "rbPatternCustom";
		customPatternRadioButton.Size = new Size(74, 18);
		customPatternRadioButton.TabIndex = 10;
		customPatternRadioButton.TabStop = true;
		customPatternRadioButton.Text = "Custom: ";
		customPatternRadioButton.UseVisualStyleBackColor = true;
		customPatternTextBox.Location = new Point(83, 87);
		customPatternTextBox.Margin = new Padding(0, 9, 0, 0);
		customPatternTextBox.Name = "tbPatternCustom";
		customPatternTextBox.Size = new Size(368, 22);
		customPatternTextBox.TabIndex = 11;
		operationModeGroupBox.Controls.Add(operationModePanel);
		operationModeGroupBox.Location = new Point(9, 173);
		operationModeGroupBox.Margin = new Padding(9, 9, 9, 0);
		operationModeGroupBox.Name = "gbOptMode";
		operationModeGroupBox.Padding = new Padding(5);
		operationModeGroupBox.Size = new Size(479, 125);
		operationModeGroupBox.TabIndex = 16;
		operationModeGroupBox.TabStop = false;
		operationModeGroupBox.Text = "Operation mode";
		operationModePanel.Controls.Add(renameFilesModeRadioButton);
		operationModePanel.Controls.Add(renameRelatedFilesCheckBox);
		operationModePanel.Controls.Add(changeTagsModeRadioButton);
		operationModePanel.Dock = DockStyle.Fill;
		operationModePanel.FlowDirection = FlowDirection.TopDown;
		operationModePanel.Location = new Point(5, 20);
		operationModePanel.Margin = new Padding(0);
		operationModePanel.Name = "panelOptMode";
		operationModePanel.Size = new Size(469, 100);
		operationModePanel.TabIndex = 0;
		renameFilesModeRadioButton.AutoSize = true;
		renameFilesModeRadioButton.Location = new Point(9, 9);
		renameFilesModeRadioButton.Margin = new Padding(9, 9, 0, 0);
		renameFilesModeRadioButton.Name = "rbModeRenameFiles";
		renameFilesModeRadioButton.Size = new Size(93, 18);
		renameFilesModeRadioButton.TabIndex = 0;
		renameFilesModeRadioButton.TabStop = true;
		renameFilesModeRadioButton.Text = "Rename files";
		renameFilesModeRadioButton.UseVisualStyleBackColor = true;
		renameFilesModeRadioButton.CheckedChanged += OnRenameFilesModeChanged;
		renameRelatedFilesCheckBox.AutoSize = true;
		renameRelatedFilesCheckBox.Checked = true;
		renameRelatedFilesCheckBox.CheckState = CheckState.Checked;
		renameRelatedFilesCheckBox.Enabled = false;
		renameRelatedFilesCheckBox.Location = new Point(25, 36);
		renameRelatedFilesCheckBox.Margin = new Padding(25, 9, 0, 0);
		renameRelatedFilesCheckBox.Name = "cbModeRenameExtra";
		renameRelatedFilesCheckBox.Size = new Size(236, 18);
		renameRelatedFilesCheckBox.TabIndex = 1;
		renameRelatedFilesCheckBox.Text = "Rename related Lrc file and picture file";
		renameRelatedFilesCheckBox.UseVisualStyleBackColor = true;
		changeTagsModeRadioButton.AutoSize = true;
		changeTagsModeRadioButton.Location = new Point(9, 63);
		changeTagsModeRadioButton.Margin = new Padding(9, 9, 0, 0);
		changeTagsModeRadioButton.Name = "rbModeChangeTags";
		changeTagsModeRadioButton.Size = new Size(294, 18);
		changeTagsModeRadioButton.TabIndex = 2;
		changeTagsModeRadioButton.TabStop = true;
		changeTagsModeRadioButton.Text = "Change tags from matching info in the file names";
		changeTagsModeRadioButton.UseVisualStyleBackColor = true;
		changeTagsModeRadioButton.CheckedChanged += OnChangeTagsModeChanged;
		patternDefinitionsGroupBox.Controls.Add(patternDefinitionsPanel);
		patternDefinitionsGroupBox.Location = new Point(9, 307);
		patternDefinitionsGroupBox.Margin = new Padding(9, 9, 9, 0);
		patternDefinitionsGroupBox.Name = "gbPatternDefs";
		patternDefinitionsGroupBox.Padding = new Padding(5);
		patternDefinitionsGroupBox.Size = new Size(479, 108);
		patternDefinitionsGroupBox.TabIndex = 17;
		patternDefinitionsGroupBox.TabStop = false;
		patternDefinitionsGroupBox.Text = "Pattern definitions";
		patternDefinitionsPanel.Controls.Add(titlePatternDefinitionLabel);
		patternDefinitionsPanel.Controls.Add(artistPatternDefinitionLabel);
		patternDefinitionsPanel.Controls.Add(albumPatternDefinitionLabel);
		patternDefinitionsPanel.Controls.Add(discPatternDefinitionLabel);
		patternDefinitionsPanel.Controls.Add(trackPatternDefinitionLabel);
		patternDefinitionsPanel.Controls.Add(yearPatternDefinitionLabel);
		patternDefinitionsPanel.Controls.Add(commentPatternDefinitionLabel);
		patternDefinitionsPanel.Controls.Add(albumArtistPatternDefinitionLabel);
		patternDefinitionsPanel.Controls.Add(unusedPatternDefinitionLabel);
		patternDefinitionsPanel.Dock = DockStyle.Fill;
		patternDefinitionsPanel.Location = new Point(5, 20);
		patternDefinitionsPanel.Margin = new Padding(0);
		patternDefinitionsPanel.Name = "flowLayoutPanel4";
		patternDefinitionsPanel.Size = new Size(469, 83);
		patternDefinitionsPanel.TabIndex = 0;
		titlePatternDefinitionLabel.Location = new Point(9, 9);
		titlePatternDefinitionLabel.Margin = new Padding(9, 9, 0, 0);
		titlePatternDefinitionLabel.Name = "lblPatternDef1";
		titlePatternDefinitionLabel.Size = new Size(140, 15);
		titlePatternDefinitionLabel.TabIndex = 0;
		titlePatternDefinitionLabel.Text = "@1 - Title";
		artistPatternDefinitionLabel.Location = new Point(158, 9);
		artistPatternDefinitionLabel.Margin = new Padding(9, 9, 0, 0);
		artistPatternDefinitionLabel.Name = "lblPatternDef2";
		artistPatternDefinitionLabel.Size = new Size(140, 15);
		artistPatternDefinitionLabel.TabIndex = 1;
		artistPatternDefinitionLabel.Text = "@2 - Artist";
		albumPatternDefinitionLabel.Location = new Point(307, 9);
		albumPatternDefinitionLabel.Margin = new Padding(9, 9, 0, 0);
		albumPatternDefinitionLabel.Name = "lblPatternDef3";
		albumPatternDefinitionLabel.Size = new Size(140, 15);
		albumPatternDefinitionLabel.TabIndex = 2;
		albumPatternDefinitionLabel.Text = "@3 - Album";
		discPatternDefinitionLabel.Location = new Point(9, 33);
		discPatternDefinitionLabel.Margin = new Padding(9, 9, 0, 0);
		discPatternDefinitionLabel.Name = "lblPatternDef4";
		discPatternDefinitionLabel.Size = new Size(140, 15);
		discPatternDefinitionLabel.TabIndex = 3;
		discPatternDefinitionLabel.Text = "@4 - Disc";
		trackPatternDefinitionLabel.Location = new Point(158, 33);
		trackPatternDefinitionLabel.Margin = new Padding(9, 9, 0, 0);
		trackPatternDefinitionLabel.Name = "lblPatternDef5";
		trackPatternDefinitionLabel.Size = new Size(140, 15);
		trackPatternDefinitionLabel.TabIndex = 4;
		trackPatternDefinitionLabel.Text = "@5 - Track";
		yearPatternDefinitionLabel.Location = new Point(307, 33);
		yearPatternDefinitionLabel.Margin = new Padding(9, 9, 0, 0);
		yearPatternDefinitionLabel.Name = "lblPatternDef6";
		yearPatternDefinitionLabel.Size = new Size(140, 15);
		yearPatternDefinitionLabel.TabIndex = 5;
		yearPatternDefinitionLabel.Text = "@6 - Year";
		commentPatternDefinitionLabel.Location = new Point(9, 57);
		commentPatternDefinitionLabel.Margin = new Padding(9, 9, 0, 0);
		commentPatternDefinitionLabel.Name = "lblPatternDef7";
		commentPatternDefinitionLabel.Size = new Size(140, 15);
		commentPatternDefinitionLabel.TabIndex = 7;
		commentPatternDefinitionLabel.Text = "@7 - Comment";
		albumArtistPatternDefinitionLabel.Location = new Point(158, 57);
		albumArtistPatternDefinitionLabel.Margin = new Padding(9, 9, 0, 0);
		albumArtistPatternDefinitionLabel.Name = "lblPatternDef8";
		albumArtistPatternDefinitionLabel.Size = new Size(140, 15);
		albumArtistPatternDefinitionLabel.TabIndex = 9;
		albumArtistPatternDefinitionLabel.Text = "@8 - Albumartist";
		unusedPatternDefinitionLabel.Location = new Point(307, 57);
		unusedPatternDefinitionLabel.Margin = new Padding(9, 9, 0, 0);
		unusedPatternDefinitionLabel.Name = "lblPatternDef0";
		unusedPatternDefinitionLabel.Size = new Size(140, 15);
		unusedPatternDefinitionLabel.TabIndex = 6;
		unusedPatternDefinitionLabel.Text = "@0 - No use";
		patternCommandRowPanel.Controls.Add(patternButtonPanel);
		patternCommandRowPanel.Dock = DockStyle.Fill;
		patternCommandRowPanel.Location = new Point(9, 421);
		patternCommandRowPanel.Margin = new Padding(9, 6, 9, 0);
		patternCommandRowPanel.Name = "flowLayoutPanel5";
		patternCommandRowPanel.Size = new Size(479, 60);
		patternCommandRowPanel.TabIndex = 18;
		patternCommandRowPanel.WrapContents = false;
		patternButtonPanel.Controls.Add(patternOkButton);
		patternButtonPanel.Controls.Add(patternCancelButton);
		patternButtonPanel.Location = new Point(0, 12);
		patternButtonPanel.Margin = new Padding(0, 12, 0, 0);
		patternButtonPanel.Name = "flowLayoutPanel6";
		patternButtonPanel.Size = new Size(220, 35);
		patternButtonPanel.TabIndex = 6;
		patternOkButton.Location = new Point(0, 0);
		patternOkButton.Margin = new Padding(0);
		patternOkButton.Name = "btnPatternOK";
		patternOkButton.Size = new Size(100, 35);
		patternOkButton.TabIndex = 1;
		patternOkButton.Text = "OK";
		patternOkButton.UseVisualStyleBackColor = true;
		patternOkButton.Click += ConfirmPatternSettings;
		patternCancelButton.Location = new Point(120, 0);
		patternCancelButton.Margin = new Padding(20, 0, 0, 0);
		patternCancelButton.Name = "btnPatternCancel";
		patternCancelButton.Size = new Size(100, 35);
		patternCancelButton.TabIndex = 2;
		patternCancelButton.Text = "Cancel";
		patternCancelButton.UseVisualStyleBackColor = true;
		patternCancelButton.Click += CancelPatternSettings;
		regexTabPage.Controls.Add(regexTabPanel);
		regexTabPage.Location = new Point(4, 23);
		regexTabPage.Margin = new Padding(0);
		regexTabPage.Name = "tabRegex";
		regexTabPage.Size = new Size(496, 494);
		regexTabPage.TabIndex = 1;
		regexTabPage.Text = "Regular Expression";
		regexTabPage.UseVisualStyleBackColor = true;
		regexTabPanel.Controls.Add(regexPatternGroupBox);
		regexTabPanel.Controls.Add(captureGroupGroupBox);
		regexTabPanel.Controls.Add(regexOperationModeGroupBox);
		regexTabPanel.Controls.Add(regexExampleGroupBox);
		regexTabPanel.Controls.Add(regexCommandRowPanel);
		regexTabPanel.Dock = DockStyle.Fill;
		regexTabPanel.FlowDirection = FlowDirection.TopDown;
		regexTabPanel.Location = new Point(0, 0);
		regexTabPanel.Margin = new Padding(0);
		regexTabPanel.Name = "flowLayoutPanel2";
		regexTabPanel.Size = new Size(496, 494);
		regexTabPanel.TabIndex = 1;
		regexPatternGroupBox.Controls.Add(regexPatternTextBox);
		regexPatternGroupBox.Location = new Point(9, 9);
		regexPatternGroupBox.Margin = new Padding(9, 9, 9, 0);
		regexPatternGroupBox.Name = "gbRegex";
		regexPatternGroupBox.Padding = new Padding(5);
		regexPatternGroupBox.Size = new Size(477, 57);
		regexPatternGroupBox.TabIndex = 2;
		regexPatternGroupBox.TabStop = false;
		regexPatternGroupBox.Text = "Regular Expression";
		regexPatternTextBox.Location = new Point(8, 23);
		regexPatternTextBox.Margin = new Padding(3, 9, 3, 9);
		regexPatternTextBox.Name = "tbInputRegex";
		regexPatternTextBox.Size = new Size(461, 22);
		regexPatternTextBox.TabIndex = 8;
		captureGroupGroupBox.Controls.Add(captureGroupPanel);
		captureGroupGroupBox.Location = new Point(9, 74);
		captureGroupGroupBox.Margin = new Padding(9, 8, 9, 0);
		captureGroupGroupBox.Name = "gbCaptureGroup";
		captureGroupGroupBox.Padding = new Padding(8, 8, 8, 5);
		captureGroupGroupBox.Size = new Size(477, 201);
		captureGroupGroupBox.TabIndex = 3;
		captureGroupGroupBox.TabStop = false;
		captureGroupGroupBox.Text = "Capture group";
		captureGroupPanel.Controls.Add(captureGroupListView);
		captureGroupPanel.Controls.Add(clearCaptureGroupsButton);
		captureGroupPanel.Dock = DockStyle.Fill;
		captureGroupPanel.Location = new Point(8, 23);
		captureGroupPanel.Margin = new Padding(0);
		captureGroupPanel.Name = "flowLayoutPanel3";
		captureGroupPanel.Size = new Size(461, 173);
		captureGroupPanel.TabIndex = 0;
		captureGroupListView.Columns.AddRange(new ColumnHeader[2] { captureGroupColumn, captureGroupMatchColumn });
		captureGroupListView.EmbeddedControlInset = 4;
		captureGroupListView.GridLines = true;
		captureGroupListView.HideSelection = false;
		captureGroupListView.Location = new Point(0, 0);
		captureGroupListView.Margin = new Padding(0);
		captureGroupListView.MultiSelect = false;
		captureGroupListView.Name = "lvCaptureGroup";
		captureGroupListView.OwnerDraw = true;
		captureGroupListView.Size = new Size(461, 136);
		captureGroupListView.SmallImageList = captureGroupRowHeightImageList;
		captureGroupListView.TabIndex = 4;
		captureGroupListView.UseCompatibleStateImageBehavior = false;
		captureGroupListView.View = View.Details;
		captureGroupColumn.Text = "Group #";
		captureGroupMatchColumn.Text = "Match";
		captureGroupMatchColumn.Width = 100;
		captureGroupRowHeightImageList.ColorDepth = ColorDepth.Depth8Bit;
		captureGroupRowHeightImageList.ImageSize = new Size(1, 23);
		captureGroupRowHeightImageList.TransparentColor = Color.Transparent;
		clearCaptureGroupsButton.Location = new Point(385, 139);
		clearCaptureGroupsButton.Margin = new Padding(385, 3, 3, 0);
		clearCaptureGroupsButton.Name = "btnClearCaptureGroup";
		clearCaptureGroupsButton.Size = new Size(75, 23);
		clearCaptureGroupsButton.TabIndex = 5;
		clearCaptureGroupsButton.Text = "Clear";
		clearCaptureGroupsButton.UseVisualStyleBackColor = true;
		clearCaptureGroupsButton.Click += ResetCaptureGroupSelections;
		regexOperationModeGroupBox.Controls.Add(regexChangeTagsModeRadioButton);
		regexOperationModeGroupBox.Location = new Point(9, 283);
		regexOperationModeGroupBox.Margin = new Padding(9, 8, 9, 0);
		regexOperationModeGroupBox.Name = "gbRegexOptMode";
		regexOperationModeGroupBox.Padding = new Padding(5);
		regexOperationModeGroupBox.Size = new Size(477, 45);
		regexOperationModeGroupBox.TabIndex = 20;
		regexOperationModeGroupBox.TabStop = false;
		regexOperationModeGroupBox.Text = "Operation mode";
		regexChangeTagsModeRadioButton.AutoSize = true;
		regexChangeTagsModeRadioButton.Checked = true;
		regexChangeTagsModeRadioButton.Location = new Point(8, 20);
		regexChangeTagsModeRadioButton.Margin = new Padding(3, 0, 0, 0);
		regexChangeTagsModeRadioButton.Name = "rbRegexModeChangeTags";
		regexChangeTagsModeRadioButton.Size = new Size(294, 18);
		regexChangeTagsModeRadioButton.TabIndex = 3;
		regexChangeTagsModeRadioButton.TabStop = true;
		regexChangeTagsModeRadioButton.Text = "Change tags from matching info in the file names";
		regexChangeTagsModeRadioButton.UseVisualStyleBackColor = true;
		regexExampleGroupBox.Controls.Add(regexExampleTextBox);
		regexExampleGroupBox.Location = new Point(9, 336);
		regexExampleGroupBox.Margin = new Padding(9, 8, 9, 0);
		regexExampleGroupBox.Name = "gbExample";
		regexExampleGroupBox.Padding = new Padding(5);
		regexExampleGroupBox.Size = new Size(477, 89);
		regexExampleGroupBox.TabIndex = 4;
		regexExampleGroupBox.TabStop = false;
		regexExampleGroupBox.Text = "Example";
		regexExampleTextBox.Location = new Point(8, 16);
		regexExampleTextBox.Multiline = true;
		regexExampleTextBox.Name = "tbExample";
		regexExampleTextBox.ReadOnly = true;
		regexExampleTextBox.ScrollBars = ScrollBars.Vertical;
		regexExampleTextBox.Size = new Size(462, 67);
		regexExampleTextBox.TabIndex = 1;
		regexExampleTextBox.Text = "File name: 03. xxartist - xxtitle\r\nRegular expression: ^(\\d+)\\. (.+) - (.+)$ \r\nCapture group: 1 - Track,  2 - Artist,  3 - Title\r\nResult: Title - xxtitle, Artist - xxartist, Track - 3";
		regexCommandRowPanel.Controls.Add(regexButtonPanel);
		regexCommandRowPanel.Dock = DockStyle.Fill;
		regexCommandRowPanel.Location = new Point(9, 430);
		regexCommandRowPanel.Margin = new Padding(9, 5, 9, 0);
		regexCommandRowPanel.Name = "flowLayoutPanel8";
		regexCommandRowPanel.Size = new Size(477, 51);
		regexCommandRowPanel.TabIndex = 19;
		regexCommandRowPanel.WrapContents = false;
		regexButtonPanel.Controls.Add(regexOkButton);
		regexButtonPanel.Controls.Add(regexCancelButton);
		regexButtonPanel.Location = new Point(0, 3);
		regexButtonPanel.Margin = new Padding(0, 3, 0, 0);
		regexButtonPanel.Name = "flowLayoutPanel9";
		regexButtonPanel.Size = new Size(220, 35);
		regexButtonPanel.TabIndex = 6;
		regexOkButton.Location = new Point(0, 0);
		regexOkButton.Margin = new Padding(0);
		regexOkButton.Name = "btnRegexOK";
		regexOkButton.Size = new Size(100, 35);
		regexOkButton.TabIndex = 1;
		regexOkButton.Text = "OK";
		regexOkButton.UseVisualStyleBackColor = true;
		regexOkButton.Click += ConfirmRegexSettings;
		regexCancelButton.Location = new Point(120, 0);
		regexCancelButton.Margin = new Padding(20, 0, 0, 0);
		regexCancelButton.Name = "btnRegexCancel";
		regexCancelButton.Size = new Size(100, 35);
		regexCancelButton.TabIndex = 2;
		regexCancelButton.Text = "Cancel";
		regexCancelButton.UseVisualStyleBackColor = true;
		regexCancelButton.Click += CancelRegexSettings;
		AutoScaleDimensions = new SizeF(96f, 96f);
		base.AutoScaleMode = AutoScaleMode.Dpi;
		base.ClientSize = new Size(504, 521);
		base.Controls.Add(tabControl);
		Font = new Font("Tahoma", 9f, FontStyle.Regular, GraphicsUnit.Point, 0);
		base.MaximizeBox = false;
		base.MinimizeBox = false;
		MinimumSize = new Size(299, 296);
		base.Name = "FormFilenameRel";
		base.ShowIcon = false;
		base.StartPosition = FormStartPosition.CenterParent;
		Text = "File name-related";
		tabControl.ResumeLayout(performLayout: false);
		patternTabPage.ResumeLayout(performLayout: false);
		patternTabPanel.ResumeLayout(performLayout: false);
		patternGroupBox.ResumeLayout(performLayout: false);
		patternOptionsPanel.ResumeLayout(performLayout: false);
		patternOptionsPanel.PerformLayout();
		operationModeGroupBox.ResumeLayout(performLayout: false);
		operationModePanel.ResumeLayout(performLayout: false);
		operationModePanel.PerformLayout();
		patternDefinitionsGroupBox.ResumeLayout(performLayout: false);
		patternDefinitionsPanel.ResumeLayout(performLayout: false);
		patternCommandRowPanel.ResumeLayout(performLayout: false);
		patternButtonPanel.ResumeLayout(performLayout: false);
		regexTabPage.ResumeLayout(performLayout: false);
		regexTabPanel.ResumeLayout(performLayout: false);
		regexPatternGroupBox.ResumeLayout(performLayout: false);
		regexPatternGroupBox.PerformLayout();
		captureGroupGroupBox.ResumeLayout(performLayout: false);
		captureGroupPanel.ResumeLayout(performLayout: false);
		regexOperationModeGroupBox.ResumeLayout(performLayout: false);
		regexOperationModeGroupBox.PerformLayout();
		regexExampleGroupBox.ResumeLayout(performLayout: false);
		regexExampleGroupBox.PerformLayout();
		regexCommandRowPanel.ResumeLayout(performLayout: false);
		regexButtonPanel.ResumeLayout(performLayout: false);
		ResumeLayout(performLayout: false);
	}

}
