using System;
using System.Threading;
using MusicTag.Candidates;
using MusicTag.Serialization;
using MusicTagWinApp.Adapter;
using MusicTagWinApp.Exporters;
using MusicTagWinApp.Writers;

namespace MusicTagWinApp.Web;

// 按 SearchSource 构造对应能力的 provider,并在返回前注入 StatusReporter(保持原 dispatch"构造后即设"的时序)。
// 未知源返回 null —— 对应原各 switch 的 default(连带忽略已退役源序数 5/6/7/8)。
// 封面工厂仅 3 源(无酷狗),镜像原封面 dispatch 的 default,与"酷狗不实现 ICoverSearchProvider"双重保证无封面。
internal static class SearchProviderFactory
{
	public static ITrackSearchProvider CreateTrackSearch(SearchSource source, CancellationTokenSource cancellation, Action<SourceSearchStatus> statusReporter = null)
	{
		switch (source)
		{
		case SearchSource.Music163:
			return Configure(new NetEaseMusicTagProvider(cancellation), statusReporter);
		case SearchSource.QQ:
			return Configure(new QqMusicTagProvider(cancellation), statusReporter);
		case SearchSource.Kugou:
			return Configure(new KugouTagProvider(cancellation), statusReporter);
		case SearchSource.Kuwo:
			return Configure(new KuwoTagProvider(cancellation), statusReporter);
		default:
			return null;
		}
	}

	public static ILyricSearchProvider CreateLyricSearch(SearchSource source, CancellationTokenSource cancellation, Action<SourceSearchStatus> statusReporter = null)
	{
		switch (source)
		{
		case SearchSource.Music163:
			return Configure(new NetEaseMusicTagProvider(cancellation), statusReporter);
		case SearchSource.QQ:
			return Configure(new QqMusicTagProvider(cancellation), statusReporter);
		case SearchSource.Kugou:
			return Configure(new KugouTagProvider(cancellation), statusReporter);
		case SearchSource.Kuwo:
			return Configure(new KuwoTagProvider(cancellation), statusReporter);
		default:
			return null;
		}
	}

	public static ICoverSearchProvider CreateCoverSearch(SearchSource source, CancellationTokenSource cancellation, Action<SourceSearchStatus> statusReporter = null)
	{
		switch (source)
		{
		case SearchSource.Music163:
			return Configure(new NetEaseMusicTagProvider(cancellation), statusReporter);
		case SearchSource.QQ:
			return Configure(new QqMusicTagProvider(cancellation), statusReporter);
		case SearchSource.Kuwo:
			return Configure(new KuwoTagProvider(cancellation), statusReporter);
		default:
			return null;
		}
	}

	public static ITrackLyricLoader CreateLyricLoader(SearchSource source, CancellationTokenSource cancellation, Action<SourceSearchStatus> statusReporter = null)
	{
		switch (source)
		{
		case SearchSource.Music163:
			return Configure(new NetEaseMusicTagProvider(cancellation), statusReporter);
		case SearchSource.QQ:
			return Configure(new QqMusicTagProvider(cancellation), statusReporter);
		case SearchSource.Kugou:
			return Configure(new KugouTagProvider(cancellation), statusReporter);
		case SearchSource.Kuwo:
			return Configure(new KuwoTagProvider(cancellation), statusReporter);
		default:
			return null;
		}
	}

	// 注入 StatusReporter 后原样返回;泛型保留具体 provider 类型,由调用方隐式转换到所需能力接口。
	private static T Configure<T>(T provider, Action<SourceSearchStatus> statusReporter) where T : RemoteTagProviderBase
	{
		provider.StatusReporter = statusReporter;
		return provider;
	}
}
