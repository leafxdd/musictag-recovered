using MusicTag.Serialization;
using MusicTagWinApp.Adapter;

namespace MusicTagWinApp.Listeners;

internal class TrieMatcher<T>
{
	public int MatchStartIndex;

	private int searchStartIndex;

	private int scanIndex;

	private bool hasPendingIntermediateMatch;

	private TrieNode<T> rootNode;

	private char[] textCharacters;

	private string matchedText;

	private int pendingMatchLength;

	private T matchedValue;

	private TrieNode<T> currentNode;

	public TrieMatcher(TrieNode<T> rootNode, string text)
	{
		textCharacters = text.ToCharArray();
		this.rootNode = rootNode;
		currentNode = rootNode;
	}

	public TrieMatcher(TrieNode<T> rootNode, char[] textCharacters)
	{
		this.textCharacters = textCharacters;
		this.rootNode = rootNode;
		currentNode = rootNode;
	}

	private string ApplyAsciiBoundaryFilter(string matchedKey)
	{
		if (matchedKey != null && !(matchedKey == ""))
		{
			char firstCharacter = matchedKey[0];
			if (firstCharacter < '\u007f' && MatchStartIndex > 0 && IsSameAsciiTokenClass(firstCharacter, textCharacters[MatchStartIndex - 1]))
			{
				return "";
			}
			char lastCharacter = firstCharacter;
			if (matchedKey.Length > 1)
			{
				lastCharacter = matchedKey[matchedKey.Length - 1];
			}
			if (lastCharacter < '\u007f' && MatchStartIndex + matchedKey.Length < textCharacters.Length && IsSameAsciiTokenClass(lastCharacter, textCharacters[MatchStartIndex + matchedKey.Length]))
			{
				return "";
			}
			return matchedKey;
		}
		return matchedKey;
	}

	private bool IsSameAsciiTokenClass(char first, char second)
	{
		if (AsciiTokenClassifier.IsAsciiLetterLike(first) && AsciiTokenClassifier.IsAsciiLetterLike(second))
		{
			return true;
		}
		if (AsciiTokenClassifier.IsAsciiDigitLike(first) && AsciiTokenClassifier.IsAsciiDigitLike(second))
		{
			return true;
		}
		return false;
	}

	public string NextMatch()
	{
		string key;
		do
		{
			key = FindNextMatch();
			key = ApplyAsciiBoundaryFilter(key);
		}
		while ("".Equals(key));
		return key;
	}

	private string FindNextMatch()
	{
		while (true)
		{
			if (scanIndex < textCharacters.Length + 1)
			{
				if (scanIndex == textCharacters.Length)
				{
					currentNode = null;
				}
				else
				{
					currentNode = currentNode.GetChild(textCharacters[scanIndex]);
				}
				if (currentNode == null)
				{
					currentNode = rootNode;
					if (hasPendingIntermediateMatch)
					{
						break;
					}
					scanIndex = searchStartIndex;
					searchStartIndex++;
				}
				else
				{
					switch (currentNode.Kind)
					{
					case TrieNodeKind.IntermediateWord:
						hasPendingIntermediateMatch = true;
						pendingMatchLength = scanIndex - searchStartIndex + 1;
						matchedValue = currentNode.Value;
						break;
					case TrieNodeKind.Word:
					{
						MatchStartIndex = searchStartIndex;
						matchedText = new string(textCharacters, searchStartIndex, scanIndex - searchStartIndex + 1);
						string currentMatchedText = matchedText;
						matchedValue = currentNode.Value;
						currentNode = rootNode;
						hasPendingIntermediateMatch = false;
						if (currentMatchedText.Length > 0)
						{
							scanIndex++;
							searchStartIndex = scanIndex;
						}
						else
						{
							scanIndex = searchStartIndex + 1;
						}
						return matchedText;
					}
					}
				}
				scanIndex++;
				continue;
			}
			pendingMatchLength += textCharacters.Length;
			return null;
		}
		MatchStartIndex = searchStartIndex;
		matchedText = new string(textCharacters, searchStartIndex, pendingMatchLength);
		if (matchedText.Length == 0)
		{
			searchStartIndex++;
			scanIndex = searchStartIndex;
		}
		else
		{
			scanIndex = searchStartIndex + pendingMatchLength;
			searchStartIndex = scanIndex;
		}
		hasPendingIntermediateMatch = false;
		return matchedText;
	}

	public T MatchedValue()
	{
		return matchedValue;
	}
}
