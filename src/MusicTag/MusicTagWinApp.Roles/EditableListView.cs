using System;
using System.Collections;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;
using MusicTag.Candidates;
using MusicTag.Consumers;
using MusicTagWinApp.Common;
using MusicTagWinApp.Containers;
using MusicTagWinApp.Exporters;
using MusicTagWinApp.Instances;
using MusicTagWinApp.Listeners;
using MusicTagWinApp.Stubs;

namespace MusicTagWinApp.Roles;

internal class EditableListView : HeaderAwareListView
{
	private struct EmbeddedControlBinding
	{
		public Control Control;

		public EmbeddedControlSubItem SubItem;
	}

	private class SortableTextComparer : IComparer
	{
		private readonly Func<object, string> keySelector;

		private readonly SortOrder sortOrder;

		public SortableTextComparer(Func<object, string> keySelector, SortOrder sortOrder)
		{
			this.keySelector = keySelector;
			this.sortOrder = sortOrder;
		}

		public int Compare(object left, object right)
		{
			int compareResult = CompareSortableText(keySelector(left), keySelector(right));
			if (sortOrder == SortOrder.Descending)
			{
				compareResult *= -1;
			}
			return compareResult;
		}
	}

	private static int CompareSortableText(string left, string right)
	{
		if (decimal.TryParse(left, out var leftNumber) && decimal.TryParse(right, out var rightNumber))
		{
			return decimal.Compare(leftNumber, rightNumber);
		}
		if (DateTime.TryParse(left, out var leftDate) && DateTime.TryParse(right, out var rightDate))
		{
			return DateTime.Compare(leftDate, rightDate);
		}
		return string.Compare(left, right);
	}

	private ListViewItem.ListViewSubItem activeSubItem;

	private ListViewItem activeItem;

	private int activeColumnIndex;

	private TextBox inlineTextEditor;

	private int sortedColumnIndex;

	private Brush sortedColumnBackBrush;

	private Brush selectedRowBackBrush;

	private Brush inactiveSelectedRowBackBrush;

	private int embeddedControlInset;

	private ArrayList embeddedControlBindings;

	[DefaultValue(false)]
	[Category("Behavior")]
	[Description("Enable sort")]
	public bool SortingEnabled { get; set; }

	[DllImport("user32.dll", EntryPoint = "SendMessage")]
	private static extern bool SendMessage(IntPtr windowHandle, uint message, int wParam, int lParam);

	protected override void WndProc(ref Message message)
	{
		if (message.Msg == 15)
		{
			UpdateEmbeddedControlBounds();
		}
		int msg = message.Msg;
		if ((uint)(msg - 276) <= 1u || msg == 522)
		{
			Focus();
		}
		base.WndProc(ref message);
	}

	private void UpdateEmbeddedControlBounds()
	{
		foreach (EmbeddedControlBinding binding in embeddedControlBindings)
		{
			Rectangle bounds = binding.SubItem.Bounds;
			if (bounds.Y <= 0 || bounds.Y >= base.ClientRectangle.Height)
			{
				binding.Control.Visible = false;
				continue;
			}
			binding.Control.Visible = true;
			if (binding.Control is ComboBox comboBox)
			{
				comboBox.Bounds = new Rectangle(bounds.X + embeddedControlInset, bounds.Y + (bounds.Height - comboBox.Height) / 2, bounds.Width - 2 * embeddedControlInset, comboBox.Height);
			}
			else
			{
				binding.Control.Bounds = new Rectangle(bounds.X + embeddedControlInset, bounds.Y + embeddedControlInset, bounds.Width - 2 * embeddedControlInset, bounds.Height - 2 * embeddedControlInset);
			}
		}
	}

	private void ScrollListView(int horizontalDelta, int verticalDelta)
	{
		SendMessage(base.Handle, 4116u, horizontalDelta, verticalDelta);
	}

	public EditableListView()
	{
		embeddedControlInset = 4;
		embeddedControlBindings = new ArrayList();
		sortedColumnIndex = -1;
		sortedColumnBackBrush = SystemBrushes.ControlLight;
		selectedRowBackBrush = SystemBrushes.Highlight;
		inactiveSelectedRowBackBrush = SystemBrushes.Control;
		base.OwnerDraw = true;
		base.View = View.Details;
		base.MouseDown += ScrollHiddenSubItemIntoView;
		base.MouseDoubleClick += StartSubItemEdit;
		base.DrawColumnHeader += DrawCustomColumnHeader;
		base.DrawSubItem += DrawCustomSubItem;
		base.MouseMove += InvalidateHoveredItem;
		base.ColumnClick += OnColumnHeaderClick;
		inlineTextEditor = new TextBox();
		inlineTextEditor.Visible = false;
		base.Controls.Add(inlineTextEditor);
		inlineTextEditor.Leave += CommitInlineTextEditor;
		inlineTextEditor.KeyPress += CommitInlineTextEditorOnEnter;
	}

