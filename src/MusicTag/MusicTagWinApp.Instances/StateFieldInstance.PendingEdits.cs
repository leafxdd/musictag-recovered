using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using MusicTag.States;
using MusicTagWinApp.Properties;

namespace MusicTagWinApp.Instances;

// 未保存的标签修改缓存。单选时编辑区的修改边改边记在 FileRow.PendingEdit 上,切换选中、过滤、刷新、改名
// 都不会丢,重新选中时叠加回编辑区;只有保存成功才清除,保存失败(文件被占用等)的文件保留缓存。
// 多选编辑沿用原行为(多选里没保存就切走照旧丢弃),但多选的显示与保存会把已有缓存算进去,
// 不会用文件里的旧值把缓存覆盖。
internal partial class StateFieldInstance
{
	// 一个文件的未保存修改。Fields 只记与文件里不同的字段(字段名 → 编辑区文本);
	// Covers 为 null 表示封面没改,否则是改后的完整封面列表。
	internal sealed class PendingTagEdit
	{
		public readonly Dictionary<string, string> Fields = new Dictionary<string, string>();

		public List<ConfigDescriptorState.PictureData> Covers;

		public bool IsEmpty => Fields.Count == 0 && Covers == null;

		public PendingTagEdit Clone()
		{
			PendingTagEdit copy = new PendingTagEdit();
			foreach (KeyValuePair<string, string> field in Fields)
			{
				copy.Fields.Add(field.Key, field.Value);
			}
			copy.Covers = (Covers == null) ? null : new List<ConfigDescriptorState.PictureData>(Covers);
			return copy;
		}
	}

	// 编辑区保存(Ctrl+S 等)随 StartCommonSaveTags 传入,Rows 与 itemsToSave 一一对应。
	// MultiSelectionChanges / MultiSelectionCovers 只在多选保存时有值:保存失败的文件据此并入这次多选的修改。
	// MultiSelectionCovers 为 null 表示多选没动封面,空列表表示删除全部封面。
	private sealed class EditorSaveContext
	{
		public FileRow[] Rows;

		public List<KeyValuePair<string, string>> MultiSelectionChanges;

		public List<ConfigDescriptorState.PictureData> MultiSelectionCovers;
	}

	// 未保存标识的强调色(深橙,白底对比度约 5:1)。
	private static readonly Color PendingEditAccentColor = Color.FromArgb(180, 80, 0);

	// 编辑区当前代表的行。单选加载完成(含叠加缓存)后才绑定,任何加载或清空之前先解绑,
	// 避免加载过程触发的 TextChanged 把别的文件的值记进这一行的缓存。
	private FileRow editorBoundRow;

	// 多选视图加载完成时各字段的显示值:多选保存失败时,据此找出用户在多选里改过的字段。
	private readonly Dictionary<string, string> multiSelectionBaseline = new Dictionary<string, string>();

	// F5 刷新列表会重建全部行:按路径暂存缓存,新行建立时接回,本次加载结束时清掉没接上的。
	private Dictionary<string, PendingTagEdit> carriedOverPendingEdits;

	private ToolStripSeparator pendingEditsStatusSeparator;

	private ToolStripStatusLabel pendingEditsStatusLabel;

	// 纯核:按"编辑区文本 vs 文件里的值"更新某个字段的缓存,返回更新后的缓存(全部撤回时为 null)。
	internal static PendingTagEdit UpdatePendingField(PendingTagEdit pending, string fieldName, string text, string fileValue)
	{
		if (string.Equals(text, fileValue, StringComparison.Ordinal))
		{
			if (pending == null)
			{
				return null;
			}
			pending.Fields.Remove(fieldName);
			return pending.IsEmpty ? null : pending;
		}
		pending ??= new PendingTagEdit();
		pending.Fields[fieldName] = text;
		return pending;
	}

