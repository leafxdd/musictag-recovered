using System;
using System.Collections.Generic;
using MusicTagWinApp.Roles;

namespace MusicTag.Tests;

// TrackSearchResult.PromoteBestMatch 自动匹配「大脑」characterization(golden master)。
// PromoteBestMatch(targetTitle, targetArtist, targetAlbum, List<TrackSearchResult> results)
// 就地重排 results(MoveTrackToFront = Remove + Insert(0)),把最契合 target 的候选提到 results[0]。
// 纯逻辑:输入 3 个 string + 候选列表,输出 = 列表顺序;无 I/O / UI / 网络。
//
// fixture 关键:PromoteBestMatch 只【读】SimilarityScores[]、从不算分,故直接写
// track.SimilarityScores[0..2](Title/Artist/Album 三项分数)即可,绕过 TextSimilarityCalculator。
// 候选靠 SourceTrackId 标识,断言 results[0].SourceTrackId。
//
// 同时直接锁定两个 public static building block:
//   ContainsEitherWay(a,b) = a.Contains(b) || b.Contains(a) —— 空串陷阱:任一为 "" 多半 true;
//   IsInstrumentalTitle(t) = 含 instrumental/off vocal/伴奏/纯音乐,【大小写敏感】(不预先小写)。
// 注:PromoteBestMatch 内部经 NormalizeForMatch 已把文本小写化,故场景用例的关键词用小写。
internal static class PromoteBestMatchCharacterization
{
	// 构造候选。ResultOrder/SearchPass/SourceOrder 默认 0(= top-provider 首趟结果),
	// 覆盖无参构造的 -1(UnassignedSortOrder),使候选能进入 artistRanked/earlyPass 集合。
	private static TrackSearchResult Track(string id, string title, string artist, string album,
		double titleScore = 0.0, double artistScore = 0.0, double albumScore = 0.0,
		int resultOrder = 0, int searchPass = 0, int sourceOrder = 0, string originalTitle = null)
	{
		TrackSearchResult track = new TrackSearchResult
		{
			SourceTrackId = id,
			Title = title,
			Artist = artist,
			Album = album,
			OriginalTitle = originalTitle,
			ResultOrder = resultOrder,
			SearchPass = searchPass,
			SourceOrder = sourceOrder
		};
		track.SimilarityScores[0] = (float)titleScore;
		track.SimilarityScores[1] = (float)artistScore;
		track.SimilarityScores[2] = (float)albumScore;
		return track;
	}

	private static void CheckFront(string expectedId, List<TrackSearchResult> results, string label)
	{
		Check.Equal(expectedId, results[0].SourceTrackId, label);
	}

