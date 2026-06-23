using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;
using MusicTagWinApp.Instances;

namespace wyDay.Controls;

public class SplitButton : Button
{
	private PushButtonState buttonState;

	private static readonly int SplitSectionWidth = DatabaseMapper.ScaleByDpi(18f);

	private static int borderInset;

	private bool suppressNextMenuOpen;

	private Rectangle splitBounds;

	private bool showSplit;

	private bool menuVisible;

	private ContextMenuStrip splitMenuStrip;

	private ContextMenu splitMenu;

	private TextFormatFlags textFormatFlags;

	private bool mouseOverButton;

	[Browsable(false)]
	public override ContextMenuStrip ContextMenuStrip
	{
		get
		{
			return SplitMenuStrip;
		}
		set
		{
			SplitMenuStrip = value;
		}
	}

	[DefaultValue(null)]
	public ContextMenu SplitMenu
	{
		get
		{
			return splitMenu;
		}
		set
		{
			if (splitMenu != null)
			{
				splitMenu.Popup -= MarkLegacyMenuOpening;
			}
			if (value == null)
			{
				ShowSplit = false;
			}
			else
			{
				ShowSplit = true;
				value.Popup += MarkLegacyMenuOpening;
			}
			splitMenu = value;
		}
	}

	[DefaultValue(null)]
	public ContextMenuStrip SplitMenuStrip
	{
		get
		{
			return splitMenuStrip;
		}
		set
		{
			if (splitMenuStrip != null)
			{
				splitMenuStrip.Closing -= HandleMenuClosing;
				splitMenuStrip.Opening -= MarkMenuOpening;
			}
			if (value != null)
			{
				ShowSplit = true;
				value.Closing += HandleMenuClosing;
				value.Opening += MarkMenuOpening;
			}
			else
			{
				ShowSplit = false;
			}
			splitMenuStrip = value;
		}
	}

	[DefaultValue(false)]
	public bool ShowSplit
	{
		set
		{
			if (value == showSplit)
			{
				return;
			}
			showSplit = value;
			Invalidate();
			if (base.Parent == null)
			{
				return;
			}
			base.Parent.PerformLayout();
		}
	}

	private PushButtonState GetButtonState()
	{
		return buttonState;
	}

	private void SetButtonState(PushButtonState value)
	{
		if (!buttonState.Equals(value))
		{
			buttonState = value;
			Invalidate();
		}
	}

	protected override bool IsInputKey(Keys keyData)
	{
		if (keyData.Equals(Keys.Down) && showSplit)
		{
			return true;
		}
		return base.IsInputKey(keyData);
	}

	protected override void OnGotFocus(EventArgs e)
	{
		if (!showSplit)
		{
			base.OnGotFocus(e);
			return;
		}
		if (GetButtonState().Equals(PushButtonState.Pressed) || GetButtonState().Equals(PushButtonState.Disabled))
		{
			return;
		}
		SetButtonState(PushButtonState.Default);
	}

	protected override void OnKeyDown(KeyEventArgs kevent)
	{
		if (showSplit)
		{
			if (kevent.KeyCode.Equals(Keys.Down) && !menuVisible)
			{
				ShowDropdown();
			}
			else if (kevent.KeyCode.Equals(Keys.Space) && kevent.Modifiers == Keys.None)
			{
				SetButtonState(PushButtonState.Pressed);
			}
		}
		base.OnKeyDown(kevent);
	}

	protected override void OnKeyUp(KeyEventArgs kevent)
	{
		if (kevent.KeyCode.Equals(Keys.Space))
		{
			if (Control.MouseButtons == MouseButtons.None)
			{
				SetButtonState(PushButtonState.Normal);
			}
		}
		else if (kevent.KeyCode.Equals(Keys.Apps) && Control.MouseButtons == MouseButtons.None && !menuVisible)
		{
			ShowDropdown();
		}
		base.OnKeyUp(kevent);
	}

	protected override void OnEnabledChanged(EventArgs e)
	{
		SetButtonState(base.Enabled ? PushButtonState.Normal : PushButtonState.Disabled);
		base.OnEnabledChanged(e);
	}

	protected override void OnLostFocus(EventArgs e)
	{
		if (!showSplit)
		{
			base.OnLostFocus(e);
			return;
		}
		if (GetButtonState().Equals(PushButtonState.Pressed))
		{
			return;
		}
		if (!GetButtonState().Equals(PushButtonState.Disabled))
		{
			SetButtonState(PushButtonState.Normal);
		}
	}

