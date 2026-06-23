using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using MusicTag.Attributes;
using MusicTag.Composer;
using MusicTag.Serialization;
using MusicTag.States;
using MusicTagWinApp.Adapter;
using MusicTagWinApp.Instances;
using MusicTagWinApp.Listeners;
using MusicTagWinApp.Roles;
using MusicTagWinApp.Web;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MusicTag.Services;

internal class XiamiTagProvider : RemoteTagProviderBase
{
	private static readonly string searchEndpoint;

	private static readonly string initialRequestPayload;

	private static readonly string defaultCookieHeader;

	private static string xiamiCookieHeader;

	private readonly CookieContainer cookieContainer = new CookieContainer();

	protected override SearchSource GetSource()
	{
		return SearchSource.Xiami;
	}

	protected override HttpClient CreateHttpClient()
	{
		HttpClientHandler handler = new HttpClientHandler
		{
			AutomaticDecompression = (DecompressionMethods.GZip | DecompressionMethods.Deflate),
			CookieContainer = cookieContainer
		};
		if (xiamiCookieHeader == null)
		{
			xiamiCookieHeader = defaultCookieHeader;
		}
		foreach (Cookie cookie in ParseCookieHeader(xiamiCookieHeader))
		{
			cookieContainer.Add(cookie);
		}
		HttpClient httpClient = new HttpClient(handler);
		httpClient.Timeout = TimeSpan.FromSeconds(10.0);
		httpClient.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3");
		httpClient.DefaultRequestHeaders.Add("Accept-language", "zh-CN,zh;q=0.9,en;q=0.8,zh-TW;q=0.7");
		httpClient.DefaultRequestHeaders.Add("Referer", Marshal.PtrToStringUni(GetRefererPointer()));
		httpClient.DefaultRequestHeaders.Add("user-agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_13_1) AppleWebKit/537.36 (KHTML, like Gecko) XIAMI-MUSIC/3.0.9 Chrome/56.0.2924.87 Electron/1.6.11 Safari/537.36");
		return httpClient;
	}

	public XiamiTagProvider(CancellationTokenSource cancellationSource)
		: base(cancellationSource)
	{
	}

	public XiamiTagProvider()
		: this(null)
	{
	}

