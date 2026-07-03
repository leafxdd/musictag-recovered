using System;
using System.Collections.Generic;
using System.Net.Http;
using MusicTag.Serialization;
using MusicTagWinApp.Exporters;
using MusicTagWinApp.Roles;
using MusicTagWinApp.Web;

namespace MusicTag.Tests;

// 首个 provider 解析 characterization：经测试子类 override protected virtual PostString
// 喂录制 JSON fixture，从 public SearchTracks 端到端驱动 NetEase 真实解析链
//（NetEaseCrypto 加密 → JObject.Parse → 字段映射 → 去重 / 排序 → ParseFailed 回填），
// 锁定「当前实际行为」golden master。无 mock HttpClient、无反射、不联网。
// 注意：CommentTagWrite163Key 默认 False（musictag/MusicTag.config），故 Comment 走别名文本分支。
internal static class NetEaseProviderCharacterization
{
	// override PostString 返回固定响应：SearchSongs→PostSongQuery→PostString 被截流，
	// CreateHttpClient / GetHttpClient 永不触发，故不发起任何网络。
	private sealed class StubNetEaseProvider : NetEaseMusicTagProvider
	{
		private readonly string response;

		public StubNetEaseProvider(string response)
			: base(null)
		{
			this.response = response;
		}

		protected override string PostString(string url, string body, HttpClient client = null, bool postJson = false)
		{
			return response;
		}
	}

	private static List<TrackSearchResult> SearchTracks(StubNetEaseProvider provider)
	{
		return provider.SearchTracks("query", 10, 0L, 0, 0, new List<TrackSearchResult>(), new List<TrackSearchResult>());
	}

	private const string TwoSongsResponse =
		"{\"result\":{\"songs\":[" +
		"{\"id\":111,\"name\":\"SongA\",\"ar\":[{\"id\":1,\"name\":\"ArtistA\"}],\"al\":{\"id\":10,\"name\":\"AlbumA\",\"picUrl\":\"http://p/a.jpg\"},\"alia\":[\"AliasA\"],\"publishTime\":1262304000000}," +
		"{\"id\":222,\"name\":\"SongB\",\"ar\":[{\"id\":2,\"name\":\"ArtistB\"}],\"al\":{\"id\":20,\"name\":\"AlbumB\"}}" +
		"]}}";

	public static IEnumerable<(string, Action)> All()
	{
		yield return ("NetEase.SearchTracks parses two songs (typical)", delegate
		{
			List<TrackSearchResult> tracks = SearchTracks(new StubNetEaseProvider(TwoSongsResponse));
			Check.Equal(2, tracks.Count, "count");
			Check.Equal("111", tracks[0].SourceTrackId, "[0].SourceTrackId");
			Check.Equal("SongA", tracks[0].Title, "[0].Title");
			Check.Equal("ArtistA", tracks[0].Artist, "[0].Artist");
			Check.Equal("AlbumA", tracks[0].Album, "[0].Album");
			Check.Equal("2010", tracks[0].Year, "[0].Year");
			Check.Equal(0, tracks[0].ResultOrder, "[0].ResultOrder");
			Check.Equal(SearchSource.Music163, tracks[0].SearchSource, "[0].SearchSource");
			Check.Equal("222", tracks[1].SourceTrackId, "[1].SourceTrackId");
			Check.Equal("SongB", tracks[1].Title, "[1].Title");
			Check.Equal(1, tracks[1].ResultOrder, "[1].ResultOrder");
			// CommentTagWrite163Key=False → Comment 取别名文本（GetAliasCommentText，\n 连接）。
			Check.Equal("AliasA", tracks[0].Comment, "[0].Comment");
			Check.Equal("", tracks[1].Comment, "[1].Comment");
		});

		yield return ("NetEase.SearchTracks empty result -> 0 tracks", delegate
		{
			List<TrackSearchResult> tracks = SearchTracks(new StubNetEaseProvider("{\"result\":{\"songs\":[]}}"));
			Check.Equal(0, tracks.Count, "count");
		});

		yield return ("NetEase.SearchTracks dedups identical id", delegate
		{
			string dup =
				"{\"result\":{\"songs\":[" +
				"{\"id\":111,\"name\":\"SongA\",\"ar\":[{\"id\":1,\"name\":\"ArtistA\"}],\"al\":{\"id\":10,\"name\":\"AlbumA\"}}," +
				"{\"id\":111,\"name\":\"SongA-dup\",\"ar\":[{\"id\":1,\"name\":\"ArtistA\"}],\"al\":{\"id\":10,\"name\":\"AlbumA\"}}" +
				"]}}";
			List<TrackSearchResult> tracks = SearchTracks(new StubNetEaseProvider(dup));
			Check.Equal(1, tracks.Count, "count");
			Check.Equal("111", tracks[0].SourceTrackId, "[0].SourceTrackId");
		});

		yield return ("NetEase.SearchTracks HTTP-200 unparseable -> ParseFailed", delegate
		{
			StubNetEaseProvider provider = new StubNetEaseProvider("<html>not json</html>");
			List<TrackSearchResult> tracks = SearchTracks(provider);
			Check.Equal(0, tracks.Count, "count");
			Check.NotNull(provider.LastTransportResult, "LastTransportResult");
			Check.Equal(RemoteErrorKind.ParseFailed, provider.LastTransportResult.Error, "Error");
		});
	}
}
