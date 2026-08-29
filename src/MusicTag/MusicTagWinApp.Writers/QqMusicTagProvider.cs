using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MusicTagWinApp.Writers;

internal class QqMusicTagProvider : RemoteTagProviderBase, ITrackSearchProvider, ITrackIdLookupProvider, ILyricSearchProvider, ICoverSearchProvider, ITrackLyricLoader
{
	// 显式接口实现:把能力接口的统一签名(网易云超集)转发到本类既有 concrete,丢弃 QQ 不接收的 knownSongId / existingLyrics。
	// concrete 方法体与签名一字未动;SearchCovers / LoadLyricsForTrack 因签名匹配而隐式实现。
	List<TrackSearchResult> ITrackSearchProvider.SearchTracks(string query, int resultLimit, long knownSongId, int searchPass, int sourceOrder, List<TrackSearchResult> existingTracks, List<TrackSearchResult> previousResults)
	{
		return SearchTracks(query, resultLimit, searchPass, sourceOrder, existingTracks, previousResults);
	}

	List<LyricSearchResult> ILyricSearchProvider.SearchLyrics(string query, int resultLimit, long knownSongId, List<LyricSearchResult> existingLyrics, int sourceOrder)
	{
		return SearchLyrics(query, resultLimit, sourceOrder);
	}

	private const string EmptyLyricPlaceholderBase64 = "WzAwOjAwOjAwXeatpOatjOabsuS4uuayoeacieWhq+ivjeeahOe6r+mfs+S5kO+8jOivt+aCqOaso+i1jw==";

	private const string callbackName = "MusicJsonCallback34475857153687595";

	private const string searchEndpointUrl = "https://u.y.qq.com/cgi-bin/musicu.fcg";

	private const string songDetailUrlFormat = "https://c.y.qq.com/v8/fcg-bin/fcg_play_single_song.fcg?{0}={1}&tpl=yqq_song_detail&format=json";

	private const string searchRequestTemplate = "{{\"{0}\":{{\"method\":\"DoSearchForQQMusicDesktop\",\"module\":\"music.search.SearchCgiService\",\"param\":{{\"search_type\":0,\"query\":\"{1}\",\"page_num\":1,\"num_per_page\":{2}}}}}}}";

	private const string lyricUrlTemplate = "https://c.y.qq.com/lyric/fcgi-bin/fcg_query_lyric_new.fcg?songmid={0}&g_tk=5381&jsonpCallback={1}&format=jsonp";

	private const string qrcLyricEndpointUrl = "https://u.y.qq.com/cgi-bin/musicu.fcg";

	private const string albumCoverUrlTemplate = "https://y.qq.com/music/photo_new/T002R800x800M000{0}.jpg";

	private static readonly Regex callbackJsonRegex = new Regex(Regex.Escape(callbackName) + "\\((.+)\\)", RegexOptions.Compiled);

	// QQ 限流(2001)重试的倒计时缓冲:实际等待 = 显示秒数 * 1000 + 该值。多留这点缓冲,
	// 使倒计时计时器能在等待结束前数到 0 再发起重试(否则秒数会停在 1,跳不到 0)。
	private const int RetryCountdownBufferMs = 300;

	private static readonly object searchCacheLock = new object();

	private static readonly Dictionary<string, CachedSongSearch> searchCache = new Dictionary<string, CachedSongSearch>(StringComparer.Ordinal);

	private static readonly Dictionary<string, Lazy<List<QqSongInfo>>> inFlightSearches = new Dictionary<string, Lazy<List<QqSongInfo>>>(StringComparer.Ordinal);

	private sealed class CachedSongSearch
	{
		public List<QqSongInfo> Songs;

		public DateTime ExpiresUtc;
	}

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

	protected virtual bool WaitForLyricRetryDelay(int waitMilliseconds)
	{
		return cancellationSource.Token.WaitHandle.WaitOne(waitMilliseconds);
	}

	// 测试 provider 覆盖此开关以跳过真实的进程级等待和缓存;生产 provider 始终启用。
	protected virtual bool UseSharedRequestCoordination => true;

