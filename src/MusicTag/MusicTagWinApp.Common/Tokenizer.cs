using System;
using System.IO;
using System.Text;
using MusicTagWinApp.Instances;
using UtfUnknown;

namespace MusicTagWinApp.Common;

internal class Tokenizer
{
	public static string DetectFileEncoding(string filePath)
	{
		byte[] sampleBytes = ReadFileSampleBytes(filePath);
		if (sampleBytes.Length > 0)
		{
			Encoding detectedEncoding = CharsetDetector.DetectFromBytes(sampleBytes).Detected?.Encoding;
			if (detectedEncoding != null && !IsSystemDefaultFallback(detectedEncoding.WebName))
			{
				Console.WriteLine("GetCSharpEncode " + detectedEncoding.WebName);
				return detectedEncoding.WebName;
			}
		}
		return Encoding.Default.HeaderName;
	}

	private static byte[] ReadFileSampleBytes(string filePath)
	{
		try
		{
			byte[] buffer = new byte[2000];
			using FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			int bytesRead = fileStream.Read(buffer, 0, buffer.Length);
			if (bytesRead <= 0)
			{
				return Array.Empty<byte>();
			}
			if (bytesRead < buffer.Length)
			{
				Array.Resize(ref buffer, bytesRead);
			}
			return buffer;
		}
		catch (Exception ex)
		{
			Console.WriteLine("GetFileBytes error:" + ex.GetMessageChain());
			return Array.Empty<byte>();
		}
	}

	// The native `de` detector reported "" for ASCII and "ISO8859-1" for ambiguous
	// single-byte Western text, and DetectFileEncoding routed both to
	// Encoding.Default. Mirror that: never commit to us-ascii / iso-8859-1 /
	// windows-1252. us-ascii in particular MUST fall back, because the file may
	// contain CJK past the 2000-byte detection sample and Encoding.Default is an
	// ASCII superset that still decodes it, whereas us-ascii would corrupt it.
	private static bool IsSystemDefaultFallback(string webName)
	{
		switch (webName)
		{
			case "us-ascii":
			case "iso-8859-1":
			case "windows-1252":
				return true;
			default:
				return false;
		}
	}
}
