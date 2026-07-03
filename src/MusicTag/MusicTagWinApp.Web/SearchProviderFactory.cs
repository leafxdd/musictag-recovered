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
	// 四源全量 provider 构造 + StatusReporter 注入:track/lyric/lyric-loader 三个能力工厂的公共核
	// (原为三份逐字节相同的 switch;四个 provider 均实现这三个能力接口,由原各 case 的隐式接口
	// 转换在编译期担保,故包装处的显式 cast 恒安全,null 经 cast 仍为 null)。未知源返回 null。
	private static RemoteTagProviderBase CreateProvider(SearchSource source, CancellationTokenSource cancellation, Action<SourceSearchStatus> statusReporter)
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

	public static ITrackSearchProvider CreateTrackSearch(SearchSource source, CancellationTokenSource cancellation, Action<SourceSearchStatus> statusReporter = null)
	{
		return (ITrackSearchProvider)CreateProvider(source, cancellation, statusReporter);
	}

	public static ILyricSearchProvider CreateLyricSearch(SearchSource source, CancellationTokenSource cancellation, Action<SourceSearchStatus> statusReporter = null)
	{
		return (ILyricSearchProvider)CreateProvider(source, cancellation, statusReporter);
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
		return (ITrackLyricLoader)CreateProvider(source, cancellation, statusReporter);
	}

	// 组合曲目搜索(多趟编排)工厂:仅 3 源 —— 网易云/QQ/酷我,镜像原 SearchTracksFromSource 的 switch(无酷狗 case)。
	// 各实现内部 new provider 并注入 StatusReporter(保原"构造后即设"时序);未知源(含酷狗 + 退役源)返回 null = 原 default。
	public static ICombinedTrackSearch CreateCombinedTrackSearch(SearchSource source, CancellationTokenSource cancellation, Action<SourceSearchStatus> statusReporter = null)
	{
		switch (source)
		{
		case SearchSource.Music163:
			return new NetEaseCombinedTrackSearch(cancellation, statusReporter);
		case SearchSource.QQ:
			return new QqCombinedTrackSearch(cancellation, statusReporter);
		case SearchSource.Kuwo:
			return new KuwoCombinedTrackSearch(cancellation, statusReporter);
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
