using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
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

internal class KugouTagProvider : RemoteTagProviderBase
{
	private sealed class SearchResultDetailLoader
	{
		public KugouSongInfo SearchResult;

		internal LyricSearchResult Load(CancellationTokenSource cancellationTokenSource)
		{
			using KugouTagProvider kugouTagProvider = new KugouTagProvider(cancellationTokenSource);
			return kugouTagProvider.LoadLyrics(SearchResult);
		}
	}

	private const string songSearchUrlTemplate = "http://mobilecdn.kugou.com/api/v3/search/song?format=json&keyword={0}&page=1&pagesize={1}&showtype=1";

	private const string lyricUrlTemplate = "https://m3ws.kugou.com/api/v1/krc/get_krc?keyword={0}&hash={1}&timelength={2}";

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

	private List<KugouSongInfo> SearchSongs(string query, int resultLimit)
	{
		if (cancellationSource.IsCancellationRequested)
		{
			return new List<KugouSongInfo>();
		}
		string responseBody = GetResponseString(string.Format(songSearchUrlTemplate, DatabaseMapper.UrlEncodeUtf8(query), resultLimit));
		if (cancellationSource.IsCancellationRequested)
		{
			return new List<KugouSongInfo>();
		}
		return ParseSongSearchResponse(responseBody);
	}

	public List<LyricSearchResult> SearchLyrics(string query, int resultLimit, int sourceOrder)
	{
		List<LyricSearchResult> lyrics = new List<LyricSearchResult>();
		IEnumerable<KugouSongInfo> songs = SearchSongs(query, resultLimit).Take(resultLimit);
		int resultIndex = 0;
		foreach (KugouSongInfo song in songs)
		{
			if (!cancellationSource.IsCancellationRequested)
			{
				LyricSearchResult lyric = LoadLyrics(song);
				if (lyric != null)
				{
					lyric.ResultOrder = resultIndex++;
					lyric.SourceOrder = sourceOrder;
					lyrics.Add(lyric);
				}
				continue;
			}
			break;
		}
		return lyrics;
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
		List<TrackSearchResult> tracks = new List<TrackSearchResult>();
		IEnumerable<KugouSongInfo> songs = SearchSongs(query, resultLimit).Take(resultLimit);
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
		using (IEnumerator<KugouSongInfo> songEnumerator = songs.GetEnumerator())
		{
			while (songEnumerator.MoveNext())
			{
				SearchResultDetailLoader searchResultDetailLoader = new SearchResultDetailLoader();
				searchResultDetailLoader.SearchResult = songEnumerator.Current;
				TrackSearchResult track = new TrackSearchResult();
				track.SearchSource = GetSource();
				track.SourceTrackId = searchResultDetailLoader.SearchResult.AudioId;
				track.Title = searchResultDetailLoader.SearchResult.Title;
				track.Artist = searchResultDetailLoader.SearchResult.Artist;
				track.Album = searchResultDetailLoader.SearchResult.Album;
				track.KugouHash = searchResultDetailLoader.SearchResult.Hash;
				track.KugouDurationMs = searchResultDetailLoader.SearchResult.DurationMs;
				LyricSearchResult lyric = new LyricSearchResult();
				lyric.LyricUrl = string.Format(lyricUrlTemplate, BuildEncodedLyricKeyword(searchResultDetailLoader.SearchResult.Artist, searchResultDetailLoader.SearchResult.Title), searchResultDetailLoader.SearchResult.Hash, searchResultDetailLoader.SearchResult.DurationMs);
				lyric.SearchSource = GetSource();
				lyric.DeferredLyricLoader = searchResultDetailLoader.Load;
				track.LyricResult = lyric;
				if (!tracksById.ContainsKey(track.SourceTrackId) && !knownTrackIds.Contains(track.SourceTrackId))
				{
					trackIdsInOrder.Add(track.SourceTrackId);
					tracksById.Add(track.SourceTrackId, track);
				}
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

	private List<KugouSongInfo> ParseSongSearchResponse(string responseBody)
	{
		List<KugouSongInfo> songs = new List<KugouSongInfo>();
		try
		{
			JToken songListJson = JObject.Parse(responseBody)["data"]?["info"];
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
		}
		return songs;
	}

	private static KugouSongInfo ParseSongSearchResult(JToken songJson)
	{
		return new KugouSongInfo
		{
			AudioId = GetStringField(songJson, "audio_id"),
			Title = GetStringField(songJson, "songname"),
			Artist = GetStringField(songJson, "singername"),
			Album = GetStringField(songJson, "album_name"),
			Hash = GetStringField(songJson, "hash"),
			DurationMs = GetIntField(songJson, "duration") * 1000
		};
	}

	private static string GetStringField(JToken token, string fieldName)
	{
		if (token?.Type != JTokenType.Object)
		{
			return "";
		}

		return token[fieldName]?.ToString() ?? "";
	}

	private static int GetIntField(JToken token, string fieldName)
	{
		if (token?.Type != JTokenType.Object)
		{
			return 0;
		}

		JToken fieldValue = token[fieldName];
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

	private string BuildEncodedLyricKeyword(string artist, string title)
	{
		if (!string.IsNullOrWhiteSpace(artist) && !string.IsNullOrWhiteSpace(title))
		{
			return DatabaseMapper.UrlEncodeUtf8(title.Trim() + " - " + artist.Trim());
		}
		if (string.IsNullOrWhiteSpace(title))
		{
			return DatabaseMapper.UrlEncodeUtf8(artist.Trim() ?? "");
		}
		return DatabaseMapper.UrlEncodeUtf8(title.Trim() ?? "");
	}

	private string ParseTranslatedLyric(string content, LyricTextProcessor lyricMerger)
	{
		List<string> translatedLines = new List<string>();
		try
		{
			Regex regex = new Regex("^\\[(.*)\\]$");
			Match outerMatch = regex.Match(content.Trim());
			if (outerMatch.Success)
			{
				string[] translationItems = outerMatch.Groups[1].Value.Trim().Split(',');
				foreach (string translationItem in translationItems)
				{
					Match innerMatch = regex.Match(translationItem.Trim());
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
