using System;

namespace MusicTag.Candidates;

internal class PhraseMapping
{
	public PhraseMapping(string phrase, params string[] replacements)
	{
		Phrase = phrase;
		Replacements = replacements ?? Array.Empty<string>();
	}

	public string Phrase { get; }

	public string[] Replacements { get; }

	public override string ToString()
	{
		if (Replacements.Length == 0)
		{
			return Phrase;
		}
		return Phrase + "\t" + string.Join("\t", Replacements);
	}
}
