using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using MusicTag.Serialization;
using MusicTag.States;
using MusicTagWinApp.Containers;
using MusicTagWinApp.Properties;
using MusicTagWinApp.Roles;
using MusicTagWinApp.Web;
using MusicTagWinApp.Writers;

namespace MusicTag.Tests;

// QQ provider 解析 characterization。注入点同 NetEase：override protected virtual PostString
// 喂录制 JSON fixture（QQ SearchSongs 走 PostString postJson=true）。
// QQ 特有：空 album guard（Album.Id>0 && Mid 非空 && Name 非空才入候选）、title/name 双字段
//（Title←"title"、OriginalTitle←"name"）。限流 2001 重试见 QqRateLimit 测试（单列，避免真实退避等待）。
internal static class QqProviderCharacterization
{
	private sealed class StubQqProvider : QqMusicTagProvider
	{
		private readonly string response;

		public string LastRequestBody { get; private set; }

		public StubQqProvider(string response, CancellationTokenSource cancellation = null)
			: base(cancellation)
		{
			this.response = response;
		}

		protected override string PostString(string url, string body, HttpClient client = null, bool postJson = false)
		{
			LastRequestBody = body;
			return response;
		}

		protected override bool UseSharedRequestCoordination => false;
	}

	private sealed class CoordinatedStubQqProvider : QqMusicTagProvider
	{
		public int RequestCount { get; private set; }

		protected override string PostString(string url, string body, HttpClient client = null, bool postJson = false)
		{
			RequestCount++;
			return OneSongResponse;
		}
	}

	private sealed class StubTrackSearchProvider : ITrackSearchProvider
	{
		private readonly Queue<HttpResult> transportResults;

		public List<(string query, int resultLimit, long knownSongId, int queryPass, int sourceOrder)> Calls { get; } = new List<(string, int, long, int, int)>();

		public HttpResult LastTransportResult { get; private set; }

		public StubTrackSearchProvider(params HttpResult[] transportResults)
		{
			this.transportResults = new Queue<HttpResult>(transportResults);
		}

		public List<TrackSearchResult> SearchTracks(string query, int resultLimit, long knownSongId, int searchPass, int sourceOrder, List<TrackSearchResult> existingTracks, List<TrackSearchResult> previousResults)
		{
			Calls.Add((query, resultLimit, knownSongId, searchPass, sourceOrder));
			LastTransportResult = transportResults.Count > 0 ? transportResults.Dequeue() : new HttpResult();
			return new List<TrackSearchResult>();
		}

		public void Dispose()
		{
		}
	}

	private static List<TrackSearchResult> SearchTracks(string response)
	{
		StubQqProvider provider = new StubQqProvider(response);
		return provider.SearchTracks("query", 10, 0, 0, new List<TrackSearchResult>(), new List<TrackSearchResult>());
	}

	private static void WithFullSearchContext(Action<TrackSearchContext> test)
	{
		bool useOnlyFilename = Settings.Default.SearchCondition_UseOnlyFilename;
		bool useArtist = Settings.Default.SearchCondition_UseArtist;
		bool useAlbum = Settings.Default.SearchCondition_UseAlbum;
		try
		{
			Settings.Default.SearchCondition_UseOnlyFilename = false;
			Settings.Default.SearchCondition_UseArtist = true;
			Settings.Default.SearchCondition_UseAlbum = true;
			using ConfigDescriptorState tagState = new ConfigDescriptorState();
			test(new TrackSearchContext(tagState, "Title", "Artist", "Album"));
		}
		finally
		{
			Settings.Default.SearchCondition_UseOnlyFilename = useOnlyFilename;
			Settings.Default.SearchCondition_UseArtist = useArtist;
			Settings.Default.SearchCondition_UseAlbum = useAlbum;
		}
	}

