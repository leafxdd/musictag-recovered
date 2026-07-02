using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using MusicTag.Composer;
using MusicTag.Serialization;
using MusicTag.Services;
using MusicTagWinApp.Instances;
using MusicTagWinApp.Listeners;
using MusicTagWinApp.Roles;
using MusicTagWinApp.Web;
using Newtonsoft.Json.Linq;

namespace MusicTagWinApp.Adapter;

internal class KuwoTagProvider : RemoteTagProviderBase, ITrackSearchProvider, ILyricSearchProvider, ICoverSearchProvider, ITrackLyricLoader
{
	// 显式接口实现:把能力接口的统一签名(网易云超集)转发到本类既有 concrete,丢弃酷我不接收的 knownSongId / existingLyrics;
	// LoadLyricsForTrack 转发到酷我单数名 concrete LoadLyricForTrack。concrete 方法体与签名一字未动;SearchCovers 隐式实现。
	List<TrackSearchResult> ITrackSearchProvider.SearchTracks(string query, int resultLimit, long knownSongId, int searchPass, int sourceOrder, List<TrackSearchResult> existingTracks, List<TrackSearchResult> previousResults)
	{
		return SearchTracks(query, resultLimit, searchPass, sourceOrder, existingTracks, previousResults);
	}

	List<LyricSearchResult> ILyricSearchProvider.SearchLyrics(string query, int resultLimit, long knownSongId, List<LyricSearchResult> existingLyrics, int sourceOrder)
	{
		return SearchLyrics(query, resultLimit, sourceOrder);
	}

	LyricSearchResult ITrackLyricLoader.LoadLyricsForTrack(TrackSearchResult track)
	{
		return LoadLyricForTrack(track);
	}

	private const int DetailApiRetryIntervalMs = 300000;

	private const string SearchUrlFormat = "https://search.kuwo.cn/r.s?all={0}&client=kt&pn=0&rn={1}&ver=kwplayer_ar_9.2.3.2&vipver=1&show_copyright_off=1&newver=1&correct=1&ft=music&cluster=0&strategy=2012&encoding=utf8&rformat=json&vermerge=1&mobi=1&issubtitle=1";

	private const string SongDetailUrlFormat = "https://m.kuwo.cn/newh5/singles/songinfoandlrc?musicId={0}";

	private const string AlbumCoverUrlPrefix = "https://img2.kuwo.cn/star/albumcover/";

	private static bool? detailApiUnavailable;

	private static int lastDetailApiCheckTick;

	// 本实例(本次搜索)期间是否因详情接口熔断/不可用而拿不到封面与歌词,供 UI 单独提示。
	private bool detailUnavailableThisSearch;

	public bool DetailApiUnavailableThisSearch => detailUnavailableThisSearch;

	protected override SearchSource GetSource()
	{
		return SearchSource.Kuwo;
	}

	protected override HttpClient CreateHttpClient()
	{
		HttpClient httpClient = new HttpClient(new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
		})
		{
			Timeout = TimeSpan.FromSeconds(10.0)
		};
		httpClient.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3");
		httpClient.DefaultRequestHeaders.Add("accept-language", "zh-CN,zh;q=0.9,en;q=0.8");
		httpClient.DefaultRequestHeaders.Add("referer", "https://kuwo.cn");
		httpClient.DefaultRequestHeaders.Add("user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
		return httpClient;
	}

	public KuwoTagProvider(CancellationTokenSource cancellation)
		: base(cancellation)
	{
	}

	public KuwoTagProvider()
		: this(null)
	{
	}

	public List<TrackSearchResult> SearchTracks(string query, int maxResults, int searchPass, int sourceOrder, List<TrackSearchResult> previousResults, List<TrackSearchResult> currentResults)
	{
		return BuildOrderedTracks<KuwoSongInfo>(SearchSongs(query, maxResults), CreateTrackResult, searchPass, sourceOrder, previousResults, currentResults);
	}

	public List<LyricSearchResult> SearchLyrics(string query, int maxResults, int sourceOrder)
	{
		return BuildOrderedLyrics<KuwoSongInfo>(SearchSongs(query, maxResults), LoadSongLyric, sourceOrder);
	}

