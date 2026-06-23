using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using MusicTag.Candidates;
using MusicTagWinApp.Instances;
using MusicTagWinApp.Listeners;
using MusicTagWinApp.Web;
using Newtonsoft.Json.Linq;

namespace MusicTag.Serialization;

internal class LastfmCoverProvider : RemoteTagProviderBase
{
	private static readonly string baseUrl;

	private static readonly string apiKey;

	private static readonly string searchUrlTemplate;

	protected override SearchSource GetSource()
	{
		return SearchSource.Lastfm;
	}

	protected override HttpClient CreateHttpClient()
	{
		return KugouTagProvider.CreateKugouHttpClient();
	}

	public LastfmCoverProvider(CancellationTokenSource cancellation)
		: base(cancellation)
	{
	}

	public LastfmCoverProvider()
		: this(null)
	{
	}

	public List<CoverSearchResult> SearchTracks(string title, string artist, string album, int resultLimit, List<CoverSearchResult> existingCovers)
	{
		StringBuilder query = new StringBuilder();
		if (!string.IsNullOrWhiteSpace(title))
		{
			query.Append("&track=" + DatabaseMapper.UrlEncodeUtf8(title));
		}
		if (!string.IsNullOrWhiteSpace(artist))
		{
			query.Append("&artist=" + DatabaseMapper.UrlEncodeUtf8(artist));
		}
		if (!string.IsNullOrWhiteSpace(album))
		{
			query.Append("&album=" + DatabaseMapper.UrlEncodeUtf8(album));
		}
		return SearchImages(string.Format(searchUrlTemplate, resultLimit, "track.search", query), "trackmatches", "track", existingCovers);
	}

	public List<CoverSearchResult> SearchAlbums(string artist, string album, int resultLimit, List<CoverSearchResult> existingCovers)
	{
		StringBuilder query = new StringBuilder();
		if (!string.IsNullOrWhiteSpace(album))
		{
			query.Append("&album=" + DatabaseMapper.UrlEncodeUtf8(album));
		}
		if (!string.IsNullOrWhiteSpace(artist))
		{
			query.Append("&artist=" + DatabaseMapper.UrlEncodeUtf8(artist));
		}
		return SearchImages(string.Format(searchUrlTemplate, resultLimit, "album.search", query), "albummatches", "album", existingCovers);
	}

	public List<CoverSearchResult> SearchArtists(string artist, int resultLimit, List<CoverSearchResult> existingCovers)
	{
		string query = "";
		if (!string.IsNullOrWhiteSpace(artist))
		{
			query = "&artist=" + DatabaseMapper.UrlEncodeUtf8(artist);
		}
		return SearchImages(string.Format(searchUrlTemplate, resultLimit, "artist.search", query), "artistmatches", "artist", existingCovers);
	}

	private List<CoverSearchResult> SearchImages(string url, string matchesPropertyName, string itemPropertyName, List<CoverSearchResult> existingCovers)
	{
		List<CoverSearchResult> covers = new List<CoverSearchResult>();
		HashSet<string> seenCoverUrls = new HashSet<string>();
		if (cancellationSource.IsCancellationRequested)
		{
			return covers;
		}
		try
		{
				JArray resultItems = JObject.Parse(GetResponseString(url))["results"]?[matchesPropertyName]?[itemPropertyName] as JArray;
				if (resultItems == null)
				{
					return covers;
				}

				foreach (JToken resultItem in resultItems)
				{
					JObject resultObject = resultItem as JObject;
					if (resultObject == null)
					{
						continue;
					}

					if (cancellationSource.IsCancellationRequested)
					{
						break;
					}

					string coverUrl = GetBestImageUrl(resultObject["image"]);
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
					cover.CoverDownloader = CreateCoverDownloader<LastfmCoverProvider>(coverUrl);
					covers.Add(cover);
					seenCoverUrls.Add(coverUrl);
				}
			}
		}
		catch (Exception searchError)
		{
			Console.WriteLine("SearchPicture error:" + searchError.GetMessageChain());
		}
		return covers;
	}

	private static string GetBestImageUrl(JToken images)
	{
		Dictionary<string, string> urlsBySize = new Dictionary<string, string>();
		if (images == null)
		{
			return null;
		}

			JArray imageItems = images as JArray;
			if (imageItems == null)
			{
				return null;
			}

			foreach (JToken image in imageItems)
			{
				JObject imageObject = image as JObject;
				if (imageObject == null)
				{
					continue;
				}

				string size = imageObject["size"]?.ToString() ?? "";
				string imageUrl = imageObject["#text"]?.ToString() ?? "";
			if (!string.IsNullOrWhiteSpace(imageUrl) && !string.IsNullOrWhiteSpace(size) && !urlsBySize.ContainsKey(size))
			{
				urlsBySize.Add(size, imageUrl);
			}
		}

		if (!urlsBySize.TryGetValue("mega", out var coverUrl) && !urlsBySize.TryGetValue("extralarge", out coverUrl) && !urlsBySize.TryGetValue("large", out coverUrl))
		{
			urlsBySize.TryGetValue("medium", out coverUrl);
		}
		return coverUrl;
	}

	[DllImport("MusicTag.dll", EntryPoint = "nj")]
	private static extern IntPtr GetApiKey();

	static LastfmCoverProvider()
	{
		baseUrl = "http://ws.audioscrobbler.com/2.0/";
		apiKey = Marshal.PtrToStringUni(GetApiKey());
		searchUrlTemplate = baseUrl + "?limit={0}&format=json&api_key=" + apiKey + "&method={1}{2}";
	}
}
