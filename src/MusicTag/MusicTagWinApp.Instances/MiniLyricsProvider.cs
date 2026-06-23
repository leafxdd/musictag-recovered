using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Xml;
using MusicTag.Serialization;
using MusicTagWinApp.Adapter;
using MusicTagWinApp.Roles;
using MusicTagWinApp.Web;

namespace MusicTagWinApp.Instances;

internal class MiniLyricsProvider : RemoteTagProviderBase
{
	private sealed class LyricDownloadState
	{
		public string LyricUrl;

		internal LyricSearchResult DownloadLyric(CancellationTokenSource cancellation)
		{
			using MiniLyricsProvider provider = new MiniLyricsProvider(cancellation);
			return provider.DownloadLyric(LyricUrl);
		}
	}

	private static readonly string searchEndpointUrl;

	private static readonly string userAgent;

	private static readonly string searchOptions;

	private static readonly string searchXmlTemplate;

	private static readonly string requestPageTemplate;

	private static readonly byte[] protocolMagic;

	private static bool serviceUnavailable;

	protected override HttpClient CreateHttpClient()
	{
		HttpClient httpClient = new HttpClient(new HttpClientHandler
		{
			AutomaticDecompression = (DecompressionMethods.GZip | DecompressionMethods.Deflate)
		});
		httpClient.Timeout = TimeSpan.FromSeconds(15.0);
		httpClient.DefaultRequestHeaders.Add("User-Agent", userAgent);
		return httpClient;
	}

	public static void SetServiceUnavailable(bool unavailable)
	{
		serviceUnavailable = unavailable;
	}

	public static bool IsServiceUnavailable()
	{
		return serviceUnavailable;
	}

	protected override SearchSource GetSource()
	{
		return SearchSource.MiniLyrics;
	}

	public MiniLyricsProvider(CancellationTokenSource cancellation)
		: base(cancellation)
	{
	}

	public List<LyricSearchResult> SearchLyrics(string title, string artist, int sourceOrder)
	{
		if (!cancellationSource.IsCancellationRequested && !IsServiceUnavailable())
		{
			List<LyricSearchResult> lyrics = PostSearchRequest(string.Format(searchXmlTemplate, artist, title, searchOptions + string.Format(requestPageTemplate, 0)));
			int displayOrder = 0;
			foreach (LyricSearchResult lyric in lyrics)
			{
				lyric.ResultOrder = displayOrder++;
				lyric.SourceOrder = sourceOrder;
			}
			return lyrics;
		}
		return new List<LyricSearchResult>();
	}

