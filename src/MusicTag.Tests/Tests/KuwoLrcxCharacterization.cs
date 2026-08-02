using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using MusicTag.Serialization;
using MusicTagWinApp.Adapter;
using MusicTagWinApp.Properties;
using MusicTagWinApp.Roles;

namespace MusicTag.Tests;

internal static class KuwoLrcxCharacterization
{
	private const string OneSongResponse =
		"{\"abslist\":[{\"SONGNAME\":\"SongA\",\"ARTIST\":\"ArtistA\",\"ALBUM\":\"AlbumA\",\"MUSICRID\":\"MUSIC_198554068\"}]}";

	private const string TwoSongResponse =
		"{\"abslist\":[" +
		"{\"SONGNAME\":\"SongA\",\"ARTIST\":\"ArtistA\",\"ALBUM\":\"AlbumA\",\"MUSICRID\":\"MUSIC_198554068\"}," +
		"{\"SONGNAME\":\"SongB\",\"ARTIST\":\"ArtistB\",\"ALBUM\":\"AlbumB\",\"MUSICRID\":\"MUSIC_237787110\"}]}";

	private const string LegacyDetailResponse =
		"{\"data\":{\"lrclist\":[{\"time\":\"1.5\",\"lineLyric\":\"Legacy\"}],\"songinfo\":{\"pic\":\"\"}}}";

	private static readonly byte[] XorKey = Encoding.ASCII.GetBytes("yeelion");

	private sealed class ScriptedKuwoProvider : KuwoTagProvider
	{
		private readonly Func<string, HttpResult> responder;

		internal readonly List<string> RequestedUrls = new List<string>();

		internal ScriptedKuwoProvider(Func<string, HttpResult> responder, CancellationTokenSource cancellation = null)
			: base(cancellation)
		{
			this.responder = responder;
		}

		protected override HttpResult GetResponseBytesResult(string url)
		{
			RequestedUrls.Add(url);
			return responder(url);
		}

		protected override HttpResult GetResponseStringResult(string url)
		{
			RequestedUrls.Add(url);
			return responder(url);
		}
	}

