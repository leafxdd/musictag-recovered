using System;
using System.Collections.Generic;
using System.Net.Http;
using MusicTag.Candidates;
using MusicTag.Serialization;
using MusicTagWinApp.Adapter;
using MusicTagWinApp.Exporters;
using MusicTagWinApp.Roles;
using MusicTagWinApp.Web;
using MusicTagWinApp.Writers;

namespace MusicTag.Tests;

internal static class TrackIdLookupCharacterization
{
	private const string NetEaseDetailResponse =
		"{\"songs\":[{\"id\":111,\"name\":\"SongA\",\"ar\":[{\"id\":1,\"name\":\"ArtistA\"}],\"al\":{\"id\":10,\"name\":\"AlbumA\",\"picUrl\":\"http://p/a.jpg\",\"publishTime\":1262304000000},\"alia\":[\"AliasA\"],\"no\":3,\"cd\":\"2\"}]}";

	private const string QqDetailResponse =
		"{\"code\":0,\"data\":[{\"id\":449198,\"mid\":\"003cI52o4daJJL\",\"name\":\"花海\",\"title\":\"花海\",\"subtitle\":\"\",\"time_public\":\"2008-10-15\",\"index_album\":4,\"index_cd\":1,\"singer\":[{\"name\":\"周杰伦\"}],\"album\":{\"id\":234,\"mid\":\"002Neh8l0uciQZ\",\"name\":\"魔杰座\"}}]}";

	private const string KuwoDetailResponse =
		"[{\"id\":\"228908\",\"name\":\"晴天\",\"artist\":\"周杰伦\",\"album\":\"叶惠美\",\"artistid\":\"336\",\"albumpic\":\"http://img2.kuwo.cn/star/albumcover/120/s3s94/93/211513640.jpg\",\"duration\":\"269\"}]";

	private const string KugouDetailResponse =
		"{\"hash\":\"514AF2D2B993E1A4BFFD0DF3EDA31874\",\"author_name\":\"周杰伦\",\"status\":1,\"audio_id\":460895122,\"errcode\":0,\"fileName\":\"周杰伦 - 花海 (DJ 阿若版)\",\"songName\":\"花海\",\"timeLength\":210,\"albumid\":167998403,\"album_img\":\"http://imge.kugou.com/stdmusic/{size}/cover.jpg\"}";

	private const string KugouAlbumResponse =
		"{\"data\":{\"albumid\":167998403,\"albumname\":\"魔杰座 DJ 阿若版\"},\"errcode\":0,\"status\":1}";

	private const string KugouEntityResponse =
		"{\"msg\":\"\",\"data\":[{\"__status\":1,\"base\":{\"songname\":\"花海 (DJ 阿若版)\",\"author_name\":\"周杰伦\",\"album_name\":\"魔杰座 DJ 阿若版\",\"publish_date\":\"2025-05-22\",\"audio_id\":460895122,\"album_audio_id\":756464249},\"audio_info\":{\"timelength\":210468,\"hash\":\"514AF2D2B993E1A4BFFD0DF3EDA31874\"},\"album_info\":{\"album_name\":\"魔杰座 DJ 阿若版\",\"publish_date\":\"2025-05-22\",\"cover\":\"http://imge.kugou.com/stdmusic/{size}/cover.jpg\"}}],\"status\":1,\"error_code\":0}";

	private sealed class StubNetEaseProvider : NetEaseMusicTagProvider
	{
		protected override string PostString(string url, string body, HttpClient client = null, bool postJson = false)
		{
			return NetEaseDetailResponse;
		}
	}

	private sealed class StubNetEaseNoCoverProvider : NetEaseMusicTagProvider
	{
		protected override string PostString(string url, string body, HttpClient client = null, bool postJson = false)
		{
			return NetEaseDetailResponse.Replace("http://p/a.jpg", "");
		}
	}

	private sealed class StubQqProvider : QqMusicTagProvider
	{
		public string RequestedUrl;

		protected override string GetResponseString(string url)
		{
			RequestedUrl = url;
			return QqDetailResponse;
		}
	}

