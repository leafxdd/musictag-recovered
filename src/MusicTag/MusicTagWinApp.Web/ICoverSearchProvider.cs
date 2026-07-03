using System.Collections.Generic;
using MusicTagWinApp.Listeners;

namespace MusicTagWinApp.Web;

// 封面搜索能力。网易云 / QQ / 酷我实现;酷狗无封面接口,故不实现(工厂亦不为酷狗构造封面 provider)。
internal interface ICoverSearchProvider : IRemoteSearchProvider
{
	List<CoverSearchResult> SearchCovers(string query, int maxResults, List<CoverSearchResult> existingCovers);
}
