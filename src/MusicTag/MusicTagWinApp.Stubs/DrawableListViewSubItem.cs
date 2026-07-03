using System.Drawing;
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

	// 居中绘制 image 到 bounds 垂直中线，返回推进后的 x（图宽 + 2）。统一 4 处等价居中绘图。
	public static int DrawCenteredImage(Graphics graphics, Image image, Rectangle bounds, int x)
	{
		int y = bounds.Y + bounds.Height / 2 - image.Height / 2;
		graphics.DrawImage(image, x, y, image.Width, image.Height);
		return x + image.Width + 2;
	}
}