	public int EmbeddedControlInset
	{
		get
		{
			return embeddedControlInset;
		}
		set
		{
			embeddedControlInset = value;
		}
	}

	public void AttachEmbeddedControl(Control control, EmbeddedControlSubItem subItem)
	{
		base.Controls.Add(control);
		subItem.EmbeddedControl = control;
		embeddedControlBindings.Add(new EmbeddedControlBinding
		{
			Control = control,
			SubItem = subItem
		});
	}

	private void CommitInlineTextEditorOnEnter(object sender, KeyPressEventArgs e)
	{
		if (e.KeyChar == '\r')
		{
			activeSubItem.Text = inlineTextEditor.Text;
			inlineTextEditor.Visible = false;
			activeItem.Tag = null;
		}
	}

	private void CommitInlineTextEditor(object sender, EventArgs e)
	{
		Control control = (Control)sender;
		activeSubItem.Text = control.Text;
		control.Visible = false;
		activeItem.Tag = null;
	}

	private void ScrollHiddenSubItemIntoView(object sender, MouseEventArgs e)
	{
		ListViewItem.ListViewSubItem subItem = HitTest(e.X, e.Y).SubItem;
		if (subItem == null)
		{
			return;
		}
		int left = subItem.Bounds.Left;
		if (left >= 0)
		{
			return;
		}
		ScrollListView(left, 0);
	}

	private void StartSubItemEdit(object sender, MouseEventArgs mouseArgs)
	{
		AssociatedValueListViewItem listViewItem = GetItemAt(mouseArgs.X, mouseArgs.Y) as AssociatedValueListViewItem;
		if (listViewItem == null)
		{
			return;
		}
		activeItem = listViewItem;
		int columnLeft = listViewItem.Bounds.Left;
		int columnIndex = 0;
		for (; columnIndex < base.Columns.Count; columnIndex++)
		{
			int columnRight = columnLeft + base.Columns[columnIndex].Width;
			if (columnRight > mouseArgs.X)
			{
				break;
			}
			columnLeft = columnRight;
		}
		if (columnIndex >= base.Columns.Count || !(base.Columns[columnIndex] is CustomColumnHeader customColumnHeader))
		{
			return;
		}
		activeSubItem = listViewItem.SubItems[columnIndex];
		activeColumnIndex = columnIndex;
		int rowTop = GetItemRect(base.Items.IndexOf(listViewItem)).Y;
		if (customColumnHeader.GetType() == typeof(EditableColumnHeader))
		{
			EditableColumnHeader editableColumnHeader = (EditableColumnHeader)customColumnHeader;
			Control control = editableColumnHeader.EditorControl;
			if (control == null)
			{
				inlineTextEditor.Location = new Point(columnLeft, rowTop);
				ShowEditorControl(inlineTextEditor, columnIndex, activeSubItem.Text);
				return;
			}
			if (control.Tag != null)
			{
				base.Controls.Add(control);
				control.Tag = null;
				if (control is ComboBox)
				{
					((ComboBox)control).SelectedValueChanged += CommitComboBoxEditorSelection;
				}
				control.Leave += CommitInlineTextEditor;
			}
			control.Location = new Point(columnLeft, rowTop);
			ShowEditorControl(control, columnIndex, activeSubItem.Text);
			return;
		}
		if (customColumnHeader.GetType() == typeof(CheckBoxColumnHeader) && ((CheckBoxColumnHeader)customColumnHeader).IsEditable)
		{
			CheckBoxSubItem checkBoxSubItem = (CheckBoxSubItem)activeSubItem;
			checkBoxSubItem.IsChecked = !checkBoxSubItem.IsChecked;
			Invalidate(checkBoxSubItem.Bounds);
		}
	}

	private void ShowEditorControl(Control control, int columnIndex, string text)
	{
		control.Width = base.Columns[columnIndex].Width;
		if (control.Width > base.Width)
		{
			control.Width = base.ClientRectangle.Width;
		}
		control.Text = text;
		control.Visible = true;
		control.BringToFront();
		control.Focus();
	}

	private void CommitComboBoxEditorSelection(object editor, EventArgs e)
	{
		ComboBox comboBox = (ComboBox)editor;
		if (!comboBox.Visible || activeSubItem == null)
		{
			return;
		}
		activeSubItem.Text = comboBox.Text;
		comboBox.Visible = false;
		activeItem.Tag = null;
	}

	private void InvalidateHoveredItem(object sender, MouseEventArgs e)
	{
		ListViewItem hoveredItem = GetItemAt(e.X, e.Y);
		if (hoveredItem == null || hoveredItem.Tag != null)
		{
			return;
		}
		Invalidate(hoveredItem.Bounds);
		hoveredItem.Tag = "t";
	}

