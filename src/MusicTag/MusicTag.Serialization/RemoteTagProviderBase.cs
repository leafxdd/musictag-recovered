using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MusicTagWinApp.Adapter;
using MusicTagWinApp.Instances;
using MusicTagWinApp.Listeners;
using MusicTagWinApp.Properties;
using MusicTagWinApp.Roles;
using MusicTagWinApp.Web;
using MusicTagWinApp.Writers;
using Newtonsoft.Json.Linq;

namespace MusicTag.Serialization;

internal abstract class RemoteTagProviderBase : IDisposable
{
	internal const int MaxApiResponseBytes = 8 * 1024 * 1024;

	internal const long MaxCoverDownloadBytes = 32L * 1024L * 1024L;

	public enum DownloadStatus
	{
		Success,
		NotFound,
		Error,
		NotStarted
	}

	private HttpClient httpClient;

	private CancellationTokenSource fallbackCancellationSource;

	protected CancellationTokenSource cancellationSource;

	protected abstract SearchSource GetSource();

	protected HttpClient GetHttpClient()
	{
		if (httpClient == null)
		{
			httpClient = CreateHttpClient();
			ApplyCustomUserAgent(httpClient);
		}
		return httpClient;
	}

	private static void ApplyCustomUserAgent(HttpClient client)
	{
		string customUserAgent = Settings.Default.WebSearch_CustomUserAgent;
		if (!string.IsNullOrWhiteSpace(customUserAgent))
		{
			client.DefaultRequestHeaders.Remove("User-Agent");
			client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", customUserAgent.Trim());
		}
	}

	protected abstract HttpClient CreateHttpClient();

	public RemoteTagProviderBase(CancellationTokenSource cancellationSource)
	{
		SetCancellationSource(cancellationSource);
	}

	public void Dispose()
	{
		httpClient?.Dispose();
		fallbackCancellationSource?.Dispose();
	}

	protected void SetCancellationSource(CancellationTokenSource cancellationSource)
	{
		fallbackCancellationSource?.Dispose();
		fallbackCancellationSource = null;
		if (cancellationSource == null)
		{
			fallbackCancellationSource = new CancellationTokenSource();
			this.cancellationSource = fallbackCancellationSource;
			return;
		}

		this.cancellationSource = cancellationSource;
	}

	// 最近一次传输调用(本 provider 实例)的结果,供上层在拿到空结果时区分
	// "搜到 0 条" 与 "网络/HTTP 失败"。每个源使用独立 provider 实例,并行下互不干扰。
	public HttpResult LastTransportResult { get; private set; }

	private HttpResult RecordResult(HttpResult result)
	{
		LastTransportResult = result;
		return result;
	}

	// 状态上报通道(可选):provider 在搜索过程中(如 QQ 限流重试)推送 SourceSearchStatus,
	// 由协调器转发到 UI。默认 null(自动匹配等无 UI 场景不设置,零开销)。
	public Action<SourceSearchStatus> StatusReporter { get; set; }

	protected void ReportStatus(SourceSearchPhase phase, string errorCode = null, int retryAttempt = 0, int retryTotal = 0, int retrySecondsLeft = 0, int cooldownSecondsLeft = 0)
	{
		StatusReporter?.Invoke(new SourceSearchStatus
		{
			Source = GetSource(),
			Phase = phase,
			ErrorCode = errorCode,
			RetryAttempt = retryAttempt,
			RetryTotal = retryTotal,
			RetrySecondsLeft = retrySecondsLeft,
			CooldownSecondsLeft = cooldownSecondsLeft
		});
	}

	// provider 在解析阶段判定业务错误(如 QQ 限流 2001)后回填,使 LastTransportResult
	// 反映业务码而非传输层的"HTTP 200 成功"。
	protected void SetTransportError(RemoteErrorKind error, string errorCode)
	{
		LastTransportResult = new HttpResult { Error = error, ErrorCode = errorCode };
	}

	// 把传输异常归类为可观测的错误类型(详见 docs/SEARCH_STATUS_INDICATOR_DESIGN.md §5.4)。
	// 用户/全局取消不算错误,返回 None。
	private HttpResult ClassifyException(Exception exception)
	{
		return ClassifyException(exception, cancellationSource.IsCancellationRequested);
	}

