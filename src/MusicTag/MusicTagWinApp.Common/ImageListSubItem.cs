using System.Collections;
using System.Drawing;
using System.Windows.Forms;
using MusicTagWinApp.Stubs;

namespace MusicTagWinApp.Common;

internal class ImageListSubItem : DrawableListViewSubItem
{
	public ArrayList Images { get; set; }

	public ImageListSubItem()
	{
	}

	public ImageListSubItem(string info)
	{
		base.Text = info;
	}

	public ImageListSubItem(ArrayList images)
	{
		Images = images;
	}

	public ImageListSubItem(ArrayList images, string sortValue)
	{
		Images = images;
		SortValue = sortValue;
	}

	public ImageListSubItem(string text, ArrayList images, string sortValue)
	{
		Text = text;
		Images = images;
		SortValue = sortValue;
	}

	public override int DoDraw(DrawListViewSubItemEventArgs item, int x, CustomColumnHeader column)
	{
		if (Images == null || Images.Count == 0)
		{
			return x;
		}

		foreach (Image image in Images)
		{
			int y = item.Bounds.Y + item.Bounds.Height / 2 - image.Height / 2;
			item.Graphics.DrawImage(image, x, y, image.Width, image.Height);
			x += image.Width + 2;
		}
		return x;
	}
}

