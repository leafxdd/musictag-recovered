using MusicTagWinApp.Adapter;
using MusicTagWinApp.Listeners;

namespace MusicTagWinApp.Writers;

internal class PhraseTrie : TrieNode<string[]>
{
	public PhraseTrie()
	{
	}

	public PhraseTrie(char character, int nodeKind, string[] replacements)
		: base(character, nodeKind, replacements)
	{
	}

	public override TrieNode<string[]> GetChild(char character)
	{
		return base.GetChild(character);
	}

	public override TrieMatcher<string[]> GetMatcher(string text)
	{
		return GetMatcher(text.ToCharArray());
	}

	public override TrieMatcher<string[]> GetMatcher(char[] text)
	{
		return new PhraseMatcher(this, text);
	}

}
