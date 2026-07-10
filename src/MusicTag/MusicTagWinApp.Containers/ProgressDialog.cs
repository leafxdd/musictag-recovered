using System;
using System.ComponentModel;
using System.Drawing;
using System.Timers;
using System.Windows.Forms;
using MusicTagWinApp.Instances;
using MusicTagWinApp.Properties;
using MusicTagWinApp.Win32.Taskbar;

namespace MusicTagWinApp.Containers;

internal class ProgressDialog : Form
{
	public delegate void ProgressDialogCallback();

	public event ProgressDialogCallback CancelRequested;

	public event ProgressDialogCallback ProgressUpdate;

	private readonly System.Timers.Timer updateTimer;

	private readonly TaskbarProgressController taskbarProgress;

	private volatile bool isClosing;

	private IContainer components;

	private Label messageLabel;

	private Label progressLabel;

	private ProgressBar progressBar;

	private Button cancelButton;

	public ProgressDialog(TaskbarProgressController taskbarProgress)
	{
		updateTimer = new System.Timers.Timer(1000.0);
		InitializeComponent();
		this.taskbarProgress = taskbarProgress;
		cancelButton.Text = Resources.Cancel;
		updateTimer.AutoReset = true;
		updateTimer.Elapsed += UpdateTimer_Elapsed;
	}

	public void ShowDialogIfNotDisposed()
	{
		if (IsDisposed)
		{
			return;
		}
		try
		{
			ShowDialog();
		}
		finally
		{
			Dispose();
		}
	}

	public void CloseAfterCompletion()
	{
		isClosing = true;
		taskbarProgress?.SetProgressState(TaskbarProgressBarStatus.NoProgress);
		updateTimer.Stop();
		if (!IsDisposed)
		{
			Close();
		}
	}

	public void ShowIndeterminateProgress(string message)
	{
		if (isClosing || IsDisposed)
		{
			return;
		}
		SetProgressBarStyle(ProgressBarStyle.Marquee);
		progressLabel.Hide();
		messageLabel.Text = message;
		taskbarProgress?.SetProgressState(TaskbarProgressBarStatus.Indeterminate);
	}

	public void UpdateProgress(string message, int current, int total, string progressText)
	{
		if (isClosing || IsDisposed)
		{
			return;
		}
		SetProgressBarStyle(ProgressBarStyle.Blocks);
		progressLabel.Show();
		progressBar.Maximum = total;
		progressBar.Value = current;
		progressLabel.Text = progressText;
		messageLabel.Text = message;
		taskbarProgress?.SetProgressState(TaskbarProgressBarStatus.Normal);
		taskbarProgress?.SetProgressValue(current, total);
	}

	public void UpdateCountProgress(string message, int current, int total)
	{
		UpdateProgress(message, current, total, current + "/" + total);
	}

	public void UpdateListRangeProgress(string message, int current, int total, int listStart, int listEnd, int listTotal)
	{
		UpdateProgress(message, current, total, string.Format(Resources.Msg_OK_Fail_Count, listStart, listEnd, listTotal));
	}

	public void UpdateStatisticsProgress(string message, int current, int total, int processed, int matched, int updated, int all)
	{
		UpdateProgress(message, current, total, string.Format(Resources.Msg_OK_Fail_Skip_Count, processed, matched, updated, all));
	}

	protected override void OnShown(EventArgs e)
	{
		base.OnShown(e);
		taskbarProgress?.SetProgressState(TaskbarProgressBarStatus.Normal);
		updateTimer.Start();
		PostProgressUpdate();
	}

	protected override void OnClosed(EventArgs e)
	{
		updateTimer.Stop();
		base.OnClosed(e);
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			components?.Dispose();
			updateTimer?.Dispose();
		}
		base.Dispose(disposing);
	}

	private void SetProgressBarStyle(ProgressBarStyle style)
	{
		if (progressBar.Style != style)
		{
			progressBar.Style = style;
		}
	}

	private void CancelButton_Click(object sender, EventArgs e)
	{
		CancelRequested?.Invoke();
		cancelButton.Enabled = false;
		updateTimer.Stop();
	}

	private void UpdateTimer_Elapsed(object sender, ElapsedEventArgs e)
	{
		PostProgressUpdate();
	}

	private void PostProgressUpdate()
	{
		if (isClosing || IsDisposed || !IsHandleCreated)
		{
			return;
		}
		try
		{
			BeginInvoke(new Action(RaiseProgressUpdate));
		}
		catch (InvalidOperationException)
		{
		}
	}

	private void RaiseProgressUpdate()
	{
		if (!isClosing && !IsDisposed)
		{
			ProgressUpdate?.Invoke();
		}
	}

	private void InitializeComponent()
	{
		messageLabel = new Label();
		progressLabel = new Label();
		progressBar = new ProgressBar();
		cancelButton = new Button();
		SuspendLayout();
		messageLabel.AutoSize = true;
		messageLabel.Location = new Point(20, 20);
		messageLabel.Margin = new Padding(0);
		messageLabel.Name = "lblFileName";
		messageLabel.Size = new Size(47, 14);
		messageLabel.TabIndex = 0;
		messageLabel.Text = "          ";
		progressLabel.AutoSize = true;
		progressLabel.Location = new Point(20, 89);
		progressLabel.Margin = new Padding(0);
		progressLabel.Name = "lblProgress";
		progressLabel.Size = new Size(59, 14);
		progressLabel.TabIndex = 1;
		progressLabel.Text = "             ";
		progressBar.Location = new Point(20, 50);
		progressBar.MarqueeAnimationSpeed = 30;
		progressBar.Name = "progressBar";
		progressBar.Size = new Size(510, 23);
		progressBar.TabIndex = 2;
		cancelButton.Location = new Point(455, 89);
		cancelButton.Margin = new Padding(0);
		cancelButton.Name = "btnCancel";
		cancelButton.Size = new Size(75, 23);
		cancelButton.TabIndex = 3;
		cancelButton.Text = "Cancel";
		cancelButton.UseVisualStyleBackColor = true;
		cancelButton.Click += CancelButton_Click;
		CancelButton = cancelButton;
		AutoScaleDimensions = new SizeF(96f, 96f);
		AutoScaleMode = AutoScaleMode.Dpi;
		ClientSize = new Size(550, 130);
		Controls.Add(cancelButton);
		Controls.Add(progressBar);
		Controls.Add(progressLabel);
		Controls.Add(messageLabel);
		Font = new Font("Tahoma", 9f, FontStyle.Regular, GraphicsUnit.Point, 0);
		FormBorderStyle = FormBorderStyle.None;
		Name = "FormProgress";
		ShowIcon = false;
		ShowInTaskbar = false;
		StartPosition = FormStartPosition.CenterParent;
		Text = "FormProgress";
		ResumeLayout(performLayout: false);
		PerformLayout();
	}
}
