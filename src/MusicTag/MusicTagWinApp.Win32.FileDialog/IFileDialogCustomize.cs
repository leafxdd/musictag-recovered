using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MusicTagWinApp.Win32.FileDialog;

[ComImport]
[Guid("8016b7b3-3d49-4504-a0aa-2a37494e606f")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IFileDialogCustomize : IFileDialog
{
	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	uint EnableOpenDropDown([In] int dwIDCtl);

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	uint AddMenu([In] int dwIDCtl, [In][MarshalAs(UnmanagedType.LPWStr)] string pszLabel);

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	uint AddPushButton([In] int dwIDCtl, [In][MarshalAs(UnmanagedType.LPWStr)] string pszLabel);

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	uint AddComboBox([In] int dwIDCtl);

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	uint AddRadioButtonList([In] int dwIDCtl);

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	uint AddCheckButton([In] int dwIDCtl, [In][MarshalAs(UnmanagedType.LPWStr)] string pszLabel, [In] bool bChecked);

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	uint AddEditBox([In] int dwIDCtl, [In][MarshalAs(UnmanagedType.LPWStr)] string pszText);

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	uint AddSeparator([In] int dwIDCtl);

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	uint AddText([In] int dwIDCtl, [In][MarshalAs(UnmanagedType.LPWStr)] string pszText);

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	uint SetControlLabel([In] int dwIDCtl, [In][MarshalAs(UnmanagedType.LPWStr)] string pszLabel);

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	uint GetControlState([In] int dwIDCtl, out CDCONTROLSTATE pdwState);

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	uint SetControlState([In] int dwIDCtl, [In] CDCONTROLSTATE dwState);

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	uint GetEditBoxText([In] int dwIDCtl, [Out] IntPtr ppszText);

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	uint SetEditBoxText([In] int dwIDCtl, [In][MarshalAs(UnmanagedType.LPWStr)] string pszText);

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	uint GetCheckButtonState([In] int dwIDCtl, out bool pbChecked);

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	uint SetCheckButtonState([In] int dwIDCtl, [In] bool bChecked);

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	uint AddControlItem([In] int dwIDCtl, [In] int dwIDItem, [In][MarshalAs(UnmanagedType.LPWStr)] string pszLabel);

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	uint RemoveControlItem([In] int dwIDCtl, [In] int dwIDItem);

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	uint RemoveAllControlItems([In] int dwIDCtl);

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	uint GetControlItemState([In] int dwIDCtl, [In] int dwIDItem, out CDCONTROLSTATE pdwState);

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	uint SetControlItemState([In] int dwIDCtl, [In] int dwIDItem, [In] CDCONTROLSTATE dwState);

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	uint GetSelectedControlItem([In] int dwIDCtl, out int pdwIDItem);

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	uint SetSelectedControlItem([In] int dwIDCtl, [In] int dwIDItem);

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	uint StartVisualGroup([In] int dwIDCtl, [In][MarshalAs(UnmanagedType.LPWStr)] string pszLabel);

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	uint EndVisualGroup();

	[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
	uint MakeProminent([In] int dwIDCtl);
}
