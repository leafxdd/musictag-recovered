using System.Drawing;
using System.Windows.Forms;
using MusicTagWinApp.Common;
using MusicTagWinApp.Stubs;

namespace MusicTag.Services;

internal class ImageSubItem : DrawableListViewSubItem
{
	public Image IconImage { get; set; }

	public ImageSubItem()
	{
	}

	public ImageSubItem(string text)
	{
		base.Text = text;
	}

	public ImageSubItem(Image image)
	{
		IconImage = image;
	}

	public ImageSubItem(Image image, string sortValue)
	{
		IconImage = image;
		SortValue = sortValue;
	}

	public ImageSubItem(string text, Image image, string sortValue)
	{
		Text = text;
		IconImage = image;
		SortValue = sortValue;
	}

	public override int DoDraw(DrawListViewSubItemEventArgs item, int x, CustomColumnHeader column)
	{
		if (IconImage != null)
		{
			int y = item.Bounds.Y + item.Bounds.Height / 2 - IconImage.Height / 2;
			item.Graphics.DrawImage(IconImage, x, y, IconImage.Width, IconImage.Height);
			x += IconImage.Width + 2;
		}
		return x;
	}
}