	protected override void OnMouseEnter(EventArgs e)
	{
		if (showSplit)
		{
			mouseOverButton = true;
			if (!GetButtonState().Equals(PushButtonState.Pressed) && !GetButtonState().Equals(PushButtonState.Disabled))
			{
				SetButtonState(PushButtonState.Hot);
			}
			return;
		}
		base.OnMouseEnter(e);
	}

	protected override void OnMouseLeave(EventArgs e)
	{
		PushButtonState pushButtonState = default(PushButtonState);
		if (!showSplit)
		{
			base.OnMouseLeave(e);
			return;
		}
		mouseOverButton = false;
		pushButtonState = GetButtonState();
		if (!pushButtonState.Equals(PushButtonState.Pressed) && !GetButtonState().Equals(PushButtonState.Disabled))
		{
			SetButtonState((!Focused) ? PushButtonState.Normal : PushButtonState.Default);
		}
	}

	protected override void OnMouseDown(MouseEventArgs e)
	{
		if (!showSplit)
		{
			base.OnMouseDown(e);
			return;
		}
		if (splitMenu != null && e.Button == MouseButtons.Left && !mouseOverButton)
		{
			suppressNextMenuOpen = true;
		}
		if (splitBounds.Contains(e.Location) && !menuVisible && e.Button == MouseButtons.Left)
		{
			ShowDropdown();
			return;
		}
		SetButtonState(PushButtonState.Pressed);
	}

	protected override void OnMouseUp(MouseEventArgs mevent)
	{
		if (!showSplit)
		{
			base.OnMouseUp(mevent);
			return;
		}
		if (mevent.Button == MouseButtons.Right && ClientRectangle.Contains(mevent.Location) && !menuVisible)
		{
			ShowDropdown();
			return;
		}
		if ((splitMenuStrip == null && splitMenu == null) || !menuVisible)
		{
			UpdateButtonStateAfterMenuClose();
			if (base.ClientRectangle.Contains(mevent.Location) && !splitBounds.Contains(mevent.Location))
			{
				OnClick(new EventArgs());
			}
		}
	}

	protected override void OnPaint(PaintEventArgs pevent)
	{
		base.OnPaint(pevent);
		if (!showSplit)
		{
			return;
		}
		Graphics graphics = pevent.Graphics;
		Rectangle clientRectangle = base.ClientRectangle;
		if (GetButtonState() != PushButtonState.Pressed && base.IsDefault && !Application.RenderWithVisualStyles)
		{
			Rectangle bounds = clientRectangle;
			bounds.Inflate(-1, -1);
			ButtonRenderer.DrawButton(graphics, bounds, GetButtonState());
			graphics.DrawRectangle(SystemPens.WindowFrame, 0, 0, clientRectangle.Width - 1, clientRectangle.Height - 1);
		}
		else
		{
			ButtonRenderer.DrawButton(graphics, clientRectangle, GetButtonState());
		}
		splitBounds = new Rectangle(clientRectangle.Right - SplitSectionWidth, 0, SplitSectionWidth, clientRectangle.Height);
		int inset = borderInset;
		Rectangle rectangle = new Rectangle(inset - 1, inset - 1, clientRectangle.Width - splitBounds.Width - inset, clientRectangle.Height - inset * 2 + 2);
		bool flag = GetButtonState() == PushButtonState.Hot || GetButtonState() == PushButtonState.Pressed || !Application.RenderWithVisualStyles;
		if (RightToLeft == RightToLeft.Yes)
		{
			splitBounds.X = clientRectangle.Left + 1;
			rectangle.X = splitBounds.Right;
			if (flag)
			{
				graphics.DrawLine(SystemPens.ButtonShadow, clientRectangle.Left + SplitSectionWidth, borderInset, clientRectangle.Left + SplitSectionWidth, clientRectangle.Bottom - borderInset);
				graphics.DrawLine(SystemPens.ButtonFace, clientRectangle.Left + SplitSectionWidth + 1, borderInset, clientRectangle.Left + SplitSectionWidth + 1, clientRectangle.Bottom - borderInset);
			}
		}
		else if (flag)
		{
			graphics.DrawLine(SystemPens.ButtonShadow, clientRectangle.Right - SplitSectionWidth, borderInset, clientRectangle.Right - SplitSectionWidth, clientRectangle.Bottom - borderInset);
			graphics.DrawLine(SystemPens.ButtonFace, clientRectangle.Right - SplitSectionWidth - 1, borderInset, clientRectangle.Right - SplitSectionWidth - 1, clientRectangle.Bottom - borderInset);
		}
		DrawDropdownArrow(graphics, splitBounds);
		DrawTextAndImage(graphics, new Rectangle(0, 0, base.ClientRectangle.Width - SplitSectionWidth, ClientRectangle.Height));
		if (GetButtonState() != PushButtonState.Pressed && Focused && ShowFocusCues)
		{
			ControlPaint.DrawFocusRectangle(graphics, rectangle);
		}
	}

