using System.Collections.Generic;
using MusicTagWinApp.Roles;

namespace MusicTagWinApp.Web;

// 曲目搜索能力。签名取各源超集(网易云最全:含 knownSongId)。
// QQ / 酷狗 / 酷我经显式接口实现转发,丢弃其 concrete 不接收的 knownSongId。
internal interface ITrackSearchProvider : IRemoteSearchProvider
{
	List<TrackSearchResult> SearchTracks(string query, int resultLimit, long knownSongId, int searchPass, int sourceOrder, List<TrackSearchResult> existingTracks, List<TrackSearchResult> previousResults);
}
