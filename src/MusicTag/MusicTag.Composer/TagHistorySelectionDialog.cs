using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using MusicTag.States;
using MusicTagWinApp.Instances;
using MusicTagWinApp.Listeners;
using MusicTagWinApp.Properties;

namespace MusicTag.Composer;

internal class TagHistorySelectionDialog : Form
{
	private string historyFilePath;

	private ConfigDescriptorState selectedTagState;

	private IContainer components;

	private FlowLayoutPanel mainPanel;

	private ListView historyListView;

	private ColumnHeader titleColumn;

	private ColumnHeader artistColumn;

	private ColumnHeader albumColumn;

	private ColumnHeader yearColumn;

	private ColumnHeader trackColumn;

	private ColumnHeader discColumn;

	private ImageList rowHeightImageList;

	private FlowLayoutPanel footerPanel;

	private FlowLayoutPanel buttonPanel;

	private Button okButton;

	private Button cancelButton;

	private ColumnHeader recordTimeColumn;

	public void SetHistoryFilePath(string filePath)
	{
		historyFilePath = filePath;
	}

	public ConfigDescriptorState GetSelectedTagState()
	{
		return selectedTagState;
	}

	public TagHistorySelectionDialog()
	{
		InitializeComponent();
		ScaleControlsForDpi();
		UpdateLayout();
		ApplyLocalizedText();
	}

	private void ScaleControlsForDpi()
	{
		rowHeightImageList.ImageSize = new Size(1, DatabaseMapper.ScaleByDpi(40f));
		foreach (ColumnHeader column in historyListView.Columns)
		{
			column.Width = DatabaseMapper.ScaleByDpi(column.Width);
		}
	}

	private void UpdateLayout()
	{
		historyListView.Width = mainPanel.Width;
		historyListView.Height = mainPanel.Height - footerPanel.Height;
		int left = (footerPanel.Width - buttonPanel.Width) / 2;
		buttonPanel.Margin = new Padding(left, buttonPanel.Margin.Top, 0, buttonPanel.Margin.Bottom);
	}

	private void ApplyLocalizedText()
	{
		Text = Resources.TagsHistory;
		okButton.Text = Resources.OK;
		cancelButton.Text = Resources.Cancel;
		foreach (ColumnHeader column in historyListView.Columns)
		{
			column.Text = Resources.ResourceManager.GetString(column.Tag.ToString(), Resources.Culture);
		}
	}

	protected override void OnShown(EventArgs e)
	{
		base.OnShown(e);
		using TagHistoryRepository tagHistoryRepository = new TagHistoryRepository(useTransaction: false);
		TagHistoryRepository.GetHistoryRecords(historyFilePath, newestFirst: true, tagHistoryRepository).ForEach(AddHistoryRecord);
	}

	private void MainLayout_SizeChanged(object sender, EventArgs e)
	{
		UpdateLayout();
	}

	private void OkButton_Click(object sender, EventArgs e)
	{
		if (historyListView.SelectedItems.Count <= 0)
		{
			DatabaseMapper.ShowErrorMessage(Resources.Msg_PleaseSelectItem);
			return;
		}
		int selectedHistoryIndex = historyListView.SelectedItems[0].Index;
		selectedTagState = new ConfigDescriptorState();
		foreach (ColumnHeader column in historyListView.Columns)
		{
			string fieldName = column.Tag.ToString();
			int columnIndex = column.Index;
			selectedTagState[fieldName] = historyListView.Items[selectedHistoryIndex].SubItems[columnIndex].Text;
		}
		base.DialogResult = DialogResult.OK;
		Close();
	}

	private void CancelButton_Click(object sender, EventArgs e)
	{
		base.DialogResult = DialogResult.Cancel;
		Close();
	}

	private void HistoryList_DoubleClick(object sender, EventArgs e)
	{
		if (historyListView.FocusedItem != null && historyListView.SelectedItems.Count > 0)
		{
			okButton.PerformClick();
		}
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing && components != null)
		{
			components.Dispose();
		}