	private LyricSearchResult LoadSongLyric(KuwoSongInfo song)
	{
		LoadSongDetails(song);
		return song.LoadedLyric;
	}

	public LyricSearchResult LoadLyricForTrack(TrackSearchResult track)
	{
		KuwoSongInfo song = new KuwoSongInfo
		{
			TrackId = track.SourceTrackId,
			Title = track.Title,
			Artist = track.Artist,
			Album = track.Album,
			OriginalTitle = track.OriginalTitle
		};
		LoadSongDetails(song);
		return song.LoadedLyric;
	}

	public List<CoverSearchResult> SearchCovers(string query, int maxResults, List<CoverSearchResult> existingCovers)
	{
		return BuildDedupedCovers<KuwoSongInfo>(SearchSongs(query, maxResults), song => CreateCoverResult(song, null), existingCovers);
	}

	private TrackSearchResult CreateTrackResult(KuwoSongInfo song)
	{
		string detailUrl = string.Format(SongDetailUrlFormat, song.TrackId);
		TrackSearchResult track = new TrackSearchResult
		{
			SearchSource = GetSource(),
			SourceTrackId = song.TrackId,
			Title = song.Title,
			OriginalTitle = song.OriginalTitle,
			Artist = song.Artist,
			Album = song.Album
		};
		track.Cover = CreateCoverResult(song, track);
		track.LyricResult = new LyricSearchResult
		{
			LyricUrl = detailUrl,
			DeferredLyricLoader = cancellation => LoadDeferredLyric(song, track, cancellation)
		};
		return track;
	}

	// 封面候选:优先用搜索结果直带的专辑封面直链(绕开限流的详情 API),
	// 仅当其缺失时回退到老的“二次请求 songinfoandlrc 取 pic”方式(track 用于回退路径的歌词并发锁)。
	private CoverSearchResult CreateCoverResult(KuwoSongInfo song, TrackSearchResult track)
	{
		if (!string.IsNullOrWhiteSpace(song.SearchAlbumCoverUrl))
		{
			return new CoverSearchResult
			{
				CoverUrl = song.SearchAlbumCoverUrl,
				SearchSource = GetSource(),
				CoverDownloader = CreateCoverDownloader<KuwoTagProvider>(song.SearchAlbumCoverUrl)
			};
		}

		return new CoverSearchResult
		{
			CoverUrl = string.Format(SongDetailUrlFormat, song.TrackId),
			SearchSource = GetSource(),
			CoverDownloader = (cancellation, filePath, requestTimeout) => DownloadDeferredCover(song, track, cancellation, filePath)
		};
	}

	private static (DownloadStatus, long) DownloadDeferredCover(KuwoSongInfo song, TrackSearchResult track, CancellationTokenSource cancellation, string filePath)
	{
		using KuwoTagProvider provider = new KuwoTagProvider(cancellation);
		if (track == null)
		{
			provider.LoadSongDetails(song);
		}
		else
		{
			lock (track)
			{
				provider.LoadSongDetails(song);
			}
		}

		return provider.DownloadLoadedCover(song, filePath);
	}

	private static LyricSearchResult LoadDeferredLyric(KuwoSongInfo song, TrackSearchResult track, CancellationTokenSource cancellation)
	{
		using KuwoTagProvider provider = new KuwoTagProvider(cancellation);
		lock (track)
		{
			provider.LoadSongDetails(song);
		}

		return song.LoadedLyric;
	}

	private (DownloadStatus, long) DownloadLoadedCover(KuwoSongInfo song, string filePath)
	{
		DownloadStatus status = DownloadStatus.NotStarted;
		long fileLength = 0L;

		if (!string.IsNullOrWhiteSpace(song.LargeCoverUrl))
		{
			using FileStream stream = new FileStream(filePath, FileMode.Create);
			status = DownloadToStream(song.LargeCoverUrl, stream);
			fileLength = stream.Length;
		}

		if (status != DownloadStatus.Success && !string.IsNullOrWhiteSpace(song.CoverUrl))
		{
			using FileStream stream = new FileStream(filePath, FileMode.Create);
			status = DownloadToStream(song.CoverUrl, stream);
			fileLength = stream.Length;
		}

		return (status, fileLength);
	}

