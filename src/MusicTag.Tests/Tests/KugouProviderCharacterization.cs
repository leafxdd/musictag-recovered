using System;
using System.Collections.Generic;
using MusicTag.Candidates;
using MusicTag.Serialization;
using MusicTagWinApp.Roles;
using MusicTagWinApp.Web;

namespace MusicTag.Tests;

// Kugou provider 解析 characterization。注入点：override protected virtual GetResponseString
//（Kugou SearchSongs 走 GET）喂录制 JSON fixture。
// Kugou 特有：仅 lyrics+tracks、**NO covers**（BuildTrackResult 不设 track.Cover，恒 null）；
// DurationMs = duration 秒 * 1000；设 KugouHash/KugouDurationMs；按 audio_id 非空过滤。
internal static class KugouProviderCharacterization
{
	private sealed class StubKugouProvider : KugouTagProvider
	{
		private readonly string response;

		// KugouTagProvider 无无参构造，只有 base(CancellationTokenSource)。
		public StubKugouProvider(string response)
			: base(null)
		{
			this.response = response;
		}

		protected override string GetResponseString(string url)
		{
			return response;
		}
	}

	private static List<TrackSearchResult> SearchTracks(string response)
	{
		StubKugouProvider provider = new StubKugouProvider(response);
		return provider.SearchTracks("query", 10, 0, 0, new List<TrackSearchResult>(), new List<TrackSearchResult>());
	}

	private const string OneSongResponse =
		"{\"data\":{\"info\":[" +
		"{\"audio_id\":\"A123\",\"songname\":\"SongA\",\"singername\":\"ArtistA\",\"album_name\":\"AlbumA\",\"hash\":\"HASH1\",\"duration\":240}" +
		"]}}";

	public static IEnumerable<(string, Action)> All()
	{
		yield return ("Kugou.SearchTracks parses one song (typical, no cover)", delegate
		{
			List<TrackSearchResult> tracks = SearchTracks(OneSongResponse);
			Check.Equal(1, tracks.Count, "count");
			Check.Equal("A123", tracks[0].SourceTrackId, "[0].SourceTrackId");
			Check.Equal("SongA", tracks[0].Title, "[0].Title");
			Check.Equal("ArtistA", tracks[0].Artist, "[0].Artist");
			Check.Equal("AlbumA", tracks[0].Album, "[0].Album");
			Check.Equal("HASH1", tracks[0].KugouHash, "[0].KugouHash");
			// duration 秒 * 1000 -> 毫秒
			Check.Equal(240000, tracks[0].KugouDurationMs, "[0].KugouDurationMs");
			Check.Equal(0, tracks[0].ResultOrder, "[0].ResultOrder");
			Check.Equal(SearchSource.Kugou, tracks[0].SearchSource, "[0].SearchSource");
			// Kugou 特有：无封面，BuildTrackResult 不设 track.Cover。
			Check.Null(tracks[0].Cover, "[0].Cover");
		});

		yield return ("Kugou.SearchTracks filters song missing audio_id", delegate
		{
			string response =
				"{\"data\":{\"info\":[" +
				"{\"audio_id\":\"A123\",\"songname\":\"SongA\",\"singername\":\"ArtistA\",\"album_name\":\"AlbumA\",\"hash\":\"HASH1\",\"duration\":240}," +
				"{\"songname\":\"SongB\",\"singername\":\"ArtistB\",\"album_name\":\"AlbumB\",\"hash\":\"HASH2\",\"duration\":100}" +
				"]}}";
			List<TrackSearchResult> tracks = SearchTracks(response);
			Check.Equal(1, tracks.Count, "count");
			Check.Equal("A123", tracks[0].SourceTrackId, "[0].SourceTrackId");
		});

		yield return ("Kugou.SearchTracks empty info -> 0 tracks", delegate
		{
			List<TrackSearchResult> tracks = SearchTracks("{\"data\":{\"info\":[]}}");
			Check.Equal(0, tracks.Count, "count");
		});

		yield return ("Kugou.SearchTracks HTTP-200 unparseable -> ParseFailed", delegate
		{
			StubKugouProvider provider = new StubKugouProvider("<html>not json</html>");
			List<TrackSearchResult> tracks = provider.SearchTracks("query", 10, 0, 0, new List<TrackSearchResult>(), new List<TrackSearchResult>());
			Check.Equal(0, tracks.Count, "count");
			Check.NotNull(provider.LastTransportResult, "LastTransportResult");
			Check.Equal(RemoteErrorKind.ParseFailed, provider.LastTransportResult.Error, "Error");
		});
	}
}
