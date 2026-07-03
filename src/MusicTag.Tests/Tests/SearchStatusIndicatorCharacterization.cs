using System;
using System.Collections.Generic;
using MusicTagWinApp.Web;

namespace MusicTag.Tests;

// SearchStatusIndicator(MusicTagWinApp.Web,三个搜索弹窗共用的底部状态栏渲染器)的文案渲染 characterization。
// phase 的【计算】(Completed/Error/Retrying)已由 SourceOutcomeTracker/ReportSourceOutcome 测全,但
// phase->【文案】这一层此前零测。本批:
//   GetSourceDisplayName(already-testable):源 -> 硬编码中文名(有意区别于 Enum_* 资源,替换即改用户可见文案)。
//   BuildSearchingLine / BuildErrorOrRetryLine:private 实例方法提取为 internal static(收已按显示序的
//     源状态序列;实例方法委托 GetStatusesInDisplayOrder()。BuildErrorOrRetryLine 内部二次遍历,包装器
//     物化 List 传入,与原两次枚举 statuses 字典结果等价)。static 按【传入序列顺序】拼源名,显示序由
//     调用侧 GetStatusesInDisplayOrder 保证,此处按显示序传入。
internal static class SearchStatusIndicatorCharacterization
{
	private static SourceSearchStatus S(SearchSource source, SourceSearchPhase phase, string errorCode = null, int retrySecondsLeft = 0, int retryAttempt = 0, int retryTotal = 0)
	{
		return new SourceSearchStatus
		{
			Source = source,
			Phase = phase,
			ErrorCode = errorCode,
			RetrySecondsLeft = retrySecondsLeft,
			RetryAttempt = retryAttempt,
			RetryTotal = retryTotal
		};
	}

	private static List<SourceSearchStatus> Seq(params SourceSearchStatus[] items)
	{
		return new List<SourceSearchStatus>(items);
	}

