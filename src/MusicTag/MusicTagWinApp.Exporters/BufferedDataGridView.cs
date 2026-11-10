using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;
using MusicTagWinApp.Containers;

namespace MusicTagWinApp.Exporters;

// 文件列表使用的 DataGridView:构造即开启 protected 的 DoubleBuffered —— 这是 DGV 横向/纵向
// 滚动顺滑的关键前提(对照 MusicTag.Composer/DoubleBufferedSplitContainer 的派生双缓冲先例)。
// 横向滚动卡顿("文字一行一行刷出来")是 WinForms ListView 的引擎弱项,迁到双缓冲 DataGridView
// 根治 —— 实测可跑满显示器刷新率。
internal sealed class BufferedDataGridView : DataGridView
{
	private bool suppressLeftDragSelection;

	// 首列("图标 + 文字"由宿主 CellPainting 自绘)的图标占位宽度。就地重命名时
	// IndentedEditTextBoxCell 按此把编辑框整体右移,让图标继续露在编辑框左侧 ——
	// 复刻原 ListView LabelEdit 的外观(编辑框只盖住文件名,不盖图标)。0 = 不缩进。
	public int FirstColumnEditIndent { get; set; }

	public BufferedDataGridView()
	{
		DoubleBuffered = true;
	}

	protected override void OnMouseDown(MouseEventArgs e)
	{
		DataGridView.HitTestInfo hitInfo = HitTest(e.X, e.Y);
		suppressLeftDragSelection = e.Button == MouseButtons.Left && hitInfo.RowIndex >= 0;
		base.OnMouseDown(e);
	}

	protected override void OnMouseMove(MouseEventArgs e)
	{
		if (suppressLeftDragSelection && e.Button == MouseButtons.Left)
		{
			return;
		}
		base.OnMouseMove(e);
	}

	protected override void OnMouseUp(MouseEventArgs e)
	{
		suppressLeftDragSelection = false;
		base.OnMouseUp(e);
	}

	protected override bool ProcessDataGridViewKey(KeyEventArgs e)
	{
		if (e.KeyCode == Keys.Enter && TryCommitEditWithoutMovingCurrentCell())
		{
			return true;
		}
		return base.ProcessDataGridViewKey(e);
	}

	protected override bool ProcessDialogKey(Keys keyData)
	{
		if ((keyData & Keys.KeyCode) == Keys.Enter && TryCommitEditWithoutMovingCurrentCell())
		{
			return true;
		}
		return base.ProcessDialogKey(keyData);
	}

	// 回车提交就地重命名后,DGV 默认会把当前单元格下移一行;原 ListView 的 LabelEdit
	// 提交后选中留在原地。编辑态下的回车改成"只提交,不移动"。
	// 两个入口按编辑控件是否吃下该键只会命中其一,另一个因 IsCurrentCellInEditMode
	// 已为 false 自动落空,不会重复提交。非编辑态的回车完全不受影响。
	private bool TryCommitEditWithoutMovingCurrentCell()
	{
		if (!IsCurrentCellInEditMode)
		{
			return false;
		}
		EndEdit();
		return true;
	}

	// uxtheme HEADER 类的 HP_HEADERSORTARROW 图元及其状态、TS_TRUE 尺寸类型(vssym32.h)。
	private const int HeaderSortArrowPart = 4;

	private const int HeaderSortArrowSortedUp = 1;

	private const int HeaderSortArrowSortedDown = 2;

	private const int ThemeSizeTrue = 1;

	// 排序箭头画在列头顶部居中,用的是原生 Header 控件(迁移前 ListView 的列头)同一个主题图元,
	// 升序 = SORTEDUP,与旧版 HDF_SORTUP 的取法一致。主题句柄按本控件窗口打开:Per-Monitor V2 下
	// 尺寸跟随本窗口所在屏的 DPI;public 的 VisualStyleRenderer 不带窗口句柄,只给系统 DPI 的尺寸,
	// 拖到异 DPI 屏会画大或画小。拿不到主题(经典样式等)时退回自绘的线条箭头。
	public void PaintHeaderSortArrow(Graphics graphics, Rectangle cellBounds, Rectangle clipBounds, SortOrder sortOrder)
	{
		if (sortOrder == SortOrder.None)
		{
			return;
		}
		Rectangle clip = Rectangle.Intersect(cellBounds, clipBounds);
		if (clip.IsEmpty)
		{
			return;
		}
		if (!TryPaintThemedHeaderSortArrow(graphics, cellBounds, clip, sortOrder))
		{
			PaintFallbackHeaderSortArrow(graphics, cellBounds, clip, sortOrder);
		}
	}

