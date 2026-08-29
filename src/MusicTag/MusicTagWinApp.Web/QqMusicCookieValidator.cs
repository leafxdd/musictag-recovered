using System;
using System.Collections.Generic;

namespace MusicTagWinApp.Web;

internal sealed class QqMusicCookieValidationResult
{
	public bool IsValid { get; set; }

	public bool IsEmpty { get; set; }

	public List<string> MissingFields { get; } = new List<string>();
}

internal static class QqMusicCookieValidator
{
	private static readonly string[] UserIdCookieNames = new string[5] { "loginUin", "uin", "p_uin", "euin", "p_euin" };

	private static readonly string[] AuthCookieNames = new string[4] { "authst", "qm_keyst", "qqmusic_key", "qqmusic_key_new" };

	internal static QqMusicCookieValidationResult Validate(string cookieHeader)
	{
		bool isEmpty = string.IsNullOrWhiteSpace(cookieHeader);
		Dictionary<string, string> cookies = ParseCookieHeader(cookieHeader);
		QqMusicCookieValidationResult result = new QqMusicCookieValidationResult
		{
			IsEmpty = isEmpty,
			IsValid = isEmpty
		};
		if (isEmpty)
		{
			return result;
		}

		if (GetCookieValue(cookies, UserIdCookieNames).Length == 0)
		{
			result.MissingFields.Add("uin/p_uin/euin");
		}
		if (GetCookieValue(cookies, AuthCookieNames).Length == 0)
		{
			result.MissingFields.Add("authst/qm_keyst/qqmusic_key");
		}
		result.IsValid = result.MissingFields.Count == 0;
		return result;
	}

	internal static Dictionary<string, string> ParseCookieHeader(string cookieHeader)
	{
		Dictionary<string, string> cookies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (string.IsNullOrWhiteSpace(cookieHeader))
		{
			return cookies;
		}
		cookieHeader = cookieHeader.Trim();
		if (cookieHeader.StartsWith("Cookie:", StringComparison.OrdinalIgnoreCase))
		{
			cookieHeader = cookieHeader.Substring("Cookie:".Length).Trim();
		}

		foreach (string segment in cookieHeader.Split(';'))
		{
			int separator = segment.IndexOf('=');
			if (separator <= 0)
			{
				continue;
			}
			string name = segment.Substring(0, separator).Trim();
			string value = segment.Substring(separator + 1).Trim();
			if (name.Length > 0 && value.Length > 0)
			{
				cookies[name] = value.Trim('"');
			}
		}
		return cookies;
	}

	internal static string GetCookieValue(Dictionary<string, string> cookies, params string[] names)
	{
		foreach (string name in names)
		{
			if (cookies.TryGetValue(name, out string value) && !string.IsNullOrWhiteSpace(value))
			{
				return value.Trim();
			}
		}
		return "";
	}
}
