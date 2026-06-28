using System.Drawing;
using System.Windows.Forms;
using MusicTagWinApp.Common;
using MusicTagWinApp.Stubs;

namespace MusicTagWinApp.Containers;

internal class CheckBoxSubItem : DrawableListViewSubItem
{
	public bool IsChecked
	{
		get => bool.Parse(SortValue);
		set => SortValue = value.ToString();
	}

	public CheckBoxSubItem()
	{
		IsChecked = false;
	}

	public CheckBoxSubItem(bool isChecked)
	{
		IsChecked = isChecked;
	}

	public override int DoDraw(DrawListViewSubItemEventArgs item, int x, CustomColumnHeader column)
	{
		CheckBoxColumnHeader checkBoxColumnHeader = (CheckBoxColumnHeader)column;
		Image image = IsChecked ? checkBoxColumnHeader.CheckedImage : checkBoxColumnHeader.UncheckedImage;
		return DrawCenteredImage(item.Graphics, image, item.Bounds, x);
	}
}

