using System.Collections.Generic;
using MusicTagWinApp.Adapter;

namespace MusicTagWinApp.Web;

// 歌词搜索能力。签名取网易云超集(含 knownSongId 与 existingLyrics)。
// QQ / 酷狗 / 酷我经显式接口实现转发,丢弃其 concrete 不接收的 knownSongId / existingLyrics。
internal interface ILyricSearchProvider : IRemoteSearchProvider
{
	List<LyricSearchResult> SearchLyrics(string query, int resultLimit, long knownSongId, List<LyricSearchResult> existingLyrics, int sourceOrder);
}