	private bool TryPaintThemedHeaderSortArrow(Graphics graphics, Rectangle cellBounds, Rectangle clip, SortOrder sortOrder)
	{
		if (!Application.RenderWithVisualStyles || !IsHandleCreated)
		{
			return false;
		}
		// GetHdc 不带 GDI+ 的变换;DGV 的绘制 Graphics 没有变换,万一有就交给自绘路径。
		using (Matrix transform = graphics.Transform)
		{
			if (!transform.IsIdentity)
			{
				return false;
			}
		}
		IntPtr theme = NativeMethods.OpenThemeData(Handle, "HEADER");
		if (theme == IntPtr.Zero)
		{
			return false;
		}
		try
		{
			int stateId = sortOrder == SortOrder.Ascending ? HeaderSortArrowSortedUp : HeaderSortArrowSortedDown;
			IntPtr deviceContext = graphics.GetHdc();
			try
			{
				if (NativeMethods.GetThemePartSize(theme, deviceContext, HeaderSortArrowPart, stateId, IntPtr.Zero, ThemeSizeTrue, out NativeMethods.ThemeSize size) < 0 || size.Width <= 0 || size.Height <= 0)
				{
					return false;
				}
				int left = cellBounds.Left + (cellBounds.Width - size.Width) / 2;
				NativeMethods.ThemeRect bounds = new NativeMethods.ThemeRect { Left = left, Top = cellBounds.Top, Right = left + size.Width, Bottom = cellBounds.Top + size.Height };
				// GetHdc 也不带 GDI+ 的剪裁,剪裁框显式传给 DrawThemeBackground。
				NativeMethods.ThemeRect clipBounds = new NativeMethods.ThemeRect { Left = clip.Left, Top = clip.Top, Right = clip.Right, Bottom = clip.Bottom };
				return NativeMethods.DrawThemeBackground(theme, deviceContext, HeaderSortArrowPart, stateId, ref bounds, ref clipBounds) >= 0;
			}
			finally
			{
				graphics.ReleaseHdc(deviceContext);
			}
		}
		finally
		{
			NativeMethods.CloseThemeData(theme);
		}
	}

	private void PaintFallbackHeaderSortArrow(Graphics graphics, Rectangle cellBounds, Rectangle clip, SortOrder sortOrder)
	{
		int halfWidth = LogicalToDeviceUnits(4);
		int height = LogicalToDeviceUnits(4);
		int centerX = cellBounds.Left + cellBounds.Width / 2;
		int top = cellBounds.Top + LogicalToDeviceUnits(1);
		Point[] points = sortOrder == SortOrder.Ascending
			? new[] { new Point(centerX - halfWidth, top + height), new Point(centerX, top), new Point(centerX + halfWidth, top + height) }
			: new[] { new Point(centerX - halfWidth, top), new Point(centerX, top + height), new Point(centerX + halfWidth, top) };
		GraphicsState state = graphics.Save();
		try
		{
			graphics.SetClip(clip, CombineMode.Intersect);
			graphics.SmoothingMode = SmoothingMode.AntiAlias;
			using Pen pen = new Pen(SystemColors.GrayText, LogicalToDeviceUnits(1));
			graphics.DrawLines(pen, points);
		}
		finally
		{
			graphics.Restore(state);
		}
	}
}

// 文件列表的列头单元格:背景按系统主题绘制,可排序列的悬停/按下态由 DGV 负责(需
// EnableHeadersVisualStyles = true)。在此之上补两处,让它和原 ListView 的原生列头一致:
// 1) .NET 8 画完主题背景后会在右缘无条件描一条 WindowFrame 色的分隔线
//    (ControlPaint.EnforceHeaderCellDividerContrast,无开关),比原生列头的浅灰分隔线深得多。
//    这里按 DGV 同样的状态规则(按下 > 悬停 > 常态),只把右缘那 1px 用主题重画回去。
// 2) 排序箭头不用 SortGlyphDirection(DGV 自画的立体三角,在标题右侧),改由 SortArrow 记录,
//    按原生列头的样式画在顶部居中。
internal sealed class NativeStyleColumnHeaderCell : DataGridViewColumnHeaderCell
{
	private static VisualStyleRenderer headerItemRenderer;

	private bool mouseOver;

	private SortOrder sortArrow;

	public SortOrder SortArrow
	{
		get
		{
			return sortArrow;
		}
		set
		{
			if (sortArrow == value)
			{
				return;
			}
			sortArrow = value;
			if (DataGridView != null && ColumnIndex >= 0)
			{
				DataGridView.InvalidateCell(ColumnIndex, -1);
			}
		}
	}

	public override object Clone()
	{
		NativeStyleColumnHeaderCell clone = (NativeStyleColumnHeaderCell)base.Clone();
		clone.sortArrow = sortArrow;
		return clone;
	}

	protected override void OnMouseEnter(int rowIndex)
	{
		mouseOver = true;
		base.OnMouseEnter(rowIndex);
	}

	protected override void OnMouseLeave(int rowIndex)
	{
		mouseOver = false;
		base.OnMouseLeave(rowIndex);
	}

