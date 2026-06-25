using System.Windows.Forms;
using MusicTagWinApp.Common;
using MusicTagWinApp.Stubs;

namespace MusicTagWinApp.Listeners;

internal class EmbeddedControlSubItem : DrawableListViewSubItem
{
	public Control EmbeddedControl { get; set; }

	public override int DoDraw(DrawListViewSubItemEventArgs item, int x, CustomColumnHeader column)
	{
		return x;
	}
}
