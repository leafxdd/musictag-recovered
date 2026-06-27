using System.Text;

namespace MusicTagWinApp.Common;

internal class Page
{
	private readonly StringBuilder messages = new StringBuilder();

	public int LineCount { get; private set; }

	public void AddLine(string message)
	{
		if (LineCount >= 20)
		{
			if (!messages.ToString().EndsWith("..."))
			{
				messages.Append("...");
			}
			return;
		}

		messages.Append(message + "\n");
		LineCount++;
	}

	public override string ToString()
	{
		return messages.ToString();
	}
}
