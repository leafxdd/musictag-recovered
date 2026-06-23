using System.Windows.Forms;
using MusicTagWinApp.Common;
using MusicTagWinApp.Stubs;

namespace MusicTag.Bridges;

internal class PlainTextSubItem : DrawableListViewSubItem
{
	public PlainTextSubItem()
	{
	}

	public PlainTextSubItem(string text)
	{
		Text = text;
	}

	public override int DoDraw(DrawListViewSubItemEventArgs item, int x, CustomColumnHeader column)
	{
		return x;
	}
}