	// 传输异常归类的纯核(提取供 characterization):取消优先 -> None;AggregateException 经
	// GetBaseException 解包后 TaskCanceled/Timeout -> Timeout;否则 -> Network。实例方法委托,
	// 读取消状态的时机(方法入口)不变。
	internal static HttpResult ClassifyException(Exception exception, bool cancellationRequested)
	{
		if (cancellationRequested)
		{
			return new HttpResult { Error = RemoteErrorKind.None };
		}
		Exception rootException = (exception is AggregateException aggregate) ? aggregate.GetBaseException() : exception;
		if (rootException is ResponseSizeLimitExceededException)
		{
			return new HttpResult { Error = RemoteErrorKind.Network, ErrorCode = "response_too_large" };
		}
		if (rootException is TaskCanceledException || rootException is TimeoutException)
		{
			return new HttpResult { Error = RemoteErrorKind.Timeout, ErrorCode = "timeout" };
		}
		return new HttpResult { Error = RemoteErrorKind.Network, ErrorCode = "network" };
	}

	protected virtual HttpResult PostStringResult(string url, string body, HttpClient client = null, bool postJson = false)
	{
		if (client == null)
		{
			client = GetHttpClient();
		}
		try
		{
			using CancellationTokenSource requestCancellation = CreateRequestCancellation(client, cancellationSource.Token);
			CancellationToken requestToken = requestCancellation.Token;
			HttpContent requestContent;
			if (postJson)
			{
				requestContent = new StringContent(body, Encoding.UTF8, "application/json");
			}
			else
			{
				requestContent = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
				requestContent.Headers.Add("Content-Type", "application/x-www-form-urlencoded");
			}

			using (requestContent)
			using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url) { Content = requestContent })
			using (HttpResponseMessage response = client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestToken).Result)
			{
				if (response.StatusCode == HttpStatusCode.OK)
				{
					return RecordResult(new HttpResult { Body = Encoding.UTF8.GetString(ReadResponseBody(response.Content, MaxApiResponseBytes, requestToken)) });
				}
				return RecordResult(HttpResult.FromHttpStatus((int)response.StatusCode));
			}
		}
		catch (Exception exception)
		{
			Console.WriteLine("PostHttp error:" + exception.GetMessageChain());
			return RecordResult(ClassifyException(exception));
		}
	}

	protected virtual string PostString(string url, string body, HttpClient client = null, bool postJson = false)
	{
		return PostStringResult(url, body, client, postJson).Body;
	}

	protected virtual HttpResult GetResponseBytesResult(string url)
	{
		try
		{
			HttpClient client = GetHttpClient();
			using CancellationTokenSource requestCancellation = CreateRequestCancellation(client, cancellationSource.Token);
			CancellationToken requestToken = requestCancellation.Token;
			using HttpResponseMessage response = client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, requestToken).Result;
			if (response.IsSuccessStatusCode)
			{
				return RecordResult(new HttpResult { Bytes = ReadResponseBody(response.Content, MaxApiResponseBytes, requestToken) });
			}
			return RecordResult(HttpResult.FromHttpStatus((int)response.StatusCode));
		}
		catch (Exception exception)
		{
			Console.WriteLine("GetHttp error:" + exception.GetMessageChain());
			return RecordResult(ClassifyException(exception));
		}
	}

	protected virtual byte[] GetResponseBytes(string url)
	{
		return GetResponseBytesResult(url).Bytes;
	}

	protected virtual HttpResult GetResponseStringResult(string url)
	{
		HttpResult result = GetResponseBytesResult(url);
		if (result.Bytes != null)
		{
			result.Body = Encoding.UTF8.GetString(result.Bytes);
		}
		return result;
	}

	protected virtual string GetResponseString(string url)
	{
		return GetResponseStringResult(url).Body ?? "";
	}

	protected virtual DownloadStatus DownloadToStream(string url, Stream destination, int requestTimeout = 300000)
	{
		try
		{
			// net8 迁移:WebRequestHandler → HttpClientHandler(同 NetEaseMusicTagProvider)。丢失
			// ReadWriteTimeout=30000 流级超时,由 downloadClient.Timeout(requestTimeout)+ 取消令牌兜底。
			using HttpClient downloadClient = new HttpClient(new HttpClientHandler
			{
				AutomaticDecompression = (DecompressionMethods.GZip | DecompressionMethods.Deflate)
			});
			downloadClient.Timeout = TimeSpan.FromMilliseconds(requestTimeout);
			downloadClient.DefaultRequestHeaders.Add("user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
			ApplyCustomUserAgent(downloadClient);
			if (!(this is QqMusicTagProvider))
			{
				downloadClient.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3");
			}
			using CancellationTokenSource requestCancellation = CreateRequestCancellation(downloadClient, cancellationSource.Token);
			CancellationToken requestToken = requestCancellation.Token;
			using HttpResponseMessage response = downloadClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, requestToken).Result;
			if (response.IsSuccessStatusCode)
			{
				ValidateContentLength(response.Content, MaxCoverDownloadBytes);
				using Stream responseStream = response.Content.ReadAsStreamAsync(requestToken).Result;
				long bytesReadTotal = CopyStreamWithLimit(responseStream, destination, MaxCoverDownloadBytes, requestToken);
				return (cancellationSource.IsCancellationRequested || bytesReadTotal <= 0) ? DownloadStatus.Error : DownloadStatus.Success;
			}
			return (response.StatusCode == HttpStatusCode.NotFound) ? DownloadStatus.NotFound : DownloadStatus.Error;
		}
		catch (Exception exception)
		{
			Console.WriteLine("GetHttpStream error:" + exception.GetMessageChain());
		}
		return DownloadStatus.Error;
	}

	private static CancellationTokenSource CreateRequestCancellation(HttpClient client, CancellationToken cancellationToken)
	{
		CancellationTokenSource linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		if (client.Timeout != Timeout.InfiniteTimeSpan)
		{
			linkedCancellation.CancelAfter(client.Timeout);
		}
		return linkedCancellation;
	}

	private static byte[] ReadResponseBody(HttpContent content, int maximumBytes, CancellationToken cancellationToken)
	{
		ValidateContentLength(content, maximumBytes);
		using Stream responseStream = content.ReadAsStreamAsync(cancellationToken).Result;
		using MemoryStream memoryStream = new MemoryStream();
		CopyStreamWithLimit(responseStream, memoryStream, maximumBytes, cancellationToken);
		return memoryStream.ToArray();
	}

	private static void ValidateContentLength(HttpContent content, long maximumBytes)
	{
		if (content.Headers.ContentLength is long contentLength && contentLength > maximumBytes)
		{
			throw new ResponseSizeLimitExceededException(maximumBytes);
		}
	}

	internal static long CopyStreamWithLimit(Stream source, Stream destination, long maximumBytes, CancellationToken cancellationToken)
	{
		byte[] buffer = new byte[8192];
		long bytesReadTotal = 0L;
		while (true)
		{
			int bytesRead = source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).AsTask().GetAwaiter().GetResult();
			if (bytesRead <= 0)
			{
				break;
			}
			if (bytesReadTotal > maximumBytes - bytesRead)
			{
				throw new ResponseSizeLimitExceededException(maximumBytes);
			}
			destination.Write(buffer, 0, bytesRead);
			bytesReadTotal += bytesRead;
		}
		return bytesReadTotal;
	}

	protected Func<CancellationTokenSource, string, int, (DownloadStatus, long)> CreateCoverDownloader<T>(string url) where T : RemoteTagProviderBase, new()
	{
		return (cancellation, filePath, timeout) =>
		{
			using T downloader = new T();
			downloader.SetCancellationSource(cancellation);
			using FileStream fileStream = new FileStream(filePath, FileMode.Create);
			return (downloader.DownloadToStream(url, fileStream, timeout), fileStream.Length);
		};
	}

	// 候选装配共用骨架(原各 provider 内联的 拉取→去重→排序 循环上提;详见 docs/SIMPLIFICATION_PLAN.md B3-1)。
	// 仅接收已拉取的歌曲序列 + 构造委托,内部从不发起网络。Tracks 无循环内取消检查(三源原本即无),
	// Lyrics/Covers 在每次迭代开头做取消短路,与原各 provider 一致。
	protected List<TrackSearchResult> BuildOrderedTracks<TSong>(IEnumerable<TSong> songs, Func<TSong, TrackSearchResult> buildTrack, int searchPass, int sourceOrder, List<TrackSearchResult> excludeA, List<TrackSearchResult> excludeB)
	{
		HashSet<string> knownTrackIds = new HashSet<string>();
		foreach (TrackSearchResult track in excludeA)
		{
			if (track.SearchSource == GetSource())
			{
				knownTrackIds.Add(track.SourceTrackId);
			}
		}

		foreach (TrackSearchResult track in excludeB)
		{
			if (track.SearchSource == GetSource())
			{
				knownTrackIds.Add(track.SourceTrackId);
			}
		}

		List<TrackSearchResult> tracks = new List<TrackSearchResult>();
		HashSet<string> seenTrackIds = new HashSet<string>();
		foreach (TSong song in songs)
		{
			TrackSearchResult track = buildTrack(song);
			if (!seenTrackIds.Contains(track.SourceTrackId) && !knownTrackIds.Contains(track.SourceTrackId))
			{
				seenTrackIds.Add(track.SourceTrackId);
				tracks.Add(track);
			}
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

	protected List<LyricSearchResult> BuildOrderedLyrics<TSong>(IEnumerable<TSong> songs, Func<TSong, LyricSearchResult> loadLyric, int sourceOrder)
	{
		List<LyricSearchResult> lyrics = new List<LyricSearchResult>();
		int resultIndex = 0;
		foreach (TSong song in songs)
		{
			if (cancellationSource.IsCancellationRequested)
			{
				break;
			}

			LyricSearchResult lyric = loadLyric(song);
			if (lyric != null)
			{
				lyric.ResultOrder = resultIndex++;
				lyric.SourceOrder = sourceOrder;
				lyrics.Add(lyric);
			}
		}

		return lyrics;
	}

	protected List<CoverSearchResult> BuildDedupedCovers<TSong>(IEnumerable<TSong> songs, Func<TSong, CoverSearchResult> buildCover, List<CoverSearchResult> existingCovers)
	{
		List<CoverSearchResult> covers = new List<CoverSearchResult>();
		HashSet<string> addedCoverUrls = new HashSet<string>();
		foreach (TSong song in songs)
		{
			if (cancellationSource.IsCancellationRequested)
			{
				break;
			}

			CoverSearchResult cover = buildCover(song);
			string coverUrl = cover.CoverUrl;
			if (string.IsNullOrWhiteSpace(coverUrl) || addedCoverUrls.Contains(coverUrl))
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
				covers.Add(cover);
				addedCoverUrls.Add(coverUrl);
			}
		}

		return covers;
	}

	// 联网解析共用的 JToken 安全读取器(原各 provider 私有副本上提;详见 docs/SIMPLIFICATION_PLAN.md B3-2)。
	// 语义:取字段时跳过 JSON null,缺失/类型不符一律回退为 ""、null 或 0。
	protected static string GetStringOrEmpty(JToken token)
	{
		return token?.ToString() ?? "";
	}

	protected internal static JToken GetFirstField(JToken token, params string[] fieldNames)
	{
		if (token?.Type != JTokenType.Object)
		{
			return null;
		}

		foreach (string fieldName in fieldNames)
		{
			JToken field = token[fieldName];
			if (field != null && field.Type != JTokenType.Null)
			{
				return field;
			}
		}
		return null;
	}

	protected static string GetStringField(JToken token, string fieldName)
	{
		return GetStringOrEmpty(GetFirstField(token, fieldName));
	}

	protected static int? GetNullableIntField(JToken token, string fieldName)
	{
		JToken fieldValue = GetFirstField(token, fieldName);
		if (fieldValue == null)
		{
			return null;
		}

		int intValue;
		return int.TryParse(fieldValue.ToString(), out intValue) ? intValue : (int?)null;
	}

	protected static long GetLongField(JToken token, string fieldName)
	{
		return GetNullableLongField(token, fieldName) ?? 0L;
	}

	protected static long? GetNullableLongField(JToken token, string fieldName)
	{
		JToken fieldValue = GetFirstField(token, fieldName);
		if (fieldValue == null)
		{
			return null;
		}

		long longValue;
		return long.TryParse(fieldValue.ToString(), out longValue) ? longValue : (long?)null;
	}
}

