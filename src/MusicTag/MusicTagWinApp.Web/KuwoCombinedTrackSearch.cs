using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MusicTag.Serialization;
using MusicTagWinApp.Adapter;
using MusicTagWinApp.Containers;
using MusicTagWinApp.Roles;

namespace MusicTagWinApp.Web;

// 酷我的组合曲目搜索:首趟全名(8);仅当首趟【无结果】且专辑非空且 != 标题才追加第二趟(专辑+艺术家,8)。
// 趟间检查取消。酷我 concrete SearchTracks 无 knownSongId,故忽略 useLinkedNetEaseId(与原 case 一致)。
// 逐字节搬自 SearchTracksFromSource 的 case SearchSource.Kuwo(concrete kuwoTagProvider.SearchTracks 调用一字未动)。
internal sealed class KuwoCombinedTrackSearch : ICombinedTrackSearch
{
	private readonly CancellationTokenSource cancellationSource;

	private readonly KuwoTagProvider kuwoTagProvider;

	public KuwoCombinedTrackSearch(CancellationTokenSource cancellationSource, Action<SourceSearchStatus> statusReporter)
	{
		this.cancellationSource = cancellationSource;
		kuwoTagProvider = new KuwoTagProvider(cancellationSource);
		kuwoTagProvider.StatusReporter = statusReporter;
	}

	public HttpResult LastTransportResult => kuwoTagProvider.LastTransportResult;

	public List<TrackSearchResult> SearchTracks(bool useLinkedNetEaseId, List<TrackSearchResult> existingResults, int searchPass, TrackSearchContext searchContext)
	{
		List<TrackSearchResult> results = new List<TrackSearchResult>();
		results.AddRange(kuwoTagProvider.SearchTracks((searchContext.Title + " " + searchContext.Artist).Trim(), 8, 0, searchPass, existingResults, results));
		if (cancellationSource.IsCancellationRequested)
		{
			return results;
		}
		if (!results.Any() && !string.IsNullOrWhiteSpace(searchContext.Album) && searchContext.Album != searchContext.Title)
		{
			results.AddRange(kuwoTagProvider.SearchTracks((searchContext.Album + " " + searchContext.Artist).Trim(), 8, 1, searchPass, existingResults, results));
		}
		return results;
	}

	public void Dispose()
	{
		kuwoTagProvider.Dispose();
	}
}
