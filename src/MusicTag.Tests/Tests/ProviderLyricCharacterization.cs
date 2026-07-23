using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using MusicTag.Candidates;
using MusicTag.Serialization;
using MusicTagWinApp.Adapter;
using MusicTagWinApp.Exporters;
using MusicTagWinApp.Web;
using MusicTagWinApp.Writers;
using MusicTagWinApp.Properties;

namespace MusicTag.Tests;

// 联网歌词搜索 characterization：SearchLyrics 端到端驱动各 provider 真实解析链
//（搜索 JSON 取候选 → 逐首 LoadLyrics/LoadSongDetails 二次请求歌词端点 → 解析 LRC/翻译 → 排序）。
// 注入按 HTTP 调用区分：
//  - NetEase/QQ：搜索走 PostString、歌词走 GetResponseString（两个 override 分别喂）。
//  - Kugou/Kuwo：搜索与歌词同走 GetResponseString，故 Stub 按 URL 关键字分流（Kugou 歌词含 get_krc、
//    Kuwo 详情含 songinfoandlrc）。
// QQ 歌词是 base64-in-jsonp：fixture 用 Convert.ToBase64String(UTF8) 动态编码（自解释，不硬编码 base64）。
// QQ 仅在原文+译文都非空时才走 AlignAndSplitTranslatedLyric 复杂对齐 / Kuwo 仅在多行双语时走重排——
// 此处均用单语规避（对齐/重排留待边界外）。
internal static class ProviderLyricCharacterization
{
	private sealed class StubNetEase : NetEaseMusicTagProvider
	{
		private readonly string searchResponse;
		private readonly string lyricResponse;
		public string LastLyricUrl { get; private set; }
		public StubNetEase(string searchResponse, string lyricResponse = null) : base(null)
		{
			this.searchResponse = searchResponse;
			this.lyricResponse = lyricResponse;
		}
		protected override string PostString(string url, string body, HttpClient client = null, bool postJson = false) => searchResponse;
		protected override string GetResponseString(string url)
		{
			LastLyricUrl = url;
			return lyricResponse;
		}
	}

	private sealed class StubQq : QqMusicTagProvider
	{
		private readonly string searchResponse;
		private readonly string lyricResponse;
		private readonly string qrcResponse;
		public string QrcRequestUrl { get; private set; }
		public string QrcRequestBody { get; private set; }
		public StubQq(string searchResponse, string lyricResponse = null, string qrcResponse = null) : base(null)
		{
			this.searchResponse = searchResponse;
			this.lyricResponse = lyricResponse;
			this.qrcResponse = qrcResponse;
		}
		protected override string PostString(string url, string body, HttpClient client = null, bool postJson = false)
		{
			if (body?.Contains("GetPlayLyricInfo") == true)
			{
				QrcRequestUrl = url;
				QrcRequestBody = body;
				return qrcResponse ?? searchResponse;
			}
			return searchResponse;
		}
		protected override string GetResponseString(string url) => lyricResponse;
	}

	private sealed class StubKugou : KugouTagProvider
	{
		private readonly string searchResponse;
		private readonly string lyricResponse;
		public StubKugou(string searchResponse, string lyricResponse = null) : base(null)
		{
			this.searchResponse = searchResponse;
			this.lyricResponse = lyricResponse;
		}
		// 搜索（/api/v3/search/song）与歌词（/krc/get_krc）同走 GET，按 URL 关键字分流。
		protected override string GetResponseString(string url) => url.Contains("get_krc") ? lyricResponse : searchResponse;
	}

	private sealed class StubKuwo : KuwoTagProvider
	{
		private readonly string searchResponse;
		private readonly string detailResponse;
		public StubKuwo(string searchResponse, string detailResponse = null) : base(null)
		{
			this.searchResponse = searchResponse;
			this.detailResponse = detailResponse;
		}
		// 搜索（search.kuwo.cn/r.s）与详情/歌词（…/songinfoandlrc）同走 GET，按 URL 关键字分流。
		protected override string GetResponseString(string url) => url.Contains("songinfoandlrc") ? detailResponse : searchResponse;
	}

