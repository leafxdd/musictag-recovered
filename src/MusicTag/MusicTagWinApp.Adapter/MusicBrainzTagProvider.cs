using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using MusicTag.Serialization;
using MusicTag.Services;
using MusicTagWinApp.Instances;
using MusicTagWinApp.Listeners;
using MusicTagWinApp.Properties;
using MusicTagWinApp.Roles;
using MusicTagWinApp.Web;
using Newtonsoft.Json.Linq;

namespace MusicTagWinApp.Adapter;

internal class MusicBrainzTagProvider : RemoteTagProviderBase
{
	private static readonly string musicBrainzApiBaseUrl;

	private static readonly string recordingSearchEndpointFormat;

	private static readonly string releaseSearchEndpointFormat;

	private static readonly string releaseLookupEndpointFormat;

	private static readonly string releaseLookupIncludes;

	private static readonly string coverArtArchiveFrontUrlFormat;

	protected override SearchSource GetSource()
	{
		return SearchSource.Brainz;
	}

	protected override HttpClient CreateHttpClient()
	{
		HttpClient client = new HttpClient(new HttpClientHandler
		{
			AutomaticDecompression = (DecompressionMethods.GZip | DecompressionMethods.Deflate)
		})
		{
			Timeout = TimeSpan.FromSeconds(15.0)
		};
		client.DefaultRequestHeaders.Add("user-agent", "Mozilla/5.0 (Windows NT 6.3; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/68.0.3440.106 Safari/537.36");
		client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3");
		return client;
	}

	public MusicBrainzTagProvider(CancellationTokenSource cancellationSource)
		: base(cancellationSource)
	{
	}

	public MusicBrainzTagProvider()
		: this(null)
	{
	}

	public List<MusicBrainzReleaseMatch> SearchRecordings(string title, string artist, string album, int resultLimit)
	{
		if (cancellationSource.IsCancellationRequested)
		{
			return new List<MusicBrainzReleaseMatch>();
		}
		StringBuilder queryBuilder = new StringBuilder();
		if (!string.IsNullOrWhiteSpace(title))
		{
			AppendTextQuery(queryBuilder, title);
		}
		if (!string.IsNullOrWhiteSpace(artist))
		{
			AppendFieldQuery(queryBuilder, "artist", artist);
		}
		if (!string.IsNullOrWhiteSpace(album))
		{
			AppendFieldQuery(queryBuilder, "release", album);
		}
		string responseBody = GetResponseString(string.Format(recordingSearchEndpointFormat, queryBuilder.ToString(), resultLimit));
		if (cancellationSource.IsCancellationRequested)
		{
			return new List<MusicBrainzReleaseMatch>();
		}
		return ParseRecordingSearchResponse(responseBody);
	}

	public List<MusicBrainzReleaseMatch> SearchAlbums(string artist, string album, int resultLimit)
	{
		if (cancellationSource.IsCancellationRequested)
		{
			return new List<MusicBrainzReleaseMatch>();
		}
		string responseBody = "";
		try
		{
			StringBuilder queryBuilder = new StringBuilder();
			if (!string.IsNullOrWhiteSpace(album))
			{
				AppendTextQuery(queryBuilder, album);
			}
			if (!string.IsNullOrWhiteSpace(artist))
			{
				AppendFieldQuery(queryBuilder, "artist", artist);
			}
			responseBody = GetResponseString(string.Format(releaseSearchEndpointFormat, queryBuilder.ToString(), resultLimit));
		}
		catch (Exception ex)
		{
			Console.WriteLine("SearchAlbums error:" + ex.GetMessageChain());
		}
		if (cancellationSource.IsCancellationRequested)
		{
			return new List<MusicBrainzReleaseMatch>();
		}
		return ParseAlbumSearchResponse(responseBody, includeTrackDetails: false);
	}

	public List<MusicBrainzReleaseMatch> LookupReleaseTracks(string releaseId)
	{
		if (cancellationSource.IsCancellationRequested)
		{
			return new List<MusicBrainzReleaseMatch>();
		}
		try
		{
			string responseBody = GetResponseString(string.Format(releaseLookupEndpointFormat, releaseId, DatabaseMapper.UrlEncodeUtf8(releaseLookupIncludes)));
			return (!cancellationSource.IsCancellationRequested) ? ParseAlbumResponse(responseBody, includeTrackDetails: true) : new List<MusicBrainzReleaseMatch>();
		}
		catch (Exception ex)
		{
			Console.WriteLine("LookupAlbum error:" + ex.GetMessageChain());
		}
		return new List<MusicBrainzReleaseMatch>();
	}

