using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using MusicTag.Consumers;
using MusicTag.Serialization;
using MusicTag.Services;
using MusicTagWinApp.Instances;
using MusicTagWinApp.Listeners;
using MusicTagWinApp.Roles;
using MusicTagWinApp.Web;
using Newtonsoft.Json.Linq;

namespace MusicTagWinApp.Stubs;

internal class VgmdbTagProvider : RemoteTagProviderBase
{
	private static readonly string vgmdbInfoBaseUrl;

	private static readonly string albumSearchEndpointFormat;

	private static readonly string albumDetailsEndpointFormat;

	protected override SearchSource GetSource()
	{
		return SearchSource.Vgmdb;
	}

	protected override HttpClient CreateHttpClient()
	{
		HttpClient client = new HttpClient(new HttpClientHandler
		{
			AutomaticDecompression = (DecompressionMethods.GZip | DecompressionMethods.Deflate)
		})
		{
			Timeout = TimeSpan.FromSeconds(15.0),
			DefaultRequestHeaders = { { "user-agent", "Mozilla/5.0 (Windows NT 6.3; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/68.0.3440.106 Safari/537.36" } }
		};
		client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3");
		return client;
	}

	public VgmdbTagProvider(CancellationTokenSource cancellation)
		: base(cancellation)
	{
	}

	public VgmdbTagProvider()
		: this(null)
	{
	}

	public List<CoverSearchResult> SearchCovers(string artist, string album, int coverLimit, List<CoverSearchResult> existingCovers)
	{
		List<CoverSearchResult> covers = new List<CoverSearchResult>();
		HashSet<string> seenCoverUrls = new HashSet<string>();
		if (cancellationSource.IsCancellationRequested)
		{
			return covers;
		}
		List<VgmdbAlbum> matchedAlbums = new List<VgmdbAlbum>();
		if (!string.IsNullOrWhiteSpace(artist) && !string.IsNullOrWhiteSpace(album))
		{
			matchedAlbums = SearchAlbums(artist, album);
		}
		else if (!string.IsNullOrWhiteSpace(album))
		{
			matchedAlbums = SearchAlbums("", "\"" + album + "\"");
		}
		foreach (VgmdbAlbum matchedAlbum in matchedAlbums)
		{
			if (!cancellationSource.IsCancellationRequested)
			{
				VgmdbAlbum albumDetails = LoadAlbumDetails(matchedAlbum.AlbumUrl);
				AddCoverIfNew(albumDetails, existingCovers, seenCoverUrls, covers);
				continue;
			}
			break;
		}
		return covers;
	}

