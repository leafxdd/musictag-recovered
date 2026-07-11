using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using MusicTag.Consumers;
using MusicTagWinApp.Instances;
using MusicTagWinApp.Web;

namespace MusicTagWinApp.Stubs;

internal class SourceOrderControl : UserControl
{
	private IContainer components;

	private GroupBox sourceGroupBox;

	private FlowLayoutPanel mainPanel;

	private ListView sourceListView;

	private FlowLayoutPanel buttonPanel;

	private Button moveUpButton;

	private Button moveDownButton;

	private ColumnHeader sourceColumn;

	private List<SourceItem> sources;

	public string Title { get; set; }

	public SourceOrderControl()
	{
		InitializeComponent();
		ApplyDpiMetrics(DeviceDpi);
	}

	public void SetSources(List<SourceItem> sourceItems)
	{
		sources = sourceItems;
	}

	public void ApplyListViewOrder()
	{
		foreach (ListViewItem listViewItem in sourceListView.Items)
		{
			SourceItem sourceItem = listViewItem.Tag as SourceItem;
			sourceItem.Enabled = listViewItem.Checked;
			sourceItem.Sequence = listViewItem.Index;
		}
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			moveUpButton.Image?.Dispose();
			moveUpButton.Image = null;
			moveDownButton.Image?.Dispose();
			moveDownButton.Image = null;
			components?.Dispose();
		}

