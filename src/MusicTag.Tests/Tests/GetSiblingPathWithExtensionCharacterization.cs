using System;
using System.Collections.Generic;
using MusicTagWinApp.Instances;

namespace MusicTag.Tests;

// PathFileUtilities.GetSiblingPathWithExtension 的 characterization(零成本:已 public static、纯字符串拼接、
// 无 I/O)。语义:Path.GetDirectoryName(filePath) + "\" + Path.GetFileNameWithoutExtension(filePath) +
// extension(extension 原样拼接,不智能加点)。被 RenameFiles 关联文件路径 + ResolveRelatedFileTarget 复用,
// 此处锁定其拼接现状(防 Path.Combine 式清理重构改变结果字符串)。
internal static class GetSiblingPathWithExtensionCharacterization
{
	public static IEnumerable<(string, Action)> All()
	{
		yield return ("GetSiblingPathWithExtension: replace extension (.mp3 -> .lrc)", delegate
		{
			Check.Equal("C:\\m\\song.lrc", PathFileUtilities.GetSiblingPathWithExtension("C:\\m\\song.mp3", ".lrc"), "sibling .lrc");
		});

		yield return ("GetSiblingPathWithExtension: different source/target ext (.flac -> .jpg)", delegate
		{
			Check.Equal("C:\\m\\song.jpg", PathFileUtilities.GetSiblingPathWithExtension("C:\\m\\song.flac", ".jpg"), "sibling .jpg");
		});

		yield return ("GetSiblingPathWithExtension: no directory -> leading backslash", delegate
		{
			Check.Equal("\\song.lrc", PathFileUtilities.GetSiblingPathWithExtension("song.mp3", ".lrc"), "empty dir + hardcoded separator");
		});

		yield return ("GetSiblingPathWithExtension: multi-dot filename keeps inner dots", delegate
		{
			Check.Equal("C:\\m\\a.b.lrc", PathFileUtilities.GetSiblingPathWithExtension("C:\\m\\a.b.mp3", ".lrc"), "GetFileNameWithoutExtension strips only last ext");
		});

		yield return ("GetSiblingPathWithExtension: empty extension -> no trailing ext", delegate
		{
			Check.Equal("C:\\m\\song", PathFileUtilities.GetSiblingPathWithExtension("C:\\m\\song.mp3", ""), "empty extension appended verbatim");
		});

		yield return ("GetSiblingPathWithExtension: extension without dot appended verbatim (no auto-dot)", delegate
		{
			Check.Equal("C:\\m\\songx", PathFileUtilities.GetSiblingPathWithExtension("C:\\m\\song.mp3", "x"), "no smart dot insertion");
		});
	}
}