	// 纯核:多选时某字段在"缓存优先"下是否只有一个值。diskValueCounts 是选中行按文件里的值统计的计数
	// (selectedFilterValueStates,空白已归一为 ""),overrides 是有缓存的选中行的(文件里的值, 缓存值)。
	internal static (bool HasCommonValue, string Value) ResolveCommonValueWithPendingEdits(IReadOnlyDictionary<string, int> diskValueCounts, IEnumerable<(string DiskValue, string PendingValue)> overrides)
	{
		Dictionary<string, int> counts = new Dictionary<string, int>();
		foreach (KeyValuePair<string, int> diskValueCount in diskValueCounts)
		{
			counts.Add(diskValueCount.Key, diskValueCount.Value);
		}
		foreach ((string DiskValue, string PendingValue) pendingOverride in overrides)
		{
			string diskValue = NormalizeSelectionValue(pendingOverride.DiskValue);
			if (counts.TryGetValue(diskValue, out int diskCount))
			{
				if (diskCount <= 1)
				{
					counts.Remove(diskValue);
				}
				else
				{
					counts[diskValue] = diskCount - 1;
				}
			}
			string pendingValue = NormalizeSelectionValue(pendingOverride.PendingValue);
			counts[pendingValue] = counts.TryGetValue(pendingValue, out int pendingCount) ? pendingCount + 1 : 1;
		}
		return (counts.Count == 1) ? (true, counts.Keys.First()) : (false, null);
	}

	private static string NormalizeSelectionValue(string value)
	{
		return string.IsNullOrWhiteSpace(value) ? "" : value;
	}

	// 纯核:多选保存失败时要并入失败文件缓存的字段——用户在多选里改过(与加载时的显示值不同)且不是 <keep>;
	// <blank> 记成空串,与单选编辑区里"清空"一致。
	internal static List<KeyValuePair<string, string>> CollectMultiSelectionChanges(IEnumerable<KeyValuePair<string, string>> currentTexts, IReadOnlyDictionary<string, string> baseline)
	{
		List<KeyValuePair<string, string>> changes = new List<KeyValuePair<string, string>>();
		foreach (KeyValuePair<string, string> field in currentTexts)
		{
			if (field.Value == "<keep>" || (baseline.TryGetValue(field.Key, out string loadedText) && loadedText == field.Value))
			{
				continue;
			}
			changes.Add(new KeyValuePair<string, string>(field.Key, (field.Value == "<blank>") ? "" : field.Value));
		}
		return changes;
	}

	// 纯核:多选保存时,多选框保留(含 <keep>)的音轨号/碟号会把各文件缓存里的值原样写进文件,
	// 要过与编辑区相同的校验。返回第一个不合法的字段名(trackstr/discstr),都合法返回 null。
	internal static string FindInvalidPendingNumberedField(PendingTagEdit pending, string multiTrackText, string multiDiscText)
	{
		if (pending == null)
		{
			return null;
		}
		if (multiTrackText.Contains("<keep>") && pending.Fields.TryGetValue("trackstr", out string track) && !IsNumberedTagFieldValueValid(track))
		{
			return "trackstr";
		}
		if (multiDiscText.Contains("<keep>") && pending.Fields.TryGetValue("discstr", out string disc) && !IsNumberedTagFieldValueValid(disc))
		{
			return "discstr";
		}
		return null;
	}

	// 纯核:单选文件没能读出标签时编辑区是空的,空字段表示"没填",保存时按 <keep> 处理,
	// 不把文件里原有的值清掉。
	internal static string ResolveUnloadedFileFieldValue(string text)
	{
		return (text.Length == 0) ? "<keep>" : text;
	}

	// ComboBox.Text 的 setter 会按忽略大小写匹配下拉项并选中它,文字随之换成下拉项的写法,
	// 只改了大小写的修改会被还原。先摘掉只差大小写的下拉项,写完文字再放回去。
	internal static void SetComboBoxTextExact(ComboBox comboBox, string text)
	{
		List<object> caseVariants = null;
		foreach (object item in comboBox.Items)
		{
			string itemText = comboBox.GetItemText(item);
			if (!string.Equals(itemText, text, StringComparison.Ordinal) && string.Compare(itemText, text, ignoreCase: true, CultureInfo.CurrentCulture) == 0)
			{
				(caseVariants ??= new List<object>()).Add(item);
			}
		}
		caseVariants?.ForEach(comboBox.Items.Remove);
		comboBox.Text = text;
		caseVariants?.ForEach(item => comboBox.Items.Add(item));
	}

