using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using MusicTagWinApp.Containers;

namespace MusicTagWinApp.Exporters;

internal class HeaderAwareListView : ListView
{
	private const int WindowsMessageNotify = 0x004E;

	private const int NotificationRightClick = -5;

	private const int ListViewGetHeaderMessage = 0x101F;

	private const int HeaderHitTestMessage = 0x1206;

	[Category("Action")]
	public event ColumnClickEventHandler HeaderRightClick;

	public HeaderAwareListView()
	{
		DoubleBuffered = true;
	}

	public void SetDoubleBuffered(bool enabled)
	{
		DoubleBuffered = enabled;
	}

	protected virtual void OnHeaderRightClick(ColumnClickEventArgs e)
	{
		HeaderRightClick?.Invoke(this, e);
	}

	protected override void WndProc(ref Message message)
	{
		if (message.Msg == WindowsMessageNotify)
		{
			NativeMethods.NativeNotificationHeader notificationHeader = (NativeMethods.NativeNotificationHeader)message.GetLParam(typeof(NativeMethods.NativeNotificationHeader));
			if (notificationHeader.NotificationCode == NotificationRightClick)
			{
				IntPtr headerHandle = NativeMethods.SendMessage(Handle, ListViewGetHeaderMessage, IntPtr.Zero, IntPtr.Zero);
				uint messagePosition = NativeMethods.GetMessagePos();
				Point headerPoint = PointToClient(new Point((short)messagePosition, (int)messagePosition >> 16));
				NativeMethods.HeaderHitTestInfo hitTestInfo = new NativeMethods.HeaderHitTestInfo
				{
					Point = headerPoint
				};
				NativeMethods.SendHeaderHitTestMessage(headerHandle, HeaderHitTestMessage, IntPtr.Zero, ref hitTestInfo);
				OnHeaderRightClick(new ColumnClickEventArgs(hitTestInfo.ItemIndex));
			}
		}
		base.WndProc(ref message);
	}
}