	public static IEnumerable<(string, Action)> All()
	{
		yield return ("KuwoLrcxDecoder builds the captured request URL", delegate
		{
			Check.Equal(KuwoLrcxFixtures.RequestUrl, KuwoLrcxDecoder.BuildRequestUrl(KuwoLrcxFixtures.TrackId), "request URL");
			CheckThrows<ArgumentException>(() => KuwoLrcxDecoder.BuildRequestUrl("0"), "zero track ID");
			CheckThrows<ArgumentException>(() => KuwoLrcxDecoder.BuildRequestUrl("MUSIC_1"), "prefixed track ID");
		});

		yield return ("KuwoLrcxDecoder decodes the real response and preserves 7433 ms", delegate
		{
			byte[] responseBytes = KuwoLrcxFixtures.GetRealResponseBytes();
			Check.Equal(KuwoLrcxFixtures.ResponseLength, responseBytes.Length, "response length");
			Check.Equal(KuwoLrcxFixtures.ResponseSha256, Convert.ToHexString(SHA256.HashData(responseBytes)), "response SHA-256");

			KuwoLrcxDecodeResult result = KuwoLrcxDecoder.DecodeResponse(responseBytes);
			Check.Equal(69, result.LyricLines.Count, "timed line count");
			Check.Equal(0, result.TranslatedLyricLines.Count, "translation count");
			string lyric = result.FormatLyric(useThreeDigitMilliseconds: true);
			Check.True(lyric.Contains("[00:07.433]\u8BCD\uFF1A\u5510\u606C", StringComparison.Ordinal), "real 7.433 line");
			Check.True(!lyric.Contains("<2104,-2104>", StringComparison.Ordinal), "word markers removed");
			Check.True(result.FormatLyric(useThreeDigitMilliseconds: false).Contains("[00:07.43]\u8BCD\uFF1A\u5510\u606C", StringComparison.Ordinal), "two-digit rounding");
		});

		yield return ("KuwoLrcxDecoder parses fractional precision and negative word markers", delegate
		{
			KuwoLrcxDecodeResult result = KuwoLrcxDecoder.ParseTimedLines(
				"[00:01.2]<-10,-20>A\n" +
				"[00:02.34]<1,2,3>B\n" +
				"[00:03.456]<-1,-2,-3>C\n");
			Check.Equal("[00:01.200]A\n[00:02.340]B\n[00:03.456]C\n", result.FormatLyric(true), "fractional timestamps");
		});

		yield return ("KuwoLrcxDecoder decodes GB18030 Chinese and Japanese text", delegate
		{
			KuwoLrcxDecodeResult result = KuwoLrcxDecoder.DecodeResponse(BuildPlainTextResponse(
				"[00:01.005]<-1,-2>\u5922\u306A\u3089\u3070\n" +
				"[00:02.010]<1,2,3>\u5982\u679C\u53EA\u662F\u4E00\u573A\u68A6\n"));
			Check.Equal(
				"[00:01.005]\u5922\u306A\u3089\u3070\n[00:02.010]\u5982\u679C\u53EA\u662F\u4E00\u573A\u68A6\n",
				result.FormatLyric(true),
				"GB18030 lyric");
		});

		yield return ("KuwoLrcxDecoder pairs Lemon translation slots without language heuristics", delegate
		{
			KuwoLrcxDecodeResult result = KuwoLrcxDecoder.ParseTimedLines(
				"[00:00.530]   \n" +
				"[00:00.530]\u8BCD\uFF1A\u7C73\u6D25\u7384\u5E2B\n" +
				"[00:01.547]   \n" +
				"[00:01.547]\u5922\u306A\u3089\u3070\n" +
				"[00:02.880]\u5982\u679C\u53EA\u662F\u4E00\u573A\u68A6\n" +
				"[00:02.880]\u3069\u308C\u307B\u3069\u3088\u304B\u3063\u305F\u3067\u3057\u3087\u3046\n");
			Check.Equal(
				"[00:00.530]\u8BCD\uFF1A\u7C73\u6D25\u7384\u5E2B\n" +
				"[00:01.547]\u5922\u306A\u3089\u3070\n" +
				"[00:02.880]\u3069\u308C\u307B\u3069\u3088\u304B\u3063\u305F\u3067\u3057\u3087\u3046\n",
				result.FormatLyric(true),
				"main lyric");
			Check.Equal("[00:01.547]\u5982\u679C\u53EA\u662F\u4E00\u573A\u68A6\n", result.FormatTranslatedLyric(true), "translation");
		});

		yield return ("KuwoLrcxDecoder rejects damaged response contracts", delegate
		{
			CheckThrows<ArgumentNullException>(() => KuwoLrcxDecoder.DecodeResponse(null), "null response");
			CheckThrows<InvalidDataException>(() => KuwoLrcxDecoder.DecodeResponse(Encoding.ASCII.GetBytes("tp=content")), "missing separator");
			CheckThrows<InvalidDataException>(() => KuwoLrcxDecoder.DecodeResponse(BuildCompressedResponse(Encoding.ASCII.GetBytes("QQ=="), "tp=error\r\n\r\n")), "wrong response type");
			CheckThrows<InvalidDataException>(() => KuwoLrcxDecoder.DecodeResponse(Encoding.ASCII.GetBytes("tp=content\r\n\r\n")), "empty compressed body");
			CheckThrows<InvalidDataException>(() => KuwoLrcxDecoder.DecodeResponse(Combine(Encoding.ASCII.GetBytes("tp=content\r\n\r\n"), new byte[] { 0 })), "damaged zlib body");
			CheckThrows<FormatException>(() => KuwoLrcxDecoder.DecodeResponse(BuildCompressedResponse(Encoding.ASCII.GetBytes("not!base64"))), "invalid Base64");
			CheckThrows<FormatException>(() => KuwoLrcxDecoder.DecodeResponse(BuildCompressedResponse(new byte[] { 0xFF })), "non-ASCII Base64");
			CheckThrows<DecoderFallbackException>(() => KuwoLrcxDecoder.DecodeResponse(BuildCompressedResponse(Encoding.ASCII.GetBytes("+A=="))), "invalid GB18030");
		});

		yield return ("Kuwo LRCX primary succeeds without calling legacy", delegate
		{
			RunWithReformatSetting(false, delegate
			{
				ScriptedKuwoProvider provider = new ScriptedKuwoProvider(url =>
				{
					if (url.StartsWith("https://search.kuwo.cn/", StringComparison.Ordinal))
					{
						return SuccessBody(OneSongResponse);
					}
					if (IsLrcxUrl(url))
					{
						return SuccessBytes(KuwoLrcxFixtures.GetRealResponseBytes());
					}
					throw new TestFailure("unexpected request: " + url);
				});

				List<LyricSearchResult> lyrics = provider.SearchLyrics("query", 5, 4);
				Check.Equal(1, lyrics.Count, "count");
				Check.True(lyrics[0].Lyric.Contains("[00:07.433]", StringComparison.Ordinal), "high precision lyric");
				Check.Equal("SongA", lyrics[0].Title, "title");
				Check.Equal(4, lyrics[0].SourceOrder, "source order");
				Check.Equal(1, CountLrcxRequests(provider), "LRCX count");
				Check.Equal(0, CountDetailRequests(provider), "legacy count");
			});
		});

		yield return ("Kuwo no LRCX candidate falls back per song without a circuit breaker", delegate
		{
			byte[] noCandidate = BuildPlainTextResponse("[ti:no timed lyric]\n");
			ScriptedKuwoProvider provider = new ScriptedKuwoProvider(url =>
			{
				if (url.StartsWith("https://search.kuwo.cn/", StringComparison.Ordinal))
				{
					return SuccessBody(TwoSongResponse);
				}
				return IsLrcxUrl(url) ? SuccessBytes(noCandidate) : SuccessBody(LegacyDetailResponse);
			});

			List<LyricSearchResult> lyrics = provider.SearchLyrics("query", 5, 0);
			Check.Equal(2, lyrics.Count, "count");
			Check.Equal(2, CountLrcxRequests(provider), "LRCX count");
			Check.Equal(2, CountDetailRequests(provider), "legacy count");
		});

		yield return ("Kuwo LRCX transport failure trips a one-search circuit breaker", delegate
		{
			ScriptedKuwoProvider provider = new ScriptedKuwoProvider(url =>
			{
				if (url.StartsWith("https://search.kuwo.cn/", StringComparison.Ordinal))
				{
					return SuccessBody(TwoSongResponse);
				}
				if (IsLrcxUrl(url))
				{
					return new HttpResult { Error = RemoteErrorKind.Timeout, ErrorCode = "timeout" };
				}
				return SuccessBody(LegacyDetailResponse);
			});

			List<LyricSearchResult> lyrics = provider.SearchLyrics("query", 5, 0);
			Check.Equal(2, lyrics.Count, "count");
			Check.Equal(1, CountLrcxRequests(provider), "LRCX count");
			Check.Equal(2, CountDetailRequests(provider), "legacy count");
			Check.NotNull(provider.LastTransportResult, "LastTransportResult");
			Check.Equal(RemoteErrorKind.None, provider.LastTransportResult.Error, "successful fallback clears LRCX error");
		});

		yield return ("Kuwo malformed LRCX trips a one-search protocol circuit breaker", delegate
		{
			ScriptedKuwoProvider provider = new ScriptedKuwoProvider(url =>
			{
				if (url.StartsWith("https://search.kuwo.cn/", StringComparison.Ordinal))
				{
					return SuccessBody(TwoSongResponse);
				}
				return IsLrcxUrl(url) ? SuccessBytes(Encoding.ASCII.GetBytes("damaged")) : SuccessBody(LegacyDetailResponse);
			});

			List<LyricSearchResult> lyrics = provider.SearchLyrics("query", 5, 0);
			Check.Equal(2, lyrics.Count, "count");
			Check.Equal(1, CountLrcxRequests(provider), "LRCX count");
			Check.Equal(2, CountDetailRequests(provider), "legacy count");
		});

		yield return ("Kuwo cancellation stops legacy fallback and allows a later LRCX retry", delegate
		{
			KuwoSongInfo song = CreateSong();
			using CancellationTokenSource cancellation = new CancellationTokenSource();
			ScriptedKuwoProvider canceledProvider = new ScriptedKuwoProvider(url =>
			{
				cancellation.Cancel();
				return SuccessBytes(KuwoLrcxFixtures.GetRealResponseBytes());
			}, cancellation);
			LyricSearchResult lyric = canceledProvider.LoadLyrics(song);
			Check.Null(lyric, "lyric");
			Check.Equal(1, canceledProvider.RequestedUrls.Count, "canceled request count");
			Check.True(IsLrcxUrl(canceledProvider.RequestedUrls[0]), "canceled LRCX request");
			Check.True(!song.LrcxAttempted, "cancellation does not persist attempted state");

			RunWithReformatSetting(false, delegate
			{
				ScriptedKuwoProvider retryProvider = new ScriptedKuwoProvider(url => SuccessBytes(KuwoLrcxFixtures.GetRealResponseBytes()));
				LyricSearchResult retried = retryProvider.LoadLyrics(song);
				Check.NotNull(retried, "retried lyric");
				Check.True(retried.Lyric.Contains("[00:07.433]", StringComparison.Ordinal), "retried precision");
				Check.Equal(1, retryProvider.RequestedUrls.Count, "retry request count");
			});
		});

		yield return ("Kuwo cached legacy lyric upgrades to LRCX and cannot be overwritten", delegate
		{
			RunWithReformatSetting(false, delegate
			{
				ScriptedKuwoProvider provider = new ScriptedKuwoProvider(url => SuccessBytes(KuwoLrcxFixtures.GetRealResponseBytes()));
				KuwoSongInfo song = CreateSong();
				provider.PopulateSongDetails(song, LegacyDetailResponse);
				Check.Equal(KuwoLyricQuality.Legacy, song.LoadedLyricQuality, "initial quality");

				LyricSearchResult upgraded = provider.LoadLyrics(song);
				Check.NotNull(upgraded, "upgraded lyric");
				Check.True(upgraded.Lyric.Contains("[00:07.433]", StringComparison.Ordinal), "upgraded precision");
				Check.Equal(KuwoLyricQuality.HighPrecision, song.LoadedLyricQuality, "upgraded quality");
				Check.Equal(1, CountLrcxRequests(provider), "first LRCX count");

				Check.True(ReferenceEquals(upgraded, provider.LoadLyrics(song)), "high precision cache");
				Check.Equal(1, CountLrcxRequests(provider), "cached LRCX count");
				provider.PopulateSongDetails(song, LegacyDetailResponse.Replace("Legacy", "Replacement", StringComparison.Ordinal));
				Check.True(ReferenceEquals(upgraded, song.LoadedLyric), "legacy details do not overwrite LRCX");
			});
		});

		yield return ("Kuwo failed LRCX keeps an existing legacy lyric", delegate
		{
			ScriptedKuwoProvider provider = new ScriptedKuwoProvider(url => SuccessBytes(Encoding.ASCII.GetBytes("damaged")));
			KuwoSongInfo song = CreateSong();
			provider.PopulateSongDetails(song, LegacyDetailResponse);
			LyricSearchResult legacy = song.LoadedLyric;

			Check.True(ReferenceEquals(legacy, provider.LoadLyrics(song)), "legacy cache retained");
			Check.Equal(KuwoLyricQuality.Legacy, song.LoadedLyricQuality, "legacy quality");
			Check.Equal(1, CountLrcxRequests(provider), "LRCX count");
			Check.Equal(0, CountDetailRequests(provider), "detail count");
			Check.NotNull(provider.LastTransportResult, "LastTransportResult");
			Check.Equal(RemoteErrorKind.None, provider.LastTransportResult.Error, "legacy cache clears LRCX error");
		});

		yield return ("Kuwo LRCX and legacy failures preserve a diagnostic error", delegate
		{
			ScriptedKuwoProvider provider = new ScriptedKuwoProvider(url =>
				IsLrcxUrl(url)
					? SuccessBytes(Encoding.ASCII.GetBytes("damaged"))
					: SuccessBody("{"));
			LyricSearchResult lyric = provider.LoadLyrics(CreateSong());
			Check.Null(lyric, "lyric");
			Check.NotNull(provider.LastTransportResult, "LastTransportResult");
			Check.Equal(RemoteErrorKind.ParseFailed, provider.LastTransportResult.Error, "Error");
			Check.Equal("parse", provider.LastTransportResult.ErrorCode, "legacy parse error wins");
		});

		yield return ("Kuwo failed LRCX remains diagnosable when legacy has no lyric", delegate
		{
			ScriptedKuwoProvider provider = new ScriptedKuwoProvider(url =>
				IsLrcxUrl(url)
					? SuccessBytes(Encoding.ASCII.GetBytes("damaged"))
					: SuccessBody("{\"data\":{}}"));
			LyricSearchResult lyric = provider.LoadLyrics(CreateSong());
			Check.Null(lyric, "lyric");
			Check.NotNull(provider.LastTransportResult, "LastTransportResult");
			Check.Equal(RemoteErrorKind.ParseFailed, provider.LastTransportResult.Error, "Error");
			Check.Equal("lrcx_parse", provider.LastTransportResult.ErrorCode, "LRCX error retained");
		});
	}

