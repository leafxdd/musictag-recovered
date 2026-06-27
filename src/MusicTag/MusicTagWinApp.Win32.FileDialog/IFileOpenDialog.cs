using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MusicTag.Bridges;
using MusicTag.Serialization;

namespace MusicTagWinApp.Win32.FileDialog;

[ComImport]
[Guid("D57C7288-D4AD-4768-BE02-9D969532D960")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IFileOpenDialog : IFileDialog
{
	[MethodImpl(MethodImplOptions.PreserveSig | MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	new uint Show([Optional][In] IntPtr hwndOwner);

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	new uint SetFileTypes([In] uint cFileTypes, [In][MarshalAs(UnmanagedType.LPArray)] FileDialogFilterSpec[] rgFilterSpec);

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	new uint SetFileTypeIndex([In] uint iFileType);

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	new uint GetFileTypeIndex(out uint piFileType);

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	new uint Advise([In][MarshalAs(UnmanagedType.Interface)] IntPtr pfde, out uint pdwCookie);

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	new uint Unadvise([In] uint dwCookie);

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	new uint SetOptions([In] FOS fos);

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	new uint GetOptions(out FOS fos);

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	new void SetDefaultFolder([In][MarshalAs(UnmanagedType.Interface)] IShellItem psi);

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	new uint SetFolder([In][MarshalAs(UnmanagedType.Interface)] IShellItem psi);

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	new uint GetFolder([MarshalAs(UnmanagedType.Interface)] out IShellItem ppsi);

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	new uint GetCurrentSelection([MarshalAs(UnmanagedType.Interface)] out IShellItem ppsi);

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	new uint SetFileName([In][MarshalAs(UnmanagedType.LPWStr)] string pszName);

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	new uint GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	new uint SetTitle([In][MarshalAs(UnmanagedType.LPWStr)] string pszTitle);

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	new uint SetOkButtonLabel([In][MarshalAs(UnmanagedType.LPWStr)] string pszText);

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	new uint SetFileNameLabel([In][MarshalAs(UnmanagedType.LPWStr)] string pszLabel);

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	new uint GetResult([MarshalAs(UnmanagedType.Interface)] out IShellItem ppsi);

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	new uint AddPlace([In][MarshalAs(UnmanagedType.Interface)] IShellItem psi, uint fdap);

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	new uint SetDefaultExtension([In][MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	new uint Close([MarshalAs(UnmanagedType.Error)] uint hr);

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	new uint SetClientGuid([In] ref Guid guid);

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	new uint ClearClientData();

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	new uint SetFilter([MarshalAs(UnmanagedType.Interface)] IntPtr pFilter);

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	uint GetResults([MarshalAs(UnmanagedType.Interface)] out IShellItemArray ppenum);

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	uint GetSelectedItems([MarshalAs(UnmanagedType.Interface)] out IShellItemArray ppsai);
}
