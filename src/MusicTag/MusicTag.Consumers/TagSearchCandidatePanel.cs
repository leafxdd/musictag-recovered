using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using MusicTagWinApp.Instances;

namespace MusicTag.Consumers;

internal class TagSearchCandidatePanel : UserControl
{
	private bool visibilityInitialized;

	public Action SelectionRequested { get; set; }

	public Action ContextMenuRequested { get; set; }

	private IContainer components;

	private FlowLayoutPanel contentPanel;

	private Label sourceLabel;

	private Label pictureSizeLabel;

	private Label yearLabel;

	private Label trackLabel;

	private Label genreLabel;

	private PictureBox lyricPictureBox;

	public string Source
	{
		get => sourceLabel.Text;
		set => SetLabelValue(sourceLabel, value);
	}

	public string PictureSize
	{
		get => pictureSizeLabel.Text;
		set => SetLabelValue(pictureSizeLabel, value);
	}

	public string Year
	{
		get => yearLabel.Text;
		set => SetLabelValue(yearLabel, value);
	}

	public string Track
	{
		get => trackLabel.Text;
		set => SetLabelValue(trackLabel, value);
	}

	public string Genre
	{
		get => genreLabel.Text;
		set => SetLabelValue(genreLabel, value);
	}

	public string Lyric
	{
		get => lyricPictureBox.Tag as string;
		set
		{
			lyricPictureBox.Tag = value;
			UpdateVisibility(lyricPictureBox);
		}
	}

	public TagSearchCandidatePanel()
	{
		DoubleBuffered = true;
		InitializeComponent();
		ScaleChildHeights();
	}

	private void ScaleChildHeights()
	{
		sourceLabel.Height = ImageUtilities.ScaleByDpi(sourceLabel.Height);
		pictureSizeLabel.Height = ImageUtilities.ScaleByDpi(pictureSizeLabel.Height);
		yearLabel.Height = ImageUtilities.ScaleByDpi(yearLabel.Height);
		trackLabel.Height = ImageUtilities.ScaleByDpi(trackLabel.Height);
		genreLabel.Height = ImageUtilities.ScaleByDpi(genreLabel.Height);
		lyricPictureBox.Height = ImageUtilities.ScaleByDpi(lyricPictureBox.Height);
		lyricPictureBox.Margin = new Padding(0, ImageUtilities.ScaleByDpi(lyricPictureBox.Margin.Top), 0, 0);
	}

	private void SetLabelValue(Label label, string value)
	{
		label.Text = value;
		label.Tag = value;
		UpdateVisibility(label);
	}

	private void Panel_Load(object sender, EventArgs e)
	{
		visibilityInitialized = true;
		sourceLabel.Visible = HasText(sourceLabel.Text);
		pictureSizeLabel.Visible = HasText(pictureSizeLabel.Text);
		yearLabel.Visible = HasText(yearLabel.Text);
		trackLabel.Visible = HasText(trackLabel.Text);
		genreLabel.Visible = HasText(genreLabel.Text);
		lyricPictureBox.Visible = HasText(lyricPictureBox.Tag as string);
		lyricPictureBox.Image = ImageUtilities.LoadResourceBitmap("ly");
		UpdateContentHeight();
	}

	private void Panel_SizeChanged(object sender, EventArgs e)
	{
		contentPanel.Width = Width;
		CenterContentPanel();
	}

	private void ContentPanel_SizeChanged(object sender, EventArgs e)
	{
		sourceLabel.Width = contentPanel.Width;
		pictureSizeLabel.Width = contentPanel.Width;
		yearLabel.Width = contentPanel.Width;
		trackLabel.Width = contentPanel.Width;
		genreLabel.Width = contentPanel.Width;
		lyricPictureBox.Width = contentPanel.Width;
	}

	private void ChildVisibleChanged(object sender, EventArgs e)
	{
		UpdateContentHeight();
	}

	private void UpdateVisibility(Control control)
	{
		if (visibilityInitialized)
		{
			control.Visible = HasText(control.Tag as string);
		}
	}

	private void UpdateContentHeight()
	{
		int visibleHeight = 0;
		visibleHeight += sourceLabel.Visible ? sourceLabel.Height : 0;
		visibleHeight += pictureSizeLabel.Visible ? pictureSizeLabel.Height : 0;
		visibleHeight += yearLabel.Visible ? yearLabel.Height : 0;
		visibleHeight += trackLabel.Visible ? trackLabel.Height : 0;
		visibleHeight += genreLabel.Visible ? genreLabel.Height : 0;
		visibleHeight += lyricPictureBox.Visible ? lyricPictureBox.Height + lyricPictureBox.Margin.Top : 0;
		contentPanel.Height = visibleHeight;
		CenterContentPanel();
	}

