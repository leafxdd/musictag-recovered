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
// 搬自 SearchTracksFromSource 的 case SearchSource.QQ；统一接口转发后 concrete provider 的参数映射保持不变。
internal sealed class QqCombinedTrackSearch : ICombinedTrackSearch
{
	private readonly CancellationTokenSource cancellationSource;

	private readonly ITrackSearchProvider qqProvider;

	public QqCombinedTrackSearch(CancellationTokenSource cancellationSource, Action<SourceSearchStatus> statusReporter)
	{
		this.cancellationSource = cancellationSource;
		QqMusicTagProvider provider = new QqMusicTagProvider(cancellationSource);
		provider.StatusReporter = statusReporter;
		qqProvider = provider;
	}

	internal QqCombinedTrackSearch(CancellationTokenSource cancellationSource, ITrackSearchProvider qqProvider)
	{
		this.cancellationSource = cancellationSource;
		this.qqProvider = qqProvider;
	}

	public HttpResult LastTransportResult => qqProvider.LastTransportResult;

	public List<TrackSearchResult> SearchTracks(bool useLinkedNetEaseId, List<TrackSearchResult> existingResults, int searchPass, TrackSearchContext searchContext)
	{
		List<TrackSearchResult> results = new List<TrackSearchResult>();
		results.AddRange(qqProvider.SearchTracks((searchContext.Title + " " + searchContext.Artist).Trim(), 15, 0L, 0, searchPass, existingResults, results));
		if (ShouldStopSearch())
		{
			return results;
		}
		if (!string.IsNullOrWhiteSpace(searchContext.Artist))
		{
			results.AddRange(qqProvider.SearchTracks(searchContext.Title.Trim(), 10, 0L, 1, searchPass, existingResults, results));
		}
		if (ShouldStopSearch())
		{
			return results;
		}
		if (!string.IsNullOrWhiteSpace(searchContext.Album) && searchContext.Album != searchContext.Title)
		{
			results.AddRange(qqProvider.SearchTracks((searchContext.Album + " " + searchContext.Artist).Trim(), 8, 0L, 2, searchPass, existingResults, results));
		}
		return results;
	}

	private bool ShouldStopSearch()
	{
		return cancellationSource.IsCancellationRequested || qqProvider.LastTransportResult?.Error == RemoteErrorKind.RateLimited;
	}

	public void Dispose()
	{
		qqProvider.Dispose();
	}
}
