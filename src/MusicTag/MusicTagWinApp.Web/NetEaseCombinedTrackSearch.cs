using System;
using System.Collections.Generic;
using System.Threading;
using MusicTag.Serialization;
using MusicTagWinApp.Containers;
using MusicTagWinApp.Exporters;
using MusicTagWinApp.Roles;

namespace MusicTagWinApp.Web;

// 网易云的组合曲目搜索:linked(已知 musicId)时单趟;否则 3 趟(全名 15 / 仅标题 10 / 专辑+艺术家 8),
// 趟间检查取消、趟2 gate 艺术家非空、趟3 gate 专辑非空且 != 标题。
// 逐字节搬自 SearchTracksFromSource 的 case SearchSource.Music163(concrete netEaseProvider.SearchTracks 调用一字未动,
// 原 case 内的 break 在此对应 return results)。provider 由本实现持有,Dispose() 转发(原 case 内 using 声明的释放)。
internal sealed class NetEaseCombinedTrackSearch : ICombinedTrackSearch
{
	private readonly CancellationTokenSource cancellationSource;

	private readonly NetEaseMusicTagProvider netEaseProvider;

	public NetEaseCombinedTrackSearch(CancellationTokenSource cancellationSource, Action<SourceSearchStatus> statusReporter)
	{
		this.cancellationSource = cancellationSource;
		netEaseProvider = new NetEaseMusicTagProvider(cancellationSource);
		netEaseProvider.StatusReporter = statusReporter;
	}

	public HttpResult LastTransportResult => netEaseProvider.LastTransportResult;

	public List<TrackSearchResult> SearchTracks(bool useLinkedNetEaseId, List<TrackSearchResult> existingResults, int searchPass, TrackSearchContext searchContext)
	{
		List<TrackSearchResult> results = new List<TrackSearchResult>();
		if (useLinkedNetEaseId)
		{
			results.AddRange(netEaseProvider.SearchTracks("", 0, searchContext.LinkedMusicMetadata.musicId, 0, searchPass, existingResults, results));
			return results;
		}
		results.AddRange(netEaseProvider.SearchTracks((searchContext.Title + " " + searchContext.Artist).Trim(), 15, 0L, 0, searchPass, existingResults, results));
		if (cancellationSource.IsCancellationRequested)
		{
			return results;
		}
		if (!string.IsNullOrWhiteSpace(searchContext.Artist))
		{
			results.AddRange(netEaseProvider.SearchTracks(searchContext.Title.Trim(), 10, 0L, 1, searchPass, existingResults, results));
		}
		if (cancellationSource.IsCancellationRequested)
		{
			return results;
		}
		if (!string.IsNullOrWhiteSpace(searchContext.Album) && searchContext.Album != searchContext.Title)
		{
			results.AddRange(netEaseProvider.SearchTracks((searchContext.Album + " " + searchContext.Artist).Trim(), 8, 0L, 2, searchPass, existingResults, results));
		}
		return results;
	}

	public void Dispose()
	{
		netEaseProvider.Dispose();
	}
}
