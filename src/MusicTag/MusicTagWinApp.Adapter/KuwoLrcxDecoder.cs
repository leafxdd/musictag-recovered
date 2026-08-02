using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using MusicTag.Composer;

namespace MusicTagWinApp.Adapter;

internal sealed class KuwoLrcxTimedLine
{
	internal long TimestampMs { get; }

	internal string Text { get; }

	internal KuwoLrcxTimedLine(long timestampMs, string text)
	{
		TimestampMs = timestampMs;
		Text = text ?? "";
	}
}

internal sealed class KuwoLrcxDecodeResult
{
	internal IReadOnlyList<KuwoLrcxTimedLine> LyricLines { get; }

	internal IReadOnlyList<KuwoLrcxTimedLine> TranslatedLyricLines { get; }

	internal KuwoLrcxDecodeResult(
		IReadOnlyList<KuwoLrcxTimedLine> lyricLines,
		IReadOnlyList<KuwoLrcxTimedLine> translatedLyricLines)
	{
		LyricLines = lyricLines ?? Array.Empty<KuwoLrcxTimedLine>();
		TranslatedLyricLines = translatedLyricLines ?? Array.Empty<KuwoLrcxTimedLine>();
	}

	internal string FormatLyric(bool useThreeDigitMilliseconds)
	{
		return FormatLines(LyricLines, useThreeDigitMilliseconds);
	}

	internal string FormatTranslatedLyric(bool useThreeDigitMilliseconds)
	{
		return FormatLines(TranslatedLyricLines, useThreeDigitMilliseconds);
	}

	private static string FormatLines(IReadOnlyList<KuwoLrcxTimedLine> lines, bool useThreeDigitMilliseconds)
	{
		StringBuilder builder = new StringBuilder();
		foreach (KuwoLrcxTimedLine line in lines)
		{
			builder.Append(LyricTextProcessor.FormatTimestamp(line.TimestampMs, useThreeDigitMilliseconds));
			builder.Append(line.Text);
			builder.Append('\n');
		}
		return builder.ToString();
	}
}

internal static class KuwoLrcxDecoder
{
	private const int MaximumHeaderBytes = 16 * 1024;

	private const int MaximumDecompressedBytes = 2 * 1024 * 1024;

	private const string RequestUrlPrefix = "https://newlyric.kuwo.cn/newlyric.lrc?";

	private static readonly byte[] XorKey = Encoding.ASCII.GetBytes("yeelion");

	private static readonly Encoding StrictGb18030 = Encoding.GetEncoding(
		"GB18030",
		EncoderFallback.ExceptionFallback,
		DecoderFallback.ExceptionFallback);

	private static readonly Regex TimedLineRegex = new Regex(
		"^\\[(\\d+):(\\d{2})(?:\\.(\\d{1,3}))?\\](.*)$",
		RegexOptions.Compiled | RegexOptions.CultureInvariant);

	private static readonly Regex WordTimingRegex = new Regex(
		"<-?\\d+,-?\\d+(?:,-?\\d+)?>",
		RegexOptions.Compiled | RegexOptions.CultureInvariant);

	private static readonly Regex ResidualAngleTagRegex = new Regex(
		"<[^>]*>",
		RegexOptions.Compiled | RegexOptions.CultureInvariant);

	internal static string BuildRequestUrl(string trackId)
	{
		if (!long.TryParse(trackId, NumberStyles.None, CultureInfo.InvariantCulture, out long numericTrackId) || numericTrackId <= 0L)
		{
			throw new ArgumentException("Kuwo LRCX track ID must be a positive integer.", nameof(trackId));
		}

		string requestText = "user=12345,web,web,web&requester=localhost&req=1&rid=MUSIC_" +
			numericTrackId.ToString(CultureInfo.InvariantCulture) + "&lrcx=1";
		byte[] requestBytes = Encoding.ASCII.GetBytes(requestText);
		ApplyXor(requestBytes);
		return RequestUrlPrefix + Convert.ToBase64String(requestBytes);
	}

	internal static KuwoLrcxDecodeResult DecodeResponse(byte[] responseBytes)
	{
		if (responseBytes == null)
		{
			throw new ArgumentNullException(nameof(responseBytes));
		}

		int separatorIndex = FindHeaderSeparator(responseBytes);
		if (separatorIndex < 0)
		{
			throw new InvalidDataException("Kuwo LRCX response header separator is missing.");
		}

		string header = DecodeAsciiHeader(responseBytes, separatorIndex);
		if (!HasContentHeader(header))
		{
			throw new InvalidDataException("Kuwo LRCX response is not a content payload.");
		}

		int compressedOffset = separatorIndex + 4;
		if (compressedOffset >= responseBytes.Length)
		{
			throw new InvalidDataException("Kuwo LRCX compressed payload is empty.");
		}

		byte[] compressedBytes = new byte[responseBytes.Length - compressedOffset];
		Buffer.BlockCopy(responseBytes, compressedOffset, compressedBytes, 0, compressedBytes.Length);
		byte[] encodedBytes = Decompress(compressedBytes);
		byte[] lyricBytes = DecodeStrictBase64(encodedBytes);
		ApplyXor(lyricBytes);
		string lyricText = StrictGb18030.GetString(lyricBytes);
		return ParseTimedLines(lyricText);
	}

	internal static KuwoLrcxDecodeResult ParseTimedLines(string lyricText)
	{
		if (lyricText == null)
		{
			throw new ArgumentNullException(nameof(lyricText));
		}

		List<KuwoLrcxTimedLine> timedLines = new List<KuwoLrcxTimedLine>();
		using (StringReader reader = new StringReader(lyricText))
		{
			string line;
			while ((line = reader.ReadLine()) != null)
			{
				if (TryParseTimedLine(line, out KuwoLrcxTimedLine timedLine))
				{
					timedLines.Add(timedLine);
				}
			}
		}

		return PairTimedLines(timedLines);
	}