	private void InitializePendingEditsIndicator()
	{
		pendingEditsStatusSeparator = new ToolStripSeparator
		{
			BackColor = SystemColors.Control,
			Visible = false
		};
		pendingEditsStatusLabel = new ToolStripStatusLabel
		{
			IsLink = true,
			LinkBehavior = LinkBehavior.HoverUnderline,
			LinkColor = PendingEditAccentColor,
			ActiveLinkColor = PendingEditAccentColor,
			VisitedLinkColor = PendingEditAccentColor,
			TextAlign = ContentAlignment.MiddleLeft,
			Visible = false
		};
		pendingEditsStatusLabel.Click += PendingEditsStatusLabel_Click;
		fileSummaryStatusStrip.ShowItemToolTips = true;
		fileSummaryStatusStrip.Items.Add(pendingEditsStatusSeparator);
		fileSummaryStatusStrip.Items.Add(pendingEditsStatusLabel);
		coverPictureBox.Paint += CoverPicture_PaintPendingEditBadge;
	}

	private int CountPendingEditRows()
	{
		int count = 0;
		foreach (FileRow row in cachedFileListItems)
		{
			if (row.PendingEdit != null)
			{
				count++;
			}
		}
		return count;
	}

	// 状态栏"● N 个文件未保存":为 0 时隐藏,点击选中这些文件。
	private void UpdatePendingEditsStatus()
	{
		if (pendingEditsStatusLabel == null)
		{
			return;
		}
		int count = CountPendingEditRows();
		if (count > 0)
		{
			pendingEditsStatusLabel.Text = string.Format(UiText.Get("● {0} file(s) with unsaved changes", "● {0} 个文件未保存", "● {0} 個檔案未儲存"), count);
			pendingEditsStatusLabel.ToolTipText = UiText.Get("Click to select the files with unsaved changes", "点击选中有未保存修改的文件", "點擊選取有未儲存修改的檔案");
		}
		pendingEditsStatusLabel.Visible = count > 0;
		pendingEditsStatusSeparator.Visible = count > 0;
	}

	private void PendingEditsStatusLabel_Click(object sender, EventArgs e)
	{
		bool anyPending = false;
		bool anyHidden = false;
		foreach (FileRow row in cachedFileListItems)
		{
			if (row.PendingEdit != null)
			{
				anyPending = true;
				anyHidden |= row.IsHidden;
			}
		}
		if (!anyPending)
		{
			return;
		}
		if (anyHidden)
		{
			// 有未保存的文件被过滤隐藏:清空过滤,让它们都能被选中。
			filterTextBox.Text = "";
			ApplyFileListFilter(requireFilterText: false, suspendListSorting: true);
		}
		ApplyFileSelectionMode(FileSelectionMode.PendingEdits, refreshStatusAllInfo: false);
		ScrollFirstSelectedFileRowIntoView();
	}

	// 编辑区绑定的行仍是当前唯一选中的行时才返回它。
	private FileRow GetEditorBoundRow()
	{
		FileRow row = editorBoundRow;
		return (row != null && row == GetSingleSelectedFileRow()) ? row : null;
	}

	private void UnbindEditorFromRow()
	{
		if (editorBoundRow != null)
		{
			editorBoundRow = null;
			coverPictureBox.Invalidate();
		}
	}

	// 编辑区换内容之前:解绑当前行,丢掉上一次多选视图的显示值记录。
	private void ResetEditorPendingEditBinding()
	{
		UnbindEditorFromRow();
		multiSelectionBaseline.Clear();
	}

	// 行在"有/无未保存修改"之间切换时刷新列表标记与状态栏计数(封面角标随缓存清空一起消失)。
	private void OnRowPendingStateMaybeChanged(FileRow row, bool wasPending)
	{
		if (wasPending == (row.PendingEdit != null))
		{
			return;
		}
		InvalidateFileRow(row);
		UpdatePendingEditsStatus();
		if (row == editorBoundRow)
		{
			coverPictureBox.Invalidate();
		}
	}

