using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using MusicTag.States;
using MusicTagWinApp.Instances;

namespace MusicTag.Serialization;

internal class TagTextEncoding
{
	private const string ValueSeparator = "; ";

	private static readonly List<string> availableEncodingNames = new List<string>();

	private static readonly Dictionary<string, TagTextEncoding> encodingProfiles = CreateEncodingProfiles();

	private readonly Encoding encoding;

	private TagTextEncoding(string encodingName)
	{
		if (encodingName != null)
		{
			encoding = Encoding.GetEncoding(encodingName);
		}
	}

	public static List<string> GetEncodingNames()
	{
		return availableEncodingNames;
	}

	private static Dictionary<string, TagTextEncoding> CreateEncodingProfiles()
	{
		Dictionary<string, TagTextEncoding> profiles = new Dictionary<string, TagTextEncoding>();
		Action<string> registerDirectEncoding = (encodingName) => RegisterEncoding(profiles, encodingName, encodingName);
		registerDirectEncoding("UTF-8");
		registerDirectEncoding("UTF-16");
		registerDirectEncoding("UTF-16LE");
		registerDirectEncoding("UTF-16BE");
		registerDirectEncoding("ISO-8859-1");
		registerDirectEncoding("GB18030");
		registerDirectEncoding("BIG5");
		registerDirectEncoding("Shift_JIS");
		registerDirectEncoding("EUC-JP");
		registerDirectEncoding("EUC-KR");
		RegisterEncoding(profiles, "GB=>BIG5", null);
		RegisterEncoding(profiles, "GB=>Shift_JIS", null);
		RegisterEncoding(profiles, "GB=>EUC-KR", null);
		RegisterEncoding(profiles, "BIG5=>GB", null);
		RegisterEncoding(profiles, "BIG5=>Shift_JIS", null);
		RegisterEncoding(profiles, "BIG5=>EUC-KR", null);
		RegisterEncoding(profiles, "ISO-8859-1=>GB", null);
		RegisterEncoding(profiles, "ISO-8859-1=>BIG5", null);
		RegisterEncoding(profiles, "ISO-8859-1=>Shift_JIS", null);
		RegisterEncoding(profiles, "ISO-8859-1=>EUC-KR", null);
		registerDirectEncoding("ASMO-708");
		registerDirectEncoding("ISO-8859-6");
		registerDirectEncoding("WINDOWS-1256");
		registerDirectEncoding("ISO-8859-4");
		registerDirectEncoding("WINDOWS-1257");
		registerDirectEncoding("IBM852");
		registerDirectEncoding("ISO-8859-2");
		registerDirectEncoding("WINDOWS-1250");
		registerDirectEncoding("CP866");
		registerDirectEncoding("ISO-8859-5");
		registerDirectEncoding("KOI8-R");
		registerDirectEncoding("KOI8-U");
		registerDirectEncoding("WINDOWS-1251");
		registerDirectEncoding("ISO-8859-7");
		registerDirectEncoding("WINDOWS-1253");
		registerDirectEncoding("ISO-8859-8");
		registerDirectEncoding("WINDOWS-1255");
		registerDirectEncoding("WINDOWS-874");
		registerDirectEncoding("ISO-8859-9");
		registerDirectEncoding("WINDOWS-1254");
		registerDirectEncoding("WINDOWS-1258");
		registerDirectEncoding("WINDOWS-1252");
		return profiles;
	}

	private static void RegisterEncoding(Dictionary<string, TagTextEncoding> profiles, string displayName, string encodingName)
	{
		try
		{
			profiles.Add(displayName, new TagTextEncoding(encodingName));
			availableEncodingNames.Add(displayName);
		}
		catch (ArgumentException ex)
		{
			Console.WriteLine("append text encoding error:" + ex.Message);
		}
	}

	public static string NormalizeEncodingName(string encodingName)
	{
		switch (encodingName)
		{
		case "UTF8":
			return "UTF-8";
		case "UTF16BE":
			return "UTF-16BE";
		case "UTF16LE":
			return "UTF-16LE";
		case "UTF16":
			return "UTF-16";
		case "Latin1":
			return "ISO-8859-1";
		default:
			if (Encoding.Default.HeaderName.Equals("gb2312", StringComparison.OrdinalIgnoreCase))
			{
				return "GB18030";
			}
			return Encoding.Default.HeaderName.ToUpper();
		}
	}

