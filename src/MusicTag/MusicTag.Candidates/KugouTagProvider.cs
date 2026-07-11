using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Threading;
using MusicTag.Composer;
using MusicTag.Serialization;
using MusicTag.Services;
using MusicTagWinApp.Adapter;
using MusicTagWinApp.Instances;
using MusicTagWinApp.Roles;
using MusicTagWinApp.Web;
using Newtonsoft.Json.Linq;

namespace MusicTag.Candidates;

internal class KugouTagProvider : RemoteTagProviderBase, ITrackSearchProvider, ITrackIdLookupProvider, ILyricSearchProvider, ITrackLyricLoader
{
	// 显式接口实现:把能力接口的统一签名(网易云超集)转发到本类既有 concrete,丢弃酷狗不接收的 knownSongId / existingLyrics。
	// concrete 方法体与签名一字未动;LoadLyricsForTrack 因签名匹配而隐式实现。酷狗无封面,故不实现 ICoverSearchProvider。
	List<TrackSearchResult> ITrackSearchProvider.SearchTracks(string query, int resultLimit, long knownSongId, int searchPass, int sourceOrder, List<TrackSearchResult> existingTracks, List<TrackSearchResult> previousResults)
	{
		return SearchTracks(query, resultLimit, searchPass, sourceOrder, existingTracks, previousResults);
	}

	List<LyricSearchResult> ILyricSearchProvider.SearchLyrics(string query, int resultLimit, long knownSongId, List<LyricSearchResult> existingLyrics, int sourceOrder)
	{
		return SearchLyrics(query, resultLimit, sourceOrder);
	}

	private const string songSearchUrlTemplate = "https://songsearch.kugou.com/song_search_v2?keyword={0}&page=1&pagesize={1}";

	private const string songInfoUrlTemplate = "https://m.kugou.com/app/i/getSongInfo.php?cmd=playInfo&hash={0}&from=mkugou";

	private const string albumInfoUrlTemplate = "https://mobilecdn.kugou.com/api/v3/album/info?albumid={0}&version=9108&area_code=1";

	private const string songEntityUrlTemplate = "https://gateway.kugou.com/kmr/v2/audio?appid=1005&clienttime={0}&clientver=20489&dfid=-&mid={1}&uuid=-&signature={2}";

	private const string KugouAndroidSignatureSalt = "OIlwieks28dk2k092lksi2UIkp";

	private const string KugouDeviceGuid = "550e8400-e29b-41d4-a716-446655440000";

	private const string lyricUrlTemplate = "https://m3ws.kugou.com/api/v1/krc/get_krc?keyword={0}&hash={1}&timelength={2}";

	private static readonly Regex bracketedContentRegex = new Regex("^\\[(.*)\\]$", RegexOptions.Compiled);

