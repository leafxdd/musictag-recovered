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

	private const int ListViewSetExtendedStyleMessage = 0x1036;

	private const int ListViewExtendedStyleDoubleBuffer = 0x00010000;

	private bool useDoubleBuffer = true;

	[Category("Action")]
	public event ColumnClickEventHandler HeaderRightClick;

	// 用 comctl32 原生双缓冲(LVS_EX_DOUBLEBUFFER),而不是托管 DoubleBuffered。
	// 托管 OptimizedDoubleBuffer 不参与原生 ListView 的内容绘制,却会把横向滚动从
	// 增量重绘退化为整屏重绘,使 Details 视图左右滚动严重卡顿;原生双缓冲则横纵向
	// 滚动都流畅无闪烁。
	public void SetDoubleBuffered(bool enabled)
	{
		useDoubleBuffer = enabled;
		ApplyDoubleBuffer();
	}

	protected override void OnHandleCreated(EventArgs e)
	{
		base.OnHandleCreated(e);
		// ListView 改属性时会重建句柄并丢失扩展样式,因此每次句柄创建后都要重新应用。
		ApplyDoubleBuffer();
	}

	private void ApplyDoubleBuffer()
	{
		if (!IsHandleCreated)
		{
			return;
		}
		IntPtr styleValue = (IntPtr)(useDoubleBuffer ? ListViewExtendedStyleDoubleBuffer : 0);
		NativeMethods.SendMessage(Handle, ListViewSetExtendedStyleMessage, (IntPtr)ListViewExtendedStyleDoubleBuffer, styleValue);
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