	public List<TrackSearchResult> SearchTracks(string title, string artist, int searchPass, int sourceOrder, List<TrackSearchResult> existingTracks, List<TrackSearchResult> pendingTracks)
	{
		List<TrackSearchResult> tracks = new List<TrackSearchResult>();
		List<LyricSearchResult> lyrics = SearchLyrics(title, artist, 0);
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
		foreach (LyricSearchResult lyric in lyrics)
		{
			TrackSearchResult track = new TrackSearchResult();
			track.SearchSource = GetSource();
			track.SourceTrackId = lyric.TrackId;
			track.Title = lyric.Title;
			track.Artist = lyric.Artist;
			track.Album = lyric.Album;
			track.LyricResult = lyric;
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

	private List<LyricSearchResult> PostSearchRequest(string requestXml)
	{
		List<LyricSearchResult> lyrics = new List<LyricSearchResult>();
		try
		{
			byte[] buffer = EncodeRequest(Encoding.UTF8.GetBytes(requestXml));
			HttpResponseMessage httpResponseMessage = null;
			using (Stream content = new MemoryStream(buffer))
			{
				using HttpContent httpContent = new StreamContent(content);
				httpContent.Headers.Add("Content-Type", "application/x-www-form-urlencoded");
				httpResponseMessage = GetHttpClient().PostAsync(searchEndpointUrl, httpContent, cancellationSource.Token).Result;
			}
			if (httpResponseMessage != null && httpResponseMessage.StatusCode == HttpStatusCode.OK)
			{
				byte[] responseBytes = httpResponseMessage.Content.ReadAsByteArrayAsync().Result;
				responseBytes = DecodeResponse(responseBytes);
				ParseSearchResponse(responseBytes, lyrics);
				httpResponseMessage.Dispose();
			}
		}
		catch (Exception v)
		{
			SetServiceUnavailable(unavailable: true);
			Console.WriteLine("posthttp error:" + v.GetMessageChain());
		}
		return lyrics;
	}

	public LyricSearchResult DownloadLyric(string lyricUrl)
	{
		byte[] lyricBytes = GetResponseBytes(lyricUrl);
		if (lyricBytes == null || cancellationSource.IsCancellationRequested)
		{
			return null;
		}

		string lyric = IsUtf16WithByteOrderMark(lyricBytes) ? Encoding.Unicode.GetString(lyricBytes) : Encoding.UTF8.GetString(lyricBytes);
		return new LyricSearchResult
		{
			Lyric = lyric
		};
	}

	private static byte[] EncodeRequest(byte[] requestBytes)
	{
		byte[] signatureInput = new byte[requestBytes.Length + protocolMagic.Length];
		Array.Copy(requestBytes, 0, signatureInput, 0, requestBytes.Length);
		Array.Copy(protocolMagic, 0, signatureInput, requestBytes.Length, protocolMagic.Length);
		byte[] signature = DatabaseMapper.ComputeMd5Hash(signatureInput);

		int checksum = 0;
		foreach (byte requestByte in requestBytes)
		{
			checksum += requestByte;
		}
		byte xorKey = (byte)(checksum / requestBytes.Length);
		for (int i = 0; i < requestBytes.Length; i++)
		{
			requestBytes[i] = (byte)(xorKey ^ requestBytes[i]);
		}

		using (MemoryStream memoryStream = new MemoryStream())
		{
			memoryStream.WriteByte(2);
			memoryStream.WriteByte(xorKey);
			memoryStream.WriteByte(4);
			memoryStream.WriteByte(0);
			memoryStream.WriteByte(0);
			memoryStream.WriteByte(0);
			memoryStream.Write(signature, 0, signature.Length);
			memoryStream.Write(requestBytes, 0, requestBytes.Length);
			return memoryStream.ToArray();
		}
	}

	private static byte[] DecodeResponse(byte[] responseBytes)
	{
		if (responseBytes.Length <= 22)
		{
			throw new IOException("Length=" + responseBytes.Length + "<=22");
		}
		if (ByteEquals(responseBytes[0], 60))
		{
			return responseBytes;
		}
		if (!ByteEquals(responseBytes[0], 2))
		{
			throw new IOException("Unknown type: " + responseBytes[0]);
		}

		byte xorKey = responseBytes[1];
		if ((responseBytes[2] | (responseBytes[3] << 8) | (responseBytes[4] << 16) | (responseBytes[5] << 24)) != 4)
		{
			throw new IOException("4 is not found in signature");
		}
		using MemoryStream memoryStream = new MemoryStream();
		for (int i = 22; i < responseBytes.Length; i++)
		{
			memoryStream.WriteByte((byte)(responseBytes[i] ^ xorKey));
		}
		return memoryStream.ToArray();
	}

	private void ParseSearchResponse(byte[] responseBytes, List<LyricSearchResult> lyrics)
	{
		try
		{
			XmlDocument xmlDocument = new XmlDocument();
			using MemoryStream inStream = new MemoryStream(responseBytes);
			xmlDocument.Load(inStream);
			XmlNode responseNode = xmlDocument.SelectSingleNode("return");
			if (responseNode == null)
			{
				return;
			}

			ReadIntAttribute(responseNode, "CurPage", 0);
			ReadIntAttribute(responseNode, "PageCount", 1);
			string serverUrl = ReadStringAttribute(responseNode, "server_url", "http://www.viewlyrics.com/");
			XmlNodeList fileInfoNodes = responseNode.SelectNodes("fileinfo");
			if (fileInfoNodes == null)
			{
				return;
			}

			foreach (XmlNode fileInfo in fileInfoNodes)
			{
				string lyricPath = ReadStringAttribute(fileInfo, "link", "");
				if (!string.IsNullOrWhiteSpace(lyricPath))
				{
					LyricDownloadState downloadState = new LyricDownloadState();
					downloadState.LyricUrl = serverUrl + lyricPath;
					LyricSearchResult lyric = new LyricSearchResult();
					lyric.LyricUrl = downloadState.LyricUrl;
					lyric.Title = ReadStringAttribute(fileInfo, "title", "");
					lyric.Artist = ReadStringAttribute(fileInfo, "artist", "");
					lyric.Album = ReadStringAttribute(fileInfo, "album", "");
					lyric.SearchSource = GetSource();
					lyric.DeferredLyricLoader = downloadState.DownloadLyric;
					lyrics.Add(lyric);
				}
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine("ParseMiniLyricsResponse error:" + ex.GetMessageChain());
		}
	}

	private static bool ByteEquals(byte value, int expected)
	{
		return (value & 0xFF) == expected;
	}

	private static bool IsUtf16WithByteOrderMark(byte[] bytes)
	{
		return bytes.Length > 1 && ((ByteEquals(bytes[0], 255) && ByteEquals(bytes[1], 254)) || (ByteEquals(bytes[0], 254) && ByteEquals(bytes[1], 255)));
	}

	private static int ReadIntAttribute(XmlNode node, string attributeName, int defaultValue)
	{
		try
		{
			string value = node.Attributes[attributeName]?.Value;
			if (!string.IsNullOrWhiteSpace(value))
			{
				return int.Parse(value);
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine("ReadIntFromAttr error:" + ex.Message);
		}
		return defaultValue;
	}

	private static string ReadStringAttribute(XmlNode node, string attributeName, string defaultValue)
	{
		try
		{
			return node.Attributes[attributeName]?.Value ?? defaultValue;
		}
		catch (Exception ex)
		{
			Console.WriteLine("ReadIntFromAttr error:" + ex.Message);
			return defaultValue;
		}
	}

	[DllImport("MusicTag.dll", EntryPoint = "nk")]
	private static extern IntPtr GetSearchEndpointUrl();

	[DllImport("MusicTag.dll", EntryPoint = "nl")]
	private static extern IntPtr GetUserAgent();

	[DllImport("MusicTag.dll", EntryPoint = "nm")]
	private static extern IntPtr GetSearchOptions();

	static MiniLyricsProvider()
	{
		searchEndpointUrl = Marshal.PtrToStringUni(GetSearchEndpointUrl());
		userAgent = Marshal.PtrToStringUni(GetUserAgent());
		searchOptions = Marshal.PtrToStringUni(GetSearchOptions());
		searchXmlTemplate = "<?xml version='1.0' encoding='utf-8' ?><searchV1 artist=\"{0}\" title=\"{1}\" OnlyMatched=\"1\" {2}/>";
		requestPageTemplate = " RequestPage='{0}'";
		protocolMagic = Encoding.UTF8.GetBytes("Mlv1clt4.0");
		serviceUnavailable = false;
	}
}
