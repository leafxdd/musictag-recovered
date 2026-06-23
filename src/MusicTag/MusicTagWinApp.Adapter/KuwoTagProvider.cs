using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
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

internal class KuwoTagProvider : RemoteTagProviderBase
{
	private const int DetailApiRetryIntervalMs = 300000;

	private static readonly string SearchUrlFormat = Marshal.PtrToStringUni(GetSearchUrlFormat());

	private static readonly string SongDetailUrlFormat = Marshal.PtrToStringUni(GetSongDetailUrlFormat());

	private static bool? detailApiUnavailable;

	private static int lastDetailApiCheckTick;

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
		List<TrackSearchResult> results = new List<TrackSearchResult>();
		List<KuwoSongInfo> songs = SearchSongs(query, maxResults);
		Dictionary<string, TrackSearchResult> tracksById = new Dictionary<string, TrackSearchResult>();
		List<string> orderedTrackIds = new List<string>();
		HashSet<string> skippedTrackIds = new HashSet<string>();

		foreach (TrackSearchResult existingTrack in previousResults.Concat(currentResults))
		{
			if (existingTrack.SearchSource == GetSource())
			{
				skippedTrackIds.Add(existingTrack.SourceTrackId);
			}
		}

		foreach (KuwoSongInfo song in songs)
		{
			TrackSearchResult track = CreateTrackResult(song);
			if (tracksById.ContainsKey(track.SourceTrackId) || skippedTrackIds.Contains(track.SourceTrackId))
			{
				continue;
			}

			orderedTrackIds.Add(track.SourceTrackId);
			tracksById.Add(track.SourceTrackId, track);
		}

		foreach (string trackId in orderedTrackIds)
		{
			results.Add(tracksById[trackId]);
		}

		for (int index = 0; index < results.Count; index++)
		{
			results[index].ResultOrder = index;
			results[index].SearchPass = searchPass;
			results[index].SourceOrder = sourceOrder;
		}

		return results;
	}

	public List<LyricSearchResult> SearchLyrics(string query, int maxResults, int sourceOrder)
	{
		List<LyricSearchResult> lyrics = new List<LyricSearchResult>();
		int resultOrder = 0;

		foreach (KuwoSongInfo song in SearchSongs(query, maxResults))
		{
			if (cancellationSource.IsCancellationRequested)
			{
				break;
			}

			LoadSongDetails(song);
			LyricSearchResult lyric = song.LoadedLyric;
			if (lyric == null)
			{
				continue;
			}

			lyric.ResultOrder = resultOrder++;
			lyric.SourceOrder = sourceOrder;
			lyrics.Add(lyric);
		}

		return lyrics;
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
		List<CoverSearchResult> results = new List<CoverSearchResult>();
		HashSet<string> queuedCoverUrls = new HashSet<string>();

		foreach (KuwoSongInfo song in SearchSongs(query, maxResults))
		{
			if (cancellationSource.IsCancellationRequested)
			{
				break;
			}

			string coverUrl = string.Format(SongDetailUrlFormat, song.TrackId);
			if (queuedCoverUrls.Contains(coverUrl) || existingCovers.Any(existingCover => existingCover.CoverUrl == coverUrl))
			{
				continue;
			}

			results.Add(new CoverSearchResult
			{
				CoverUrl = coverUrl,
				SearchSource = GetSource(),
				CoverDownloader = (cancellation, filePath, requestTimeout) => DownloadDeferredCover(song, null, cancellation, filePath)
			});
			queuedCoverUrls.Add(coverUrl);
		}

		return results;
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
		track.Cover = new CoverSearchResult
		{
			CoverUrl = detailUrl,
			SearchSource = GetSource(),
			CoverDownloader = (cancellation, filePath, requestTimeout) => DownloadDeferredCover(song, track, cancellation, filePath)
		};
		track.LyricResult = new LyricSearchResult
		{
			LyricUrl = detailUrl,
			DeferredLyricLoader = cancellation => LoadDeferredLyric(song, track, cancellation)
		};
		return track;
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

		string responseBody = GetResponseString(string.Format(SearchUrlFormat, DatabaseMapper.UrlEncodeUtf8(query), maxResults));
		if (cancellationSource.IsCancellationRequested)
		{
			return new List<KuwoSongInfo>();
		}

		return ParseSearchResponse(responseBody);
	}

	private void LoadSongDetails(KuwoSongInfo song)
	{
		if (song.CoverUrl != null || IsDetailApiBackoffActive())
		{
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
						FillExtendedSongMetadata(song, songJson);
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
								FillExtendedSongMetadata(childSong, childJson);
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

					FillExtendedSongMetadata(song, songJson);
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
			AlbumId = ReadJsonString(token, "ALBUMID")
		};
	}

	private static void FillExtendedSongMetadata(KuwoSongInfo song, JObject token)
	{
		song.AlbumArtist = ReadJsonString(token, "AARTIST");
		song.Alias = ReadJsonString(token, "ALIAS");
		song.Duration = ReadJsonString(token, "DURATION");
		song.FormattedArtist = ReadJsonString(token, "FARTIST");
		song.Format = ReadJsonString(token, "FORMAT");
		song.FormattedTitle = ReadJsonString(token, "FSONGNAME");
		song.KMark = ReadJsonString(token, "KMARK");
		song.MusicInfo = ReadJsonString(token, "MINFO");
		song.MusicVideoFlag = ReadJsonString(token, "MVFLAG");
		song.MusicVideoPicture = ReadJsonString(token, "MVPIC");
		song.MusicVideoQuality = ReadJsonString(token, "MVQUALITY");
		song.Subtitle = ReadJsonString(token, "SUBTITLE");
		song.Tags = ReadJsonString(token, "TAG");
	}

	private void PopulateSongDetails(KuwoSongInfo song, string detailsJson)
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

						long timestampMs = Convert.ToInt64(double.Parse(ReadJsonString(lyricToken, "time")) * 1000.0);
						string lyricText = TextEncodingService.DecodeBasicHtmlEntities(ReadJsonString(lyricToken, "lineLyric"));
						if (timedLines.ContainsKey(timestampMs))
						{
							(string PrimaryText, List<string> AlternateText) line = timedLines[timestampMs];
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
					if (line.AlternateText != null && DatabaseMapper.ContainsChinese(line.AlternateText) && !DatabaseMapper.ContainsChinese(line.PrimaryText))
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
						if (DatabaseMapper.ContainsChinese(secondFromLast.PrimaryText) && DatabaseMapper.ContainsChinese(lastLine.PrimaryText) && !DatabaseMapper.ContainsChinese(lastLine.AlternateText))
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

	[DllImport("MusicTag.dll", EntryPoint = "pa")]
	private static extern IntPtr GetSearchUrlFormat();

	[DllImport("MusicTag.dll", EntryPoint = "pb")]
	private static extern IntPtr GetSongDetailUrlFormat();
}