	private void DrawTextAndImage(Graphics graphics, Rectangle bounds)
	{
		Rectangle textBounds = default(Rectangle);
		Rectangle imageBounds = default(Rectangle);
		CalculateTextAndImageBounds(ref bounds, out textBounds, out imageBounds);
		if (base.Image != null)
		{
			if (base.Enabled)
			{
				graphics.DrawImage(base.Image, imageBounds.X, imageBounds.Y, base.Image.Width, base.Image.Height);
			}
			else
			{
				ControlPaint.DrawImageDisabled(graphics, base.Image, imageBounds.X, imageBounds.Y, BackColor);
			}
		}
		if (!base.UseMnemonic)
		{
			textFormatFlags |= TextFormatFlags.NoPrefix;
		}
		else if (!ShowKeyboardCues)
		{
			textFormatFlags |= TextFormatFlags.HidePrefix;
		}
		if (!string.IsNullOrEmpty(Text))
		{
			if (base.Enabled)
			{
				TextRenderer.DrawText(graphics, Text, Font, textBounds, ForeColor, textFormatFlags);
			}
			else
			{
				ControlPaint.DrawStringDisabled(graphics, Text, Font, BackColor, textBounds, textFormatFlags);
			}
		}
	}

	private void DrawDropdownArrow(Graphics graphics, Rectangle arrowBounds)
	{
		Point point = new Point(Convert.ToInt32(arrowBounds.Left + arrowBounds.Width / 2), Convert.ToInt32(arrowBounds.Top + arrowBounds.Height / 2));
		point.X += arrowBounds.Width % 2;
		Point[] array = new Point[3]
		{
			new Point(point.X - DatabaseMapper.ScaleByDpi(2f), point.Y - DatabaseMapper.ScaleByDpi(1f)),
			new Point(point.X + DatabaseMapper.ScaleByDpi(3f, roundUp: true), point.Y - DatabaseMapper.ScaleByDpi(1f)),
			new Point(point.X, point.Y + DatabaseMapper.ScaleByDpi(2f, roundUp: true))
		};
		if (!base.Enabled)
		{
			graphics.FillPolygon(SystemBrushes.ButtonShadow, array);
			return;
		}
		graphics.FillPolygon(SystemBrushes.ControlText, array);
	}

	public override Size GetPreferredSize(Size proposedSize)
	{
		Size preferredSize = base.GetPreferredSize(proposedSize);
		if (showSplit)
		{
			if (AutoSize)
			{
				return CalculateAutoSize();
			}
			if (!string.IsNullOrEmpty(Text) && TextRenderer.MeasureText(Text, Font).Width + SplitSectionWidth > preferredSize.Width)
			{
				return preferredSize + new Size(SplitSectionWidth + borderInset * 2, 0);
			}
		}
		return preferredSize;
	}

	private Size CalculateAutoSize()
	{
		Size textSize = TextRenderer.MeasureText(Text, Font);
		Size imageSize = base.Image == null ? Size.Empty : base.Image.Size;
		if (Text.Length != 0)
		{
			textSize.Height += 4;
			textSize.Width += 4;
		}
		Size preferredSize;
		switch (base.TextImageRelation)
		{
		case TextImageRelation.ImageBeforeText:
		case TextImageRelation.TextBeforeImage:
			preferredSize = new Size(textSize.Width + imageSize.Width, Math.Max(textSize.Height, imageSize.Height));
			break;
		case TextImageRelation.ImageAboveText:
		case TextImageRelation.TextAboveImage:
			preferredSize = new Size(Math.Max(textSize.Width, imageSize.Width), textSize.Height + imageSize.Height);
			break;
		case TextImageRelation.Overlay:
			preferredSize = new Size(Math.Max(textSize.Width, imageSize.Width), Math.Max(Text.Length != 0 ? textSize.Height : 0, imageSize.Height));
			break;
		default:
			preferredSize = Size.Empty;
			break;
		}
		preferredSize.Height += base.Padding.Vertical + 6;
		preferredSize.Width += base.Padding.Horizontal + 6;
		if (showSplit)
		{
			preferredSize.Width += SplitSectionWidth;
		}
		return preferredSize;
	}