	private string GetEditorFileValue(string fieldName)
	{
		return (selectedTagState != null) ? selectedTagState.GetDisplayValue(fieldName) : "";
	}

	// 字段 TextChanged:与文件里的值不同就记进绑定行的缓存,改回原值就撤销。
	private void CaptureEditorFieldEdit(string fieldName, string text)
	{
		FileRow row = GetEditorBoundRow();
		if (row == null)
		{
			return;
		}
		bool wasPending = row.PendingEdit != null;
		row.PendingEdit = UpdatePendingField(row.PendingEdit, fieldName, text, GetEditorFileValue(fieldName));
		OnRowPendingStateMaybeChanged(row, wasPending);
	}

	// 单选编辑区改了封面(添加/替换/删除/改类型):记下改后的完整封面列表。多选时没有绑定行,不记。
	private void MarkEditorCoversModified()
	{
		FileRow row = GetEditorBoundRow();
		if (row == null || selectedTagState == null || !selectedTagState.TryGetRawValue("allpicturedata", out object rawCovers) || !(rawCovers is List<ConfigDescriptorState.PictureData> covers))
		{
			return;
		}
		bool wasPending = row.PendingEdit != null;
		row.PendingEdit ??= new PendingTagEdit();
		row.PendingEdit.Covers = covers;
		OnRowPendingStateMaybeChanged(row, wasPending);
		coverPictureBox.Invalidate();
	}

	// 保存前把编辑区当前内容完整同步到绑定行的缓存(正常情况下边改边记已经是最新,这里兜底)。
	private void SyncEditorPendingEditFromControls()
	{
		FileRow row = GetEditorBoundRow();
		if (row == null)
		{
			return;
		}
		bool wasPending = row.PendingEdit != null;
		foreach (KeyValuePair<string, ComboBox> tagField in tagComboBoxes)
		{
			row.PendingEdit = UpdatePendingField(row.PendingEdit, tagField.Key, tagField.Value.Text, GetEditorFileValue(tagField.Key));
		}
		OnRowPendingStateMaybeChanged(row, wasPending);
	}

	// 单选加载完成后:把这一行的未保存缓存叠加回编辑区(只覆盖改过的字段,其余仍取文件里的最新值),再绑定。
	private void RestorePendingEditToEditor(FileRow row)
	{
		PendingTagEdit pending = row.PendingEdit;
		if (pending != null)
		{
			foreach (KeyValuePair<string, string> field in pending.Fields.ToList())
			{
				if (string.Equals(field.Value, GetEditorFileValue(field.Key), StringComparison.Ordinal))
				{
					// 文件里已经是这个值(例如在别处改好了):这一项不再算未保存。
					pending.Fields.Remove(field.Key);
				}
				else if (tagComboBoxes.TryGetValue(field.Key, out ComboBox comboBox))
				{
					SetComboBoxTextExact(comboBox, field.Value);
				}
			}
			if (pending.Covers != null && selectedTagState != null)
			{
				selectedTagState["allpicturedata"] = pending.Covers;
				currentCoverIndex = 0;
				RefreshCoverPreview();
			}
			if (pending.IsEmpty)
			{
				row.PendingEdit = null;
				OnRowPendingStateMaybeChanged(row, wasPending: true);
			}
		}
		editorBoundRow = row;
		coverPictureBox.Invalidate();
	}

