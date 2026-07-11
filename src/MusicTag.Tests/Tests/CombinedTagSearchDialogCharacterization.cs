using System;
using System.Collections.Generic;
using System.Globalization;
using MusicTag.Mocks;
using MusicTag.Serialization;
using MusicTagWinApp.Roles;
using MusicTagWinApp.Web;

namespace MusicTag.Tests;

// CombinedTagSearchDialog(联网标签搜索弹窗,code-health 最差的 god-form)里三簇纯逻辑的
// characterization(golden master)。这些方法此前【无任何测试覆盖】(untested hotspot,正是
// health 评分低的根因),本批在 Phase 2 提取/可见性提升后补齐回归网,锁定其现有行为。
//
// 三簇:
//   ① ShouldAcceptFilenameFallbackMatch —— 本批从 RankSearchResults 60 行方法里提取的纯谓词:
//      文件名 "艺术家 - 标题" 拆分后的回退排序结果是否足够可信而采用。双阈值(0.5 宽松双确认 /
//      0.8 强分数)直接决定用户可见的候选排序,敏感,逐边界锁定。
//   ② ReportSourceOutcome —— 单次(per 源 per 趟)的"完成/出错"判定:有结果即完成(即便末次
//      传输错误),仅 0 结果且末次传输错误才出错。
//   ③ SourceOutcomeTracker —— 封面/歌词弹窗共用的【跨趟聚合】成败统计:某源任一趟有结果即终态
//      Completed(记忆"曾有结果"),仅始终 0 结果且末次传输错误才 Error。
//
//   ②与③刻意语义不同:Tag 弹窗各源【并行单趟】,故用无状态单次判定;封面/歌词弹窗各源【串行
//   多趟】(album/artist、title/artist、二级源…),故用记忆型聚合。二者各自正确,非矛盾。
//
// fixture 关键(同 PromoteBestMatchCharacterization):TrackSearchResult 只【读】SimilarityScores[],
// 故直接写 SimilarityScores[0]=Title 分 / [1]=Artist 分即可,绕过 TextSimilarityCalculator。
// HttpResult.IsSuccess => Error==None;FromHttpStatus(code) 产出 Error=HttpStatus、ErrorCode=code、
// !IsSuccess。ContainsEitherWay(a,b)=a.Contains(b)||b.Contains(a)(空串陷阱:任一为 "" 多半 true)。
internal static class CombinedTagSearchDialogCharacterization
{
	// 构造回退候选:只设标题/艺术家文本与标题/艺术家相似度分(谓词只读这四项 + ContainsEitherWay)。
	private static TrackSearchResult Fallback(string title, string artist, double titleScore, double artistScore)
	{
		TrackSearchResult track = new TrackSearchResult
		{
			Title = title,
			Artist = artist
		};
		track.SimilarityScores[0] = (float)titleScore;
		track.SimilarityScores[1] = (float)artistScore;
		return track;
	}

	// 注入捕获通道驱动 ReportSourceOutcome(无返回值,经 reporter 回调产出 SourceSearchStatus)。
	private static SourceSearchStatus ReportOutcome(List<TrackSearchResult> results, HttpResult transport)
	{
		SourceSearchStatus captured = null;
		CombinedTagSearchDialog.ReportSourceOutcome(status => captured = status, SearchSource.QQ, results, transport);
		return captured;
	}

	private static List<TrackSearchResult> Results(int count)
	{
		List<TrackSearchResult> results = new List<TrackSearchResult>();
		for (int i = 0; i < count; i++)
		{
			results.Add(new TrackSearchResult());
		}
		return results;
	}

