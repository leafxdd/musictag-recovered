using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using MusicTag.Serialization;
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

		public StubQqProvider(string response, CancellationTokenSource cancellation = null)
			: base(cancellation)
		{
			this.response = response;
		}

		protected override string PostString(string url, string body, HttpClient client = null, bool postJson = false)
		{
			return response;
		}
	}

	private static List<TrackSearchResult> SearchTracks(string response)
	{
		StubQqProvider provider = new StubQqProvider(response);
		return provider.SearchTracks("query", 10, 0, 0, new List<TrackSearchResult>(), new List<TrackSearchResult>());
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
			// 首次 Retrying 上报即取消：WaitOne 立即返回、下轮循环开头取消短路，避免真实指数退避等待（2/4/8… 秒）。
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
	}
}