	// 多选视图加载后:有未保存缓存的选中文件按"缓存优先"重算公共值,算不出就显示 <keep>,
	// 多选保存时就不会用文件里的旧值把缓存覆盖(lyrics/comment 列表里只存了截断值,一律 <keep>)。
	// 最后记下加载完成时的显示值。
	private void ApplyPendingEditsToMultiSelectionView()
	{
		List<FileRow> pendingRows = null;
		foreach (FileRow row in visibleRows)
		{
			if (row.Selected && row.PendingEdit != null)
			{
				(pendingRows ??= new List<FileRow>()).Add(row);
			}
		}
		if (pendingRows != null)
		{
			foreach (KeyValuePair<string, ComboBox> tagField in tagComboBoxes)
			{
				string fieldName = tagField.Key;
				List<(string DiskValue, string PendingValue)> overrides = new List<(string, string)>();
				foreach (FileRow row in pendingRows)
				{
					if (row.PendingEdit.Fields.TryGetValue(fieldName, out string pendingValue))
					{
						overrides.Add((GetSelectedFilterValue(row, fieldName), pendingValue));
					}
				}
				if (overrides.Count == 0)
				{
					continue;
				}
				string text = "<keep>";
				if (fieldName != "lyrics" && fieldName != "comment")
				{
					(bool hasCommonValue, string commonValue) = ResolveCommonValueWithPendingEdits(selectedFilterValueStates[fieldName].valueCounts, overrides);
					if (hasCommonValue)
					{
						text = commonValue;
					}
				}
				if (tagField.Value.Text != text)
				{
					SetComboBoxTextExact(tagField.Value, text);
				}
			}
		}
		multiSelectionBaseline.Clear();
		foreach (KeyValuePair<string, ComboBox> tagField in tagComboBoxes)
		{
			multiSelectionBaseline[tagField.Key] = tagField.Value.Text;
		}
	}

	// 多选保存前:各选中文件缓存里会被原样写入的音轨号/碟号不合法时,提示是哪个文件并取消保存。
	private bool ValidateSelectedPendingNumberedFields()
	{
		string trackText = tagComboBoxes["trackstr"].Text;
		string discText = tagComboBoxes["discstr"].Text;
		foreach (FileRow row in SelectedFileRows)
		{
			string invalidField = FindInvalidPendingNumberedField(row.PendingEdit, trackText, discText);
			if (invalidField != null)
			{
				DialogService.ShowErrorMessage(((invalidField == "trackstr") ? Resources.Msg_InvalidTrackFormat : Resources.Msg_InvalidDiscFormat) + "\n" + Path.GetFileName(row.FilePath));
				return false;
			}
		}
		return true;
	}

	// 单选文件没能读出标签:空字段按 <keep> 处理;绑定行缓存里改过的封面也一并写入。
	private void ApplyUnloadedFileSaveValues(Dictionary<string, object> valueMap)
	{
		foreach (string key in valueMap.Keys.ToList())
		{
			if (valueMap[key] is string text)
			{
				valueMap[key] = ResolveUnloadedFileFieldValue(text);
			}
		}
		List<ConfigDescriptorState.PictureData> pendingCovers = GetEditorBoundRow()?.PendingEdit?.Covers;
		if (pendingCovers != null)
		{
			valueMap["allpicturedata"] = pendingCovers;
		}
	}

	private EditorSaveContext CreateEditorSaveContext(SelectedListViewItemInfo[] selectedItems)
	{
		EditorSaveContext editorSave = new EditorSaveContext
		{
			Rows = selectedItems.Select(item => cachedFileListItems[item.Index]).ToArray()
		};
		if (selectedItems.Length == 1)
		{
			SyncEditorPendingEditFromControls();
			return editorSave;
		}
		editorSave.MultiSelectionChanges = CollectMultiSelectionChanges(tagComboBoxes.Select(tagField => new KeyValuePair<string, string>(tagField.Key, tagField.Value.Text)), multiSelectionBaseline);
		if (multiSelectionCoverList == null)
		{
			editorSave.MultiSelectionCovers = new List<ConfigDescriptorState.PictureData>();
		}
		else if (multiSelectionCoverList.Any())
		{
			editorSave.MultiSelectionCovers = new List<ConfigDescriptorState.PictureData>(multiSelectionCoverList);
		}
		return editorSave;
	}

	// 多选保存:每个文件先套用自己的未保存缓存,再套用多选框的值。在 UI 线程上拍快照交给保存线程。
	private static PendingTagEdit[] SnapshotPendingEditsForSave(FileRow[] rows)
	{
		if (!rows.Any(row => row.PendingEdit != null))
		{
			return null;
		}
		return rows.Select(row => row.PendingEdit?.Clone()).ToArray();
	}

