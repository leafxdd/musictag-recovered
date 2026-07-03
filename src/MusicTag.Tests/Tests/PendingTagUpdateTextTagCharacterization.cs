using System;
using System.Collections.Generic;
using MusicTag.Schemes;
using MusicTag.States;

namespace MusicTag.Tests;

// FilenameRelatedBatchDialog.PendingTagUpdate 的【文本 tag 写入路径】characterization。
// 补此前缺口:PendingTagUpdateCharacterization 只覆盖数字 case(@4/@5/@4@5,传 null TagState、
// 直写 Changes["discstr"/"trackstr"]);文本 case(@1/@2/@3/@6/@7/@8)经 private SetTextTagIfChanged
// 做【变更门控】(value 非空白 **且** value != TagState.GetDisplayValue(tagName) 才记入 Changes),
// 读 TagState,故用 new ConfigDescriptorState() 空构造 + indexer 预置当前显示值。
// 另覆盖 ApplyChanges(把 Changes 逐键写回 TagState[key])。全部走 public 方法,零生产改动。
internal static class PendingTagUpdateTextTagCharacterization
{
	private static FilenameRelatedBatchDialog.PendingTagUpdate NewUpdate(ConfigDescriptorState state)
	{
		return new FilenameRelatedBatchDialog.PendingTagUpdate(state);
	}

	public static IEnumerable<(string, Action)> All()
	{
		// --- SetTextTagIfChanged 变更门控四态(以 @1 title 演示) ---
		yield return ("PendingTagUpdate @1 title differs from current -> recorded", delegate
		{
			ConfigDescriptorState state = new ConfigDescriptorState();
			state["title"] = "Old";
			FilenameRelatedBatchDialog.PendingTagUpdate update = NewUpdate(state);
			update.SetFilenamePatternTag("@1", "New");
			Check.True(update.Changes.ContainsKey("title"), "has title change");
			Check.Equal("New", update.Changes["title"], "title");
		});

		yield return ("PendingTagUpdate @1 title equals current -> NOT recorded", delegate
		{
			ConfigDescriptorState state = new ConfigDescriptorState();
			state["title"] = "Same";
			FilenameRelatedBatchDialog.PendingTagUpdate update = NewUpdate(state);
			update.SetFilenamePatternTag("@1", "Same");
			Check.True(!update.Changes.ContainsKey("title"), "unchanged value not recorded");
		});

		yield return ("PendingTagUpdate @1 blank value -> NOT recorded (IsNullOrWhiteSpace)", delegate
		{
			ConfigDescriptorState state = new ConfigDescriptorState();
			state["title"] = "Old";
			FilenameRelatedBatchDialog.PendingTagUpdate update = NewUpdate(state);
			update.SetFilenamePatternTag("@1", "   ");
			Check.True(!update.Changes.ContainsKey("title"), "blank not recorded");
		});

		yield return ("PendingTagUpdate @1 current missing (empty display) -> recorded", delegate
		{
			ConfigDescriptorState state = new ConfigDescriptorState();
			FilenameRelatedBatchDialog.PendingTagUpdate update = NewUpdate(state);
			update.SetFilenamePatternTag("@1", "New");
			Check.Equal("New", update.Changes["title"], "title from missing current");
		});

		// --- tagName 映射:@2/@3/@6/@7/@8 -> artist/album/year/comment/albumartist ---
		yield return ("PendingTagUpdate @2 -> artist", delegate
		{
			ConfigDescriptorState state = new ConfigDescriptorState();
			FilenameRelatedBatchDialog.PendingTagUpdate update = NewUpdate(state);
			update.SetFilenamePatternTag("@2", "ArtistX");
			Check.Equal("ArtistX", update.Changes["artist"], "artist");
		});

		yield return ("PendingTagUpdate @3 -> album", delegate
		{
			ConfigDescriptorState state = new ConfigDescriptorState();
			FilenameRelatedBatchDialog.PendingTagUpdate update = NewUpdate(state);
			update.SetFilenamePatternTag("@3", "AlbumX");
			Check.Equal("AlbumX", update.Changes["album"], "album");
		});

		yield return ("PendingTagUpdate @6 -> year", delegate
		{
			ConfigDescriptorState state = new ConfigDescriptorState();
			FilenameRelatedBatchDialog.PendingTagUpdate update = NewUpdate(state);
			update.SetFilenamePatternTag("@6", "2021");
			Check.Equal("2021", update.Changes["year"], "year");
		});

		yield return ("PendingTagUpdate @7 -> comment", delegate
		{
			ConfigDescriptorState state = new ConfigDescriptorState();
			FilenameRelatedBatchDialog.PendingTagUpdate update = NewUpdate(state);
			update.SetFilenamePatternTag("@7", "CommentX");
			Check.Equal("CommentX", update.Changes["comment"], "comment");
		});

		yield return ("PendingTagUpdate @8 -> albumartist", delegate
		{
			ConfigDescriptorState state = new ConfigDescriptorState();
			FilenameRelatedBatchDialog.PendingTagUpdate update = NewUpdate(state);
			update.SetFilenamePatternTag("@8", "AlbumArtistX");
			Check.Equal("AlbumArtistX", update.Changes["albumartist"], "albumartist");
		});

		// --- ApplyChanges:把 Changes 逐键写回 TagState ---
		yield return ("PendingTagUpdate ApplyChanges writes text change back to TagState", delegate
		{
			ConfigDescriptorState state = new ConfigDescriptorState();
			state["title"] = "Old";
			FilenameRelatedBatchDialog.PendingTagUpdate update = NewUpdate(state);
			update.SetFilenamePatternTag("@1", "New");
			update.ApplyChanges();
			Check.Equal("New", state.GetDisplayValue("title"), "applied title");
		});

		yield return ("PendingTagUpdate ApplyChanges writes mixed text + numeric (@1 + @4)", delegate
		{
			ConfigDescriptorState state = new ConfigDescriptorState();
			FilenameRelatedBatchDialog.PendingTagUpdate update = NewUpdate(state);
			update.SetFilenamePatternTag("@1", "T");
			update.SetFilenamePatternTag("@4", "3");
			update.ApplyChanges();
			Check.Equal("T", state.GetDisplayValue("title"), "applied title");
			Check.Equal("3", state.GetDisplayValue("discstr"), "applied discstr");
		});
	}
}
