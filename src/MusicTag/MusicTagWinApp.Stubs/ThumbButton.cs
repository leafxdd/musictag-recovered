using System;
using System.Runtime.InteropServices;
using MusicTagWinApp.Win32.Taskbar;

namespace MusicTagWinApp.Stubs;

internal struct ThumbButton
{
	[MarshalAs(UnmanagedType.U4)]
	internal ThumbButtonMask dwMask;

	internal uint iId;

	internal uint iBitmap;

	internal IntPtr hIcon;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
	internal string szTip;

	[MarshalAs(UnmanagedType.U4)]
	internal ThumbButtonOptions dwFlags;
}
