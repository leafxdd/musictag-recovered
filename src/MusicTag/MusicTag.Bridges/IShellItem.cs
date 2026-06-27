using System;
using System.Runtime.InteropServices;
using MusicTagWinApp.Win32.FileDialog;

namespace MusicTag.Bridges;

[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
internal interface IShellItem
{
	[PreserveSig]
	int BindToHandler(IntPtr bindContext, ref Guid handlerId, ref Guid interfaceId, out IntPtr result);

	[PreserveSig]
	int GetParent([MarshalAs(UnmanagedType.Interface)] out IShellItem parent);

	[PreserveSig]
	int GetDisplayName(SIGDN displayName, out IntPtr name);

	[PreserveSig]
	int GetAttributes(uint attributeMask, out uint attributes);

	[PreserveSig]
	int Compare([MarshalAs(UnmanagedType.Interface)] IShellItem item, uint hint, out int order);
}