	private void CenterContentPanel()
	{
		contentPanel.Location = new Point(0, (Height - contentPanel.Height) / 2);
	}

	private static bool HasText(string value)
	{
		return !string.IsNullOrWhiteSpace(value);
	}

	private void CandidatePanel_MouseDown(object sender, MouseEventArgs e)
	{
		SelectionRequested?.Invoke();
	}

	private void CandidatePanel_MouseUp(object sender, MouseEventArgs e)
	{
		if (e.Button == MouseButtons.Right)
		{
			ContextMenuRequested?.Invoke();
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
		contentPanel = new FlowLayoutPanel();
		sourceLabel = new Label();
		pictureSizeLabel = new Label();
		yearLabel = new Label();
		trackLabel = new Label();
		genreLabel = new Label();
		lyricPictureBox = new PictureBox();

		contentPanel.SuspendLayout();
		((ISupportInitialize)lyricPictureBox).BeginInit();
		SuspendLayout();

		contentPanel.Controls.Add(sourceLabel);
		contentPanel.Controls.Add(pictureSizeLabel);
		contentPanel.Controls.Add(yearLabel);
		contentPanel.Controls.Add(trackLabel);
		contentPanel.Controls.Add(genreLabel);
		contentPanel.Controls.Add(lyricPictureBox);
		contentPanel.FlowDirection = FlowDirection.TopDown;
		contentPanel.Font = new Font("Tahoma", 9f, FontStyle.Regular, GraphicsUnit.Point, 0);
		contentPanel.Location = new Point(0, 0);
		contentPanel.Margin = new Padding(0);
		contentPanel.Name = "flowLayoutPanel1";
		contentPanel.Size = new Size(80, 120);
		contentPanel.TabIndex = 0;
		contentPanel.WrapContents = false;
		contentPanel.SizeChanged += ContentPanel_SizeChanged;
		contentPanel.MouseDown += CandidatePanel_MouseDown;

		ConfigureLabel(sourceLabel, "lblSource", 0, 0, bold: true);
		ConfigureLabel(pictureSizeLabel, "lblPicSize", 20, 1, bold: false);
		ConfigureLabel(yearLabel, "lblYear", 40, 2, bold: false);
		ConfigureLabel(trackLabel, "lblTrack", 60, 4, bold: false);
		ConfigureLabel(genreLabel, "lblGenre", 80, 5, bold: false);

		lyricPictureBox.Location = new Point(0, 102);
		lyricPictureBox.Margin = new Padding(0, 2, 0, 0);
		lyricPictureBox.Name = "pbLyric";
		lyricPictureBox.Size = new Size(80, 16);
		lyricPictureBox.SizeMode = PictureBoxSizeMode.Zoom;
		lyricPictureBox.TabIndex = 6;
		lyricPictureBox.TabStop = false;
		lyricPictureBox.Visible = false;
		lyricPictureBox.VisibleChanged += ChildVisibleChanged;
		lyricPictureBox.MouseDown += CandidatePanel_MouseDown;
		lyricPictureBox.MouseUp += CandidatePanel_MouseUp;

		AutoScaleDimensions = new SizeF(7f, 14f);
		AutoScaleMode = AutoScaleMode.Font;
		Controls.Add(contentPanel);
		Font = new Font("Tahoma", 9f, FontStyle.Regular, GraphicsUnit.Point, 0);
		Margin = new Padding(0);
		Name = "ControlCombTagsSourcePanel";
		Size = new Size(80, 128);
		Load += Panel_Load;
		SizeChanged += Panel_SizeChanged;
		MouseDown += CandidatePanel_MouseDown;
		MouseUp += CandidatePanel_MouseUp;

		contentPanel.ResumeLayout(performLayout: false);
		((ISupportInitialize)lyricPictureBox).EndInit();
		ResumeLayout(performLayout: false);
	}

	private void ConfigureLabel(Label label, string name, int top, int tabIndex, bool bold)
	{
		if (bold)
		{
			label.Font = new Font("Tahoma", 9f, FontStyle.Bold, GraphicsUnit.Point, 0);
		}

		label.Location = new Point(0, top);
		label.Margin = new Padding(0);
		label.Name = name;
		label.Size = new Size(80, 20);
		label.TabIndex = tabIndex;
		label.TextAlign = ContentAlignment.MiddleCenter;
		label.Visible = false;
		label.VisibleChanged += ChildVisibleChanged;
		label.MouseDown += CandidatePanel_MouseDown;
		label.MouseUp += CandidatePanel_MouseUp;
	}
}
