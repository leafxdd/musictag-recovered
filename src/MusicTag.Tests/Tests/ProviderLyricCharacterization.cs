using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using MusicTag.Serialization;
using MusicTagWinApp.Adapter;
using MusicTagWinApp.Exporters;
using MusicTagWinApp.Web;
using MusicTagWinApp.Writers;

namespace MusicTag.Tests;

// 联网歌词搜索 characterization：SearchLyrics 端到端驱动各 provider 真实解析链
//（搜索 JSON 取候选 → 逐首 LoadLyrics 二次请求歌词端点 → 解析 LRC/翻译 → 排序）。
// 双注入点：搜索与歌词是不同的 HTTP 调用——NetEase/QQ 搜索走 PostString、歌词走 GetResponseString，
// 故 Stub 分别 override 两者（searchResponse / lyricResponse），互不串扰。
// QQ 歌词是 base64-in-jsonp：fixture 用 Convert.ToBase64String(UTF8) 动态编码（自解释，不硬编码 base64）。
// QQ 仅在原文+译文都非空时才走 AlignAndSplitTranslatedLyric 复杂对齐——此处用纯原文规避（对齐留待边界外）。
// Kugou/Kuwo（搜索与歌词同走 GetResponseString，需按 URL 区分）见后续批次。
internal static class ProviderLyricCharacterization
{
	private sealed class StubNetEase : NetEaseMusicTagProvider
	{
		private readonly string searchResponse;
		private readonly string lyricResponse;
		public StubNetEase(string searchResponse, string lyricResponse = null) : base(null)
		{
			this.searchResponse = searchResponse;
			this.lyricResponse = lyricResponse;
		}
		protected override string PostString(string url, string body, HttpClient client = null, bool postJson = false) => searchResponse;
		protected override string GetResponseString(string url) => lyricResponse;
	}

	private sealed class StubQq : QqMusicTagProvider
	{
		private readonly string searchResponse;
		private readonly string lyricResponse;
		public StubQq(string searchResponse, string lyricResponse = null) : base(null)
		{
			this.searchResponse = searchResponse;
			this.lyricResponse = lyricResponse;
		}
		protected override string PostString(string url, string body, HttpClient client = null, bool postJson = false) => searchResponse;
		protected override string GetResponseString(string url) => lyricResponse;
	}

	// QQ 歌词响应：callbackName({"lyric":"<base64>","trans":"<base64>"})。空串保持为空（触发"无歌词"分支）。
	private static string QqJsonpLyric(string lyric, string translation = "")
	{
		string encodedLyric = string.IsNullOrEmpty(lyric) ? "" : Convert.ToBase64String(Encoding.UTF8.GetBytes(lyric));
		string encodedTrans = string.IsNullOrEmpty(translation) ? "" : Convert.ToBase64String(Encoding.UTF8.GetBytes(translation));
		return "MusicJsonCallback34475857153687595({\"lyric\":\"" + encodedLyric + "\",\"trans\":\"" + encodedTrans + "\"})";
	}

	private const string NetEaseSearchOneSong =
		"{\"result\":{\"songs\":[{\"id\":111,\"name\":\"SongA\",\"ar\":[{\"id\":1,\"name\":\"ArtistA\"}],\"al\":{\"id\":10,\"name\":\"AlbumA\"}}]}}";

	private const string QqSearchOneSong =
		"{\"req_0\":{\"code\":0,\"data\":{\"body\":{\"song\":{\"list\":[" +
		"{\"id\":555,\"mid\":\"M555\",\"name\":\"NameA\",\"title\":\"TitleA\",\"singer\":[{\"name\":\"SingerA\"}],\"album\":{\"id\":99,\"mid\":\"ALB99\",\"name\":\"AlbumA\"}}" +
		"]}}}}}";

