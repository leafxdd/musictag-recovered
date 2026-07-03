using System.Web;

namespace MusicTag.Services;

internal static class TextEncodingService
{
	public static string DecodeBasicHtmlEntities(string value)
	{
		// .NET Framework 的 HtmlDecode 基于 HTML4 实体表,不解码 &apos;(属 XML/HTML5),
		// 先手动还原以避免相对旧实现丢失该实体。
		return HttpUtility.HtmlDecode((value ?? string.Empty).Replace("&apos;", "'"));
	}

	public static string JavaScriptStringEncode(string value)
	{
		return HttpUtility.JavaScriptStringEncode(value);
	}
}
