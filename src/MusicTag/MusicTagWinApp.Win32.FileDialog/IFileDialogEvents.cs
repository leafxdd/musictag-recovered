using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MusicTag.Bridges;

namespace MusicTagWinApp.Win32.FileDialog;

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("973510DB-7D7F-452B-8975-74A85828D354")]
internal interface IFileDialogEvents
{
	[MethodImpl(MethodImplOptions.PreserveSig | MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	uint OnFileOk([In][MarshalAs(UnmanagedType.Interface)] IFileDialog pfd);

	[MethodImpl(MethodImplOptions.PreserveSig | MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	uint OnFolderChanging([In][MarshalAs(UnmanagedType.Interface)] IFileDialog pfd, [In][MarshalAs(UnmanagedType.Interface)] IShellItem psiFolder);

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	void OnFolderChange([In][MarshalAs(UnmanagedType.Interface)] IFileDialog pfd);

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	void OnSelectionChange([In][MarshalAs(UnmanagedType.Interface)] IFileDialog pfd);

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	void OnShareViolation([In][MarshalAs(UnmanagedType.Interface)] IFileDialog pfd, [In][MarshalAs(UnmanagedType.Interface)] IShellItem psi, out FileDialogEventShareViolationResponse pResponse);

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	void OnTypeChange([In][MarshalAs(UnmanagedType.Interface)] IFileDialog pfd);

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	void OnOverwrite([In][MarshalAs(UnmanagedType.Interface)] IFileDialog pfd, [In][MarshalAs(UnmanagedType.Interface)] IShellItem psi, out FileDialogEventOverwriteResponse pResponse);
}
