using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using MusicTag.Composer;
using Newtonsoft.Json.Linq;

namespace MusicTag.Candidates;

internal sealed class KugouKrcDecodeResult
{
	internal string Lyric { get; }

	internal string TranslatedLyric { get; }

	internal KugouKrcDecodeResult(string lyric, string translatedLyric)
	{
		Lyric = lyric ?? "";
		TranslatedLyric = translatedLyric ?? "";
	}
}

internal static class KugouKrcDecoder
{
	private const int MaximumDecompressedBytes = 2 * 1024 * 1024;

	private static readonly byte[] KrcXorKey =
	{
		0x40, 0x47, 0x61, 0x77, 0x5E, 0x32, 0x74, 0x47,
		0x51, 0x36, 0x31, 0x2D, 0xCE, 0xD2, 0x6E, 0x69
	};

	private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

	private static readonly Regex TimedLineRegex = new Regex(
		"^\\[(\\d+),(\\d+)\\](.*)$",
		RegexOptions.Compiled | RegexOptions.CultureInvariant);

	private static readonly Regex DamagedTimedLineRegex = new Regex(
		"^\\[(\\d+),[^\\]]*\\](.*)$",
		RegexOptions.Compiled | RegexOptions.CultureInvariant);

	private static readonly Regex WordTimingRegex = new Regex(
		"<(-?\\d+),(-?\\d+),(-?\\d+)>",
		RegexOptions.Compiled | RegexOptions.CultureInvariant);

	private static readonly Regex MetadataRegex = new Regex(
		"^\\[([A-Za-z]+):(.*)\\]$",
		RegexOptions.Compiled | RegexOptions.CultureInvariant);

	internal static KugouKrcDecodeResult DecodeContent(string encodedContent, int contentType, bool useThreeDigitMilliseconds)
	{
		byte[] payload = DecodeStrictBase64(encodedContent);
		switch (contentType)
		{
			case 0:
				return ConvertKrcText(DecodeKrcPayload(payload), useThreeDigitMilliseconds);
			case 1:
			case 2:
				string plainText = TrimUtf8Bom(StrictUtf8.GetString(payload));
				if (string.IsNullOrWhiteSpace(plainText))
				{
					throw new InvalidDataException("Kugou lyric payload is empty.");
				}
				return new KugouKrcDecodeResult(plainText, "");
			default:
				throw new InvalidDataException("Unsupported Kugou lyric content type.");
		}
	}

	internal static KugouKrcDecodeResult ConvertKrcText(string krcText, bool useThreeDigitMilliseconds)
	{
		if (krcText == null)
		{
			throw new ArgumentNullException(nameof(krcText));
		}

		List<KrcTimedLine> timedLines = new List<KrcTimedLine>();
		StringBuilder metadataBuilder = new StringBuilder();
		string languagePayload = null;
		using (StringReader reader = new StringReader(TrimUtf8Bom(krcText)))
		{
			string line;
			while ((line = reader.ReadLine()) != null)
			{
				if (TryParseTimedLine(line, out KrcTimedLine timedLine))
				{
					timedLines.Add(timedLine);
					continue;
				}

				Match metadataMatch = MetadataRegex.Match(line);
				if (!metadataMatch.Success)
				{
					continue;
				}

				string metadataName = metadataMatch.Groups[1].Value.ToLowerInvariant();
				if (metadataName == "language")
				{
					languagePayload = metadataMatch.Groups[2].Value;
				}
				else if (IsStandardLrcMetadata(metadataName))
				{
					metadataBuilder.Append(line);
					metadataBuilder.Append('\n');
				}
			}
		}

		StringBuilder lyricBuilder = new StringBuilder(metadataBuilder.ToString());
		bool appendedTimedLine = false;
		foreach (KrcTimedLine timedLine in timedLines)
		{
			if (string.IsNullOrWhiteSpace(timedLine.Text))
			{
				continue;
			}
			AppendTimedText(lyricBuilder, timedLine.TimestampMs, timedLine.Text, useThreeDigitMilliseconds);
			appendedTimedLine = true;
		}
		if (!appendedTimedLine)
		{
			throw new InvalidDataException("KRC contains no timed lyric lines.");
		}

		StringBuilder translatedLyricBuilder = new StringBuilder();
		List<string> translatedLines = TryDecodeTranslatedLines(languagePayload, timedLines.Count);
		if (translatedLines != null)
		{
			for (int index = 0; index < timedLines.Count; index++)
			{
				if (string.IsNullOrWhiteSpace(timedLines[index].Text) || string.IsNullOrWhiteSpace(translatedLines[index]))
				{
					continue;
				}
				AppendTimedText(translatedLyricBuilder, timedLines[index].TimestampMs, translatedLines[index], useThreeDigitMilliseconds);
			}
		}

		return new KugouKrcDecodeResult(lyricBuilder.ToString(), translatedLyricBuilder.ToString());
	}

