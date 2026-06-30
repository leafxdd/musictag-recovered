using System;
using System.Collections.Generic;
using MusicTag.Schemes;
using MusicTag.States;

namespace MusicTag.Tests;

// FilenameRelatedBatchDialog.PendingTagUpdate.SetRegexCaptureTag 的 characterization。
// 该方法由本批从 ChangeTags 的 regex-capture 分支提取(byte-identical move):把 regex 捕获组序号
// (selectedTagIndex 1-8)路由到标签键。锁定它与【非 regex 路径】SetFilenamePatternTag(@1-@8)的
// 语义分叉(CLAUDE.md 列为"三处 @N↔标签映射不可合并"之一):
//   - 文本字段(1/2/3/6/7/8): 在此【盲写】Changes[key]=value —— 不跳空、不读 TagState、不做变更检查;
//     而 @1/@2/@3/@6/@7/@8 经 SetTextTagIfChanged(空白跳过 + 与 TagState 现值比较)。
//   - disc/track(4/5): 在此走 SetNumberedTag(>0->空 门控,"0"->""、"07"->"7");
//     而 @4/@5 走无门控的 int.TryParse("0"->"0")。
//   - 未知 index: switch 无 default -> no-op。
// 全 case 均不读 TagState,故用 new PendingTagUpdate(null) 隔离。
internal static class SetRegexCaptureTagCharacterization
{
	private static FilenameRelatedBatchDialog.PendingTagUpdate NewUpdate()
	{
		return new FilenameRelatedBatchDialog.PendingTagUpdate((ConfigDescriptorState)null);
	}

	public static IEnumerable<(string, Action)> All()
	{
		// --- tagName 映射(文本字段盲写) ---
		yield return ("SetRegexCaptureTag 1 -> title (blind write)", delegate
		{
			FilenameRelatedBatchDialog.PendingTagUpdate update = NewUpdate();
			update.SetRegexCaptureTag(1, "T");
			Check.Equal("T", update.Changes["title"], "title");
		});

		yield return ("SetRegexCaptureTag 2 -> artist", delegate
		{
			FilenameRelatedBatchDialog.PendingTagUpdate update = NewUpdate();
			update.SetRegexCaptureTag(2, "A");
			Check.Equal("A", update.Changes["artist"], "artist");
		});

		yield return ("SetRegexCaptureTag 3 -> album", delegate
		{
			FilenameRelatedBatchDialog.PendingTagUpdate update = NewUpdate();
			update.SetRegexCaptureTag(3, "Al");
			Check.Equal("Al", update.Changes["album"], "album");
		});

		yield return ("SetRegexCaptureTag 6 -> year", delegate
		{
			FilenameRelatedBatchDialog.PendingTagUpdate update = NewUpdate();
			update.SetRegexCaptureTag(6, "2020");
			Check.Equal("2020", update.Changes["year"], "year");
		});

		yield return ("SetRegexCaptureTag 7 -> comment", delegate
		{
			FilenameRelatedBatchDialog.PendingTagUpdate update = NewUpdate();
			update.SetRegexCaptureTag(7, "C");
			Check.Equal("C", update.Changes["comment"], "comment");
		});

		yield return ("SetRegexCaptureTag 8 -> albumartist", delegate
		{
			FilenameRelatedBatchDialog.PendingTagUpdate update = NewUpdate();
			update.SetRegexCaptureTag(8, "AA");
			Check.Equal("AA", update.Changes["albumartist"], "albumartist");
		});

		// --- 文本字段盲写:空值也记(不跳空) —— 对比 SetFilenamePatternTag @1 空值则跳过不记 ---
		yield return ("SetRegexCaptureTag 1 empty -> recorded as \"\" (blind, NOT skipped unlike @1)", delegate
		{
			FilenameRelatedBatchDialog.PendingTagUpdate update = NewUpdate();
			update.SetRegexCaptureTag(1, "");
			Check.True(update.Changes.ContainsKey("title"), "title recorded even when empty");
			Check.Equal("", update.Changes["title"], "title empty");
		});

		// --- disc/track 走 SetNumberedTag(>0->空 门控) —— 对比 @4 的 int.TryParse("0"->"0") ---
		yield return ("SetRegexCaptureTag 4 \"5\" -> discstr \"5\"", delegate
		{
			FilenameRelatedBatchDialog.PendingTagUpdate update = NewUpdate();
			update.SetRegexCaptureTag(4, "5");
			Check.Equal("5", update.Changes["discstr"], "discstr");
		});

		yield return ("SetRegexCaptureTag 4 \"0\" -> discstr \"\" (SetNumberedTag zero-gate, NOT \"0\" unlike @4)", delegate
		{
			FilenameRelatedBatchDialog.PendingTagUpdate update = NewUpdate();
			update.SetRegexCaptureTag(4, "0");
			Check.Equal("", update.Changes["discstr"], "discstr zero -> empty");
		});

		yield return ("SetRegexCaptureTag 5 \"07\" -> trackstr \"7\" (leading zero normalized)", delegate
		{
			FilenameRelatedBatchDialog.PendingTagUpdate update = NewUpdate();
			update.SetRegexCaptureTag(5, "07");
			Check.Equal("7", update.Changes["trackstr"], "trackstr");
		});

		// --- 未知 index: switch 无 default -> no-op ---
		yield return ("SetRegexCaptureTag unknown index 9 -> no change", delegate
		{
			FilenameRelatedBatchDialog.PendingTagUpdate update = NewUpdate();
			update.SetRegexCaptureTag(9, "X");
			Check.Equal(0, update.Changes.Count, "no changes");
		});

		yield return ("SetRegexCaptureTag index 0 -> no change", delegate
		{
			FilenameRelatedBatchDialog.PendingTagUpdate update = NewUpdate();
			update.SetRegexCaptureTag(0, "X");
			Check.Equal(0, update.Changes.Count, "no changes");
		});
	}
}