		base.Dispose(disposing);
	}

	private void InitializeComponent()
	{
		components = new Container();
		mainPanel = new FlowLayoutPanel();
		historyListView = new ListView();
		titleColumn = new ColumnHeader();
		artistColumn = new ColumnHeader();
		albumColumn = new ColumnHeader();
		yearColumn = new ColumnHeader();
		trackColumn = new ColumnHeader();
		discColumn = new ColumnHeader();
		recordTimeColumn = new ColumnHeader();
		rowHeightImageList = new ImageList(components);
		footerPanel = new FlowLayoutPanel();
		buttonPanel = new FlowLayoutPanel();
		okButton = new Button();
		cancelButton = new Button();

		mainPanel.SuspendLayout();
		footerPanel.SuspendLayout();
		buttonPanel.SuspendLayout();
		SuspendLayout();

		mainPanel.Controls.Add(historyListView);
		mainPanel.Controls.Add(footerPanel);
		mainPanel.Dock = DockStyle.Fill;
		mainPanel.FlowDirection = FlowDirection.TopDown;
		mainPanel.Location = new Point(0, 0);
		mainPanel.Name = "flowLayoutPanel1";
		mainPanel.Size = new Size(680, 303);
		mainPanel.TabIndex = 2;
		mainPanel.WrapContents = false;
		mainPanel.SizeChanged += MainLayout_SizeChanged;

		historyListView.Columns.AddRange(new ColumnHeader[7] { titleColumn, artistColumn, albumColumn, yearColumn, trackColumn, discColumn, recordTimeColumn });
		historyListView.FullRowSelect = true;
		historyListView.GridLines = true;
		historyListView.HideSelection = false;
		historyListView.Location = new Point(0, 0);
		historyListView.Margin = new Padding(0);
		historyListView.MultiSelect = false;
		historyListView.Name = "listView1";
		historyListView.Size = new Size(679, 239);
		historyListView.SmallImageList = rowHeightImageList;
		historyListView.TabIndex = 0;
		historyListView.UseCompatibleStateImageBehavior = false;
		historyListView.View = View.Details;
		historyListView.DoubleClick += HistoryList_DoubleClick;

		titleColumn.Tag = "title";
		titleColumn.Text = "Title";
		titleColumn.Width = 130;
		artistColumn.Tag = "artist";
		artistColumn.Text = "Artist";
		artistColumn.Width = 130;
		albumColumn.Tag = "album";
		albumColumn.Text = "Album";
		albumColumn.Width = 130;
		yearColumn.Tag = "year";
		yearColumn.Text = "Year";
		yearColumn.Width = 50;
		trackColumn.Tag = "trackstr";
		trackColumn.Text = "Track";
		trackColumn.Width = 50;
		discColumn.Tag = "discstr";
		discColumn.Text = "Disc";
		discColumn.Width = 50;
		recordTimeColumn.Tag = "his_ctime";
		recordTimeColumn.Text = "Record time";
		recordTimeColumn.Width = 135;

		rowHeightImageList.ColorDepth = ColorDepth.Depth8Bit;
		rowHeightImageList.ImageSize = new Size(1, 40);
		rowHeightImageList.TransparentColor = Color.Transparent;

		footerPanel.Controls.Add(buttonPanel);
		footerPanel.Dock = DockStyle.Fill;
		footerPanel.Location = new Point(0, 239);
		footerPanel.Margin = new Padding(0);
		footerPanel.Name = "flowLayoutPanel2";
		footerPanel.Size = new Size(679, 60);
		footerPanel.TabIndex = 7;
		footerPanel.WrapContents = false;

		buttonPanel.Controls.Add(okButton);
		buttonPanel.Controls.Add(cancelButton);
		buttonPanel.Location = new Point(0, 12);
		buttonPanel.Margin = new Padding(0, 12, 0, 0);
		buttonPanel.Name = "flowLayoutPanel3";
		buttonPanel.Size = new Size(220, 35);
		buttonPanel.TabIndex = 6;

		okButton.Location = new Point(0, 0);
		okButton.Margin = new Padding(0);
		okButton.Name = "btnOK";
		okButton.Size = new Size(100, 35);
		okButton.TabIndex = 1;
		okButton.Text = "OK";
		okButton.UseVisualStyleBackColor = true;
		okButton.Click += OkButton_Click;

		cancelButton.Location = new Point(120, 0);
		cancelButton.Margin = new Padding(20, 0, 0, 0);
		cancelButton.Name = "btnCancel";
		cancelButton.Size = new Size(100, 35);
		cancelButton.TabIndex = 2;
		cancelButton.Text = "Cancel";
		cancelButton.UseVisualStyleBackColor = true;
		cancelButton.Click += CancelButton_Click;

		AutoScaleDimensions = new SizeF(96f, 96f);
		AutoScaleMode = AutoScaleMode.Dpi;
		ClientSize = new Size(680, 303);
		Controls.Add(mainPanel);
		Font = new Font("Tahoma", 9f, FontStyle.Regular, GraphicsUnit.Point, 0);
		MinimumSize = new Size(300, 300);
		Name = "FormTagsHistory";
		ShowIcon = false;
		StartPosition = FormStartPosition.CenterParent;
		Text = "Tags History";

		mainPanel.ResumeLayout(performLayout: false);
		footerPanel.ResumeLayout(performLayout: false);
		buttonPanel.ResumeLayout(performLayout: false);
		ResumeLayout(performLayout: false);
	}

	private void AddHistoryRecord(ConfigDescriptorState tagState)
	{
		ListViewItem historyItem = historyListView.Items.Add(tagState.GetDisplayValue("title"));
		foreach (ColumnHeader column in historyListView.Columns)
		{
			if (column.Index > 0)
			{
				historyItem.SubItems.Add(tagState.GetDisplayValue(column.Tag.ToString()));
			}
		}
	}

}
