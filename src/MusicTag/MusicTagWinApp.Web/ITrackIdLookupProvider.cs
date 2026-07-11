using MusicTagWinApp.Roles;

namespace MusicTagWinApp.Web;

// 精确曲目查询能力。各平台的公开主键形态不同(数字 ID、songmid、hash)，故使用字符串契约。
internal interface ITrackIdLookupProvider : IRemoteSearchProvider
{
	TrackSearchResult LookupTrackById(string trackId, int sourceOrder);
}
