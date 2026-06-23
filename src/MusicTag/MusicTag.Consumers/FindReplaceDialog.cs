using System;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MusicTag.Serialization;
using MusicTagWinApp.Instances;
using MusicTagWinApp.Properties;

namespace MusicTag.Consumers;

internal class FindReplaceDialog : Form
{
	private TextBoxFindReplaceController findReplaceController;

	private IContainer components;

	private FlowLayoutPanel mainLayoutPanel;

	private FlowLayoutPanel findRowPanel;

	private Label findWhatLabel;

	private TextBox findWhatTextBox;

	private Button findPreviousButton;

	private Button findNextButton;

	private FlowLayoutPanel replaceRowPanel;

	private Label replaceWithLabel;

	private TextBox replaceWithTextBox;

	private Button replaceButton;

	private FlowLayoutPanel replaceAllRowPanel;

	private Button replaceAllButton;

	private FlowLayoutPanel optionsRowPanel;

	private CheckBox matchCaseCheckBox;

	private Button cancelButton;

	private ToolTip toolTip;

	public TextBoxFindReplaceController FindReplaceController
	{
		get => findReplaceController;
		set => findReplaceController = value;
	}

	public FindReplaceDialog()
	{
		InitializeComponent();
		ApplyLocalizedText();
		ApplyScaledLayout();
		Load += FindReplaceDialog_Load;
	}

	private void ApplyLocalizedText()
	{
		Text = Resources.FindOrReplace;
		findWhatLabel.Text = "Find what:";
		replaceWithLabel.Text = "Replace with:";
		replaceButton.Text = "Replace";
		replaceAllButton.Text = "Replace All";
		cancelButton.Text = Resources.Cancel;
		matchCaseCheckBox.Text = "Match case";

		FontAwesome.Properties fontProperties = new FontAwesome.Properties
		{
			Size = DatabaseMapper.ScaleByDpi(20f),
			ShowBorder = false
		};
		findPreviousButton.Image = FontAwesome.Type.AngleUp.AsImage(fontProperties);
		findNextButton.Image = FontAwesome.Type.AngleDown.AsImage(fontProperties);
		findPreviousButton.Text = "";
		findNextButton.Text = "";
		toolTip.SetToolTip(findPreviousButton, Resources.FindPrevious);
		toolTip.SetToolTip(findNextButton, Resources.FindNext);
	}

	private void ApplyScaledLayout()
	{
		findWhatLabel.Width = DatabaseMapper.ScaleByDpi(100f);
		replaceWithLabel.Width = findWhatLabel.Width;
		findWhatTextBox.Width = DatabaseMapper.ScaleByDpi(250f);
		replaceWithTextBox.Width = findWhatTextBox.Width;
		findPreviousButton.Width = DatabaseMapper.ScaleByDpi(31f);
		findNextButton.Width = findPreviousButton.Width;
		replaceButton.Width = DatabaseMapper.ScaleByDpi(77f);
		replaceAllButton.Width = replaceButton.Width;
		cancelButton.Width = replaceButton.Width;

		findWhatLabel.Height = replaceButton.Height;
		replaceWithLabel.Height = replaceAllButton.Height;
		matchCaseCheckBox.Height = cancelButton.Height;
		findWhatTextBox.Margin = new Padding(0, (replaceButton.Height - findWhatTextBox.Height) / 2, 0, 0);
		replaceWithTextBox.Margin = new Padding(0, (replaceAllButton.Height - replaceWithTextBox.Height) / 2, 0, 0);
		replaceAllButton.Margin = new Padding(replaceButton.Location.X, 0, 0, 0);
		cancelButton.Margin = new Padding(replaceButton.Location.X - matchCaseCheckBox.Width, 0, 0, 0);

		Width = findNextButton.Location.X + findNextButton.Width + DatabaseMapper.ScaleByDpi(50f);
		Height = optionsRowPanel.Location.Y + optionsRowPanel.Height + DatabaseMapper.ScaleByDpi(60f);
	}

	private void FindReplaceDialog_Load(object sender, EventArgs e)
	{
		if (FindReplaceController != null)
		{
			findWhatTextBox.Text = FindReplaceController.SearchText;
			matchCaseCheckBox.Checked = FindReplaceController.MatchCase;
		}

		if (Owner != null)
		{
			Location = new Point(Owner.Location.X + Owner.Size.Width / 2 - Size.Width / 2, Owner.Location.Y + Owner.Size.Height / 2 - Size.Height / 2);
		}
	}

	private void CancelButton_Click(object sender, EventArgs e)
	{
		Close();
	}

	private void FindWhatTextBox_TextChanged(object sender, EventArgs e)
	{
		bool hasSearchText = findWhatTextBox.Text.Any();
		findPreviousButton.Enabled = hasSearchText;
		findNextButton.Enabled = hasSearchText;
		replaceButton.Enabled = hasSearchText;
		replaceAllButton.Enabled = hasSearchText;
	}