	public static HttpClient CreateKugouHttpClient()
	{
		HttpClient httpClient = new HttpClient(new HttpClientHandler
		{
			AutomaticDecompression = (DecompressionMethods.GZip | DecompressionMethods.Deflate)
		})
		{
			Timeout = TimeSpan.FromSeconds(20.0)
		};
		httpClient.DefaultRequestHeaders.Add("user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
		httpClient.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3");
		return httpClient;
	}

	protected override SearchSource GetSource()
	{
		return SearchSource.Kugou;
	}

	protected override HttpClient CreateHttpClient()
	{
		return CreateKugouHttpClient();
	}

	public KugouTagProvider(CancellationTokenSource cancellationSource)
		: base(cancellationSource)
	{
	}

	public KugouTagProvider()
		: this(null)
	{
	}

	private List<KugouSongInfo> SearchSongs(string query, int resultLimit)
	{
		if (cancellationSource.IsCancellationRequested)
		{
			return new List<KugouSongInfo>();
		}
		string responseBody = GetResponseString(string.Format(songSearchUrlTemplate, TextUtilities.UrlEncodeUtf8(query), resultLimit));
		if (cancellationSource.IsCancellationRequested)
		{
			return new List<KugouSongInfo>();
		}
		return ParseSongSearchResponse(responseBody);
	}

	public List<LyricSearchResult> SearchLyrics(string query, int resultLimit, int sourceOrder)
	{
		return BuildOrderedLyrics<KugouSongInfo>(SearchSongs(query, resultLimit).Take(resultLimit), LoadLyrics, sourceOrder);
	}

	private LyricSearchResult LoadLyrics(KugouSongInfo song)
	{
		string responseBody = GetResponseString(string.Format(lyricUrlTemplate, BuildEncodedLyricKeyword(song.Artist, song.Title), song.Hash, song.DurationMs));
		return ParseLyricResponse(song, responseBody);
	}

	public LyricSearchResult LoadLyricsForTrack(TrackSearchResult track)
	{
		KugouSongInfo song = new KugouSongInfo
		{
			AudioId = track.SourceTrackId,
			Title = track.Title,
			Artist = track.Artist,
			DurationMs = track.KugouDurationMs,
			Hash = track.KugouHash
		};
		return LoadLyrics(song);
	}

	public List<TrackSearchResult> SearchTracks(string query, int resultLimit, int searchPass, int sourceOrder, List<TrackSearchResult> existingTracks, List<TrackSearchResult> previousResults)
	{
		return BuildOrderedTracks<KugouSongInfo>(SearchSongs(query, resultLimit).Take(resultLimit), BuildTrackResult, searchPass, sourceOrder, existingTracks, previousResults);
	}

	public TrackSearchResult LookupTrackById(string trackId, int sourceOrder)
	{
		string normalizedId = TrackIdInput.ExtractLastPathOrQueryValue(trackId, "hash", "album_audio_id", "mixsongid", "id");
		if (long.TryParse(normalizedId, out long albumAudioId) && albumAudioId > 0L)
		{
			return LookupTrackByAlbumAudioId(albumAudioId, sourceOrder);
		}
		string hash = normalizedId.ToUpperInvariant();
		if (!Regex.IsMatch(hash, "^[A-F0-9]{32}$"))
		{
			return null;
		}
		string responseBody = GetResponseString(string.Format(songInfoUrlTemplate, hash));
		if (cancellationSource.IsCancellationRequested)
		{
			return null;
		}
		try
		{
			JObject songJson = JObject.Parse(responseBody);
			int errorCode = GetIntField(songJson, "errcode");
			if (errorCode != 0)
			{
				SetTransportError(errorCode == 1002 ? RemoteErrorKind.RateLimited : RemoteErrorKind.ParseFailed, errorCode.ToString(CultureInfo.InvariantCulture));
				return null;
			}
			if (string.IsNullOrWhiteSpace(GetStringOrEmpty(GetFirstField(songJson, "songName", "fileName"))))
			{
				return null;
			}
			string title = GetStringOrEmpty(GetFirstField(songJson, "songName"));
			string fileName = GetStringOrEmpty(GetFirstField(songJson, "fileName"));
			int separatorIndex = fileName.IndexOf(" - ", StringComparison.Ordinal);
			if (separatorIndex >= 0 && separatorIndex + 3 < fileName.Length)
			{
				title = fileName.Substring(separatorIndex + 3).Trim();
			}
			string albumId = GetStringOrEmpty(GetFirstField(songJson, "albumid", "req_albumid"));
			KugouSongInfo song = new KugouSongInfo
			{
				AudioId = GetStringOrEmpty(GetFirstField(songJson, "audio_id", "album_audio_id")),
				Title = title,
				Artist = GetStringOrEmpty(GetFirstField(songJson, "author_name", "singerName")),
				Album = LoadAlbumName(albumId),
				Hash = hash,
				DurationMs = GetIntField(songJson, "timeLength") * 1000
			};
			if (string.IsNullOrWhiteSpace(song.AudioId))
			{
				song.AudioId = hash;
			}
			TrackSearchResult track = BuildTrackResult(song);
			string coverUrl = GetStringOrEmpty(GetFirstField(songJson, "album_img", "imgUrl")).Replace("{size}", "500");
			SetKugouTrackCover(track, coverUrl);
			track.ResultOrder = 0;
			track.SearchPass = 0;
			track.SourceOrder = sourceOrder;
			return track;
		}
		catch (Exception parseError)
		{
			Console.WriteLine("Parse Kugou song detail error:" + parseError.GetMessageChain());
			if (!string.IsNullOrWhiteSpace(responseBody))
			{
				SetTransportError(RemoteErrorKind.ParseFailed, "parse");
			}
			return null;
		}
	}

	private TrackSearchResult LookupTrackByAlbumAudioId(long albumAudioId, int sourceOrder)
	{
		string responseBody = PostKugouSongEntity(albumAudioId);
		if (cancellationSource.IsCancellationRequested)
		{
			return null;
		}
		try
		{
			JObject responseJson = JObject.Parse(responseBody);
			JObject entity = responseJson["data"]?.First as JObject;
			JObject baseInfo = entity?["base"] as JObject;
			JObject audioInfo = entity?["audio_info"] as JObject;
			JObject albumInfo = entity?["album_info"] as JObject;
			if ((int?)responseJson["status"] != 1 || (int?)entity?["__status"] != 1 || baseInfo == null || audioInfo == null)
			{
				return null;
			}
			string hash = GetStringOrEmpty(audioInfo["hash"]).ToUpperInvariant();
			if (!Regex.IsMatch(hash, "^[A-F0-9]{32}$"))
			{
				return null;
			}
			string audioId = GetStringOrEmpty(baseInfo["audio_id"]);
			TrackSearchResult track = BuildTrackResult(new KugouSongInfo
			{
				AudioId = string.IsNullOrWhiteSpace(audioId) ? albumAudioId.ToString(CultureInfo.InvariantCulture) : audioId,
				Title = GetStringOrEmpty(baseInfo["songname"]),
				Artist = GetStringOrEmpty(baseInfo["author_name"]),
				Album = GetStringOrEmpty(baseInfo["album_name"]),
				Hash = hash,
				DurationMs = GetIntField(audioInfo, "timelength")
			});
			string publishDate = GetStringOrEmpty(albumInfo?["publish_date"] ?? baseInfo["publish_date"]);
			if (publishDate.Length >= 4)
			{
				track.Year = publishDate.Substring(0, 4);
			}
			string coverUrl = GetStringOrEmpty(albumInfo?["cover"]).Replace("{size}", "500");
			SetKugouTrackCover(track, coverUrl);
			track.ResultOrder = 0;
			track.SearchPass = 0;
			track.SourceOrder = sourceOrder;
			return track;
		}
		catch (Exception parseError)
		{
			Console.WriteLine("Parse Kugou entity detail error:" + parseError.GetMessageChain());
			if (!string.IsNullOrWhiteSpace(responseBody))
			{
				SetTransportError(RemoteErrorKind.ParseFailed, "parse");
			}
			return null;
		}
	}

	private string PostKugouSongEntity(long albumAudioId)
	{
		// MakcRe/KuGouMusicApi 的公开 Android 客户端签名协议；只使用通用设备标识，不需要用户账号或 Cookie。
		string clientTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
		string midHex = TextUtilities.ComputeMd5HashString(KugouDeviceGuid).Replace("-", "");
		string mid = BigInteger.Parse("0" + midHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
		string requestBody = "{\"data\":[{\"entity_id\":" + albumAudioId.ToString(CultureInfo.InvariantCulture) + "}],\"fields\":\"album_info,base,audio_info\"}";
		string sortedParameters = "appid=1005clienttime=" + clientTime + "clientver=20489dfid=-mid=" + mid + "uuid=-";
		string signature = TextUtilities.ComputeMd5HashString(KugouAndroidSignatureSalt + sortedParameters + requestBody + KugouAndroidSignatureSalt).Replace("-", "").ToLowerInvariant();
		string url = string.Format(songEntityUrlTemplate, clientTime, mid, signature);
		using HttpClient client = CreateKugouHttpClient();
		client.DefaultRequestHeaders.TryAddWithoutValidation("dfid", "-");
		client.DefaultRequestHeaders.TryAddWithoutValidation("clienttime", clientTime);
		client.DefaultRequestHeaders.TryAddWithoutValidation("mid", mid);
		client.DefaultRequestHeaders.TryAddWithoutValidation("kg-rc", "1");
		client.DefaultRequestHeaders.TryAddWithoutValidation("kg-thash", "5d816a0");
		client.DefaultRequestHeaders.TryAddWithoutValidation("kg-rec", "1");
		client.DefaultRequestHeaders.TryAddWithoutValidation("kg-rf", "B9EDA08A64250DEFFBCADDEE00F8F25F");
		client.DefaultRequestHeaders.TryAddWithoutValidation("x-router", "openapi.kugou.com");
		client.DefaultRequestHeaders.TryAddWithoutValidation("KG-TID", "238");
		return PostString(url, requestBody, client, postJson: true);
	}

	private void SetKugouTrackCover(TrackSearchResult track, string coverUrl)
	{
		if (string.IsNullOrWhiteSpace(coverUrl))
		{
			return;
		}
		track.Cover = new MusicTagWinApp.Listeners.CoverSearchResult
		{
			CoverUrl = coverUrl,
			SearchSource = GetSource(),
			CoverDownloader = CreateCoverDownloader<KugouTagProvider>(coverUrl)
		};
	}

	private string LoadAlbumName(string albumId)
	{
		if (string.IsNullOrWhiteSpace(albumId) || albumId == "0")
		{
			return "";
		}
		try
		{
			JObject albumJson = JObject.Parse(GetResponseString(string.Format(albumInfoUrlTemplate, TextUtilities.UrlEncodeUtf8(albumId))));
			return albumJson["data"]?["albumname"]?.ToString() ?? "";
		}
		catch (Exception albumParseError)
		{
			Console.WriteLine("Parse Kugou album detail error:" + albumParseError.GetMessageChain());
			return "";
		}
	}

	private TrackSearchResult BuildTrackResult(KugouSongInfo song)
	{
		TrackSearchResult track = new TrackSearchResult();
		track.SearchSource = GetSource();
		track.SourceTrackId = song.AudioId;
		track.Title = song.Title;
		track.Artist = song.Artist;
		track.Album = song.Album;
		track.KugouHash = song.Hash;
		track.KugouDurationMs = song.DurationMs;
		LyricSearchResult lyric = new LyricSearchResult();
		lyric.LyricUrl = string.Format(lyricUrlTemplate, BuildEncodedLyricKeyword(song.Artist, song.Title), song.Hash, song.DurationMs);
		lyric.SearchSource = GetSource();
		lyric.DeferredLyricLoader = cancellation =>
		{
			using KugouTagProvider kugouTagProvider = new KugouTagProvider(cancellation);
			return kugouTagProvider.LoadLyrics(song);
		};
		track.LyricResult = lyric;
		return track;
	}

	private List<KugouSongInfo> ParseSongSearchResponse(string responseBody)
	{
		List<KugouSongInfo> songs = new List<KugouSongInfo>();
		try
		{
			JToken data = JObject.Parse(responseBody)["data"];
			JToken songListJson = data?["info"] ?? data?["lists"];
			if (!(songListJson is JArray songList))
			{
				return songs;
			}

			foreach (JToken songJson in songList)
			{
				if (songJson?.Type != JTokenType.Object)
				{
					continue;
				}

				try
				{
					KugouSongInfo song = ParseSongSearchResult(songJson);
					if (!string.IsNullOrWhiteSpace(song.AudioId))
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

	private static KugouSongInfo ParseSongSearchResult(JToken songJson)
	{
		return new KugouSongInfo
		{
			AudioId = GetStringOrEmpty(GetFirstField(songJson, "audio_id", "Audioid", "ID")),
			Title = GetStringOrEmpty(GetFirstField(songJson, "songname", "SongName")),
			Artist = GetStringOrEmpty(GetFirstField(songJson, "singername", "SingerName")),
			Album = GetStringOrEmpty(GetFirstField(songJson, "album_name", "AlbumName")),
			Hash = GetStringOrEmpty(GetFirstField(songJson, "hash", "FileHash")),
			DurationMs = GetIntField(songJson, "duration", "Duration") * 1000
		};
	}

	private static int GetIntField(JToken token, params string[] fieldNames)
	{
		JToken fieldValue = GetFirstField(token, fieldNames);
		if (fieldValue == null)
		{
			return 0;
		}

		int intValue;
		return int.TryParse(fieldValue.ToString(), out intValue) ? intValue : 0;
	}

	private LyricSearchResult ParseLyricResponse(KugouSongInfo song, string responseBody)
	{
		LyricSearchResult lyric = null;
		try
		{
			JObject responseJson = JObject.Parse(responseBody);
			JObject lyricDataJson = responseJson["data"] as JObject;
			if (lyricDataJson == null)
			{
				return null;
			}

			string lyricText = lyricDataJson["lrc"]?.ToString() ?? "";
			string translatedLyric = "";
			if (lyricDataJson["landata"] is JArray translatedLines)
			{
				LyricTextProcessor lyricMerger = new LyricTextProcessor(lyricText, allowDuplicateTimestamps: true);
				foreach (JToken translatedLine in translatedLines)
				{
					if (translatedLine?.Type != JTokenType.Object)
					{
						continue;
					}

					int lineType;
					if (int.TryParse(translatedLine["type"]?.ToString(), out lineType) && lineType == 1)
					{
						translatedLyric = ParseTranslatedLyric(translatedLine["content"]?.ToString() ?? "", lyricMerger);
					}
				}
			}
			if (!string.IsNullOrWhiteSpace(lyricText))
			{
				lyric = new LyricSearchResult();
				lyric.Lyric = lyricText;
				lyric.TranslatedLyric = translatedLyric;
				if (song != null)
				{
					lyric.TrackId = song.AudioId;
					lyric.Title = song.Title;
					lyric.Artist = song.Artist;
					lyric.Album = song.Album;
				}
				lyric.SearchSource = GetSource();
			}
		}
		catch (Exception parseError)
		{
			Console.WriteLine("ParseSongsJson error:" + parseError.GetMessageChain());
		}
		return lyric;
	}

	internal static string BuildEncodedLyricKeyword(string artist, string title)
	{
		if (!string.IsNullOrWhiteSpace(artist) && !string.IsNullOrWhiteSpace(title))
		{
			return TextUtilities.UrlEncodeUtf8(title.Trim() + " - " + artist.Trim());
		}
		if (string.IsNullOrWhiteSpace(title))
		{
			return TextUtilities.UrlEncodeUtf8((artist ?? "").Trim());
		}
		return TextUtilities.UrlEncodeUtf8((title ?? "").Trim());
	}

	private string ParseTranslatedLyric(string content, LyricTextProcessor lyricMerger)
	{
		List<string> translatedLines = new List<string>();
		try
		{
			Match outerMatch = bracketedContentRegex.Match(content.Trim());
			if (outerMatch.Success)
			{
				string[] translationItems = outerMatch.Groups[1].Value.Trim().Split(',');
				foreach (string translationItem in translationItems)
				{
					Match innerMatch = bracketedContentRegex.Match(translationItem.Trim());
					if (innerMatch.Success)
					{
						translatedLines.Add(innerMatch.Groups[1].Value.Trim());
					}
				}
			}
		}
		catch (Exception parseError)
		{
			Console.WriteLine(parseError.GetMessageChain());
		}
		if (lyricMerger.TryReplaceOriginalLines(translatedLines))
		{
			return lyricMerger.ReformatLyricText(removeBlankLines: false, removeHeaderTags: true);
		}
		return "";
	}
}
