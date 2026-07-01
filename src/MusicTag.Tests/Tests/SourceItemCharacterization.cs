using System;
using System.Collections.Generic;
using MusicTagWinApp.Web;

namespace MusicTag.Tests;

// SourceItem(MusicTagWinApp.Web,持久化源配置模型,JSON 键 Src/Seq/Enabled/IsOther/WebSearchItemsLimit)
// 的纯逻辑 characterization。三个 already-testable public 方法(零源码改动),锁定 golden master:
// 每源单次结果上限判定 + 持久化配置恢复/规整 + 按序排序 —— 直接决定联网搜索每源返回条数与遍历顺序(用户可见)。
//   GetEffectiveSearchResultLimit:1..99 原样;<=0 或 >=100 -> int.MaxValue(不限)。反直觉边界。
//   GetSortedBySequence:复制入参按 Sequence 升序,不原地改。
//   ApplySavedSourceSettings:反序列化保存的 List<SourceItem>,按 SearchSource(JSON 键 Src 显式 ordinal)
//     匹配合并 Enabled/Sequence/SearchResultLimit;然后对副本按 (IsSecondarySource 靠后, Sequence) 排序,
//     再把每个 source.Sequence 重写为 0..n。韧性:malformed JSON 被吞、已删源 Src 匹配不到即忽略。
internal static class SourceItemCharacterization
{
	private static SourceItem Src(SearchSource source, int sequence, bool enabled = true, bool isSecondary = false, int limit = 100)
	{
		return new SourceItem(source, sequence, enabled, isSecondary, limit);
	}

