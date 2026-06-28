using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using MusicTagWinApp.Exporters;
using MusicTagWinApp.Instances;
using MusicTagWinApp.Properties;

namespace MusicTagWinApp.Common;

internal class DirectoryManagerDialog : Form
{
	private ListViewFileSetting fileSetting;

	private readonly List<ListViewFileSettingFileInfo> removedDirectories = new List<ListViewFileSettingFileInfo>();

	private IContainer components;

	private FlowLayoutPanel mainPanel;

	private HeaderAwareListView directoryListView;

	private ColumnHeader directoryColumn;

	private FlowLayoutPanel buttonPanel;

	private Button okButton;

	private Button cancelButton;

	private ContextMenuStrip directoryContextMenu;

	private ToolStripMenuItem deleteMenuItem;

	public bool HasChanges { get; private set; }

	public DirectoryManagerDialog()
	{
		InitializeComponent();
		LayoutControls();
		Text = Resources.managedirs;
		okButton.Text = Resources.OK;
		cancelButton.Text = Resources.Cancel;
		deleteMenuItem.Text = Resources.DeleteItems;
		directoryListView.SmallImageList = new ImageList(components)
		{
			ImageSize = new Size(1, ImageUtilities.ScaleByDpi(32f))
		};
	}

	public void SetFileSetting(ListViewFileSetting setting)
	{
		fileSetting = setting;
	}

	protected override void OnLoad(EventArgs e)
	{
		base.OnLoad(e);
		if (fileSetting == null)
		{
			return;
		}

		lock (fileSetting)
		{
				foreach (ListViewFileSettingFileInfo directoryInfo in fileSetting.List)
				{
					if (directoryInfo.IsAnyFile())
					{
						continue;
					}

					ListViewItem listViewItem = directoryListView.Items.Add(directoryInfo.DirPath);
					listViewItem.Tag = directoryInfo;
					listViewItem.Checked = !directoryInfo.Disabled;
				}
			}
	}

	private void LayoutPanelSizeChanged(object sender, EventArgs e)
	{
		LayoutControls();
	}

	private void LayoutControls()
	{
		DatabaseMapper.FillAndCenterButtons(directoryListView, mainPanel, buttonPanel);
		directoryColumn.Width = directoryListView.ClientSize.Width;
	}

	private void OkButtonClick(object sender, EventArgs e)
	{
		if (fileSetting == null)
		{
			DialogResult = DialogResult.OK;
			Close();
			return;
		}

		foreach (ListViewItem listViewItem in directoryListView.Items)
		{
			if (listViewItem.Tag is ListViewFileSettingFileInfo directoryInfo && directoryInfo.Disabled == listViewItem.Checked)
			{
				directoryInfo.Disabled = !listViewItem.Checked;
				HasChanges = true;
			}
		}

		foreach (ListViewFileSettingFileInfo directoryInfo in removedDirectories)
		{
			fileSetting.RemoveForDir(directoryInfo);
			if (!directoryInfo.Disabled)
			{
				HasChanges = true;
			}
		}

		lock (fileSetting)
		{
			if (!fileSetting.List.Exists(HasEnabledSpecificDirectory))
			{
				fileSetting.ClearForAnyFile();
			}
		}

		DialogResult = DialogResult.OK;
		Close();
	}

	private static bool HasEnabledSpecificDirectory(ListViewFileSettingFileInfo directoryInfo)
	{
		return !directoryInfo.IsAnyFile() && !directoryInfo.Disabled;
	}

	private void CancelButtonClick(object sender, EventArgs e)
	{
		DialogResult = DialogResult.Cancel;
		Close();
	}

	private void DeleteSelectedDirectories()
	{
		List<ListViewItem> selectedItems = new List<ListViewItem>();
		foreach (ListViewItem selectedItem in directoryListView.SelectedItems)
		{
			selectedItems.Add(selectedItem);
		}

		foreach (ListViewItem selectedItem in selectedItems)
		{
			if (selectedItem.Tag is ListViewFileSettingFileInfo directoryInfo)
			{
				removedDirectories.Add(directoryInfo);
			}
			directoryListView.Items.Remove(selectedItem);
		}
	}

	private void DirectoryListViewKeyUp(object sender, KeyEventArgs e)
	{
		if (directoryListView.FocusedItem == null || directoryListView.SelectedItems.Count <= 0)
		{
			return;
		}

		if (e.KeyCode == Keys.Delete || e.KeyCode == Keys.Decimal)
		{
			DeleteSelectedDirectories();
		}
	}

	private void DirectoryListViewMouseUp(object sender, MouseEventArgs e)
	{
		if (e.Button == MouseButtons.Right && directoryListView.FocusedItem != null && directoryListView.SelectedItems.Count > 0)
		{
			directoryContextMenu.Show(directoryListView, e.Location);
		}
	}