	public List<TrackSearchResult> SearchTracks(string titleFilter, string artist, string album, int resultLimit, int searchPass, int sourceOrder, List<TrackSearchResult> existingTracks, List<TrackSearchResult> pendingTracks)
	{
		List<TrackSearchResult> tracks = new List<TrackSearchResult>();
		List<VgmdbAlbum> matchedAlbums = new List<VgmdbAlbum>();
		if (!string.IsNullOrWhiteSpace(artist) && !string.IsNullOrWhiteSpace(album))
		{
			matchedAlbums = SearchAlbums(artist, album);
		}
		else if (!string.IsNullOrWhiteSpace(album))
		{
			matchedAlbums = SearchAlbums("", "\"" + album + "\"");
		}
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
		foreach (VgmdbAlbum matchedAlbum in matchedAlbums)
		{
			if (cancellationSource.IsCancellationRequested)
			{
				break;
			}
			bool reachedLimit = false;
			VgmdbAlbum albumDetails = LoadAlbumDetails(matchedAlbum.AlbumUrl);
			int discNumber = 0;
			foreach (VgmdbDisc disc in albumDetails.Discs)
			{
				if (reachedLimit)
				{
					break;
				}
				discNumber++;
				int trackNumber = 0;
				foreach (Dictionary<string, string> trackNames in disc.Tracks)
				{
					if (reachedLimit)
					{
						break;
					}
					trackNumber++;
					foreach (KeyValuePair<string, string> trackTitle in trackNames)
					{
						if (reachedLimit)
						{
							break;
						}
						if ((!string.IsNullOrWhiteSpace(titleFilter) && trackTitle.Value.Contains(titleFilter)) || string.IsNullOrWhiteSpace(titleFilter))
						{
							TrackSearchResult trackResult = new TrackSearchResult();
							trackResult.SearchSource = SearchSource.Vgmdb;
							trackResult.SourceTrackId = matchedAlbum.AlbumUrl + "_" + discNumber + "_" + trackNumber;
							trackResult.Title = trackTitle.Value;
							trackResult.Artist = albumDetails.GetPerformerName(trackTitle.Key);
							trackResult.Album = albumDetails.GetAlbumTitleParts(trackTitle.Key, preferDisplayName: true)[0];
							trackResult.Track = trackNumber;
							trackResult.TrackLabel = "Track " + trackNumber;
							trackResult.Comment = "";
							if (DateTime.TryParse(albumDetails.ReleaseDate, out var releaseDate))
							{
								trackResult.Year = releaseDate.ToString("yyyy");
							}
							if (albumDetails.Discs.Count > 1)
							{
								trackResult.Disc = discNumber;
								trackResult.TrackLabel = trackResult.TrackLabel + " of " + discNumber;
							}
							if (!string.IsNullOrWhiteSpace(albumDetails.CoverImageUrl))
							{
								CoverSearchResult cover = new CoverSearchResult();
								cover.CoverUrl = albumDetails.CoverImageUrl;
								cover.SearchSource = SearchSource.Vgmdb;
								cover.CoverDownloader = CreateCoverDownloader<VgmdbTagProvider>(albumDetails.CoverImageUrl);
								trackResult.Cover = cover;
							}
							if (!tracksById.ContainsKey(trackResult.SourceTrackId) && !existingTrackIds.Contains(trackResult.SourceTrackId))
							{
								resultIds.Add(trackResult.SourceTrackId);
								tracksById.Add(trackResult.SourceTrackId, trackResult);
							}
							if (--resultLimit <= 0)
							{
								reachedLimit = true;
							}
						}
					}
				}
			}
			if (reachedLimit)
			{
				break;
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

	public List<CoverSearchResult> SearchCoversForCandidateTracks(List<MusicBrainzReleaseMatch> candidates, int limit, List<CoverSearchResult> existingCovers)
	{
		List<CoverSearchResult> covers = new List<CoverSearchResult>();
		HashSet<string> seenCoverUrls = new HashSet<string>();
		if (cancellationSource.IsCancellationRequested)
		{
			return covers;
		}
		HashSet<string> searchedAlbumKeys = new HashSet<string>();
		int searchedCandidateCount = 0;
		foreach (MusicBrainzReleaseMatch candidate in candidates)
		{
			if (cancellationSource.IsCancellationRequested || string.IsNullOrWhiteSpace(candidate.AlbumTitle) || string.IsNullOrWhiteSpace(candidate.ArtistName))
			{
				continue;
			}
			if (searchedCandidateCount++ >= limit)
			{
				break;
			}
			string albumArtistKey = candidate.AlbumTitle + " - " + candidate.ArtistName;
			if (!searchedAlbumKeys.Contains(albumArtistKey))
			{
				searchedAlbumKeys.Add(albumArtistKey);
				List<VgmdbAlbum> matchedAlbums = SearchAlbums(candidate.ArtistName, candidate.AlbumTitle);
				if (!matchedAlbums.Any())
				{
					matchedAlbums = SearchAlbums("", "\"" + candidate.AlbumTitle + "\"");
				}
				if (matchedAlbums.Any())
				{
					VgmdbAlbum albumDetails = LoadAlbumDetails(matchedAlbums[0].AlbumUrl);
					AddCoverIfNew(albumDetails, existingCovers, seenCoverUrls, covers);
				}
			}
		}
		return covers;
	}

	private bool AddCoverIfNew(VgmdbAlbum album, List<CoverSearchResult> existingCovers, HashSet<string> seenCoverUrls, List<CoverSearchResult> covers)
	{
		if (!string.IsNullOrWhiteSpace(album.CoverImageUrl) && !seenCoverUrls.Contains(album.CoverImageUrl))
		{
			bool isNewCover = true;
			foreach (CoverSearchResult existingCover in existingCovers)
			{
				if (existingCover.CoverUrl == album.CoverImageUrl)
				{
					isNewCover = false;
					break;
				}
			}
			if (!isNewCover)
			{
				return false;
			}
			CoverSearchResult cover = new CoverSearchResult();
			cover.CoverUrl = album.CoverImageUrl;
			cover.SearchSource = SearchSource.Vgmdb;
			cover.CoverDownloader = CreateCoverDownloader<VgmdbTagProvider>(album.CoverImageUrl);
			covers.Add(cover);
			seenCoverUrls.Add(album.CoverImageUrl);
			return true;
		}
		return false;
	}

	private List<VgmdbAlbum> SearchAlbums(string artist, string album)
	{
		if (cancellationSource.IsCancellationRequested)
		{
			return new List<VgmdbAlbum>();
		}
		try
		{
			string query = album;
			if (!string.IsNullOrWhiteSpace(artist))
			{
				query = query + " / " + artist;
			}
			string responseJson = GetResponseString(string.Format(albumSearchEndpointFormat, DatabaseMapper.UrlEncodeUtf8(query)));
			return (!cancellationSource.IsCancellationRequested) ? ParseAlbumSearchResults(responseJson) : new List<VgmdbAlbum>();
		}
		catch (System.Exception ex)
		{
			Console.WriteLine("SearchAlbums error:" + ex.GetMessageChain());
		}
		return new List<VgmdbAlbum>();
	}

	public VgmdbAlbum LoadAlbumDetails(string albumUrl)
	{
		if (cancellationSource.IsCancellationRequested)
		{
			return new VgmdbAlbum();
		}

		string responseJson = GetResponseString(string.Format(albumDetailsEndpointFormat, albumUrl));
		if (cancellationSource.IsCancellationRequested)
		{
			return new VgmdbAlbum();
		}
		return ParseAlbumDetailsResponse(responseJson);
	}

	private List<VgmdbAlbum> ParseAlbumSearchResults(string responseJson)
	{
		List<VgmdbAlbum> albums = new List<VgmdbAlbum>();
		try
		{
			JArray albumsJson = JObject.Parse(responseJson)["results"]?["albums"] as JArray;
			if (albumsJson == null)
			{
				return albums;
			}

			foreach (JToken albumToken in albumsJson)
			{
				JObject album = albumToken as JObject;
				if (album == null)
				{
					continue;
				}

				VgmdbAlbum searchResult = new VgmdbAlbum
				{
					CatalogNumber = album["catalog"]?.ToString() ?? "",
					AlbumUrl = album["link"]?.ToString() ?? "",
					ReleaseDate = album["release_date"]?.ToString() ?? ""
				};

					JObject titles = album["titles"] as JObject;
					if (titles != null)
				{
					AddLocalizedText(searchResult.AlbumNames, "ja", titles["ja"]?.ToString() ?? "");
					AddLocalizedText(searchResult.AlbumNames, "ja-latn", titles["ja-latn"]?.ToString() ?? "");
					AddLocalizedText(searchResult.AlbumNames, "en", titles["en"]?.ToString() ?? "");
				}
				albums.Add(searchResult);
			}
		}
		catch (System.Exception ex)
		{
			Console.WriteLine("ParseAlbumsJson error:" + ex.GetMessageChain());
		}
		return albums;
	}

	private static void AddLocalizedText(Dictionary<string, string> localizedText, string language, string value)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			localizedText.Add(language, value);
		}
	}

	private static VgmdbAlbum ParseAlbumDetailsResponse(string responseJson)
	{
		VgmdbAlbum album = new VgmdbAlbum();
		try
		{
			JObject albumJson = JObject.Parse(responseJson);
			album.CatalogNumber = GetString(albumJson, "catalog");
			album.AlbumUrl = GetString(albumJson, "link");
			album.ReleaseDate = GetString(albumJson, "release_date");
			album.Category = GetString(albumJson, "category");
			album.Classification = GetString(albumJson, "classification");
			album.CoverImageUrl = GetString(albumJson, "picture_full");
			album.DisplayName = GetString(albumJson, "name");

			CopyLocalizedNames(albumJson["names"], album.AlbumNames);
			AddNamedCredits(album.PerformerNames, albumJson["performers"]);
			AddNamedCredits(album.ComposerNames, albumJson["composers"]);
			AddDiscs(album, albumJson["discs"]);
		}
		catch (System.Exception ex)
		{
			Console.WriteLine("ParseAlbumJson error:" + ex.GetMessageChain());
		}
		return album;
	}

	private static string GetString(JToken token, string propertyName)
	{
		return token?[propertyName]?.ToString() ?? "";
	}

	private static void CopyLocalizedNames(JToken names, Dictionary<string, string> target)
	{
		JObject localizedNames = names as JObject;
		if (localizedNames == null)
		{
			return;
		}

		AddLocalizedText(target, "ja", localizedNames["ja"]?.ToString() ?? "");
		AddLocalizedText(target, "ja-latn", localizedNames["ja-latn"]?.ToString() ?? "");
		AddLocalizedText(target, "en", localizedNames["en"]?.ToString() ?? "");
	}

	private static void AddNamedCredits(List<Dictionary<string, string>> credits, JToken people)
	{
		JArray peopleArray = people as JArray;
		if (peopleArray == null)
		{
			return;
		}

		foreach (JToken personToken in peopleArray)
		{
			JObject person = personToken as JObject;
			if (person == null)
			{
				continue;
			}

			JToken names = person["names"];
			if (names == null)
			{
				continue;
			}

			Dictionary<string, string> localizedNames = new Dictionary<string, string>();
			CopyLocalizedNames(names, localizedNames);
			credits.Add(localizedNames);
		}
	}

	private static void AddDiscs(VgmdbAlbum album, JToken discs)
	{
		JArray discArray = discs as JArray;
		if (discArray == null)
		{
			return;
		}

		foreach (JToken discToken in discArray)
		{
			JObject disc = discToken as JObject;
			if (disc == null)
			{
				continue;
			}

			VgmdbDisc discInfo = new VgmdbDisc
			{
				Duration = GetString(disc, "disc_length"),
				Name = GetString(disc, "name")
			};

			AddTracks(discInfo, disc["tracks"]);
			album.Discs.Add(discInfo);
		}
	}

	private static void AddTracks(VgmdbDisc discInfo, JToken tracks)
	{
		JArray trackArray = tracks as JArray;
		if (trackArray == null)
		{
			return;
		}

		foreach (JToken trackToken in trackArray)
		{
			JObject track = trackToken as JObject;
			if (track == null)
			{
				continue;
			}

			JObject names = track["names"] as JObject;
			Dictionary<string, string> localizedTrackNames = new Dictionary<string, string>();
			AddLocalizedText(localizedTrackNames, "ja", names?["Japanese"]?.ToString() ?? "");

			string englishTitle = names?["English"]?.ToString() ?? "";
			if (string.IsNullOrWhiteSpace(englishTitle))
			{
				englishTitle = names?["Romaji"]?.ToString() ?? "";
			}
			AddLocalizedText(localizedTrackNames, "en", englishTitle);

			discInfo.Tracks.Add(localizedTrackNames);
		}
	}

	static VgmdbTagProvider()
	{
		vgmdbInfoBaseUrl = "http://vgmdb.info/";
		albumSearchEndpointFormat = vgmdbInfoBaseUrl + "search/albums?q={0}&format=json";
		albumDetailsEndpointFormat = vgmdbInfoBaseUrl + "{0}?format=json";
	}

}