	private static void AppendQueryClause(StringBuilder queryBuilder, string clause)
	{
		if (queryBuilder.Length > 0)
		{
			queryBuilder.Append("%20and%20");
		}
		queryBuilder.Append(clause);
	}

	private static void AppendTextQuery(StringBuilder queryBuilder, string value)
	{
		AppendQueryClause(queryBuilder, DatabaseMapper.UrlEncodeUtf8(EscapeMusicBrainzQueryTerm(value)));
	}

	private static void AppendFieldQuery(StringBuilder queryBuilder, string fieldName, string value)
	{
		AppendQueryClause(queryBuilder, DatabaseMapper.UrlEncodeUtf8($"{fieldName}:{EscapeMusicBrainzQueryTerm(value)}"));
	}

	private static string EscapeMusicBrainzQueryTerm(string value)
	{
		value = value.Replace("\\", "\\\\");
		value = value.Replace(":", "\\:");
		value = value.Replace("&", " ");
		value = value.Replace("|", " ");
		value = value.Replace("+", "\\+");
		value = value.Replace("-", "\\-");
		value = value.Replace("!", "\\!");
		value = value.Replace("^", "\\^");
		value = value.Replace("~", "\\~");
		value = value.Replace("/", " ");
		value = value.Replace("[", "\\[");
		value = value.Replace("]", "\\]");
		value = value.Replace("(", "\\(");
		value = value.Replace(")", "\\)");
		value = value.Replace("{", "\\{");
		value = value.Replace("}", "\\}");
		string lowerValue = value.ToLower();
		if (lowerValue.IndexOf("and") >= 0 || lowerValue.IndexOf("or") >= 0 || lowerValue.IndexOf("not") >= 0)
		{
			value = string.Format("\"{0}\"", value);
		}
		return value;
	}

