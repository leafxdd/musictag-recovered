using System;
using System.Collections.Generic;
using MusicTag.Schemes;
using MusicTag.States;

namespace MusicTag.Tests;

// FilenameRelatedBatchDialog.PendingTagUpdate 的 characterization（纯逻辑分支、无音频 fixture）。
// 锁定"文件名->标签"批量改标签路径的纯数据变换:数字型标签门控(SetNumberedTag)与 disc/track
// 占位符解析(SetFilenamePatternTag 的 @4/@5/@4@5)。这些分支仅写 Changes 字典、不读 TagState,
// 故传 null TagState 隔离音频依赖——读 TagState 的文本分支(@1/@2/@3/@6/@7/@8,经 SetTextTagIfChanged)
// 留待音频 round-trip 批次。可见性由 private sealed 放宽为 internal sealed(仅可见性、零逻辑)。
internal static class PendingTagUpdateCharacterization
{
	// 这些纯分支不触碰 TagState,传 null 隔离音频依赖(若误触会 NRE,正好暴露依赖)。
	private static FilenameRelatedBatchDialog.PendingTagUpdate NewUpdate()
	{
		return new FilenameRelatedBatchDialog.PendingTagUpdate((ConfigDescriptorState)null);
	}

	public static IEnumerable<(string, Action)> All()
	{
		// --- SetNumberedTag：全数字且 parse>0 -> 规范化;parse=0 -> 空串;否则原值 ---
		yield return ("PendingTagUpdate.SetNumberedTag \"05\" -> \"5\" (leading zero normalized)", delegate
		{
			var update = NewUpdate();
			update.SetNumberedTag("trackstr", "05");
			Check.Equal("5", update.Changes["trackstr"], "trackstr");
		});

		yield return ("PendingTagUpdate.SetNumberedTag \"0\" -> \"\" (zero becomes empty)", delegate
		{
			var update = NewUpdate();
			update.SetNumberedTag("trackstr", "0");
			Check.Equal("", update.Changes["trackstr"], "trackstr");
		});

		yield return ("PendingTagUpdate.SetNumberedTag empty -> \"\" (all-digit vacuous, parse fails -> raw)", delegate
		{
			var update = NewUpdate();
			update.SetNumberedTag("trackstr", "");
			Check.Equal("", update.Changes["trackstr"], "trackstr");
		});

		yield return ("PendingTagUpdate.SetNumberedTag \"12a\" -> \"12a\" (non-numeric kept raw)", delegate
		{
			var update = NewUpdate();
			update.SetNumberedTag("discstr", "12a");
			Check.Equal("12a", update.Changes["discstr"], "discstr");
		});

		yield return ("PendingTagUpdate.SetNumberedTag \"-5\" -> \"-5\" (sign is non-digit, kept raw)", delegate
		{
			var update = NewUpdate();
			update.SetNumberedTag("discstr", "-5");
			Check.Equal("-5", update.Changes["discstr"], "discstr");
		});

		// --- SetFilenamePatternTag @4/@5：int.TryParse 成功才写,直接 ToString(无 >0 门控,与 SetNumberedTag 有别) ---
		yield return ("PendingTagUpdate.SetFilenamePatternTag @4 \"5\" -> discstr \"5\"", delegate
		{
			var update = NewUpdate();
			update.SetFilenamePatternTag("@4", "5");
			Check.Equal("5", update.Changes["discstr"], "discstr");
		});

		yield return ("PendingTagUpdate.SetFilenamePatternTag @5 \"07\" -> trackstr \"7\" (parsed, leading zero dropped)", delegate
		{
			var update = NewUpdate();
			update.SetFilenamePatternTag("@5", "07");
			Check.Equal("7", update.Changes["trackstr"], "trackstr");
		});

		yield return ("PendingTagUpdate.SetFilenamePatternTag @5 \"0\" -> trackstr \"0\" (differs from SetNumberedTag's \"\")", delegate
		{
			var update = NewUpdate();
			update.SetFilenamePatternTag("@5", "0");
			Check.Equal("0", update.Changes["trackstr"], "trackstr");
		});

		yield return ("PendingTagUpdate.SetFilenamePatternTag @4 \"abc\" -> no change (parse fails)", delegate
		{
			var update = NewUpdate();
			update.SetFilenamePatternTag("@4", "abc");
			Check.True(!update.Changes.ContainsKey("discstr"), "discstr absent");
		});

		// --- SetFilenamePatternTag @4@5：合并值 disc*100+track,整除/取余拆回 ---
		yield return ("PendingTagUpdate.SetFilenamePatternTag @4@5 \"312\" -> disc 3 / track 12", delegate
		{
			var update = NewUpdate();
			update.SetFilenamePatternTag("@4@5", "312");
			Check.Equal("3", update.Changes["discstr"], "discstr");
			Check.Equal("12", update.Changes["trackstr"], "trackstr");
		});

		yield return ("PendingTagUpdate.SetFilenamePatternTag @4@5 \"5\" -> disc 0 / track 5", delegate
		{
			var update = NewUpdate();
			update.SetFilenamePatternTag("@4@5", "5");
			Check.Equal("0", update.Changes["discstr"], "discstr");
			Check.Equal("5", update.Changes["trackstr"], "trackstr");
		});

		yield return ("PendingTagUpdate.SetFilenamePatternTag @4@5 \"1234\" -> disc 12 / track 34", delegate
		{
			var update = NewUpdate();
			update.SetFilenamePatternTag("@4@5", "1234");
			Check.Equal("12", update.Changes["discstr"], "discstr");
			Check.Equal("34", update.Changes["trackstr"], "trackstr");
		});

		yield return ("PendingTagUpdate.SetFilenamePatternTag unknown token -> no change", delegate
		{
			var update = NewUpdate();
			update.SetFilenamePatternTag("@9", "whatever");
			Check.True(update.Changes.Count == 0, "Changes empty");
		});
	}
}