	private static KuwoLrcxDecodeResult PairTimedLines(IReadOnlyList<KuwoLrcxTimedLine> timedLines)
	{
		List<KuwoLrcxTimedLine> lyricLines = new List<KuwoLrcxTimedLine>();
		List<KuwoLrcxTimedLine> translatedLyricLines = new List<KuwoLrcxTimedLine>();
		KuwoLrcxTimedLine pendingLine = null;

		foreach (KuwoLrcxTimedLine currentLine in timedLines)
		{
			if (pendingLine == null)
			{
				pendingLine = currentLine;
				continue;
			}

			if (pendingLine.TimestampMs == currentLine.TimestampMs)
			{
				if (lyricLines.Count > 0 && !string.IsNullOrWhiteSpace(pendingLine.Text))
				{
					translatedLyricLines.Add(new KuwoLrcxTimedLine(lyricLines[lyricLines.Count - 1].TimestampMs, pendingLine.Text));
				}
				if (!string.IsNullOrWhiteSpace(currentLine.Text))
				{
					lyricLines.Add(currentLine);
				}
				pendingLine = null;
				continue;
			}

			if (!string.IsNullOrWhiteSpace(pendingLine.Text))
			{
				lyricLines.Add(pendingLine);
			}
			pendingLine = currentLine;
		}

		if (pendingLine != null && !string.IsNullOrWhiteSpace(pendingLine.Text))
		{
			lyricLines.Add(pendingLine);
		}

		return new KuwoLrcxDecodeResult(lyricLines, translatedLyricLines);
	}

	private static bool TryParseTimedLine(string line, out KuwoLrcxTimedLine timedLine)
	{
		timedLine = null;
		Match match = TimedLineRegex.Match(line);
		if (!match.Success ||
			!long.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out long minutes) ||
			!int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int seconds) ||
			seconds >= 60)
		{
			return false;
		}

		int milliseconds = 0;
		string fractionalText = match.Groups[3].Value;
		if (fractionalText.Length > 0)
		{
			if (!int.TryParse(fractionalText, NumberStyles.None, CultureInfo.InvariantCulture, out milliseconds))
			{
				return false;
			}
			if (fractionalText.Length == 1)
			{
				milliseconds *= 100;
			}
			else if (fractionalText.Length == 2)
			{
				milliseconds *= 10;
			}
		}

		string text = WordTimingRegex.Replace(match.Groups[4].Value, "");
		text = ResidualAngleTagRegex.Replace(text, "");
		if (text.IndexOf('<') >= 0 || text.IndexOf('>') >= 0)
		{
			return false;
		}

		try
		{
			long timestampMs = checked(minutes * 60000L + seconds * 1000L + milliseconds);
			timedLine = new KuwoLrcxTimedLine(timestampMs, text);
			return true;
		}
		catch (OverflowException)
		{
			return false;
		}
	}

	private static int FindHeaderSeparator(byte[] responseBytes)
	{
		int lastStartIndex = Math.Min(responseBytes.Length - 4, MaximumHeaderBytes);
		for (int index = 0; index <= lastStartIndex; index++)
		{
			if (responseBytes[index] == (byte)'\r' && responseBytes[index + 1] == (byte)'\n' &&
				responseBytes[index + 2] == (byte)'\r' && responseBytes[index + 3] == (byte)'\n')
			{
				return index;
			}
		}
		return -1;
	}

	private static string DecodeAsciiHeader(byte[] responseBytes, int headerLength)
	{
		for (int index = 0; index < headerLength; index++)
		{
			byte value = responseBytes[index];
			bool allowedControl = value == (byte)'\r' || value == (byte)'\n' || value == (byte)'\t';
			if (!allowedControl && (value < 0x20 || value > 0x7E))
			{
				throw new InvalidDataException("Kuwo LRCX response header is not ASCII.");
			}
		}
		return Encoding.ASCII.GetString(responseBytes, 0, headerLength);
	}

	private static bool HasContentHeader(string header)
	{
		foreach (string line in header.Split(new[] { "\r\n" }, StringSplitOptions.None))
		{
			if (line.Trim().Equals("tp=content", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	private static byte[] Decompress(byte[] compressedBytes)
	{
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
				throw new InvalidDataException("Kuwo LRCX decompressed payload is too large.");
			}
			output.Write(buffer, 0, bytesRead);
		}
		if (output.Length == 0L)
		{
			throw new InvalidDataException("Kuwo LRCX decompressed payload is empty.");
		}
		return output.ToArray();
	}

	private static byte[] DecodeStrictBase64(byte[] encodedBytes)
	{
		for (int index = 0; index < encodedBytes.Length; index++)
		{
			byte value = encodedBytes[index];
			bool isBase64Character = value >= (byte)'A' && value <= (byte)'Z' || value >= (byte)'a' && value <= (byte)'z' ||
				value >= (byte)'0' && value <= (byte)'9' || value == (byte)'+' || value == (byte)'/' || value == (byte)'=';
			bool isAllowedWhitespace = value == (byte)' ' || value == (byte)'\t' || value == (byte)'\r' || value == (byte)'\n';
			if (value > 0x7F || !isBase64Character && !isAllowedWhitespace)
			{
				throw new FormatException("Kuwo LRCX payload is not strict ASCII Base64.");
			}
		}
		return Convert.FromBase64String(Encoding.ASCII.GetString(encodedBytes));
	}

	private static void ApplyXor(byte[] bytes)
	{
		for (int index = 0; index < bytes.Length; index++)
		{
			bytes[index] ^= XorKey[index % XorKey.Length];
		}
	}
}