	private void CalculateTextAndImageBounds(ref Rectangle value, out Rectangle textBounds, out Rectangle imageBounds)
	{
		textBounds = Rectangle.Empty;
		imageBounds = Rectangle.Empty;
		Size textSize = TextRenderer.MeasureText(Text, Font, value.Size, textFormatFlags);
		Size imageSize = base.Image == null ? Size.Empty : base.Image.Size;
		switch (base.TextImageRelation)
		{
		default:
			return;
		case TextImageRelation.Overlay:
			textBounds = AlignWithMargin(ref value, ref textSize, TextAlign);
			if (buttonState == PushButtonState.Pressed && !Application.RenderWithVisualStyles)
			{
				textBounds.Offset(1, 1);
			}
			if (base.Image != null)
			{
				imageBounds = AlignWithMargin(ref value, ref imageSize, base.ImageAlign);
			}
			return;
		case TextImageRelation.ImageAboveText:
			value.Inflate(-4, -4);
			LayoutVerticalTextImage(value, textFirst: false, textSize, imageSize, out textBounds, out imageBounds);
			return;
		case TextImageRelation.TextAboveImage:
			value.Inflate(-4, -4);
			LayoutVerticalTextImage(value, textFirst: true, textSize, imageSize, out textBounds, out imageBounds);
			return;
		case TextImageRelation.ImageBeforeText:
			value.Inflate(-4, -4);
			LayoutHorizontalTextImage(value, textFirst: false, textSize, imageSize, out textBounds, out imageBounds);
			return;
		case TextImageRelation.TextBeforeImage:
			value.Inflate(-4, -4);
			LayoutHorizontalTextImage(value, textFirst: true, textSize, imageSize, out textBounds, out imageBounds);
			return;
		}
	}

	private static Rectangle AlignWithMargin(ref Rectangle container, ref Size contentSize, System.Drawing.ContentAlignment alignment)
	{
		int x;
		int y;
		switch (alignment)
		{
		case System.Drawing.ContentAlignment.TopLeft:
			x = 4;
			y = 4;
			break;
		case System.Drawing.ContentAlignment.TopCenter:
			x = (container.Width - contentSize.Width) / 2;
			y = 4;
			break;
		case System.Drawing.ContentAlignment.TopRight:
			x = container.Width - contentSize.Width - 4;
			y = 4;
			break;
		case System.Drawing.ContentAlignment.MiddleLeft:
			x = 4;
			y = (container.Height - contentSize.Height) / 2;
			break;
		case System.Drawing.ContentAlignment.MiddleCenter:
			x = (container.Width - contentSize.Width) / 2;
			y = (container.Height - contentSize.Height) / 2;
			break;
		case System.Drawing.ContentAlignment.MiddleRight:
			x = container.Width - contentSize.Width - 4;
			y = (container.Height - contentSize.Height) / 2;
			break;
		case System.Drawing.ContentAlignment.BottomLeft:
			x = 4;
			y = container.Height - contentSize.Height - 4;
			break;
		case System.Drawing.ContentAlignment.BottomCenter:
			x = (container.Width - contentSize.Width) / 2;
			y = container.Height - contentSize.Height - 4;
			break;
		case System.Drawing.ContentAlignment.BottomRight:
			x = container.Width - contentSize.Width - 4;
			y = container.Height - contentSize.Height - 4;
			break;
		default:
			x = 4;
			y = 4;
			break;
		}
		return new Rectangle(x, y, contentSize.Width, contentSize.Height);
	}

