using System;
using System.Runtime.InteropServices;
using MusicTagWinApp.Stubs;

namespace MusicTagWinApp.Win32.Taskbar;

[ComImport]
[Guid("c43dc798-95d1-4bea-9030-bb99e2983a1a")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITaskbarList4
{
	[PreserveSig]
	void HrInit();

	[PreserveSig]
	void AddTab(IntPtr hwnd);

	[PreserveSig]
	void DeleteTab(IntPtr hwnd);

	[PreserveSig]
	void ActivateTab(IntPtr hwnd);

	[PreserveSig]
	void SetActiveAlt(IntPtr hwnd);

	[PreserveSig]
	void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fFullscreen);

	[PreserveSig]
	void SetProgressValue(IntPtr hwnd, ulong ullCompleted, ulong ullTotal);

	[PreserveSig]
	void SetProgressState(IntPtr hwnd, TaskbarProgressBarStatus tbpFlags);

	[PreserveSig]
	void RegisterTab(IntPtr hwndTab, IntPtr hwndMDI);

	[PreserveSig]
	void UnregisterTab(IntPtr hwndTab);

	[PreserveSig]
	void SetTabOrder(IntPtr hwndTab, IntPtr hwndInsertBefore);

	[PreserveSig]
	void SetTabActive(IntPtr hwndTab, IntPtr hwndInsertBefore, uint dwReserved);

	[PreserveSig]
	uint ThumbBarAddButtons(IntPtr hwnd, uint cButtons, [MarshalAs(UnmanagedType.LPArray)] ThumbButton[] pButtons);

	[PreserveSig]
	uint ThumbBarUpdateButtons(IntPtr hwnd, uint cButtons, [MarshalAs(UnmanagedType.LPArray)] ThumbButton[] pButtons);

	[PreserveSig]
	void ThumbBarSetImageList(IntPtr hwnd, IntPtr himl);

	[PreserveSig]
	void SetOverlayIcon(IntPtr hwnd, IntPtr hIcon, [MarshalAs(UnmanagedType.LPWStr)] string pszDescription);

	[PreserveSig]
	void SetThumbnailTooltip(IntPtr hwnd, [MarshalAs(UnmanagedType.LPWStr)] string pszTip);

	[PreserveSig]
	void SetThumbnailClip(IntPtr hwnd, IntPtr prcClip);

	void SetTabProperties(IntPtr hwndTab, SetTabPropertiesOption stpFlags);
}