	private List<KuwoSongInfo> SearchSongs(string query, int maxResults)
	{
		if (cancellationSource.IsCancellationRequested)
		{
			return new List<KuwoSongInfo>();
		}

		string responseBody = GetResponseString(string.Format(SearchUrlFormat, TextUtilities.UrlEncodeUtf8(query), maxResults));
		if (cancellationSource.IsCancellationRequested)
		{
			return new List<KuwoSongInfo>();
		}

		return ParseSearchResponse(responseBody);
	}

	private void LoadSongDetails(KuwoSongInfo song)
	{
		if (song.CoverUrl != null)
		{
			return;
		}
		if (IsDetailApiBackoffActive())
		{
			detailUnavailableThisSearch = true;
			return;
		}

		string responseBody = GetResponseString(string.Format(SongDetailUrlFormat, song.TrackId));
		if (string.IsNullOrWhiteSpace(responseBody))
		{
			detailApiUnavailable = false;
			return;
		}

		if (!responseBody.TrimStart().StartsWith("{"))
		{
			detailApiUnavailable = true;
			lastDetailApiCheckTick = Environment.TickCount;
			detailUnavailableThisSearch = true;
			return;
		}

		detailApiUnavailable = false;
		PopulateSongDetails(song, responseBody);
	}

	private static bool IsDetailApiBackoffActive()
	{
		return detailApiUnavailable == true && Environment.TickCount - lastDetailApiCheckTick <= DetailApiRetryIntervalMs;
	}

