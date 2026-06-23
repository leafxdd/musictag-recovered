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

	private Timer buttonStateTimer;

	private List<SourceItem> sources;

	public string Title { get; set; }

	public SourceOrderControl()
	{
		InitializeComponent();
		ConfigureButtonImages();
		ScaleButtonSizes();
	}

	public void SetSources(List<SourceItem> sourceItems)
	{
		sources = sourceItems;
	}

	public List<SourceItem> GetSources()
	{
		return sources;
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
			components?.Dispose();
		}

		base.Dispose(disposing);
	}

	private void ConfigureButtonImages()
	{
		FontAwesome.Properties iconProperties = new FontAwesome.Properties
		{
			Size = DatabaseMapper.ScaleByDpi(20f),
			ShowBorder = false
		};
		moveUpButton.Image = FontAwesome.Type.AngleUp.AsImage(iconProperties);
		moveDownButton.Image = FontAwesome.Type.AngleDown.AsImage(iconProperties);
		moveUpButton.Text = "";
		moveDownButton.Text = "";
	}

	private void ScaleButtonSizes()
	{
		buttonPanel.Width = DatabaseMapper.ScaleByDpi(buttonPanel.Width);
		moveUpButton.Size = new Size(DatabaseMapper.ScaleByDpi(moveUpButton.Size.Width), DatabaseMapper.ScaleByDpi(moveUpButton.Size.Height));
		moveDownButton.Size = new Size(DatabaseMapper.ScaleByDpi(moveDownButton.Size.Width), DatabaseMapper.ScaleByDpi(moveDownButton.Size.Height));
	}

	private void SourceOrderControl_Load(object sender, EventArgs e)
	{
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
		moveUpButton.Margin = new Padding(0, buttonPanel.Height - moveUpButton.Height - moveDownButton.Height - DatabaseMapper.ScaleByDpi(10f), 0, 0);
	}

	private void MoveUpButton_Click(object sender, EventArgs e)
	{
		if (sourceListView.SelectedItems.Count <= 0)
		{
			return;
		}

		ListViewItem selectedItem = sourceListView.SelectedItems[0];
		if (selectedItem.Index <= 0)
		{
			return;
		}

		ListViewItem previousItem = sourceListView.Items[selectedItem.Index - 1];
		SourceItem selectedSource = selectedItem.Tag as SourceItem;
		SourceItem previousSource = previousItem.Tag as SourceItem;
		if (selectedSource.IsSecondarySource && !previousSource.IsSecondarySource)
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
	}

	private void MoveDownButton_Click(object sender, EventArgs e)
	{
		if (sourceListView.SelectedItems.Count <= 0)
		{
			return;
		}

		ListViewItem selectedItem = sourceListView.SelectedItems[0];
		if (selectedItem.Index >= sourceListView.Items.Count - 1)
		{
			return;
		}

		ListViewItem nextItem = sourceListView.Items[selectedItem.Index + 1];
		SourceItem selectedSource = selectedItem.Tag as SourceItem;
		SourceItem nextSource = nextItem.Tag as SourceItem;
		if (!selectedSource.IsSecondarySource && nextSource.IsSecondarySource)
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
	}

	private void ButtonStateTimer_Tick(object sender, EventArgs e)
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
		buttonStateTimer = new Timer(components);
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
		buttonStateTimer.Enabled = true;
		buttonStateTimer.Interval = 500;
		buttonStateTimer.Tick += ButtonStateTimer_Tick;
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