	public static IEnumerable<(string, Action)> All()
	{
		// ===== GetEffectiveSearchResultLimit:1..99 原样,否则 int.MaxValue =====

		yield return ("GetEffectiveSearchResultLimit: 50 -> 50", delegate
		{
			Check.Equal(50, Src(SearchSource.QQ, 0, true, false, 50).GetEffectiveSearchResultLimit(), "50 within range");
		});

		yield return ("GetEffectiveSearchResultLimit: 99 -> 99 (upper inclusive)", delegate
		{
			Check.Equal(99, Src(SearchSource.QQ, 0, true, false, 99).GetEffectiveSearchResultLimit(), "99 upper bound");
		});

		yield return ("GetEffectiveSearchResultLimit: 100 -> int.MaxValue (over bound = unlimited)", delegate
		{
			Check.Equal(int.MaxValue, Src(SearchSource.QQ, 0, true, false, 100).GetEffectiveSearchResultLimit(), "100 unlimited");
		});

		yield return ("GetEffectiveSearchResultLimit: 1 -> 1 (lower bound)", delegate
		{
			Check.Equal(1, Src(SearchSource.QQ, 0, true, false, 1).GetEffectiveSearchResultLimit(), "1 lower");
		});

		yield return ("GetEffectiveSearchResultLimit: 0 -> int.MaxValue", delegate
		{
			Check.Equal(int.MaxValue, Src(SearchSource.QQ, 0, true, false, 0).GetEffectiveSearchResultLimit(), "0 unlimited");
		});

		yield return ("GetEffectiveSearchResultLimit: -1 -> int.MaxValue", delegate
		{
			Check.Equal(int.MaxValue, Src(SearchSource.QQ, 0, true, false, -1).GetEffectiveSearchResultLimit(), "-1 unlimited");
		});

		// ===== GetSortedBySequence:复制 + Sequence 升序,不原地 =====

		yield return ("GetSortedBySequence: [2,0,1] -> [0,1,2]", delegate
		{
			List<SourceItem> input = new List<SourceItem> { Src(SearchSource.QQ, 2), Src(SearchSource.Music163, 0), Src(SearchSource.Kugou, 1) };
			List<SourceItem> sorted = SourceItem.GetSortedBySequence(input);
			Check.Equal(0, sorted[0].Sequence, "first");
			Check.Equal(1, sorted[1].Sequence, "second");
			Check.Equal(2, sorted[2].Sequence, "third");
		});

		yield return ("GetSortedBySequence: does not mutate input order (returns copy)", delegate
		{
			SourceItem a = Src(SearchSource.QQ, 2);
			SourceItem b = Src(SearchSource.Music163, 0);
			List<SourceItem> input = new List<SourceItem> { a, b };
			SourceItem.GetSortedBySequence(input);
			Check.True(ReferenceEquals(input[0], a) && ReferenceEquals(input[1], b), "input order preserved");
		});

		yield return ("GetSortedBySequence: empty -> empty", delegate
		{
			Check.Equal(0, SourceItem.GetSortedBySequence(new List<SourceItem>()).Count, "empty");
		});

		yield return ("GetSortedBySequence: single -> unchanged", delegate
		{
			List<SourceItem> sorted = SourceItem.GetSortedBySequence(new List<SourceItem> { Src(SearchSource.Kuwo, 7) });
			Check.Equal(1, sorted.Count, "count");
			Check.Equal(SearchSource.Kuwo, sorted[0].SearchSource, "same item");
		});

		// ===== ApplySavedSourceSettings:合并 + 排序(secondary 靠后)+ Sequence 重写 0..n =====

		// json 空白 -> 不合并;按 (IsSecondarySource, Sequence) 排序后 Sequence 重写 0..n。
		yield return ("ApplySavedSourceSettings: null json -> reorder by (secondary,seq) + resequence 0..n", delegate
		{
			SourceItem a = Src(SearchSource.Music163, 0, true, false);   // primary seq0
			SourceItem b = Src(SearchSource.QQ, 2, true, false);         // primary seq2
			SourceItem c = Src(SearchSource.Kugou, 1, true, true);       // secondary seq1
			List<SourceItem> list = new List<SourceItem> { a, b, c };
			SourceItem.ApplySavedSourceSettings(null, list);
			// 排序序:a(primary,0) < b(primary,2) < c(secondary) -> resequence 0,1,2
			Check.Equal(0, a.Sequence, "primary seq0 -> 0");
			Check.Equal(1, b.Sequence, "primary seq2 -> 1");
			Check.Equal(2, c.Sequence, "secondary -> last (2)");
		});

		// 次源无论保存 Seq 多小都排主源之后
		yield return ("ApplySavedSourceSettings: secondary always after primary regardless of seq", delegate
		{
			SourceItem primary = Src(SearchSource.Music163, 5, true, false);
			SourceItem secondary = Src(SearchSource.QQ, 0, true, true);   // small seq but secondary
			List<SourceItem> list = new List<SourceItem> { secondary, primary };
			SourceItem.ApplySavedSourceSettings("", list);
			Check.Equal(0, primary.Sequence, "primary first");
			Check.Equal(1, secondary.Sequence, "secondary last despite smaller original seq");
		});

		// 手写 JSON:Src=1 必须匹配 QQ(锁 SearchSource 显式 ordinal 持久化契约)
		yield return ("ApplySavedSourceSettings: raw JSON Src=1 matches QQ (ordinal contract)", delegate
		{
			string json = "[{\"Src\":1,\"Seq\":0,\"Enabled\":false,\"IsOther\":false,\"WebSearchItemsLimit\":7}]";
			SourceItem qq = Src(SearchSource.QQ, 3, true, false, 100);
			List<SourceItem> list = new List<SourceItem> { qq };
			SourceItem.ApplySavedSourceSettings(json, list);
			Check.True(!qq.Enabled, "Src=1 matched QQ -> Enabled=false");
			Check.Equal(7, qq.SearchResultLimit, "Src=1 matched QQ -> limit=7");
		});

		// 已删源 Src=5 匹配不到 -> 忽略,不抛
		yield return ("ApplySavedSourceSettings: retired Src=5 ignored, no throw", delegate
		{
			string json = "[{\"Src\":5,\"Seq\":0,\"Enabled\":true,\"IsOther\":false,\"WebSearchItemsLimit\":9}]";
			SourceItem qq = Src(SearchSource.QQ, 0, true, false, 100);
			List<SourceItem> list = new List<SourceItem> { qq };
			SourceItem.ApplySavedSourceSettings(json, list);
			Check.Equal(100, qq.SearchResultLimit, "unmatched retired source left QQ untouched");
		});

		// malformed json -> catch 吞掉,不抛,仍排序重写
		yield return ("ApplySavedSourceSettings: malformed json -> no throw, still resequences", delegate
		{
			SourceItem a = Src(SearchSource.Music163, 3, true, false);
			List<SourceItem> list = new List<SourceItem> { a };
			bool threw = false;
			try
			{
				SourceItem.ApplySavedSourceSettings("{bad", list);
			}
			catch (Exception)
			{
				threw = true;
			}
			Check.True(!threw, "malformed json swallowed");
			Check.Equal(0, a.Sequence, "still resequenced to 0");
		});
	}
}
