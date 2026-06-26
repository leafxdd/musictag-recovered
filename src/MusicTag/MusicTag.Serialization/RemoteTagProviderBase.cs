using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
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

	protected virtual string PostString(string url, string body, HttpClient client = null, bool postJson = false)
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
					return response.Content.ReadAsStringAsync().Result;
				}
			}
		}
		catch (Exception exception)
		{
			Console.WriteLine("PostHttp error:" + exception.GetMessageChain());
		}
		return null;
	}

	protected virtual byte[] GetResponseBytes(string url)
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
				return memoryStream.ToArray();
			}
		}
		catch (Exception exception)
		{
			Console.WriteLine("GetHttp error:" + exception.GetMessageChain());
		}
		return null;
	}

	protected virtual string GetResponseString(string url)
	{
		byte[] responseBytes = GetResponseBytes(url);
		if (responseBytes == null)
		{
			return "";
		}
		return Encoding.UTF8.GetString(responseBytes);
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
