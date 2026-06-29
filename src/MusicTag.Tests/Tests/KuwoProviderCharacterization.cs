using System;
using System.Collections.Generic;
using MusicTag.Serialization;
using MusicTagWinApp.Adapter;
using MusicTagWinApp.Roles;
using MusicTagWinApp.Web;

namespace MusicTag.Tests;

// Kuwo provider 解析 characterization。注入点：override protected virtual GetResponseString
//（Kuwo SearchSongs 走 GET，区别于 NetEase/QQ 的 PostString）喂录制 JSON fixture。
// Kuwo 特有：TrackId 去 "MUSIC_" 前缀（MUSICRID）、album-first / fallback 选取、
// Title&Artist 必须非空才入候选。CreateTrackResult 不设 Year/Comment/Track（与 NetEase/QQ 不同）。
internal static class KuwoProviderCharacterization
{
	private sealed class StubKuwoProvider : KuwoTagProvider
	{
		private readonly string response;

		public StubKuwoProvider(string response)
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
		StubKuwoProvider provider = new StubKuwoProvider(response);
		return provider.SearchTracks("query", 10, 0, 0, new List<TrackSearchResult>(), new List<TrackSearchResult>());
	}

	private const string OneSongResponse =
		"{\"abslist\":[" +
		"{\"SONGNAME\":\"SongA\",\"ARTIST\":\"ArtistA\",\"ALBUM\":\"AlbumA\",\"MUSICRID\":\"MUSIC_12345\",\"ARTISTID\":\"777\",\"NAME\":\"NameA\",\"web_albumpic_short\":\"120/s3/93/x.jpg\"}" +
		"]}";

	public static IEnumerable<(string, Action)> All()
	{
		yield return ("Kuwo.SearchTracks parses one song with album (typical)", delegate
		{
			List<TrackSearchResult> tracks = SearchTracks(OneSongResponse);
			Check.Equal(1, tracks.Count, "count");
			// MUSICRID="MUSIC_12345" -> TrackId 去 "MUSIC_" 前缀 -> "12345"
			Check.Equal("12345", tracks[0].SourceTrackId, "[0].SourceTrackId");
			Check.Equal("SongA", tracks[0].Title, "[0].Title");
			Check.Equal("NameA", tracks[0].OriginalTitle, "[0].OriginalTitle");
			Check.Equal("ArtistA", tracks[0].Artist, "[0].Artist");
			Check.Equal("AlbumA", tracks[0].Album, "[0].Album");
			Check.Equal(0, tracks[0].ResultOrder, "[0].ResultOrder");
			Check.Equal(SearchSource.Kuwo, tracks[0].SearchSource, "[0].SearchSource");
		});

		yield return ("Kuwo.SearchTracks filters song missing artist", delegate
		{
			string response =
				"{\"abslist\":[" +
				"{\"SONGNAME\":\"SongA\",\"ARTIST\":\"ArtistA\",\"ALBUM\":\"AlbumA\",\"MUSICRID\":\"MUSIC_12345\"}," +
				"{\"SONGNAME\":\"SongB\",\"ARTIST\":\"\",\"ALBUM\":\"AlbumB\",\"MUSICRID\":\"MUSIC_222\"}" +
				"]}";
			List<TrackSearchResult> tracks = SearchTracks(response);
			Check.Equal(1, tracks.Count, "count");
			Check.Equal("12345", tracks[0].SourceTrackId, "[0].SourceTrackId");
		});

		yield return ("Kuwo.SearchTracks no-album no-sublist -> fallback result", delegate
		{
			// 无 ALBUM 且无 SUBLIST -> 进 fallbackResults；albumResults 为空时返回 fallback。
			string response =
				"{\"abslist\":[" +
				"{\"SONGNAME\":\"SongC\",\"ARTIST\":\"ArtistC\",\"ALBUM\":\"\",\"MUSICRID\":\"MUSIC_333\",\"NAME\":\"NameC\"}" +
				"]}";
			List<TrackSearchResult> tracks = SearchTracks(response);
			Check.Equal(1, tracks.Count, "count");
			Check.Equal("333", tracks[0].SourceTrackId, "[0].SourceTrackId");
			Check.Equal("", tracks[0].Album, "[0].Album");
		});

		yield return ("Kuwo.SearchTracks empty abslist -> 0 tracks", delegate
		{
			List<TrackSearchResult> tracks = SearchTracks("{\"abslist\":[]}");
			Check.Equal(0, tracks.Count, "count");
		});

		yield return ("Kuwo.SearchTracks HTTP-200 unparseable -> ParseFailed", delegate
		{
			StubKuwoProvider provider = new StubKuwoProvider("<html>not json</html>");
			List<TrackSearchResult> tracks = provider.SearchTracks("query", 10, 0, 0, new List<TrackSearchResult>(), new List<TrackSearchResult>());
			Check.Equal(0, tracks.Count, "count");
			Check.NotNull(provider.LastTransportResult, "LastTransportResult");
			Check.Equal(RemoteErrorKind.ParseFailed, provider.LastTransportResult.Error, "Error");
		});
	}
}