	// 一首完整 album 的歌（通过 guard）。
	private const string OneSongResponse =
		"{\"req_0\":{\"code\":0,\"data\":{\"body\":{\"song\":{\"list\":[" +
		"{\"id\":555,\"mid\":\"M555\",\"name\":\"NameA\",\"title\":\"TitleA\",\"subtitle\":\"SubA\",\"time_public\":\"2015-03-01\",\"index_album\":3,\"index_cd\":1,\"singer\":[{\"name\":\"SingerA\"}],\"album\":{\"id\":99,\"mid\":\"ALB99\",\"name\":\"AlbumA\"}}" +
		"]}}}}}";

	// 一有效 + 一空 album（后者应被 guard 过滤）。
	private const string OneValidOneIncompleteResponse =
		"{\"req_0\":{\"code\":0,\"data\":{\"body\":{\"song\":{\"list\":[" +
		"{\"id\":555,\"mid\":\"M555\",\"name\":\"NameA\",\"title\":\"TitleA\",\"subtitle\":\"SubA\",\"singer\":[{\"name\":\"SingerA\"}],\"album\":{\"id\":99,\"mid\":\"ALB99\",\"name\":\"AlbumA\"}}," +
		"{\"id\":666,\"mid\":\"M666\",\"name\":\"NameB\",\"title\":\"TitleB\",\"subtitle\":\"\",\"singer\":[{\"name\":\"SingerB\"}],\"album\":{\"id\":0,\"mid\":\"\",\"name\":\"\"}}" +
		"]}}}}}";

