using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MusicTagWinApp.Instances;
using MusicTagWinApp.Properties;
using MusicTagWinApp.Web;
using MusicTagWinApp.Writers;

namespace MusicTag.Serialization;

internal abstract class RemoteTagProviderBase : IDisposable
{
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

	protected void ReportStatus(SourceSearchPhase phase, string errorCode = null, int retryAttempt = 0, int retryTotal = 0, int retrySecondsLeft = 0)
	{
		StatusReporter?.Invoke(new SourceSearchStatus
		{
			Source = GetSource(),
			Phase = phase,
			ErrorCode = errorCode,
			RetryAttempt = retryAttempt,
			RetryTotal = retryTotal,
			RetrySecondsLeft = retrySecondsLeft
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
		if (cancellationSource.IsCancellationRequested)
		{
			return new HttpResult { Error = RemoteErrorKind.None };
		}
		Exception rootException = (exception is AggregateException aggregate) ? aggregate.GetBaseException() : exception;
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
			using (HttpResponseMessage response = client.PostAsync(url, requestContent, cancellationSource.Token).Result)
			{
				if (response.StatusCode == HttpStatusCode.OK)
				{
					return RecordResult(new HttpResult { Body = response.Content.ReadAsStringAsync().Result });
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
			using HttpResponseMessage response = GetHttpClient().GetAsync(url, cancellationSource.Token).Result;
			if (response.IsSuccessStatusCode)
			{
				using MemoryStream memoryStream = new MemoryStream();
				using Stream responseStream = response.Content.ReadAsStreamAsync().Result;
				byte[] buffer = new byte[8192];
				while (!cancellationSource.IsCancellationRequested)
				{
					int bytesRead = responseStream.Read(buffer, 0, buffer.Length);
					if (bytesRead <= 0)
					{
						break;
					}
					memoryStream.Write(buffer, 0, bytesRead);
				}
				return RecordResult(new HttpResult { Bytes = memoryStream.ToArray() });
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

	protected virtual DownloadStatus DownloadToStream(string url, Stream destination, int readWriteTimeout = 30000, int requestTimeout = 300000)
	{
		try
		{
			int bytesReadTotal = 0;
			using HttpClient downloadClient = new HttpClient(new WebRequestHandler
			{
				AutomaticDecompression = (DecompressionMethods.GZip | DecompressionMethods.Deflate),
				ReadWriteTimeout = readWriteTimeout
			});
			downloadClient.Timeout = TimeSpan.FromMilliseconds(requestTimeout);
			downloadClient.DefaultRequestHeaders.Add("user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
			ApplyCustomUserAgent(downloadClient);
			if (!(this is QqMusicTagProvider))
			{
				downloadClient.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3");
			}
			using HttpResponseMessage response = downloadClient.GetAsync(url, cancellationSource.Token).Result;
			if (response.IsSuccessStatusCode)
			{
				using (Stream responseStream = response.Content.ReadAsStreamAsync().Result)
				{
					byte[] buffer = new byte[4096];
					while (!cancellationSource.IsCancellationRequested)
					{
						int bytesRead = responseStream.Read(buffer, 0, buffer.Length);
						if (bytesRead <= 0)
						{
							break;
						}
						destination.Write(buffer, 0, bytesRead);
						bytesReadTotal += bytesRead;
					}
				}
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

	protected Func<CancellationTokenSource, string, int, (DownloadStatus, long)> CreateCoverDownloader<T>(string url) where T : RemoteTagProviderBase, new()
	{
		return (cancellation, filePath, timeout) =>
		{
			using T downloader = new T();
			downloader.SetCancellationSource(cancellation);
			using FileStream fileStream = new FileStream(filePath, FileMode.Create);
			return (downloader.DownloadToStream(url, fileStream, 30000, timeout), fileStream.Length);
		};
	}
}

// 传输层错误归类(详见 docs/SEARCH_STATUS_INDICATOR_DESIGN.md §5.4)。
// RateLimited / ParseFailed 由 provider 在解析阶段判定后回填,传输层只产出
// None / HttpStatus / Timeout / Network。
internal enum RemoteErrorKind
{
	None,
	HttpStatus,
	Timeout,
	Network,
	ParseFailed,
	RateLimited
}

// 统一的 GET/POST 请求结果,既保留响应体(Body/Bytes),又携带错误归类与错误码,
// 使上层能区分 "搜到 0 条" / "网络失败" / "HTTP 错误" / "解析失败" / "限流"。
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
