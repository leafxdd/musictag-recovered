using System;
using System.Collections.Generic;
using System.Threading;
using MusicTag.Serialization;
using MusicTagWinApp.Containers;
using MusicTagWinApp.Roles;
using MusicTagWinApp.Writers;

namespace MusicTagWinApp.Web;

// QQ 的组合曲目搜索:3 趟(全名 15 / 仅标题 10 / 专辑+艺术家 8),趟间检查取消、趟2 gate 艺术家非空、
// 趟3 gate 专辑非空且 != 标题。QQ concrete SearchTracks 无 knownSongId,故忽略 useLinkedNetEaseId(与原 case 一致)。
// 逐字节搬自 SearchTracksFromSource 的 case SearchSource.QQ(concrete qqProvider.SearchTracks 调用一字未动)。
internal sealed class QqCombinedTrackSearch : ICombinedTrackSearch
{
	private readonly CancellationTokenSource cancellationSource;

	private readonly QqMusicTagProvider qqProvider;

	public QqCombinedTrackSearch(CancellationTokenSource cancellationSource, Action<SourceSearchStatus> statusReporter)
	{
		this.cancellationSource = cancellationSource;
		qqProvider = new QqMusicTagProvider(cancellationSource);
		qqProvider.StatusReporter = statusReporter;
	}

	public HttpResult LastTransportResult => qqProvider.LastTransportResult;

	public List<TrackSearchResult> SearchTracks(bool useLinkedNetEaseId, List<TrackSearchResult> existingResults, int searchPass, TrackSearchContext searchContext)
	{
		List<TrackSearchResult> results = new List<TrackSearchResult>();
		results.AddRange(qqProvider.SearchTracks((searchContext.Title + " " + searchContext.Artist).Trim(), 15, 0, searchPass, existingResults, results));
		if (cancellationSource.IsCancellationRequested)
		{
			return results;
		}
		if (!string.IsNullOrWhiteSpace(searchContext.Artist))
		{
			results.AddRange(qqProvider.SearchTracks(searchContext.Title.Trim(), 10, 1, searchPass, existingResults, results));
		}
		if (cancellationSource.IsCancellationRequested)
		{
			return results;
		}
		if (!string.IsNullOrWhiteSpace(searchContext.Album) && searchContext.Album != searchContext.Title)
		{
			results.AddRange(qqProvider.SearchTracks((searchContext.Album + " " + searchContext.Artist).Trim(), 8, 2, searchPass, existingResults, results));
		}
		return results;
	}

	public void Dispose()
	{
		qqProvider.Dispose();
	}
}
