using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MusicTag.Composer;
using Newtonsoft.Json.Linq;

namespace MusicTagWinApp.Exporters;

/// <summary>
/// Converts NetEase's YRC payload into ordinary timestamped LRC lines.
/// </summary>
internal static class NetEaseYrcDecoder
{
	private static readonly Regex YrcLineRegex = new Regex(@"^\[\s*(\d+)\s*,\s*(\d+)\s*\](.*)$", RegexOptions.Compiled);

	private static readonly Regex WordTimestampRegex = new Regex(@"\(\s*\d+\s*,\s*\d+\s*,\s*\d+\s*\)", RegexOptions.Compiled);

	private static readonly Regex MetadataTagRegex = new Regex(@"^\[[^,\]]+:[^\]]*\]", RegexOptions.Compiled);

	internal static string ConvertToLineLyric(string yrcText, bool useThreeDigitMilliseconds)
	{
		if (string.IsNullOrWhiteSpace(yrcText))
		{
			return "";
		}

		StringBuilder lyricBuilder = new StringBuilder();
		bool hasTimedLine = false;
		string[] lines = yrcText.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
		foreach (string sourceLine in lines)
		{
			string line = sourceLine.Trim();
			if (line.Length == 0)
			{
				continue;
			}

			if (TryParseJsonMetadataLine(line, out long metadataTimestamp, out string metadataText))
			{
				hasTimedLine |= AppendLine(lyricBuilder, metadataTimestamp, metadataText, useThreeDigitMilliseconds);
				continue;
			}

			Match lineMatch = YrcLineRegex.Match(line);
			if (lineMatch.Success && long.TryParse(lineMatch.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out long timestampMilliseconds))
			{
				string lyricContent = WordTimestampRegex.Replace(lineMatch.Groups[3].Value, "").Trim();
				hasTimedLine |= AppendLine(lyricBuilder, timestampMilliseconds, lyricContent, useThreeDigitMilliseconds);
				continue;
			}

			// Keep standard LRC metadata only when a real YRC timeline is present.
			// A metadata-only/malformed yrc field must still fall back to lrc.
			if (MetadataTagRegex.IsMatch(line))
			{
				lyricBuilder.Append(line);
				lyricBuilder.Append('\n');
			}
		}

		return hasTimedLine ? lyricBuilder.ToString().Trim() : "";
	}

	private static bool AppendLine(StringBuilder lyricBuilder, long timestampMilliseconds, string lyricContent, bool useThreeDigitMilliseconds)
	{
		if (timestampMilliseconds < 0L)
		{
			return false;
		}

		lyricBuilder.Append(LyricTextProcessor.FormatTimestamp(timestampMilliseconds, useThreeDigitMilliseconds));
		lyricBuilder.Append(lyricContent ?? "");
		lyricBuilder.Append('\n');
		return true;
	}

	private static bool TryParseJsonMetadataLine(string line, out long timestampMilliseconds, out string lyricContent)
	{
		timestampMilliseconds = 0L;
		lyricContent = "";
		if (!line.StartsWith("{", StringComparison.Ordinal))
		{
			return false;
		}

		try
		{
			JObject metadata = JObject.Parse(line);
			if (!long.TryParse(metadata["t"]?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out timestampMilliseconds) || timestampMilliseconds < 0L)
			{
				return false;
			}

			if (!(metadata["c"] is JArray contentItems))
			{
				return false;
			}

			StringBuilder contentBuilder = new StringBuilder();
			foreach (JToken contentItem in contentItems)
			{
				string text = contentItem["tx"]?.ToString();
				if (text != null)
				{
					contentBuilder.Append(text);
				}
			}

			lyricContent = contentBuilder.ToString().Trim();
			return lyricContent.Length > 0;
		}
		catch (Exception)
		{
			return false;
		}
	}
}
