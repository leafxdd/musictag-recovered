using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;
using MusicTagWinApp.Exporters;
using MusicTagWinApp.Instances;
using MusicTagWinApp.Properties;
using Newtonsoft.Json;

namespace MusicTag.Importers;

internal class CombinedTagOverwriteOptionsDialog : Form
{
	private static readonly Dictionary<string, bool> overwriteOptions = new Dictionary<string, bool>
	{
		{ "title", true },
		{ "artist", true },
		{ "album", true },
		{ "year", true },
		{ "trackstr", true },
		{ "discstr", true },
		{ "genre", true },
		{ "comment", false },
		{ "lyrics", true },
		{ "picture", true }
	};

	private IContainer components;

	private HeaderAwareListView optionListView;

	private ColumnHeader itemColumn;

	private FlowLayoutPanel mainPanel;

	private FlowLayoutPanel buttonPanel;

	private Button okButton;

	private Button cancelButton;

	static CombinedTagOverwriteOptionsDialog()
	{
		Dictionary<string, bool> savedOptions = TryDeserializeOverwriteOptions();
		if (savedOptions == null)
		{
			return;
		}
		foreach (KeyValuePair<string, bool> option in savedOptions)
		{
			ApplySavedOverwriteOption(option);
		}
	}

	public CombinedTagOverwriteOptionsDialog()
	{
		InitializeComponent();
		LayoutControls();
		itemColumn.Text = Resources.Item;
		Text = Resources.OverwriteOptions;
		okButton.Text = Resources.OK;
		cancelButton.Text = Resources.Cancel;
		optionListView.SmallImageList = new ImageList
		{
			ImageSize = new Size(1, DatabaseMapper.ScaleByDpi(32f))
		};
		foreach (KeyValuePair<string, bool> option in GetOverwriteOptions())
		{
			AddOverwriteOption(option);
		}
	}

	public static Dictionary<string, bool> GetOverwriteOptions()
	{
		return overwriteOptions;
	}

	private void LayoutControls()
	{
		optionListView.Width = mainPanel.Width;
		optionListView.Height = mainPanel.Height - buttonPanel.Height - buttonPanel.Margin.Top - buttonPanel.Margin.Bottom;
		int horizontalMargin = (mainPanel.Width - buttonPanel.Width) / 2;
		buttonPanel.Margin = new Padding(horizontalMargin, buttonPanel.Margin.Top, horizontalMargin, buttonPanel.Margin.Bottom);
		optionListView.Columns[0].Width = optionListView.ClientSize.Width;
	}

	private void SaveButtonClick(object sender, EventArgs e)
	{
		foreach (ListViewItem item in optionListView.Items)
		{
			if (item.Tag is string fieldName)
			{
				GetOverwriteOptions()[fieldName] = item.Checked;
			}
		}
		Settings.Default.CombTagsSearchOverwriteOptions = JsonConvert.SerializeObject(GetOverwriteOptions());
		DialogResult = DialogResult.OK;
		Close();
	}

