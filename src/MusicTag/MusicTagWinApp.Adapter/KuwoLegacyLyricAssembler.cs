using System.Collections.Generic;
using System.Linq;
using System.Text;
using MusicTag.Composer;
using MusicTagWinApp.Instances;

namespace MusicTagWinApp.Adapter;

internal static class KuwoLegacyLyricAssembler
{
	internal static (string Lyric, string TranslatedLyric) Build(IReadOnlyList<(long TimestampMs, string Text)> sourceLines, bool useThreeDigitMilliseconds)
	{
		StringBuilder lyricBuilder = new StringBuilder();
		StringBuilder translatedLyricBuilder = new StringBuilder();
		SortedDictionary<long, (string PrimaryText, List<string> AlternateText)> timedLines = new SortedDictionary<long, (string, List<string>)>();

		foreach ((long TimestampMs, string Text) sourceLine in sourceLines)
		{
			if (timedLines.TryGetValue(sourceLine.TimestampMs, out (string PrimaryText, List<string> AlternateText) line))
			{
				if (timedLines.Count == 1)
				{
					timedLines[sourceLine.TimestampMs] = (line.PrimaryText + " " + sourceLine.Text, new List<string>());
				}
				else
				{
					line.AlternateText.Add(sourceLine.Text);
				}
			}
			else
			{
				timedLines.Add(sourceLine.TimestampMs, (sourceLine.Text, new List<string>()));
			}
		}

		if (timedLines.Any())
		{
			KeyValuePair<long, (string PrimaryText, List<string> AlternateText)> lastLine = timedLines.Last();
			List<string> alternateText = lastLine.Value.AlternateText;
			if (alternateText.Count > 1)
			{
				timedLines.Add(lastLine.Key + 5000L, (string.Join(" ", alternateText.Skip(1)), new List<string>()));
				alternateText.RemoveRange(1, alternateText.Count - 1);
			}
		}

		List<(long TimestampMs, string PrimaryText, string AlternateText)> normalizedLines = new List<(long, string, string)>();
		foreach (KeyValuePair<long, (string PrimaryText, List<string> AlternateText)> timedLine in timedLines)
		{
			string alternateText = timedLine.Value.AlternateText.Any() ? string.Join(" ", timedLine.Value.AlternateText) : null;
			normalizedLines.Add((timedLine.Key, timedLine.Value.PrimaryText, alternateText));
		}

		for (int index = 0; index < normalizedLines.Count; index++)
		{
			(long TimestampMs, string PrimaryText, string AlternateText) line = normalizedLines[index];
			if (line.AlternateText != null && TextUtilities.ContainsChinese(line.AlternateText) && !TextUtilities.ContainsChinese(line.PrimaryText))
			{
				normalizedLines[index] = (line.TimestampMs, line.AlternateText, line.PrimaryText);
			}
		}

		bool foundTranslatedLine = false;
		for (int index = 1; index < normalizedLines.Count - 1; index++)
		{
			(long TimestampMs, string PrimaryText, string AlternateText) line = normalizedLines[index];
			if (!foundTranslatedLine && line.AlternateText != null)
			{
				foundTranslatedLine = true;
			}

			if (foundTranslatedLine && line.AlternateText == null)
			{
				(long TimestampMs, string PrimaryText, string AlternateText) nextLine = normalizedLines[index + 1];
				if (nextLine.AlternateText == null)
				{
					normalizedLines[index] = (nextLine.TimestampMs, line.PrimaryText, nextLine.PrimaryText);
					normalizedLines.RemoveAt(index + 1);
				}
			}
		}

		if (normalizedLines.Count >= 3)
		{
			(long TimestampMs, string PrimaryText, string AlternateText) thirdFromLast = normalizedLines[normalizedLines.Count - 3];
			(long TimestampMs, string PrimaryText, string AlternateText) secondFromLast = normalizedLines[normalizedLines.Count - 2];
			(long TimestampMs, string PrimaryText, string AlternateText) lastLine = normalizedLines[normalizedLines.Count - 1];
			if (lastLine.AlternateText != null && thirdFromLast.AlternateText != null && secondFromLast.AlternateText == null)
			{
				if (TextUtilities.ContainsChinese(secondFromLast.PrimaryText) && TextUtilities.ContainsChinese(lastLine.PrimaryText) && !TextUtilities.ContainsChinese(lastLine.AlternateText))
				{
					lastLine = (lastLine.TimestampMs, lastLine.AlternateText, lastLine.PrimaryText);
				}

				normalizedLines[normalizedLines.Count - 2] = (lastLine.TimestampMs, secondFromLast.PrimaryText, lastLine.PrimaryText);
				normalizedLines[normalizedLines.Count - 1] = (lastLine.TimestampMs + 5000L, lastLine.AlternateText, null);
			}
		}

		for (int index = 0; index < normalizedLines.Count; index++)
		{
			(long TimestampMs, string PrimaryText, string AlternateText) line = normalizedLines[index];
			bool appendedPrimaryLyric = false;
			if (line.AlternateText != null)
			{
				lyricBuilder.Append(LyricTextProcessor.FormatTimestamp(line.TimestampMs, useThreeDigitMilliseconds));
				lyricBuilder.Append(line.AlternateText);
				lyricBuilder.Append("\n");
			}
			else if (index < normalizedLines.Count - 1 || translatedLyricBuilder.Length == 0)
			{
				lyricBuilder.Append(LyricTextProcessor.FormatTimestamp(line.TimestampMs, useThreeDigitMilliseconds));
				lyricBuilder.Append(line.PrimaryText);
				lyricBuilder.Append("\n");
				appendedPrimaryLyric = true;
			}

			if (!appendedPrimaryLyric)
			{
				long translatedTimestampMs = index > 0 ? normalizedLines[index - 1].TimestampMs : line.TimestampMs;
				translatedLyricBuilder.Append(LyricTextProcessor.FormatTimestamp(translatedTimestampMs, useThreeDigitMilliseconds));
				translatedLyricBuilder.Append(line.PrimaryText);
				translatedLyricBuilder.Append("\n");
			}
		}

		return (lyricBuilder.ToString(), translatedLyricBuilder.ToString());
	}
}