	private void LayoutHorizontalTextImage(Rectangle bounds, bool textFirst, Size textSize, Size imageSize, out Rectangle textBounds, out Rectangle imageBounds)
	{
		int spacing = 0;
		int contentWidth = textSize.Width + spacing + imageSize.Width;
		if (!textFirst)
		{
			spacing += 2;
		}
		if (contentWidth > bounds.Width)
		{
			textSize.Width = bounds.Width - spacing - imageSize.Width;
			contentWidth = bounds.Width;
		}
		int extraWidth = bounds.Width - contentWidth;
		HorizontalAlignment textAlignment = GetHorizontalAlignment(TextAlign);
		HorizontalAlignment imageAlignment = GetHorizontalAlignment(base.ImageAlign);
		int leftOffset = imageAlignment switch
		{
			HorizontalAlignment.Left => 0,
			HorizontalAlignment.Right when textAlignment == HorizontalAlignment.Right => extraWidth,
			HorizontalAlignment.Center when textAlignment == HorizontalAlignment.Left || textAlignment == HorizontalAlignment.Center => extraWidth / 3,
			_ => 2 * (extraWidth / 3)
		};
		if (textFirst)
		{
			textBounds = new Rectangle(bounds.Left + leftOffset, AlignWithin(bounds, textSize, TextAlign).Top, textSize.Width, textSize.Height);
			imageBounds = new Rectangle(textBounds.Right + spacing, AlignWithin(bounds, imageSize, base.ImageAlign).Top, imageSize.Width, imageSize.Height);
		}
		else
		{
			imageBounds = new Rectangle(bounds.Left + leftOffset, AlignWithin(bounds, imageSize, base.ImageAlign).Top, imageSize.Width, imageSize.Height);
			textBounds = new Rectangle(imageBounds.Right + spacing, AlignWithin(bounds, textSize, TextAlign).Top, textSize.Width, textSize.Height);
		}
	}

	private void LayoutVerticalTextImage(Rectangle bounds, bool textFirst, Size textSize, Size imageSize, out Rectangle textBounds, out Rectangle imageBounds)
	{
		int spacing = 0;
		int contentHeight = textSize.Height + spacing + imageSize.Height;
		if (textFirst)
		{
			spacing += 2;
		}
		if (textSize.Width > bounds.Width)
		{
			textSize.Width = bounds.Width;
		}
		if (contentHeight > bounds.Height && textFirst)
		{
			imageSize = Size.Empty;
			contentHeight = bounds.Height;
		}
		int extraHeight = bounds.Height - contentHeight;
		VerticalAlignment textAlignment = GetVerticalAlignment(TextAlign);
		VerticalAlignment imageAlignment = GetVerticalAlignment(base.ImageAlign);
		int topOffset = imageAlignment switch
		{
			VerticalAlignment.Top => 0,
			VerticalAlignment.Bottom when textAlignment == VerticalAlignment.Bottom => extraHeight,
			VerticalAlignment.Center when textAlignment == VerticalAlignment.Top || textAlignment == VerticalAlignment.Center => extraHeight / 3,
			_ => 2 * (extraHeight / 3)
		};
		if (!textFirst)
		{
			imageBounds = new Rectangle(AlignWithin(bounds, imageSize, base.ImageAlign).Left, bounds.Top + topOffset, imageSize.Width, imageSize.Height);
			textBounds = new Rectangle(AlignWithin(bounds, textSize, TextAlign).Left, imageBounds.Bottom + spacing, textSize.Width, textSize.Height);
			if (textBounds.Bottom > bounds.Bottom)
			{
				textBounds.Y = bounds.Top;
			}
			return;
		}
		textBounds = new Rectangle(AlignWithin(bounds, textSize, TextAlign).Left, bounds.Top + topOffset, textSize.Width, textSize.Height);
		imageBounds = new Rectangle(AlignWithin(bounds, imageSize, base.ImageAlign).Left, textBounds.Bottom + spacing, imageSize.Width, imageSize.Height);
	}

	private static HorizontalAlignment GetHorizontalAlignment(System.Drawing.ContentAlignment reference)
	{
		switch (reference)
		{
		case System.Drawing.ContentAlignment.TopRight:
		case System.Drawing.ContentAlignment.MiddleRight:
		case System.Drawing.ContentAlignment.BottomRight:
			return HorizontalAlignment.Right;
		case System.Drawing.ContentAlignment.TopCenter:
		case System.Drawing.ContentAlignment.MiddleCenter:
		case System.Drawing.ContentAlignment.BottomCenter:
			return HorizontalAlignment.Center;
		default:
			return HorizontalAlignment.Left;
		}
	}

	private static VerticalAlignment GetVerticalAlignment(System.Drawing.ContentAlignment contentAlignment)
	{
		switch (contentAlignment)
		{
		case System.Drawing.ContentAlignment.MiddleLeft:
		case System.Drawing.ContentAlignment.MiddleCenter:
		case System.Drawing.ContentAlignment.MiddleRight:
			return VerticalAlignment.Center;
		case System.Drawing.ContentAlignment.BottomLeft:
		case System.Drawing.ContentAlignment.BottomCenter:
		case System.Drawing.ContentAlignment.BottomRight:
			return VerticalAlignment.Bottom;
		default:
			return VerticalAlignment.Top;
		}
	}

