using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using MusicTag.Services;
using MusicTagWinApp.Adapter;
using MusicTagWinApp.Instances;
using MusicTagWinApp.Properties;

namespace MusicTagWinApp.Roles;

internal class AboutDialog : Form
{
	private IContainer components;

	private FlowLayoutPanel mainPanel;

	private Panel headerPanel;

	private GroupBox donationGroupBox;

	private Button wechatDonateButton;

	private Button alipayDonateButton;

	private Label versionLabel;

	private Label productNameLabel;

	private PictureBox appIconPictureBox;

	private TextBox messageTextBox;

	private Button closeButton;

	public AboutDialog()
	{
		InitializeComponent();
		Text = $"{Resources.about} {Resources.AppName}";
		productNameLabel.Text = Resources.AppName;
		versionLabel.Text = $"{ApplicationInfoService.GetFileVersion()}";
		closeButton.Text = Resources.Close;
		donationGroupBox.Text = Resources.Donate;
		alipayDonateButton.Text = Resources.Alipay;
		wechatDonateButton.Text = Resources.WechatPay;
		LoadApplicationIcon();
		messageTextBox.Text = GetLocalizedAboutText();
		messageTextBox.Height = mainPanel.Height - headerPanel.Height - closeButton.Height - closeButton.Margin.Top - closeButton.Margin.Bottom - messageTextBox.Margin.Top - messageTextBox.Margin.Bottom;
	}

	private void CloseButtonClick(object sender, EventArgs args)
	{
		Close();
	}

	private void AlipayDonateButtonClick(object sender, EventArgs args)
	{
		new DonateDialog(Resources.Alipay).ShowDialog();
	}

	private void WechatDonateButtonClick(object sender, EventArgs args)
	{
		new DonateDialog(Resources.WechatPay).ShowDialog();
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
		components = new System.ComponentModel.Container();
		mainPanel = new FlowLayoutPanel();
		headerPanel = new Panel();
		donationGroupBox = new GroupBox();
		wechatDonateButton = new Button();
		alipayDonateButton = new Button();
		versionLabel = new Label();
		productNameLabel = new Label();
		appIconPictureBox = new PictureBox();
		messageTextBox = new TextBox();
		closeButton = new Button();
		mainPanel.SuspendLayout();
		headerPanel.SuspendLayout();
		donationGroupBox.SuspendLayout();
		((ISupportInitialize)appIconPictureBox).BeginInit();
		SuspendLayout();
		mainPanel.Controls.Add(headerPanel);
		mainPanel.Controls.Add(messageTextBox);
		mainPanel.Controls.Add(closeButton);
		mainPanel.Dock = DockStyle.Fill;
		mainPanel.FlowDirection = FlowDirection.RightToLeft;
		mainPanel.Location = new Point(10, 9);
		mainPanel.Margin = new Padding(0);
		mainPanel.Name = "flowLayoutPanel1";
		mainPanel.Size = new Size(386, 238);
		mainPanel.TabIndex = 7;
		headerPanel.Controls.Add(donationGroupBox);
		headerPanel.Controls.Add(versionLabel);
		headerPanel.Controls.Add(productNameLabel);
		headerPanel.Controls.Add(appIconPictureBox);
		headerPanel.Location = new Point(7, 0);
		headerPanel.Margin = new Padding(0);
		headerPanel.Name = "panel1";
		headerPanel.Size = new Size(379, 65);
		headerPanel.TabIndex = 7;
		donationGroupBox.Controls.Add(wechatDonateButton);
		donationGroupBox.Controls.Add(alipayDonateButton);
		donationGroupBox.Location = new Point(139, 2);
		donationGroupBox.Name = "groupBox1";
		donationGroupBox.Size = new Size(237, 60);
		donationGroupBox.TabIndex = 9;
		donationGroupBox.TabStop = false;
		donationGroupBox.Text = "Donate";
		wechatDonateButton.Location = new Point(131, 24);
		wechatDonateButton.Name = "btnDonateWechat";
		wechatDonateButton.Size = new Size(90, 23);
		wechatDonateButton.TabIndex = 1;
		wechatDonateButton.Text = "WeChat Pay";
		wechatDonateButton.UseVisualStyleBackColor = true;
		wechatDonateButton.Click += WechatDonateButtonClick;
		alipayDonateButton.Location = new Point(19, 24);
		alipayDonateButton.Name = "btnDonateAlipay";
		alipayDonateButton.Size = new Size(90, 23);
		alipayDonateButton.TabIndex = 0;
		alipayDonateButton.Text = "Alipay";
		alipayDonateButton.UseVisualStyleBackColor = true;
		alipayDonateButton.Click += AlipayDonateButtonClick;
		versionLabel.AutoSize = true;
		versionLabel.Location = new Point(63, 40);
		versionLabel.Name = "labelVersion";
		versionLabel.Size = new Size(47, 14);
		versionLabel.TabIndex = 8;
		versionLabel.Text = "1.0.0.0";
		productNameLabel.AutoSize = true;
		productNameLabel.Location = new Point(63, 6);
		productNameLabel.Name = "labelProductName";
		productNameLabel.Size = new Size(61, 14);
		productNameLabel.TabIndex = 7;
		productNameLabel.Text = "Music Tag";
		appIconPictureBox.BackColor = Color.Transparent;
		appIconPictureBox.Location = new Point(0, 6);
		appIconPictureBox.Name = "pictureBox1";
		appIconPictureBox.Size = new Size(48, 48);
		appIconPictureBox.SizeMode = PictureBoxSizeMode.Zoom;
		appIconPictureBox.TabIndex = 6;
		appIconPictureBox.TabStop = false;
		messageTextBox.Location = new Point(4, 68);
		messageTextBox.Multiline = true;
		messageTextBox.Name = "tbMessage";
		messageTextBox.ReadOnly = true;
		messageTextBox.ScrollBars = ScrollBars.Vertical;
		messageTextBox.Size = new Size(379, 139);
		messageTextBox.TabIndex = 8;
		closeButton.Location = new Point(308, 213);
		closeButton.Name = "btnOK";
		closeButton.Size = new Size(75, 23);
		closeButton.TabIndex = 9;
		closeButton.Text = "Close";
		closeButton.UseVisualStyleBackColor = true;
		closeButton.Click += CloseButtonClick;
		base.AutoScaleDimensions = new SizeF(96f, 96f);
		base.AutoScaleMode = AutoScaleMode.Dpi;
		base.ClientSize = new Size(406, 256);
		base.Controls.Add(mainPanel);
		Font = new Font("Tahoma", 9f, FontStyle.Regular, GraphicsUnit.Point, 0);
		base.FormBorderStyle = FormBorderStyle.FixedDialog;
		base.MaximizeBox = false;
		base.MinimizeBox = false;
		base.Name = "FormAbout";
		Padding = new Padding(10, 9, 10, 9);
		base.ShowIcon = false;
		base.ShowInTaskbar = false;
		base.SizeGripStyle = SizeGripStyle.Hide;
		base.StartPosition = FormStartPosition.CenterParent;
		Text = "FormAbout";
		mainPanel.ResumeLayout(performLayout: false);
		mainPanel.PerformLayout();
		headerPanel.ResumeLayout(performLayout: false);
		headerPanel.PerformLayout();
		donationGroupBox.ResumeLayout(performLayout: false);
		((ISupportInitialize)appIconPictureBox).EndInit();
		ResumeLayout(performLayout: false);
	}

