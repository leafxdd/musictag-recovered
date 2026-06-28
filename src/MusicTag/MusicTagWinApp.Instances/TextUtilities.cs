using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using MusicTagWinApp.Properties;

namespace MusicTagWinApp.Instances;

// 文本 / 哈希 / 编码 / 杂项工具。原 DatabaseMapper（误名神类）拆分而来（详见 docs/SIMPLIFICATION_PLAN.md Phase 2）。
internal static class TextUtilities
{
	public static string FormatFileSize(long byteCount)
	{
		if (byteCount >= 1000L)
		{
			if (byteCount < 1024000L)
			{
				return $"{(double)byteCount / 1024.0:N}KB";
			}
			if (byteCount < 1048576000L)
			{
				return $"{(double)byteCount / 1024.0 / 1024.0:N}MB";
			}
			return $"{(double)byteCount / 1024.0 / 1024.0 / 1024.0:N}GB";
		}
		return $"{byteCount}Byte";
	}

	public static string TrimNonEmptyLines(string text)
	{
		if (text != null)
		{
			string[] lines = text.Split(new char[1] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
			StringBuilder trimmedText = new StringBuilder();
			foreach (string line in lines)
			{
				if (trimmedText.Length > 0)
				{
					trimmedText.AppendLine();
				}
				trimmedText.Append(line.Trim());
			}
			return trimmedText.ToString();
		}
		return null;
	}

	public static string DecodeBase64String(string encodedText, string encodingName = "UniCode")
	{
		try
		{
			byte[] bytes = Convert.FromBase64String(encodedText);
			return Encoding.GetEncoding(encodingName).GetString(bytes);
		}
		catch
		{
			return "";
		}
	}

	public static byte[] ComputeMd5Hash(byte[] bytes)
	{
		return new MD5CryptoServiceProvider().ComputeHash(bytes);
	}

	public static string ComputeMd5HashString(byte[] bytes)
	{
		return BitConverter.ToString(ComputeMd5Hash(bytes));
	}

	public static string ComputeMd5HashString(string text, string encodingName = "UniCode")
	{
		return ComputeMd5HashString(Encoding.GetEncoding(encodingName).GetBytes(text));
	}

	public static string UrlEncodeUtf8(string text)
	{
		return HttpUtility.UrlEncode(text, Encoding.UTF8).Replace("+", "%20");
	}

	public static DateTime UnixMillisecondsToDateTime(long unixMilliseconds)
	{
		return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(unixMilliseconds);
	}

	public static int GetWebSearchResultLimit()
	{
		int webSearchItemsLimit = Settings.Default.WebSearchItemsLimit;
		if (webSearchItemsLimit <= 99 && webSearchItemsLimit > 0)
		{
			return webSearchItemsLimit;
		}
		return int.MaxValue;
	}

	public static string CoalesceNonBlank(string value, string fallback = "")
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			return value;
		}
		return fallback;
	}

	public static string GetMessageChain(this Exception exception)
	{
		StringBuilder exceptionMessages = new StringBuilder();
		if (exception is AggregateException aggregateException)
		{
			foreach (Exception innerException in aggregateException.InnerExceptions)
			{
				if (exceptionMessages.Length > 0)
				{
					exceptionMessages.Append(";");
				}
				exceptionMessages.Append(innerException.Message);
			}
		}
		if (exceptionMessages.Length == 0)
		{
			exceptionMessages.Append(exception.InnerException?.Message ?? exception.Message);
		}
		return exceptionMessages.ToString();
	}

	public static string GetStringRespectingUtf16Bom(this Encoding encoding, byte[] bytes)
	{
		if (encoding.HeaderName.Equals("UTF-16", StringComparison.OrdinalIgnoreCase) && bytes.Length >= 2)
		{
			if (bytes[0] == byte.MaxValue && bytes[1] == 254)
			{
				return Encoding.GetEncoding("UTF-16LE").GetString(bytes.Skip(2).ToArray());
			}
			if (bytes[0] == 254 && bytes[1] == byte.MaxValue)
			{
				return Encoding.GetEncoding("UTF-16BE").GetString(bytes.Skip(2).ToArray());
			}
		}
		return encoding.GetString(bytes);
	}

	public static bool ContainsChinese(string text)
	{
		return Regex.Match(text, "[\\u4e00-\\u9fa5]").Success;
	}
}
