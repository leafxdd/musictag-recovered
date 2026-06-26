using System.Collections;
using System.Drawing;
using System.Windows.Forms;

namespace MusicTag.Bridges;

internal class ImageComboBox : ComboBox
{
	public class ImageComboBoxItem
	{
		public ImageComboBoxItem()
		{
			DisplayText = string.Empty;
			SortValue = string.Empty;
		}

		public ImageComboBoxItem(string displayText)
			: this()
		{
			DisplayText = displayText;
		}

		public string DisplayText { get; set; }

		public string SortValue { get; set; }

		public override string ToString()
		{
			return DisplayText;
		}
	}

	public class SingleImageItem : ImageComboBoxItem
	{
		public SingleImageItem()
		{
		}

		public SingleImageItem(string displayText)
			: base(displayText)
		{
		}

		public SingleImageItem(Image image)
		{
			Image = image;
		}

		public SingleImageItem(string displayText, Image image)
			: base(displayText)
		{
			Image = image;
		}

		public SingleImageItem(Image image, string sortValue)
		{
			Image = image;
			SortValue = sortValue;
		}

		public SingleImageItem(string displayText, Image image, string sortValue)
			: base(displayText)
		{
			Image = image;
			SortValue = sortValue;
		}

		public Image Image { get; set; }
	}

	public class ImageListItem : ImageComboBoxItem
	{
		public ImageListItem()
		{
		}

		public ImageListItem(string displayText)
			: base(displayText)
		{
		}

		public ImageListItem(ArrayList images)
		{
			Images = images;
		}

		public ImageListItem(string displayText, ArrayList images)
			: base(displayText)
		{
			Images = images;
		}

		public ImageListItem(ArrayList images, string sortValue)
		{
			Images = images;
			SortValue = sortValue;
		}

		public ImageListItem(string displayText, ArrayList images, string sortValue)
			: base(displayText)
		{
			Images = images;
			SortValue = sortValue;
		}

		public ArrayList Images { get; set; }
	}

	private Brush selectionBackBrush;

	public ImageComboBox()
	{
		selectionBackBrush = SystemBrushes.Highlight;
		DrawMode = DrawMode.OwnerDrawFixed;
		DrawItem += DrawComboBoxItem;
	}

	private void DrawComboBoxItem(object sender, DrawItemEventArgs e)
	{
		if (e.Index == -1)
		{
			return;
		}

		e.DrawBackground();
		if ((e.State & DrawItemState.Selected) != DrawItemState.None)
		{
			e.Graphics.FillRectangle(selectionBackBrush, e.Bounds);
		}

		ImageComboBoxItem item = (ImageComboBoxItem)Items[e.Index];
		int nextX = e.Bounds.X + 2;

		if (item is SingleImageItem singleImageItem)
		{
			nextX = DrawImage(e, singleImageItem.Image, nextX);
		}
		else if (item is ImageListItem imageListItem && imageListItem.Images != null)
		{
			foreach (Image image in imageListItem.Images)
			{
				nextX = DrawImage(e, image, nextX);
			}
		}

		int textWidth = e.Bounds.Right - nextX;
		if (textWidth > 0)
		{
			TextRenderer.DrawText(e.Graphics, item.DisplayText, e.Font, new Rectangle(nextX, e.Bounds.Y, textWidth, e.Bounds.Height), e.ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
		}
		e.DrawFocusRectangle();
	}

	private static int DrawImage(DrawItemEventArgs e, Image image, int x)
	{
		if (image == null)
		{
			return x;
		}

		int y = e.Bounds.Y + e.Bounds.Height / 2 - image.Height / 2 + 1;
		e.Graphics.DrawImage(image, x, y, image.Width, image.Height);
		return x + image.Width + 2;
	}
}