internal sealed class ResponseSizeLimitExceededException : IOException
{
	public ResponseSizeLimitExceededException(long maximumBytes)
		: base("Remote response exceeded the maximum allowed size of " + maximumBytes + " bytes.")
	{
	}
}

// 传输层错误归类(详见 docs/SEARCH_STATUS_INDICATOR_DESIGN.md §5.4)。
// RateLimited / ParseFailed 由 provider 在解析阶段判定后回填,传输层只产出
// None / HttpStatus / Timeout / Network; provider-level parsing may add
// ParseFailed / RateLimited / CredentialsExpired.
internal enum RemoteErrorKind
{
	None,
	HttpStatus,
	Timeout,
	Network,
	ParseFailed,
	RateLimited,
	CredentialsExpired
}

// 统一的 GET/POST 请求结果,既保留响应体(Body/Bytes),又携带错误归类与错误码,
// 使上层能区分 "搜到 0 条" / "网络失败" / "HTTP 错误" / "解析失败" / "限流" / "登录态过期"。
internal sealed class HttpResult
{
	public string Body { get; set; }

	public byte[] Bytes { get; set; }

	// 非空表示 HTTP 层拿到了状态码(可能是非 2xx 的错误状态)。
	public int? HttpStatus { get; set; }

	public RemoteErrorKind Error { get; set; }

	// 业务码(provider 回填,如 QQ 2001)或 HTTP 状态码 / 异常归类的字符串表示。
	public string ErrorCode { get; set; }

	public bool IsSuccess => Error == RemoteErrorKind.None;

	public static HttpResult FromHttpStatus(int statusCode)
	{
		return new HttpResult
		{
			HttpStatus = statusCode,
			Error = RemoteErrorKind.HttpStatus,
			ErrorCode = statusCode.ToString()
		};
	}
}