	private void DrawCustomColumnHeader(object sender, DrawListViewColumnHeaderEventArgs e)
	{
		if (!base.CheckBoxes || e.ColumnIndex != 0)
		{
			e.DrawDefault = true;
			return;
			}
			e.DrawBackground();
			bool isChecked = false;
		try
		{
			isChecked = Convert.ToBoolean(e.Header.Tag);
		}
		catch (System.Exception)
		{
		}
		CheckBoxRenderer.DrawCheckBox(e.Graphics, new Point(e.Bounds.Left + 4, e.Bounds.Top + ImageUtilities.ScaleByDpi(4f)), isChecked ? CheckBoxState.CheckedNormal : CheckBoxState.UncheckedNormal);
	}

	private void DrawCustomSubItem(object sender, DrawListViewSubItemEventArgs e)
	{
		if (base.CheckBoxes && e.ColumnIndex == 0)
		{
			e.DrawDefault = true;
			return;
		}
		e.DrawBackground();
		if (base.FullRowSelect)
		{
			if (e.ColumnIndex == sortedColumnIndex)
			{
				e.Graphics.FillRectangle(sortedColumnBackBrush, e.Bounds);
			}
			if ((e.ItemState & ListViewItemStates.Selected) != 0 && base.SelectedItems.Contains(e.Item))
			{
				e.Graphics.FillRectangle((base.HideSelection || Focused) ? selectedRowBackBrush : inactiveSelectedRowBackBrush, e.Bounds);
			}
		}
		int textX = e.Bounds.X + 2;
		if (e.ColumnIndex == 0)
		{
			if (e.Item is CoverImageListViewItem coverImageListViewItem && coverImageListViewItem.CoverImage != null)
			{
				textX = DrawableListViewSubItem.DrawCenteredImage(e.Graphics, coverImageListViewItem.CoverImage, e.Bounds, textX);
			}
		}
		else if (e.SubItem is DrawableListViewSubItem drawableSubItem)
		{
			textX = drawableSubItem.DoDraw(e, textX, base.Columns[e.ColumnIndex] as CustomColumnHeader);
		}
		else
		{
			e.DrawDefault = true;
			return;
		}

		int textWidth = e.Bounds.Right - textX;
		if (textWidth <= 0)
		{
			return;
		}

		TextRenderer.DrawText(e.Graphics, e.SubItem.Text, e.SubItem.Font, new Rectangle(textX, e.Bounds.Y, textWidth, e.Bounds.Height), e.SubItem.ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
	}

	private void OnColumnHeaderClick(object sender, ColumnClickEventArgs e)
	{
		if (base.CheckBoxes && e.Column == 0)
		{
			bool allItemsChecked = false;
			try
			{
				allItemsChecked = Convert.ToBoolean(base.Columns[e.Column].Tag);
			}
			catch (System.Exception)
			{
				}
				base.Columns[e.Column].Tag = !allItemsChecked;
				foreach (ListViewItem listViewItem in base.Items)
				{
					listViewItem.Checked = !allItemsChecked;
				}
				Invalidate();
				return;
		}
		if (base.Items.Count == 0 || !SortingEnabled)
		{
			return;
		}
		for (int columnIndex = 0; columnIndex < base.Columns.Count; columnIndex++)
		{
			base.Columns[columnIndex].ImageKey = null;
		}
			for (int itemIndex = 0; itemIndex < base.Items.Count; itemIndex++)
			{
				base.Items[itemIndex].Tag = null;
			}
		if (e.Column != sortedColumnIndex)
		{
			sortedColumnIndex = e.Column;
			base.Sorting = SortOrder.Ascending;
			base.Columns[e.Column].ImageKey = "up";
		}
		else if (base.Sorting == SortOrder.Ascending)
		{
			base.Sorting = SortOrder.Descending;
			base.Columns[e.Column].ImageKey = "down";
		}
		else
		{
			base.Sorting = SortOrder.Ascending;
			base.Columns[e.Column].ImageKey = "up";
		}
		if (sortedColumnIndex == 0)
		{
			if (base.Items[0].GetType() == typeof(AssociatedValueListViewItem))
			{
					base.ListViewItemSorter = new SortableTextComparer(item => ((ListViewItem)item).Text, base.Sorting);
			}
			else
			{
					base.ListViewItemSorter = new SortableTextComparer(item => ((AssociatedValueListViewItem)item).AssociatedValue, base.Sorting);
			}
		}
		else
		{
			base.ListViewItemSorter = new SortableTextComparer(item => ((DrawableListViewSubItem)((ListViewItem)item).SubItems[e.Column]).SortValue, base.Sorting);
		}
	}

}
