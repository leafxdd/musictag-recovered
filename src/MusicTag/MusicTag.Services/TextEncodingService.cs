using System.Web;

namespace MusicTag.Services;

internal static class TextEncodingService
{
	public static string DecodeBasicHtmlEntities(string value)
	{
		return HttpUtility.HtmlDecode(value ?? string.Empty);
	}

	public static string JavaScriptStringEncode(string value)
	{
		return HttpUtility.JavaScriptStringEncode(value);
	}
}
