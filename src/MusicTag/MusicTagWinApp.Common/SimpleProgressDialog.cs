using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using MusicTagWinApp.Instances;
using MusicTagWinApp.Properties;
using MusicTagWinApp.Win32.Taskbar;

namespace MusicTagWinApp.Common;

internal class SimpleProgressDialog : Form
{
	private readonly TaskbarProgressController taskbarProgress;

	private IContainer components;

	private Label messageLabel;

	private Button cancelButton;

	private PictureBox progressImage;

	private Action cancelAction;

	public SimpleProgressDialog(TaskbarProgressController taskbarProgress)
	{
		InitializeComponent();
		this.taskbarProgress = taskbarProgress;
		cancelButton.Text = Resources.Cancel;
		progressImage.Image = DatabaseMapper.LoadResourceBitmap("img_wait");
	}

	public void SetMessage(string message)
	{
		messageLabel.Text = message;
	}

	public void SetCancelAction(Action action)
	{
		cancelAction = action;
	}

	public void ShowProgressDialog()
	{
		ShowDialogIfNotDisposed();
	}

	public void CloseProgressDialog()
	{
		CloseIfNotDisposed();
	}

	public void SetCancelButtonHidden(bool hidden = true)
	{
		cancelButton.Visible = !hidden;
		Height = DatabaseMapper.ScaleByDpi(cancelButton.Visible ? 100 : 60, roundUp: true);
	}

	protected override void OnShown(EventArgs e)
	{
		base.OnShown(e);
		taskbarProgress?.SetProgressState(TaskbarProgressBarStatus.Indeterminate);
	}

	protected override void OnClosed(EventArgs e)
	{
		base.OnClosed(e);
		taskbarProgress?.SetProgressState(TaskbarProgressBarStatus.NoProgress);
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			components?.Dispose();
		}
		base.Dispose(disposing);
	}

	private void ShowDialogIfNotDisposed()
	{
		try
		{
			if (!IsDisposed)
			{
				ShowDialog();
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine(ex.Message);
		}
	}

	private void CloseIfNotDisposed()
	{
		if (!IsDisposed)
		{
			Close();
		}
	}

	private void CancelButtonClick(object sender, EventArgs e)
	{
		cancelAction?.Invoke();
	}

	private void InitializeComponent()
	{
		components = new Container();
		messageLabel = new Label();
		cancelButton = new Button();
		progressImage = new PictureBox();
		((ISupportInitialize)progressImage).BeginInit();
		SuspendLayout();
		messageLabel.AutoSize = true;
		messageLabel.Location = new Point(64, 25);
		messageLabel.Margin = new Padding(0);
		messageLabel.Name = "lblMessage";
		messageLabel.Size = new Size(89, 14);
		messageLabel.TabIndex = 9;
		messageLabel.Text = "Downloading...";
		cancelButton.Location = new Point(150, 60);
		cancelButton.Name = "btnCancel";
		cancelButton.Size = new Size(75, 23);
		cancelButton.TabIndex = 10;
		cancelButton.Text = "Cancel";
		cancelButton.UseVisualStyleBackColor = true;
		cancelButton.Click += CancelButtonClick;
		progressImage.Image = Resources.img_wait;
		progressImage.Location = new Point(16, 16);
		progressImage.Name = "pbProgress";
		progressImage.Size = new Size(32, 32);
		progressImage.SizeMode = PictureBoxSizeMode.Zoom;
		progressImage.TabIndex = 11;
		progressImage.TabStop = false;
		AutoScaleDimensions = new SizeF(96f, 96f);
		AutoScaleMode = AutoScaleMode.Dpi;
		ClientSize = new Size(250, 100);
		ControlBox = false;
		Controls.Add(progressImage);
		Controls.Add(cancelButton);
		Controls.Add(messageLabel);
		Font = new Font("Tahoma", 9f, FontStyle.Regular, GraphicsUnit.Point, 0);
		FormBorderStyle = FormBorderStyle.None;
		Name = "FormProgress2";
		ShowIcon = false;
		ShowInTaskbar = false;
		StartPosition = FormStartPosition.CenterParent;
		Text = "FormProgress2";
		((ISupportInitialize)progressImage).EndInit();
		ResumeLayout(performLayout: false);
		PerformLayout();
	}
}