	internal static byte[] BuildPlainTextResponse(string lyricText)
	{
		byte[] lyricBytes = Encoding.GetEncoding("GB18030").GetBytes(lyricText);
		ApplyXor(lyricBytes);
		return BuildCompressedResponse(Encoding.ASCII.GetBytes(Convert.ToBase64String(lyricBytes)));
	}

	private static byte[] BuildCompressedResponse(byte[] encodedBytes, string header = "tp=content\r\nlrcx=1\r\n\r\n")
	{
		using MemoryStream compressed = new MemoryStream();
		using (ZLibStream compressor = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
		{
			compressor.Write(encodedBytes, 0, encodedBytes.Length);
		}
		return Combine(Encoding.ASCII.GetBytes(header), compressed.ToArray());
	}

	private static byte[] Combine(byte[] first, byte[] second)
	{
		byte[] combined = new byte[first.Length + second.Length];
		Buffer.BlockCopy(first, 0, combined, 0, first.Length);
		Buffer.BlockCopy(second, 0, combined, first.Length, second.Length);
		return combined;
	}

	private static void ApplyXor(byte[] bytes)
	{
		for (int index = 0; index < bytes.Length; index++)
		{
			bytes[index] ^= XorKey[index % XorKey.Length];
		}
	}

	private static KuwoSongInfo CreateSong()
	{
		return new KuwoSongInfo
		{
			TrackId = KuwoLrcxFixtures.TrackId,
			Title = "Title",
			Artist = "Artist",
			Album = "Album"
		};
	}

	private static bool IsLrcxUrl(string url)
	{
		return url.StartsWith("https://newlyric.kuwo.cn/", StringComparison.Ordinal);
	}

	private static int CountLrcxRequests(ScriptedKuwoProvider provider)
	{
		return provider.RequestedUrls.Count(IsLrcxUrl);
	}

	private static int CountDetailRequests(ScriptedKuwoProvider provider)
	{
		return provider.RequestedUrls.Count(url => url.Contains("songinfoandlrc", StringComparison.Ordinal));
	}

	private static HttpResult SuccessBody(string body)
	{
		return new HttpResult { Body = body };
	}

	private static HttpResult SuccessBytes(byte[] bytes)
	{
		return new HttpResult { Bytes = bytes };
	}

	private static void RunWithReformatSetting(bool value, Action action)
	{
		bool originalValue = Settings.Default.LyricDownload_ReformatTimetag;
		try
		{
			Settings.Default.LyricDownload_ReformatTimetag = value;
			action();
		}
		finally
		{
			Settings.Default.LyricDownload_ReformatTimetag = originalValue;
		}
	}

	private static void CheckThrows<TException>(Action action, string label) where TException : Exception
	{
		try
		{
			action();
		}
		catch (TException)
		{
			return;
		}
		throw new TestFailure(label + ": expected " + typeof(TException).Name);
	}
}