	public static string DecodeTagValue(string fieldName, string stringType, List<byte[]> rawValues, string currentTagValue, string currentText, string selectedEncodingName)
	{
		Encoding selectedEncoding = encodingProfiles[selectedEncodingName].encoding;
		StringBuilder output = new StringBuilder();
		if (selectedEncoding != null && currentTagValue == currentText)
		{
			for (int valueIndex = 0; valueIndex < rawValues.Count; valueIndex++)
			{
				if (valueIndex > 0)
				{
					output.Append(ValueSeparator);
				}
				string decodedValue = TrimAtNullTerminator(selectedEncoding.GetStringRespectingUtf16Bom(rawValues[valueIndex]));
				if (fieldName == "genre")
				{
					output.Append(DecodeGenreValue(decodedValue));
				}
				else
				{
					output.Append(decodedValue);
				}
			}
		}
		else
		{
			output.Append(TranscodeText(currentText, GetTranscodeSourceEncoding(stringType, selectedEncodingName), GetTranscodeTargetEncoding(selectedEncodingName)));
		}
		return output.ToString();
	}

	private static string TrimAtNullTerminator(string value)
	{
		int nullIndex = value.IndexOf('\0');
		if (nullIndex > 0)
		{
			return value.Substring(0, nullIndex);
		}
		if (nullIndex == 0)
		{
			return "";
		}
		return value;
	}

	private static string DecodeGenreValue(string value)
	{
		try
		{
			string decodedGenre = "";
			Match match = Regex.Match(value, "^(\\(\\s*([\\w+-]+)\\))?\\s*([\\w+-]+)?$");
			if (!match.Success)
			{
				return value;
			}
			bool foundGenreCode = false;
			GroupCollection groups = match.Groups;
			if (!string.IsNullOrEmpty(groups[2].Value))
			{
				string genreName = ConfigDescriptorState.GetGenreNameByIndex(int.Parse(groups[2].Value));
				decodedGenre = string.IsNullOrEmpty(genreName) ? decodedGenre + groups[1].Value : decodedGenre + genreName;
				foundGenreCode = true;
			}
			if (!string.IsNullOrEmpty(groups[3].Value))
			{
				string genreName = ConfigDescriptorState.GetGenreNameByIndex(int.Parse(groups[3].Value));
				decodedGenre = string.IsNullOrEmpty(genreName)
					? decodedGenre + ((!string.IsNullOrEmpty(decodedGenre)) ? ValueSeparator : "") + groups[3].Value
					: decodedGenre + ((!string.IsNullOrEmpty(decodedGenre)) ? ValueSeparator : "") + genreName;
				foundGenreCode = true;
			}
			if (!foundGenreCode)
			{
				decodedGenre += value;
			}
			return decodedGenre;
		}
		catch (Exception ex)
		{
			Console.WriteLine("bytearray2string error:" + ex.Message);
			return value;
		}
	}

	private static string GetTranscodeSourceEncoding(string stringType, string selectedEncodingName)
	{
		switch (selectedEncodingName)
		{
		case "GB=>Shift_JIS":
		case "GB=>EUC-KR":
		case "GB=>BIG5":
			return "GB18030";
		case "BIG5=>Shift_JIS":
		case "BIG5=>GB":
		case "BIG5=>EUC-KR":
			return "BIG5";
		case "ISO-8859-1=>EUC-KR":
		case "ISO-8859-1=>GB":
		case "ISO-8859-1=>BIG5":
		case "ISO-8859-1=>Shift_JIS":
			return "ISO-8859-1";
		default:
			return NormalizeEncodingName(stringType);
		}
	}

	private static string GetTranscodeTargetEncoding(string selectedEncodingName)
	{
		switch (selectedEncodingName)
		{
		case "GB=>Shift_JIS":
		case "BIG5=>Shift_JIS":
		case "ISO-8859-1=>Shift_JIS":
			return "Shift_JIS";
		case "ISO-8859-1=>EUC-KR":
		case "GB=>EUC-KR":
		case "BIG5=>EUC-KR":
			return "EUC-KR";
		case "BIG5=>GB":
		case "ISO-8859-1=>GB":
			return "GB18030";
		case "ISO-8859-1=>BIG5":
		case "GB=>BIG5":
			return "BIG5";
		default:
			return selectedEncodingName;
		}
	}

	public static string TranscodeText(string value, string sourceEncodingName, string targetEncodingName)
	{
		Encoding sourceEncoding = Encoding.GetEncoding(sourceEncodingName);
		Encoding targetEncoding = Encoding.GetEncoding(targetEncodingName);
		byte[] bytes = sourceEncoding.GetBytes(value);
		return targetEncoding.GetStringRespectingUtf16Bom(bytes);
	}
}