	private List<QqSongInfo> SearchSongs(string query, int maxResults)
	{
		if (!UseSharedRequestCoordination)
		{
			return SearchSongsCore(query, maxResults);
		}

		string cacheKey = BuildSearchCacheKey(query, maxResults);
		Lazy<List<QqSongInfo>> search;
		lock (searchCacheLock)
		{
			if (searchCache.TryGetValue(cacheKey, out CachedSongSearch cached))
			{
				if (cached.ExpiresUtc > DateTime.UtcNow)
				{
					return new List<QqSongInfo>(cached.Songs);
				}
				searchCache.Remove(cacheKey);
			}

			if (!inFlightSearches.TryGetValue(cacheKey, out search))
			{
				search = new Lazy<List<QqSongInfo>>(() => SearchSongsCore(query, maxResults), LazyThreadSafetyMode.ExecutionAndPublication);
				inFlightSearches.Add(cacheKey, search);
			}
		}

		try
		{
			List<QqSongInfo> songs = search.Value ?? new List<QqSongInfo>();
			if (songs.Count > 0)
			{
				lock (searchCacheLock)
				{
					searchCache[cacheKey] = new CachedSongSearch
					{
						Songs = new List<QqSongInfo>(songs),
						ExpiresUtc = DateTime.UtcNow.AddMinutes(10)
					};
				}
			}
			return new List<QqSongInfo>(songs);
		}
		finally
		{
			lock (searchCacheLock)
			{
				if (inFlightSearches.TryGetValue(cacheKey, out Lazy<List<QqSongInfo>> current) && ReferenceEquals(current, search))
				{
					inFlightSearches.Remove(cacheKey);
				}
			}
		}
	}