	public List<CoverSearchResult> SearchCovers(string query, int resultLimit, List<CoverSearchResult> existingCovers)
	{
		List<CoverSearchResult> covers = new List<CoverSearchResult>();
		HashSet<string> seenCoverUrls = new HashSet<string>();
		foreach (XiamiSong song in SearchSongs(query, resultLimit))
		{
			if (cancellationSource.IsCancellationRequested)
			{
				break;
			}
			string coverUrl = song.CoverUrl;
			if (string.IsNullOrWhiteSpace(coverUrl) || seenCoverUrls.Contains(coverUrl))
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
				cover.CoverDownloader = CreateCoverDownloader<XiamiTagProvider>(coverUrl);
				covers.Add(cover);
				seenCoverUrls.Add(coverUrl);
			}
		}
		return covers;
	}

	public List<LyricSearchResult> SearchLyrics(string query, int resultLimit, int sourceOrder)
	{
		List<LyricSearchResult> lyrics = new List<LyricSearchResult>();
		List<XiamiSong> songs = SearchSongs(query, resultLimit);
		int displayOrder = 0;
		foreach (XiamiSong song in songs)
		{
			if (!cancellationSource.IsCancellationRequested)
			{
				LyricSearchResult lyric = DownloadLyric(song);
				if (lyric != null)
				{
					lyric.ResultOrder = displayOrder++;
					lyric.SourceOrder = sourceOrder;
					lyrics.Add(lyric);
				}
				continue;
			}
			break;
		}
		return lyrics;
	}

	public List<TrackSearchResult> SearchTracks(string query, int resultLimit, int searchPass, int sourceOrder, List<TrackSearchResult> existingTracks, List<TrackSearchResult> pendingTracks)
	{
		List<TrackSearchResult> tracks = new List<TrackSearchResult>();
		List<XiamiSong> songs = SearchSongs(query, resultLimit);
		Dictionary<string, TrackSearchResult> tracksById = new Dictionary<string, TrackSearchResult>();
		List<string> resultIds = new List<string>();
		HashSet<string> existingTrackIds = new HashSet<string>();
		foreach (TrackSearchResult existingTrack in existingTracks)
		{
			if (existingTrack.SearchSource == GetSource() && !existingTrackIds.Contains(existingTrack.SourceTrackId))
			{
				existingTrackIds.Add(existingTrack.SourceTrackId);
			}
		}
		foreach (TrackSearchResult pendingTrack in pendingTracks)
		{
			if (pendingTrack.SearchSource == GetSource() && !existingTrackIds.Contains(pendingTrack.SourceTrackId))
			{
				existingTrackIds.Add(pendingTrack.SourceTrackId);
			}
		}
		foreach (XiamiSong song in songs)
		{
			TrackSearchResult track = new TrackSearchResult();
			track.SearchSource = GetSource();
			track.SourceTrackId = song.SongId;
			track.Title = song.SongName;
			track.Artist = song.GetArtistName();
			track.Album = song.AlbumName;
			track.Comment = song.Subtitle;
			try
			{
				long? releaseTime = song.CreatedAt;
				if (releaseTime.HasValue && releaseTime.GetValueOrDefault() > 0L)
				{
					track.Year = DatabaseMapper.UnixMillisecondsToDateTime(releaseTime.GetValueOrDefault()).ToString("yyyy");
				}
			}
			catch (Exception)
			{
			}
			if (song.TrackNumber.HasValue && song.TrackNumber.Value > 0)
			{
				track.Track = song.TrackNumber.Value;
				track.TrackLabel = "Track " + song.TrackNumber;
				if (song.DiscNumber.HasValue)
				{
					track.Disc = song.DiscNumber.Value;
					track.TrackLabel = track.TrackLabel + " of " + song.DiscNumber;
				}
			}
			if (!string.IsNullOrWhiteSpace(song.CoverUrl))
			{
				CoverSearchResult cover = new CoverSearchResult();
				cover.CoverUrl = song.CoverUrl;
				cover.SearchSource = GetSource();
				cover.CoverDownloader = CreateCoverDownloader<XiamiTagProvider>(song.CoverUrl);
				track.Cover = cover;
			}
			if (!string.IsNullOrWhiteSpace(song.LyricUrl))
			{
				LyricSearchResult lyric = new LyricSearchResult();
				lyric.LyricUrl = song.LyricUrl;
				lyric.SearchSource = GetSource();
				lyric.LyricType = song.LyricType;
				lyric.DeferredLyricLoader = cancellation =>
				{
					using XiamiTagProvider xiamiTagProvider = new XiamiTagProvider(cancellation);
					return xiamiTagProvider.DownloadLyric(song);
				};
				track.LyricResult = lyric;
			}
			if (!tracksById.ContainsKey(track.SourceTrackId) && !existingTrackIds.Contains(track.SourceTrackId))
			{
				resultIds.Add(track.SourceTrackId);
				tracksById.Add(track.SourceTrackId, track);
			}
		}
		foreach (string resultId in resultIds)
		{
			tracks.Add(tracksById[resultId]);
		}
		int displayOrder = 0;
		foreach (TrackSearchResult track in tracks)
		{
			track.ResultOrder = displayOrder++;
			track.SearchPass = searchPass;
			track.SourceOrder = sourceOrder;
		}
		return tracks;
	}

	private List<XiamiSong> SearchSongs(string query, int resultLimit)
	{
		if (cancellationSource.IsCancellationRequested)
		{
			return new List<XiamiSong>();
		}
		try
		{
			if (xiamiCookieHeader == null)
			{
				UpdateXiamiCookieHeader(null);
			}
			if (xiamiCookieHeader == null)
			{
				return new List<XiamiSong>();
			}
			for (int attemptIndex = 0; attemptIndex < 3; attemptIndex++)
			{
				string requestBody = new JObject
				{
					{
						"requestStr",
						new JObject
						{
							{
								"header",
								new JObject { { "platformId", "mac" } }
							},
							{
								"model",
								new JObject
								{
									{ "key", query },
									{
										"pagingVO",
										new JObject
										{
											{ "page", 1 },
											{ "pageSize", resultLimit }
										}
									}
								}
							}
						}.ToString(Formatting.None)
					}
				}.ToString(Formatting.None);
				string signedFormBody = ConfigDescriptorState.ReadAndFreeNativeString(SignSearchRequest(xiamiCookieHeader, requestBody, DatabaseMapper.UrlEncodeUtf8(requestBody)));
				var (responseBody, responseCookies) = PostForm(searchEndpoint, signedFormBody);
				try
				{
					List<XiamiSong> songs = ParseSongs(responseBody);
					UpdateXiamiCookieHeader(responseCookies);
					return (!cancellationSource.IsCancellationRequested) ? songs : new List<XiamiSong>();
				}
				catch (Exception parseError)
				{
					Console.WriteLine("SearchMusic (1) error:" + parseError.GetMessageChain());
					switch (attemptIndex)
					{
					case 1:
						UpdateXiamiCookieHeader(null);
						break;
					case 0:
						UpdateXiamiCookieHeader(responseCookies);
						if (responseCookies == null)
						{
							attemptIndex++;
						}
						break;
					}
				}
			}
		}
		catch (Exception searchError)
		{
			Console.WriteLine("SearchMusic error:" + searchError.GetMessageChain());
		}
		return new List<XiamiSong>();
	}

	private static List<XiamiSong> ParseSongs(string responseBody)
	{
		List<XiamiSong> songs = new List<XiamiSong>();
		JToken songListJson = JObject.Parse(responseBody)["data"]?["data"]?["songs"];
		if (songListJson?.Type != JTokenType.Array)
		{
			throw new InvalidOperationException("Xiami song list is missing.");
		}

		foreach (JToken songJson in songListJson)
		{
			if (songJson?.Type != JTokenType.Object)
			{
				continue;
			}

			try
			{
				XiamiSong song = ParseSongSearchResult(songJson);
				if (!string.IsNullOrWhiteSpace(song.SongId))
				{
					songs.Add(song);
				}
			}
			catch (Exception itemParseError)
			{
				Console.WriteLine("ParseSongsJson item error:" + itemParseError.GetMessageChain());
			}
		}
		return songs;
	}

	private static XiamiSong ParseSongSearchResult(JToken songJson)
	{
		XiamiSong song = new XiamiSong
		{
			SongId = GetJsonString(songJson, "songId").Trim(),
			SongName = GetJsonString(songJson, "songName"),
			Subtitle = GetJsonString(songJson, "subName"),
			NewSubtitle = GetJsonString(songJson, "newSubName"),
			AlbumId = GetJsonString(songJson, "albumId"),
			ArtistId = GetJsonString(songJson, "artistId"),
			Singers = GetJsonString(songJson, "singers"),
			DiscNumber = GetNullableInt(songJson, "cdSerial"),
			TrackNumber = GetNullableInt(songJson, "track"),
			Songwriters = GetJsonString(songJson, "songwriters"),
			Composer = GetJsonString(songJson, "composer"),
			Arrangement = GetJsonString(songJson, "arrangement"),
			CreatedAt = GetNullableLong(songJson, "gmtCreate"),
			AlbumName = GetJsonString(songJson, "albumName")
		};
		ReadArtistInfo(song, songJson["singerVOs"]);
		ReadLyricInfo(song, songJson["lyricInfo"]);
		song.CoverUrl = NormalizeOptionalResourceUrl(GetJsonString(songJson, "albumLogo"));
		if (!string.IsNullOrWhiteSpace(song.LyricUrl))
		{
			song.LyricUrl = NormalizeResourceUrl(song.LyricUrl);
		}
		return song;
	}

	private static void ReadArtistInfo(XiamiSong song, JToken singerTokens)
	{
		if (singerTokens?.Type != JTokenType.Array)
		{
			return;
		}

		foreach (JToken singerJson in singerTokens)
		{
			XiamiArtist singer = new XiamiArtist
			{
				ArtistId = GetNullableLong(singerJson, "artistId") ?? 0L,
				Name = GetJsonString(singerJson, "artistName"),
				Alias = GetJsonString(singerJson, "alias"),
				Pinyin = GetJsonString(singerJson, "pinyin")
			};
			if (singer.ArtistId > 0L && !string.IsNullOrWhiteSpace(singer.Name))
			{
				song.Artists.Add(singer);
			}
		}
	}

	private static void ReadLyricInfo(XiamiSong song, JToken lyricInfo)
	{
		if (lyricInfo?.Type != JTokenType.Object)
		{
			return;
		}

		song.LyricUrl = NormalizeOptionalResourceUrl(GetJsonString(lyricInfo, "lyricFile"));
		song.LyricType = GetNullableInt(lyricInfo, "lyricType") ?? 0;
	}

	private static string GetJsonString(JToken token, string propertyName)
	{
		if (token?.Type != JTokenType.Object)
		{
			return "";
		}
		return token[propertyName]?.ToString() ?? "";
	}

	private static int? GetNullableInt(JToken token, string propertyName)
	{
		if (token?.Type != JTokenType.Object)
		{
			return null;
		}

		JToken value = token[propertyName];
		if (value == null || value.Type == JTokenType.Null)
		{
			return null;
		}

		int intValue;
		return int.TryParse(value.ToString(), out intValue) ? intValue : (int?)null;
	}

	private static long? GetNullableLong(JToken token, string propertyName)
	{
		if (token?.Type != JTokenType.Object)
		{
			return null;
		}

		JToken value = token[propertyName];
		if (value == null || value.Type == JTokenType.Null)
		{
			return null;
		}

		long longValue;
		return long.TryParse(value.ToString(), out longValue) ? longValue : (long?)null;
	}

	private static string NormalizeOptionalResourceUrl(string resourceUrl)
	{
		if (string.IsNullOrWhiteSpace(resourceUrl) || resourceUrl == "null")
		{
			return "";
		}
		return NormalizeResourceUrl(resourceUrl);
	}

	private static string NormalizeResourceUrl(string resourceUrl)
	{
		resourceUrl = resourceUrl.Trim();
		resourceUrl = Regex.Replace(resourceUrl, "^/{1,2}", "http://");
		if (!resourceUrl.StartsWith("http"))
		{
			resourceUrl = "http://" + resourceUrl;
		}
		return resourceUrl;
	}

	private LyricSearchResult DownloadLyric(XiamiSong song)
	{
		if (string.IsNullOrWhiteSpace(song.LyricUrl))
		{
			return null;
		}
		string lyricText = GetResponseString(song.LyricUrl);
		if (string.IsNullOrWhiteSpace(lyricText))
		{
			return null;
		}
		LyricSearchResult lyric = ParseLyric(song.LyricType, lyricText);
		lyric.LyricUrl = song.LyricUrl;
		lyric.TrackId = song.SongId;
		lyric.Title = song.SongName;
		lyric.Artist = song.GetArtistName();
		lyric.Album = song.AlbumName;
		lyric.SearchSource = GetSource();
		if (cancellationSource.IsCancellationRequested)
		{
			return null;
		}
		return lyric;
	}

	public LyricSearchResult DownloadLyric(TrackSearchResult track)
	{
		if (track.LyricResult == null)
		{
			return null;
		}
		XiamiSong song = new XiamiSong
		{
			SongId = track.SourceTrackId,
			LyricUrl = track.LyricResult.LyricUrl,
			LyricType = track.LyricResult.LyricType,
			SongName = track.Title,
			Singers = track.Artist,
			AlbumName = track.Album
		};
		return DownloadLyric(song);
	}
	
	private static LyricSearchResult ParseLyric(int lyricType, string lyricText)
	{
		(string lyric, string translation) parsedLyric = (null, null);
		switch (lyricType)
		{
		case 3:
			parsedLyric.lyric = Regex.Replace(lyricText, "<\\d+>", "");
			break;
		case 4:
			parsedLyric = LyricTextProcessor.SplitXiamiTranslatedLyric(lyricText);
			break;
		case 7:
			parsedLyric = LyricTextProcessor.SplitXiamiTranslatedLyric(Regex.Replace(lyricText, "<\\d+>", ""));
			break;
		default:
			parsedLyric.lyric = lyricText;
			break;
		}
		LyricSearchResult lyric = new LyricSearchResult();
		lyric.LyricType = lyricType;
		lyric.Lyric = parsedLyric.lyric;
		lyric.TranslatedLyric = parsedLyric.translation;
		return lyric;
	}

	private (string responseBody, List<Cookie> cookies) PostForm(string url, string formBody)
	{
		(string responseBody, List<Cookie> cookies) result = (null, null);
		try
		{
			Uri uri = new Uri(url);
			byte[] bytes = Encoding.ASCII.GetBytes(formBody);
			HttpResponseMessage httpResponseMessage = null;
			using (HttpContent httpContent = new ByteArrayContent(bytes))
			{
				httpContent.Headers.Add("Content-Type", "application/x-www-form-urlencoded");
				httpResponseMessage = GetHttpClient().PostAsync(uri, httpContent, cancellationSource.Token).Result;
			}
			if (httpResponseMessage != null && httpResponseMessage.StatusCode == HttpStatusCode.OK)
			{
				result.cookies = new List<Cookie>();
				result.cookies.AddRange(cookieContainer.GetCookies(uri).Cast<Cookie>());
				result.responseBody = httpResponseMessage.Content.ReadAsStringAsync().Result;
				httpResponseMessage.Dispose();
			}
		}
		catch (Exception postError)
		{
			Console.WriteLine("PostHttp error:" + postError.GetMessageChain());
		}
		return result;
	}

	private void UpdateXiamiCookieHeader(List<Cookie> cookies)
	{
		if (cookies == null)
		{
			cookies = PostForm(searchEndpoint, initialRequestPayload).Item2;
		}
		if (cookies == null)
		{
			return;
		}
		Dictionary<string, Cookie> cookiesByName = new Dictionary<string, Cookie>();
		foreach (Cookie cookie in cookies)
		{
			if (!cookiesByName.ContainsKey(cookie.Name))
			{
				cookiesByName.Add(cookie.Name, cookie);
			}
		}
		StringBuilder cookieHeader = new StringBuilder();
		if (cookiesByName.TryGetValue("_m_h5_tk", out var tokenCookie))
		{
			cookieHeader.Append(tokenCookie.Name + "=" + tokenCookie.Value + "; ");
			if (cookiesByName.TryGetValue("_m_h5_tk_enc", out var encryptedTokenCookie))
			{
				cookieHeader.Append(encryptedTokenCookie.Name + "=" + encryptedTokenCookie.Value);
				xiamiCookieHeader = cookieHeader.ToString();
			}
		}
	}

	private static List<Cookie> ParseCookieHeader(string cookieHeader)
	{
		List<Cookie> cookies = new List<Cookie>();
		foreach (string cookiePart in cookieHeader.Split(';'))
		{
			string[] nameValue = cookiePart.Split('=');
			if (nameValue.Length == 2)
			{
				cookies.Add(new Cookie(nameValue[0].Trim(), nameValue[1].Trim(), "/", "xiami.com"));
			}
		}
		return cookies;
	}

	[DllImport("MusicTag.dll", CharSet = CharSet.Unicode, EntryPoint = "oa")]
	private static extern IntPtr SignSearchRequest(string cookieHeader, string requestJson, string requestHash);

	[DllImport("MusicTag.dll", EntryPoint = "ob")]
	private static extern IntPtr GetInitialRequestPayloadPointer();

	[DllImport("MusicTag.dll", EntryPoint = "oc")]
	private static extern IntPtr GetSearchEndpointPointer();

	[DllImport("MusicTag.dll", EntryPoint = "od")]
	private static extern IntPtr GetRefererPointer();

	[DllImport("MusicTag.dll", EntryPoint = "oe")]
	private static extern IntPtr GetDefaultCookieHeaderPointer();

	static XiamiTagProvider()
	{
		searchEndpoint = Marshal.PtrToStringUni(GetSearchEndpointPointer());
		initialRequestPayload = Marshal.PtrToStringUni(GetInitialRequestPayloadPointer());
		defaultCookieHeader = Marshal.PtrToStringUni(GetDefaultCookieHeaderPointer());
	}

}
