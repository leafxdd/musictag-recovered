using System.Drawing;
using System.Windows.Forms;

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
