using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using MusicTagWinApp.Properties;

namespace MusicTag.Candidates;

internal class LyricTimeOffsetDialog : Form
{
	private IContainer components;

	private NumericUpDown offsetNumericUpDown;

	private Label unitLabel;

	private Button okButton;

	private Button cancelButton;

	public int OffsetMilliseconds => decimal.ToInt32(offsetNumericUpDown.Value);

	public LyricTimeOffsetDialog()
	{
		InitializeComponent();
		Text = Resources.adjusttimetag;
		unitLabel.Text = Resources.ms;
		okButton.Text = Resources.OK;
		cancelButton.Text = Resources.Cancel;
		unitLabel.Top = offsetNumericUpDown.Top + 2;
	}

	private void OkButton_Click(object sender, EventArgs e)
	{
		DialogResult = DialogResult.OK;
		Close();
	}

	private void CancelButton_Click(object sender, EventArgs e)
	{
		DialogResult = DialogResult.Cancel;
		Close();
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
		offsetNumericUpDown = new NumericUpDown();
		unitLabel = new Label();
		okButton = new Button();
		cancelButton = new Button();
		((ISupportInitialize)offsetNumericUpDown).BeginInit();
		SuspendLayout();
		offsetNumericUpDown.Increment = new decimal(new int[4] { 100, 0, 0, 0 });
		offsetNumericUpDown.Location = new Point(37, 23);
		offsetNumericUpDown.Margin = new Padding(0);
		offsetNumericUpDown.Maximum = new decimal(new int[4] { 100000000, 0, 0, 0 });
		offsetNumericUpDown.Minimum = new decimal(new int[4] { 100000000, 0, 0, -2147483648 });
		offsetNumericUpDown.Name = "nudNum";
		offsetNumericUpDown.Size = new Size(140, 22);
		offsetNumericUpDown.TabIndex = 0;
		unitLabel.AutoSize = true;
		unitLabel.Location = new Point(185, 27);
		unitLabel.Name = "lblNum";
		unitLabel.Size = new Size(22, 14);
		unitLabel.TabIndex = 1;
		unitLabel.Text = "ms";
		unitLabel.TextAlign = ContentAlignment.MiddleLeft;
		okButton.Location = new Point(37, 65);
		okButton.Margin = new Padding(0);
		okButton.Name = "btnOK";
		okButton.Size = new Size(75, 23);
		okButton.TabIndex = 2;
		okButton.Text = "OK";
		okButton.UseVisualStyleBackColor = true;
		okButton.Click += OkButton_Click;
		cancelButton.Location = new Point(133, 65);
		cancelButton.Margin = new Padding(0);
		cancelButton.Name = "btnCancel";
		cancelButton.Size = new Size(75, 23);
		cancelButton.TabIndex = 3;
		cancelButton.Text = "Cancel";
		cancelButton.UseVisualStyleBackColor = true;
		cancelButton.Click += CancelButton_Click;
		AutoScaleDimensions = new SizeF(96f, 96f);
		AutoScaleMode = AutoScaleMode.Dpi;
		ClientSize = new Size(244, 106);
		Controls.Add(cancelButton);
		Controls.Add(okButton);
		Controls.Add(unitLabel);
		Controls.Add(offsetNumericUpDown);
		Font = new Font("Tahoma", 9f, FontStyle.Regular, GraphicsUnit.Point, 0);
		FormBorderStyle = FormBorderStyle.FixedDialog;
		MaximizeBox = false;
		MinimizeBox = false;
		Name = "FormLyricAdjustTimetag";
		ShowIcon = false;
		StartPosition = FormStartPosition.CenterParent;
		Text = "Adjust timetag";
		((ISupportInitialize)offsetNumericUpDown).EndInit();
		ResumeLayout(performLayout: false);
		PerformLayout();
	}
}
