using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using MusicTag.Candidates;
using MusicTag.Serialization;
using MusicTagWinApp.Adapter;
using MusicTagWinApp.Properties;
using MusicTagWinApp.Roles;

namespace MusicTag.Tests;

internal static class KugouKrcCharacterization
{
	private const string ValidHash = "514AF2D2B993E1A4BFFD0DF3EDA31874";

	private const string AlternateValidHash = "06C3E69A803F73A2ADA14FBBAD8C18C0";

	private const string KrcCandidateResponse =
		"{\"status\":200,\"errcode\":200,\"candidates\":[{\"id\":\"216374858\",\"accesskey\":\"4AFCE24BC33D02004A87B83C091E774C\"}]}";

	private const string EmptyKrcCandidateResponse =
		"{\"status\":200,\"errcode\":200,\"candidates\":[]}";

	private const string OneSongResponse =
		"{\"data\":{\"lists\":[{\"Audioid\":1,\"SongName\":\"SongA\",\"SingerName\":\"ArtistA\",\"AlbumName\":\"AlbumA\",\"FileHash\":\"" + ValidHash + "\",\"Duration\":256}]}}";

	private const string TwoSongResponse =
		"{\"data\":{\"lists\":[" +
		"{\"Audioid\":1,\"SongName\":\"SongA\",\"SingerName\":\"ArtistA\",\"AlbumName\":\"AlbumA\",\"FileHash\":\"" + ValidHash + "\",\"Duration\":256}," +
		"{\"Audioid\":2,\"SongName\":\"SongB\",\"SingerName\":\"ArtistB\",\"AlbumName\":\"AlbumB\",\"FileHash\":\"" + AlternateValidHash + "\",\"Duration\":167}]}}";

	private const string LegacyLyricResponse =
		"{\"data\":{\"lrc\":\"[00:01.00]Legacy\"}}";

	private sealed class ScriptedKugouProvider : KugouTagProvider
	{
		private readonly Func<string, HttpResult> responder;

		internal readonly List<string> RequestedUrls = new List<string>();

		internal ScriptedKugouProvider(Func<string, HttpResult> responder, CancellationTokenSource cancellation = null)
			: base(cancellation)
		{
			this.responder = responder;
		}

		protected override HttpResult GetResponseStringResult(string url)
		{
			RequestedUrls.Add(url);
			return responder(url);
		}
	}

