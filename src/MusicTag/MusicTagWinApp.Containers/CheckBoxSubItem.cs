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
		int y = item.Bounds.Y + item.Bounds.Height / 2 - image.Height / 2;
		item.Graphics.DrawImage(image, x, y, image.Width, image.Height);
		return x + image.Width + 2;
	}
}