	private sealed class StubKuwoProvider : KuwoTagProvider
	{
		protected override string GetResponseString(string url)
		{
			return KuwoDetailResponse;
		}
	}

	private sealed class StubKugouProvider : KugouTagProvider
	{
		protected override string GetResponseString(string url)
		{
			return url.Contains("album/info") ? KugouAlbumResponse : KugouDetailResponse;
		}
	}

	private sealed class StubNumericKugouProvider : KugouTagProvider
	{
		public string RequestedUrl;

		public string RequestBody;

		protected override string PostString(string url, string body, HttpClient client = null, bool postJson = false)
		{
			RequestedUrl = url;
			RequestBody = body;
			return KugouEntityResponse;
		}
	}

	public static IEnumerable<(string, Action)> All()
	{
		yield return ("TrackIdInput normalizes provider IDs and links", delegate
		{
			Check.True(TrackIdInput.TryNormalize(SearchSource.Music163, "https://music.163.com/#/song?id=111", out string netEaseId), "NetEase link valid");
			Check.Equal("111", netEaseId, "NetEase id");
			Check.True(TrackIdInput.TryNormalize(SearchSource.QQ, "https://y.qq.com/n/ryqq/songDetail/003cI52o4daJJL", out string qqMid), "QQ link valid");
			Check.Equal("003cI52o4daJJL", qqMid, "QQ mid");
			Check.True(TrackIdInput.TryNormalize(SearchSource.QQ, "449198", out string qqId), "QQ numeric id valid");
			Check.Equal("449198", qqId, "QQ numeric id");
			Check.True(TrackIdInput.TryNormalize(SearchSource.Kuwo, "https://www.kuwo.cn/play_detail/228908", out string kuwoId), "Kuwo link valid");
			Check.Equal("228908", kuwoId, "Kuwo id");
			Check.True(TrackIdInput.TryNormalize(SearchSource.Kugou, "https://www.kugou.com/song/#hash=514af2d2b993e1a4bffd0df3eda31874", out string kugouHash), "Kugou link valid");
			Check.Equal("514AF2D2B993E1A4BFFD0DF3EDA31874", kugouHash, "Kugou hash normalized");
			Check.True(TrackIdInput.TryNormalize(SearchSource.Kugou, "https://www.kugou.com/mixsong/756464249.html", out string kugouMixSongId), "Kugou MixSongID link valid");
			Check.Equal("756464249", kugouMixSongId, "Kugou MixSongID");
		});

		yield return ("NetEase.LookupTrackById builds complete exact result", delegate
		{
			TrackSearchResult track = new StubNetEaseProvider().LookupTrackById("111", 2);
			Check.NotNull(track, "track");
			Check.Equal("111", track.SourceTrackId, "id");
			Check.Equal("SongA", track.Title, "title");
			Check.Equal("ArtistA", track.Artist, "artist");
			Check.Equal("AlbumA", track.Album, "album");
			Check.Equal("2010", track.Year, "year");
			Check.Equal(3, track.Track, "track number");
			Check.Equal(2, track.Disc, "disc number");
			Check.NotNull(track.Cover, "cover");
			Check.NotNull(track.LyricResult, "lyric");
			Check.Equal(2, track.SourceOrder, "source order");
		});

		yield return ("NetEase.LookupTrackById keeps valid tracks without covers", delegate
		{
			TrackSearchResult track = new StubNetEaseNoCoverProvider().LookupTrackById("https://music.163.com/song?id=111", 2);
			Check.NotNull(track, "track");
			Check.Equal("111", track.SourceTrackId, "id");
			Check.Null(track.Cover, "cover");
			Check.NotNull(track.LyricResult, "lyric");
		});

		yield return ("QQ.LookupTrackById accepts songmid and numeric songid", delegate
		{
			StubQqProvider provider = new StubQqProvider();
			TrackSearchResult track = provider.LookupTrackById("003cI52o4daJJL", 1);
			Check.NotNull(track, "track by mid");
			Check.True(provider.RequestedUrl.Contains("songmid=003cI52o4daJJL"), "songmid parameter");
			Check.Equal("449198", track.SourceTrackId, "numeric source id");
			Check.Equal("003cI52o4daJJL", track.QqMusicMid, "mid");
			Check.Equal("魔杰座", track.Album, "album");
			Check.Equal("2008", track.Year, "year");
			Check.NotNull(track.Cover, "cover");
			Check.NotNull(track.LyricResult, "lyric");

			track = provider.LookupTrackById("449198", 1);
			Check.NotNull(track, "track by songid");
			Check.True(provider.RequestedUrl.Contains("songid=449198"), "songid parameter");
		});

		yield return ("Kuwo.LookupTrackById uses datacenter metadata without keyword search", delegate
		{
			TrackSearchResult track = new StubKuwoProvider().LookupTrackById("228908", 3);
			Check.NotNull(track, "track");
			Check.Equal("228908", track.SourceTrackId, "id");
			Check.Equal("晴天", track.Title, "title");
			Check.Equal("周杰伦", track.Artist, "artist");
			Check.Equal("叶惠美", track.Album, "album");
			Check.NotNull(track.Cover, "cover");
			Check.Equal("http://img2.kuwo.cn/star/albumcover/500/s3s94/93/211513640.jpg", track.Cover.CoverUrl, "500px cover");
			Check.NotNull(track.LyricResult, "lyric");
		});

		yield return ("Kugou.LookupTrackById uses hash detail and album metadata", delegate
		{
			TrackSearchResult track = new StubKugouProvider().LookupTrackById("514AF2D2B993E1A4BFFD0DF3EDA31874", 4);
			Check.NotNull(track, "track");
			Check.Equal("460895122", track.SourceTrackId, "audio id");
			Check.Equal("花海 (DJ 阿若版)", track.Title, "display title");
			Check.Equal("周杰伦", track.Artist, "artist");
			Check.Equal("魔杰座 DJ 阿若版", track.Album, "album");
			Check.Equal("514AF2D2B993E1A4BFFD0DF3EDA31874", track.KugouHash, "hash");
			Check.Equal(210000, track.KugouDurationMs, "duration");
			Check.NotNull(track.Cover, "cover");
			Check.Equal("http://imge.kugou.com/stdmusic/500/cover.jpg", track.Cover.CoverUrl, "cover url");
			Check.NotNull(track.LyricResult, "lyric");
		});

		yield return ("Kugou.LookupTrackById accepts numeric MixSongID", delegate
		{
			StubNumericKugouProvider provider = new StubNumericKugouProvider();
			TrackSearchResult track = provider.LookupTrackById("756464249", 4);
			Check.NotNull(track, "track");
			Check.True(provider.RequestedUrl.Contains("/kmr/v2/audio?"), "KRM endpoint");
			Check.True(provider.RequestedUrl.Contains("signature="), "signed request");
			Check.True(provider.RequestBody.Contains("\"entity_id\":756464249"), "numeric entity id body");
			Check.Equal("460895122", track.SourceTrackId, "audio id");
			Check.Equal("花海 (DJ 阿若版)", track.Title, "title");
			Check.Equal("魔杰座 DJ 阿若版", track.Album, "album");
			Check.Equal("2025", track.Year, "year");
			Check.Equal(210468, track.KugouDurationMs, "duration already milliseconds");
			Check.Equal("514AF2D2B993E1A4BFFD0DF3EDA31874", track.KugouHash, "resolved hash");
			Check.NotNull(track.Cover, "cover");
			Check.NotNull(track.LyricResult, "lyric");
		});

		yield return ("Track ID lookup parse failure is observable", delegate
		{
			StubQqParseFailureProvider provider = new StubQqParseFailureProvider();
			Check.Null(provider.LookupTrackById("003cI52o4daJJL", 0), "track");
			Check.NotNull(provider.LastTransportResult, "transport");
			Check.Equal(RemoteErrorKind.ParseFailed, provider.LastTransportResult.Error, "error kind");
		});
	}

	private sealed class StubQqParseFailureProvider : QqMusicTagProvider
	{
		protected override string GetResponseString(string url)
		{
			return "<html>not json</html>";
		}
	}
}
