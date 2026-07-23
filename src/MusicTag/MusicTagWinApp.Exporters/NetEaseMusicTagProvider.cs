using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using MusicTag.Composer;
using MusicTag.Readers;
using MusicTag.Serialization;
using MusicTagWinApp.Adapter;
using MusicTagWinApp.Instances;
using MusicTagWinApp.Listeners;
using MusicTagWinApp.Properties;
using MusicTagWinApp.Roles;
using MusicTagWinApp.Web;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MusicTagWinApp.Exporters;

internal class NetEaseMusicTagProvider : RemoteTagProviderBase, ITrackSearchProvider, ITrackIdLookupProvider, ILyricSearchProvider, ICoverSearchProvider, ITrackLyricLoader
{
	// 网易云 concrete 的 SearchTracks / SearchLyrics / SearchCovers / LoadLyricsForTrack 签名即各能力接口的超集,
	// 故四个接口全部隐式实现,无需转发器。LastTransportResult / IDisposable 由 RemoteTagProviderBase 提供。
	private const string songSearchEndpoint = "https://music.163.com/weapi/cloudsearch/pc";

	private const string encryptedPostDataFormat = "params={0}&encSecKey={1}";

	private const string lyricEndpointFormat = "https://music.163.com/api/song/lyric/v1?id={0}&cp=false&lv=0&kv=0&tv=0&rv=0&yv=0&ytv=0&yrv=0";

	private const string songDetailsEndpoint = "https://music.163.com/weapi/v3/song/detail";

	private const string albumDetailsEndpointFormat = "https://music.163.com/weapi/v1/album/{0}";

	private const string clientHeaderName = "X-Real-IP";

	private static readonly Random clientIpRandom = new Random();

	private static readonly List<(long albumId, NetEaseAlbumInfo albumInfo)> albumInfoCache = new List<(long, NetEaseAlbumInfo)>();

	private static readonly Regex LyricTimestampRegex = new Regex(@"\[(\d+):(\d{2})(?:\.(\d{1,3}))?\]", RegexOptions.Compiled);

	protected override SearchSource GetSource()
	{
		return SearchSource.Music163;
	}

	protected override HttpClient CreateHttpClient()
	{
		HttpClient client = new HttpClient(new HttpClientHandler
		{
			AutomaticDecompression = (DecompressionMethods.GZip | DecompressionMethods.Deflate)
		})
		{
			Timeout = TimeSpan.FromSeconds(20.0),
			DefaultRequestHeaders = 
			{
				{ "Host", "music.163.com" },
				{ "Referer", "https://music.163.com" },
				{ "Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3" },
				{ "user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36" }
			}
		};
		client.DefaultRequestHeaders.Add(clientHeaderName, BuildClientHeaderValue());
		return client;
	}

	private HttpClient CreateAlbumHttpClient()
	{
		// net8 迁移:WebRequestHandler(System.Net.Http.WebRequest,netfx-only)→ HttpClientHandler。
		// 丢失 ReadWriteTimeout=5000(流级超时,HttpClientHandler 无对应物),albumClient.Timeout=5s 兜底。
		HttpClientHandler handler = new HttpClientHandler
		{
			AutomaticDecompression = (DecompressionMethods.GZip | DecompressionMethods.Deflate)
		};
		HttpClient albumClient = new HttpClient(handler)
		{
			Timeout = TimeSpan.FromSeconds(5.0)
		};
		foreach (KeyValuePair<string, IEnumerable<string>> header in GetHttpClient().DefaultRequestHeaders)
		{
			albumClient.DefaultRequestHeaders.Add(header.Key, header.Value);
		}
		return albumClient;
	}

	public NetEaseMusicTagProvider(CancellationTokenSource cancellation)
		: base(cancellation)
	{
	}

	public NetEaseMusicTagProvider()
		: this(null)
	{
	}

	// 网易云加密 POST 体:把 BuildEncryptedRequest 产出的 a/b 字段编码进 params/encSecKey 模板(原三处逐字节相同)。
	private static string BuildEncryptedPostBody(JObject encryptedRequest)
	{
		return string.Format(encryptedPostDataFormat, TextUtilities.UrlEncodeUtf8(encryptedRequest["a"].ToString()), TextUtilities.UrlEncodeUtf8(encryptedRequest["b"].ToString()));
	}

