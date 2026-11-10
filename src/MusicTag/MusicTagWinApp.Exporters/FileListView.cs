using System;
using System.Collections.Generic;
using System.Windows.Forms;
using MusicTagWinApp.Containers;

namespace MusicTagWinApp.Exporters;

// 文件列表:虚拟模式的原生 ListView(报表视图)。数据全在宿主的 FileRow 模型里,控件只按需取
// 可见行(RetrieveVirtualItem),框选、就地重命名、列头动画、失焦灰选中、键入定位都由系统提供。
// 2026-06 曾为横向滚动卡顿换成 DataGridView;2026-11 实测(cctortest/listview-spike):ListView 的
// 卡只出在横向滚动 —— 每横移一步都整屏重画,Tahoma 又缺中文字形、逐格走字体回退。列宽收进一屏、
// 换用带中文字形的字体后,纵向滚动与点选和 DataGridView 相当。原生双缓冲与列头右键由基类提供。
internal sealed class FileListView : HeaderAwareListView
{
	private const int ListViewGetHeaderMessage = 0x101F;

	private const int ListViewGetEditControlMessage = 0x1018;

	private const int ListViewEditLabelMessage = 0x1076;

	private const int EditSetSelectionMessage = 0x00B1;

	private const int HeaderGetItemMessage = 0x120B;

	private const int HeaderSetItemMessage = 0x120C;

	private const uint HeaderItemFormatMask = 0x0004;

	private const int HeaderFormatSortDown = 0x0200;

	private const int HeaderFormatSortUp = 0x0400;

	// 列头排序箭头按列记:列头格式位随句柄重建丢失,要能补回。
	private readonly Dictionary<ColumnHeader, SortOrder> sortArrows = new Dictionary<ColumnHeader, SortOrder>();

	// 升序 = HDF_SORTUP,与迁移前一致。只改格式位里的两个排序位,保留对齐等其余格式。
	public void SetColumnSortArrow(ColumnHeader column, SortOrder sortOrder)
	{
		if (sortOrder == SortOrder.None)
		{
			sortArrows.Remove(column);
		}
		else
		{
			sortArrows[column] = sortOrder;
		}
		ApplySortArrow(column, sortOrder);
	}

	private void ApplySortArrow(ColumnHeader column, SortOrder sortOrder)
	{
		int columnIndex = column.Index;
		if (!IsHandleCreated || columnIndex < 0 || columnIndex >= Columns.Count || Columns[columnIndex] != column)
		{
			return;
		}
		IntPtr headerHandle = NativeMethods.SendMessage(Handle, ListViewGetHeaderMessage, IntPtr.Zero, IntPtr.Zero);
		if (headerHandle == IntPtr.Zero)
		{
			return;
		}
		NativeMethods.HeaderItem headerItem = new NativeMethods.HeaderItem { Mask = HeaderItemFormatMask };
		if (NativeMethods.SendHeaderItemMessage(headerHandle, HeaderGetItemMessage, (IntPtr)columnIndex, ref headerItem) == IntPtr.Zero)
		{
			return;
		}
		headerItem.Format &= ~(HeaderFormatSortUp | HeaderFormatSortDown);
		if (sortOrder == SortOrder.Ascending)
		{
			headerItem.Format |= HeaderFormatSortUp;
		}
		else if (sortOrder == SortOrder.Descending)
		{
			headerItem.Format |= HeaderFormatSortDown;
		}
		NativeMethods.SendHeaderItemMessage(headerHandle, HeaderSetItemMessage, (IntPtr)columnIndex, ref headerItem);
	}

	// 列被清掉重建时,旧列的箭头记录作废。
	public void ForgetSortArrows()
	{
		sortArrows.Clear();
	}

	protected override void OnHandleCreated(EventArgs e)
	{
		base.OnHandleCreated(e);
		foreach (KeyValuePair<ColumnHeader, SortOrder> sortArrow in sortArrows)
		{
			ApplySortArrow(sortArrow.Key, sortArrow.Value);
		}
	}

	// 程序化进入就地重命名(菜单 / F2)。虚拟模式下不经 ListViewItem.BeginEdit,直接按行号发 LVM_EDITLABEL。
	public bool BeginLabelEdit(int index)
	{
		if (!IsHandleCreated || index < 0 || index >= VirtualListSize)
		{
			return false;
		}
		Focus();
		return NativeMethods.SendMessage(Handle, ListViewEditLabelMessage, (IntPtr)index, IntPtr.Zero) != IntPtr.Zero;
	}

	// 改写就地重命名编辑框的文本,并选中前 selectionLength 个字符(文件名去掉扩展名的部分)。
	// 须在 BeforeLabelEdit 里调用:此时编辑框已建好、尚未显示。
	public void SetLabelEditText(string text, int selectionLength)
	{
		IntPtr editHandle = IsHandleCreated ? NativeMethods.SendMessage(Handle, ListViewGetEditControlMessage, IntPtr.Zero, IntPtr.Zero) : IntPtr.Zero;
		if (editHandle == IntPtr.Zero)
		{
			return;
		}
		NativeMethods.SetWindowText(editHandle, text);
		NativeMethods.SendMessage(editHandle, EditSetSelectionMessage, IntPtr.Zero, (IntPtr)selectionLength);
	}
}
