using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MusicTag.Readers;
using MusicTagWinApp.Web;

namespace MusicTag.Tests;

internal static class TrackLinkInputCharacterization
{
	private sealed class RedirectHandler : HttpMessageHandler
	{
		private readonly Queue<(HttpStatusCode Status, string Location)> responses;

		internal RedirectHandler(params (HttpStatusCode, string)[] responses) => this.responses = new Queue<(HttpStatusCode, string)>(responses);

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			(HttpStatusCode status, string location) = responses.Dequeue();
			HttpResponseMessage response = new HttpResponseMessage(status);
			if (location != null) response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
			return Task.FromResult(response);
		}
	}

	public static IEnumerable<(string, Action)> All()
	{
		yield return ("Track links detect provider by strict host", delegate
		{
			Check.True(TrackLinkClassifier.TryDetect("https://music.163.com/song?id=1", out SearchSource netEase, out _), "NetEase detected");
			Check.Equal(SearchSource.Music163, netEase, "NetEase source");
			Check.True(TrackLinkClassifier.TryDetect("https://c6.y.qq.com/base/fcgi-bin/u?__=x", out SearchSource qq, out Uri qqUri), "QQ detected");
			Check.Equal(SearchSource.QQ, qq, "QQ source");
			Check.True(TrackLinkClassifier.IsQqShortLink(qqUri), "QQ short link");
			Check.True(!TrackLinkClassifier.TryDetect("https://y.qq.com.evil.example/song/1", out _, out _), "suffix spoof rejected");
		});

		yield return ("QQ short link resolver follows QQ redirects", delegate
		{
			using HttpClient client = new HttpClient(new RedirectHandler((HttpStatusCode.Found, "https://y.qq.com/n/ryqq/songDetail/004123456789"), (HttpStatusCode.OK, null)));
			using QqShortLinkResolver resolver = new QqShortLinkResolver(client);
			Uri resolved = resolver.ResolveAsync(new Uri("https://c6.y.qq.com/base/fcgi-bin/u?__=sample"), CancellationToken.None).GetAwaiter().GetResult();
			Check.Equal("y.qq.com", resolved.Host, "resolved host");
		});

		yield return ("QQ short link resolver rejects off-site redirect", delegate
		{
			using HttpClient client = new HttpClient(new RedirectHandler((HttpStatusCode.Found, "https://evil.example/song/1")));
			using QqShortLinkResolver resolver = new QqShortLinkResolver(client);
			Check.True(resolver.ResolveAsync(new Uri("https://c6.y.qq.com/base/fcgi-bin/u?__=sample"), CancellationToken.None).GetAwaiter().GetResult() == null, "off-site rejected");
		});
	}
}
