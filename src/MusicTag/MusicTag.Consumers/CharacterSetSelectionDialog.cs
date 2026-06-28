using System;
using System.ComponentModel;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using MusicTag.Serialization;
using MusicTag.States;
using MusicTagWinApp.Exporters;
using MusicTagWinApp.Instances;
using MusicTagWinApp.Properties;

namespace MusicTag.Consumers;

internal class CharacterSetSelectionDialog : Form
{
	private ConfigDescriptorState tagState;

	private string[] fieldNames;

	private string[] currentValues;

	private IContainer components;

	private FlowLayoutPanel mainPanel;

	private HeaderAwareListView encodingListView;

	private FlowLayoutPanel buttonPanel;

	private Button okButton;

	private Button cancelButton;

	private ColumnHeader encodingColumn;

	private ColumnHeader previewColumn;

	public CharacterSetSelectionDialog()
	{
		InitializeComponent();
		Text = Resources.characterset;
		encodingColumn.Text = Resources.encoding;
		previewColumn.Text = Resources.content;
		okButton.Text = Resources.OK;
		cancelButton.Text = Resources.Cancel;
		encodingListView.SmallImageList = new ImageList(components)
		{
			ImageSize = new Size(1, ImageUtilities.ScaleByDpi(32f))
		};
		foreach (ColumnHeader columnHeader in encodingListView.Columns)
		{
			columnHeader.Width = ImageUtilities.ScaleByDpi(columnHeader.Width);
		}
	}

	public void SetTagState(ConfigDescriptorState state)
	{
		tagState = state;
	}

	public void SetFieldNames(string[] names)
	{
		fieldNames = names;
	}

	public void SetCurrentValues(string[] values)
	{
		currentValues = values;
	}

	public int SelectedEncodingIndex()
	{
		return encodingListView.SelectedItems[0].Index;
	}

	protected override void OnShown(EventArgs args)
	{
		base.OnShown(args);
		encodingListView.Items.Clear();
		foreach (string encodingName in TagTextEncoding.GetEncodingNames())
		{
			ListViewItem listViewItem = new ListViewItem(encodingName);
			StringBuilder previewBuilder = new StringBuilder();
			for (int index = 0; index < fieldNames.Length; index++)
			{
				string previewText = tagState.DecodeFieldWithEncoding(fieldNames[index], encodingName, currentValues[index]);
				if (string.IsNullOrEmpty(previewText))
				{
					continue;
				}

				if (previewBuilder.Length > 0)
				{
					previewBuilder.Append(";");
				}

				previewBuilder.Append(previewText);
			}

			string preview = previewBuilder.ToString();
			listViewItem.SubItems.Add(preview.Substring(0, Math.Min(preview.Length, 1000)));
			encodingListView.Items.Add(listViewItem);
		}

		BeginInvoke(new Action(UpdateLayoutForSize));
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			components?.Dispose();
		}

		base.Dispose(disposing);
	}

	private void MainPanel_SizeChanged(object sender, EventArgs args)
	{
		UpdateLayoutForSize();
	}

	private void UpdateLayoutForSize()
	{
		DatabaseMapper.FillAndCenterButtons(encodingListView, mainPanel, buttonPanel);
		encodingListView.Columns[1].Width = encodingListView.ClientSize.Width - encodingListView.Columns[0].Width;
	}

	private void OkButton_Click(object sender, EventArgs args)
	{
		if (encodingListView.SelectedItems.Count > 0)
		{
			DialogResult = DialogResult.OK;
			Close();
			return;
		}

		DatabaseMapper.ShowErrorMessage(Resources.Msg_PleaseSelectItem);
	}

	private void CancelButton_Click(object sender, EventArgs args)
	{
		DialogResult = DialogResult.Cancel;
		Close();
	}

	private void EncodingListView_DoubleClick(object sender, EventArgs args)
	{
		if (encodingListView.FocusedItem != null && encodingListView.SelectedItems.Count > 0)
		{
			okButton.PerformClick();
		}
	}

	private void InitializeComponent()
	{
		components = new Container();
		mainPanel = new FlowLayoutPanel();
		encodingListView = new HeaderAwareListView();
		encodingColumn = new ColumnHeader();
		previewColumn = new ColumnHeader();
		buttonPanel = new FlowLayoutPanel();
		okButton = new Button();
		cancelButton = new Button();
		mainPanel.SuspendLayout();
		buttonPanel.SuspendLayout();
		SuspendLayout();
		mainPanel.Controls.Add(encodingListView);
		mainPanel.Controls.Add(buttonPanel);
		mainPanel.Dock = DockStyle.Fill;
		mainPanel.FlowDirection = FlowDirection.TopDown;
		mainPanel.Font = new Font("Tahoma", 9f, FontStyle.Regular, GraphicsUnit.Point, 0);
		mainPanel.Location = new Point(0, 0);
		mainPanel.Name = "flowLayoutPanel1";
		mainPanel.Size = new Size(484, 620);
		mainPanel.TabIndex = 0;
		mainPanel.WrapContents = false;
		mainPanel.SizeChanged += MainPanel_SizeChanged;
		encodingListView.Columns.AddRange(new ColumnHeader[2] { encodingColumn, previewColumn });
		encodingListView.Font = new Font("Tahoma", 9f, FontStyle.Regular, GraphicsUnit.Point, 0);
		encodingListView.FullRowSelect = true;
		encodingListView.GridLines = true;
		encodingListView.HeaderStyle = ColumnHeaderStyle.Nonclickable;
		encodingListView.HideSelection = false;
		encodingListView.Location = new Point(0, 0);
		encodingListView.Margin = new Padding(0);
		encodingListView.MultiSelect = false;
		encodingListView.Name = "listView1";
		encodingListView.Size = new Size(481, 555);
		encodingListView.TabIndex = 0;
		encodingListView.UseCompatibleStateImageBehavior = false;
		encodingListView.View = View.Details;
		encodingListView.DoubleClick += EncodingListView_DoubleClick;
		encodingColumn.Text = "Encoding";
		encodingColumn.Width = 150;
		previewColumn.Text = "Content";
		previewColumn.Width = 381;
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
		ClientSize = new Size(484, 620);
		Controls.Add(mainPanel);
		MinimumSize = new Size(300, 500);
		Name = "FormCharacterSet";
		ShowIcon = false;
		StartPosition = FormStartPosition.CenterParent;
		Text = "Character Set";
		mainPanel.ResumeLayout(performLayout: false);
		buttonPanel.ResumeLayout(performLayout: false);
		ResumeLayout(performLayout: false);
	}
}