	private void LoadApplicationIcon()
	{
		using Icon icon = new Icon(Resources.AppIcon, appIconPictureBox.Size);
		appIconPictureBox.Image = icon.ToBitmap();
	}

	private static string GetLocalizedAboutText()
	{
		string language = StateFieldInstance.CurrentLanguageCode;
		if (language == "zh-CHS")
		{
			return "『音乐标签』是一款可以编辑歌曲的标题，专辑，艺术家等信息的应用程序， 支持FLAC, APE, WAV, AIFF, WV, TTA, MP3, MP4, M4A, OGG, MPC, OPUS, WMA, DSF, DFF等音频格式，绿色无广告，无任何功能限制。\r\n\r\n由于作者平时工作较忙，只能用空闲的业余时间来制作和维护，如果你喜欢此应用，可以随意捐赠，以支持其发展。\r\n\r\n手机版地址：https://www.coolapk.com/apk/com.xjcheng.musictageditor";
		}
		if (language == "zh-CHT")
		{
			return "『音樂標簽』是一款可以編輯歌曲的標題，專輯，藝術家等信息的應用程序， 支持FLAC, APE, WAV, AIFF, WV, TTA, MP3, MP4, M4A, OGG, MPC, OPUS, WMA, DSF, DFF等音頻格式，綠色無廣告，無任何功能限制。\r\n\r\n由於作者平時較忙，只能用空閑的業余時間來制作和維護，如果妳喜歡此應用，可以隨意捐贈，以支持其發展。\r\n\r\n手机版地址：https://www.coolapk.com/apk/com.xjcheng.musictageditor";
		}
		return "\"Music Tag\" is an application that can edit the title, album, artist and other information of songs. It supports FLAC, APE, WAV, AIFF, WV, TTA, MP3, MP4, M4A, OGG, MPC, OPUS, WMA, DSF , DFF and other audio formats, green without ads, without any functional restrictions.\r\n\r\nBecause the author is usually busy with work, he can only use idle spare time to make and maintain. If you like this application, you can donate it freely to support its development.\r\n\r\nMobile version address: https://www.coolapk.com/apk/com.xjcheng.musictageditor";
	}
}
