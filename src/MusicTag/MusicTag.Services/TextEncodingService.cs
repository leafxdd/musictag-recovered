using System.Web;

namespace MusicTag.Services;

internal static class TextEncodingService
{
	public static string DecodeBasicHtmlEntities(string value)
	{
		return value.Replace("&lt;", "<")
			.Replace("&gt;", ">")
			.Replace("&quot;", "\"")
			.Replace("&apos;", "'")
			.Replace("&amp;", "&");
	}

	public static string JavaScriptStringEncode(string value)
	{
		return HttpUtility.JavaScriptStringEncode(value);
	}
}
