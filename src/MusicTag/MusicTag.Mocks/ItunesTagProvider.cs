using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using MusicTag.Candidates;
using MusicTag.Serialization;
using MusicTagWinApp.Containers;
using MusicTagWinApp.Instances;
using MusicTagWinApp.Listeners;
using MusicTagWinApp.Properties;
using MusicTagWinApp.Roles;
using MusicTagWinApp.Web;
using Newtonsoft.Json.Linq;

namespace MusicTag.Mocks;

internal class ItunesTagProvider : RemoteTagProviderBase
{
	private static readonly string searchUrlTemplate = Marshal.PtrToStringUni(GetSearchUrlTemplate());

	protected override SearchSource GetSource()
	{
		return SearchSource.ITunes;
	}

	protected override HttpClient CreateHttpClient()
	{
		return KugouTagProvider.CreateKugouHttpClient();
	}

	public ItunesTagProvider(CancellationTokenSource cancellation)
		: base(cancellation)
	{
	}

	public ItunesTagProvider()
		: this(null)
	{
	}

	private List<ItunesSearchResult> SearchItunes(string query, int resultLimit)
	{
		if (cancellationSource.IsCancellationRequested)
		{
			return new List<ItunesSearchResult>();
		}

		string responseJson = GetResponseString(string.Format(searchUrlTemplate, DatabaseMapper.UrlEncodeUtf8(query), resultLimit, Settings.Default.ItunesSearchParams_Country));
		if (cancellationSource.IsCancellationRequested)
		{
			return new List<ItunesSearchResult>();
		}
		return ParseSearchResults(responseJson);
	}

