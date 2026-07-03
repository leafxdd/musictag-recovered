namespace MusicTagWinApp.Web;

// 各源单次搜索的结果上限的单一来源(原先散落在曲目 / 歌词 / 封面 dispatch 的字面量):
// 网易云 / QQ = 15,酷狗 / 酷我 = 5。未知源不可达(工厂对未知源先返回 null),保守回落 5。
internal static class SearchProviderPolicy
{
	public static int ResultLimit(SearchSource source)
	{
		switch (source)
		{
		case SearchSource.Music163:
		case SearchSource.QQ:
			return 15;
		case SearchSource.Kugou:
		case SearchSource.Kuwo:
			return 5;
		default:
			return 5;
		}
	}
}