	internal static Rectangle AlignWithin(Rectangle bounds, Size contentSize, System.Drawing.ContentAlignment contentAlignment)
	{
		int x;
		if (contentAlignment == System.Drawing.ContentAlignment.BottomLeft || contentAlignment == System.Drawing.ContentAlignment.MiddleLeft || contentAlignment == System.Drawing.ContentAlignment.TopLeft)
		{
			x = bounds.X;
		}
		else if (contentAlignment == System.Drawing.ContentAlignment.BottomCenter || contentAlignment == System.Drawing.ContentAlignment.MiddleCenter || contentAlignment == System.Drawing.ContentAlignment.TopCenter)
		{
			x = Math.Max(bounds.X + (bounds.Width - contentSize.Width) / 2, bounds.Left);
		}
		else if (contentAlignment == System.Drawing.ContentAlignment.BottomRight || contentAlignment == System.Drawing.ContentAlignment.MiddleRight || contentAlignment == System.Drawing.ContentAlignment.TopRight)
		{
			x = bounds.Right - contentSize.Width;
		}
		else
		{
			x = bounds.X;
		}
		int y;
		if (contentAlignment == System.Drawing.ContentAlignment.TopCenter || contentAlignment == System.Drawing.ContentAlignment.TopLeft || contentAlignment == System.Drawing.ContentAlignment.TopRight)
		{
			y = bounds.Y;
		}
		else if (contentAlignment == System.Drawing.ContentAlignment.MiddleCenter || contentAlignment == System.Drawing.ContentAlignment.MiddleLeft || contentAlignment == System.Drawing.ContentAlignment.MiddleRight)
		{
			y = bounds.Y + (bounds.Height - contentSize.Height) / 2;
		}
		else if (contentAlignment == System.Drawing.ContentAlignment.BottomCenter || contentAlignment == System.Drawing.ContentAlignment.BottomRight || contentAlignment == System.Drawing.ContentAlignment.BottomLeft)
		{
			y = bounds.Bottom - contentSize.Height;
		}
		else
		{
			y = bounds.Y;
		}
		return new Rectangle(x, y, Math.Min(contentSize.Width, bounds.Width), Math.Min(contentSize.Height, bounds.Height));
	}

	private void ShowDropdown()
	{
		if (!suppressNextMenuOpen)
		{
			SetButtonState(PushButtonState.Pressed);
			if (splitMenu != null)
			{
				splitMenu.Show(this, new Point(0, base.Height));
			}
			else if (splitMenuStrip != null)
			{
				splitMenuStrip.Show(this, new Point(0, base.Height), ToolStripDropDownDirection.BelowRight);
			}
		}
		else
		{
			suppressNextMenuOpen = false;
		}
	}

	private void MarkMenuOpening(object sender, CancelEventArgs e)
	{
		menuVisible = true;
	}

	private void HandleMenuClosing(object sender, ToolStripDropDownClosingEventArgs e)
	{
		menuVisible = false;
		UpdateButtonStateAfterMenuClose();
		if (e.CloseReason != ToolStripDropDownCloseReason.AppClicked)
		{
			return;
		}
		suppressNextMenuOpen = splitBounds.Contains(PointToClient(Cursor.Position)) && Control.MouseButtons == MouseButtons.Left;
	}

	private void MarkLegacyMenuOpening(object sender, EventArgs e)
	{
		menuVisible = true;
	}

	protected override void WndProc(ref Message m)
	{
		if (m.Msg == 530)
		{
			menuVisible = false;
			UpdateButtonStateAfterMenuClose();
		}
		base.WndProc(ref m);
	}

	private void UpdateButtonStateAfterMenuClose()
	{
		if (base.Parent != null && base.Bounds.Contains(base.Parent.PointToClient(Cursor.Position)))
		{
			SetButtonState(PushButtonState.Hot);
		}
		else if (Focused)
		{
			SetButtonState(PushButtonState.Default);
		}
		else if (!base.Enabled)
		{
			SetButtonState(PushButtonState.Disabled);
		}
		else
		{
			SetButtonState(PushButtonState.Normal);
		}
	}

	static SplitButton()
	{
		borderInset = SystemInformation.Border3DSize.Width * 2;
	}

}
