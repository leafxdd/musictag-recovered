using System.Security;
using System.Web;

namespace MusicTag.Services;

internal static class TextEncodingService
{
	public static string EscapeXml(string value)
	{
		return SecurityElement.Escape(value);
	}

	public static string DecodeBasicHtmlEntities(string value)
	{
		return value.Replace("&lt;", "<")
			.Replace("&gt;", ">")
			.Replace("&quot;", "\"")
			.Replace("&apos;", "'")
			.Replace("&amp;", "&");
	}

	public static string HtmlEncode(string value)
	{
		return HttpUtility.HtmlEncode(value);
	}

	public static string HtmlDecode(string value)
	{
		return HttpUtility.HtmlDecode(value);
	}

	public static string JavaScriptStringEncode(string value)
	{
		return HttpUtility.JavaScriptStringEncode(value);
	}
}