	// 歌曲查询 POST 公共骨架(SearchSongs / LoadSongDetails 同构,仅 endpoint / payload / 日志标签不同)。
	private List<NetEaseSongInfo> PostSongQuery(string endpoint, JObject payload, string errorLogTag)
	{
		if (cancellationSource.IsCancellationRequested)
		{
			return new List<NetEaseSongInfo>();
		}
		try
		{
			JObject encryptedRequest = NetEaseCrypto.BuildEncryptedRequest(payload.ToString(Formatting.None));
			string responseBody = PostString(endpoint, BuildEncryptedPostBody(encryptedRequest));
			return (!cancellationSource.IsCancellationRequested) ? ParseSongSearchResponse(responseBody) : new List<NetEaseSongInfo>();
		}
		catch (Exception ex)
		{
			Console.WriteLine(errorLogTag + " error:" + ex.GetMessageChain());
			return new List<NetEaseSongInfo>();
		}
	}

	// 发行年份格式化(GetAlbumReleaseYear / SearchTracks 同构):有效 publishTime → "yyyy",无效或异常 → null。
	internal static string FormatPublishYear(long? publishTime)
	{
		long publishTimeValue = publishTime.GetValueOrDefault();
		if (publishTime.HasValue && publishTimeValue > 0L)
		{
			try
			{
				return TextUtilities.UnixMillisecondsToDateTime(publishTimeValue).ToString("yyyy", CultureInfo.InvariantCulture);
			}
			catch (Exception)
			{
			}
		}
		return null;
	}

	private List<NetEaseSongInfo> SearchSongs(string query, int resultLimit)
	{
		return PostSongQuery(songSearchEndpoint, new JObject
		{
			{ "s", query },
			{ "type", 1 },
			{ "limit", resultLimit },
			{ "total", "true" },
			{ "offset", 0 }
		}, "SearchMusic");
	}

	private List<NetEaseSongInfo> LoadSongDetails(long songId)
	{
		return PostSongQuery(songDetailsEndpoint, new JObject
		{
			{
				"c",
				new JArray(new JObject
				{
					{ "id", songId },
					{ "v", 0 }
				}).ToString(Formatting.None)
			}
		}, "SearchSongDetail");
	}

	private NetEaseAlbumInfo LoadAlbumDetails(long albumId)
	{
		if (cancellationSource.IsCancellationRequested)
		{
			return null;
		}
		try
		{
			JObject encryptedRequest = NetEaseCrypto.BuildEncryptedRequest(new JObject
			{
				{ "total", "true" },
				{ "offset", "0" },
				{ "id", albumId },
				{ "limit", "1000" },
				{ "ext", "true" },
				{ "private_cloud", "true" }
			}.ToString(Formatting.None));
			using HttpClient albumClient = CreateAlbumHttpClient();
			string albumResponse = PostString(string.Format(albumDetailsEndpointFormat, albumId), BuildEncryptedPostBody(encryptedRequest), albumClient);
			return (!cancellationSource.IsCancellationRequested) ? ParseAlbumResponse(albumResponse) : null;
		}
		catch (Exception ex)
		{
			Console.WriteLine("SearchAlbum error:" + ex.GetMessageChain());
			return null;
		}
	}

	public List<LyricSearchResult> SearchLyrics(string query, int resultLimit, long knownSongId, List<LyricSearchResult> existingLyrics, int sourceOrder)
	{
		List<LyricSearchResult> lyrics = new List<LyricSearchResult>();
		List<NetEaseSongInfo> songs = ((knownSongId > 0L) ? LoadSongDetails(knownSongId) : SearchSongs(query, resultLimit));
		int resultIndex = 0;
		foreach (NetEaseSongInfo song in songs)
		{
			if (cancellationSource.IsCancellationRequested)
			{
				break;
			}
				if (!existingLyrics.Exists(lyric => lyric.TrackId == song.Id.ToString()))
				{
					LyricSearchResult lyric = LoadLyrics(song);
					if (lyric != null)
					{
						lyric.ResultOrder = resultIndex++;
						lyric.SourceOrder = sourceOrder;
						lyrics.Add(lyric);
					}
				}
		}
		return lyrics;
	}

