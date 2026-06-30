using System;
using System.Collections.Generic;
using MusicTag.Serialization;
using MusicTagWinApp.Containers;
using MusicTagWinApp.Roles;

namespace MusicTagWinApp.Web;

// 粗粒度「组合曲目搜索」能力:封装 CombinedTagSearchDialog.SearchTracksFromSource 里【按源的多趟查询编排】
// (网易云 linked 单趟 / 非 linked 3 趟、QQ 3 趟、酷我 1-2 趟,含趟间取消检查与趟2/3 条件)。
// 每源一个实现把原 switch case 体逐字节搬入(concrete provider 调用一字未动 -> characterization 恒绿);
// 工厂 SearchProviderFactory.CreateCombinedTrackSearch 按 SearchSource 造实例并注入 StatusReporter,
// 未知源(含酷狗,原 SearchTracksFromSource 无酷狗 case)返回 null(= 原 default)。
// LastTransportResult 暴露给 dialog 末尾 ReportSourceOutcome(原读已 Dispose 的 provider 同名属性,值不变;
// 此处 provider 由实现持有、随 using 在方法结束才 Dispose,读取时机更早但值一致)。
internal interface ICombinedTrackSearch : IDisposable
{
	HttpResult LastTransportResult { get; }

	List<TrackSearchResult> SearchTracks(bool useLinkedNetEaseId, List<TrackSearchResult> existingResults, int searchPass, TrackSearchContext searchContext);
}