	public static IEnumerable<(string, Action)> All()
	{
		// ===== GetSourceDisplayName:源 -> 硬编码中文名,未知源 -> ToString =====

		yield return ("GetSourceDisplayName: Music163 -> 网易云", delegate
		{
			Check.Equal("网易云", SearchStatusIndicator.GetSourceDisplayName(SearchSource.Music163), "Music163");
		});

		yield return ("GetSourceDisplayName: QQ -> QQ", delegate
		{
			Check.Equal("QQ", SearchStatusIndicator.GetSourceDisplayName(SearchSource.QQ), "QQ");
		});

		yield return ("GetSourceDisplayName: Kugou -> 酷狗", delegate
		{
			Check.Equal("酷狗", SearchStatusIndicator.GetSourceDisplayName(SearchSource.Kugou), "Kugou");
		});

		yield return ("GetSourceDisplayName: Kuwo -> 酷我", delegate
		{
			Check.Equal("酷我", SearchStatusIndicator.GetSourceDisplayName(SearchSource.Kuwo), "Kuwo");
		});

		yield return ("GetSourceDisplayName: unknown -> ToString", delegate
		{
			Check.Equal("999", SearchStatusIndicator.GetSourceDisplayName((SearchSource)999), "unknown source ToString");
		});

		// ===== BuildSearchingLine:Searching/Pending 源拼 "正在搜索: A/B";无则 null =====

		yield return ("BuildSearchingLine: two searching -> 正在搜索: 网易云/QQ", delegate
		{
			string line = SearchStatusIndicator.BuildSearchingLine(Seq(S(SearchSource.Music163, SourceSearchPhase.Searching), S(SearchSource.QQ, SourceSearchPhase.Searching)));
			Check.Equal("正在搜索: 网易云/QQ", line, "two searching");
		});

		yield return ("BuildSearchingLine: pending counts as searching", delegate
		{
			string line = SearchStatusIndicator.BuildSearchingLine(Seq(S(SearchSource.Kugou, SourceSearchPhase.Pending)));
			Check.Equal("正在搜索: 酷狗", line, "pending included");
		});

		yield return ("BuildSearchingLine: all completed -> null", delegate
		{
			string line = SearchStatusIndicator.BuildSearchingLine(Seq(S(SearchSource.Music163, SourceSearchPhase.Completed), S(SearchSource.QQ, SourceSearchPhase.Completed)));
			Check.Null(line, "no active source");
		});

		yield return ("BuildSearchingLine: error/retrying excluded from searching line", delegate
		{
			string line = SearchStatusIndicator.BuildSearchingLine(Seq(S(SearchSource.Music163, SourceSearchPhase.Error, "500"), S(SearchSource.QQ, SourceSearchPhase.Searching)));
			Check.Equal("正在搜索: QQ", line, "only QQ searching");
		});

		yield return ("BuildSearchingLine: empty -> null", delegate
		{
			Check.Null(SearchStatusIndicator.BuildSearchingLine(Seq()), "empty");
		});

		// ===== BuildErrorOrRetryLine:Retrying 优先;单错误带码;多错误合并丢码;无则 null =====

		yield return ("BuildErrorOrRetryLine: empty -> null", delegate
		{
			Check.Null(SearchStatusIndicator.BuildErrorOrRetryLine(Seq()), "empty");
		});

		yield return ("BuildErrorOrRetryLine: all completed -> null", delegate
		{
			Check.Null(SearchStatusIndicator.BuildErrorOrRetryLine(Seq(S(SearchSource.QQ, SourceSearchPhase.Completed))), "no error");
		});

		yield return ("BuildErrorOrRetryLine: single error with code", delegate
		{
			string line = SearchStatusIndicator.BuildErrorOrRetryLine(Seq(S(SearchSource.QQ, SourceSearchPhase.Error, "404")));
			Check.Equal("QQ API错误(404)", line, "single error");
		});

		yield return ("BuildErrorOrRetryLine: single error null code -> 未知", delegate
		{
			string line = SearchStatusIndicator.BuildErrorOrRetryLine(Seq(S(SearchSource.QQ, SourceSearchPhase.Error, null)));
			Check.Equal("QQ API错误(未知)", line, "null code -> 未知");
		});

		yield return ("BuildErrorOrRetryLine: multiple errors merge names, drop codes", delegate
		{
			string line = SearchStatusIndicator.BuildErrorOrRetryLine(Seq(S(SearchSource.Music163, SourceSearchPhase.Error, "500"), S(SearchSource.QQ, SourceSearchPhase.Error, "404")));
			Check.Equal("网易云/QQ API错误", line, "merged names, no code");
		});

		yield return ("BuildErrorOrRetryLine: retrying line with code/seconds/attempt", delegate
		{
			string line = SearchStatusIndicator.BuildErrorOrRetryLine(Seq(S(SearchSource.QQ, SourceSearchPhase.Retrying, "2001", 5, 1, 3)));
			Check.Equal("QQ API错误(2001), 5 秒后重试 (1/3)", line, "retrying line");
		});

		yield return ("BuildErrorOrRetryLine: retrying takes priority over error", delegate
		{
			string line = SearchStatusIndicator.BuildErrorOrRetryLine(Seq(S(SearchSource.Music163, SourceSearchPhase.Error, "500"), S(SearchSource.QQ, SourceSearchPhase.Retrying, "2001", 2, 1, 5)));
			Check.Equal("QQ API错误(2001), 2 秒后重试 (1/5)", line, "retrying priority");
		});

		yield return ("BuildErrorOrRetryLine: negative RetrySecondsLeft clamped to 0", delegate
		{
			string line = SearchStatusIndicator.BuildErrorOrRetryLine(Seq(S(SearchSource.QQ, SourceSearchPhase.Retrying, "2001", -1, 1, 3)));
			Check.Equal("QQ API错误(2001), 0 秒后重试 (1/3)", line, "Math.Max clamps");
		});
	}
}
