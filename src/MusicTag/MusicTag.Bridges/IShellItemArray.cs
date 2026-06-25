using System;
using System.Runtime.InteropServices;

namespace MusicTag.Bridges;

[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("B63EA76D-1F85-456F-A19C-48159EFA858B")]
internal interface IShellItemArray
{
	[PreserveSig]
	int BindToHandler(IntPtr bindContext, ref Guid handlerId, ref Guid interfaceId, out IntPtr result);

	[PreserveSig]
	int GetPropertyStore(uint flags, ref Guid interfaceId, out IntPtr propertyStore);

	[PreserveSig]
	int GetPropertyDescriptionList(IntPtr keyType, ref Guid interfaceId, out IntPtr propertyDescriptionList);

	[PreserveSig]
	int GetAttributes(uint attributeFlags, uint attributeMask, out uint attributes);

	[PreserveSig]
	int GetCount(out uint count);

	[PreserveSig]
	int GetItemAt([In] uint index, [MarshalAs(UnmanagedType.Interface)] out IShellItem shellItem);

	[PreserveSig]
	int EnumItems([MarshalAs(UnmanagedType.Interface)] out IntPtr enumShellItems);
}