	private static string BuildSearchCacheKey(string query, int maxResults)
	{
		string normalizedQuery = Regex.Replace((query ?? "").Trim(), "\\s+", " ").ToUpperInvariant();
		string cookie = Settings.Default.QQMusic_Cookie ?? "";
		string cookieHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cookie)));
		return normalizedQuery + "|" + maxResults.ToString(CultureInfo.InvariantCulture) + "|" + cookieHash;
	}

	private List<QqSongInfo> SearchSongsCore(string query, int maxResults)
	{
		string requestBody = BuildSearchRequestBody(query, maxResults);
		const int maxAttempts = 6;
		for (int attempt = 0; attempt < maxAttempts; attempt++)
		{
			if (cancellationSource.IsCancellationRequested)
			{
				return new List<QqSongInfo>();
			}

			if (UseSharedRequestCoordination)
			{
				int cooldownSeconds;
				QqRequestPermit permit = QqRequestCoordinator.WaitForPermit(cancellationSource.Token, out cooldownSeconds);
				if (permit == QqRequestPermit.Cancelled)
				{
					return new List<QqSongInfo>();
				}
				if (permit == QqRequestPermit.CoolingDown)
				{
					SetTransportError(RemoteErrorKind.RateLimited, "2001");
					ReportStatus(SourceSearchPhase.CoolingDown, "2001", cooldownSecondsLeft: cooldownSeconds);
					return new List<QqSongInfo>();
				}
			}

			string responseBody = PostString(searchEndpointUrl, requestBody, null, postJson: true);
			if (cancellationSource.IsCancellationRequested)
			{
				return new List<QqSongInfo>();
			}

			JObject parsedResponse = TryParseJsonObject(responseBody);
			if (attempt + 1 < maxAttempts && IsRateLimited(parsedResponse))
			{
				if (UseSharedRequestCoordination)
				{
					int cooldownSeconds = QqRequestCoordinator.RecordRateLimited();
					SetTransportError(RemoteErrorKind.RateLimited, "2001");
					Console.WriteLine($"QQ search throttled (req_0.code 2001), cooldown {cooldownSeconds}s");
					ReportStatus(SourceSearchPhase.CoolingDown, "2001", cooldownSecondsLeft: cooldownSeconds);
					return new List<QqSongInfo>();
				}
				int retryNumber = attempt + 1;
				int retryTotal = maxAttempts - 1;
				// 退避:2/4/4/4/4 秒(前两次 2、4,其后封顶 4;应用户要求缩短第 3–5 次等待)。
				// 倒计时按整秒显示,实际等待多留缓冲(见 RetryCountdownBufferMs),确保数到 0 再重试。
				int countdownSeconds = Math.Min(1 << (attempt + 1), 4);
				int waitMilliseconds = countdownSeconds * 1000 + RetryCountdownBufferMs;
				Console.WriteLine($"QQ search throttled (req_0.code 2001), retry {retryNumber}/{retryTotal}");
				ReportStatus(SourceSearchPhase.Retrying, "2001", retryNumber, retryTotal, countdownSeconds);
				cancellationSource.Token.WaitHandle.WaitOne(waitMilliseconds);
				continue;
			}

			if (IsRateLimited(parsedResponse))
			{
				// 重试用尽仍被限流:回填业务码,让上层把本源标记为出错(2001)而非"0 条结果"。
				SetTransportError(RemoteErrorKind.RateLimited, "2001");
			}
			else if (parsedResponse == null && !string.IsNullOrWhiteSpace(responseBody))
			{
				// HTTP 200 拿到响应体却无法解析为 JSON:区分"解析失败"与"搜到 0 条 / 网络失败"。
				// (传输失败时 responseBody 为空,已由传输层归类为 Network/Timeout/HttpStatus。)
				SetTransportError(RemoteErrorKind.ParseFailed, "parse");
			}
			return ParseSongSearchResponse(parsedResponse);
		}

		return new List<QqSongInfo>();
	}

	private static JObject TryParseJsonObject(string responseBody)
	{
		if (string.IsNullOrWhiteSpace(responseBody))
		{
			return null;
		}

		try
		{
			return JObject.Parse(responseBody);
		}
		catch (Exception)
		{
			return null;
		}
	}

	internal static bool IsRateLimited(JObject parsedResponse)
	{
		JToken codeToken = parsedResponse?["req_0"]?["code"];
		return codeToken != null && codeToken.Type == JTokenType.Integer && (int)codeToken == 2001;
	}

	public List<LyricSearchResult> SearchLyrics(string query, int maxResults, int sourceOrder)
	{
		return BuildOrderedLyrics<QqSongInfo>(SearchSongs(query, maxResults), LoadLyrics, sourceOrder);
	}

	public List<CoverSearchResult> SearchCovers(string query, int maxResults, List<CoverSearchResult> existingCovers)
	{
		return BuildDedupedCovers<QqSongInfo>(SearchSongs(query, maxResults), BuildCoverResult, existingCovers);
	}

	private CoverSearchResult BuildCoverResult(QqSongInfo songInfo)
	{
		string coverUrl = string.Format(albumCoverUrlTemplate, songInfo.Album.Mid);
		CoverSearchResult cover = new CoverSearchResult();
		cover.CoverUrl = coverUrl;
		cover.SearchSource = GetSource();
		cover.CoverDownloader = CreateCoverDownloader<QqMusicTagProvider>(coverUrl);
		return cover;
	}

	public List<TrackSearchResult> SearchTracks(string query, int maxResults, int searchPass, int sourceOrder, List<TrackSearchResult> existingTracks, List<TrackSearchResult> previousResults)
	{
		return BuildOrderedTracks<QqSongInfo>(SearchSongs(query, maxResults), BuildTrackResult, searchPass, sourceOrder, existingTracks, previousResults);
	}

	public TrackSearchResult LookupTrackById(string trackId, int sourceOrder)
	{
		string normalizedId = TrackIdInput.ExtractLastPathOrQueryValue(trackId, "songmid", "songid", "id");
		if (string.IsNullOrWhiteSpace(normalizedId))
		{
			return null;
		}
		string idParameter = normalizedId.All(char.IsDigit) ? "songid" : "songmid";
		string responseBody = GetResponseString(string.Format(songDetailUrlFormat, idParameter, TextUtilities.UrlEncodeUtf8(normalizedId)));
		if (cancellationSource.IsCancellationRequested)
		{
			return null;
		}
		try
		{
			JToken songJson = JObject.Parse(responseBody)?["data"]?.First;
			if (songJson == null || songJson.Type != JTokenType.Object)
			{
				return null;
			}
			QqSongInfo song = ParseSongSearchResult(songJson);
			if (song.Id <= 0L || string.IsNullOrWhiteSpace(song.Mid) || string.IsNullOrWhiteSpace(song.Title))
			{
				return null;
			}
			TrackSearchResult track = BuildTrackResult(song);
			track.ResultOrder = 0;
			track.SearchPass = 0;
			track.SourceOrder = sourceOrder;
			return track;
		}
		catch (Exception parseError)
		{
			Console.WriteLine("Parse QQ song detail error:" + parseError.GetMessageChain());
			if (!string.IsNullOrWhiteSpace(responseBody))
			{
				SetTransportError(RemoteErrorKind.ParseFailed, "parse");
			}
			return null;
		}
	}

	private TrackSearchResult BuildTrackResult(QqSongInfo songInfo)
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
			track.Year = releaseDate.ToString("yyyy", CultureInfo.InvariantCulture);
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

		track.Cover = BuildCoverResult(songInfo);

		LyricSearchResult lyric = new LyricSearchResult();
		lyric.LyricUrl = string.Format(lyricUrlTemplate, songInfo.Mid, callbackName);
		lyric.SearchSource = GetSource();
		lyric.DeferredLyricLoader = cancellation =>
		{
			using QqMusicTagProvider qqProvider = new QqMusicTagProvider(cancellation);
			return qqProvider.LoadLyrics(songInfo);
		};
		track.LyricResult = lyric;
		return track;
	}

	private LyricSearchResult LoadLyrics(QqSongInfo songInfo)
	{
		string qrcRequestBody = BuildQrcLyricRequestBody(songInfo);
		bool qrcRateLimited = false;
		const int maxAttempts = 2;
		for (int attempt = 0; attempt < maxAttempts; attempt++)
		{
			if (UseSharedRequestCoordination)
			{
				int cooldownSeconds;
				QqRequestPermit permit = QqRequestCoordinator.WaitForPermit(cancellationSource.Token, out cooldownSeconds);
				if (permit == QqRequestPermit.Cancelled)
				{
					return null;
				}
				if (permit == QqRequestPermit.CoolingDown)
				{
					SetTransportError(RemoteErrorKind.RateLimited, "2001");
					ReportStatus(SourceSearchPhase.CoolingDown, "2001", cooldownSecondsLeft: cooldownSeconds);
					return null;
				}
			}

			string qrcResponseBody = PostString(qrcLyricEndpointUrl, qrcRequestBody, null, postJson: true);
			if (cancellationSource.IsCancellationRequested)
			{
				return null;
			}

			qrcRateLimited = IsRateLimited(TryParseJsonObject(qrcResponseBody));
			if (qrcRateLimited && UseSharedRequestCoordination)
			{
				int cooldownSeconds = QqRequestCoordinator.RecordRateLimited();
				SetTransportError(RemoteErrorKind.RateLimited, "2001");
				Console.WriteLine($"QQ lyric throttled (req_0.code 2001), cooldown {cooldownSeconds}s");
				ReportStatus(SourceSearchPhase.CoolingDown, "2001", cooldownSecondsLeft: cooldownSeconds);
				return null;
			}
			if (qrcRateLimited && attempt + 1 < maxAttempts)
			{
				int retryNumber = attempt + 1;
				int retryTotal = maxAttempts - 1;
				const int countdownSeconds = 1;
				int waitMilliseconds = countdownSeconds * 1000 + RetryCountdownBufferMs;
				Console.WriteLine($"QQ lyric throttled (req_0.code 2001), retry {retryNumber}/{retryTotal}");
				ReportStatus(SourceSearchPhase.Retrying, "2001", retryNumber, retryTotal, countdownSeconds);
				if (WaitForLyricRetryDelay(waitMilliseconds))
				{
					return null;
				}
				continue;
			}

			if (!qrcRateLimited)
			{
				LyricSearchResult qrcLyric = CreateQrcLyricResult(songInfo, qrcResponseBody);
				if (qrcLyric != null)
				{
					return qrcLyric;
				}
			}
			break;
		}

		string responseBody = GetResponseString(string.Format(lyricUrlTemplate, songInfo.Mid, callbackName));
		if (cancellationSource.IsCancellationRequested)
		{
			return null;
		}

		LyricSearchResult legacyLyric = CreateLyricResult(songInfo, responseBody);
		if (legacyLyric == null && qrcRateLimited && (LastTransportResult == null || LastTransportResult.IsSuccess))
		{
			SetTransportError(RemoteErrorKind.RateLimited, "2001");
		}
		return legacyLyric;
	}

	private static string BuildSearchRequestBody(string query, int maxResults)
	{
		JObject request = JObject.Parse(string.Format(searchRequestTemplate, "req_0", TextEncodingService.JavaScriptStringEncode(query), maxResults));
		Dictionary<string, string> cookies = ParseCookieHeader(Settings.Default.QQMusic_Cookie);
		string uin = GetCookieValue(cookies, "loginUin", "uin", "p_uin", "euin", "p_euin");
		string authst = GetCookieValue(cookies, "authst", "qm_keyst", "qqmusic_key", "qqmusic_key_new");
		string loginType = GetCookieValue(cookies, "tmeLoginType");
		if (uin.Length == 0 && authst.Length == 0 && loginType.Length == 0)
		{
			return request.ToString(Formatting.None);
		}

		JObject comm = new JObject
		{
			["format"] = "json",
			["ct"] = 19,
			["cv"] = 0
		};
		if (uin.Length > 0)
		{
			request["loginUin"] = uin;
			comm["uin"] = uin;
		}
		if (authst.Length > 0)
		{
			comm["authst"] = authst;
		}
		if (loginType.Length > 0)
		{
			comm["tmeLoginType"] = loginType;
		}
		request["comm"] = comm;
		return request.ToString(Formatting.None);
	}

	private static Dictionary<string, string> ParseCookieHeader(string cookieHeader)
	{
		Dictionary<string, string> cookies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (string.IsNullOrWhiteSpace(cookieHeader))
		{
			return cookies;
		}

		foreach (string segment in cookieHeader.Split(';'))
		{
			int separator = segment.IndexOf('=');
			if (separator <= 0)
			{
				continue;
			}
			string name = segment.Substring(0, separator).Trim();
			string value = segment.Substring(separator + 1).Trim();
			if (name.Length > 0 && value.Length > 0)
			{
				cookies[name] = value.Trim('"');
			}
		}
		return cookies;
	}

	private static string GetCookieValue(Dictionary<string, string> cookies, params string[] names)
	{
		foreach (string name in names)
		{
			if (cookies.TryGetValue(name, out string value) && !string.IsNullOrWhiteSpace(value))
			{
				return value.Trim();
			}
		}
		return "";
	}

	private static string BuildQrcLyricRequestBody(QqSongInfo songInfo)
	{
		string EncodeText(string value)
		{
			return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? ""));
		}

		JObject request = new JObject
		{
			["comm"] = new JObject
			{
				["ct"] = 24,
				["cv"] = 0,
				["format"] = "json",
				["uin"] = 0
			},
			["req_0"] = new JObject
			{
				["module"] = "music.musichallSong.PlayLyricInfo",
				["method"] = "GetPlayLyricInfo",
				["param"] = new JObject
				{
					["albumName"] = EncodeText(songInfo?.Album?.Name),
					["crypt"] = 1,
					["ct"] = 19,
					["cv"] = 2111,
					["interval"] = 0,
					["lrc_t"] = 0,
					["qrc"] = 1,
					["qrc_t"] = 0,
					["roma"] = 0,
					["roma_t"] = 0,
					["singerName"] = EncodeText(songInfo?.GetArtistNames()),
					["songID"] = songInfo?.Id ?? 0L,
					["songName"] = EncodeText(songInfo?.Title),
					["trans"] = 1,
					["trans_t"] = 0,
					["type"] = 0
				}
			}
		};

		return request.ToString(Formatting.None);
	}

	private LyricSearchResult CreateQrcLyricResult(QqSongInfo songInfo, string responseBody)
	{
		try
		{
			LyricSearchResult lyric = ParseQrcLyricResponse(responseBody);
			if (lyric == null)
			{
				return null;
			}

			PopulateLyricMetadata(lyric, songInfo);
			return lyric;
		}
		catch (Exception parseError)
		{
			Console.WriteLine("Parse QQ QRC lyric error:" + parseError.GetMessageChain());
			return null;
		}
	}

	private static LyricSearchResult ParseQrcLyricResponse(string responseBody)
	{
		JToken lyricData = JObject.Parse(responseBody)?["req_0"]?["data"];
		string lyricText = DecodeQrcField(lyricData?["lyric"]);
		if (string.IsNullOrWhiteSpace(lyricText))
		{
			return null;
		}

		bool useThreeDigitMilliseconds = !Settings.Default.LyricDownload_ReformatTimetag;
		LyricSearchResult lyric = new LyricSearchResult
		{
			Lyric = QqQrcDecoder.ConvertToLineLyric(lyricText, useThreeDigitMilliseconds),
			TranslatedLyric = QqQrcDecoder.ConvertToLineLyric(DecodeQrcField(lyricData?["trans"]), useThreeDigitMilliseconds)
		};

		if (string.IsNullOrWhiteSpace(lyric.Lyric))
		{
			return null;
		}

		if (!string.IsNullOrWhiteSpace(lyric.TranslatedLyric))
		{
			string mergedTranslation;
			(lyric.Lyric, mergedTranslation) = new LyricTextProcessor(lyric.Lyric).AlignAndSplitTranslatedLyric(new LyricTextProcessor(lyric.TranslatedLyric));
			lyric.TranslatedLyric = mergedTranslation;
		}

		return lyric;
	}

	private static string DecodeQrcField(JToken encryptedField)
	{
		string encryptedLyrics = encryptedField?.ToString();
		if (!QqQrcDecoder.LooksEncryptedLyrics(encryptedLyrics))
		{
			return "";
		}

		return QqQrcDecoder.DecryptLyrics(encryptedLyrics);
	}

	public LyricSearchResult LoadLyricsForTrack(TrackSearchResult track)
	{
		if (!long.TryParse(track.SourceTrackId, out var sourceTrackId))
		{
			return null;
		}

		QqSongInfo songInfo = new QqSongInfo
		{
			Id = sourceTrackId,
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
		Match match = callbackJsonRegex.Match(value.Trim());
		if (!match.Success)
		{
			return "";
		}
		return match.Groups[1].Value;
	}

	private List<QqSongInfo> ParseSongSearchResponse(JObject parsedResponse)
	{
		List<QqSongInfo> songs = new List<QqSongInfo>();
		try
		{
			JToken songListJson = parsedResponse?["req_0"]?["data"]?["body"]?["song"]?["list"];
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
		songInfo.ReleaseDate = GetStringField(songJson, "time_public");
		songInfo.Title = TextEncodingService.DecodeBasicHtmlEntities(GetStringField(songJson, "title"));
		songInfo.Subtitle = TextEncodingService.DecodeBasicHtmlEntities(GetStringField(songJson, "subtitle"));

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
				Name = GetStringField(singer, "name")
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

			PopulateLyricMetadata(lyric, songInfo);
		}
		catch (Exception parseError)
		{
			Console.WriteLine("ParseLyricJson error:" + parseError.GetMessageChain());
		}

		return lyric;
	}

	private void PopulateLyricMetadata(LyricSearchResult lyric, QqSongInfo songInfo)
	{
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

	internal static (string Lyric, string Translation) DecodeLyricPayload(string responseBody)
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
			lyric = TextUtilities.DecodeBase64String(encodedLyric, "UTF-8");
		}

		if (!string.IsNullOrWhiteSpace(encodedTranslation))
		{
			translation = TextUtilities.DecodeBase64String(encodedTranslation, "UTF-8");
		}

		return (lyric, translation);
	}
}
