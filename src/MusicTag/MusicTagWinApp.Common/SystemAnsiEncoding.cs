using System;
using System.Text;
using MusicTagWinApp.Containers;

namespace MusicTagWinApp.Common;

// net8 迁移:.NET Core 起 Encoding.Default 恒为 UTF-8,不再是 netfx 的"系统 ANSI 代码页"
// (中文系统 = 936/gb2312)。乱码修复回退(Tokenizer.DetectFileEncoding)与标签编码
// default 分支(TagTextEncoding.NormalizeEncodingName)的语义依赖 ANSI 代码页,此处经
// GetACP 还原 netfx Encoding.Default 语义。代码页编码由 CodePagesEncodingProvider 提供
// (主程序与测试宿主的 Main 首行注册);任何失败回退 Encoding.Default,等同 core 默认行为。
internal static class SystemAnsiEncoding
{
	public static readonly Encoding Instance = CreateInstance();

	private static Encoding CreateInstance()
	{
		try
		{
			return Encoding.GetEncoding(NativeMethods.GetSystemAnsiCodePage());
		}
		catch (Exception)
		{
			return Encoding.Default;
		}
	}
}
