using MusicTagWinApp.Adapter;
using MusicTagWinApp.Roles;

namespace MusicTagWinApp.Web;

// 为已选中的候选曲目按其来源加载歌词。四源均实现;酷我 concrete 为单数名 LoadLyricForTrack,经显式接口实现转发。
internal interface ITrackLyricLoader : IRemoteSearchProvider
{
	LyricSearchResult LoadLyricsForTrack(TrackSearchResult track);
}