	public List<CoverSearchResult> SearchCovers(string query, int resultLimit, List<CoverSearchResult> existingCovers)
	{
		return BuildDedupedCovers<NetEaseSongInfo>(SearchSongs(query, resultLimit), BuildCoverResult, existingCovers);
	}

	private CoverSearchResult BuildCoverResult(NetEaseSongInfo song)
	{
		string coverUrl = song.Album.CoverUrl;
		CoverSearchResult cover = new CoverSearchResult();
		cover.CoverUrl = coverUrl;
		cover.SearchSource = GetSource();
		cover.CoverDownloader = CreateCoverDownloader<NetEaseMusicTagProvider>(coverUrl);
		return cover;
	}

	public string GetAlbumReleaseYear(long albumId)
	{
		NetEaseAlbumInfo albumInfo = null;
		lock (albumInfoCache)
		{
			foreach (var cachedAlbum in albumInfoCache)
			{
				if (cachedAlbum.albumId == albumId)
				{
					albumInfo = cachedAlbum.albumInfo;
					break;
				}
			}
		}
		if (albumInfo == null)
		{
			albumInfo = LoadAlbumDetails(albumId);
			if (albumInfo != null)
			{
				lock (albumInfoCache)
				{
					bool alreadyCached = false;
					foreach (var cachedAlbum in albumInfoCache)
					{
						if (cachedAlbum.albumId == albumId)
						{
							alreadyCached = true;
							break;
						}
					}
					if (!alreadyCached)
					{
						albumInfoCache.Add((albumId, albumInfo));
						if (albumInfoCache.Count > 100)
						{
							albumInfoCache.RemoveAt(0);
						}
					}
				}
			}
		}
		if (albumInfo != null)
		{
			return FormatPublishYear(albumInfo.PublishTime);
		}
		return null;
	}

	public List<TrackSearchResult> SearchTracks(string query, int resultLimit, long knownSongId, int searchPass, int sourceOrder, List<TrackSearchResult> existingTracks, List<TrackSearchResult> previousResults)
	{
		List<TrackSearchResult> tracks = new List<TrackSearchResult>();
		List<NetEaseSongInfo> songs = ((knownSongId > 0L) ? LoadSongDetails(knownSongId) : SearchSongs(query, resultLimit));
		HashSet<string> seenTrackIds = new HashSet<string>();
		HashSet<string> knownTrackIds = new HashSet<string>();
		foreach (TrackSearchResult existingTrack in existingTracks)
		{
			if (existingTrack.SearchSource == GetSource() && !knownTrackIds.Contains(existingTrack.SourceTrackId))
			{
				knownTrackIds.Add(existingTrack.SourceTrackId);
			}
		}
		foreach (TrackSearchResult previousTrack in previousResults)
		{
			if (previousTrack.SearchSource == GetSource() && !knownTrackIds.Contains(previousTrack.SourceTrackId))
			{
				knownTrackIds.Add(previousTrack.SourceTrackId);
			}
		}
		foreach (NetEaseSongInfo song in songs)
		{
			TrackSearchResult track = BuildTrackResult(song);
			if (!seenTrackIds.Contains(track.SourceTrackId) && !knownTrackIds.Contains(track.SourceTrackId) && (knownSongId == 0L || track.Cover != null))
			{
				seenTrackIds.Add(track.SourceTrackId);
				tracks.Add(track);
			}
		}
			int resultOrder = 0;
			foreach (TrackSearchResult track in tracks)
			{
				if (Settings.Default.CommentTagWrite163Key && !string.IsNullOrWhiteSpace(track.Comment))
				{
					track.Comment = NetEaseCrypto.EncodeMusicComment(track.Comment);
				}
				track.ResultOrder = resultOrder++;
				track.SearchPass = searchPass;
				track.SourceOrder = sourceOrder;
			}
			return tracks;
		}