	private List<KuwoSongInfo> ParseSearchResponse(string searchJson)
	{
		List<KuwoSongInfo> albumResults = new List<KuwoSongInfo>();
		List<KuwoSongInfo> fallbackResults = new List<KuwoSongInfo>();

		try
		{
			JArray songList = JObject.Parse(searchJson)["abslist"] as JArray;
			if (songList == null)
			{
				return albumResults;
			}

			foreach (JToken songToken in songList)
			{
				try
				{
					if (!(songToken is JObject songJson))
					{
						continue;
					}

					KuwoSongInfo song = CreateSongFromJson(songJson);
					if (string.IsNullOrWhiteSpace(song.Title) || string.IsNullOrWhiteSpace(song.Artist))
					{
						continue;
					}

					if (!string.IsNullOrWhiteSpace(song.Album))
					{
						albumResults.Add(song);
						continue;
					}

					JToken sublistToken = null;
					songJson.TryGetValue("SUBLIST", out sublistToken);
					JArray childSongs = sublistToken as JArray;
					if (childSongs != null)
					{
						foreach (JToken childToken in childSongs)
						{
							try
							{
								if (!(childToken is JObject childJson))
								{
									continue;
								}

								if (childJson["SONGNAME"]?.ToString() != song.Title || childJson["ARTISTID"]?.ToString() != song.ArtistId || !string.IsNullOrWhiteSpace(childJson["ALBUM"]?.ToString()))
								{
									continue;
								}

								KuwoSongInfo childSong = CreateSongFromJson(childJson);
								albumResults.Add(childSong);
							}
							catch (Exception childParseError)
							{
								Console.WriteLine("ParseSongsJson child item error:" + childParseError.GetMessageChain());
							}
						}
					}

					if (childSongs != null && childSongs.Any())
					{
						continue;
					}

					fallbackResults.Add(song);
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
			if (!string.IsNullOrWhiteSpace(searchJson))
			{
				// HTTP 200 拿到响应体却无法解析为 JSON:区分"解析失败"与"搜到 0 条 / 网络失败"。
				// (传输失败时 searchJson 为空,已由传输层归类为 Network/Timeout/HttpStatus。)
				SetTransportError(RemoteErrorKind.ParseFailed, "parse");
			}
		}

		return albumResults.Any() ? albumResults : fallbackResults;
	}

	private static KuwoSongInfo CreateSongFromJson(JObject token)
	{
		return new KuwoSongInfo
		{
			Title = ReadJsonString(token, "SONGNAME"),
			Artist = ReadJsonString(token, "ARTIST"),
			Album = ReadJsonString(token, "ALBUM"),
			TrackId = Regex.Replace(ReadJsonString(token, "MUSICRID"), "^MUSIC_", ""),
			ArtistId = ReadJsonString(token, "ARTISTID"),
			OriginalTitle = ReadJsonString(token, "NAME"),
			SearchAlbumCoverUrl = BuildAlbumCoverUrl(ReadJsonString(token, "web_albumpic_short"))
		};
	}

	// 搜索结果里 web_albumpic_short 形如 "120/s3s94/93/xxxx.jpg"(首段为尺寸),
	// 拼成可下载的专辑封面高清直链(尺寸取 500),让封面下载绕开限流的 songinfoandlrc 详情 API。
	private static string BuildAlbumCoverUrl(string webAlbumPicShort)
	{
		if (string.IsNullOrWhiteSpace(webAlbumPicShort))
		{
			return "";
		}

		return AlbumCoverUrlPrefix + Regex.Replace(webAlbumPicShort.Trim(), "^\\d+/", "500/");
	}

	internal void PopulateSongDetails(KuwoSongInfo song, string detailsJson)
	{
		song.CoverUrl = "";

		try
		{
			JObject data = JObject.Parse(detailsJson)["data"] as JObject;
			JArray lyricItems = data?["lrclist"] as JArray;
			JObject songInfo = data?["songinfo"] as JObject;
			StringBuilder lyricBuilder = new StringBuilder();
			StringBuilder translatedLyricBuilder = new StringBuilder();
			SortedDictionary<long, (string PrimaryText, List<string> AlternateText)> timedLines = new SortedDictionary<long, (string, List<string>)>();

			if (lyricItems != null)
			{
				foreach (JToken lyricItem in lyricItems)
				{
					try
					{
						if (!(lyricItem is JObject lyricToken))
						{
							continue;
						}

						long timestampMs = Convert.ToInt64(double.Parse(ReadJsonString(lyricToken, "time"), CultureInfo.InvariantCulture) * 1000.0);
						string lyricText = TextEncodingService.DecodeBasicHtmlEntities(ReadJsonString(lyricToken, "lineLyric"));
						if (timedLines.TryGetValue(timestampMs, out (string PrimaryText, List<string> AlternateText) line))
						{
							if (timedLines.Count == 1)
							{
								timedLines[timestampMs] = (line.PrimaryText + " " + lyricText, new List<string>());
							}
							else
							{
								line.AlternateText.Add(lyricText);
							}
						}
						else
						{
							timedLines.Add(timestampMs, (lyricText, new List<string>()));
						}
					}
					catch (Exception lyricParseError)
					{
						Console.WriteLine("ParseSongsJson lyric item error:" + lyricParseError.GetMessageChain());
					}
				}

				if (timedLines.Any() && timedLines.Last().Value.AlternateText.Count > 1)
				{
					KeyValuePair<long, (string PrimaryText, List<string> AlternateText)> lastLine = timedLines.Last();
					List<string> alternateText = lastLine.Value.AlternateText;
					timedLines.Add(lastLine.Key + 5000L, (string.Join(" ", alternateText.Skip(1)), new List<string>()));
					alternateText.RemoveRange(1, alternateText.Count - 1);
				}

				List<(long TimestampMs, string PrimaryText, string AlternateText)> normalizedLines = new List<(long, string, string)>();
				foreach (KeyValuePair<long, (string PrimaryText, List<string> AlternateText)> timedLine in timedLines)
				{
					string alternateText = timedLine.Value.AlternateText.Any() ? string.Join(" ", timedLine.Value.AlternateText) : null;
					normalizedLines.Add((timedLine.Key, timedLine.Value.PrimaryText, alternateText));
				}

				for (int index = 0; index < normalizedLines.Count; index++)
				{
					(long TimestampMs, string PrimaryText, string AlternateText) line = normalizedLines[index];
					if (line.AlternateText != null && TextUtilities.ContainsChinese(line.AlternateText) && !TextUtilities.ContainsChinese(line.PrimaryText))
					{
						normalizedLines[index] = (line.TimestampMs, line.AlternateText, line.PrimaryText);
					}
				}

				bool foundTranslatedLine = false;
				for (int index = 1; index < normalizedLines.Count - 1; index++)
				{
					(long TimestampMs, string PrimaryText, string AlternateText) line = normalizedLines[index];
					if (!foundTranslatedLine && line.AlternateText != null)
					{
						foundTranslatedLine = true;
					}

					if (foundTranslatedLine && line.AlternateText == null)
					{
						(long TimestampMs, string PrimaryText, string AlternateText) nextLine = normalizedLines[index + 1];
						if (nextLine.AlternateText == null)
						{
							normalizedLines[index] = (nextLine.TimestampMs, line.PrimaryText, nextLine.PrimaryText);
							normalizedLines.RemoveAt(index + 1);
						}
					}
				}

				if (normalizedLines.Count >= 3)
				{
					(long TimestampMs, string PrimaryText, string AlternateText) thirdFromLast = normalizedLines[normalizedLines.Count - 3];
					(long TimestampMs, string PrimaryText, string AlternateText) secondFromLast = normalizedLines[normalizedLines.Count - 2];
					(long TimestampMs, string PrimaryText, string AlternateText) lastLine = normalizedLines[normalizedLines.Count - 1];
					if (lastLine.AlternateText != null && thirdFromLast.AlternateText != null && secondFromLast.AlternateText == null)
					{
						if (TextUtilities.ContainsChinese(secondFromLast.PrimaryText) && TextUtilities.ContainsChinese(lastLine.PrimaryText) && !TextUtilities.ContainsChinese(lastLine.AlternateText))
						{
							lastLine = (lastLine.TimestampMs, lastLine.AlternateText, lastLine.PrimaryText);
						}

						normalizedLines[normalizedLines.Count - 2] = (lastLine.TimestampMs, secondFromLast.PrimaryText, lastLine.PrimaryText);
						normalizedLines[normalizedLines.Count - 1] = (lastLine.TimestampMs + 5000L, lastLine.AlternateText, null);
					}
				}

				for (int index = 0; index < normalizedLines.Count; index++)
				{
					(long TimestampMs, string PrimaryText, string AlternateText) line = normalizedLines[index];
					bool appendedPrimaryLyric = false;
					if (line.AlternateText != null)
					{
						lyricBuilder.Append(LyricTextProcessor.FormatTimestamp(line.TimestampMs, useThreeDigitMilliseconds: false));
						lyricBuilder.Append(line.AlternateText);
						lyricBuilder.Append("\n");
					}
					else if (index < normalizedLines.Count - 1 || translatedLyricBuilder.Length == 0)
					{
						lyricBuilder.Append(LyricTextProcessor.FormatTimestamp(line.TimestampMs, useThreeDigitMilliseconds: false));
						lyricBuilder.Append(line.PrimaryText);
						lyricBuilder.Append("\n");
						appendedPrimaryLyric = true;
					}

					if (!appendedPrimaryLyric)
					{
						long translatedTimestampMs = index > 0 ? normalizedLines[index - 1].TimestampMs : line.TimestampMs;
						translatedLyricBuilder.Append(LyricTextProcessor.FormatTimestamp(translatedTimestampMs, useThreeDigitMilliseconds: false));
						translatedLyricBuilder.Append(line.PrimaryText);
						translatedLyricBuilder.Append("\n");
					}
				}
			}

			song.CoverUrl = songInfo?["pic"]?.ToString() ?? "";
			Match coverMatch = Regex.Match(song.CoverUrl, "^(.+)[/](\\d+)[/](\\d+[/]\\d+[/].+)$");
			if (coverMatch.Success)
			{
				song.LargeCoverUrl = coverMatch.Groups[1].Value + "/700/" + coverMatch.Groups[3].Value;
			}

			if (lyricBuilder.Length > 0)
			{
				song.LoadedLyric = new LyricSearchResult
				{
					Lyric = lyricBuilder.ToString(),
					TranslatedLyric = translatedLyricBuilder.ToString(),
					Title = song.Title,
					Artist = song.Artist,
					Album = song.Album,
					OriginalTitle = song.OriginalTitle,
					SearchSource = GetSource()
				};
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine("ParseSongsJson error:" + ex.GetMessageChain());
		}
	}

	private static string ReadJsonString(JObject token, string propertyName)
	{
		return token[propertyName]?.ToString() ?? "";
	}
}
