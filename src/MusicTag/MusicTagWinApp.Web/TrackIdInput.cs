using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MusicTagWinApp.Web;

internal static class TrackIdInput
{
	private static readonly Regex KugouHashRegex = new Regex("^[A-Fa-f0-9]{32}$", RegexOptions.Compiled);

	public static bool TryNormalize(SearchSource source, string input, out string normalizedId)
	{
		switch (source)
		{
		case SearchSource.QQ:
			normalizedId = ExtractLastPathOrQueryValue(input, "songmid", "songid", "id");
			if (long.TryParse(normalizedId, out long qqSongId))
			{
				if (qqSongId > 0L)
				{
					normalizedId = qqSongId.ToString(CultureInfo.InvariantCulture);
					return true;
				}
				return false;
			}
			return Regex.IsMatch(normalizedId, "^[A-Za-z0-9]{10,32}$");
		case SearchSource.Kugou:
			normalizedId = ExtractLastPathOrQueryValue(input, "hash", "album_audio_id", "mixsongid", "id").ToUpperInvariant();
			if (long.TryParse(normalizedId, out long kugouId) && kugouId > 0L)
			{
				normalizedId = kugouId.ToString(CultureInfo.InvariantCulture);
				return true;
			}
			return KugouHashRegex.IsMatch(normalizedId);
		case SearchSource.Kuwo:
			normalizedId = ExtractLastPathOrQueryValue(input, "id", "musicId", "rid");
			if (long.TryParse(normalizedId, out long kuwoId) && kuwoId > 0L)
			{
				normalizedId = kuwoId.ToString(CultureInfo.InvariantCulture);
				return true;
			}
			return false;
		default:
			normalizedId = ExtractLastPathOrQueryValue(input, "id", "songid");
			if (long.TryParse(normalizedId, out long netEaseId) && netEaseId > 0L)
			{
				normalizedId = netEaseId.ToString(CultureInfo.InvariantCulture);
				return true;
			}
			return false;
		}
	}

	public static string ExtractLastPathOrQueryValue(string input, params string[] queryNames)
	{
		string value = (input ?? "").Trim();
		if (value.Length == 0)
		{
			return "";
		}
		if (Uri.TryCreate(value, UriKind.Absolute, out Uri uri))
		{
			foreach (string rawQuery in new[] { uri.Query.TrimStart('?'), uri.Fragment.TrimStart('#', '?') })
			{
				string query = rawQuery;
				int queryStart = query.IndexOf('?');
				if (queryStart >= 0)
				{
					query = query.Substring(queryStart + 1);
				}
				foreach (string pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
				{
					string[] parts = pair.Split('=', 2);
					foreach (string queryName in queryNames)
					{
						if (parts.Length == 2 && string.Equals(Uri.UnescapeDataString(parts[0]), queryName, StringComparison.OrdinalIgnoreCase))
						{
							return Uri.UnescapeDataString(parts[1]).Trim();
						}
					}
				}
			}
			string pathValue = uri.AbsolutePath.TrimEnd('/');
			int slashIndex = pathValue.LastIndexOf('/');
			string lastPathValue = Uri.UnescapeDataString(slashIndex >= 0 ? pathValue.Substring(slashIndex + 1) : pathValue).Trim();
			return lastPathValue.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ? lastPathValue.Substring(0, lastPathValue.Length - 5) : lastPathValue;
		}
		return value;
	}
}
