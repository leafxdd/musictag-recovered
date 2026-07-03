using System;
using System.Collections.Generic;
using MusicTag.Mocks;
using MusicTagWinApp.Roles;
using MusicTagWinApp.Web;

namespace MusicTag.Tests;

// CombinedTagSearchDialog.SelectResultsWithinSourceCaps characterization —— 每源 + 全局上限过滤本批候选
// 的纯计数器数学(B2 后从 private nested TrackResultLimitCollector.AddIfWithinLimit 提取为可测 static，
// 行为逐字节保持)。载荷用户可见行为:每源在候选列表中出现多少条。
// 锁定:全局余额 <=0 或该源余额 <=0 -> 跳过;否则收录并同时递减全局(ref 回写)与该源(dict 原地)计数器;
//   收录顺序 = 输入顺序;全局余额的 || 短路 —— 全局耗尽时不索引 dict(缺失源也不抛 KeyNotFound)。
internal static class TrackResultLimitCharacterization
{
	private static TrackSearchResult Track(SearchSource source)
	{
		TrackSearchResult track = new TrackSearchResult();
		track.SearchSource = source;
		return track;
	}

	public static IEnumerable<(string, Action)> All()
	{
		// 1. 全局上限耗尽:global=2，4 条同源 -> 收前 2 条，global 归 0，源计数递减 2。
		yield return ("SelectResultsWithinSourceCaps: global cap exhausted", delegate
		{
			int global = 2;
			Dictionary<SearchSource, int> bySource = new Dictionary<SearchSource, int> { { SearchSource.Music163, 10 } };
			List<TrackSearchResult> batch = new List<TrackSearchResult> { Track(SearchSource.Music163), Track(SearchSource.Music163), Track(SearchSource.Music163), Track(SearchSource.Music163) };
			List<TrackSearchResult> limited = CombinedTagSearchDialog.SelectResultsWithinSourceCaps(batch, bySource, ref global);
			Check.Equal(2, limited.Count, "kept count");
			Check.Equal(0, global, "global remaining");
			Check.Equal(8, bySource[SearchSource.Music163], "per-source remaining");
		});

		// 2. 单源上限耗尽:global 充足，源 cap=2，3 条 -> 收 2 条。
		yield return ("SelectResultsWithinSourceCaps: per-source cap exhausted", delegate
		{
			int global = 10;
			Dictionary<SearchSource, int> bySource = new Dictionary<SearchSource, int> { { SearchSource.Music163, 2 } };
			List<TrackSearchResult> batch = new List<TrackSearchResult> { Track(SearchSource.Music163), Track(SearchSource.Music163), Track(SearchSource.Music163) };
			List<TrackSearchResult> limited = CombinedTagSearchDialog.SelectResultsWithinSourceCaps(batch, bySource, ref global);
			Check.Equal(2, limited.Count, "kept count");
			Check.Equal(8, global, "global remaining");
			Check.Equal(0, bySource[SearchSource.Music163], "per-source remaining");
		});

		// 3. 混源各自 cap=1:[163,163,QQ,QQ] -> 收 [163,QQ]。
		yield return ("SelectResultsWithinSourceCaps: mixed sources each capped", delegate
		{
			int global = 10;
			Dictionary<SearchSource, int> bySource = new Dictionary<SearchSource, int> { { SearchSource.Music163, 1 }, { SearchSource.QQ, 1 } };
			List<TrackSearchResult> batch = new List<TrackSearchResult> { Track(SearchSource.Music163), Track(SearchSource.Music163), Track(SearchSource.QQ), Track(SearchSource.QQ) };
			List<TrackSearchResult> limited = CombinedTagSearchDialog.SelectResultsWithinSourceCaps(batch, bySource, ref global);
			Check.Equal(2, limited.Count, "kept count");
			Check.Equal(SearchSource.Music163, limited[0].SearchSource, "first kept source");
			Check.Equal(SearchSource.QQ, limited[1].SearchSource, "second kept source");
			Check.Equal(8, global, "global remaining");
			Check.Equal(0, bySource[SearchSource.Music163], "163 remaining");
			Check.Equal(0, bySource[SearchSource.QQ], "QQ remaining");
		});

		// 4. 空批 -> 空结果，计数不变。
		yield return ("SelectResultsWithinSourceCaps: empty batch keeps nothing", delegate
		{
			int global = 5;
			Dictionary<SearchSource, int> bySource = new Dictionary<SearchSource, int> { { SearchSource.Music163, 5 } };
			List<TrackSearchResult> limited = CombinedTagSearchDialog.SelectResultsWithinSourceCaps(new List<TrackSearchResult>(), bySource, ref global);
			Check.Equal(0, limited.Count, "kept count");
			Check.Equal(5, global, "global unchanged");
		});

		// 5. 全局边界恰剩 1:2 条 -> 收 1 条，global 归 0。
		yield return ("SelectResultsWithinSourceCaps: global exactly 1 keeps one", delegate
		{
			int global = 1;
			Dictionary<SearchSource, int> bySource = new Dictionary<SearchSource, int> { { SearchSource.Music163, 5 } };
			List<TrackSearchResult> batch = new List<TrackSearchResult> { Track(SearchSource.Music163), Track(SearchSource.Music163) };
			List<TrackSearchResult> limited = CombinedTagSearchDialog.SelectResultsWithinSourceCaps(batch, bySource, ref global);
			Check.Equal(1, limited.Count, "kept count");
			Check.Equal(0, global, "global remaining");
			Check.Equal(4, bySource[SearchSource.Music163], "per-source remaining");
		});

		// 6. 单源归零而全局仍正:源 cap=1，2 条同源 -> 收 1，global 仍正。
		yield return ("SelectResultsWithinSourceCaps: per-source zero while global positive", delegate
		{
			int global = 10;
			Dictionary<SearchSource, int> bySource = new Dictionary<SearchSource, int> { { SearchSource.Music163, 1 }, { SearchSource.QQ, 5 } };
			List<TrackSearchResult> batch = new List<TrackSearchResult> { Track(SearchSource.Music163), Track(SearchSource.Music163) };
			List<TrackSearchResult> limited = CombinedTagSearchDialog.SelectResultsWithinSourceCaps(batch, bySource, ref global);
			Check.Equal(1, limited.Count, "kept count");
			Check.Equal(9, global, "global still positive");
			Check.Equal(0, bySource[SearchSource.Music163], "163 exhausted");
		});

		// 7. 收录顺序 = 输入顺序(引用不变)。
		yield return ("SelectResultsWithinSourceCaps: preserves input order by reference", delegate
		{
			int global = 10;
			Dictionary<SearchSource, int> bySource = new Dictionary<SearchSource, int> { { SearchSource.Music163, 5 }, { SearchSource.QQ, 5 }, { SearchSource.Kuwo, 5 } };
			TrackSearchResult a = Track(SearchSource.Music163);
			TrackSearchResult b = Track(SearchSource.QQ);
			TrackSearchResult c = Track(SearchSource.Kuwo);
			List<TrackSearchResult> limited = CombinedTagSearchDialog.SelectResultsWithinSourceCaps(new List<TrackSearchResult> { a, b, c }, bySource, ref global);
			Check.Equal(3, limited.Count, "kept count");
			Check.True(ReferenceEquals(a, limited[0]), "order 0");
			Check.True(ReferenceEquals(b, limited[1]), "order 1");
			Check.True(ReferenceEquals(c, limited[2]), "order 2");
			Check.Equal(7, global, "global remaining");
		});

		// 8. 全局余额短路:global=0 时不索引 dict —— 缺失源也不抛 KeyNotFound。
		yield return ("SelectResultsWithinSourceCaps: global<=0 short-circuits dict lookup (no throw on missing source)", delegate
		{
			int global = 0;
			Dictionary<SearchSource, int> bySource = new Dictionary<SearchSource, int>();
			List<TrackSearchResult> limited = CombinedTagSearchDialog.SelectResultsWithinSourceCaps(new List<TrackSearchResult> { Track(SearchSource.Music163) }, bySource, ref global);
			Check.Equal(0, limited.Count, "nothing kept, no KeyNotFound thrown");
			Check.Equal(0, global, "global unchanged");
		});
	}
}