	// 编辑区保存结束:保存成功的文件清掉缓存;失败或没处理到的(被占用、取消、封面压缩失败)保留,
	// 多选保存再把这次多选里改过的内容并进去。有保留时在结果提示末尾说明。
	private (string, bool) ApplyEditorSaveOutcome(EditorSaveContext editorSave, bool[] itemSaved, (string, bool) message)
	{
		bool anyRetained = false;
		for (int i = 0; i < editorSave.Rows.Length; i++)
		{
			FileRow row = editorSave.Rows[i];
			bool wasPending = row.PendingEdit != null;
			if (itemSaved[i])
			{
				row.PendingEdit = null;
			}
			else
			{
				MergeMultiSelectionChangesIntoPending(row, editorSave);
				anyRetained |= row.PendingEdit != null;
			}
			OnRowPendingStateMaybeChanged(row, wasPending);
		}
		if (anyRetained)
		{
			message.Item1 = message.Item1 + "\n\n" + UiText.Get(
				"The changes of the files that could not be saved have been kept (marked with * in the list). You can save them again later.",
				"未能保存的文件,修改已保留(列表中带 * 标记),可稍后重新保存。",
				"未能儲存的檔案,修改已保留(清單中帶 * 標記),可稍後重新儲存。");
		}
		return message;
	}

	private static void MergeMultiSelectionChangesIntoPending(FileRow row, EditorSaveContext editorSave)
	{
		if (editorSave.MultiSelectionChanges == null)
		{
			return;
		}
		foreach (KeyValuePair<string, string> change in editorSave.MultiSelectionChanges)
		{
			row.PendingEdit ??= new PendingTagEdit();
			row.PendingEdit.Fields[change.Key] = change.Value;
		}
		if (editorSave.MultiSelectionCovers != null)
		{
			row.PendingEdit ??= new PendingTagEdit();
			row.PendingEdit.Covers = new List<ConfigDescriptorState.PictureData>(editorSave.MultiSelectionCovers);
		}
	}

	// 只统计文件仍然存在的行:文件已被删除时缓存已无意义(删除文件后会走"从列表移除")。
	private int CountDiscardablePendingEdits(IEnumerable<FileRow> rows)
	{
		return rows.Count(row => row.PendingEdit != null && File.Exists(row.FilePath));
	}

	// 有未保存修改的文件将被移出列表时确认;没有就直接放行。
	private bool ConfirmDiscardPendingEdits(IEnumerable<FileRow> rows)
	{
		int count = CountDiscardablePendingEdits(rows);
		return count == 0 || DialogService.ConfirmYesNo(string.Format(UiText.Get(
			"{0} file(s) have unsaved tag changes. Continuing will discard them.\nContinue?",
			"有 {0} 个文件的标签修改尚未保存,继续将放弃这些修改。\n确定继续吗?",
			"有 {0} 個檔案的標籤修改尚未儲存,繼續將放棄這些修改。\n確定繼續嗎?"), count));
	}

	// 读取标签 = 从文件重新读取:选中文件里有未保存修改时先确认,确认后放弃这些修改。
	private bool DiscardSelectedPendingEditsForReload()
	{
		List<FileRow> rows = SelectedFileRows.Where(row => row.PendingEdit != null).ToList();
		if (rows.Count == 0)
		{
			return true;
		}
		if (!DialogService.ConfirmYesNo(string.Format(UiText.Get(
			"{0} of the selected file(s) have unsaved tag changes. Reading the tags again will discard them.\nContinue?",
			"选中的文件中有 {0} 个有未保存的标签修改,重新读取将放弃这些修改。\n确定继续吗?",
			"選取的檔案中有 {0} 個有未儲存的標籤修改,重新讀取將放棄這些修改。\n確定繼續嗎?"), rows.Count)))
		{
			return false;
		}
		if (rows.Contains(editorBoundRow))
		{
			UnbindEditorFromRow();
		}
		foreach (FileRow row in rows)
		{
			row.PendingEdit = null;
			InvalidateFileRow(row);
		}
		UpdatePendingEditsStatus();
		return true;
	}

