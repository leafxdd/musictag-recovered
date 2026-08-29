using System;
using System.Collections.Generic;
using System.Net.Http;
using MusicTag.Serialization;
using MusicTagWinApp.Adapter;
using MusicTagWinApp.Exporters;
using MusicTagWinApp.Listeners;
using MusicTagWinApp.Web;
using MusicTagWinApp.Writers;

namespace MusicTag.Tests;

// 三源封面搜索 characterization：SearchCovers 端到端驱动各 provider 真实解析链
//（搜索 JSON → 字段映射 → CoverUrl 构造 → 基类 BuildDedupedCovers 去重），锁定 golden master。
// 注入点同 SearchTracks：NetEase/QQ override PostString、Kuwo override GetResponseString，喂录制搜索 JSON。
// 封面下载是延迟闭包（CoverDownloader），characterization 不触发，故无需 mock 图片字节。
// Kugou 无封面（不实现 ICoverSearchProvider），不在此列。
// 共享去重（空 CoverUrl 跳过 / 重复 CoverUrl 去重 / existingCovers 已有则跳过）由基类 BuildDedupedCovers
// 实现，用 NetEase 代表性覆盖一次（QQ/Kuwo 只验各自 CoverUrl 映射 + ParseFailed）。
internal static class ProviderCoverCharacterization
{
	private sealed class StubNetEase : NetEaseMusicTagProvider
	{
		private readonly string response;
		public StubNetEase(string response) : base(null) { this.response = response; }
		protected override string PostString(string url, string body, HttpClient client = null, bool postJson = false) => response;
	}

	private sealed class StubQq : QqMusicTagProvider
	{
		private readonly string response;
		public StubQq(string response) : base(null) { this.response = response; }
		protected override string PostString(string url, string body, HttpClient client = null, bool postJson = false) => response;
		protected override bool UseSharedRequestCoordination => false;
	}

	private sealed class StubKuwo : KuwoTagProvider
	{
		private readonly string response;
		public StubKuwo(string response) : base(null) { this.response = response; }
		protected override string GetResponseString(string url) => response;
	}

