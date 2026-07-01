using System;
using System.Collections.Generic;
using MusicTagWinApp.Web;

namespace MusicTag.Tests;

// SearchProviderPolicy.ResultLimit(MusicTagWinApp.Web):每源单次搜索结果上限的单一来源
// (B2 收敛后集中,原散落在曲目/歌词/封面 dispatch 的字面量)。网易云/QQ=15,酷狗/酷我=5,未知源回落 5。
// 回归会改各源实际返回条数(用户可见)。public static 纯 switch,直接断言。
internal static class SearchProviderPolicyCharacterization
{
	public static IEnumerable<(string, Action)> All()
	{
		yield return ("ResultLimit: Music163 -> 15", delegate
		{
			Check.Equal(15, SearchProviderPolicy.ResultLimit(SearchSource.Music163), "Music163");
		});

		yield return ("ResultLimit: QQ -> 15", delegate
		{
			Check.Equal(15, SearchProviderPolicy.ResultLimit(SearchSource.QQ), "QQ");
		});

		yield return ("ResultLimit: Kugou -> 5", delegate
		{
			Check.Equal(5, SearchProviderPolicy.ResultLimit(SearchSource.Kugou), "Kugou");
		});

		yield return ("ResultLimit: Kuwo -> 5", delegate
		{
			Check.Equal(5, SearchProviderPolicy.ResultLimit(SearchSource.Kuwo), "Kuwo");
		});

		yield return ("ResultLimit: unknown source -> 5 (default fallback)", delegate
		{
			Check.Equal(5, SearchProviderPolicy.ResultLimit((SearchSource)999), "unknown default");
		});
	}
}
