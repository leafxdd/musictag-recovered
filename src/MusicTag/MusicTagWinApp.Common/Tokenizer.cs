using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using MusicTag.States;
using MusicTagWinApp.Instances;

namespace MusicTagWinApp.Common;

internal class Tokenizer
{
	public static string DetectFileEncoding(string filePath)
	{
		var (sampleBytes, bytesRead) = ReadFileSampleBytes(filePath);
		string encodingName;
		if (bytesRead > 0
			&& (encodingName = ConfigDescriptorState.ReadAndFreeNativeString(ResolveToken(sampleBytes, bytesRead))) != null
			&& !string.IsNullOrWhiteSpace(encodingName)
			&& encodingName != "ISO8859-1")
		{
			Console.WriteLine("GetCSharpEncode " + encodingName);
			return encodingName;
		}
		return Encoding.Default.HeaderName;
	}

	private static (byte[], int) ReadFileSampleBytes(string filePath)
	{
		byte[] sampleBytes = new byte[2000];
		int bytesRead = 0;
		try
		{
			using FileStream fileStream = new FileStream(filePath, FileMode.Open);
			bytesRead = fileStream.Read(sampleBytes, 0, sampleBytes.Length);
		}
		catch (Exception ex)
		{
			Console.WriteLine("GetFileBytes error:" + ex.GetMessageChain());
		}
		return (sampleBytes, bytesRead);
	}

	[DllImport("MusicTag.dll", EntryPoint = "de")]
	private static extern IntPtr ResolveToken(byte[] res, int offset_cont);
}