	protected override void Paint(Graphics graphics, Rectangle clipBounds, Rectangle cellBounds, int rowIndex, DataGridViewElementStates dataGridViewElementState, object value, object formattedValue, string errorText, DataGridViewCellStyle cellStyle, DataGridViewAdvancedBorderStyle advancedBorderStyle, DataGridViewPaintParts paintParts)
	{
		base.Paint(graphics, clipBounds, cellBounds, rowIndex, dataGridViewElementState, value, formattedValue, errorText, cellStyle, advancedBorderStyle, paintParts);
		if (DataGridView is not BufferedDataGridView grid)
		{
			return;
		}
		// 与基类取主题背景的范围一致:单元格减去边框。
		Rectangle borderWidths = BorderWidths(advancedBorderStyle);
		Rectangle backgroundBounds = new Rectangle(cellBounds.X + borderWidths.X, cellBounds.Y + borderWidths.Y, cellBounds.Width - borderWidths.X - borderWidths.Width, cellBounds.Height - borderWidths.Y - borderWidths.Height);
		if (backgroundBounds.Width <= 0 || backgroundBounds.Height <= 0)
		{
			return;
		}
		if ((paintParts & DataGridViewPaintParts.Background) != 0 && grid.EnableHeadersVisualStyles && Application.RenderWithVisualStyles)
		{
			RestoreThemeDivider(graphics, clipBounds, backgroundBounds);
		}
		if (sortArrow != SortOrder.None && (paintParts & DataGridViewPaintParts.ContentForeground) != 0)
		{
			grid.PaintHeaderSortArrow(graphics, backgroundBounds, clipBounds, sortArrow);
		}
	}

	private void RestoreThemeDivider(Graphics graphics, Rectangle clipBounds, Rectangle backgroundBounds)
	{
		VisualStyleElement element = ButtonState != ButtonState.Normal ? VisualStyleElement.Header.Item.Pressed : (mouseOver ? VisualStyleElement.Header.Item.Hot : VisualStyleElement.Header.Item.Normal);
		if (!VisualStyleRenderer.IsElementDefined(element))
		{
			return;
		}
		// 对比度线从单元格上沿外一格画到下沿(含),重画范围与之对齐。
		Rectangle divider = new Rectangle(backgroundBounds.Right - 1, backgroundBounds.Top - 1, 1, backgroundBounds.Height + 2);
		divider.Intersect(clipBounds);
		if (divider.IsEmpty)
		{
			return;
		}
		headerItemRenderer ??= new VisualStyleRenderer(element);
		headerItemRenderer.SetParameters(element);
		headerItemRenderer.DrawBackground(graphics, backgroundBounds, divider);
	}
}

// 首列单元格:只改"编辑框的位置",绘制仍全部由宿主的 CellPainting 接管。
// 把编辑面板与编辑框一起按 FirstColumnEditIndent 右移,避开自绘的文件类型图标;
// 再上下各让出一条焦点描边的厚度,避开宿主 RowPostPaint 画的整行虚线框。
internal sealed class IndentedEditTextBoxCell : DataGridViewTextBoxCell
{
	// 与 ControlPaint.DrawFocusRectangle 的线宽一致(恒 1px,不随 DPI 变)。
	private const int FocusRectangleThickness = 1;

	// 上下让出的高度不对称。宿主把描边框的高度取成 RowBounds.Height - 1,底边那条线因此落在
	// 行底网格线的**上面一格**;而这里拿到的 cellBounds 是连网格线一起的整行高度。所以底部要
	// 让出两格(网格线 + 描边线),顶部只让描边线一格。
	// 2026-09-19 实测(截图逐扫描线统计颜色跳变):行高 33px 时描边上下边分别在 y=156 / y=187,
	// 上下对称各让 1 格时编辑面板占到 187,底边被盖住;让 2 格后 187 复现。
	private const int TopEditInset = FocusRectangleThickness;
	private const int BottomEditInset = FocusRectangleThickness + 1;

	public override void PositionEditingControl(bool setLocation, bool setSize, Rectangle cellBounds, Rectangle cellClip, DataGridViewCellStyle cellStyle, bool singleVerticalBorderAdded, bool singleHorizontalBorderAdded, bool isFirstDisplayedColumn, bool isFirstDisplayedRow)
	{
		Rectangle adjustedBounds = cellBounds;
		int indent = (DataGridView as BufferedDataGridView)?.FirstColumnEditIndent ?? 0;
		// 列被拖到只剩图标宽度时不再缩进,否则编辑框会窄到不可用。
		if (indent > 0 && adjustedBounds.Width > indent + 16)
		{
			adjustedBounds.X += indent;
			adjustedBounds.Width -= indent;
		}
		// 编辑面板是子控件,会盖住它底下的一切 —— 不让出这两条线,重命名时整行虚线框就断一截。
		if (adjustedBounds.Height > TopEditInset + BottomEditInset + 8)
		{
			adjustedBounds.Y += TopEditInset;
			adjustedBounds.Height -= TopEditInset + BottomEditInset;
		}
		if (adjustedBounds != cellBounds)
		{
			Rectangle clippedBounds = Rectangle.Intersect(cellClip, adjustedBounds);
			cellBounds = adjustedBounds;
			cellClip = clippedBounds.IsEmpty ? adjustedBounds : clippedBounds;
		}
		base.PositionEditingControl(setLocation, setSize, cellBounds, cellClip, cellStyle, singleVerticalBorderAdded, singleHorizontalBorderAdded, isFirstDisplayedColumn, isFirstDisplayedRow);
	}
}
