using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using MusicTagWinApp.Properties;

namespace MusicTagWinApp.Adapter;

internal class DonateDialog : Form
{
	private IContainer components;

	private PictureBox donationImageBox;

	public DonateDialog(string paymentName)
	{
		InitializeComponent();
		if (paymentName == Resources.WechatPay)
		{
			Text = Resources.WechatPay + Resources.Donate;
			donationImageBox.Image = Resources.qrcode_wechat;
		}
		else
		{
			Text = Resources.Alipay + Resources.Donate;
			donationImageBox.Image = Resources.qrcode_alipay;
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
		donationImageBox = new PictureBox();
		((ISupportInitialize)donationImageBox).BeginInit();
		SuspendLayout();
		donationImageBox.Location = new Point(20, 20);
		donationImageBox.Margin = new Padding(0);
		donationImageBox.Name = "pictureBox1";
		donationImageBox.Size = new Size(600, 600);
		donationImageBox.SizeMode = PictureBoxSizeMode.CenterImage;
		donationImageBox.TabIndex = 0;
		donationImageBox.TabStop = false;
		base.AutoScaleDimensions = new SizeF(7f, 14f);
		base.AutoScaleMode = AutoScaleMode.Font;
		base.ClientSize = new Size(644, 641);
		base.Controls.Add(donationImageBox);
		Font = new Font("Tahoma", 9f, FontStyle.Regular, GraphicsUnit.Point, 0);
		base.FormBorderStyle = FormBorderStyle.FixedSingle;
		base.MaximizeBox = false;
		base.MinimizeBox = false;
		base.Name = "FormDonate";
		base.ShowIcon = false;
		base.StartPosition = FormStartPosition.CenterParent;
		Text = "Donate";
		((ISupportInitialize)donationImageBox).EndInit();
		ResumeLayout(performLayout: false);
	}
}

