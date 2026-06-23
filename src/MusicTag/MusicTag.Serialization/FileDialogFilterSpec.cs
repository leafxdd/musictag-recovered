using System.Runtime.InteropServices;

namespace MusicTag.Serialization;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
internal struct FileDialogFilterSpec
{
	[MarshalAs(UnmanagedType.LPWStr)]
	public string DisplayName;

	[MarshalAs(UnmanagedType.LPWStr)]
	public string Pattern;
}