	public static IEnumerable<(string, Action)> All()
	{
		// ---- NetEase：lrc.lyric / tlyric.lyric 直取 ----
		yield return ("NetEase.SearchLyrics maps lrc + tlyric + fields", delegate
		{
			string lyricJson = "{\"lrc\":{\"lyric\":\"[00:01.00]Line1\\n[00:02.00]Line2\"},\"tlyric\":{\"lyric\":\"[00:01.00]Trans1\"}}";
			List<LyricSearchResult> lyrics = new StubNetEase(NetEaseSearchOneSong, lyricJson).SearchLyrics("q", 10, 0L, new List<LyricSearchResult>(), 2);
			Check.Equal(1, lyrics.Count, "count");
			Check.Equal("[00:01.00]Line1\n[00:02.00]Line2", lyrics[0].Lyric, "[0].Lyric");
			Check.Equal("[00:01.00]Trans1", lyrics[0].TranslatedLyric, "[0].TranslatedLyric");
			Check.Equal("111", lyrics[0].TrackId, "[0].TrackId");
			Check.Equal("SongA", lyrics[0].Title, "[0].Title");
			Check.Equal("ArtistA", lyrics[0].Artist, "[0].Artist");
			Check.Equal("AlbumA", lyrics[0].Album, "[0].Album");
			Check.Equal(SearchSource.Music163, lyrics[0].SearchSource, "[0].SearchSource");
			Check.Equal(0, lyrics[0].ResultOrder, "[0].ResultOrder");
			Check.Equal(2, lyrics[0].SourceOrder, "[0].SourceOrder");
		});

		yield return ("NetEase.SearchLyrics empty lrc -> 0 lyrics (null result skipped)", delegate
		{
			List<LyricSearchResult> lyrics = new StubNetEase(NetEaseSearchOneSong, "{\"lrc\":{\"lyric\":\"\"}}").SearchLyrics("q", 10, 0L, new List<LyricSearchResult>(), 0);
			Check.Equal(0, lyrics.Count, "count");
		});

		yield return ("NetEase.SearchLyrics existing TrackId skipped", delegate
		{
			List<LyricSearchResult> existing = new List<LyricSearchResult> { new LyricSearchResult { TrackId = "111" } };
			List<LyricSearchResult> lyrics = new StubNetEase(NetEaseSearchOneSong, "{\"lrc\":{\"lyric\":\"[00:01.00]X\"}}").SearchLyrics("q", 10, 0L, existing, 0);
			Check.Equal(0, lyrics.Count, "count");
		});

		yield return ("NetEase.SearchLyrics search HTTP-200 unparseable -> 0 + ParseFailed", delegate
		{
			StubNetEase provider = new StubNetEase("<html>not json</html>", "{\"lrc\":{\"lyric\":\"x\"}}");
			List<LyricSearchResult> lyrics = provider.SearchLyrics("q", 10, 0L, new List<LyricSearchResult>(), 0);
			Check.Equal(0, lyrics.Count, "count");
			Check.NotNull(provider.LastTransportResult, "LastTransportResult");
			Check.Equal(RemoteErrorKind.ParseFailed, provider.LastTransportResult.Error, "Error");
		});

		// ---- QQ：base64-in-jsonp 歌词，纯原文（规避 AlignAndSplitTranslatedLyric） ----
		yield return ("QQ.SearchLyrics decodes base64 jsonp lyric + fields", delegate
		{
			string lyricText = "[00:01.00]Line1\n[00:02.00]Line2";
			List<LyricSearchResult> lyrics = new StubQq(QqSearchOneSong, QqJsonpLyric(lyricText)).SearchLyrics("q", 10, 3);
			Check.Equal(1, lyrics.Count, "count");
			Check.Equal(lyricText, lyrics[0].Lyric, "[0].Lyric");
			Check.Equal("555", lyrics[0].TrackId, "[0].TrackId");
			Check.Equal("TitleA", lyrics[0].Title, "[0].Title");
			Check.Equal("NameA", lyrics[0].OriginalTitle, "[0].OriginalTitle");
			Check.Equal("AlbumA", lyrics[0].Album, "[0].Album");
			Check.Equal("SingerA", lyrics[0].Artist, "[0].Artist");
			Check.Equal(SearchSource.QQ, lyrics[0].SearchSource, "[0].SearchSource");
			Check.Equal(3, lyrics[0].SourceOrder, "[0].SourceOrder");
		});

		yield return ("QQ.SearchLyrics empty jsonp lyric -> 0 lyrics", delegate
		{
			List<LyricSearchResult> lyrics = new StubQq(QqSearchOneSong, QqJsonpLyric("")).SearchLyrics("q", 10, 0);
			Check.Equal(0, lyrics.Count, "count");
		});

		yield return ("QQ.SearchLyrics search HTTP-200 unparseable -> 0 + ParseFailed", delegate
		{
			StubQq provider = new StubQq("<html>not json</html>", QqJsonpLyric("x"));
			List<LyricSearchResult> lyrics = provider.SearchLyrics("q", 10, 0);
			Check.Equal(0, lyrics.Count, "count");
			Check.NotNull(provider.LastTransportResult, "LastTransportResult");
			Check.Equal(RemoteErrorKind.ParseFailed, provider.LastTransportResult.Error, "Error");
		});
	}
}