	// QQ 歌词响应：callbackName({"lyric":"<base64>","trans":"<base64>"})。空串保持为空（触发"无歌词"分支）。
	private static string QqJsonpLyric(string lyric, string translation = "")
	{
		string encodedLyric = string.IsNullOrEmpty(lyric) ? "" : Convert.ToBase64String(Encoding.UTF8.GetBytes(lyric));
		string encodedTrans = string.IsNullOrEmpty(translation) ? "" : Convert.ToBase64String(Encoding.UTF8.GetBytes(translation));
		return "MusicJsonCallback34475857153687595({\"lyric\":\"" + encodedLyric + "\",\"trans\":\"" + encodedTrans + "\"})";
	}

	private static string QqQrcResponse(string lyric, string translation = "")
	{
		return "{\"req_0\":{\"code\":0,\"data\":{\"qrc_t\":1,\"lyric\":\"" + EncryptQrcFixture(lyric) + "\",\"trans\":\"" + EncryptQrcFixture(translation) + "\"}}}";
	}

	private static string EncryptQrcFixture(string lyric)
	{
		if (string.IsNullOrEmpty(lyric))
		{
			return "";
		}

		byte[] compressed;
		using (MemoryStream output = new MemoryStream())
		{
			using (ZLibStream compressor = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
			{
				byte[] source = Encoding.UTF8.GetBytes(lyric);
				compressor.Write(source, 0, source.Length);
			}
			compressed = output.ToArray();
		}

		int paddedLength = (compressed.Length + 7) / 8 * 8;
		byte[] padded = new byte[paddedLength];
		Buffer.BlockCopy(compressed, 0, padded, 0, compressed.Length);
		byte[] encrypted = new byte[paddedLength];
		byte[][][] schedule = new byte[3][][];
		for (int keyIndex = 0; keyIndex < schedule.Length; keyIndex++)
		{
			schedule[keyIndex] = new byte[16][];
			for (int round = 0; round < schedule[keyIndex].Length; round++)
			{
				schedule[keyIndex][round] = new byte[6];
			}
		}

		byte[] key = Encoding.ASCII.GetBytes("!@#)(*$%123ZXC!@!@#)(NHL");
		QqDesHelper.TripleDESKeySetup(key, schedule, QqDesHelper.ENCRYPT);
		for (int offset = 0; offset < padded.Length; offset += 8)
		{
			byte[] block = new byte[8];
			Buffer.BlockCopy(padded, offset, block, 0, block.Length);
			byte[] encryptedBlock = new byte[8];
			QqDesHelper.TripleDESCrypt(block, encryptedBlock, schedule);
			Buffer.BlockCopy(encryptedBlock, 0, encrypted, offset, encryptedBlock.Length);
		}

		return Convert.ToHexString(encrypted);
	}

	private const string NetEaseSearchOneSong =
		"{\"result\":{\"songs\":[{\"id\":111,\"name\":\"SongA\",\"ar\":[{\"id\":1,\"name\":\"ArtistA\"}],\"al\":{\"id\":10,\"name\":\"AlbumA\"}}]}}";

	private const string QqSearchOneSong =
		"{\"req_0\":{\"code\":0,\"data\":{\"body\":{\"song\":{\"list\":[" +
		"{\"id\":555,\"mid\":\"M555\",\"name\":\"NameA\",\"title\":\"TitleA\",\"singer\":[{\"name\":\"SingerA\"}],\"album\":{\"id\":99,\"mid\":\"ALB99\",\"name\":\"AlbumA\"}}" +
		"]}}}}}";

	private const string KugouSearchOneSong =
		"{\"data\":{\"info\":[{\"audio_id\":\"A123\",\"songname\":\"SongA\",\"singername\":\"ArtistA\",\"album_name\":\"AlbumA\",\"hash\":\"H1\",\"duration\":240}]}}";

	private const string KuwoSearchOneSong =
		"{\"abslist\":[{\"SONGNAME\":\"SongA\",\"ARTIST\":\"ArtistA\",\"ALBUM\":\"AlbumA\",\"MUSICRID\":\"MUSIC_12345\"}]}";

	public static IEnumerable<(string, Action)> All()
	{
		// ---- NetEase：lrc.lyric / tlyric.lyric 直取 ----
		yield return ("NetEase.SearchLyrics requests latest YRC fields on the v1 lyric endpoint", delegate
		{
			StubNetEase provider = new StubNetEase(NetEaseSearchOneSong, "{\"lrc\":{\"lyric\":\"[00:01.00]Line1\"}}");
			provider.SearchLyrics("q", 10, 0L, new List<LyricSearchResult>(), 0);
			Check.Equal("https://music.163.com/api/song/lyric/v1?id=111&cp=false&lv=0&kv=0&tv=0&rv=0&yv=0&ytv=0&yrv=0", provider.LastLyricUrl, "latest lyric endpoint and flags");
		});

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
		}
		);

