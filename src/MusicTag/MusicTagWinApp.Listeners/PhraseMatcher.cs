using MusicTagWinApp.Adapter;
using MusicTagWinApp.Writers;

namespace MusicTagWinApp.Listeners;

internal class PhraseMatcher : TrieMatcher<string[]>
{
	public PhraseMatcher(PhraseTrie phraseTrie, char[] text)
		: base(phraseTrie, text)
	{
	}

	public string GetReplacementAt(int index)
	{
		string[] values = MatchedValue();
		if (values != null && index < values.Length)
		{
			return values[index];
		}
		return null;
	}
}