	public List<CoverSearchResult> SearchCovers(string query, int resultLimit, List<CoverSearchResult> existingCovers)
	{
		List<CoverSearchResult> covers = new List<CoverSearchResult>();
		HashSet<string> seenCoverUrls = new HashSet<string>();
		foreach (ItunesSearchResult searchResult in SearchItunes(query, resultLimit))
		{
			if (cancellationSource.IsCancellationRequested)
			{
				break;
			}

			string coverUrl = GetHighResolutionArtworkUrl(searchResult);
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
				cover.CoverDownloader = CreateCoverDownloader<ItunesTagProvider>(coverUrl);
				covers.Add(cover);
				seenCoverUrls.Add(coverUrl);
			}
		}
		return covers;
	}

	public List<TrackSearchResult> SearchTracks(string query, int resultLimit, int searchPass, int sourceOrder, List<TrackSearchResult> existingTracks, List<TrackSearchResult> pendingTracks)
	{
		List<TrackSearchResult> tracks = new List<TrackSearchResult>();
		List<ItunesSearchResult> searchResults = SearchItunes(query, resultLimit);
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
		foreach (ItunesSearchResult searchResult in searchResults)
		{
			TrackSearchResult track = new TrackSearchResult();
			track.SearchSource = GetSource();
			track.SourceTrackId = searchResult.TrackId.ToString();
			track.Title = searchResult.TrackName;
			track.Artist = searchResult.ArtistName;
			track.Album = searchResult.CollectionName;
			track.Comment = "";
			if (!string.IsNullOrWhiteSpace(searchResult.ReleaseDate) && DateTime.TryParse(searchResult.ReleaseDate, out var releaseDate))
			{
				track.Year = releaseDate.ToString("yyyy");
			}
			if (searchResult.TrackNumber > 0)
			{
				track.Track = searchResult.TrackNumber;
				track.TrackLabel = "Track " + searchResult.TrackNumber;
				if (searchResult.DiscNumber > 0)
				{
					track.Disc = searchResult.DiscNumber;
					track.TrackLabel = track.TrackLabel + " of " + searchResult.DiscNumber;
				}
			}
			track.Genre = searchResult.PrimaryGenreName;

			string coverUrl = GetHighResolutionArtworkUrl(searchResult);
			if (!string.IsNullOrWhiteSpace(coverUrl))
			{
				CoverSearchResult cover = new CoverSearchResult();
				cover.CoverUrl = coverUrl;
				cover.SearchSource = GetSource();
				cover.CoverDownloader = CreateCoverDownloader<ItunesTagProvider>(coverUrl);
				track.Cover = cover;
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

	private static List<ItunesSearchResult> ParseSearchResults(string responseJson)
	{
		List<ItunesSearchResult> results = new List<ItunesSearchResult>();
		try
		{
			JArray resultsJson = JObject.Parse(responseJson)["results"] as JArray;
			if (resultsJson == null)
			{
				return results;
			}

			foreach (JToken resultToken in resultsJson)
			{
				if (!(resultToken is JObject))
				{
					continue;
				}

				try
				{
					results.Add(ParseSearchResult(resultToken));
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
		return results;
	}

	private static ItunesSearchResult ParseSearchResult(JToken resultToken)
	{
		return new ItunesSearchResult
		{
			WrapperType = GetStringField(resultToken, "wrapperType"),
			Kind = GetStringField(resultToken, "kind"),
			ArtistId = GetLongField(resultToken, "artistId"),
			CollectionId = GetLongField(resultToken, "collectionId"),
			TrackId = GetLongField(resultToken, "trackId"),
			ArtistName = GetStringField(resultToken, "artistName"),
			CollectionName = GetStringField(resultToken, "collectionName"),
			TrackName = GetStringField(resultToken, "trackName"),
			CollectionCensoredName = GetStringField(resultToken, "collectionCensoredName"),
			TrackCensoredName = GetStringField(resultToken, "trackCensoredName"),
			ArtworkUrl30 = GetStringField(resultToken, "artworkUrl30"),
			ArtworkUrl60 = GetStringField(resultToken, "artworkUrl60"),
			ArtworkUrl100 = GetStringField(resultToken, "artworkUrl100"),
			ReleaseDate = GetStringField(resultToken, "releaseDate"),
			DiscCount = GetIntField(resultToken, "discCount"),
			DiscNumber = GetIntField(resultToken, "discNumber"),
			TrackCount = GetIntField(resultToken, "trackCount"),
			TrackNumber = GetIntField(resultToken, "trackNumber"),
			TrackTimeMillis = GetLongField(resultToken, "trackTimeMillis"),
			Country = GetStringField(resultToken, "country"),
			PrimaryGenreName = GetStringField(resultToken, "primaryGenreName")
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

	private static long GetLongField(JToken token, string fieldName)
	{
		if (token?.Type != JTokenType.Object)
		{
			return 0L;
		}

		JToken fieldValue = token[fieldName];
		if (fieldValue == null)
		{
			return 0L;
		}

		long longValue;
		return long.TryParse(fieldValue.ToString(), out longValue) ? longValue : 0L;
	}

	private static string GetHighResolutionArtworkUrl(ItunesSearchResult searchResult)
	{
		string coverUrl = searchResult.ArtworkUrl100;
		if (string.IsNullOrWhiteSpace(coverUrl))
		{
			coverUrl = searchResult.ArtworkUrl60;
		}
		if (string.IsNullOrWhiteSpace(coverUrl))
		{
			coverUrl = searchResult.ArtworkUrl30;
		}
		if (string.IsNullOrWhiteSpace(coverUrl))
		{
			return null;
		}

		int fileNameOffset = coverUrl.LastIndexOf('/');
		if (fileNameOffset > 0)
		{
			string pathPrefix = coverUrl.Substring(0, fileNameOffset + 1);
			string fileName = coverUrl.Substring(fileNameOffset + 1);
			fileName = new Regex("(\\d+)x(\\d+)").Replace(fileName, "3000x3000", 1);
			coverUrl = pathPrefix + fileName;
		}
		return coverUrl;
	}

	[DllImport("MusicTag.dll", EntryPoint = "ng")]
	private static extern IntPtr GetSearchUrlTemplate();
}