		yield return ("NetEase.SearchLyrics empty lrc -> 0 lyrics (null result skipped)", delegate
		{
			List<LyricSearchResult> lyrics = new StubNetEase(NetEaseSearchOneSong, "{\"lrc\":{\"lyric\":\"\"}}").SearchLyrics("q", 10, 0L, new List<LyricSearchResult>(), 0);
			Check.Equal(0, lyrics.Count, "count");
		}
		);

		yield return ("NetEase.SearchLyrics existing TrackId skipped", delegate
		{
			List<LyricSearchResult> existing = new List<LyricSearchResult> { new LyricSearchResult { TrackId = "111" } };
			List<LyricSearchResult> lyrics = new StubNetEase(NetEaseSearchOneSong, "{\"lrc\":{\"lyric\":\"[00:01.00]X\"}}").SearchLyrics("q", 10, 0L, existing, 0);
			Check.Equal(0, lyrics.Count, "count");
		}
		);

		yield return ("NetEase.SearchLyrics search HTTP-200 unparseable -> 0 + ParseFailed", delegate
		{
			StubNetEase provider = new StubNetEase("<html>not json</html>", "{\"lrc\":{\"lyric\":\"x\"}}");
			List<LyricSearchResult> lyrics = provider.SearchLyrics("q", 10, 0L, new List<LyricSearchResult>(), 0);
			Check.Equal(0, lyrics.Count, "count");
			Check.NotNull(provider.LastTransportResult, "LastTransportResult");
			Check.Equal(RemoteErrorKind.ParseFailed, provider.LastTransportResult.Error, "Error");
		}
		);

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
		}
		);

		yield return ("QQ.SearchLyrics prefers QRC and preserves real 3-digit milliseconds", delegate
		{
			bool previousSetting = Settings.Default.LyricDownload_ReformatTimetag;
			try
			{
				Settings.Default.LyricDownload_ReformatTimetag = false;
				string qrc = "<?xml version=\"1.0\"?><QrcInfos><LyricInfo LyricContent=\"[ti:Title] [ar:Singer] [12347,800]Hello(12347,400) world(12747,400) [13201,500]Next(13201,500)\" /></QrcInfos>";
				StubQq provider = new StubQq(QqSearchOneSong, QqJsonpLyric("[00:01.00]fallback"), QqQrcResponse(qrc));
				List<LyricSearchResult> lyrics = provider.SearchLyrics("q", 10, 0);
				Check.Equal(1, lyrics.Count, "count");
				Check.Equal("[ti:Title]\n[ar:Singer]\n[00:12.347]Hello world\n[00:13.201]Next", lyrics[0].Lyric, "QRC line lyric");
				Check.True(provider.QrcRequestUrl.Contains("musicu.fcg"), "QRC endpoint");
				Check.True(provider.QrcRequestBody.Contains("\"songID\":555"), "numeric song id");
			}
			finally
			{
				Settings.Default.LyricDownload_ReformatTimetag = previousSetting;
			}
		}
		);

		yield return ("QQ.SearchLyrics QRC follows ReformatTimetag 2-digit rounding", delegate
		{
			bool previousSetting = Settings.Default.LyricDownload_ReformatTimetag;
			try
			{
				Settings.Default.LyricDownload_ReformatTimetag = true;
				string qrc = "[12347,800]Hello(12347,800)";
				List<LyricSearchResult> lyrics = new StubQq(QqSearchOneSong, QqJsonpLyric("fallback"), QqQrcResponse(qrc)).SearchLyrics("q", 10, 0);
				Check.Equal("[00:12.35]Hello", lyrics[0].Lyric, "rounded QRC line lyric");
			}
			finally
			{
				Settings.Default.LyricDownload_ReformatTimetag = previousSetting;
			}
		}
		);

		yield return ("QQ.SearchLyrics aligns 2-digit translation to precise QRC line timestamps", delegate
		{
			bool previousSetting = Settings.Default.LyricDownload_ReformatTimetag;
			try
			{
				Settings.Default.LyricDownload_ReformatTimetag = false;
				string qrc = "<?xml version=\"1.0\"?><QrcInfos><LyricInfo LyricContent=\"[ti:Title] [12347,800]Hello(12347,800) [13201,500]Next(13201,500) [14509,500]Last(14509,500)\" /></QrcInfos>";
				string translation = "[kana:fixture]\n[00:12.34]你好\n[00:13.20]//\n[00:14.50]最后";
				List<LyricSearchResult> lyrics = new StubQq(QqSearchOneSong, QqJsonpLyric("fallback"), QqQrcResponse(qrc, translation)).SearchLyrics("q", 10, 0);
				Check.Equal("[ti:Title]\n[00:12.347]Hello\n[00:13.201]Next\n[00:14.509]Last", lyrics[0].Lyric, "precise QRC original");
				Check.Equal("[00:12.347]你好\n[00:14.509]最后", lyrics[0].TranslatedLyric, "translation aligned to QRC timestamps");
			}
			finally
			{
				Settings.Default.LyricDownload_ReformatTimetag = previousSetting;
			}
		}
		);

		yield return ("QQ.SearchLyrics invalid QRC falls back to base64 LRC", delegate
		{
			string fallback = "[00:01.23]fallback";
			List<LyricSearchResult> lyrics = new StubQq(QqSearchOneSong, QqJsonpLyric(fallback), "{\"req_0\":{\"code\":0,\"data\":{\"lyric\":\"not-hex\"}}}").SearchLyrics("q", 10, 0);
			Check.Equal(fallback, lyrics[0].Lyric, "fallback lyric");
		}
		);

		yield return ("QQ.SearchLyrics empty jsonp lyric -> 0 lyrics", delegate
		{
			List<LyricSearchResult> lyrics = new StubQq(QqSearchOneSong, QqJsonpLyric("")).SearchLyrics("q", 10, 0);
			Check.Equal(0, lyrics.Count, "count");
		}
		);

		yield return ("QQ.SearchLyrics search HTTP-200 unparseable -> 0 + ParseFailed", delegate
		{
			StubQq provider = new StubQq("<html>not json</html>", QqJsonpLyric("x"));
			List<LyricSearchResult> lyrics = provider.SearchLyrics("q", 10, 0);
			Check.Equal(0, lyrics.Count, "count");
			Check.NotNull(provider.LastTransportResult, "LastTransportResult");
			Check.Equal(RemoteErrorKind.ParseFailed, provider.LastTransportResult.Error, "Error");
		}
		);

		// ---- Kugou：data.lrc 直取（无 landata 翻译时 TranslatedLyric 空） ----
		yield return ("Kugou.SearchLyrics maps data.lrc + fields", delegate
		{
			List<LyricSearchResult> lyrics = new StubKugou(KugouSearchOneSong, "{\"data\":{\"lrc\":\"[00:01.00]Hello\"}}").SearchLyrics("q", 10, 1);
			Check.Equal(1, lyrics.Count, "count");
			Check.Equal("[00:01.00]Hello", lyrics[0].Lyric, "[0].Lyric");
			Check.Equal("A123", lyrics[0].TrackId, "[0].TrackId");
			Check.Equal("SongA", lyrics[0].Title, "[0].Title");
			Check.Equal("ArtistA", lyrics[0].Artist, "[0].Artist");
			Check.Equal("AlbumA", lyrics[0].Album, "[0].Album");
			Check.Equal(SearchSource.Kugou, lyrics[0].SearchSource, "[0].SearchSource");
			Check.Equal(1, lyrics[0].SourceOrder, "[0].SourceOrder");
		}
		);

		yield return ("Kugou.SearchLyrics empty lrc -> 0 lyrics", delegate
		{
			List<LyricSearchResult> lyrics = new StubKugou(KugouSearchOneSong, "{\"data\":{}}").SearchLyrics("q", 10, 0);
			Check.Equal(0, lyrics.Count, "count");
		}
		);

		yield return ("Kugou.SearchLyrics search HTTP-200 unparseable -> 0 + ParseFailed", delegate
		{
			StubKugou provider = new StubKugou("<html>not json</html>", "{\"data\":{\"lrc\":\"x\"}}");
			List<LyricSearchResult> lyrics = provider.SearchLyrics("q", 10, 0);
			Check.Equal(0, lyrics.Count, "count");
			Check.NotNull(provider.LastTransportResult, "LastTransportResult");
			Check.Equal(RemoteErrorKind.ParseFailed, provider.LastTransportResult.Error, "Error");
		}
		);

		// ---- Kuwo：详情 lrclist（time 秒*1000，FormatTimestamp 厘秒）逐行拼装；单语规避双语重排 ----
		yield return ("Kuwo.SearchLyrics builds lyric from detail lrclist + fields", delegate
		{
			string detail = "{\"data\":{\"lrclist\":[{\"time\":\"1.5\",\"lineLyric\":\"Hello\"},{\"time\":\"3.0\",\"lineLyric\":\"World\"}]}}";
			List<LyricSearchResult> lyrics = new StubKuwo(KuwoSearchOneSong, detail).SearchLyrics("q", 10, 3);
			Check.Equal(1, lyrics.Count, "count");
			Check.Equal("[00:01.50]Hello\n[00:03.00]World\n", lyrics[0].Lyric, "[0].Lyric");
			Check.Equal("SongA", lyrics[0].Title, "[0].Title");
			Check.Equal("ArtistA", lyrics[0].Artist, "[0].Artist");
			Check.Equal("AlbumA", lyrics[0].Album, "[0].Album");
			Check.Equal(SearchSource.Kuwo, lyrics[0].SearchSource, "[0].SearchSource");
			Check.Equal(3, lyrics[0].SourceOrder, "[0].SourceOrder");
		}
		);

		yield return ("Kuwo.SearchLyrics no lrclist -> 0 lyrics", delegate
		{
			List<LyricSearchResult> lyrics = new StubKuwo(KuwoSearchOneSong, "{\"data\":{}}").SearchLyrics("q", 10, 0);
			Check.Equal(0, lyrics.Count, "count");
		}
		);

		yield return ("Kuwo.SearchLyrics search HTTP-200 unparseable -> 0 + ParseFailed", delegate
		{
			StubKuwo provider = new StubKuwo("<html>not json</html>", "{\"data\":{\"lrclist\":[]}}");
			List<LyricSearchResult> lyrics = provider.SearchLyrics("q", 10, 0);
			Check.Equal(0, lyrics.Count, "count");
			Check.NotNull(provider.LastTransportResult, "LastTransportResult");
			Check.Equal(RemoteErrorKind.ParseFailed, provider.LastTransportResult.Error, "Error");
		}
		);
	}
}
