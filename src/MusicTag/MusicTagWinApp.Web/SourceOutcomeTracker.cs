using System;
using System.Collections.Generic;
using MusicTag.Serialization;

namespace MusicTagWinApp.Web;

// 各联网搜索源在一轮合并搜索中的成败统计与最终状态归约,封面 / 歌词搜索弹窗共用,
// 确保 "某源任一次搜索有结果即视为完成;仅当始终 0 结果且末次传输出错时上报出错"
// 的规则一致。详见 docs/SEARCH_STATUS_INDICATOR_DESIGN.md。
//
// 线程约定:Record 由后台搜索线程串行写入;BuildFinalStatus/ReportFinal 由调用方在
// await 之后的 UI 线程读取(封面则在后台线程经 reporter 编组上报)。内部无锁,沿用
// 原 dialog/worker 字段的访问时序约定。
internal sealed class SourceOutcomeTracker
{
	private readonly HashSet<SearchSource> completedSources = new HashSet<SearchSource>();

	private readonly Dictionary<SearchSource, HttpResult> errorBySource = new Dictionary<SearchSource, HttpResult>();

	// 记录本源一次搜索的结果:有结果即视为完成;0 结果且传输出错则记录错误。
	public void Record(SearchSource source, bool hadResults, HttpResult lastTransportResult)
	{
		if (hadResults)
		{
			completedSources.Add(source);
		}
		else if (lastTransportResult != null && !lastTransportResult.IsSuccess)
		{
			errorBySource[source] = lastTransportResult;
		}
	}

	// 清空上一轮统计(供 dialog 级复用实例在新一轮搜索前调用)。
	public void Clear()
	{
		completedSources.Clear();
		errorBySource.Clear();
	}

	// 本源最终状态:始终无结果且有末次传输错误 -> Error(带错误码);否则 Completed。
	public SourceSearchStatus BuildFinalStatus(SearchSource source)
	{
		if (!completedSources.Contains(source) && errorBySource.TryGetValue(source, out HttpResult error) && error != null && !error.IsSuccess)
		{
			return new SourceSearchStatus
			{
				Source = source,
				Phase = SourceSearchPhase.Error,
				ErrorCode = error.ErrorCode
			};
		}
		return new SourceSearchStatus
		{
			Source = source,
			Phase = SourceSearchPhase.Completed
		};
	}

	// 按给定源序列逐一上报最终状态。序列由调用方提供(与各弹窗 "已勾选源" 遍历口径一致),
	// 顺序保持不变;reporter 即各弹窗的状态上报通道(封面经 searchStatusReporter 编组,
	// 歌词直达 searchStatusIndicator.Report)。
	public void ReportFinal(IEnumerable<SearchSource> sources, Action<SourceSearchStatus> reporter)
	{
		foreach (SearchSource source in sources)
		{
			reporter(BuildFinalStatus(source));
		}
	}
}
