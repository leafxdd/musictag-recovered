using System.Windows.Forms;

namespace MusicTagWinApp.Common;

internal class CustomColumnHeader : ColumnHeader
{
	public CustomColumnHeader()
	{
	}

	public CustomColumnHeader(string text)
	{
		Text = text;
	}

	public CustomColumnHeader(string text, int width)
	{
		Text = text;
		Width = width;
	}
}