		base.Dispose(disposing);
	}

	private void ApplyDpiMetrics(int dpi)
	{
		FontAwesome.Properties iconProperties = new FontAwesome.Properties
		{
			Size = ImageUtilities.ScaleLogicalPixels(20f, dpi),
			ShowBorder = false
		};
		Image oldUpImage = moveUpButton.Image;
		Image oldDownImage = moveDownButton.Image;
		moveUpButton.Image = FontAwesome.Type.AngleUp.AsImage(iconProperties);
		moveDownButton.Image = FontAwesome.Type.AngleDown.AsImage(iconProperties);
		oldUpImage?.Dispose();
		oldDownImage?.Dispose();
		moveUpButton.Text = "";
		moveDownButton.Text = "";
		buttonPanel.Width = ImageUtilities.ScaleLogicalPixels(30f, dpi);
		Size buttonSize = new Size(ImageUtilities.ScaleLogicalPixels(30f, dpi), ImageUtilities.ScaleLogicalPixels(23f, dpi));
		moveUpButton.Size = buttonSize;
		moveDownButton.Size = buttonSize;
		MainPanel_SizeChanged(mainPanel, EventArgs.Empty);
	}

	protected override void OnParentChanged(EventArgs e)
	{
		base.OnParentChanged(e);
		if (Disposing || IsDisposed)
		{
			return;
		}
		ApplyDpiMetrics(Parent?.DeviceDpi ?? DeviceDpi);
	}

	protected override void OnDpiChangedAfterParent(EventArgs e)
	{
		base.OnDpiChangedAfterParent(e);
		ApplyDpiMetrics(DeviceDpi);
	}

	private void SourceOrderControl_Load(object sender, EventArgs e)
	{
		ApplyDpiMetrics(DeviceDpi);
		sourceGroupBox.Text = Title ?? "Tag Sources";
		sourceListView.Items.Clear();
		if (sources != null)
		{
			foreach (SourceItem sourceItem in sources)
			{
				ListViewItem listViewItem = sourceListView.Items.Add(sourceItem.SearchSource.GetDisplayName());
				listViewItem.Checked = sourceItem.Enabled;
				listViewItem.Tag = sourceItem;
			}
		}

		sourceListView.Columns[0].Width = sourceListView.ClientSize.Width;
		UpdateButtonState();
	}

	private void SourceOrderControl_SizeChanged(object sender, EventArgs e)
	{
		sourceGroupBox.Width = Width;
		sourceGroupBox.Height = Height;
	}

	private void MainPanel_SizeChanged(object sender, EventArgs e)
	{
		sourceListView.Width = mainPanel.Width - buttonPanel.Width - buttonPanel.Margin.Left - buttonPanel.Margin.Right;
		sourceListView.Height = mainPanel.Height;
		buttonPanel.Height = mainPanel.Height;
		moveUpButton.Margin = new Padding(0, buttonPanel.Height - moveUpButton.Height - moveDownButton.Height - ImageUtilities.ScaleByDpi(10f, this), 0, 0);
	}

	private void MoveUpButton_Click(object sender, EventArgs e)
	{
		if (sourceListView.SelectedItems.Count <= 0)
		{
			return;
		}

		ListViewItem selectedItem = sourceListView.SelectedItems[0];
		if (!CanMoveUp(selectedItem))
		{
			return;
		}

		int newIndex = selectedItem.Index - 1;
		sourceListView.Items.Remove(selectedItem);
		sourceListView.Items.Insert(newIndex, selectedItem);
		if (sourceListView.GetItemRect(newIndex).Top <= sourceListView.ClientRectangle.Top)
		{
			sourceListView.EnsureVisible(newIndex);
		}
		UpdateButtonState();
	}

	private void MoveDownButton_Click(object sender, EventArgs e)
	{
		if (sourceListView.SelectedItems.Count <= 0)
		{
			return;
		}

		ListViewItem selectedItem = sourceListView.SelectedItems[0];
		if (!CanMoveDown(selectedItem))
		{
			return;
		}

		int newIndex = selectedItem.Index + 1;
		sourceListView.Items.Remove(selectedItem);
		sourceListView.Items.Insert(newIndex, selectedItem);
		if (sourceListView.GetItemRect(newIndex).Bottom >= sourceListView.ClientRectangle.Bottom)
		{
			sourceListView.EnsureVisible(newIndex);
		}
		UpdateButtonState();
	}

	private void SourceListView_ItemSelectionChanged(object sender, ListViewItemSelectionChangedEventArgs e)
	{
		UpdateButtonState();
	}

	private void UpdateButtonState()
	{
		bool canMoveUp = false;
		bool canMoveDown = false;
		if (sourceListView.SelectedItems.Count > 0)
		{
			ListViewItem selectedItem = sourceListView.SelectedItems[0];
			canMoveUp = CanMoveUp(selectedItem);
			canMoveDown = CanMoveDown(selectedItem);
		}

		moveUpButton.Enabled = canMoveUp;
		moveDownButton.Enabled = canMoveDown;
	}

	private bool CanMoveUp(ListViewItem selectedItem)
	{
		if (selectedItem.Index <= 0)
		{
			return false;
		}

		SourceItem selectedSource = selectedItem.Tag as SourceItem;
		SourceItem previousSource = sourceListView.Items[selectedItem.Index - 1].Tag as SourceItem;
		return !selectedSource.IsSecondarySource || previousSource.IsSecondarySource;
	}

	private bool CanMoveDown(ListViewItem selectedItem)
	{
		if (selectedItem.Index >= sourceListView.Items.Count - 1)
		{
			return false;
		}

		SourceItem selectedSource = selectedItem.Tag as SourceItem;
		SourceItem nextSource = sourceListView.Items[selectedItem.Index + 1].Tag as SourceItem;
		return selectedSource.IsSecondarySource || !nextSource.IsSecondarySource;
	}

	private void InitializeComponent()
	{
		components = new Container();
		sourceGroupBox = new GroupBox();
		mainPanel = new FlowLayoutPanel();
		sourceListView = new ListView();
		sourceColumn = new ColumnHeader();
		buttonPanel = new FlowLayoutPanel();
		moveUpButton = new Button();
		moveDownButton = new Button();
		sourceGroupBox.SuspendLayout();
		mainPanel.SuspendLayout();
		buttonPanel.SuspendLayout();
		SuspendLayout();
		sourceGroupBox.Controls.Add(mainPanel);
		sourceGroupBox.Location = new Point(0, 0);
		sourceGroupBox.Margin = new Padding(0);
		sourceGroupBox.Name = "gbTagSrcs";
		sourceGroupBox.Padding = new Padding(5);
		sourceGroupBox.Size = new Size(365, 230);
		sourceGroupBox.TabIndex = 1;
		sourceGroupBox.TabStop = false;
		sourceGroupBox.Text = "Picture";
		mainPanel.Controls.Add(sourceListView);
		mainPanel.Controls.Add(buttonPanel);
		mainPanel.Dock = DockStyle.Fill;
		mainPanel.Location = new Point(5, 20);
		mainPanel.Margin = new Padding(0);
		mainPanel.Name = "flowLayoutPanel1";
		mainPanel.Size = new Size(355, 205);
		mainPanel.TabIndex = 0;
		mainPanel.WrapContents = false;
		mainPanel.SizeChanged += MainPanel_SizeChanged;
		sourceListView.CheckBoxes = true;
		sourceListView.Columns.AddRange(new ColumnHeader[1] { sourceColumn });
		sourceListView.FullRowSelect = true;
		sourceListView.HeaderStyle = ColumnHeaderStyle.None;
		sourceListView.HideSelection = false;
		sourceListView.Location = new Point(0, 0);
		sourceListView.Margin = new Padding(0);
		sourceListView.MultiSelect = false;
		sourceListView.Name = "listView1";
		sourceListView.Size = new Size(300, 200);
		sourceListView.TabIndex = 0;
		sourceListView.UseCompatibleStateImageBehavior = false;
		sourceListView.View = View.Details;
		sourceListView.ItemSelectionChanged += SourceListView_ItemSelectionChanged;
		sourceColumn.Width = 250;
		buttonPanel.Controls.Add(moveUpButton);
		buttonPanel.Controls.Add(moveDownButton);
		buttonPanel.FlowDirection = FlowDirection.TopDown;
		buttonPanel.Location = new Point(310, 0);
		buttonPanel.Margin = new Padding(10, 0, 5, 0);
		buttonPanel.Name = "flowLayoutPanel2";
		buttonPanel.Size = new Size(30, 200);
		buttonPanel.TabIndex = 1;
		buttonPanel.WrapContents = false;
		moveUpButton.Location = new Point(0, 134);
		moveUpButton.Margin = new Padding(0, 134, 0, 0);
		moveUpButton.Name = "btnUp";
		moveUpButton.Size = new Size(30, 23);
		moveUpButton.TabIndex = 0;
		moveUpButton.UseVisualStyleBackColor = true;
		moveUpButton.Click += MoveUpButton_Click;
		moveDownButton.Location = new Point(0, 167);
		moveDownButton.Margin = new Padding(0, 10, 0, 0);
		moveDownButton.Name = "btnDown";
		moveDownButton.Size = new Size(30, 23);
		moveDownButton.TabIndex = 1;
		moveDownButton.UseVisualStyleBackColor = true;
		moveDownButton.Click += MoveDownButton_Click;
		AutoScaleDimensions = new SizeF(7f, 14f);
		AutoScaleMode = AutoScaleMode.Font;
		Controls.Add(sourceGroupBox);
		Font = new Font("Tahoma", 9f, FontStyle.Regular, GraphicsUnit.Point, 0);
		Margin = new Padding(0);
		Name = "ControlTagSources";
		Size = new Size(365, 230);
		Load += SourceOrderControl_Load;
		SizeChanged += SourceOrderControl_SizeChanged;
		sourceGroupBox.ResumeLayout(performLayout: false);
		mainPanel.ResumeLayout(performLayout: false);
		buttonPanel.ResumeLayout(performLayout: false);
		ResumeLayout(performLayout: false);
	}
}
