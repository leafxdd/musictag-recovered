using System;
using System.Collections.Generic;
using MusicTagWinApp.Adapter;
using MusicTagWinApp.Roles;

namespace MusicTag.Tests;

// AutoMatchTagsDialog 三个字段 / 元数据分类纯谓词的 characterization。三者由 AutoMatchWorker 内 private static
// 上提为外层 AutoMatchTagsDialog 的 internal static(仅可见性 private->internal + 移到外层类,零逻辑改动,
// 嵌套调用点按简单名/方法组仍解析到外层),经 IVT 可测。均 ordinal 字符串/字段比较,locale 无关、确定性。
internal static class AutoMatchFieldClassificationCharacterization
{
	public static IEnumerable<(string, Action)> All()
	{
		// ===== IsSameTrackMetadata:Title + Artist + Album 三字段 ordinal 全等(跨源 alternate track 复用判定)=====
		yield return ("AutoMatchTagsDialog.IsSameTrackMetadata: all-three-equal predicate", delegate
		{
			TrackSearchResult a = new TrackSearchResult { Title = "T", Artist = "A", Album = "Al" };
			Check.True(AutoMatchTagsDialog.IsSameTrackMetadata(a, new TrackSearchResult { Title = "T", Artist = "A", Album = "Al" }), "all three equal -> true");
			Check.True(!AutoMatchTagsDialog.IsSameTrackMetadata(a, new TrackSearchResult { Title = "T2", Artist = "A", Album = "Al" }), "title differs -> false");
			Check.True(!AutoMatchTagsDialog.IsSameTrackMetadata(a, new TrackSearchResult { Title = "T", Artist = "A2", Album = "Al" }), "artist differs -> false");
			Check.True(!AutoMatchTagsDialog.IsSameTrackMetadata(a, new TrackSearchResult { Title = "T", Artist = "A", Album = "Al2" }), "album differs -> false");
			Check.True(!AutoMatchTagsDialog.IsSameTrackMetadata(new TrackSearchResult { Title = "Song" }, new TrackSearchResult { Title = "song" }), "case-sensitive ordinal -> false");
			Check.True(AutoMatchTagsDialog.IsSameTrackMetadata(new TrackSearchResult(), new TrackSearchResult()), "two defaults (null fields) -> true (null==null)");
		});

		// ===== IsTextTagMatchKey:排除 cover/lyrics 两个 magic string,余皆文本标签键 =====
		yield return ("AutoMatchTagsDialog.IsTextTagMatchKey: excludes cover/lyrics magic strings", delegate
		{
			Check.True(AutoMatchTagsDialog.IsTextTagMatchKey("title"), "title -> true");
			Check.True(!AutoMatchTagsDialog.IsTextTagMatchKey("cover"), "cover -> false");
			Check.True(!AutoMatchTagsDialog.IsTextTagMatchKey("lyrics"), "lyrics -> false");
			Check.True(AutoMatchTagsDialog.IsTextTagMatchKey("Cover"), "Cover (case) -> true (ordinal exact match)");
			Check.True(AutoMatchTagsDialog.IsTextTagMatchKey(null), "null -> true (null != both literals)");
		});

		// ===== IsYearFieldName:仅精确匹配 "year" =====
		yield return ("AutoMatchTagsDialog.IsYearFieldName: exact year match", delegate
		{
			Check.True(AutoMatchTagsDialog.IsYearFieldName("year"), "year -> true");
			Check.True(!AutoMatchTagsDialog.IsYearFieldName("Year"), "Year (case) -> false (ordinal)");
			Check.True(!AutoMatchTagsDialog.IsYearFieldName("title"), "title -> false");
			Check.True(!AutoMatchTagsDialog.IsYearFieldName(null), "null -> false");
			Check.True(!AutoMatchTagsDialog.IsYearFieldName(""), "empty -> false");
		});
	}
}