	private void FindPreviousButton_Click(object sender, EventArgs e)
	{
		FindReplaceController.SearchText = findWhatTextBox.Text;
		FindReplaceController.MatchCase = matchCaseCheckBox.Checked;
		FindReplaceController.FindPrevious();
	}

	private void FindNextButton_Click(object sender, EventArgs e)
	{
		FindReplaceController.SearchText = findWhatTextBox.Text;
		FindReplaceController.MatchCase = matchCaseCheckBox.Checked;
		FindReplaceController.FindNext();
	}

	private void ReplaceButton_Click(object sender, EventArgs e)
	{
		FindReplaceController.SearchText = findWhatTextBox.Text;
		FindReplaceController.MatchCase = matchCaseCheckBox.Checked;
		FindReplaceController.ReplaceCurrentAndFindNext(replaceWithTextBox.Text);
	}

	private void ReplaceAllButton_Click(object sender, EventArgs e)
	{
		FindReplaceController.SearchText = findWhatTextBox.Text;
		FindReplaceController.MatchCase = matchCaseCheckBox.Checked;
		FindReplaceController.ReplaceAll(replaceWithTextBox.Text);
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
		mainLayoutPanel = new FlowLayoutPanel();
		findRowPanel = new FlowLayoutPanel();
		findWhatLabel = new Label();
		findWhatTextBox = new TextBox();
		findPreviousButton = new Button();
		findNextButton = new Button();
		replaceRowPanel = new FlowLayoutPanel();
		replaceWithLabel = new Label();
		replaceWithTextBox = new TextBox();
		replaceButton = new Button();
		replaceAllRowPanel = new FlowLayoutPanel();
		replaceAllButton = new Button();
		optionsRowPanel = new FlowLayoutPanel();
		matchCaseCheckBox = new CheckBox();
		cancelButton = new Button();
		toolTip = new ToolTip(components);

		mainLayoutPanel.SuspendLayout();
		findRowPanel.SuspendLayout();
		replaceRowPanel.SuspendLayout();
		replaceAllRowPanel.SuspendLayout();
		optionsRowPanel.SuspendLayout();
		SuspendLayout();

		mainLayoutPanel.Controls.Add(findRowPanel);
		mainLayoutPanel.Controls.Add(replaceRowPanel);
		mainLayoutPanel.Controls.Add(replaceAllRowPanel);
		mainLayoutPanel.Controls.Add(optionsRowPanel);
		mainLayoutPanel.Dock = DockStyle.Fill;
		mainLayoutPanel.Location = new Point(0, 0);
		mainLayoutPanel.Name = "flowLayoutPanel1";
		mainLayoutPanel.Size = new Size(684, 261);
		mainLayoutPanel.TabIndex = 0;

		findRowPanel.Controls.Add(findWhatLabel);
		findRowPanel.Controls.Add(findWhatTextBox);
		findRowPanel.Controls.Add(findPreviousButton);
		findRowPanel.Controls.Add(findNextButton);
		findRowPanel.Location = new Point(15, 15);
		findRowPanel.Margin = new Padding(15, 15, 0, 0);
		findRowPanel.Name = "flowLayoutPanel2";
		findRowPanel.Size = new Size(669, 23);
		findRowPanel.TabIndex = 0;

		findWhatLabel.Location = new Point(0, 0);
		findWhatLabel.Margin = new Padding(0);
		findWhatLabel.Name = "lblFindWhat";
		findWhatLabel.Size = new Size(100, 23);
		findWhatLabel.TabIndex = 0;
		findWhatLabel.Text = "Find what:";
		findWhatLabel.TextAlign = ContentAlignment.MiddleLeft;

		findWhatTextBox.Location = new Point(100, 0);
		findWhatTextBox.Margin = new Padding(0);
		findWhatTextBox.Name = "tbFindWhat";
		findWhatTextBox.Size = new Size(250, 22);
		findWhatTextBox.TabIndex = 1;
		findWhatTextBox.TextChanged += FindWhatTextBox_TextChanged;

		findPreviousButton.Enabled = false;
		findPreviousButton.Location = new Point(365, 0);
		findPreviousButton.Margin = new Padding(15, 0, 0, 0);
		findPreviousButton.Name = "btnUp";
		findPreviousButton.Size = new Size(30, 23);
		findPreviousButton.TabIndex = 2;
		findPreviousButton.Text = "Up";
		findPreviousButton.UseVisualStyleBackColor = true;
		findPreviousButton.Click += FindPreviousButton_Click;

		findNextButton.Enabled = false;
		findNextButton.Location = new Point(410, 0);
		findNextButton.Margin = new Padding(15, 0, 0, 0);
		findNextButton.Name = "btnDown";
		findNextButton.Size = new Size(30, 23);
		findNextButton.TabIndex = 3;
		findNextButton.Text = "Dn";
		findNextButton.UseVisualStyleBackColor = true;
		findNextButton.Click += FindNextButton_Click;

		replaceRowPanel.Controls.Add(replaceWithLabel);
		replaceRowPanel.Controls.Add(replaceWithTextBox);
		replaceRowPanel.Controls.Add(replaceButton);
		replaceRowPanel.Location = new Point(15, 48);
		replaceRowPanel.Margin = new Padding(15, 10, 0, 0);
		replaceRowPanel.Name = "flowLayoutPanel3";
		replaceRowPanel.Size = new Size(669, 23);
		replaceRowPanel.TabIndex = 1;

		replaceWithLabel.Location = new Point(0, 0);
		replaceWithLabel.Margin = new Padding(0);
		replaceWithLabel.Name = "lblReplaceWith";
		replaceWithLabel.Size = new Size(100, 23);
		replaceWithLabel.TabIndex = 0;
		replaceWithLabel.Text = "Replace with:";
		replaceWithLabel.TextAlign = ContentAlignment.MiddleLeft;

		replaceWithTextBox.Location = new Point(100, 0);
		replaceWithTextBox.Margin = new Padding(0);
		replaceWithTextBox.Name = "tbReplaceWith";
		replaceWithTextBox.Size = new Size(250, 22);
		replaceWithTextBox.TabIndex = 1;

		replaceButton.Enabled = false;
		replaceButton.Location = new Point(365, 0);
		replaceButton.Margin = new Padding(15, 0, 0, 0);
		replaceButton.Name = "btnReplace";
		replaceButton.Size = new Size(78, 23);
		replaceButton.TabIndex = 2;
		replaceButton.Text = "Replace";
		replaceButton.UseVisualStyleBackColor = true;
		replaceButton.Click += ReplaceButton_Click;

		replaceAllRowPanel.Controls.Add(replaceAllButton);
		replaceAllRowPanel.Location = new Point(15, 81);
		replaceAllRowPanel.Margin = new Padding(15, 10, 0, 0);
		replaceAllRowPanel.Name = "flowLayoutPanel4";
		replaceAllRowPanel.Size = new Size(669, 23);
		replaceAllRowPanel.TabIndex = 2;

		replaceAllButton.Enabled = false;
		replaceAllButton.Location = new Point(365, 0);
		replaceAllButton.Margin = new Padding(365, 0, 0, 0);
		replaceAllButton.Name = "btnReplaceAll";
		replaceAllButton.Size = new Size(78, 23);
		replaceAllButton.TabIndex = 2;
		replaceAllButton.Text = "Replace All";
		replaceAllButton.UseVisualStyleBackColor = true;
		replaceAllButton.Click += ReplaceAllButton_Click;

		optionsRowPanel.Controls.Add(matchCaseCheckBox);
		optionsRowPanel.Controls.Add(cancelButton);
		optionsRowPanel.Location = new Point(15, 114);
		optionsRowPanel.Margin = new Padding(15, 10, 0, 0);
		optionsRowPanel.Name = "flowLayoutPanel5";
		optionsRowPanel.Size = new Size(669, 23);
		optionsRowPanel.TabIndex = 3;

		matchCaseCheckBox.Location = new Point(0, 0);
		matchCaseCheckBox.Margin = new Padding(0);
		matchCaseCheckBox.Name = "cbMatchCase";
		matchCaseCheckBox.Padding = new Padding(5, 0, 0, 0);
		matchCaseCheckBox.Size = new Size(200, 23);
		matchCaseCheckBox.TabIndex = 3;
		matchCaseCheckBox.Text = "Match case";
		matchCaseCheckBox.UseVisualStyleBackColor = true;

		cancelButton.Location = new Point(365, 0);
		cancelButton.Margin = new Padding(165, 0, 0, 0);
		cancelButton.Name = "btnCancel";
		cancelButton.Size = new Size(78, 23);
		cancelButton.TabIndex = 2;
		cancelButton.Text = "Cancel";
		cancelButton.UseVisualStyleBackColor = true;
		cancelButton.Click += CancelButton_Click;

		AutoScaleDimensions = new SizeF(96f, 96f);
		AutoScaleMode = AutoScaleMode.Dpi;
		ClientSize = new Size(684, 261);
		Controls.Add(mainLayoutPanel);
		Font = new Font("Tahoma", 9f, FontStyle.Regular, GraphicsUnit.Point, 0);
		MaximizeBox = false;
		MinimizeBox = false;
		Name = "FormFindReplace";
		ShowIcon = false;
		ShowInTaskbar = false;
		Text = "FormFindReplace";

		mainLayoutPanel.ResumeLayout(performLayout: false);
		findRowPanel.ResumeLayout(performLayout: false);
		findRowPanel.PerformLayout();
		replaceRowPanel.ResumeLayout(performLayout: false);
		replaceRowPanel.PerformLayout();
		replaceAllRowPanel.ResumeLayout(performLayout: false);
		optionsRowPanel.ResumeLayout(performLayout: false);
		ResumeLayout(performLayout: false);
	}
}
