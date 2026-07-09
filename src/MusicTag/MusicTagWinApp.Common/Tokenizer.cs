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
		// net8 迁移:core 上 Encoding.Default 恒为 UTF-8,这里要的是 netfx 的"系统 ANSI
		// 代码页"回退语义(中文系统 = gb2312),改经 SystemAnsiEncoding。
		return SystemAnsiEncoding.Instance.HeaderName;
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
	// single-byte Western text, and DetectFileEncoding routed both to the system
	// ANSI code page (netfx Encoding.Default; SystemAnsiEncoding after the net8
	// migration). Mirror that: never commit to us-ascii / iso-8859-1 /
	// windows-1252. us-ascii in particular MUST fall back, because the file may
	// contain CJK past the 2000-byte detection sample and the ANSI code page is an
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