	private void DeleteMenuItemClick(object sender, EventArgs e)
	{
		DeleteSelectedDirectories();
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			components?.Dispose();
		}
		base.Dispose(disposing);
	}

	private void InitializeComponent()
	{
		components = new Container();
		mainPanel = new FlowLayoutPanel();
		directoryListView = new HeaderAwareListView();
		directoryColumn = new ColumnHeader();
		buttonPanel = new FlowLayoutPanel();
		okButton = new Button();
		cancelButton = new Button();
		directoryContextMenu = new ContextMenuStrip(components);
		deleteMenuItem = new ToolStripMenuItem();
		mainPanel.SuspendLayout();
		buttonPanel.SuspendLayout();
		directoryContextMenu.SuspendLayout();
		SuspendLayout();
		mainPanel.Controls.Add(directoryListView);
		mainPanel.Controls.Add(buttonPanel);
		mainPanel.Dock = DockStyle.Fill;
		mainPanel.FlowDirection = FlowDirection.TopDown;
		mainPanel.Font = new Font("Tahoma", 9f, FontStyle.Regular, GraphicsUnit.Point, 0);
		mainPanel.Location = new Point(0, 0);
		mainPanel.Name = "flowLayoutPanel1";
		mainPanel.Size = new Size(484, 620);
		mainPanel.TabIndex = 1;
		mainPanel.WrapContents = false;
		mainPanel.SizeChanged += LayoutPanelSizeChanged;
		directoryListView.CheckBoxes = true;
		directoryListView.Columns.AddRange(new ColumnHeader[1] { directoryColumn });
		directoryListView.Font = new Font("Tahoma", 9f, FontStyle.Regular, GraphicsUnit.Point, 0);
		directoryListView.FullRowSelect = true;
		directoryListView.GridLines = true;
		directoryListView.HeaderStyle = ColumnHeaderStyle.None;
		directoryListView.HideSelection = false;
		directoryListView.Location = new Point(0, 0);
		directoryListView.Margin = new Padding(0);
		directoryListView.Name = "listView1";
		directoryListView.Size = new Size(481, 555);
		directoryListView.TabIndex = 0;
		directoryListView.UseCompatibleStateImageBehavior = false;
		directoryListView.View = View.Details;
		directoryListView.KeyUp += DirectoryListViewKeyUp;
		directoryListView.MouseUp += DirectoryListViewMouseUp;
		directoryColumn.Text = "";
		directoryColumn.Width = 150;
		buttonPanel.Controls.Add(okButton);
		buttonPanel.Controls.Add(cancelButton);
		buttonPanel.Location = new Point(0, 567);
		buttonPanel.Margin = new Padding(0, 12, 0, 12);
		buttonPanel.Name = "flowLayoutPanel2";
		buttonPanel.Size = new Size(220, 35);
		buttonPanel.TabIndex = 5;
		okButton.Location = new Point(0, 0);
		okButton.Margin = new Padding(0);
		okButton.Name = "btnOK";
		okButton.Size = new Size(100, 35);
		okButton.TabIndex = 1;
		okButton.Text = "OK";
		okButton.UseVisualStyleBackColor = true;
		okButton.Click += OkButtonClick;
		cancelButton.Location = new Point(120, 0);
		cancelButton.Margin = new Padding(20, 0, 0, 0);
		cancelButton.Name = "btnCancel";
		cancelButton.Size = new Size(100, 35);
		cancelButton.TabIndex = 2;
		cancelButton.Text = "Cancel";
		cancelButton.UseVisualStyleBackColor = true;
		cancelButton.Click += CancelButtonClick;
		directoryContextMenu.Items.AddRange(new ToolStripItem[1] { deleteMenuItem });
		directoryContextMenu.Name = "contextMenuStrip1";
		directoryContextMenu.Size = new Size(114, 26);
		deleteMenuItem.Name = "deleteToolStripMenuItem";
		deleteMenuItem.Size = new Size(113, 22);
		deleteMenuItem.Text = "Delete";
		deleteMenuItem.Click += DeleteMenuItemClick;
		AutoScaleDimensions = new SizeF(96f, 96f);
		AutoScaleMode = AutoScaleMode.Dpi;
		ClientSize = new Size(484, 620);
		Controls.Add(mainPanel);
		Font = new Font("Tahoma", 9f, FontStyle.Regular, GraphicsUnit.Point, 0);
		MinimumSize = new Size(300, 500);
		Name = "FormManageDirs";
		ShowIcon = false;
		StartPosition = FormStartPosition.CenterParent;
		Text = "FormManageDirs";
		mainPanel.ResumeLayout(performLayout: false);
		buttonPanel.ResumeLayout(performLayout: false);
		directoryContextMenu.ResumeLayout(performLayout: false);
		ResumeLayout(performLayout: false);
	}
}