	public TrackSearchResult LookupTrackById(string trackId, int sourceOrder)
	{
		if (!TrackIdInput.TryNormalize(GetSource(), trackId, out string normalizedId) || !long.TryParse(normalizedId, out long songId))
		{
			return null;
		}
		List<NetEaseSongInfo> songs = LoadSongDetails(songId);
		if (songs.Count == 0)
		{
			return null;
		}
		NetEaseSongInfo song = songs[0];
		TrackSearchResult track = BuildTrackResult(song);
		if (Settings.Default.CommentTagWrite163Key && !string.IsNullOrWhiteSpace(track.Comment))
		{
			track.Comment = NetEaseCrypto.EncodeMusicComment(track.Comment);
		}
		track.ResultOrder = 0;
		track.SearchPass = 0;
		track.SourceOrder = sourceOrder;
		return track;
	}

	private TrackSearchResult BuildTrackResult(NetEaseSongInfo song)
	{
		TrackSearchResult track = new TrackSearchResult();
		track.SearchSource = GetSource();
		track.SourceTrackId = song.Id.ToString();
		track.Title = song.Title;
		track.Artist = song.GetArtistDisplayText();
		track.Album = song.Album.Name;
		track.Comment = (Settings.Default.CommentTagWrite163Key ? song.CommentJson : song.GetAliasCommentText());
		track.NetEaseAlbumId = song.Album.Id.ToString();
		track.Year = FormatPublishYear(song.Album.PublishTime);
		if (song.TrackNumber.HasValue && song.TrackNumber.Value > 0)
		{
			track.Track = song.TrackNumber.Value;
			track.TrackLabel = "Track " + song.TrackNumber;
			if (!string.IsNullOrWhiteSpace(song.DiscNumberText))
			{
				try
				{
					if (int.TryParse(song.DiscNumberText, out var discNumber))
					{
						if (discNumber > 1)
						{
							track.Disc = discNumber;
							track.TrackLabel = track.TrackLabel + " of " + song.DiscNumberText;
						}
					}
					else
					{
						Match discMatch = Regex.Match(song.DiscNumberText, "(\\d+)/\\d+");
						string discText;
						if (discMatch.Success && (discText = discMatch.Groups[1].Value) != null && int.TryParse(discText, out var parsedDiscNumber) && parsedDiscNumber > 1)
						{
							track.Disc = parsedDiscNumber;
							track.TrackLabel = track.TrackLabel + " of " + song.DiscNumberText;
						}
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine($"SearchCombTags parse int error:{ex.GetMessageChain()},{song.DiscNumberText},{song.Title},{song.TrackNumber.Value}");
				}
			}
		}
		if (!string.IsNullOrWhiteSpace(song.Album.CoverUrl))
		{
			CoverSearchResult cover = new CoverSearchResult();
			cover.CoverUrl = song.Album.CoverUrl;
			cover.CoverDownloader = CreateCoverDownloader<NetEaseMusicTagProvider>(song.Album.CoverUrl);
			track.Cover = cover;
		}
		LyricSearchResult lyric = new LyricSearchResult();
		lyric.LyricUrl = string.Format(lyricEndpointFormat, song.Id);
		lyric.SearchSource = GetSource();
		lyric.DeferredLyricLoader = cancellation =>
		{
			using NetEaseMusicTagProvider netEaseProvider = new NetEaseMusicTagProvider(cancellation);
			return netEaseProvider.LoadLyrics(song);
		};
		track.LyricResult = lyric;
		return track;
	}

	private LyricSearchResult LoadLyrics(NetEaseSongInfo song)
	{
		string responseBody = GetResponseString(string.Format(lyricEndpointFormat, song.Id));
		if (cancellationSource.IsCancellationRequested)
		{
			return null;
		}
		return ParseLyricResponse(song, responseBody);
	}

	public LyricSearchResult LoadLyricsForTrack(TrackSearchResult track)
	{
		if (!long.TryParse(track.SourceTrackId, out var sourceTrackId))
		{
			return null;
		}

		NetEaseSongInfo song = new NetEaseSongInfo
		{
			Id = sourceTrackId,
			Title = track.Title
		};
		song.ArtistNames.Add(track.Artist);
		song.Album.Name = track.Album;
		return LoadLyrics(song);
	}

	private List<NetEaseSongInfo> ParseSongSearchResponse(string responseBody)
	{
		List<NetEaseSongInfo> songs = new List<NetEaseSongInfo>();
		try
		{
			JObject responseJson = JObject.Parse(responseBody);
			JToken songListJson = responseJson.TryGetValue("result", out JToken searchResultJson) ? searchResultJson["songs"] : responseJson["songs"];
			if (!(songListJson is JArray songList))
			{
				return songs;
			}

			foreach (JToken songJson in songList)
			{
				try
				{
					NetEaseSongInfo song = ParseSongSearchResult(songJson);
					if (song.Id > 0L)
					{
						songs.Add(song);
					}
				}
				catch (Exception itemParseError)
				{
					Console.WriteLine("ParseSongsJson item error:" + itemParseError.GetMessageChain());
				}
			}
		}
		catch (Exception parseError)
		{
			Console.WriteLine("ParseSongsJson error:" + parseError.GetMessageChain());
			if (!string.IsNullOrWhiteSpace(responseBody))
			{
				// HTTP 200 拿到响应体却无法解析为 JSON:区分"解析失败"与"搜到 0 条 / 网络失败"。
				// (传输失败时 responseBody 为空,已由传输层归类为 Network/Timeout/HttpStatus。)
				SetTransportError(RemoteErrorKind.ParseFailed, "parse");
			}
		}
		return songs;
	}

	private static NetEaseSongInfo ParseSongSearchResult(JToken songJson)
	{
		NetEaseSongInfo song = new NetEaseSongInfo();
		song.Id = GetLongField(songJson, "id");
		song.Title = GetStringField(songJson, "name");
		song.DiscNumberText = GetStringField(songJson, "cd");
		song.TrackNumber = GetNullableIntField(songJson, "no");
		song.MvId = GetNullableLongField(songJson, "mvId") ?? GetNullableLongField(songJson, "mvid");
		song.Album.PublishTime = GetNullableLongField(songJson, "publishTime");
		ReadAlbumInfo(song, GetFirstField(songJson, "al", "album"));
		ReadArtistInfo(song, GetFirstField(songJson, "ar", "artists"));
		ReadAliases(song, GetFirstField(songJson, "alia", "alias"));
		song.Flag = GetNullableLongField(GetFirstField(songJson, "privilege"), "flag");
		song.CommentJson = "music:" + BuildCommentJson(song, songJson).ToString(Formatting.None);
		return song;
	}

	private static void ReadAlbumInfo(NetEaseSongInfo song, JToken albumJson)
	{
		song.Album.Name = "";
		song.Album.CoverUrl = "";
		if (albumJson == null)
		{
			return;
		}

		song.Album.Id = GetLongField(albumJson, "id");
		song.Album.Name = GetStringField(albumJson, "name");
		song.Album.CoverUrl = GetStringField(albumJson, "picUrl");
		song.Album.CoverDocId = GetNullableLongField(albumJson, "pic") ?? GetNullableLongField(albumJson, "picId");
		song.Album.PublishTime = song.Album.PublishTime ?? GetNullableLongField(albumJson, "publishTime");
		if (song.Album.CoverUrl == "null")
		{
			song.Album.CoverUrl = "";
		}
	}

	private static void ReadArtistInfo(NetEaseSongInfo song, JToken artistsJson)
	{
		if (!(artistsJson is JArray artistList))
		{
			return;
		}

		foreach (JToken artistJson in artistList)
		{
			long artistId = GetLongField(artistJson, "id");
			string artistName = GetStringField(artistJson, "name");
			if (!string.IsNullOrEmpty(artistName))
			{
				song.ArtistNames.Add(artistName);
			}
			if (artistId > 0L && !string.IsNullOrEmpty(artistName))
			{
				song.ArtistJson.Add(new JArray(artistName, artistId));
			}
		}
	}

	private static void ReadAliases(NetEaseSongInfo song, JToken aliasesJson)
	{
		if (!(aliasesJson is JArray aliasList))
		{
			return;
		}

		foreach (JToken aliasJson in aliasList)
		{
			string alias = GetStringOrEmpty(aliasJson);
			if (!string.IsNullOrEmpty(alias))
			{
				song.Aliases.Add(alias);
				song.AliasJson.Add(alias);
			}
		}
	}

	private static JObject BuildCommentJson(NetEaseSongInfo song, JToken songJson)
	{
		JObject commentJson = new JObject
		{
			{ "format", "mp3" },
			{ "musicId", song.Id },
			{ "musicName", song.Title },
			{ "artist", song.ArtistJson },
			{ "album", song.Album.Name },
			{ "albumId", song.Album.Id },
			{ "albumPic", song.Album.CoverUrl },
			{ "albumPicDocId", song.Album.CoverDocId ?? 0L },
			{ "mvId", song.MvId ?? 0L },
			{ "flag", song.Flag ?? 0L },
			{ "alias", song.AliasJson },
			{ "transNames", new JArray() }
		};
		long? bitrate = GetBestBitrate(songJson);
		long? durationMs = GetNullableLongField(songJson, "dt") ?? GetNullableLongField(songJson, "duration");
		if (bitrate.HasValue && durationMs.HasValue)
		{
			commentJson["bitrate"] = bitrate;
			commentJson["duration"] = durationMs;
		}
		long albumPicDocId = GetNullableLongField(commentJson, "albumPicDocId") ?? 0L;
		if (albumPicDocId == 0L && !string.IsNullOrWhiteSpace(song.Album.CoverUrl))
		{
			long coverDocId = ParseCoverDocId(song.Album.CoverUrl);
			if (coverDocId > 0L)
			{
				commentJson["albumPicDocId"] = coverDocId;
			}
		}
		return commentJson;
	}

	private static long? GetBestBitrate(JToken songJson)
	{
		return GetNullableLongField(GetFirstField(songJson, "h"), "br")
			?? GetNullableLongField(GetFirstField(songJson, "m"), "br")
			?? GetNullableLongField(GetFirstField(songJson, "l"), "br");
	}

	internal static long ParseCoverDocId(string coverUrl)
	{
		Match coverDocIdMatch = Regex.Match(coverUrl, "/(\\d+)\\.\\w+$");
		string coverDocIdText;
		if (coverDocIdMatch.Success && (coverDocIdText = coverDocIdMatch.Groups[1].Value) != null && long.TryParse(coverDocIdText, out var coverDocId) && coverDocId > 0L)
		{
			return coverDocId;
		}
		return 0L;
	}

	private NetEaseAlbumInfo ParseAlbumResponse(string responseBody)
	{
		try
		{
			JObject albumJson = JObject.Parse(responseBody)["album"] as JObject;
			if (albumJson == null)
			{
				return null;
			}

			return new NetEaseAlbumInfo
			{
				Id = GetLongField(albumJson, "id"),
				Name = GetStringOrEmpty(albumJson["name"]),
				PublishTime = GetNullableLongField(albumJson, "publishTime"),
				CoverUrl = GetStringOrEmpty(albumJson["picUrl"])
			};
		}
		catch (Exception ex)
		{
			Console.WriteLine("ParseAlbumJson error:" + ex.GetMessageChain());
		}
		return null;
	}

	private LyricSearchResult ParseLyricResponse(NetEaseSongInfo song, string responseBody)
	{
		LyricSearchResult lyric = null;
		try
		{
			var (lyricText, translatedLyricText) = ExtractLyricTexts(responseBody);
			if (string.IsNullOrEmpty(lyricText))
			{
				return null;
			}
			lyric = new LyricSearchResult();
			lyric.Lyric = lyricText;
			if (!string.IsNullOrEmpty(translatedLyricText))
			{
				lyric.TranslatedLyric = translatedLyricText;
			}
			if (song != null)
			{
				lyric.TrackId = song.Id.ToString();
				lyric.Title = song.Title;
				lyric.Artist = song.GetArtistDisplayText();
				lyric.Album = song.Album.Name;
			}
			lyric.SearchSource = GetSource();
		}
		catch (Exception ex)
		{
			Console.WriteLine("ParseLyricJson error:" + ex.GetMessageChain());
		}
		return lyric;
	}

	internal static (string lyricText, string translatedLyricText) ExtractLyricTexts(string responseBody)
	{
		JObject responseJson = JObject.Parse(responseBody);
		string lyricText = GetLyricField(responseJson["lrc"]);
		string translatedLyricText = GetLyricField(responseJson["tlyric"]);
		string yrcText = GetLyricField(responseJson["yrc"]);
		if (string.IsNullOrWhiteSpace(yrcText))
		{
			return (lyricText, translatedLyricText);
		}

		bool useThreeDigitMilliseconds = !Settings.Default.LyricDownload_ReformatTimetag;
		string convertedLyricText = NetEaseYrcDecoder.ConvertToLineLyric(yrcText, useThreeDigitMilliseconds);
		if (string.IsNullOrWhiteSpace(convertedLyricText))
		{
			return (lyricText, translatedLyricText);
		}

		string ytlrcText = GetLyricField(responseJson["ytlrc"]);
		if (!string.IsNullOrWhiteSpace(ytlrcText))
		{
			string normalizedYtlrc = NormalizeTranslatedLyric(ytlrcText, useThreeDigitMilliseconds);
			if (TryAlignTranslatedLyric(convertedLyricText, normalizedYtlrc, out (string original, string translated) alignedYtlrc, dropUnmatchedLines: true))
			{
				return alignedYtlrc;
			}
		}

		// YTLRC normally has the same line starts as YRC.  If it is absent or
		// malformed, the same alignment path handles the legacy tlyric fallback,
		// whose timestamps can be a few milliseconds earlier or later than YRC.
		if (!string.IsNullOrWhiteSpace(translatedLyricText) &&
			TryAlignTranslatedLyric(convertedLyricText, translatedLyricText, out (string original, string translated) alignedTlyric))
		{
			return alignedTlyric;
		}

		if (string.IsNullOrWhiteSpace(translatedLyricText))
		{
			return (convertedLyricText, "");
		}

		// Do not mix a partially aligned translation with YRC.  Retain the
		// legacy pair when neither translated timeline can be aligned safely.
		return (lyricText, translatedLyricText);
	}

	private static string GetLyricField(JToken lyricContainer)
	{
		string lyricText = lyricContainer?["lyric"]?.ToString() ?? "";
		return lyricText == "null" ? "" : lyricText;
	}

	private static string NormalizeTranslatedLyric(string lyricText, bool useThreeDigitMilliseconds)
	{
		string convertedYrc = NetEaseYrcDecoder.ConvertToLineLyric(lyricText, useThreeDigitMilliseconds);
		return string.IsNullOrWhiteSpace(convertedYrc) ? lyricText : convertedYrc;
	}

	private static bool TryAlignTranslatedLyric(string originalLyric, string translatedLyric, out (string original, string translated) alignedLyric, bool dropUnmatchedLines = false)
	{
		alignedLyric = (originalLyric, "");
		LyricTextProcessor lyricProcessor = new LyricTextProcessor(originalLyric);
		(string original, string translated) candidate = lyricProcessor.AlignAndSplitTranslatedLyric(new LyricTextProcessor(translatedLyric));
		if (string.IsNullOrWhiteSpace(candidate.translated))
		{
			alignedLyric = (candidate.original, "");
			return true;
		}

		HashSet<long> originalTimestamps = ExtractNonEmptyLyricTimestamps(candidate.original);
		List<string> retainedTranslatedLines = new List<string>();
		int translatedTimestampCount = 0;
		int droppedLineCount = 0;
		string[] translatedLines = candidate.translated.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
		foreach (string translatedLine in translatedLines)
		{
			MatchCollection timestampMatches = LyricTimestampRegex.Matches(translatedLine);
			bool lineMatchesOriginal = true;
			int lineTimestampCount = 0;
			foreach (Match timestampMatch in timestampMatches)
			{
				if (!TryParseLyricTimestamp(timestampMatch, out long timestampMilliseconds) || !originalTimestamps.Contains(timestampMilliseconds))
				{
					lineMatchesOriginal = false;
					break;
				}
				lineTimestampCount++;
			}

			if (!lineMatchesOriginal)
			{
				if (!dropUnmatchedLines)
				{
					return false;
				}
				droppedLineCount++;
				continue;
			}

			translatedTimestampCount += lineTimestampCount;
			retainedTranslatedLines.Add(translatedLine);
		}

		if (translatedTimestampCount == 0)
		{
			return false;
		}

		if (droppedLineCount > 0)
		{
			candidate.original = RemoveTranslationOnlyPlaceholderLines(candidate.original, originalLyric);
			candidate.translated = string.Join("\n", retainedTranslatedLines).Trim();
			Console.WriteLine($"NetEase translation alignment dropped {droppedLineCount} unmatched line(s).");
		}

		alignedLyric = candidate;
		return true;
	}

	private static string RemoveTranslationOnlyPlaceholderLines(string candidateOriginalLyric, string sourceOriginalLyric)
	{
		HashSet<long> sourceTimestamps = new HashSet<long>();
		foreach (Match timestampMatch in LyricTimestampRegex.Matches(sourceOriginalLyric ?? ""))
		{
			if (TryParseLyricTimestamp(timestampMatch, out long timestampMilliseconds))
			{
				sourceTimestamps.Add(timestampMilliseconds);
			}
		}

		List<string> retainedLines = new List<string>();
		string[] candidateLines = (candidateOriginalLyric ?? "").Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
		foreach (string candidateLine in candidateLines)
		{
			MatchCollection timestampMatches = LyricTimestampRegex.Matches(candidateLine);
			if (timestampMatches.Count > 0 && LyricTimestampRegex.Replace(candidateLine, "").Trim().Length == 0)
			{
				bool belongsToSource = false;
				foreach (Match timestampMatch in timestampMatches)
				{
					if (TryParseLyricTimestamp(timestampMatch, out long timestampMilliseconds) && sourceTimestamps.Contains(timestampMilliseconds))
					{
						belongsToSource = true;
						break;
					}
				}
				if (!belongsToSource)
				{
					continue;
				}
			}

			retainedLines.Add(candidateLine);
		}

		return string.Join("\n", retainedLines).Trim();
	}

	private static HashSet<long> ExtractNonEmptyLyricTimestamps(string lyricText)
	{
		HashSet<long> timestamps = new HashSet<long>();
		string[] lines = (lyricText ?? "").Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
		foreach (string line in lines)
		{
			string text = LyricTimestampRegex.Replace(line, "").Trim();
			if (text.Length == 0 || text == "//")
			{
				continue;
			}

			foreach (Match timestampMatch in LyricTimestampRegex.Matches(line))
			{
				if (TryParseLyricTimestamp(timestampMatch, out long timestampMilliseconds))
				{
					timestamps.Add(timestampMilliseconds);
				}
			}
		}

		return timestamps;
	}

	private static bool TryParseLyricTimestamp(Match timestampMatch, out long timestampMilliseconds)
	{
		timestampMilliseconds = 0L;
		if (!long.TryParse(timestampMatch.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out long minutes) ||
			!long.TryParse(timestampMatch.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out long seconds))
		{
			return false;
		}

		string fractionText = timestampMatch.Groups[3].Value;
		long fraction = 0L;
		if (fractionText.Length > 0 && !long.TryParse(fractionText, NumberStyles.None, CultureInfo.InvariantCulture, out fraction))
		{
			return false;
		}

		for (int digitCount = fractionText.Length; digitCount < 3; digitCount++)
		{
			fraction *= 10L;
		}

		timestampMilliseconds = (minutes * 60L + seconds) * 1000L + fraction;
		return true;
	}

	private static string BuildClientHeaderValue()
	{
		// The original native rc export returned a runtime-random 112.88.x.x address
		// used as a fake X-Real-IP header (a China-Mobile Guangdong range) to dodge
		// region gating. Reproduce the same shape in managed code.
		int third, fourth;
		lock (clientIpRandom)
		{
			third = clientIpRandom.Next(256);
			fourth = clientIpRandom.Next(256);
		}
		return "112.88." + third + "." + fourth;
	}
}