	private bool ConfirmExitWithPendingEdits()
	{
		int count = CountPendingEditRows();
		return count == 0 || DialogService.ConfirmYesNo(string.Format(UiText.Get(
			"{0} file(s) have unsaved tag changes, which will be lost on exit.\n\nYes: discard the changes and exit\nNo: go back",
			"有 {0} 个文件的标签修改尚未保存,退出后这些修改会丢失。\n\n是:放弃修改并退出\n否:返回",
			"有 {0} 個檔案的標籤修改尚未儲存,退出後這些修改會遺失。\n\n是:放棄修改並退出\n否:返回"), count));
	}

	private void CarryOverPendingEditsForReload()
	{
		carriedOverPendingEdits = null;
		foreach (FileRow row in cachedFileListItems)
		{
			if (row.PendingEdit != null)
			{
				(carriedOverPendingEdits ??= new Dictionary<string, PendingTagEdit>(StringComparer.OrdinalIgnoreCase))[row.FilePath] = row.PendingEdit;
			}
		}
	}

	private PendingTagEdit TakeCarriedOverPendingEdit(string filePath)
	{
		if (carriedOverPendingEdits == null || !carriedOverPendingEdits.TryGetValue(filePath, out PendingTagEdit pending))
		{
			return null;
		}
		carriedOverPendingEdits.Remove(filePath);
		return pending;
	}

	private void FinishPendingEditCarryOver()
	{
		carriedOverPendingEdits = null;
		UpdatePendingEditsStatus();
	}

	// 有未保存修改时,文件名前画 *,未选中时用强调色(加载失败的红色优先)。
	private static (string Text, Color ForeColor) DecoratePendingEditCellText(FileRow fileRow, string text, Color foreColor, bool isSelected)
	{
		if (fileRow.PendingEdit == null)
		{
			return (text, foreColor);
		}
		return ("*" + text, (isSelected || fileRow.LoadFailed) ? foreColor : PendingEditAccentColor);
	}

	// 文件名列被隐藏时,把 * 标记画在第一个显示的列上。
	private void PaintPendingEditMarkerInFirstColumn(DataGridViewCellPaintingEventArgs e)
	{
		FileRow fileRow = visibleRows[e.RowIndex];
		if (fileRow.PendingEdit == null || fileListView.Columns[0].Visible)
		{
			return;
		}
		DataGridViewColumn firstColumn = fileListView.Columns.GetFirstColumn(DataGridViewElementStates.Visible);
		if (firstColumn == null || firstColumn.Index != e.ColumnIndex)
		{
			return;
		}
		bool isSelected = (e.State & DataGridViewElementStates.Selected) != 0;
		e.PaintBackground(e.CellBounds, isSelected);
		(string text, Color foreColor) = DecoratePendingEditCellText(fileRow, e.FormattedValue as string ?? "", isSelected ? e.CellStyle.SelectionForeColor : e.CellStyle.ForeColor, isSelected);
		Rectangle textBounds = new Rectangle(e.CellBounds.Left + fileListIconPadding, e.CellBounds.Top, Math.Max(0, e.CellBounds.Width - fileListIconPadding), e.CellBounds.Height);
		TextRenderer.DrawText(e.Graphics, text, e.CellStyle.Font, textBounds, foreColor, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
		e.Handled = true;
	}

	// 单选编辑区的封面有未保存修改时,在封面预览左上角标"(已修改)",与字段标签的提示一致。
	private void CoverPicture_PaintPendingEditBadge(object sender, PaintEventArgs e)
	{
		if (GetEditorBoundRow()?.PendingEdit?.Covers == null)
		{
			return;
		}
		string text = "(" + Resources.Changed + ")";
		Size textSize = TextRenderer.MeasureText(e.Graphics, text, coverPictureBox.Font, Size.Empty, TextFormatFlags.NoPrefix);
		Rectangle badgeBounds = new Rectangle(1, 1, textSize.Width + 4, textSize.Height + 2);
		using (SolidBrush background = new SolidBrush(Color.FromArgb(220, Color.White)))
		{
			e.Graphics.FillRectangle(background, badgeBounds);
		}
		TextRenderer.DrawText(e.Graphics, text, coverPictureBox.Font, badgeBounds, PendingEditAccentColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
	}
}
