using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using MusicTag.Candidates;
using MusicTagWinApp.ChineseUtils;
using MusicTagWinApp.Listeners;
using MusicTagWinApp.Properties;
using MusicTagWinApp.Writers;

namespace MusicTagWinApp.Structs;

internal class ChineseTextConverter
{
	private const char FirstMappedChineseChar = '一';

	private const char LastMappedChineseChar = '龥';

	private static readonly ChineseTextConverter traditionalToSimplifiedConverter = new ChineseTextConverter(Resources.tsmap);

	private static readonly ChineseTextConverter simplifiedToTraditionalConverter = new ChineseTextConverter(Resources.tcmap);

	private char[] characterMap;

	private PhraseTrie phraseTrie;

	private ChineseTextConverter(byte[] serializedMapping)
	{
		using MemoryStream mappingStream = new MemoryStream(serializedMapping);
		MappingChars mappingChars = new BinaryFormatter().Deserialize(mappingStream) as MappingChars;
		characterMap = mappingChars.chars.ToCharArray();
		LoadPhraseMappings(mappingChars.lexemics);
	}

	public static ChineseTextConverter TraditionalToSimplified()
	{
		return traditionalToSimplifiedConverter;
	}

	public static ChineseTextConverter SimplifiedToTraditional()
	{
		return simplifiedToTraditionalConverter;
	}

	private void LoadPhraseMappings(string[] mappings)
	{
		phraseTrie = new PhraseTrie();
		foreach (string mapping in mappings)
		{
			if (string.IsNullOrEmpty(mapping) || mapping.StartsWith("#"))
			{
				continue;
			}

			string[] parts = mapping.Split('=');
			if (parts.Length < 2)
			{
				continue;
			}

			string phrase = parts[0];
			string replacement = parts[1];
			PhraseTrieBuilder.AddMapping(phraseTrie, new PhraseMapping(phrase, replacement));
		}
	}

	public char ConvertCharacter(char value)
	{
		if (value >= FirstMappedChineseChar && value <= LastMappedChineseChar)
		{
			return characterMap[value - FirstMappedChineseChar];
		}
		return value;
	}

	private void AppendConvertedCharacters(string text, StringBuilder output)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return;
		}

		for (int index = 0; index < text.Length; index++)
		{
			output.Append(ConvertCharacter(text[index]));
		}
	}

	public string ConvertText(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return text;
		}

		PhraseMatcher phraseMatcher = phraseTrie.GetMatcher(text) as PhraseMatcher;
		StringBuilder output = new StringBuilder(text.Length);
		int currentIndex = 0;
		string matchedPhrase;
		while ((matchedPhrase = phraseMatcher.NextMatch()) != null)
		{
			AppendConvertedCharacters(text.Substring(currentIndex, phraseMatcher.MatchStartIndex - currentIndex), output);
			output.Append(phraseMatcher.GetReplacementAt(0));
			currentIndex = phraseMatcher.MatchStartIndex + matchedPhrase.Length;
		}

		if (currentIndex < text.Length)
		{
			AppendConvertedCharacters(text.Substring(currentIndex), output);
		}

		return output.ToString();
	}

	public string ConvertCharactersOnly(string text)
	{
		try
		{
			StringBuilder output = new StringBuilder(text.Length);
			for (int index = 0; index < text.Length; index++)
			{
				output.Append(ConvertCharacter(text[index]));
			}
			return output.ToString();
		}
		catch (Exception ex)
		{
			Console.WriteLine("convertnomatch error:" + ex.Message);
			return text;
		}
	}
}
