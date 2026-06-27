using System.Windows.Forms;
using MusicTagWinApp.Common;

namespace MusicTagWinApp.Stubs;

internal abstract class DrawableListViewSubItem : ListViewItem.ListViewSubItem
{
	public string SortValue { get; set; } = "";

	public DrawableListViewSubItem()
	{
	}

	public DrawableListViewSubItem(string text)
	{
		Text = text;
	}

	public abstract int DoDraw(DrawListViewSubItemEventArgs item, int x, CustomColumnHeader column);
}