	private static string GetStringOrEmpty(JToken token)
	{
		return token?.ToString() ?? "";
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

	private static string BuildReleaseTypeText(JToken releaseGroupJson)
	{
		StringBuilder releaseTypeBuilder = new StringBuilder();
		if (releaseGroupJson == null)
		{
			return releaseTypeBuilder.ToString();
		}
		releaseTypeBuilder.Append(GetStringOrEmpty(releaseGroupJson["primary-type"]));
		JArray secondaryTypesJson = releaseGroupJson["secondary-types"] as JArray;
		if (secondaryTypesJson != null)
		{
			int secondaryTypeIndex = 0;
			foreach (JToken secondaryTypeJson in secondaryTypesJson)
			{
				releaseTypeBuilder.Append((secondaryTypeIndex == 0) ? " + " : ",");
				releaseTypeBuilder.Append(secondaryTypeJson.ToString());
				secondaryTypeIndex++;
			}
		}
		return releaseTypeBuilder.ToString();
	}

	private static string BuildArtistCreditText(JToken artistCreditsJson)
	{
		StringBuilder artistBuilder = new StringBuilder();
		JArray artistCredits = artistCreditsJson as JArray;
		if (artistCredits == null)
		{
			return artistBuilder.ToString();
		}

		int artistIndex = 0;
		foreach (JToken artistCreditJson in artistCredits)
		{
			if (artistIndex > 0)
			{
				artistBuilder.Append(Settings.Default.ConnectorsArtists);
			}
			artistBuilder.Append(GetStringOrEmpty(artistCreditJson["name"]));
			artistIndex++;
		}
		return artistBuilder.ToString();
	}

	private List<MusicBrainzReleaseMatch> ParseRecordingSearchResponse(string responseBody)
	{
		List<MusicBrainzReleaseMatch> results = new List<MusicBrainzReleaseMatch>();
		try
		{
			JArray recordingsJson = JObject.Parse(responseBody)["recordings"] as JArray;
			if (recordingsJson == null)
			{
				return results;
			}

			foreach (JToken recordingToken in recordingsJson)
			{
				JObject recordingJson = recordingToken as JObject;
				if (recordingJson == null)
				{
					continue;
				}

				string recordingId = GetStringOrEmpty(recordingJson["id"]);
				string recordingTitle = GetStringOrEmpty(recordingJson["title"]);
				string recordingArtist = GetStringOrEmpty(recordingJson["artist-credit"]?[0]?["artist"]?["name"]);
				JArray releasesJson = recordingJson["releases"] as JArray;
				if (releasesJson == null)
				{
					continue;
				}

				foreach (JToken releaseToken in releasesJson)
				{
					JObject releaseJson = releaseToken as JObject;
					if (releaseJson == null)
					{
						continue;
					}

					results.Add(new MusicBrainzReleaseMatch
					{
						RecordingId = recordingId,
						ReleaseId = GetStringOrEmpty(releaseJson["id"]),
						TrackTitle = recordingTitle,
						ArtistName = recordingArtist,
						AlbumTitle = GetStringOrEmpty(releaseJson["title"]),
						ReleaseDate = GetStringOrEmpty(releaseJson["date"]),
						Country = GetStringOrEmpty(releaseJson["country"]),
						ReleaseType = BuildReleaseTypeText(releaseJson["release-group"]),
						Asin = GetStringOrEmpty(releaseJson["asin"]),
						Disambiguation = GetStringOrEmpty(releaseJson["disambiguation"])
					});
				}
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine("ParseTracksJson error:" + ex.GetMessageChain());
		}
		return results;
	}

	private List<MusicBrainzReleaseMatch> ParseAlbumSearchResponse(string responseBody, bool includeTrackDetails)
	{
		List<MusicBrainzReleaseMatch> results = new List<MusicBrainzReleaseMatch>();
		try
		{
			JArray releasesJson = JObject.Parse(responseBody)["releases"] as JArray;
			if (releasesJson == null)
			{
				return results;
			}

			foreach (JToken releaseToken in releasesJson)
			{
				JObject releaseJson = releaseToken as JObject;
				if (releaseJson == null)
				{
					continue;
				}

				results.AddRange(ParseAlbumResponse(releaseJson.ToString(), includeTrackDetails));
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine("ParseAlbumsJson error:" + ex.GetMessageChain());
		}
		return results;
	}

	private List<MusicBrainzReleaseMatch> ParseAlbumResponse(string responseBody, bool includeTrackDetails)
	{
		List<MusicBrainzReleaseMatch> results = new List<MusicBrainzReleaseMatch>();
		try
		{
			JObject releaseJson = JObject.Parse(responseBody);
			string releaseId = GetStringOrEmpty(releaseJson["id"]);
			string releaseTitle = GetStringOrEmpty(releaseJson["title"]);
			string releaseDate = GetStringOrEmpty(releaseJson["date"]);
			string country = GetStringOrEmpty(releaseJson["country"]);
			string asin = GetStringOrEmpty(releaseJson["asin"]);
			string disambiguation = GetStringOrEmpty(releaseJson["disambiguation"]);
			string albumArtist = GetStringOrEmpty(releaseJson["artist-credit"]?[0]?["artist"]?["name"]);
			string releaseType = BuildReleaseTypeText(releaseJson["release-group"]);
			if (includeTrackDetails)
			{
				JArray mediaJson = releaseJson["media"] as JArray;
				if (mediaJson != null)
				{
						foreach (JToken mediumJson in mediaJson)
						{
							JObject mediumObject = mediumJson as JObject;
							if (mediumObject == null)
							{
								continue;
							}

							JArray tracksJson = mediumObject["tracks"] as JArray;
							if (tracksJson == null)
							{
								continue;
							}

							foreach (JToken trackToken in tracksJson)
							{
								JObject trackJson = trackToken as JObject;
								if (trackJson == null)
								{
									continue;
								}

								JToken recordingJson = trackJson["recording"];
								MusicBrainzReleaseMatch track = new MusicBrainzReleaseMatch
								{
								ReleaseId = releaseId,
								RecordingId = GetStringOrEmpty(recordingJson?["id"]),
								TrackTitle = GetStringOrEmpty(recordingJson?["title"]),
								AlbumTitle = releaseTitle,
								ReleaseDate = releaseDate,
								Country = country,
									Asin = asin,
									Disambiguation = disambiguation,
									ReleaseType = releaseType,
									MediaFormat = GetStringOrEmpty(mediumObject["format"]),
									DiscCount = mediaJson.Count,
									DiscNumber = GetNullableIntField(mediumObject, "position"),
									TrackCount = GetNullableIntField(mediumObject, "track-count"),
									TrackNumber = GetNullableIntField(trackJson, "position"),
									ArtistName = BuildArtistCreditText(recordingJson?["artist-credit"])
								};
							results.Add(track);
						}
					}
				}
			}
			else
			{
				MusicBrainzReleaseMatch album = new MusicBrainzReleaseMatch();
				album.ReleaseId = releaseId;
				album.ArtistName = albumArtist;
				album.AlbumTitle = releaseTitle;
				album.ReleaseDate = releaseDate;
				album.Country = country;
				album.Asin = asin;
				album.Disambiguation = disambiguation;
				album.ReleaseType = releaseType;
				results.Add(album);
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine("ParseAlbumJson error:" + ex.GetMessageChain());
		}
		return results;
	}

	public List<TrackSearchResult> SearchTracks(string title, string artist, string album, int resultLimit, int searchPass, int sourceOrder, List<TrackSearchResult> existingTracks, List<TrackSearchResult> previousResults)
	{
		List<TrackSearchResult> tracks = new List<TrackSearchResult>();
		List<MusicBrainzReleaseMatch> recordingCandidates = SearchRecordings(title, artist, album, resultLimit);
		Dictionary<string, TrackSearchResult> tracksById = new Dictionary<string, TrackSearchResult>();
		List<string> trackIdsInOrder = new List<string>();
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
		foreach (MusicBrainzReleaseMatch recordingCandidate in recordingCandidates)
		{
			if (cancellationSource.IsCancellationRequested)
			{
				break;
			}
			MusicBrainzReleaseMatch matchedTrack = null;
			foreach (MusicBrainzReleaseMatch releaseTrack in LookupReleaseTracks(recordingCandidate.ReleaseId))
			{
				if (releaseTrack.RecordingId == recordingCandidate.RecordingId)
				{
					matchedTrack = releaseTrack;
					break;
				}
			}
			if (matchedTrack == null)
			{
				continue;
			}
			TrackSearchResult track = new TrackSearchResult();
			track.SearchSource = GetSource();
			track.SourceTrackId = matchedTrack.RecordingId;
			track.Title = matchedTrack.TrackTitle;
			track.Artist = matchedTrack.ArtistName;
			track.Album = matchedTrack.AlbumTitle;
			track.Comment = "";
			if (DateTime.TryParse(matchedTrack.ReleaseDate, out var releaseDate))
			{
				track.Year = releaseDate.ToString("yyyy");
			}
			if (matchedTrack.TrackNumber.HasValue)
			{
				track.Track = matchedTrack.TrackNumber.Value;
				track.TrackLabel = "Track " + matchedTrack.TrackNumber;
				if (matchedTrack.DiscCount > 1 && matchedTrack.DiscNumber.HasValue)
				{
					track.Disc = matchedTrack.DiscNumber.Value;
					track.TrackLabel = track.TrackLabel + " of " + matchedTrack.DiscNumber;
				}
			}
			string coverUrl = string.Format(coverArtArchiveFrontUrlFormat, recordingCandidate.ReleaseId);
			CoverSearchResult cover = new CoverSearchResult();
			cover.CoverUrl = coverUrl;
			cover.CoverDownloader = CreateCoverDownloader<MusicBrainzTagProvider>(coverUrl);
			track.Cover = cover;
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
		int resultOrder = 0;
		foreach (TrackSearchResult track in tracks)
		{
			track.ResultOrder = resultOrder++;
			track.SearchPass = searchPass;
			track.SourceOrder = sourceOrder;
		}
		return tracks;
	}

	static MusicBrainzTagProvider()
	{
		musicBrainzApiBaseUrl = "http://musicbrainz.org/ws/2/";
		recordingSearchEndpointFormat = musicBrainzApiBaseUrl + "recording?query={0}&fmt=json&limit={1}";
		releaseSearchEndpointFormat = musicBrainzApiBaseUrl + "release?query={0}&fmt=json&limit={1}";
		releaseLookupEndpointFormat = musicBrainzApiBaseUrl + "release/{0}?fmt=json&inc={1}";
		releaseLookupIncludes = "aliases+artist-credits+discids+labels+recordings";
		coverArtArchiveFrontUrlFormat = "http://coverartarchive.org/release/{0}/front";
	}
}