	public static IEnumerable<(string, Action)> All()
	{
		yield return ("TrackIdLookup UI text is localized per provider", delegate
		{
			CultureInfo english = CultureInfo.GetCultureInfo("en-US");
			CultureInfo simplifiedChinese = CultureInfo.GetCultureInfo("zh-CN");
			Check.Equal("songmid, songid, or song link", CombinedTagSearchDialog.GetTrackIdInputHint(SearchSource.QQ, english), "QQ English hint");
			Check.Equal("musicId 或歌曲链接", CombinedTagSearchDialog.GetTrackIdInputHint(SearchSource.Kuwo, simplifiedChinese), "Kuwo Chinese hint");
			Check.Equal("MixSongID、hash 或歌曲链接", CombinedTagSearchDialog.GetTrackIdInputHint(SearchSource.Kugou, simplifiedChinese), "Kugou Chinese hint");
			Check.Equal("Request rate limited (2001)", CombinedTagSearchDialog.GetTrackIdLookupFailureText(new HttpResult { Error = RemoteErrorKind.RateLimited, ErrorCode = "2001" }, english), "rate limit text");
			Check.Equal("未找到该歌曲", CombinedTagSearchDialog.GetTrackIdLookupFailureText(null, simplifiedChinese), "not found text");
		});

		// ===== ① ShouldAcceptFilenameFallbackMatch:双阈值接受谓词 =====
		// 谓词 = (CEW(标题) && titleScore>=0.5 && CEW(艺术家) && artistScore>=0.5)   // 通道一:宽松双确认
		//      || (titleScore>=0.8 && artistScore>=0.8)                              // 通道二:强分数

		// 通道一全真,0.5 下边界(0.5 可精确表示,>= 包含)
		yield return ("FilenameFallback: lenient both-confirm at 0.5 boundary -> accept", delegate
		{
			TrackSearchResult best = Fallback("hello", "adele", 0.5, 0.5);
			Check.True(CombinedTagSearchDialog.ShouldAcceptFilenameFallbackMatch("hello", "adele", best), "0.5/0.5 both contain");
		});

		// 通道一与通道二皆满足(happy path,短路于通道一)
		yield return ("FilenameFallback: both channels satisfied -> accept", delegate
		{
			TrackSearchResult best = Fallback("hello", "adele", 0.9, 0.9);
			Check.True(CombinedTagSearchDialog.ShouldAcceptFilenameFallbackMatch("hello", "adele", best), "0.9/0.9 contain");
		});

		// 通道一 titleScore=0.49 跌出 + 通道二不满足 -> 拒绝(锁定 title 0.5 下边界)
		yield return ("FilenameFallback: title score 0.49 below 0.5, strong fails -> reject", delegate
		{
			TrackSearchResult best = Fallback("hello", "adele", 0.49, 0.9);
			Check.True(!CombinedTagSearchDialog.ShouldAcceptFilenameFallbackMatch("hello", "adele", best), "title 0.49 rejects");
		});

		// 通道一 artistScore=0.49 跌出 + 通道二不满足(artist 0.49<0.8)-> 拒绝(锁定 artist 0.5 下边界)
		yield return ("FilenameFallback: artist score 0.49 below 0.5, strong fails -> reject", delegate
		{
			TrackSearchResult best = Fallback("hello", "adele", 0.9, 0.49);
			Check.True(!CombinedTagSearchDialog.ShouldAcceptFilenameFallbackMatch("hello", "adele", best), "artist 0.49 rejects");
		});

		// 通道一 标题不互含跌出 + 通道二分数不足 -> 拒绝
		yield return ("FilenameFallback: title not contained, weak score -> reject", delegate
		{
			TrackSearchResult best = Fallback("hello", "adele", 0.7, 0.7);
			Check.True(!CombinedTagSearchDialog.ShouldAcceptFilenameFallbackMatch("xyz", "adele", best), "title disjoint rejects");
		});

		// 通道一 艺术家不互含跌出 + 通道二分数不足 -> 拒绝
		yield return ("FilenameFallback: artist not contained, weak score -> reject", delegate
		{
			TrackSearchResult best = Fallback("hello", "adele", 0.7, 0.7);
			Check.True(!CombinedTagSearchDialog.ShouldAcceptFilenameFallbackMatch("hello", "xyz", best), "artist disjoint rejects");
		});

		// 通道二:标题/艺术家均不互含,但分数各 >=0.8(强分数无需互含)-> 采用。
		// 0.8 经 float->double 提升后略大于 double 字面量 0.8,故 0.8 分满足 >=0.8(锁定边界含浮点语义)。
		yield return ("FilenameFallback: strong-score 0.8 both, no contains -> accept", delegate
		{
			TrackSearchResult best = Fallback("hello", "adele", 0.8, 0.8);
			Check.True(CombinedTagSearchDialog.ShouldAcceptFilenameFallbackMatch("xyz", "qqq", best), "0.8/0.8 strong accepts");
		});

		// 通道二 titleScore=0.79 跌出 + 通道一不互含 -> 拒绝(锁定 title 0.8 下边界)
		yield return ("FilenameFallback: strong title 0.79 below 0.8, no contains -> reject", delegate
		{
			TrackSearchResult best = Fallback("hello", "adele", 0.79, 0.9);
			Check.True(!CombinedTagSearchDialog.ShouldAcceptFilenameFallbackMatch("xyz", "qqq", best), "strong title 0.79 rejects");
		});

		// 通道二 artistScore=0.79 跌出 + 通道一不互含 -> 拒绝(锁定 artist 0.8 下边界)
		yield return ("FilenameFallback: strong artist 0.79 below 0.8, no contains -> reject", delegate
		{
			TrackSearchResult best = Fallback("hello", "adele", 0.9, 0.79);
			Check.True(!CombinedTagSearchDialog.ShouldAcceptFilenameFallbackMatch("xyz", "qqq", best), "strong artist 0.79 rejects");
		});

		// .ToLower() 使互含大小写不敏感(若不小写 "HELLO".Contains("hello")=false)
		yield return ("FilenameFallback: case-insensitive contains via ToLower -> accept", delegate
		{
			TrackSearchResult best = Fallback("hello", "adele", 0.5, 0.5);
			Check.True(CombinedTagSearchDialog.ShouldAcceptFilenameFallbackMatch("HELLO", "ADELE", best), "uppercase filename still matches");
		});

		// ContainsEitherWay 空串陷阱:空 filename 使互含恒真(谓词纯行为;上游 RankSearchResults 已
		// IsNullOrWhiteSpace gate 保证非空白,此为防御性边界锁定)。
		yield return ("FilenameFallback: empty filename parts -> contains-empty makes lenient true", delegate
		{
			TrackSearchResult best = Fallback("hello", "adele", 0.5, 0.5);
			Check.True(CombinedTagSearchDialog.ShouldAcceptFilenameFallbackMatch("", "", best), "empty filename contains-trap accepts");
		});

		// ===== ② ReportSourceOutcome:单次(per 源 per 趟)完成/出错判定 =====

		// 0 结果 + 末次传输错误 -> 出错,错误码与源透传
		yield return ("ReportSourceOutcome: zero results + transport error -> Error with code", delegate
		{
			SourceSearchStatus status = ReportOutcome(Results(0), HttpResult.FromHttpStatus(404));
			Check.NotNull(status, "status reported");
			Check.Equal(SourceSearchPhase.Error, status.Phase, "phase Error");
			Check.Equal("404", status.ErrorCode, "error code 404");
			Check.Equal(SearchSource.QQ, status.Source, "source QQ");
		});

		// 有结果 -> 完成(即便末次传输错误;Count>0 短路第一条件)
		yield return ("ReportSourceOutcome: has results despite transport error -> Completed", delegate
		{
			SourceSearchStatus status = ReportOutcome(Results(1), HttpResult.FromHttpStatus(500));
			Check.Equal(SourceSearchPhase.Completed, status.Phase, "phase Completed");
			Check.Null(status.ErrorCode, "completed carries no error code");
		});

		// 0 结果 + transport=null -> 完成(第二条件 null 短路)
		yield return ("ReportSourceOutcome: zero results + null transport -> Completed", delegate
		{
			SourceSearchStatus status = ReportOutcome(Results(0), null);
			Check.Equal(SourceSearchPhase.Completed, status.Phase, "null transport completes");
		});

		// 0 结果 + 传输成功(IsSuccess=true)-> 完成(搜到 0 条不算错)
		yield return ("ReportSourceOutcome: zero results + successful transport -> Completed", delegate
		{
			SourceSearchStatus status = ReportOutcome(Results(0), new HttpResult());
			Check.Equal(SourceSearchPhase.Completed, status.Phase, "zero-but-success completes");
		});

		// Error 分支业务码透传(如 QQ 限流 2001,非 HTTP 状态)
		yield return ("ReportSourceOutcome: business error code (rate limit) passthrough", delegate
		{
			HttpResult transport = new HttpResult
			{
				Error = RemoteErrorKind.RateLimited,
				ErrorCode = "2001"
			};
			SourceSearchStatus status = ReportOutcome(Results(0), transport);
			Check.Equal(SourceSearchPhase.Error, status.Phase, "phase Error");
			Check.Equal("2001", status.ErrorCode, "business code 2001");
		});

		// Completed 分支也透传 Source(对抗验证完备性补:此前仅 Error 分支断言了 Source 透传)
		yield return ("ReportSourceOutcome: Completed branch passes source through", delegate
		{
			SourceSearchStatus status = ReportOutcome(Results(1), null);
			Check.Equal(SourceSearchPhase.Completed, status.Phase, "phase Completed");
			Check.Equal(SearchSource.QQ, status.Source, "completed source passthrough");
		});

		// ===== ③ SourceOutcomeTracker:跨趟聚合成败统计(封面/歌词共用) =====

		// 从未 Record 的源 -> 完成
		yield return ("SourceOutcomeTracker: never recorded -> Completed", delegate
		{
			SourceOutcomeTracker tracker = new SourceOutcomeTracker();
			Check.Equal(SourceSearchPhase.Completed, tracker.BuildFinalStatus(SearchSource.QQ).Phase, "unrecorded completes");
		});

		// Record(有结果) -> 完成
		yield return ("SourceOutcomeTracker: recorded with results -> Completed", delegate
		{
			SourceOutcomeTracker tracker = new SourceOutcomeTracker();
			tracker.Record(SearchSource.QQ, hadResults: true, null);
			Check.Equal(SourceSearchPhase.Completed, tracker.BuildFinalStatus(SearchSource.QQ).Phase, "had-results completes");
		});

		// Record(0 结果 + 传输错误) -> 出错 + 错误码
		yield return ("SourceOutcomeTracker: zero results + transport error -> Error with code", delegate
		{
			SourceOutcomeTracker tracker = new SourceOutcomeTracker();
			tracker.Record(SearchSource.QQ, hadResults: false, HttpResult.FromHttpStatus(404));
			SourceSearchStatus status = tracker.BuildFinalStatus(SearchSource.QQ);
			Check.Equal(SourceSearchPhase.Error, status.Phase, "phase Error");
			Check.Equal("404", status.ErrorCode, "error code 404");
		});

		// 聚合:先有结果后出错 -> 仍完成(记忆"曾有结果",与 ReportSourceOutcome 单次语义的关键区别)
		yield return ("SourceOutcomeTracker: results then error -> Completed (remembers success)", delegate
		{
			SourceOutcomeTracker tracker = new SourceOutcomeTracker();
			tracker.Record(SearchSource.QQ, hadResults: true, null);
			tracker.Record(SearchSource.QQ, hadResults: false, HttpResult.FromHttpStatus(500));
			Check.Equal(SourceSearchPhase.Completed, tracker.BuildFinalStatus(SearchSource.QQ).Phase, "success remembered across passes");
		});

		// 聚合:先出错后有结果 -> 完成(顺序无关,有结果终态胜出)
		yield return ("SourceOutcomeTracker: error then results -> Completed (order-independent)", delegate
		{
			SourceOutcomeTracker tracker = new SourceOutcomeTracker();
			tracker.Record(SearchSource.QQ, hadResults: false, HttpResult.FromHttpStatus(500));
			tracker.Record(SearchSource.QQ, hadResults: true, null);
			Check.Equal(SourceSearchPhase.Completed, tracker.BuildFinalStatus(SearchSource.QQ).Phase, "later success wins");
		});

		// Record(0 结果 + transport=null,如取消)-> 完成(无传输错误不记)
		yield return ("SourceOutcomeTracker: zero results + null transport -> Completed", delegate
		{
			SourceOutcomeTracker tracker = new SourceOutcomeTracker();
			tracker.Record(SearchSource.QQ, hadResults: false, null);
			Check.Equal(SourceSearchPhase.Completed, tracker.BuildFinalStatus(SearchSource.QQ).Phase, "null transport not an error");
		});

		// Record(0 结果 + 传输成功)-> 完成(IsSuccess=true,else-if 不记 error)
		yield return ("SourceOutcomeTracker: zero results + successful transport -> Completed", delegate
		{
			SourceOutcomeTracker tracker = new SourceOutcomeTracker();
			tracker.Record(SearchSource.QQ, hadResults: false, new HttpResult());
			Check.Equal(SourceSearchPhase.Completed, tracker.BuildFinalStatus(SearchSource.QQ).Phase, "success transport not an error");
		});

		// Clear 重置已记录的错误
		yield return ("SourceOutcomeTracker: Clear resets recorded error -> Completed", delegate
		{
			SourceOutcomeTracker tracker = new SourceOutcomeTracker();
			tracker.Record(SearchSource.QQ, hadResults: false, HttpResult.FromHttpStatus(404));
			tracker.Clear();
			Check.Equal(SourceSearchPhase.Completed, tracker.BuildFinalStatus(SearchSource.QQ).Phase, "cleared error completes");
		});

		// Clear 也清除"曾有结果"记忆:复用实例上一轮成功、本轮失败必须浮现 Error(对抗验证完备性补;
		// 锁定 Clear 同时清 completedSources,防未来回退漏清致 stale success 掩盖新一轮失败)。
		yield return ("SourceOutcomeTracker: Clear forgets prior-round success so new-round failure surfaces", delegate
		{
			SourceOutcomeTracker tracker = new SourceOutcomeTracker();
			tracker.Record(SearchSource.QQ, hadResults: true, null);
			tracker.Clear();
			tracker.Record(SearchSource.QQ, hadResults: false, HttpResult.FromHttpStatus(404));
			SourceSearchStatus status = tracker.BuildFinalStatus(SearchSource.QQ);
			Check.Equal(SourceSearchPhase.Error, status.Phase, "post-Clear new failure surfaces");
			Check.Equal("404", status.ErrorCode, "post-Clear error code surfaces");
		});

		// 多源独立:一源出错不影响另一源
		yield return ("SourceOutcomeTracker: per-source isolation", delegate
		{
			SourceOutcomeTracker tracker = new SourceOutcomeTracker();
			tracker.Record(SearchSource.QQ, hadResults: false, HttpResult.FromHttpStatus(404));
			Check.Equal(SourceSearchPhase.Completed, tracker.BuildFinalStatus(SearchSource.Music163).Phase, "other source unaffected");
		});

		// 业务码透传(限流 2001)
		yield return ("SourceOutcomeTracker: business error code passthrough", delegate
		{
			SourceOutcomeTracker tracker = new SourceOutcomeTracker();
			tracker.Record(SearchSource.QQ, hadResults: false, new HttpResult
			{
				Error = RemoteErrorKind.RateLimited,
				ErrorCode = "2001"
			});
			Check.Equal("2001", tracker.BuildFinalStatus(SearchSource.QQ).ErrorCode, "business code 2001");
		});

		// 同源多趟错误:indexer 覆盖语义 = 末次传输错误胜出 + Record 重复调用不抛(对抗验证完备性补;
		// 直接锁定韧性不变量:单源多次失败既不 abort 也不 keep-first,防未来重构成 Dictionary.Add 抛 ArgumentException)。
		yield return ("SourceOutcomeTracker: repeated errors -> last transport wins, no throw", delegate
		{
			SourceOutcomeTracker tracker = new SourceOutcomeTracker();
			tracker.Record(SearchSource.QQ, hadResults: false, HttpResult.FromHttpStatus(404));
			tracker.Record(SearchSource.QQ, hadResults: false, HttpResult.FromHttpStatus(500));
			Check.Equal("500", tracker.BuildFinalStatus(SearchSource.QQ).ErrorCode, "last error 500 wins");
		});

		// ReportFinal:按给定源序列逐一上报,顺序保持,逐源状态正确,未 Record 的源为完成
		yield return ("SourceOutcomeTracker: ReportFinal preserves order and per-source status", delegate
		{
			SourceOutcomeTracker tracker = new SourceOutcomeTracker();
			tracker.Record(SearchSource.QQ, hadResults: true, null);
			tracker.Record(SearchSource.Music163, hadResults: false, HttpResult.FromHttpStatus(404));
			List<SourceSearchStatus> captured = new List<SourceSearchStatus>();
			tracker.ReportFinal(new[] { SearchSource.Music163, SearchSource.QQ, SearchSource.Kuwo }, status => captured.Add(status));
			Check.Equal(3, captured.Count, "three reported");
			Check.Equal(SearchSource.Music163, captured[0].Source, "[0] Music163");
			Check.Equal(SourceSearchPhase.Error, captured[0].Phase, "[0] Error");
			Check.Equal(SearchSource.QQ, captured[1].Source, "[1] QQ");
			Check.Equal(SourceSearchPhase.Completed, captured[1].Phase, "[1] Completed");
			Check.Equal(SearchSource.Kuwo, captured[2].Source, "[2] Kuwo");
			Check.Equal(SourceSearchPhase.Completed, captured[2].Phase, "[2] unrecorded Completed");
		});
	}
}
