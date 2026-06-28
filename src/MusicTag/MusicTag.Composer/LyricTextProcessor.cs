using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MusicTag.Importers;
using MusicTag.Readers;
using MusicTagWinApp.Instances;
using MusicTagWinApp.Properties;

namespace MusicTag.Composer;

	internal class LyricTextProcessor
	{
	private class LyricLine
	{
		public string OriginalText;

		public string TranslatedText;

		public LyricLine(string originalText)
		{
			OriginalText = originalText ?? "";
		}
	}

		private static readonly Regex TimestampRegex = new Regex("\\[(\\d{1,2}:\\d{1,2}\\.\\d{1,3})\\]|\\[(\\d{1,2}:\\d{1,2})\\]|\\[(\\d{1,2}:\\d{1,2}:\\d{1,3})\\]");

		private static readonly Regex TimestampSeparatorRegex = new Regex(":|\\.");

	private string lyricist;
	
	private string composer;
	
	private string arranger;
	
	private string publisher;
	
	private string author;
	
	private string editor;
	
	private string version;
	
	private string totalDuration;
	
	private long timestampOffsetMilliseconds;
	
	private readonly SortedDictionary<long, LyricLine> linesByTimestamp;

	public string Lyric { get; private set; }

	public string Title { get; private set; }

	public string Artist { get; private set; }
	
	public string Album { get; private set; }

	private string Creator { get; set; }
	
	public LyricTextProcessor(string lyricText, bool allowDuplicateTimestamps = false)
	{
		Artist = "";
		Album = "";
		Creator = "";
		lyricist = "";
		composer = "";
		arranger = "";
		publisher = "";
		author = "";
		editor = "";
		version = "";
		totalDuration = "";
		linesByTimestamp = new SortedDictionary<long, LyricLine>();
		Lyric = lyricText;
		try
		{
			string[] lines = lyricText.Split(new char[1] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
			for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
			{
				ParseLine(lines[lineIndex].Trim(), allowDuplicateTimestamps);
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine("lrcparser init error:" + ex.Message);
		}
	}

	private void ParseLine(string rawLine, bool allowDuplicateTimestamps)
	{
		if (rawLine.StartsWith("[ti:") && rawLine.Length > 5)
		{
			Title = rawLine.Substring(4, rawLine.Length - 5).Trim();
			return;
		}
		if (rawLine.StartsWith("[ar:") && rawLine.Length > 5)
		{
			Artist = rawLine.Substring(4, rawLine.Length - 5).Trim();
			return;
		}
		if (rawLine.StartsWith("[al:") && rawLine.Length > 5)
		{
			Album = rawLine.Substring(4, rawLine.Length - 5).Trim();
			return;
		}
		if (rawLine.StartsWith("[by:") && rawLine.Length > 5)
		{
			Creator = rawLine.Substring(4, rawLine.Length - 5).Trim();
			return;
		}
		if (rawLine.StartsWith("[ly:") && rawLine.Length > 5)
		{
			lyricist = rawLine.Substring(4, rawLine.Length - 5).Trim();
			return;
		}
		if (rawLine.StartsWith("[mu:") && rawLine.Length > 5)
		{
			composer = rawLine.Substring(4, rawLine.Length - 5).Trim();
			return;
		}
		if (rawLine.StartsWith("[ma:") && rawLine.Length > 5)
		{
			arranger = rawLine.Substring(4, rawLine.Length - 5).Trim();
			return;
		}
		if (rawLine.StartsWith("[pu:") && rawLine.Length > 5)
		{
			publisher = rawLine.Substring(4, rawLine.Length - 5).Trim();
			return;
		}
		if (rawLine.StartsWith("[au:") && rawLine.Length > 5)
		{
			author = rawLine.Substring(4, rawLine.Length - 5).Trim();
			return;
		}
		if (rawLine.StartsWith("[re:") && rawLine.Length > 5)
		{
			editor = rawLine.Substring(4, rawLine.Length - 5).Trim();
			return;
		}
		if (rawLine.StartsWith("[ve:") && rawLine.Length > 5)
		{
			version = rawLine.Substring(4, rawLine.Length - 5).Trim();
			return;
		}
		if (rawLine.StartsWith("[total:") && rawLine.Length > 8)
		{
			totalDuration = rawLine.Substring(7, rawLine.Length - 8).Trim();
			return;
		}
		if (rawLine.StartsWith("[offset:") && rawLine.Length > 9)
		{
			try
			{
				timestampOffsetMilliseconds = long.Parse(rawLine.Substring(8, rawLine.Length - 9).Trim(), CultureInfo.InvariantCulture);
			}
			catch (Exception ex)
			{
				Console.WriteLine("parse lyricoffset error:" + ex.Message);
			}
			return;
		}
		string[] lyricParts = TimestampRegex.Split(rawLine);
		string lyricText = "";
		if (lyricParts != null && lyricParts.Length != 0)
		{
			lyricText = lyricParts[lyricParts.Length - 1].Trim();
		}
		if (lyricText == "//")
		{
			lyricText = "";
		}
		foreach (Match timestampMatch in TimestampRegex.Matches(rawLine))
		{
			LyricLine existingLine = null;
			for (int groupIndex = 1; groupIndex < timestampMatch.Groups.Count; groupIndex++)
			{
				string timestamp = timestampMatch.Groups[groupIndex].Value;
				if (string.IsNullOrEmpty(timestamp))
				{
					continue;
				}
				long lyricTime = Math.Max(0L, ParseTimestampMilliseconds(timestamp) + timestampOffsetMilliseconds);
				if (linesByTimestamp.TryGetValue(lyricTime, out existingLine))
				{
					if (allowDuplicateTimestamps)
					{
						while (linesByTimestamp.ContainsKey(++lyricTime))
						{
						}
						linesByTimestamp.Add(lyricTime, new LyricLine(lyricText));
					}
					else if (!string.IsNullOrWhiteSpace(lyricText))
					{
						if (existingLine.TranslatedText != null)
						{
							existingLine.OriginalText = existingLine.OriginalText + " " + existingLine.TranslatedText;
						}
						existingLine.TranslatedText = lyricText;
					}
				}
				else
				{
					linesByTimestamp.Add(lyricTime, new LyricLine(lyricText));
				}
			}
		}
	}

	private static long ParseTimestampMilliseconds(string timestampText)
	{
		long minutes = 0L;
		long seconds = 0L;
		long milliseconds = 0L;
		try
		{
				string[] timestampParts = TimestampSeparatorRegex.Split(timestampText);
			for (int index = 0; index < timestampParts.Length; index++)
			{
				string timestampPart = timestampParts[index];
				switch (index)
				{
				case 0:
					minutes = long.Parse(timestampPart, CultureInfo.InvariantCulture);
					break;
				case 1:
					seconds = long.Parse(timestampPart, CultureInfo.InvariantCulture);
					break;
				case 2:
					milliseconds = ParseMillisecondPart(timestampPart);
					break;
				}
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine("stringtotime error:" + ex.Message);
		}
		return (minutes * 60L + seconds) * 1000L + milliseconds;
	}

	private static long ParseMillisecondPart(string timestampPart)
	{
		long milliseconds = long.Parse(timestampPart, CultureInfo.InvariantCulture);
		for (int digitCount = timestampPart.Length; digitCount < 3; digitCount++)
		{
			milliseconds *= 10L;
		}
		return milliseconds;
	}

	public static string FormatTimestamp(long timestampMilliseconds, bool useThreeDigitMilliseconds)
	{
		long milliseconds = timestampMilliseconds % 1000L;
		long seconds = timestampMilliseconds / 1000L % 60L;
		long minutes = timestampMilliseconds / 1000L / 60L;
		if (useThreeDigitMilliseconds)
		{
			return string.Format(CultureInfo.InvariantCulture, "[{0:D2}:{1:D2}.{2:D3}]", minutes, seconds, milliseconds);
		}
		return string.Format(CultureInfo.InvariantCulture, "[{0:D2}:{1:D2}.{2:D2}]", minutes, seconds, milliseconds / 10L);
	}

	private string MergeMetadataValue(string currentValue, string incomingValue)
	{
		if (currentValue == incomingValue)
		{
			return currentValue;
		}
		if (!string.IsNullOrWhiteSpace(currentValue) && !string.IsNullOrWhiteSpace(incomingValue))
		{
			return currentValue + "/" + incomingValue;
		}
		if (!string.IsNullOrWhiteSpace(currentValue))
		{
			return currentValue;
		}
		if (!string.IsNullOrWhiteSpace(incomingValue))
		{
			return incomingValue;
		}
		return "";
	}

	private void AppendMetadataTags(StringBuilder metadataBuilder)
	{
		if (!string.IsNullOrWhiteSpace(Title))
		{
			metadataBuilder.Append("[ti:" + Title + "]\n");
		}
		if (!string.IsNullOrWhiteSpace(Artist))
		{
			metadataBuilder.Append("[ar:" + Artist + "]\n");
		}
		if (!string.IsNullOrWhiteSpace(Album))
		{
			metadataBuilder.Append("[al:" + Album + "]\n");
		}
		if (!string.IsNullOrWhiteSpace(Creator))
		{
			metadataBuilder.Append("[by:" + Creator + "]\n");
		}
		if (!string.IsNullOrWhiteSpace(lyricist))
		{
			metadataBuilder.Append("[ly:" + lyricist + "]\n");
		}
		if (!string.IsNullOrWhiteSpace(composer))
		{
			metadataBuilder.Append("[mu:" + composer + "]\n");
		}
		if (!string.IsNullOrWhiteSpace(arranger))
		{
			metadataBuilder.Append("[ma:" + arranger + "]\n");
		}
		if (!string.IsNullOrWhiteSpace(publisher))
		{
			metadataBuilder.Append("[pu:" + publisher + "]\n");
		}
		if (!string.IsNullOrWhiteSpace(author))
		{
			metadataBuilder.Append("[au:" + author + "]\n");
		}
		if (!string.IsNullOrWhiteSpace(editor))
		{
			metadataBuilder.Append("[re:" + editor + "]\n");
		}
		if (!string.IsNullOrWhiteSpace(version))
		{
			metadataBuilder.Append("[ve:" + version + "]\n");
		}
		if (!string.IsNullOrWhiteSpace(totalDuration))
		{
			metadataBuilder.Append("[total:" + totalDuration + "]\n");
		}
	}

	private void AppendLyricLines(StringBuilder output, bool omitTimestamps, bool removeBlankLines)
	{
		int lineIndex = 0;
		KeyValuePair<long, LyricLine>? previousLine = null;
		foreach (KeyValuePair<long, LyricLine> lineEntry in linesByTimestamp)
		{
			if ((!omitTimestamps && !removeBlankLines) || !string.IsNullOrWhiteSpace(lineEntry.Value.OriginalText) || !string.IsNullOrWhiteSpace(lineEntry.Value.TranslatedText))
			{
				output.Append(FormatLyricLine(lineEntry, previousLine, lineIndex == linesByTimestamp.Count - 1, omitTimestamps, removeBlankLines));
				previousLine = lineEntry;
			}
			lineIndex++;
		}
	}

	private string FormatLyricLine(KeyValuePair<long, LyricLine> currentLine, KeyValuePair<long, LyricLine>? previousLine, bool isLastLine, bool omitTimestamps, bool removeBlankLines)
	{
		int lyricFormat = Settings.Default.LyricDownload_DownloadTrans_LyricFormat;
		if (lyricFormat == 3)
		{
			lyricFormat = 1;
		}
		if (omitTimestamps && lyricFormat > 1)
		{
			lyricFormat = 1;
		}
		StringBuilder lineBuilder = new StringBuilder();
		LyricLine line = currentLine.Value;
		void AppendTimestamp(long timestampKey)
		{
			if (!omitTimestamps)
			{
				lineBuilder.Append(FormatTimestamp(timestampKey, useThreeDigitMilliseconds: false));
			}
		}
		switch (lyricFormat)
		{
		default:
			AppendTimestamp(currentLine.Key);
			lineBuilder.Append(line.OriginalText);
			if (!string.IsNullOrWhiteSpace(line.TranslatedText))
			{
				string separator = TextUtilities.CoalesceNonBlank(Settings.Default.ConnectorsLyricAndTLyric, " ");
				if (OptionsDialog.BuiltInLyricTranslationSeparators.Contains(separator))
				{
					if (separator.Length > 0)
					{
						lineBuilder.Append(separator[0]);
					}
					if ((separator.Length == 2 && separator[1] == ' ') || separator.Length > 2)
					{
						lineBuilder.Append(separator.Substring(1));
					}
					lineBuilder.Append(line.TranslatedText);
					if (separator.Length == 2 && separator[1] != ' ')
					{
						lineBuilder.Append(separator[1]);
					}
				}
				else
				{
					lineBuilder.Append(separator);
					lineBuilder.Append(line.TranslatedText);
				}
			}
			lineBuilder.Append("\n");
			break;
		case 2:
			if (previousLine.HasValue && previousLine?.Value.TranslatedText != null)
			{
				AppendTimestamp(currentLine.Key - 10L);
				lineBuilder.Append(previousLine?.Value.TranslatedText + "\n");
			}
			AppendTimestamp(currentLine.Key);
			lineBuilder.Append(line.OriginalText + "\n");
			if (isLastLine && line.TranslatedText != null)
			{
				AppendTimestamp(currentLine.Key + 10000L);
				lineBuilder.Append(line.TranslatedText + "\n");
			}
			break;
		case 1:
			if (!removeBlankLines || !string.IsNullOrWhiteSpace(line.OriginalText))
			{
				AppendTimestamp(currentLine.Key);
				lineBuilder.Append(line.OriginalText + "\n");
			}
			if (line.TranslatedText != null)
			{
				AppendTimestamp(currentLine.Key);
				lineBuilder.Append(line.TranslatedText + "\n");
			}
			break;
		}
		return lineBuilder.ToString();
	}

	private void AbsorbTranslatedLines(LyricTextProcessor translatedLyric)
	{
		foreach (KeyValuePair<long, LyricLine> translatedEntry in translatedLyric.linesByTimestamp)
		{
			long timestamp = translatedEntry.Key;
			LyricLine incomingLine = translatedEntry.Value;
			linesByTimestamp.TryGetValue(timestamp, out var targetLine);
			string translationText = (incomingLine.OriginalText + ((incomingLine.TranslatedText == null) ? "" : (" " + incomingLine.TranslatedText))).Trim();
			if (string.IsNullOrWhiteSpace(translationText))
			{
				continue;
			}
			if (targetLine == null)
			{
				targetLine = new LyricLine("");
				linesByTimestamp.Add(timestamp, targetLine);
			}
			if (targetLine.TranslatedText != null)
			{
				targetLine.OriginalText = targetLine.OriginalText + " " + targetLine.TranslatedText;
			}
			targetLine.TranslatedText = translationText;
		}
	}

	public string MergeTranslatedLyric(LyricTextProcessor translatedLyric)
	{
		AbsorbTranslatedLines(translatedLyric);
		Title = MergeMetadataValue(Title, translatedLyric.Title);
		Artist = MergeMetadataValue(Artist, translatedLyric.Artist);
		Album = MergeMetadataValue(Album, translatedLyric.Album);
		Creator = MergeMetadataValue(Creator, translatedLyric.Creator);
		lyricist = MergeMetadataValue(lyricist, translatedLyric.lyricist);
		composer = MergeMetadataValue(composer, translatedLyric.composer);
		arranger = MergeMetadataValue(arranger, translatedLyric.arranger);
		publisher = MergeMetadataValue(publisher, translatedLyric.publisher);
		author = MergeMetadataValue(author, translatedLyric.author);
		editor = MergeMetadataValue(editor, translatedLyric.editor);
		version = MergeMetadataValue(version, translatedLyric.version);
		totalDuration = MergeMetadataValue(totalDuration, translatedLyric.totalDuration);
		StringBuilder lyricBuilder = new StringBuilder();
		AppendMetadataTags(lyricBuilder);
		AppendLyricLines(lyricBuilder, omitTimestamps: false, removeBlankLines: false);
		return Lyric = lyricBuilder.ToString().Trim();
	}

	public bool TryReplaceOriginalLines(List<string> originalLines)
	{
		if (linesByTimestamp.Count() != originalLines.Count())
		{
			return false;
		}
		int lineIndex = 0;
		foreach (KeyValuePair<long, LyricLine> lineEntry in linesByTimestamp)
		{
			lineEntry.Value.OriginalText = originalLines[lineIndex++];
		}
		return true;
	}

	public (string, string) AlignAndSplitTranslatedLyric(LyricTextProcessor translatedLyric)
	{
		AbsorbTranslatedLines(translatedLyric);
		long[] timestampKeys = linesByTimestamp.Keys.ToArray();
		int keyIndex;
		for (keyIndex = 1; keyIndex < timestampKeys.Length - 1; keyIndex++)
		{
			long currentTimestamp = timestampKeys[keyIndex];
			LyricLine translationOnlyLine = linesByTimestamp[currentTimestamp];
			if (translationOnlyLine.OriginalText.Any() || translationOnlyLine.TranslatedText == null)
			{
				continue;
			}
			long previousTimestamp = timestampKeys[keyIndex - 1];
			long nextTimestamp = timestampKeys[keyIndex + 1];
			LyricLine previousLine = linesByTimestamp[previousTimestamp];
			LyricLine nextLine = linesByTimestamp[nextTimestamp];
			long previousDistance = Math.Abs(previousTimestamp - currentTimestamp);
			long nextDistance = Math.Abs(nextTimestamp - currentTimestamp);
			bool canAttachToPreviousLine = previousLine.OriginalText.Any() && previousLine.TranslatedText == null && previousDistance < 1000L;
			bool canAttachToNextLine = nextLine.OriginalText.Any() && nextLine.TranslatedText == null && nextDistance < 1000L;
			if (canAttachToPreviousLine && canAttachToNextLine)
			{
				if (previousDistance <= nextDistance)
				{
					translationOnlyLine.OriginalText = previousLine.OriginalText;
					linesByTimestamp.Remove(previousTimestamp);
				}
				else
				{
					nextLine.TranslatedText = translationOnlyLine.TranslatedText;
					linesByTimestamp.Remove(currentTimestamp);
					keyIndex++;
				}
			}
			else if (canAttachToPreviousLine)
			{
				translationOnlyLine.OriginalText = previousLine.OriginalText;
				linesByTimestamp.Remove(previousTimestamp);
			}
			else
			{
				nextLine.TranslatedText = translationOnlyLine.TranslatedText;
				linesByTimestamp.Remove(currentTimestamp);
				keyIndex++;
			}
		}
		if (keyIndex > 0 && keyIndex == timestampKeys.Length - 1)
		{
			long lastTimestamp = timestampKeys[keyIndex];
			long previousTimestamp = timestampKeys[keyIndex - 1];
			LyricLine lastLine = linesByTimestamp[lastTimestamp];
			LyricLine previousLine = linesByTimestamp[previousTimestamp];
			if (!lastLine.OriginalText.Any() && lastLine.TranslatedText != null && previousLine.OriginalText.Any() && previousLine.TranslatedText == null && Math.Abs(previousTimestamp - lastTimestamp) < 1000L)
			{
				lastLine.OriginalText = previousLine.OriginalText;
				linesByTimestamp.Remove(previousTimestamp);
			}
		}
		StringBuilder originalLyricBuilder = new StringBuilder();
		StringBuilder translatedLyricBuilder = new StringBuilder();
		AppendMetadataTags(originalLyricBuilder);
		translatedLyric.AppendMetadataTags(translatedLyricBuilder);
		foreach (KeyValuePair<long, LyricLine> lineEntry in linesByTimestamp)
		{
			long timestamp = lineEntry.Key;
			LyricLine line = lineEntry.Value;
			originalLyricBuilder.Append(FormatTimestamp(timestamp, useThreeDigitMilliseconds: false));
			originalLyricBuilder.Append(line.OriginalText + "\n");
			if (line.TranslatedText != null)
			{
				translatedLyricBuilder.Append(FormatTimestamp(timestamp, useThreeDigitMilliseconds: false));
				translatedLyricBuilder.Append(line.TranslatedText + "\n");
			}
		}
		return (originalLyricBuilder.ToString().Trim(), translatedLyricBuilder.ToString().Trim());
	}

	public string RemoveTimestampTags()
	{
		StringBuilder lyricBuilder = new StringBuilder();
		AppendLyricLines(lyricBuilder, omitTimestamps: true, removeBlankLines: false);
		return lyricBuilder.ToString().Trim();
	}

	public string ReformatLyricText(bool removeBlankLines, bool removeHeaderTags)
	{
		StringBuilder lyricBuilder = new StringBuilder();
		if (!removeHeaderTags)
		{
			AppendMetadataTags(lyricBuilder);
		}
		AppendLyricLines(lyricBuilder, omitTimestamps: false, removeBlankLines);
		return lyricBuilder.ToString().Trim();
	}

	public string ShiftTimestamps(int offsetMilliseconds)
	{
		StringBuilder lyricBuilder = new StringBuilder();
		AppendMetadataTags(lyricBuilder);
		SortedDictionary<long, LyricLine> shiftedLinesByTimestamp = new SortedDictionary<long, LyricLine>();
		foreach (KeyValuePair<long, LyricLine> lineEntry in linesByTimestamp)
		{
			long shiftedTimestamp = lineEntry.Key + offsetMilliseconds;
			if (shiftedTimestamp < 0L)
			{
				shiftedTimestamp = 0L;
			}
			for (; shiftedLinesByTimestamp.ContainsKey(shiftedTimestamp); shiftedTimestamp++)
			{
			}
			shiftedLinesByTimestamp.Add(shiftedTimestamp, lineEntry.Value);
		}
		linesByTimestamp.Clear();
		linesByTimestamp.AddEntriesFrom(shiftedLinesByTimestamp);
		AppendLyricLines(lyricBuilder, omitTimestamps: false, removeBlankLines: false);
		return lyricBuilder.ToString().Trim();
	}

	public static string RemoveTimestamps(string lyricText)
	{
		string lyricWithoutTimestamps = new LyricTextProcessor(lyricText).RemoveTimestampTags();
		if (!string.IsNullOrEmpty(lyricWithoutTimestamps))
		{
			return lyricWithoutTimestamps;
		}
		return lyricText;
	}

	public static string MergeDownloadedLyrics(string lyricText, string translatedLyricText, bool preferTranslatedOnly)
	{
		if (!string.IsNullOrEmpty(lyricText) && !string.IsNullOrEmpty(translatedLyricText) && !preferTranslatedOnly)
		{
			string mergedLyric = new LyricTextProcessor(lyricText).MergeTranslatedLyric(new LyricTextProcessor(translatedLyricText));
			if (!string.IsNullOrEmpty(mergedLyric))
			{
				return mergedLyric;
			}
			return lyricText;
		}
		if (preferTranslatedOnly && !string.IsNullOrEmpty(translatedLyricText))
		{
			return translatedLyricText;
		}
		return lyricText;
	}

	private static LyricTextProcessor CreateMergedProcessor(string lyricText, string translatedLyricText)
	{
		LyricTextProcessor lyricProcessor = new LyricTextProcessor(lyricText);
		if (!string.IsNullOrEmpty(translatedLyricText))
		{
			lyricProcessor.MergeTranslatedLyric(new LyricTextProcessor(translatedLyricText));
		}
		return lyricProcessor;
	}

	public static string RemoveTimestampsFromDownloadedLyrics(string lyricText, string translatedLyricText, bool preferTranslatedOnly)
	{
		if (!string.IsNullOrEmpty(lyricText))
		{
			if (!preferTranslatedOnly)
			{
				LyricTextProcessor lyricProcessor = CreateMergedProcessor(lyricText, translatedLyricText);
				string lyricWithoutTimestamps = lyricProcessor.RemoveTimestampTags();
				if (!string.IsNullOrEmpty(lyricWithoutTimestamps))
				{
					return lyricWithoutTimestamps;
				}
				return lyricText;
			}
		}
		if (preferTranslatedOnly)
		{
			if (!string.IsNullOrEmpty(translatedLyricText))
			{
				return RemoveTimestamps(translatedLyricText);
			}
			return RemoveTimestamps(lyricText);
		}
		return lyricText;
	}

	public static string ReformatDownloadedLyrics(string lyricText, string translatedLyricText, bool removeBlankLines, bool removeHeaderTags, bool preferTranslatedOnly)
	{
		if (!string.IsNullOrEmpty(lyricText) && !preferTranslatedOnly)
		{
			LyricTextProcessor lyricProcessor = CreateMergedProcessor(lyricText, translatedLyricText);
			string reformattedLyric = lyricProcessor.ReformatLyricText(removeBlankLines, removeHeaderTags);
			if (!string.IsNullOrEmpty(reformattedLyric))
			{
				return reformattedLyric;
			}
			return lyricText;
		}
		if (!preferTranslatedOnly)
		{
			return lyricText;
		}
		if (string.IsNullOrEmpty(translatedLyricText))
		{
			return ReformatLyric(lyricText, removeBlankLines, removeHeaderTags);
		}
		return ReformatLyric(translatedLyricText, removeBlankLines, removeHeaderTags);
	}

	public static string ReformatLyric(string lyricText, bool removeBlankLines, bool removeHeaderTags)
	{
		if (string.IsNullOrEmpty(lyricText))
		{
			return lyricText;
		}
		string reformattedLyric = new LyricTextProcessor(lyricText).ReformatLyricText(removeBlankLines, removeHeaderTags);
		if (!string.IsNullOrEmpty(reformattedLyric))
		{
			return reformattedLyric;
		}
		return lyricText;
	}

	public static string ShiftLyricTimestamps(string lyricText, int offsetMilliseconds)
	{
		if (string.IsNullOrEmpty(lyricText))
		{
			return lyricText;
		}
		string shiftedLyric = new LyricTextProcessor(lyricText).ShiftTimestamps(offsetMilliseconds);
		if (shiftedLyric == null)
		{
			return "";
		}
		if (shiftedLyric.Any())
		{
			return shiftedLyric;
		}
		return lyricText;
	}

}
