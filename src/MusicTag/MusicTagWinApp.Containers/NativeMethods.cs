using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using MusicTag.Bridges;

namespace MusicTagWinApp.Containers;

internal static class NativeMethods
{
	public delegate bool EnumThreadWindowsCallback(IntPtr windowHandle, int lParam);

	public struct NativeNotificationHeader
	{
		public IntPtr WindowHandle;

		public int ControlId;

		public int NotificationCode;
	}

	public struct HeaderHitTestInfo
	{
		public Point Point;

		public uint Flags;

		public int ItemIndex;
	}

	public struct ShellFileInfo
	{
		public IntPtr IconHandle;

		public IntPtr IconIndex;

		public uint Attributes;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
		public string DisplayName;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
		public string TypeName;
	}

	public struct ListViewColumnInfo
	{
		public int Mask;

		public int Format;

		[MarshalAs(UnmanagedType.LPTStr)]
		public string Text;

		public IntPtr BitmapHandle;

		public int TextMax;

		public int Width;

		public int SubItemIndex;

		public int ImageIndex;

		public int Order;
	}

	public struct CopyDataStruct
	{
		public IntPtr DataIdentifier;

		public int DataLength;

		[MarshalAs(UnmanagedType.LPWStr)]
		public string Data;
	}

	public const int ShowWindowHide = 0;

	public const int ShowWindowNormal = 1;

	public const int ShowWindowMaximize = 3;

	public const int ShowWindowNoActivate = 4;

	public const int ShowWindowShow = 5;

	public const int ShowWindowMinimize = 6;

	public const int ShowWindowRestore = 9;

	public const int ShowWindowDefault = 10;

	// PMv2(实验分支):取某坐标点所在显示器的有效 DPI(MDT_EFFECTIVE_DPI=0;MONITOR_DEFAULTTONEAREST=2)。
	// shcore 仅 Win8.1+,调用方需捕获 DllNotFound/EntryPointNotFound 以在更低系统维持旧行为。
	[DllImport("user32.dll", EntryPoint = "MonitorFromPoint")]
	public static extern IntPtr MonitorFromPoint(Point point, uint flags);

	[DllImport("shcore.dll", EntryPoint = "GetDpiForMonitor")]
	public static extern int GetDpiForMonitor(IntPtr monitorHandle, int dpiType, out uint dpiX, out uint dpiY);

	[DllImport("user32.dll", EntryPoint = "ShowWindowAsync")]
	public static extern bool ShowWindowAsync(IntPtr windowHandle, int command);

	[DllImport("user32.dll", EntryPoint = "SetForegroundWindow")]
	public static extern bool SetForegroundWindow(IntPtr windowHandle);

	[DllImport("user32.dll", EntryPoint = "FindWindow")]
	public static extern IntPtr FindWindow(string className, string windowName);

	[DllImport("user32.dll", EntryPoint = "FindWindowEx")]
	public static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfterHandle, string className, string windowName);

	[DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowText")]
	public static extern int GetWindowText(IntPtr windowHandle, [Out] StringBuilder text, int maxCount);

	[DllImport("user32.dll", EntryPoint = "SendMessage")]
	public static extern IntPtr SendMessage(IntPtr windowHandle, int message, IntPtr wParam, IntPtr lParam);

	[DllImport("user32.dll", EntryPoint = "SendMessage")]
	public static extern IntPtr SendHeaderHitTestMessage(IntPtr windowHandle, int message, IntPtr wParam, ref HeaderHitTestInfo hitTestInfo);

	[DllImport("user32.dll", EntryPoint = "SendMessage")]
	public static extern IntPtr SendListViewColumnMessage(IntPtr windowHandle, int message, IntPtr wParam, ref ListViewColumnInfo columnInfo);

	[DllImport("user32.dll", EntryPoint = "SendMessage")]
	public static extern IntPtr SendCopyDataMessage(IntPtr windowHandle, int message, IntPtr wParam, ref CopyDataStruct copyData);

	[DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessage")]
	public static extern IntPtr SendStringMessage(IntPtr windowHandle, int message, IntPtr wParam, [MarshalAs(UnmanagedType.LPWStr)] string text);

	[DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessage")]
	public static extern IntPtr SendTextBufferMessage(IntPtr windowHandle, int message, int wParam, StringBuilder text);

	[DllImport("user32.dll", EntryPoint = "GetMessagePos")]
	public static extern uint GetMessagePos();

	[DllImport("user32.dll", CharSet = CharSet.Auto, EntryPoint = "SetWindowText", SetLastError = true)]
	public static extern bool SetWindowText(IntPtr windowHandle, string text);

	[DllImport("user32.dll", EntryPoint = "DestroyIcon")]
	public static extern bool DestroyIcon(IntPtr iconHandle);

	[DllImport("shell32.dll", CharSet = CharSet.Auto, EntryPoint = "SHGetFileInfo")]
	public static extern IntPtr GetShellFileInfo(string path, uint fileAttributes, ref ShellFileInfo fileInfo, uint fileInfoSize, uint flags);

	[DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHCreateItemFromParsingName", SetLastError = true)]
	public static extern int CreateShellItemFromPath([MarshalAs(UnmanagedType.LPWStr)] string path, IntPtr bindContext, ref Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out IShellItem shellItem);

	[DllImport("shell32.dll", EntryPoint = "ILFree", ExactSpelling = true)]
	public static extern void FreeItemIdList(IntPtr itemIdList);

	[DllImport("shell32.dll", CharSet = CharSet.Auto, EntryPoint = "ILCreateFromPath", ExactSpelling = true)]
	public static extern IntPtr CreateItemIdListFromPath(string path);

	[DllImport("shell32.dll", EntryPoint = "SHOpenFolderAndSelectItems", ExactSpelling = true)]
	public static extern int OpenFolderAndSelectItems(IntPtr folderItemIdList, uint itemCount, IntPtr itemIdLists, uint flags);

	[DllImport("uxtheme.dll", CharSet = CharSet.Auto, EntryPoint = "SetWindowTheme", ExactSpelling = true)]
	public static extern int SetWindowTheme(IntPtr windowHandle, string subAppName, string subIdList);

	[DllImport("user32.dll", EntryPoint = "EnumThreadWindows")]
	public static extern int EnumThreadWindows(int threadId, EnumThreadWindowsCallback callback, IntPtr lParam);

	[DllImport("user32.dll", CharSet = CharSet.Auto, EntryPoint = "GetClassName")]
	public static extern int GetClassName(IntPtr windowHandle, StringBuilder className, int maxCount);

	[DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")]
	public static extern int GetWindowThreadProcessId(IntPtr windowHandle, out int processId);
}