	public static IEnumerable<(string, Action)> All()
	{
		yield return ("QQ Cookie validation accepts empty optional Cookie", delegate
		{
			QqMusicCookieValidationResult result = QqMusicCookieValidator.Validate("  ");
			Check.True(result.IsValid, "empty cookie is valid");
			Check.True(result.IsEmpty, "empty cookie flag");
		});

		yield return ("QQ Cookie validation accepts browser Uin casing and authst", delegate
		{
			QqMusicCookieValidationResult result = QqMusicCookieValidator.Validate("Uin=123456; authst=token");
			Check.True(result.IsValid, "browser cookie casing");
			Check.Equal(0, result.MissingFields.Count, "missing fields");
		});

		yield return ("QQ Cookie validation accepts a Cookie header prefix", delegate
		{
			QqMusicCookieValidationResult result = QqMusicCookieValidator.Validate("Cookie: uin=123456; qm_keyst=token");
			Check.True(result.IsValid, "Cookie header prefix");
		});

		yield return ("QQ Cookie validation reports missing account and auth fields", delegate
		{
			QqMusicCookieValidationResult result = QqMusicCookieValidator.Validate("foo=bar");
			Check.True(!result.IsValid, "invalid cookie");
			Check.Equal(2, result.MissingFields.Count, "missing field count");
			Check.Equal("uin/p_uin/euin", result.MissingFields[0], "account field");
			Check.Equal("authst/qm_keyst/qqmusic_key", result.MissingFields[1], "auth field");
		});

		yield return ("QQ request coordinator enters cooldown after 2001", delegate
		{
			QqRequestCoordinator.ResetForTests();
			try
			{
				int ignoredSeconds;
				Check.Equal(QqRequestPermit.Granted, QqRequestCoordinator.WaitForPermit(CancellationToken.None, out ignoredSeconds), "initial permit");
				int cooldownSeconds = QqRequestCoordinator.RecordRateLimited();
				Check.True(cooldownSeconds >= 59, "cooldown seconds");
				Check.Equal(QqRequestPermit.CoolingDown, QqRequestCoordinator.WaitForPermit(CancellationToken.None, out ignoredSeconds), "cooldown permit");
			}
			finally
			{
				QqRequestCoordinator.ResetForTests();
			}
		});

		yield return ("QQ.SearchTracks parses one complete song (typical)", delegate
		{
			List<TrackSearchResult> tracks = SearchTracks(OneSongResponse);
			Check.Equal(1, tracks.Count, "count");
			Check.Equal("555", tracks[0].SourceTrackId, "[0].SourceTrackId");
			Check.Equal("M555", tracks[0].QqMusicMid, "[0].QqMusicMid");
			Check.Equal("TitleA", tracks[0].Title, "[0].Title");
			Check.Equal("NameA", tracks[0].OriginalTitle, "[0].OriginalTitle");
			Check.Equal("AlbumA", tracks[0].Album, "[0].Album");
			Check.Equal("SingerA", tracks[0].Artist, "[0].Artist");
			Check.Equal("SubA", tracks[0].Comment, "[0].Comment");
			Check.Equal("2015", tracks[0].Year, "[0].Year");
			Check.Equal(0, tracks[0].ResultOrder, "[0].ResultOrder");
			Check.Equal(SearchSource.QQ, tracks[0].SearchSource, "[0].SearchSource");
		});

		yield return ("QQ search maps configured Cookie login fields into request context", delegate
		{
			string previousCookie = Settings.Default.QQMusic_Cookie;
			try
			{
				Settings.Default.QQMusic_Cookie = "uin=123456; authst=login-token; tmeLoginType=2; unrelated=value";
				StubQqProvider provider = new StubQqProvider("{\"req_0\":{\"code\":0,\"data\":{\"body\":{\"song\":{\"list\":[]}}}}}");
				provider.SearchTracks("query", 10, 0, 0, new List<TrackSearchResult>(), new List<TrackSearchResult>());
				Newtonsoft.Json.Linq.JObject request = Newtonsoft.Json.Linq.JObject.Parse(provider.LastRequestBody);
				Check.Equal("123456", request["loginUin"]?.ToString(), "loginUin");
				Check.Equal("123456", request["comm"]?["uin"]?.ToString(), "comm.uin");
				Check.Equal("login-token", request["comm"]?["authst"]?.ToString(), "comm.authst");
				Check.Equal("2", request["comm"]?["tmeLoginType"]?.ToString(), "comm.tmeLoginType");
			}
			finally
			{
				Settings.Default.QQMusic_Cookie = previousCookie;
			}
		});

		yield return ("QQ coordinated search caches the same successful query", delegate
		{
			QqRequestCoordinator.ResetForTests();
			try
			{
				CoordinatedStubQqProvider provider = new CoordinatedStubQqProvider();
				string query = "cache-test-" + Guid.NewGuid().ToString("N");
				provider.SearchTracks(query, 10, 0, 0, new List<TrackSearchResult>(), new List<TrackSearchResult>());
				provider.SearchTracks(query, 10, 0, 0, new List<TrackSearchResult>(), new List<TrackSearchResult>());
				Check.Equal(1, provider.RequestCount, "request count");
			}
			finally
			{
				QqRequestCoordinator.ResetForTests();
			}
		});

		yield return ("QQ.SearchTracks filters song with incomplete album (guard)", delegate
		{
			List<TrackSearchResult> tracks = SearchTracks(OneValidOneIncompleteResponse);
			Check.Equal(1, tracks.Count, "count");
			Check.Equal("555", tracks[0].SourceTrackId, "[0].SourceTrackId");
		});

		yield return ("QQ.SearchTracks empty list -> 0 tracks", delegate
		{
			List<TrackSearchResult> tracks = SearchTracks("{\"req_0\":{\"code\":0,\"data\":{\"body\":{\"song\":{\"list\":[]}}}}}");
			Check.Equal(0, tracks.Count, "count");
		});

		yield return ("QQ.SearchTracks HTTP-200 unparseable -> ParseFailed", delegate
		{
			StubQqProvider provider = new StubQqProvider("<html>not json</html>");
			List<TrackSearchResult> tracks = provider.SearchTracks("query", 10, 0, 0, new List<TrackSearchResult>(), new List<TrackSearchResult>());
			Check.Equal(0, tracks.Count, "count");
			Check.NotNull(provider.LastTransportResult, "LastTransportResult");
			Check.Equal(RemoteErrorKind.ParseFailed, provider.LastTransportResult.Error, "Error");
		});

		yield return ("QQ.SearchTracks rate-limited (2001) reports Retrying then stops", delegate
		{
			CancellationTokenSource cts = new CancellationTokenSource();
			List<SourceSearchStatus> statuses = new List<SourceSearchStatus>();
			StubQqProvider provider = new StubQqProvider("{\"req_0\":{\"code\":2001}}", cts);
			// 首次 Retrying 上报即取消：WaitOne 立即返回、下轮循环开头取消短路，避免真实退避等待。
			provider.StatusReporter = delegate(SourceSearchStatus status)
			{
				statuses.Add(status);
				cts.Cancel();
			};
			List<TrackSearchResult> tracks = provider.SearchTracks("query", 10, 0, 0, new List<TrackSearchResult>(), new List<TrackSearchResult>());
			Check.Equal(0, tracks.Count, "count");
			Check.True(statuses.Count >= 1, "received status");
			Check.Equal(SourceSearchPhase.Retrying, statuses[0].Phase, "phase");
			Check.Equal("2001", statuses[0].ErrorCode, "errorCode");
		});

		yield return ("QQ combined search preserves all three fallback queries after empty success", delegate
		{
			WithFullSearchContext(delegate(TrackSearchContext context)
			{
				using CancellationTokenSource cts = new CancellationTokenSource();
				StubTrackSearchProvider provider = new StubTrackSearchProvider(new HttpResult(), new HttpResult(), new HttpResult());
				using QqCombinedTrackSearch search = new QqCombinedTrackSearch(cts, provider);

				search.SearchTracks(false, new List<TrackSearchResult>(), 7, context);

				Check.Equal(3, provider.Calls.Count, "call count");
				Check.Equal("Title Artist", provider.Calls[0].query, "query 1");
				Check.Equal("Title", provider.Calls[1].query, "query 2");
				Check.Equal("Album Artist", provider.Calls[2].query, "query 3");
				Check.Equal(15, provider.Calls[0].resultLimit, "result limit 1");
				Check.Equal(10, provider.Calls[1].resultLimit, "result limit 2");
				Check.Equal(8, provider.Calls[2].resultLimit, "result limit 3");
				Check.Equal(0L, provider.Calls[0].knownSongId, "known song id");
				Check.Equal(0, provider.Calls[0].queryPass, "query pass 1");
				Check.Equal(1, provider.Calls[1].queryPass, "query pass 2");
				Check.Equal(2, provider.Calls[2].queryPass, "query pass 3");
				Check.Equal(7, provider.Calls[0].sourceOrder, "source order");
			});
		});

		yield return ("QQ combined search stops fallback queries after first rate limit", delegate
		{
			WithFullSearchContext(delegate(TrackSearchContext context)
			{
				using CancellationTokenSource cts = new CancellationTokenSource();
				StubTrackSearchProvider provider = new StubTrackSearchProvider(new HttpResult { Error = RemoteErrorKind.RateLimited, ErrorCode = "2001" });
				using QqCombinedTrackSearch search = new QqCombinedTrackSearch(cts, provider);

				search.SearchTracks(false, new List<TrackSearchResult>(), 0, context);

				Check.Equal(1, provider.Calls.Count, "call count");
				Check.Equal(RemoteErrorKind.RateLimited, search.LastTransportResult.Error, "final error");
			});
		});

		yield return ("QQ combined search stops third query after second-query rate limit", delegate
		{
			WithFullSearchContext(delegate(TrackSearchContext context)
			{
				using CancellationTokenSource cts = new CancellationTokenSource();
				StubTrackSearchProvider provider = new StubTrackSearchProvider(
					new HttpResult(),
					new HttpResult { Error = RemoteErrorKind.RateLimited, ErrorCode = "2001" });
				using QqCombinedTrackSearch search = new QqCombinedTrackSearch(cts, provider);

				search.SearchTracks(false, new List<TrackSearchResult>(), 0, context);

				Check.Equal(2, provider.Calls.Count, "call count");
				Check.Equal(RemoteErrorKind.RateLimited, search.LastTransportResult.Error, "final error");
			});
		});
	}
}