	public static IEnumerable<(string, Action)> All()
	{
		// ---- NetEase：CoverUrl = al.picUrl，并代表性覆盖 BuildDedupedCovers 去重三分支 ----
		yield return ("NetEase.SearchCovers maps al.picUrl -> CoverUrl", delegate
		{
			string resp = "{\"result\":{\"songs\":[{\"id\":111,\"name\":\"A\",\"ar\":[{\"id\":1,\"name\":\"X\"}],\"al\":{\"id\":10,\"name\":\"AlbumA\",\"picUrl\":\"http://p/a.jpg\"}}]}}";
			List<CoverSearchResult> covers = new StubNetEase(resp).SearchCovers("q", 10, new List<CoverSearchResult>());
			Check.Equal(1, covers.Count, "count");
			Check.Equal("http://p/a.jpg", covers[0].CoverUrl, "[0].CoverUrl");
			Check.Equal(SearchSource.Music163, covers[0].SearchSource, "[0].SearchSource");
		});

		yield return ("NetEase.SearchCovers dedups identical picUrl", delegate
		{
			string resp = "{\"result\":{\"songs\":[" +
				"{\"id\":111,\"name\":\"A\",\"ar\":[{\"id\":1,\"name\":\"X\"}],\"al\":{\"id\":10,\"name\":\"AlbumA\",\"picUrl\":\"http://p/same.jpg\"}}," +
				"{\"id\":222,\"name\":\"B\",\"ar\":[{\"id\":2,\"name\":\"Y\"}],\"al\":{\"id\":20,\"name\":\"AlbumB\",\"picUrl\":\"http://p/same.jpg\"}}]}}";
			List<CoverSearchResult> covers = new StubNetEase(resp).SearchCovers("q", 10, new List<CoverSearchResult>());
			Check.Equal(1, covers.Count, "count");
		});

		yield return ("NetEase.SearchCovers blank picUrl skipped", delegate
		{
			string resp = "{\"result\":{\"songs\":[{\"id\":111,\"name\":\"A\",\"ar\":[{\"id\":1,\"name\":\"X\"}],\"al\":{\"id\":10,\"name\":\"AlbumA\"}}]}}";
			List<CoverSearchResult> covers = new StubNetEase(resp).SearchCovers("q", 10, new List<CoverSearchResult>());
			Check.Equal(0, covers.Count, "count");
		});

		yield return ("NetEase.SearchCovers existing CoverUrl skipped", delegate
		{
			string resp = "{\"result\":{\"songs\":[{\"id\":111,\"name\":\"A\",\"ar\":[{\"id\":1,\"name\":\"X\"}],\"al\":{\"id\":10,\"name\":\"AlbumA\",\"picUrl\":\"http://p/exists.jpg\"}}]}}";
			List<CoverSearchResult> existing = new List<CoverSearchResult> { new CoverSearchResult { CoverUrl = "http://p/exists.jpg" } };
			List<CoverSearchResult> covers = new StubNetEase(resp).SearchCovers("q", 10, existing);
			Check.Equal(0, covers.Count, "count");
		});

		yield return ("NetEase.SearchCovers HTTP-200 unparseable -> ParseFailed", delegate
		{
			StubNetEase provider = new StubNetEase("<html>not json</html>");
			List<CoverSearchResult> covers = provider.SearchCovers("q", 10, new List<CoverSearchResult>());
			Check.Equal(0, covers.Count, "count");
			Check.NotNull(provider.LastTransportResult, "LastTransportResult");
			Check.Equal(RemoteErrorKind.ParseFailed, provider.LastTransportResult.Error, "Error");
		});

		// ---- QQ：CoverUrl = albumCoverUrlTemplate(album.mid) ----
		yield return ("QQ.SearchCovers maps album.mid -> photo URL", delegate
		{
			string resp = "{\"req_0\":{\"code\":0,\"data\":{\"body\":{\"song\":{\"list\":[" +
				"{\"id\":555,\"mid\":\"M555\",\"name\":\"NameA\",\"title\":\"TitleA\",\"singer\":[{\"name\":\"SingerA\"}],\"album\":{\"id\":99,\"mid\":\"ALB99\",\"name\":\"AlbumA\"}}" +
				"]}}}}}";
			List<CoverSearchResult> covers = new StubQq(resp).SearchCovers("q", 10, new List<CoverSearchResult>());
			Check.Equal(1, covers.Count, "count");
			Check.Equal("https://y.qq.com/music/photo_new/T002R800x800M000ALB99.jpg", covers[0].CoverUrl, "[0].CoverUrl");
			Check.Equal(SearchSource.QQ, covers[0].SearchSource, "[0].SearchSource");
		});

		yield return ("QQ.SearchCovers HTTP-200 unparseable -> ParseFailed", delegate
		{
			StubQq provider = new StubQq("<html>not json</html>");
			List<CoverSearchResult> covers = provider.SearchCovers("q", 10, new List<CoverSearchResult>());
			Check.Equal(0, covers.Count, "count");
			Check.NotNull(provider.LastTransportResult, "LastTransportResult");
			Check.Equal(RemoteErrorKind.ParseFailed, provider.LastTransportResult.Error, "Error");
		});

		// ---- Kuwo：CoverUrl = web_albumpic_short 拼 500/ 高清直链；缺失则回退详情 API URL ----
		yield return ("Kuwo.SearchCovers maps web_albumpic_short -> 500/ album cover", delegate
		{
			string resp = "{\"abslist\":[{\"SONGNAME\":\"SongA\",\"ARTIST\":\"ArtistA\",\"ALBUM\":\"AlbumA\",\"MUSICRID\":\"MUSIC_12345\",\"web_albumpic_short\":\"120/s3/93/x.jpg\"}]}";
			List<CoverSearchResult> covers = new StubKuwo(resp).SearchCovers("q", 10, new List<CoverSearchResult>());
			Check.Equal(1, covers.Count, "count");
			Check.Equal("https://img2.kuwo.cn/star/albumcover/500/s3/93/x.jpg", covers[0].CoverUrl, "[0].CoverUrl");
			Check.Equal(SearchSource.Kuwo, covers[0].SearchSource, "[0].SearchSource");
		});

		yield return ("Kuwo.SearchCovers no album pic -> fallback detail URL", delegate
		{
			string resp = "{\"abslist\":[{\"SONGNAME\":\"SongB\",\"ARTIST\":\"ArtistB\",\"ALBUM\":\"AlbumB\",\"MUSICRID\":\"MUSIC_999\"}]}";
			List<CoverSearchResult> covers = new StubKuwo(resp).SearchCovers("q", 10, new List<CoverSearchResult>());
			Check.Equal(1, covers.Count, "count");
			Check.Equal("https://m.kuwo.cn/newh5/singles/songinfoandlrc?musicId=999", covers[0].CoverUrl, "[0].CoverUrl");
		});

		yield return ("Kuwo.SearchCovers HTTP-200 unparseable -> ParseFailed", delegate
		{
			StubKuwo provider = new StubKuwo("<html>not json</html>");
			List<CoverSearchResult> covers = provider.SearchCovers("q", 10, new List<CoverSearchResult>());
			Check.Equal(0, covers.Count, "count");
			Check.NotNull(provider.LastTransportResult, "LastTransportResult");
			Check.Equal(RemoteErrorKind.ParseFailed, provider.LastTransportResult.Error, "Error");
		});
	}
}
