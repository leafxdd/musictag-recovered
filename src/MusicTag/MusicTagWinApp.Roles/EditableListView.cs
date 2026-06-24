using System;
using System.Collections;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;
using MusicTag.Bridges;
using MusicTag.Candidates;
using MusicTag.Consumers;
using MusicTag.Services;
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

	private class TextSubItemComparer : IComparer
	{
		private int columnIndex;

		private SortOrder sortOrder;

		public TextSubItemComparer()
		{
			columnIndex = 0;
			sortOrder = SortOrder.Ascending;
		}

		public TextSubItemComparer(int columnIndex, SortOrder sortOrder)
		{
			this.columnIndex = columnIndex;
			this.sortOrder = sortOrder;
		}

		public int Compare(object left, object right)
		{
			string leftText = ((ListViewItem)left).SubItems[columnIndex].Text;
			string rightText = ((ListViewItem)right).SubItems[columnIndex].Text;
			int compareResult = CompareSortableText(leftText, rightText);
			if (sortOrder == SortOrder.Descending)
			{
				compareResult *= -1;
			}
			return compareResult;
		}
	}

	private class DrawableSubItemSortValueComparer : IComparer
	{
		private int columnIndex;

		private SortOrder sortOrder;

		public DrawableSubItemSortValueComparer()
		{
			columnIndex = 0;
			sortOrder = SortOrder.Ascending;
		}

		public DrawableSubItemSortValueComparer(int columnIndex, SortOrder sortOrder)
		{
			this.columnIndex = columnIndex;
			this.sortOrder = sortOrder;
		}

		public int Compare(object left, object right)
		{
			string leftSortValue = ((DrawableListViewSubItem)((ListViewItem)left).SubItems[columnIndex]).SortValue;
			string rightSortValue = ((DrawableListViewSubItem)((ListViewItem)right).SubItems[columnIndex]).SortValue;
			int compareResult = CompareSortableText(leftSortValue, rightSortValue);
			if (sortOrder == SortOrder.Descending)
			{
				compareResult *= -1;
			}
			return compareResult;
		}
	}

	private class ItemTextComparer : IComparer
	{
		private SortOrder sortOrder;

		public ItemTextComparer()
		{
			sortOrder = SortOrder.Ascending;
		}

		public ItemTextComparer(SortOrder sortOrder)
		{
			this.sortOrder = sortOrder;
		}

		public int Compare(object left, object right)
		{
			string leftText = ((ListViewItem)left).Text;
			string rightText = ((ListViewItem)right).Text;
			int compareResult = CompareSortableText(leftText, rightText);
			if (sortOrder == SortOrder.Descending)
			{
				compareResult *= -1;
			}
			return compareResult;
		}
	}

	private class AssociatedValueComparer : IComparer
	{
		private SortOrder sortOrder;

		public AssociatedValueComparer()
		{
			sortOrder = SortOrder.Ascending;
		}

		public AssociatedValueComparer(SortOrder sortOrder)
		{
			this.sortOrder = sortOrder;
		}

		public int Compare(object left, object right)
		{
			string leftValue = ((AssociatedValueListViewItem)left).AssociatedValue;
			string rightValue = ((AssociatedValueListViewItem)right).AssociatedValue;
			int compareResult = CompareSortableText(leftValue, rightValue);
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
		if (editor.GetType() == typeof(ImageComboBox))
		{
			object selectedItem = ((ImageComboBox)editor).SelectedItem;
			if (selectedItem.GetType() == typeof(ImageComboBox.SingleImageItem))
			{
				ApplySingleImageSelection((ImageComboBox.SingleImageItem)selectedItem);
			}
			else if (selectedItem.GetType() == typeof(ImageComboBox.ImageListItem))
			{
				ApplyImageListSelection((ImageComboBox.ImageListItem)selectedItem);
			}
		}
		activeSubItem.Text = comboBox.Text;
		comboBox.Visible = false;
		activeItem.Tag = null;
	}

	private void ApplySingleImageSelection(ImageComboBox.SingleImageItem selectedImage)
	{
		if (activeColumnIndex == 0)
		{
			if (activeItem.GetType() == typeof(CoverImageListViewItem))
			{
				((CoverImageListViewItem)activeItem).CoverImage = selectedImage.Image;
			}
			else if (activeItem.GetType() == typeof(MultiValueListViewItem))
			{
				MultiValueListViewItem imageListItem = (MultiValueListViewItem)activeItem;
				imageListItem.Values.Clear();
				imageListItem.Values.AddRange(new object[1] { selectedImage.Image });
			}
			return;
		}
		if (activeSubItem.GetType() == typeof(ImageSubItem))
		{
			((ImageSubItem)activeSubItem).IconImage = selectedImage.Image;
		}
		else if (activeSubItem.GetType() == typeof(ImageListSubItem))
		{
			ImageListSubItem imageListSubItem = (ImageListSubItem)activeSubItem;
			imageListSubItem.Images.Clear();
			imageListSubItem.Images.Add(selectedImage.Image);
			imageListSubItem.SortValue = selectedImage.SortValue;
		}
	}

	private void ApplyImageListSelection(ImageComboBox.ImageListItem selectedImages)
	{
		if (activeColumnIndex == 0)
		{
			if (activeItem.GetType() == typeof(CoverImageListViewItem))
			{
				((CoverImageListViewItem)activeItem).CoverImage = (Image)selectedImages.Images[0];
			}
			else if (activeItem.GetType() == typeof(MultiValueListViewItem))
			{
				MultiValueListViewItem imageListItem = (MultiValueListViewItem)activeItem;
				imageListItem.Values.Clear();
				imageListItem.Values.AddRange(selectedImages.Images);
			}
			return;
		}
		if (activeSubItem.GetType() == typeof(ImageSubItem))
		{
			if (selectedImages.Images != null)
			{
				((ImageSubItem)activeSubItem).IconImage = (Image)selectedImages.Images[0];
			}
		}
		else if (activeSubItem.GetType() == typeof(ImageListSubItem))
		{
			ImageListSubItem imageListSubItem = (ImageListSubItem)activeSubItem;
			imageListSubItem.Images.Clear();
			imageListSubItem.Images.AddRange((ICollection)selectedImages.Images);
			imageListSubItem.SortValue = selectedImages.SortValue;
		}
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
		CheckBoxRenderer.DrawCheckBox(e.Graphics, new Point(e.Bounds.Left + 4, e.Bounds.Top + DatabaseMapper.ScaleByDpi(4f)), isChecked ? CheckBoxState.CheckedNormal : CheckBoxState.UncheckedNormal);
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
		int textY = e.Bounds.Y + e.Bounds.Height / 2 - e.SubItem.Font.Height / 2;
			int textX = e.Bounds.X + 2;
			if (e.ColumnIndex == 0)
			{
				if (e.Item is CoverImageListViewItem coverImageListViewItem && coverImageListViewItem.CoverImage != null)
				{
					Image coverImage = coverImageListViewItem.CoverImage;
					int imageY = e.Bounds.Y + e.Bounds.Height / 2 - coverImage.Height / 2;
					e.Graphics.DrawImage(coverImage, textX, imageY, coverImage.Width, coverImage.Height);
					textX += coverImage.Width + 2;
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
		using (SolidBrush textBrush = new SolidBrush(e.SubItem.ForeColor))
		{
			e.Graphics.DrawString(e.SubItem.Text, e.SubItem.Font, textBrush, textX, textY);
		}
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
					base.ListViewItemSorter = new ItemTextComparer(base.Sorting);
			}
			else
			{
					base.ListViewItemSorter = new AssociatedValueComparer(base.Sorting);
			}
		}
		else if (base.Items[0].SubItems[sortedColumnIndex].GetType() == typeof(DrawableListViewSubItem))
		{
			base.ListViewItemSorter = new TextSubItemComparer(e.Column, base.Sorting);
		}
		else
		{
			base.ListViewItemSorter = new DrawableSubItemSortValueComparer(e.Column, base.Sorting);
		}
	}

}