	public static IEnumerable<(string, Action)> All()
	{
		yield return ("KugouKrcDecoder decrypts real krc1 and preserves 22144 ms", delegate
		{
			KugouKrcDecodeResult result = KugouKrcDecoder.DecodeContent(KugouKrcFixtures.RealKrcContentBase64, 0, useThreeDigitMilliseconds: true);
			Check.NotNull(result, "result");
			Check.True(result.Lyric.StartsWith("[offset:0]\n[00:22.144]都是勇敢的\n", StringComparison.Ordinal), "real first line");
			Check.True(!result.Lyric.Contains("<0,", StringComparison.Ordinal), "word markers removed");
			Check.Equal("", result.TranslatedLyric, "no translation");
		});

		yield return ("KugouKrcDecoder uses existing two-digit rounding when formatting is enabled", delegate
		{
			KugouKrcDecodeResult result = KugouKrcDecoder.DecodeContent(KugouKrcFixtures.RealKrcContentBase64, 0, useThreeDigitMilliseconds: false);
			Check.True(result.Lyric.StartsWith("[offset:0]\n[00:22.14]都是勇敢的\n", StringComparison.Ordinal), "rounded first line");
		});

		yield return ("KugouKrcDecoder contenttype 1 and 2 are strict Base64 UTF-8 plain text", delegate
		{
			KugouKrcDecodeResult lrcResult = KugouKrcDecoder.DecodeContent(KugouKrcFixtures.RealLrcContentBase64, 1, useThreeDigitMilliseconds: true);
			Check.True(lrcResult.Lyric.StartsWith("[offset:0]\r\n[00:22.14]都是勇敢的", StringComparison.Ordinal), "real contenttype 1");

			KugouKrcDecodeResult type2Result = KugouKrcDecoder.DecodeContent(KugouKrcFixtures.Type2ContentBase64, 2, useThreeDigitMilliseconds: true);
			Check.Equal("[00:01.23]Type2\n", type2Result.Lyric, "contenttype 2");
		});

		yield return ("KugouKrcDecoder aligns type 1 translation by timed-line index", delegate
		{
			string krcText =
				"\uFEFF[ti:Title]\n" +
				"[id:drop]\n" +
				"[language:" + KugouKrcFixtures.LanguageMetadataBase64 + "]\n" +
				"[1000,500]<5,100,0>Hello <105,100,0> world!\n" +
				"[2000,500]<0,100,0>World\n";
			KugouKrcDecodeResult result = KugouKrcDecoder.ConvertKrcText(krcText, useThreeDigitMilliseconds: true);
			Check.Equal("[ti:Title]\n[00:01.000]Hello  world!\n[00:02.000]World\n", result.Lyric, "main lyric");
			Check.Equal("[00:01.000]你好\n[00:02.000]世界\n", result.TranslatedLyric, "translation");
		});

		yield return ("KugouKrcDecoder damaged optional translation keeps the main lyric", delegate
		{
			KugouKrcDecodeResult result = KugouKrcDecoder.ConvertKrcText(
				"[language:not!base64]\n[1000,500]<0,100,0>Main\n",
				useThreeDigitMilliseconds: true);
			Check.Equal("[00:01.000]Main\n", result.Lyric, "main lyric");
			Check.Equal("", result.TranslatedLyric, "translation dropped");
		});

		yield return ("KugouKrcDecoder damaged line duration falls back to first word offset", delegate
		{
			KugouKrcDecodeResult result = KugouKrcDecoder.ConvertKrcText(
				"[1000,broken]<5,100,0>Fallback\n",
				useThreeDigitMilliseconds: true);
			Check.Equal("[00:01.005]Fallback\n", result.Lyric, "fallback timestamp");
		});

		yield return ("KugouKrcDecoder mismatched translation count keeps the main lyric", delegate
		{
			string languageJson = "{\"content\":[{\"lyricContent\":[[\"Only one\"]],\"type\":1}]}";
			string languagePayload = Convert.ToBase64String(Encoding.UTF8.GetBytes(languageJson));
			KugouKrcDecodeResult result = KugouKrcDecoder.ConvertKrcText(
				"[language:" + languagePayload + "]\n" +
				"[1000,500]<0,100,0>First\n" +
				"[2000,500]<0,100,0>Second\n",
				useThreeDigitMilliseconds: true);
			Check.Equal("[00:01.000]First\n[00:02.000]Second\n", result.Lyric, "main lyric");
			Check.Equal("", result.TranslatedLyric, "translation dropped");
		});

		yield return ("KugouKrcDecoder rejects invalid Base64, UTF-8, magic, compressed body, and content type", delegate
		{
			CheckThrows<FormatException>(() => KugouKrcDecoder.DecodeContent("not!base64", 1, true), "invalid base64");
			CheckThrows<FormatException>(() => KugouKrcDecoder.DecodeContent("QQ==\u4e2d", 1, true), "non-ASCII base64");
			CheckThrows<DecoderFallbackException>(() => KugouKrcDecoder.DecodeContent("/w==", 1, true), "invalid UTF-8");
			CheckThrows<InvalidDataException>(() => KugouKrcDecoder.DecodeContent("bm90a3Jj", 0, true), "wrong magic");
			CheckThrows<InvalidDataException>(() => KugouKrcDecoder.DecodeContent("a3JjMQ==", 0, true), "missing compressed body");
			CheckThrows<InvalidDataException>(() => KugouKrcDecoder.DecodeContent("a3JjMQA=", 0, true), "damaged zlib body");
			CheckThrows<InvalidDataException>(() => KugouKrcDecoder.DecodeContent(KugouKrcFixtures.Type2ContentBase64, 9, true), "unknown content type");
		});

		yield return ("Kugou KRC primary succeeds without calling legacy", delegate
		{
			bool originalSetting = Settings.Default.LyricDownload_ReformatTimetag;
			try
			{
				Settings.Default.LyricDownload_ReformatTimetag = false;
				ScriptedKugouProvider provider = new ScriptedKugouProvider(url =>
				{
					if (url.Contains("song_search_v2", StringComparison.Ordinal))
					{
						return Success(OneSongResponse);
					}
					if (url.Contains("lyrics.kugou.com/search", StringComparison.Ordinal))
					{
						Check.True(url.Contains("hash=" + ValidHash, StringComparison.Ordinal), "hash query");
						Check.True(url.Contains("duration=256000", StringComparison.Ordinal), "duration query");
						return Success(KrcCandidateResponse);
					}
					if (url.Contains("lyrics.kugou.com/download", StringComparison.Ordinal))
					{
						return Success(BuildDownloadResponse(0, KugouKrcFixtures.RealKrcContentBase64));
					}
					throw new TestFailure("unexpected request: " + url);
				});

				List<LyricSearchResult> lyrics = provider.SearchLyrics("query", 5, 4);
				Check.Equal(1, lyrics.Count, "count");
				Check.True(lyrics[0].Lyric.Contains("[00:22.144]", StringComparison.Ordinal), "high precision lyric");
				Check.Equal("1", lyrics[0].TrackId, "track id");
				Check.Equal("SongA", lyrics[0].Title, "title");
				Check.Equal(4, lyrics[0].SourceOrder, "source order");
				Check.Equal(0, provider.RequestedUrls.Count(url => url.Contains("m3ws.kugou.com", StringComparison.Ordinal)), "legacy request count");
			}
			finally
			{
				Settings.Default.LyricDownload_ReformatTimetag = originalSetting;
			}
		});

		yield return ("Kugou LoadLyricsForTrack uses the KRC path", delegate
		{
			ScriptedKugouProvider provider = new ScriptedKugouProvider(url =>
				url.Contains("/search?", StringComparison.Ordinal)
					? Success(KrcCandidateResponse)
					: Success(BuildDownloadResponse(0, KugouKrcFixtures.RealKrcContentBase64)));
			TrackSearchResult track = new TrackSearchResult
			{
				SourceTrackId = "track",
				Title = "Title",
				Artist = "Artist",
				Album = "Album",
				KugouHash = ValidHash,
				KugouDurationMs = 256000
			};
			LyricSearchResult lyric = provider.LoadLyricsForTrack(track);
			Check.NotNull(lyric, "lyric");
			Check.Equal("track", lyric.TrackId, "track id");
			Check.Equal(2, provider.RequestedUrls.Count, "request count");
		});

		yield return ("Kugou no KRC candidate falls back without tripping the circuit breaker", delegate
		{
			ScriptedKugouProvider provider = new ScriptedKugouProvider(url =>
			{
				if (url.Contains("song_search_v2", StringComparison.Ordinal))
				{
					return Success(TwoSongResponse);
				}
				if (url.Contains("lyrics.kugou.com/search", StringComparison.Ordinal))
				{
					return Success(EmptyKrcCandidateResponse);
				}
				return Success(LegacyLyricResponse);
			});

			List<LyricSearchResult> lyrics = provider.SearchLyrics("query", 5, 0);
			Check.Equal(2, lyrics.Count, "count");
			Check.Equal(2, provider.RequestedUrls.Count(url => url.Contains("lyrics.kugou.com/search", StringComparison.Ordinal)), "KRC search count");
			Check.Equal(2, provider.RequestedUrls.Count(url => url.Contains("m3ws.kugou.com", StringComparison.Ordinal)), "legacy count");
		});

		yield return ("Kugou endpoint transport failure trips one-search circuit breaker and falls back", delegate
		{
			ScriptedKugouProvider provider = new ScriptedKugouProvider(url =>
			{
				if (url.Contains("song_search_v2", StringComparison.Ordinal))
				{
					return Success(TwoSongResponse);
				}
				if (url.Contains("lyrics.kugou.com/search", StringComparison.Ordinal))
				{
					return new HttpResult { Error = RemoteErrorKind.Timeout, ErrorCode = "timeout" };
				}
				return Success(LegacyLyricResponse);
			});

			List<LyricSearchResult> lyrics = provider.SearchLyrics("query", 5, 0);
			Check.Equal(2, lyrics.Count, "count");
			Check.Equal(1, provider.RequestedUrls.Count(url => url.Contains("lyrics.kugou.com/search", StringComparison.Ordinal)), "KRC search count");
			Check.Equal(2, provider.RequestedUrls.Count(url => url.Contains("m3ws.kugou.com", StringComparison.Ordinal)), "legacy count");
		});

		yield return ("Kugou malformed candidate payload is per-song and does not trip circuit breaker", delegate
		{
			int downloadCount = 0;
			ScriptedKugouProvider provider = new ScriptedKugouProvider(url =>
			{
				if (url.Contains("song_search_v2", StringComparison.Ordinal))
				{
					return Success(TwoSongResponse);
				}
				if (url.Contains("lyrics.kugou.com/search", StringComparison.Ordinal))
				{
					return Success(KrcCandidateResponse);
				}
				if (url.Contains("lyrics.kugou.com/download", StringComparison.Ordinal))
				{
					downloadCount++;
					return Success(BuildDownloadResponse(0, downloadCount == 1 ? "bm90a3Jj" : KugouKrcFixtures.RealKrcContentBase64));
				}
				return Success(LegacyLyricResponse);
			});

			List<LyricSearchResult> lyrics = provider.SearchLyrics("query", 5, 0);
			Check.Equal(2, lyrics.Count, "count");
			Check.Equal(2, provider.RequestedUrls.Count(url => url.Contains("lyrics.kugou.com/search", StringComparison.Ordinal)), "KRC search count");
			Check.Equal(2, downloadCount, "download count");
			Check.Equal(1, provider.RequestedUrls.Count(url => url.Contains("m3ws.kugou.com", StringComparison.Ordinal)), "legacy count");
		});

		yield return ("Kugou malformed KRC and legacy response remains diagnosable", delegate
		{
			ScriptedKugouProvider provider = new ScriptedKugouProvider(url =>
			{
				if (url.Contains("song_search_v2", StringComparison.Ordinal))
				{
					return Success(OneSongResponse);
				}
				if (url.Contains("lyrics.kugou.com/search", StringComparison.Ordinal))
				{
					return Success(KrcCandidateResponse);
				}
				if (url.Contains("lyrics.kugou.com/download", StringComparison.Ordinal))
				{
					return Success(BuildDownloadResponse(0, "bm90a3Jj"));
				}
				return Success("<html>not json</html>");
			});

			List<LyricSearchResult> lyrics = provider.SearchLyrics("query", 5, 0);
			Check.Equal(0, lyrics.Count, "count");
			Check.NotNull(provider.LastTransportResult, "LastTransportResult");
			Check.Equal(RemoteErrorKind.ParseFailed, provider.LastTransportResult.Error, "Error");
			Check.Equal("parse", provider.LastTransportResult.ErrorCode, "ErrorCode");
		});

		yield return ("Kugou cancellation between KRC search and download stops all later requests", delegate
		{
			using CancellationTokenSource cancellation = new CancellationTokenSource();
			ScriptedKugouProvider provider = new ScriptedKugouProvider(url =>
			{
				cancellation.Cancel();
				return Success(KrcCandidateResponse);
			}, cancellation);
			LyricSearchResult lyric = provider.LoadLyricsForTrack(new TrackSearchResult
			{
				KugouHash = ValidHash,
				KugouDurationMs = 256000
			});
			Check.Null(lyric, "lyric");
			Check.Equal(1, provider.RequestedUrls.Count, "request count");
			Check.True(provider.RequestedUrls[0].Contains("lyrics.kugou.com/search", StringComparison.Ordinal), "canceled after KRC search");
		});

		yield return ("Kugou cancellation after KRC download failure does not call legacy", delegate
		{
			using CancellationTokenSource cancellation = new CancellationTokenSource();
			ScriptedKugouProvider provider = new ScriptedKugouProvider(url =>
			{
				if (url.Contains("/search?", StringComparison.Ordinal))
				{
					return Success(KrcCandidateResponse);
				}
				cancellation.Cancel();
				return Success(BuildDownloadResponse(0, "bm90a3Jj"));
			}, cancellation);
			LyricSearchResult lyric = provider.LoadLyricsForTrack(new TrackSearchResult
			{
				KugouHash = ValidHash,
				KugouDurationMs = 256000
			});
			Check.Null(lyric, "lyric");
			Check.Equal(2, provider.RequestedUrls.Count, "request count");
			Check.Equal(0, provider.RequestedUrls.Count(url => url.Contains("m3ws.kugou.com", StringComparison.Ordinal)), "legacy count");
		});
	}

	private static string BuildDownloadResponse(int contentType, string content)
	{
		return "{\"status\":200,\"contenttype\":" + contentType + ",\"content\":\"" + content + "\"}";
	}

	private static HttpResult Success(string body)
	{
		return new HttpResult { Body = body };
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