	private static Dictionary<string, bool> TryDeserializeOverwriteOptions()
	{
		try
		{
			return JsonConvert.DeserializeObject<Dictionary<string, bool>>(Settings.Default.CombTagsSearchOverwriteOptions);
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private void CancelButtonClick(object sender, EventArgs e)
	{
		DialogResult = DialogResult.Cancel;
		Close();
	}

	private void DrawColumnHeader(object sender, DrawListViewColumnHeaderEventArgs e)
	{
		if (e.ColumnIndex != itemColumn.Index)
		{
			return;
		}
		e.DrawDefault = false;
		e.DrawBackground();
		bool isChecked = itemColumn.Tag is bool checkedValue && checkedValue;
		Point checkBoxLocation = new Point(e.Bounds.Left + 4, e.Bounds.Top + DatabaseMapper.ScaleByDpi(4f));
		CheckBoxRenderer.DrawCheckBox(e.Graphics, checkBoxLocation, isChecked ? CheckBoxState.CheckedNormal : CheckBoxState.UncheckedNormal);
		using SolidBrush textBrush = new SolidBrush(e.ForeColor);
		e.Graphics.DrawString(e.Header.Text, e.Font, textBrush, e.Bounds.Left + DatabaseMapper.ScaleByDpi(16f) + 4, e.Bounds.Top + 4);
	}

	private void DrawSubItem(object sender, DrawListViewSubItemEventArgs e)
	{
		e.DrawDefault = true;
	}

	private void HeaderClick(object sender, ColumnClickEventArgs e)
	{
		if (e.Column != itemColumn.Index)
		{
			return;
		}
		bool isChecked = !(itemColumn.Tag is bool checkedValue && checkedValue);
		optionListView.Columns[e.Column].Tag = isChecked;
		foreach (ListViewItem item in optionListView.Items)
		{
			item.Checked = isChecked;
		}
		optionListView.Invalidate();
	}

	private void OptionCheckedChanged(object sender, ItemCheckedEventArgs e)
	{
		if (e.Item.Checked && !optionListView.Items.Cast<ListViewItem>().Any(IsUncheckedListViewItem))
		{
			optionListView.Columns[0].Tag = true;
			optionListView.Invalidate(invalidateChildren: true);
			return;
		}
		optionListView.Columns[0].Tag = false;
		optionListView.Invalidate(invalidateChildren: true);
	}

	private static void ApplySavedOverwriteOption(KeyValuePair<string, bool> entry)
	{
		if (GetOverwriteOptions().ContainsKey(entry.Key))
		{
			GetOverwriteOptions()[entry.Key] = entry.Value;
		}
	}

	private static bool IsUncheckedListViewItem(ListViewItem item)
	{
		return !item.Checked;
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
		optionListView = new HeaderAwareListView();
		itemColumn = new ColumnHeader();
		buttonPanel = new FlowLayoutPanel();
		okButton = new Button();
		cancelButton = new Button();
		mainPanel.SuspendLayout();
		buttonPanel.SuspendLayout();
		SuspendLayout();
		mainPanel.Controls.Add(optionListView);
		mainPanel.Controls.Add(buttonPanel);
		mainPanel.Dock = DockStyle.Fill;
		mainPanel.FlowDirection = FlowDirection.TopDown;
		mainPanel.Font = new Font("Tahoma", 9f, FontStyle.Regular, GraphicsUnit.Point, 0);
		mainPanel.Location = new Point(0, 0);
		mainPanel.Margin = new Padding(0);
		mainPanel.Name = "flowLayoutPanel1";
		mainPanel.Size = new Size(284, 411);
		mainPanel.TabIndex = 2;
		mainPanel.WrapContents = false;
		optionListView.CheckBoxes = true;
		optionListView.Columns.AddRange(new ColumnHeader[1] { itemColumn });
		optionListView.Font = new Font("Tahoma", 9f, FontStyle.Regular, GraphicsUnit.Point, 0);
		optionListView.FullRowSelect = true;
		optionListView.GridLines = true;
		optionListView.HideSelection = false;
		optionListView.Location = new Point(0, 0);
		optionListView.Margin = new Padding(0);
		optionListView.MultiSelect = false;
		optionListView.Name = "listView1";
		optionListView.OwnerDraw = true;
		optionListView.Size = new Size(284, 344);
		optionListView.TabIndex = 0;
		optionListView.UseCompatibleStateImageBehavior = false;
		optionListView.View = View.Details;
		optionListView.ColumnClick += HeaderClick;
		optionListView.DrawColumnHeader += DrawColumnHeader;
		optionListView.DrawSubItem += DrawSubItem;
		optionListView.ItemChecked += OptionCheckedChanged;
		itemColumn.Text = "";
		itemColumn.Width = 150;
		buttonPanel.Controls.Add(okButton);
		buttonPanel.Controls.Add(cancelButton);
		buttonPanel.Location = new Point(0, 356);
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
		okButton.Click += SaveButtonClick;
		cancelButton.Location = new Point(120, 0);
		cancelButton.Margin = new Padding(20, 0, 0, 0);
		cancelButton.Name = "btnCancel";
		cancelButton.Size = new Size(100, 35);
		cancelButton.TabIndex = 2;
		cancelButton.Text = "Cancel";
		cancelButton.UseVisualStyleBackColor = true;
		cancelButton.Click += CancelButtonClick;
		AutoScaleDimensions = new SizeF(96f, 96f);
		AutoScaleMode = AutoScaleMode.Dpi;
		ClientSize = new Size(284, 411);
		Controls.Add(mainPanel);
		Font = new Font("Tahoma", 9f, FontStyle.Regular, GraphicsUnit.Point, 0);
		FormBorderStyle = FormBorderStyle.FixedDialog;
		MaximizeBox = false;
		MinimizeBox = false;
		Name = "FormCombTagsSearchOwOptions";
		ShowIcon = false;
		StartPosition = FormStartPosition.CenterParent;
		Text = "FormCombTagsSearchOwOptions";
		mainPanel.ResumeLayout(performLayout: false);
		buttonPanel.ResumeLayout(performLayout: false);
		ResumeLayout(performLayout: false);
	}

	private void AddOverwriteOption(KeyValuePair<string, bool> option)
	{
		ListViewItem listViewItem = optionListView.Items.Add(Resources.ResourceManager.GetString(option.Key));
		listViewItem.Checked = option.Value;
		listViewItem.Tag = option.Key;
	}
}
