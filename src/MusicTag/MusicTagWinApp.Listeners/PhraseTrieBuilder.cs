using MusicTag.Candidates;
using MusicTagWinApp.Adapter;
using MusicTagWinApp.Writers;

namespace MusicTagWinApp.Listeners;

internal class PhraseTrieBuilder
{
	public static void AddMapping(PhraseTrie trie, PhraseMapping mapping)
	{
		AddWord(trie, mapping.Phrase, mapping.Replacements);
	}

	private static void AddWord(PhraseTrie trie, string word, params string[] values)
	{
		TrieNode<string[]> currentNode = trie;
		char[] characters = word.ToCharArray();
		for (int index = 0; index < characters.Length; index++)
		{
			TrieNodeKind nodeKind = characters.Length == index + 1 ? TrieNodeKind.Word : TrieNodeKind.Prefix;
			string[] nodeValue = nodeKind == TrieNodeKind.Word ? values : null;
			currentNode.AddChild(new PhraseTrie(characters[index], (int)nodeKind, nodeValue));
			currentNode = currentNode.GetChild(characters[index]);
		}
	}
}