	public static IEnumerable<(string, Action)> All()
	{
		// ===== ContainsEitherWay:双向 Contains + 空串陷阱 =====

		yield return ("ContainsEitherWay: first contains second", delegate
		{
			Check.True(TrackSearchResult.ContainsEitherWay("abc", "b"), "abc⊇b");
		});

		yield return ("ContainsEitherWay: second contains first", delegate
		{
			Check.True(TrackSearchResult.ContainsEitherWay("b", "abc"), "b⊆abc");
		});

		yield return ("ContainsEitherWay: disjoint -> false", delegate
		{
			Check.True(!TrackSearchResult.ContainsEitherWay("abc", "xyz"), "abc vs xyz");
		});

		yield return ("ContainsEitherWay: identical -> true", delegate
		{
			Check.True(TrackSearchResult.ContainsEitherWay("abc", "abc"), "abc==abc");
		});

		// 空串陷阱:"abc".Contains("") == true(.NET 语义)
		yield return ("ContainsEitherWay: (\"abc\",\"\") -> true (Contains empty)", delegate
		{
			Check.True(TrackSearchResult.ContainsEitherWay("abc", ""), "abc vs empty");
		});

		// 反直觉:第一参为空 -> "".Contains("abc")=false,但回退 "abc".Contains("")=true -> 整体 true
		yield return ("ContainsEitherWay: (\"\",\"abc\") -> true (reverse Contains empty)", delegate
		{
			Check.True(TrackSearchResult.ContainsEitherWay("", "abc"), "empty vs abc");
		});

		yield return ("ContainsEitherWay: (\"\",\"\") -> true", delegate
		{
			Check.True(TrackSearchResult.ContainsEitherWay("", ""), "empty vs empty");
		});

		// ===== IsInstrumentalTitle:4 关键词 + 大小写敏感 =====

		yield return ("IsInstrumentalTitle: \"song instrumental\" -> true", delegate
		{
			Check.True(TrackSearchResult.IsInstrumentalTitle("song instrumental"), "instrumental");
		});

		yield return ("IsInstrumentalTitle: \"off vocal ver\" -> true", delegate
		{
			Check.True(TrackSearchResult.IsInstrumentalTitle("off vocal ver"), "off vocal");
		});

		yield return ("IsInstrumentalTitle: \"伴奏\" -> true", delegate
		{
			Check.True(TrackSearchResult.IsInstrumentalTitle("伴奏"), "伴奏");
		});

		yield return ("IsInstrumentalTitle: \"纯音乐\" -> true", delegate
		{
			Check.True(TrackSearchResult.IsInstrumentalTitle("纯音乐"), "纯音乐");
		});

		yield return ("IsInstrumentalTitle: \"normal song\" -> false", delegate
		{
			Check.True(!TrackSearchResult.IsInstrumentalTitle("normal song"), "normal");
		});

		yield return ("IsInstrumentalTitle: \"\" -> false", delegate
		{
			Check.True(!TrackSearchResult.IsInstrumentalTitle(""), "empty");
		});

		// 大小写敏感:大写 "INSTRUMENTAL" 不含小写子串 -> false
		yield return ("IsInstrumentalTitle: \"INSTRUMENTAL\" -> false (case-sensitive)", delegate
		{
			Check.True(!TrackSearchResult.IsInstrumentalTitle("INSTRUMENTAL"), "uppercase");
		});

		// ===== PromoteBestMatch:边界 =====

		// 空列表 -> 守卫直接 return,不抛、不变
		yield return ("PromoteBestMatch: empty list -> no-op", delegate
		{
			List<TrackSearchResult> results = new List<TrackSearchResult>();
			TrackSearchResult.PromoteBestMatch("title", "artist", "album", results);
			Check.Equal(0, results.Count, "empty stays empty");
		});

		// 单元素 -> 尾部 count<=1 return;首元素不变
		yield return ("PromoteBestMatch: single element -> unchanged", delegate
		{
			List<TrackSearchResult> results = new List<TrackSearchResult>
			{
				Track("T0", "x", "y", "z")
			};
			TrackSearchResult.PromoteBestMatch("x", "y", "z", results);
			CheckFront("T0", results, "single unchanged");
			Check.Equal(1, results.Count, "single count");
		});

		// ===== PromoteBestMatch:分支3(target 无艺术家)title-only 提升 =====

		// currentBest 标题弱匹配(score<0.5 且不互含)+ 另一候选标题匹配 -> 提升后者
		yield return ("PromoteBestMatch: no-artist branch promotes title match", delegate
		{
			List<TrackSearchResult> results = new List<TrackSearchResult>
			{
				Track("T0", "wrong", "", "", titleScore: 0.0),
				Track("T1", "target song", "", "")
			};
			TrackSearchResult.PromoteBestMatch("target song", "", "", results);
			CheckFront("T1", results, "no-artist title promote");
		});

		// 分支3 但 currentBest 标题已互含 target -> 不进入循环,不动
		yield return ("PromoteBestMatch: no-artist branch keeps already-matching best", delegate
		{
			List<TrackSearchResult> results = new List<TrackSearchResult>
			{
				Track("T0", "target", "", ""),
				Track("T1", "target", "", "")
			};
			TrackSearchResult.PromoteBestMatch("target", "", "", results);
			CheckFront("T0", results, "no-artist keep best");
		});

		// ===== PromoteBestMatch:分支2(currentBest 艺术家强匹配 >=0.8)=====

		// 标题+专辑+艺术家都已匹配、无需专辑替换 -> 不动
		yield return ("PromoteBestMatch: strong-artist best, no replacement -> unchanged", delegate
		{
			List<TrackSearchResult> results = new List<TrackSearchResult>
			{
				Track("T0", "song", "artist", "album", titleScore: 0.9, artistScore: 0.9, albumScore: 0.9),
				Track("T1", "other", "x", "y")
			};
			TrackSearchResult.PromoteBestMatch("song", "artist", "album", results);
			CheckFront("T0", results, "strong-artist unchanged");
		});

		// ===== PromoteBestMatch:分支1(currentBest 艺术家弱匹配 <0.8)的提升通道 =====

		// 通道一(252-267):artist-ranked 候选 标题∧专辑 双匹配 -> 提升
		yield return ("PromoteBestMatch: weak-artist best, artist-ranked album+title match promotes", delegate
		{
			List<TrackSearchResult> results = new List<TrackSearchResult>
			{
				Track("T0", "wrong", "a", "b", titleScore: 0.3, artistScore: 0.3, albumScore: 0.3),
				Track("T1", "target", "a", "album1", titleScore: 0.9, artistScore: 0.9, albumScore: 0.9)
			};
			TrackSearchResult.PromoteBestMatch("target", "a", "album1", results);
			CheckFront("T1", results, "artist-ranked album+title");
		});

		// 通道二(268-284):专辑不匹配但 artist 强候选 标题匹配 -> title-only 通道提升
		yield return ("PromoteBestMatch: weak-artist best, title-only pass promotes", delegate
		{
			List<TrackSearchResult> results = new List<TrackSearchResult>
			{
				Track("T0", "wrong", "a", "x", titleScore: 0.3, artistScore: 0.3, albumScore: 0.3),
				Track("T1", "target", "a", "diff", titleScore: 0.9, artistScore: 0.9, albumScore: 0.5)
			};
			TrackSearchResult.PromoteBestMatch("target", "a", "album1", results);
			CheckFront("T1", results, "title-only pass");
		});

		// 全不匹配(标题/艺术家/专辑都不互含、分数都 <0.8)-> 无候选可提升,currentBest 保持
		yield return ("PromoteBestMatch: weak-artist best, no candidate matches -> unchanged", delegate
		{
			List<TrackSearchResult> results = new List<TrackSearchResult>
			{
				Track("T0", "aaa", "bbb", "ccc", titleScore: 0.1, artistScore: 0.1, albumScore: 0.1),
				Track("T1", "ddd", "eee", "fff", titleScore: 0.1, artistScore: 0.1, albumScore: 0.1)
			};
			TrackSearchResult.PromoteBestMatch("zzz", "yyy", "xxx", results);
			CheckFront("T0", results, "no match unchanged");
		});

		// ===== PromoteBestMatch:伴奏尾部回退 =====

		// currentBest 是伴奏变体、target 非伴奏、存在非伴奏母版 -> 母版提到最前
		yield return ("PromoteBestMatch: instrumental best falls back to base track", delegate
		{
			List<TrackSearchResult> results = new List<TrackSearchResult>
			{
				Track("T0", "song instrumental", "a", "b", artistScore: 0.9),
				Track("T1", "song", "a", "b")
			};
			TrackSearchResult.PromoteBestMatch("song", "a", "b", results);
			CheckFront("T1", results, "instrumental fallback to base");
		});

		// target 本身也是伴奏 -> IsCandidateInstrumentalVariant 为 false,不回退,currentBest 保持
		yield return ("PromoteBestMatch: instrumental target -> no fallback", delegate
		{
			List<TrackSearchResult> results = new List<TrackSearchResult>
			{
				Track("T0", "song instrumental", "a", "b", artistScore: 0.9),
				Track("T1", "song", "a", "b")
			};
			TrackSearchResult.PromoteBestMatch("song instrumental", "a", "b", results);
			CheckFront("T0", results, "instrumental target no fallback");
		});

		// ===== 分支1/2 深层 fallback 通道(Workflow 8-agent 逐 pass 设计触发 fixture + 独立追踪复核 + probe-first build 锁定)=====
		// 每例精确触发一个更深的 fallback 通道:构造使前序通道全部不命中(promoted 保持 false)、
		// 仅目标通道的 MoveTrackToFront 执行。候选用 SearchPass/ResultOrder/分数/严格相等-vs-互含 等杠杆
		// 把控制流逐级推进。预期 front 全为 T1(从 currentBest=T0 提升,可观察变化锁定该通道)。

		// 通道三(285-303):SearchPass==2 候选,album/artist 与 target【严格相等】+ 标题互含。
		// T1.SearchPass=2 使其被排除出 artistRanked(236 要求 SearchPass<2),故通道一/二跳过它,由本通道捕获。
		yield return ("PromoteBestMatch: pass3 second-pass(SearchPass==2) album+artist exact match", delegate
		{
			List<TrackSearchResult> results = new List<TrackSearchResult>
			{
				Track("T0", "hello", "wrong", "19", titleScore: 0.3, artistScore: 0.3, albumScore: 0.3),
				Track("T1", "hello", "adele", "25", titleScore: 1.0, artistScore: 1.0, albumScore: 1.0, searchPass: 2)
			};
			TrackSearchResult.PromoteBestMatch("hello", "adele", "25", results);
			CheckFront("T1", results, "pass3 second-pass-album-fallback");
		});

		// 通道四(304-322):遍历全部 results 找 SearchPass<2 候选,album 互含 + artist【严格相等】+ 标题互含。
		// T1.ResultOrder=1 使其被排除出 artistRanked(230 跳过非 top 候选),故通道一/二/五(均遍历 artistRanked)
		// 不命中;通道三要 SearchPass==2 而 T1 是 1;本通道遍历 results 捕获 T1。
		yield return ("PromoteBestMatch: pass4 album-artist(SearchPass<2 scan all) exact-artist match", delegate
		{
			List<TrackSearchResult> results = new List<TrackSearchResult>
			{
				Track("T0", "love story", "karaoke version", "karaoke hits", titleScore: 0.9, artistScore: 0.2, albumScore: 0.1),
				Track("T1", "love story", "taylor swift", "fearless", titleScore: 0.95, artistScore: 1.0, albumScore: 0.95, resultOrder: 1, searchPass: 1, sourceOrder: 1)
			};
			TrackSearchResult.PromoteBestMatch("love story", "taylor swift", "fearless", results);
			CheckFront("T1", results, "pass4 album-artist-fallback");
		});

		// 通道五(323-341):遍历 artistRanked 找 artist【互含】(非严格)+ album 非空互含 + 标题互含。
		// T1.artist "alpha beta" 互含 target "alpha" 但不严格相等,故落出通道四(严格)、被本通道(互含)捕获。
		// (resultOrder=0 使 T1 进 artistRanked——这是 Workflow 设计中 fixture/trace 的不一致点,以 trace 为准修正。)
		yield return ("PromoteBestMatch: pass5 artist-album(loose-artist Contains) match", delegate
		{
			List<TrackSearchResult> results = new List<TrackSearchResult>
			{
				Track("T0", "wrong", "zzz", "other", artistScore: 0.3),
				Track("T1", "song", "alpha beta", "greatest hits", artistScore: 0.3, sourceOrder: 1)
			};
			TrackSearchResult.PromoteBestMatch("song", "alpha", "greatest", results);
			CheckFront("T1", results, "pass5 artist-album-fallback");
		});

		// 通道六(342-358):遍历 primaryCandidates 找 artist 互含 + 标题互含。
		// currentBest.Album="" 使通道三/四/五(均 gated by currentBestAlbum.Any())整块跳过,直达本通道。
		// currentBest.Artist="eason"(非空)避开 ContainsEitherWay 空串陷阱(否则 T0 自命中成 no-op)。
		yield return ("PromoteBestMatch: pass6 primary(blank currentBest album bypasses 3/4/5) artist+title", delegate
		{
			List<TrackSearchResult> results = new List<TrackSearchResult>
			{
				Track("T0", "alpha", "eason", ""),
				Track("T1", "beta", "jay", "", artistScore: 0.5, sourceOrder: 1)
			};
			TrackSearchResult.PromoteBestMatch("beta", "jay", "", results);
			CheckFront("T1", results, "pass6 primary-fallback");
		});

		// 通道七(359-365):artistRanked 排序后首位(artist 分最高)!=currentBest 且 artist 分>=0.8、
		// 而 currentBest 标题既不互含 target 又 titleScore<0.8 -> 提升 artistRanked[0]。
		yield return ("PromoteBestMatch: pass7 artist-ranked-best (top artist-score promoted)", delegate
		{
			List<TrackSearchResult> results = new List<TrackSearchResult>
			{
				Track("T0", "mmm", "ccc", "", titleScore: 0.1, artistScore: 0.3),
				Track("T1", "zzz", "bbb", "", artistScore: 0.9)
			};
			TrackSearchResult.PromoteBestMatch("ttt", "aaa", "", results);
			CheckFront("T1", results, "pass7 artist-ranked-best");
		});

		// 通道八(366-402):earlyPass 候选 标题+专辑双匹配 target 且不该保留 currentBest -> 提升。
		// currentBest 标题不匹配(currentBestAlreadyMatches=false)使 T0(earlyPass 首位、==currentBest)
		// 不触发 398 提前 break,T1 得以被处理。
		yield return ("PromoteBestMatch: pass8 early-pass album+title match", delegate
		{
			List<TrackSearchResult> results = new List<TrackSearchResult>
			{
				Track("T0", "diff", "zoe", ""),
				Track("T1", "song", "bob", "album", titleScore: 1.0, albumScore: 1.0)
			};
			TrackSearchResult.PromoteBestMatch("song", "alice", "album", results);
			CheckFront("T1", results, "pass8 early-pass-album-match");
		});

		// 通道九(404-429):earlyPass 候选 标题匹配 + artist 可接受 -> 提升。
		// T0.SearchPass=1 使 currentBest 不入 earlyPass(成员要 SearchPass<1),故 408 的 ==currentBest break
		// 永不触发、T1 可被处理(这是本通道唯一的可达构造——currentBest 必为 results[0])。
		yield return ("PromoteBestMatch: pass9 early-pass title match (currentBest excluded from earlyPass)", delegate
		{
			List<TrackSearchResult> results = new List<TrackSearchResult>
			{
				Track("T0", "omega", "zzz", "", searchPass: 1),
				Track("T1", "alpha", "bbb", "ealb", titleScore: 1.0, artistScore: 0.3)
			};
			TrackSearchResult.PromoteBestMatch("alpha", "aaa", "", results);
			CheckFront("T1", results, "pass9 early-pass-title-match");
		});

		// 分支2(438-459):currentBest artist 强匹配(>=0.8)但 album 错(albumScore<0.8 且不互含 target),
		// 另一候选同 title+artist 但 album 正确且 albumScore 更高 -> 替换提升。
		yield return ("PromoteBestMatch: branch2 album replacement (right album, higher score)", delegate
		{
			List<TrackSearchResult> results = new List<TrackSearchResult>
			{
				Track("T0", "song", "artist", "wrongalbum", titleScore: 0.9, artistScore: 0.9, albumScore: 0.3),
				Track("T1", "song", "artist", "rightalbum", titleScore: 0.9, artistScore: 0.9, albumScore: 0.9)
			};
			TrackSearchResult.PromoteBestMatch("song", "artist", "rightalbum", results);
			CheckFront("T1", results, "branch2-album-replacement");
		});
	}
}
