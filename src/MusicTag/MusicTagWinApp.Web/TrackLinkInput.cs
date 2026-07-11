using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MusicTag.Readers;

namespace MusicTagWinApp.Web;

internal static class TrackLinkClassifier
{
	internal static bool TryDetect(string input, out SearchSource source, out Uri uri)
	{
		source = default;
		uri = null;
		if (!Uri.TryCreate(input?.Trim(), UriKind.Absolute, out Uri parsed) || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
		{
			return false;
		}
		uri = parsed;
		string host = parsed.IdnHost.ToLowerInvariant();
		if (IsHost(host, "music.163.com") || IsHost(host, "163cn.tv")) source = SearchSource.Music163;
		else if (IsHost(host, "y.qq.com")) source = SearchSource.QQ;
		else if (IsHost(host, "kugou.com")) source = SearchSource.Kugou;
		else if (IsHost(host, "kuwo.cn")) source = SearchSource.Kuwo;
		else return false;
		return true;
	}

	internal static bool IsQqShortLink(Uri uri) => uri != null && IsHost(uri.IdnHost, "y.qq.com") && uri.AbsolutePath.Equals("/base/fcgi-bin/u", StringComparison.OrdinalIgnoreCase);

	internal static bool IsQqHost(string host) => IsHost(host, "y.qq.com");

	private static bool IsHost(string host, string suffix) => host.Equals(suffix, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase);
}

internal sealed class QqShortLinkResolver : IDisposable
{
	private const int MaxRedirects = 5;
	private readonly HttpClient httpClient;

	internal QqShortLinkResolver(HttpClient httpClient)
	{
		this.httpClient = httpClient;
	}

	internal static QqShortLinkResolver CreateDefault()
	{
		return new QqShortLinkResolver(new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(10) });
	}

	internal async Task<Uri> ResolveAsync(Uri shortLink, CancellationToken cancellationToken)
	{
		if (!TrackLinkClassifier.IsQqShortLink(shortLink)) return null;
		Uri current = shortLink;
		for (int redirect = 0; redirect < MaxRedirects; redirect++)
		{
			using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, current);
			using HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
			if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location != null)
			{
				Uri next = response.Headers.Location.IsAbsoluteUri ? response.Headers.Location : new Uri(current, response.Headers.Location);
				if (!TrackLinkClassifier.IsQqHost(next.IdnHost)) return null;
				current = next;
				continue;
			}
			return response.StatusCode == HttpStatusCode.OK && TrackLinkClassifier.IsQqHost(current.IdnHost) ? current : null;
		}
		return null;
	}

	public void Dispose() => httpClient.Dispose();
}