	private static string DecodeKrcPayload(byte[] payload)
	{
		if (payload.Length <= 4 || payload[0] != (byte)'k' || payload[1] != (byte)'r' || payload[2] != (byte)'c' || payload[3] != (byte)'1')
		{
			throw new InvalidDataException("Invalid KRC magic or compressed body.");
		}

		byte[] compressedBytes = new byte[payload.Length - 4];
		for (int index = 0; index < compressedBytes.Length; index++)
		{
			compressedBytes[index] = (byte)(payload[index + 4] ^ KrcXorKey[index % KrcXorKey.Length]);
		}

		using MemoryStream input = new MemoryStream(compressedBytes, writable: false);
		using ZLibStream decompressor = new ZLibStream(input, CompressionMode.Decompress);
		using MemoryStream output = new MemoryStream();
		byte[] buffer = new byte[8192];
		while (true)
		{
			int bytesRead = decompressor.Read(buffer, 0, buffer.Length);
			if (bytesRead <= 0)
			{
				break;
			}
			if (output.Length > MaximumDecompressedBytes - bytesRead)
			{
				throw new InvalidDataException("KRC decompressed payload is too large.");
			}
			output.Write(buffer, 0, bytesRead);
		}
		if (output.Length == 0L)
		{
			throw new InvalidDataException("KRC decompressed payload is empty.");
		}
		return TrimUtf8Bom(StrictUtf8.GetString(output.ToArray()));
	}

	private static byte[] DecodeStrictBase64(string encodedContent)
	{
		if (encodedContent == null)
		{
			throw new ArgumentNullException(nameof(encodedContent));
		}
		for (int index = 0; index < encodedContent.Length; index++)
		{
			char value = encodedContent[index];
			bool isBase64Character = value >= 'A' && value <= 'Z' || value >= 'a' && value <= 'z' ||
				value >= '0' && value <= '9' || value == '+' || value == '/' || value == '=';
			bool isAllowedWhitespace = value == ' ' || value == '\t' || value == '\r' || value == '\n';
			if (value > 0x7F || !isBase64Character && !isAllowedWhitespace)
			{
				throw new FormatException("Kugou lyric payload is not strict ASCII Base64.");
			}
		}
		return Convert.FromBase64String(encodedContent);
	}

	private static bool TryParseTimedLine(string line, out KrcTimedLine timedLine)
	{
		timedLine = null;
		Match match = TimedLineRegex.Match(line);
		if (match.Success && long.TryParse(match.Groups[1].Value, out long timestampMs))
		{
			timedLine = new KrcTimedLine(timestampMs, WordTimingRegex.Replace(match.Groups[3].Value, ""));
			return true;
		}

		match = DamagedTimedLineRegex.Match(line);
		if (!match.Success || !long.TryParse(match.Groups[1].Value, out long damagedLineStartMs))
		{
			return false;
		}
		Match firstWordMatch = WordTimingRegex.Match(match.Groups[2].Value);
		if (!firstWordMatch.Success || !long.TryParse(firstWordMatch.Groups[1].Value, out long firstWordOffsetMs))
		{
			return false;
		}
		try
		{
			timedLine = new KrcTimedLine(checked(damagedLineStartMs + firstWordOffsetMs), WordTimingRegex.Replace(match.Groups[2].Value, ""));
			return true;
		}
		catch (OverflowException)
		{
			return false;
		}
	}

	private static List<string> TryDecodeTranslatedLines(string encodedLanguage, int expectedLineCount)
	{
		if (string.IsNullOrWhiteSpace(encodedLanguage))
		{
			return null;
		}
		try
		{
			string languageJson = StrictUtf8.GetString(DecodeStrictBase64(encodedLanguage));
			JArray contents = JObject.Parse(languageJson)["content"] as JArray;
			if (contents == null)
			{
				return null;
			}
			foreach (JToken contentToken in contents)
			{
				if (!(contentToken is JObject content) || content["type"]?.Value<int?>() != 1 || !(content["lyricContent"] is JArray lyricContent))
				{
					continue;
				}
				if (lyricContent.Count != expectedLineCount)
				{
					return null;
				}

				List<string> translatedLines = new List<string>(expectedLineCount);
				foreach (JToken lineToken in lyricContent)
				{
					if (!(lineToken is JArray fragments))
					{
						return null;
					}
					StringBuilder lineBuilder = new StringBuilder();
					foreach (JToken fragment in fragments)
					{
						if (fragment.Type != JTokenType.String)
						{
							return null;
						}
						lineBuilder.Append(fragment.ToString());
					}
					translatedLines.Add(lineBuilder.ToString());
				}
				return translatedLines;
			}
		}
		catch (Exception)
		{
			return null;
		}
		return null;
	}

	private static void AppendTimedText(StringBuilder builder, long timestampMs, string text, bool useThreeDigitMilliseconds)
	{
		builder.Append(LyricTextProcessor.FormatTimestamp(timestampMs, useThreeDigitMilliseconds));
		builder.Append(text);
		builder.Append('\n');
	}

	private static bool IsStandardLrcMetadata(string metadataName)
	{
		return metadataName == "ti" || metadataName == "ar" || metadataName == "al" || metadataName == "by" || metadataName == "offset";
	}

	private static string TrimUtf8Bom(string value)
	{
		return value.Length > 0 && value[0] == '\uFEFF' ? value.Substring(1) : value;
	}

	private sealed class KrcTimedLine
	{
		internal long TimestampMs { get; }

		internal string Text { get; }

		internal KrcTimedLine(long timestampMs, string text)
		{
			TimestampMs = timestampMs;
			Text = text;
		}
	}
}
