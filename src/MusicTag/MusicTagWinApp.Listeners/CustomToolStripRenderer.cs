using System;
using System.Drawing;
using System.Windows.Forms;
using MusicTagWinApp.Instances;

namespace MusicTagWinApp.Listeners;

internal class CustomToolStripRenderer : ToolStripProfessionalRenderer
{
	private static readonly int imageMarginWidth = DatabaseMapper.ScaleByDpi(16f);

	private static readonly int dropDownTextX = imageMarginWidth + 17;

	protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
	{
		Rectangle imageMarginBounds = e.AffectedBounds;
		imageMarginBounds.Width = imageMarginWidth + 9;
		base.OnRenderImageMargin(new ToolStripRenderEventArgs(e.Graphics, e.ToolStrip, imageMarginBounds, e.BackColor));
	}

	protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
	{
		Rectangle imageRectangle = e.ImageRectangle;
		Image checkImage = DatabaseMapper.LoadResourceBitmap("check");
		if (imageRectangle == Rectangle.Empty || checkImage == null)
		{
			base.OnRenderItemCheck(e);
			return;
		}

		Image imageToDraw = checkImage;
		bool shouldDisposeImage = false;
		if (!e.Item.Enabled)
		{
			imageToDraw = ToolStripRenderer.CreateDisabledImage(checkImage);
			shouldDisposeImage = true;
		}

		base.OnRenderItemCheck(new ToolStripItemImageRenderEventArgs(e.Graphics, e.Item, null, e.ImageRectangle));
		e.Graphics.DrawImage(imageToDraw, imageRectangle, new Rectangle(Point.Empty, imageToDraw.Size), GraphicsUnit.Pixel);
		if (shouldDisposeImage)
		{
			imageToDraw.Dispose();
		}
	}

	protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
	{
		Rectangle textRectangle = e.TextRectangle;
		if (e.Item.IsOnDropDown)
		{
			textRectangle.X = dropDownTextX;
		}

		base.OnRenderItemText(new ToolStripItemTextRenderEventArgs(e.Graphics, e.Item, e.Text, textRectangle, e.TextColor, e.TextFont, e.TextFormat));
	}

	protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
	{
		DrawSeparator(e.Graphics, e.Item, new Rectangle(Point.Empty, e.Item.Size), e.Vertical);
	}

	protected override void OnRenderSplitButtonBackground(ToolStripItemRenderEventArgs e)
	{
		base.OnRenderSplitButtonBackground(e);
		if (!(e.Item is ToolStripSplitButton splitButton))
		{
			return;
		}

		Rectangle dropDownButtonBounds = splitButton.DropDownButtonBounds;
		Point center = new Point(
			dropDownButtonBounds.Left + dropDownButtonBounds.Width / 2,
			dropDownButtonBounds.Top + dropDownButtonBounds.Height / 2);
		Point[] arrowPoints =
		{
			new Point(center.X - DatabaseMapper.ScaleByDpi(2f), center.Y - DatabaseMapper.ScaleByDpi(1f)),
			new Point(center.X + DatabaseMapper.ScaleByDpi(3f, roundUp: true), center.Y - DatabaseMapper.ScaleByDpi(1f)),
			new Point(center.X, center.Y + DatabaseMapper.ScaleByDpi(2f, roundUp: true))
		};

		using SolidBrush arrowBrush = new SolidBrush(splitButton.Enabled ? SystemColors.ControlText : SystemColors.ControlDark);
		e.Graphics.FillPolygon(arrowBrush, arrowPoints);
	}

	private void DrawSeparator(Graphics graphics, ToolStripItem item, Rectangle bounds, bool vertical)
	{
		using Pen darkPen = new Pen(ColorTable.SeparatorDark);
		using Pen lightPen = new Pen(ColorTable.SeparatorLight);

		if (vertical)
		{
			if (!item.IsOnDropDown)
			{
				bounds.Y += 3;
				bounds.Height = Math.Max(0, bounds.Height - 6);
			}
			if (bounds.Height >= 4)
			{
				bounds.Inflate(0, -2);
			}
			DrawVerticalSeparator(graphics, item, bounds, darkPen, lightPen);
			return;
		}

		bool drawDoubleLine = !(item is ToolStripSeparator);
		ToolStripDropDownMenu parentDropDown = item.GetCurrentParent() as ToolStripDropDownMenu;
		if (parentDropDown != null)
		{
			if (parentDropDown.RightToLeft == RightToLeft.No)
			{
				bounds.X += dropDownTextX - 2;
				bounds.Width = parentDropDown.Width - bounds.X;
			}
			else
			{
				bounds.X += 2;
				bounds.Width = parentDropDown.Width - bounds.X - parentDropDown.Padding.Right;
			}
		}
		else
		{
			drawDoubleLine = true;
			if (bounds.Width >= 4)
			{
				bounds.Inflate(-2, 0);
			}
		}

		int y = bounds.Height / 2;
		graphics.DrawLine(darkPen, bounds.Left, y, bounds.Right - 1, y);
		if (drawDoubleLine)
		{
			graphics.DrawLine(lightPen, bounds.Left + 1, y + 1, bounds.Right - 1, y + 1);
		}
	}

	private static void DrawVerticalSeparator(Graphics graphics, ToolStripItem item, Rectangle bounds, Pen darkPen, Pen lightPen)
	{
		bool isRightToLeft = item.RightToLeft == RightToLeft.Yes;
		Pen leftPen = isRightToLeft ? lightPen : darkPen;
		Pen rightPen = isRightToLeft ? darkPen : lightPen;
		int x = bounds.Width / 2;
		graphics.DrawLine(leftPen, x, bounds.Top, x, bounds.Bottom - 1);
		graphics.DrawLine(rightPen, x + 1, bounds.Top + 1, x + 1, bounds.Bottom);
	}
}
