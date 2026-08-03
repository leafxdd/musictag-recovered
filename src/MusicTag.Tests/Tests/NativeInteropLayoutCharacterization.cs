using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using MusicTagWinApp.Containers;
using MusicTagWinApp.Instances;

namespace MusicTag.Tests;

internal static class NativeInteropLayoutCharacterization
{
	public static IEnumerable<(string, Action)> All()
	{
		yield return ("NMHDR layout follows pointer width", delegate
		{
			Check.Equal(IntPtr.Size == 8 ? 24 : 12, Marshal.SizeOf<NativeMethods.NativeNotificationHeader>(), "NMHDR size");
			Check.Equal(IntPtr.Size, OffsetOf<NativeMethods.NativeNotificationHeader>(nameof(NativeMethods.NativeNotificationHeader.ControlId)), "NMHDR idFrom offset");
			Check.Equal(IntPtr.Size == 8 ? 16 : 8, OffsetOf<NativeMethods.NativeNotificationHeader>(nameof(NativeMethods.NativeNotificationHeader.NotificationCode)), "NMHDR code offset");
		});

		yield return ("SHFILEINFOW layout follows pointer width", delegate
		{
			Check.Equal(IntPtr.Size == 8 ? 696 : 692, Marshal.SizeOf<NativeMethods.ShellFileInfo>(), "SHFILEINFOW size");
			Check.Equal(IntPtr.Size, OffsetOf<NativeMethods.ShellFileInfo>(nameof(NativeMethods.ShellFileInfo.IconIndex)), "SHFILEINFOW iIcon offset");
			Check.Equal(IntPtr.Size == 8 ? 12 : 8, OffsetOf<NativeMethods.ShellFileInfo>(nameof(NativeMethods.ShellFileInfo.Attributes)), "SHFILEINFOW dwAttributes offset");
			Check.Equal(IntPtr.Size == 8 ? 16 : 12, OffsetOf<NativeMethods.ShellFileInfo>(nameof(NativeMethods.ShellFileInfo.DisplayName)), "SHFILEINFOW szDisplayName offset");
		});

		yield return ("COPYDATASTRUCT layout follows pointer width", delegate
		{
			Check.Equal(IntPtr.Size == 8 ? 24 : 12, Marshal.SizeOf<NativeMethods.CopyDataStruct>(), "COPYDATASTRUCT size");
			Check.Equal(IntPtr.Size == 8 ? 8 : 4, OffsetOf<NativeMethods.CopyDataStruct>(nameof(NativeMethods.CopyDataStruct.DataLength)), "COPYDATASTRUCT cbData offset");
			Check.Equal(IntPtr.Size == 8 ? 16 : 8, OffsetOf<NativeMethods.CopyDataStruct>(nameof(NativeMethods.CopyDataStruct.Data)), "COPYDATASTRUCT lpData offset");
		});

		yield return ("HDHITTESTINFO layout is architecture independent", delegate
		{
			Check.Equal(16, Marshal.SizeOf<NativeMethods.HeaderHitTestInfo>(), "HDHITTESTINFO size");
			Check.Equal(12, OffsetOf<NativeMethods.HeaderHitTestInfo>(nameof(NativeMethods.HeaderHitTestInfo.ItemIndex)), "HDHITTESTINFO iItem offset");
		});

		yield return ("SHGetFileInfo import and callback use x64-safe signatures", delegate
		{
			MethodInfo getShellFileInfo = typeof(NativeMethods).GetMethod(nameof(NativeMethods.GetShellFileInfo));
			DllImportAttribute import = getShellFileInfo?.GetCustomAttribute<DllImportAttribute>();
			Check.NotNull(import, "SHGetFileInfo DllImport");
			Check.Equal(CharSet.Unicode, import.CharSet, "SHGetFileInfo CharSet");
			Check.Equal("SHGetFileInfoW", import.EntryPoint, "SHGetFileInfo entry point");

			ParameterInfo[] callbackParameters = typeof(NativeMethods.EnumThreadWindowsCallback).GetMethod("Invoke").GetParameters();
			Check.Equal(typeof(IntPtr), callbackParameters[1].ParameterType, "EnumThreadWindows lParam type");
		});

		yield return ("Shell file icon remains available", delegate
		{
			using System.Drawing.Icon icon = ImageUtilities.GetSmallFileIcon(Environment.ProcessPath);
			Check.NotNull(icon, "shell file icon");
			Check.True(icon.Width > 0 && icon.Height > 0, "shell file icon dimensions");
		});
	}

	private static int OffsetOf<T>(string fieldName)
	{
		return Marshal.OffsetOf<T>(fieldName).ToInt32();
	}
}
