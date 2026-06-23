using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using MusicTag.Composer;
using MusicTag.Serialization;
using MusicTag.Services;
using MusicTagWinApp.Adapter;
using MusicTagWinApp.Instances;
using MusicTagWinApp.Listeners;
using MusicTagWinApp.Properties;
using MusicTagWinApp.Roles;
using MusicTagWinApp.Structs;
using MusicTagWinApp.Web;
using Newtonsoft.Json.Linq;

namespace MusicTagWinApp.Writers;

internal class QqMusicTagProvider : RemoteTagProviderBase
{
	private const string EmptyLyricPlaceholderBase64 = "WzAwOjAwOjAwXeatpOatjOabsuS4uuayoeacieWhq+ivjeeahOe6r+mfs+S5kO+8jOivt+aCqOaso+i1jw==";

	private static readonly string callbackName;

	private static readonly string searchEndpointUrl;

	private static readonly string searchRequestTemplate;

	private static readonly string lyricUrlTemplate;

	private static readonly string albumCoverUrlTemplate;

	protected override SearchSource GetSource()
	{
		return SearchSource.QQ;
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
				{ "accept-language", "zh-CN,zh;q=0.9,en;q=0.8" },
				{ "referer", "https://i.y.qq.com/" },
				{ "user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36" }
			}
		};
		string cookie = Settings.Default.QQMusic_Cookie;
		if (!string.IsNullOrWhiteSpace(cookie))
		{
			client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", cookie.Trim());
		}
		return client;
	}

	public QqMusicTagProvider(CancellationTokenSource cancellationSource)
		: base(cancellationSource)
	{
	}

	public QqMusicTagProvider()
		: this(null)
	{
	}

	private List<QqSongInfo> SearchSongs(string query, int maxResults)
	{
		string requestBody = string.Format(searchRequestTemplate, "req_0", TextEncodingService.JavaScriptStringEncode(query), maxResults);
		const int maxAttempts = 3;
		for (int attempt = 0; attempt < maxAttempts; attempt++)
		{
			if (cancellationSource.IsCancellationRequested)
			{
				return new List<QqSongInfo>();
			}

			string responseBody = PostString(searchEndpointUrl, requestBody, null, postJson: true);
			if (cancellationSource.IsCancellationRequested)
			{
				return new List<QqSongInfo>();
			}

			if (attempt + 1 < maxAttempts && IsRateLimited(responseBody))
			{
				Console.WriteLine($"QQ search throttled (req_0.code 2001), retry {attempt + 1}/{maxAttempts - 1}");
				cancellationSource.Token.WaitHandle.WaitOne(800 * (attempt + 1));
				continue;
			}

			return ParseSongSearchResponse(responseBody);
		}

		return new List<QqSongInfo>();
	}

	private static bool IsRateLimited(string responseBody)
	{
		if (string.IsNullOrWhiteSpace(responseBody))
		{
			return false;
		}

		try
		{
			JToken codeToken = JObject.Parse(responseBody)["req_0"]?["code"];
			return codeToken != null && codeToken.Type == JTokenType.Integer && (int)codeToken == 2001;
		}
		catch (Exception)
		{
			return false;
		}
	}

	public List<LyricSearchResult> SearchLyrics(string query, int maxResults, int sourceOrder)
	{
		List<LyricSearchResult> lyrics = new List<LyricSearchResult>();
		int resultIndex = 0;
		foreach (QqSongInfo songInfo in SearchSongs(query, maxResults))
		{
			if (cancellationSource.IsCancellationRequested)
			{
				break;
			}

			LyricSearchResult lyric = LoadLyrics(songInfo);
			if (lyric != null)
			{
				lyric.ResultOrder = resultIndex++;
				lyric.SourceOrder = sourceOrder;
				lyrics.Add(lyric);
			}
		}

		return lyrics;
	}

	public List<CoverSearchResult> SearchCovers(string query, int maxResults, List<CoverSearchResult> existingCovers)
	{
		List<CoverSearchResult> covers = new List<CoverSearchResult>();
		HashSet<string> addedCoverUrls = new HashSet<string>();
		foreach (QqSongInfo songInfo in SearchSongs(query, maxResults))
		{
			if (cancellationSource.IsCancellationRequested)
			{
				break;
			}

			string coverUrl = string.Format(albumCoverUrlTemplate, songInfo.Album.Mid);
			if (addedCoverUrls.Contains(coverUrl))
			{
				continue;
			}

			bool isNewCover = true;
			foreach (CoverSearchResult existingCover in existingCovers)
			{
				if (existingCover.CoverUrl == coverUrl)
				{
					isNewCover = false;
					break;
				}
			}

			if (isNewCover)
			{
				CoverSearchResult cover = new CoverSearchResult();
				cover.CoverUrl = coverUrl;
				cover.SearchSource = GetSource();
				cover.CoverDownloader = CreateCoverDownloader<QqMusicTagProvider>(coverUrl);
				covers.Add(cover);
				addedCoverUrls.Add(coverUrl);
			}
		}

		return covers;
	}

	public List<TrackSearchResult> SearchTracks(string query, int maxResults, int searchPass, int sourceOrder, List<TrackSearchResult> existingTracks, List<TrackSearchResult> previousResults)
	{
		List<TrackSearchResult> tracks = new List<TrackSearchResult>();
		List<QqSongInfo> songs = SearchSongs(query, maxResults);
		Dictionary<string, TrackSearchResult> tracksById = new Dictionary<string, TrackSearchResult>();
		List<string> trackIdsInOrder = new List<string>();
		HashSet<string> knownTrackIds = new HashSet<string>();
		foreach (TrackSearchResult track in existingTracks)
		{
			if (track.SearchSource == GetSource() && !knownTrackIds.Contains(track.SourceTrackId))
			{
				knownTrackIds.Add(track.SourceTrackId);
			}
		}

		foreach (TrackSearchResult track in previousResults)
		{
			if (track.SearchSource == GetSource() && !knownTrackIds.Contains(track.SourceTrackId))
			{
				knownTrackIds.Add(track.SourceTrackId);
			}
		}

		foreach (QqSongInfo songInfo in songs)
		{
			TrackSearchResult track = new TrackSearchResult();
			track.SearchSource = GetSource();
			track.SourceTrackId = songInfo.Id.ToString();
			track.QqMusicMid = songInfo.Mid;
			track.Title = songInfo.Title;
			track.OriginalTitle = songInfo.Name;
			track.Artist = songInfo.GetArtistNames();
			track.Album = songInfo.Album.Name;
			track.Genre = songInfo.GetGenreName();
			track.Comment = songInfo.Subtitle;
			if (!string.IsNullOrWhiteSpace(songInfo.ReleaseDate) && DateTime.TryParseExact(songInfo.ReleaseDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var releaseDate))
			{
				track.Year = releaseDate.ToString("yyyy");
			}

			if (songInfo.TrackNumber.HasValue && songInfo.TrackNumber.Value > 0)
			{
				track.Track = songInfo.TrackNumber.Value;
				track.TrackLabel = "Track " + songInfo.TrackNumber;
				if (songInfo.DiscNumber.HasValue && songInfo.DiscNumber > 1)
				{
					track.Disc = songInfo.DiscNumber.Value;
					track.TrackLabel = track.TrackLabel + " of " + songInfo.DiscNumber;
				}
			}

			string coverUrl = string.Format(albumCoverUrlTemplate, songInfo.Album.Mid);
			CoverSearchResult cover = new CoverSearchResult();
			cover.CoverUrl = coverUrl;
			cover.SearchSource = GetSource();
			cover.CoverDownloader = CreateCoverDownloader<QqMusicTagProvider>(coverUrl);
			track.Cover = cover;

			LyricSearchResult lyric = new LyricSearchResult();
			lyric.LyricUrl = string.Format(lyricUrlTemplate, songInfo.Mid, callbackName);
			lyric.SearchSource = GetSource();
			lyric.DeferredLyricLoader = cancellation =>
			{
				using QqMusicTagProvider qqProvider = new QqMusicTagProvider(cancellation);
				return qqProvider.LoadLyrics(songInfo);
			};
			track.LyricResult = lyric;
			if (!tracksById.ContainsKey(track.SourceTrackId) && !knownTrackIds.Contains(track.SourceTrackId))
			{
				trackIdsInOrder.Add(track.SourceTrackId);
				tracksById.Add(track.SourceTrackId, track);
			}
		}

		foreach (string trackId in trackIdsInOrder)
		{
			tracks.Add(tracksById[trackId]);
		}

		int resultIndex = 0;
		foreach (TrackSearchResult track in tracks)
		{
			track.ResultOrder = resultIndex++;
			track.SearchPass = searchPass;
			track.SourceOrder = sourceOrder;
		}

		return tracks;
	}

	private LyricSearchResult LoadLyrics(QqSongInfo songInfo)
	{
		string responseBody = GetResponseString(string.Format(lyricUrlTemplate, songInfo.Mid, callbackName));
		if (cancellationSource.IsCancellationRequested)
		{
			return null;
		}

		return CreateLyricResult(songInfo, responseBody);
	}

	public LyricSearchResult LoadLyricsForTrack(TrackSearchResult track)
	{
		QqSongInfo songInfo = new QqSongInfo
		{
			Id = long.Parse(track.SourceTrackId),
			Mid = track.QqMusicMid,
			Title = track.Title,
			Name = track.OriginalTitle
		};

		songInfo.Artists.Add(new QqArtistInfo
		{
			Name = track.Artist
		});
		songInfo.Album.Name = track.Album;

		return LoadLyrics(songInfo);
	}

	private static string ExtractCallbackJson(string value)
	{
		Match match = new Regex($"{callbackName}\\((.+)\\)").Match(value.Trim());
		if (!match.Success)
		{
			return "";
		}
		return match.Groups[1].Value;
	}

	private List<QqSongInfo> ParseSongSearchResponse(string responseBody)
	{
		List<QqSongInfo> songs = new List<QqSongInfo>();
		try
		{
			if (string.IsNullOrWhiteSpace(responseBody))
			{
				return songs;
			}

			JToken songListJson = JObject.Parse(responseBody)["req_0"]?["data"]?["body"]?["song"]?["list"];
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
					QqSongInfo songInfo = ParseSongSearchResult(songJson);
					if (songInfo.Album.Id > 0L && songInfo.Album.Mid.Any() && !string.IsNullOrWhiteSpace(songInfo.Album.Name))
					{
						songs.Add(songInfo);
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
		}
		return songs;
	}

	private static QqSongInfo ParseSongSearchResult(JToken songJson)
	{
		QqSongInfo songInfo = new QqSongInfo();
		songInfo.Id = GetLongField(songJson, "id");
		songInfo.Mid = GetStringField(songJson, "mid");
		songInfo.Name = TextEncodingService.DecodeBasicHtmlEntities(GetStringField(songJson, "name"));
		songInfo.GenreId = GetNullableIntField(songJson, "genre");
		songInfo.TrackNumber = GetNullableIntField(songJson, "index_album");
		songInfo.DiscNumber = GetNullableIntField(songJson, "index_cd");
		songInfo.DurationSeconds = GetNullableLongField(songJson, "interval");
		songInfo.ReleaseDate = GetStringField(songJson, "time_public");
		songInfo.Title = TextEncodingService.DecodeBasicHtmlEntities(GetStringField(songJson, "title"));
		songInfo.Subtitle = TextEncodingService.DecodeBasicHtmlEntities(GetStringField(songJson, "subtitle"));
		songInfo.LyricPreview = GetStringField(songJson, "lyric");

		ReadSingerInfo(songInfo, songJson["singer"]);
		ReadAlbumInfo(songInfo, songJson["album"]);
		return songInfo;
	}

	private static void ReadSingerInfo(QqSongInfo songInfo, JToken singers)
	{
		if (singers?.Type != JTokenType.Array)
		{
			return;
		}

		foreach (JToken singer in singers)
		{
			songInfo.Artists.Add(new QqArtistInfo
			{
				Id = GetLongField(singer, "id"),
				Mid = GetStringField(singer, "mid"),
				Name = GetStringField(singer, "name"),
				Title = GetStringField(singer, "title")
			});
		}
	}

	private static void ReadAlbumInfo(QqSongInfo songInfo, JToken album)
	{
		if (album?.Type != JTokenType.Object)
		{
			return;
		}

		songInfo.Album.Id = GetLongField(album, "id");
		songInfo.Album.Mid = GetStringField(album, "mid");
		songInfo.Album.Name = TextEncodingService.DecodeBasicHtmlEntities(GetStringField(album, "name"));
		songInfo.Album.Title = GetStringField(album, "title");
		songInfo.Album.Subtitle = GetStringField(album, "subtitle");
	}

	private static string GetStringField(JToken token, string fieldName)
	{
		if (token?.Type != JTokenType.Object)
		{
			return "";
		}
		return token[fieldName]?.ToString() ?? "";
	}

	private static int? GetNullableIntField(JToken token, string fieldName)
	{
		if (token?.Type != JTokenType.Object)
		{
			return null;
		}

		JToken fieldValue = token[fieldName];
		if (fieldValue == null)
		{
			return null;
		}

		int intValue;
		return int.TryParse(fieldValue.ToString(), out intValue) ? intValue : (int?)null;
	}

	private static long GetLongField(JToken token, string fieldName)
	{
		return GetNullableLongField(token, fieldName) ?? 0L;
	}

	private static long? GetNullableLongField(JToken token, string fieldName)
	{
		if (token?.Type != JTokenType.Object)
		{
			return null;
		}

		JToken fieldValue = token[fieldName];
		if (fieldValue == null)
		{
			return null;
		}

		long longValue;
		return long.TryParse(fieldValue.ToString(), out longValue) ? longValue : (long?)null;
	}

	private LyricSearchResult CreateLyricResult(QqSongInfo songInfo, string responseBody)
	{
		LyricSearchResult lyric = null;
		try
		{
			lyric = ParseLyricResponse(responseBody);
			if (lyric == null)
			{
				return null;
			}

			if (songInfo != null)
			{
				lyric.TrackId = songInfo.Id.ToString();
				lyric.Title = songInfo.Title;
				lyric.Artist = songInfo.GetArtistNames();
				lyric.Album = songInfo.Album.Name;
				lyric.OriginalTitle = songInfo.Name;
			}

			lyric.SearchSource = GetSource();
		}
		catch (Exception parseError)
		{
			Console.WriteLine("ParseLyricJson error:" + parseError.GetMessageChain());
		}

		return lyric;
	}

	private static LyricSearchResult ParseLyricResponse(string responseBody)
	{
		var (lyricText, translatedLyricText) = DecodeLyricPayload(responseBody);
		if (string.IsNullOrWhiteSpace(lyricText))
		{
			return null;
		}

		LyricSearchResult lyric = new LyricSearchResult();
		lyric.Lyric = TextEncodingService.DecodeBasicHtmlEntities(lyricText);
		if (!string.IsNullOrWhiteSpace(translatedLyricText))
		{
			lyric.TranslatedLyric = TextEncodingService.DecodeBasicHtmlEntities(translatedLyricText);
		}

		if (!string.IsNullOrWhiteSpace(lyric.Lyric) && !string.IsNullOrWhiteSpace(lyric.TranslatedLyric))
		{
			string mergedTranslation;
			(lyric.Lyric, mergedTranslation) = new LyricTextProcessor(lyric.Lyric).AlignAndSplitTranslatedLyric(new LyricTextProcessor(lyric.TranslatedLyric));
			lyric.TranslatedLyric = mergedTranslation;
		}

		return lyric;
	}

	private static (string Lyric, string Translation) DecodeLyricPayload(string responseBody)
	{
		string callbackJson = ExtractCallbackJson(responseBody);
		if (string.IsNullOrWhiteSpace(callbackJson))
		{
			return ("", "");
		}

		JObject json = JObject.Parse(callbackJson);
		string encodedLyric = json["lyric"]?.ToString() ?? "";
		string encodedTranslation = json["trans"]?.ToString() ?? "";
		if (encodedLyric == "null")
		{
			encodedLyric = "";
		}

		if (encodedTranslation == "null")
		{
			encodedTranslation = "";
		}

		string lyric = "";
		string translation = "";
		if (!string.IsNullOrWhiteSpace(encodedLyric) && encodedLyric != EmptyLyricPlaceholderBase64)
		{
			lyric = DatabaseMapper.DecodeBase64String(encodedLyric, "UTF-8");
		}

		if (!string.IsNullOrWhiteSpace(encodedTranslation))
		{
			translation = DatabaseMapper.DecodeBase64String(encodedTranslation, "UTF-8");
		}

		return (lyric, translation);
	}

	[DllImport("MusicTag.dll", EntryPoint = "nd")]
	private static extern IntPtr GetSearchEndpointUrlPointer();

	[DllImport("MusicTag.dll", EntryPoint = "ndd")]
	private static extern IntPtr GetSearchRequestTemplatePointer();

	[DllImport("MusicTag.dll", EntryPoint = "ne")]
	private static extern IntPtr GetLyricUrlTemplatePointer();

	[DllImport("MusicTag.dll", EntryPoint = "nf")]
	private static extern IntPtr GetAlbumCoverUrlTemplatePointer();

	static QqMusicTagProvider()
	{
		callbackName = "MusicJsonCallback34475857153687595";
		searchEndpointUrl = Marshal.PtrToStringUni(GetSearchEndpointUrlPointer());
		searchRequestTemplate = Marshal.PtrToStringUni(GetSearchRequestTemplatePointer());
		lyricUrlTemplate = Marshal.PtrToStringUni(GetLyricUrlTemplatePointer());
		albumCoverUrlTemplate = Marshal.PtrToStringUni(GetAlbumCoverUrlTemplatePointer());
	}
}
