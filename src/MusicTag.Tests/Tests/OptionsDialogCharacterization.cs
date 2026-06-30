using System;
using System.Collections.Generic;
using MusicTag.Importers;

namespace MusicTag.Tests;

// OptionsDialog(MusicTag.Importers,code-health biomarker 最重的 god class)纯逻辑 characterization。
// 两个无状态 static helper 由 private 提升为 internal(仅可见性,逻辑零改动),锁定 golden master
// 为后续渐进拆分铺路。两者无 I/O / UI / 实例状态依赖,static 直调、无需实例化 Form。
internal static class OptionsDialogCharacterization
{
	private static void CheckArray(string expectedJoined, string[] actual, string label)
	{
		Check.Equal(expectedJoined, string.Join(",", actual), label);
	}

	public static IEnumerable<(string, Action)> All()
	{
		// ===== GetResourceText:IsNullOrEmpty(resourceText) ? fallback : resourceText =====
		// 注意是 IsNullOrEmpty(非 IsNullOrWhiteSpace):纯空白【不】算空,原样返回。

		yield return ("GetResourceText: empty resource -> fallback", delegate
		{
			Check.Equal("fb", OptionsDialog.GetResourceText("", "fb"), "empty");
		});

		yield return ("GetResourceText: null resource -> fallback", delegate
		{
			Check.Equal("fb", OptionsDialog.GetResourceText(null, "fb"), "null");
		});

		// 纯空白 " " 非空(IsNullOrEmpty(" ")=false)-> 原样返回 " ",不取 fallback
		yield return ("GetResourceText: whitespace resource -> kept (not blank-stripped)", delegate
		{
			Check.Equal(" ", OptionsDialog.GetResourceText(" ", "fb"), "whitespace");
		});

		yield return ("GetResourceText: non-empty resource -> resource", delegate
		{
			Check.Equal("val", OptionsDialog.GetResourceText("val", "fb"), "value");
		});

		yield return ("GetResourceText: non-empty resource + null fallback -> resource", delegate
		{
			Check.Equal("val", OptionsDialog.GetResourceText("val", null), "value over null fallback");
		});

		// ===== NormalizeRestrictedExtensions:Split(';',RemoveEmpty) -> Trim().ToLowerInvariant() -> 过滤空 -> Distinct(OrdinalIgnoreCase) =====

		yield return ("NormalizeRestrictedExtensions: typical \"mp3;flac\" -> [mp3,flac]", delegate
		{
			CheckArray("mp3,flac", OptionsDialog.NormalizeRestrictedExtensions("mp3;flac"), "typical");
		});

		// 大写 -> ToLowerInvariant
		yield return ("NormalizeRestrictedExtensions: uppercase \"MP3;FLAC\" -> [mp3,flac]", delegate
		{
			CheckArray("mp3,flac", OptionsDialog.NormalizeRestrictedExtensions("MP3;FLAC"), "uppercase");
		});

		// 各段 Trim
		yield return ("NormalizeRestrictedExtensions: whitespace \" mp3 ; flac \" -> [mp3,flac]", delegate
		{
			CheckArray("mp3,flac", OptionsDialog.NormalizeRestrictedExtensions(" mp3 ; flac "), "trim");
		});

		// 完全重复 -> Distinct 去重
		yield return ("NormalizeRestrictedExtensions: duplicate \"mp3;mp3\" -> [mp3]", delegate
		{
			CheckArray("mp3", OptionsDialog.NormalizeRestrictedExtensions("mp3;mp3"), "duplicate");
		});

		// 大小写重复(Select 先小写,二者皆变 mp3)-> 单个 mp3
		yield return ("NormalizeRestrictedExtensions: case-dup \"mp3;MP3\" -> [mp3]", delegate
		{
			CheckArray("mp3", OptionsDialog.NormalizeRestrictedExtensions("mp3;MP3"), "case-dup");
		});

		// 空段(连续分号)被 RemoveEmptyEntries 去除
		yield return ("NormalizeRestrictedExtensions: empty segment \"mp3;;flac\" -> [mp3,flac]", delegate
		{
			CheckArray("mp3,flac", OptionsDialog.NormalizeRestrictedExtensions("mp3;;flac"), "empty segment");
		});

		// 纯空白段(" ".Trim()=="" 被 Where 过滤)
		yield return ("NormalizeRestrictedExtensions: blank segment \"mp3; ;flac\" -> [mp3,flac]", delegate
		{
			CheckArray("mp3,flac", OptionsDialog.NormalizeRestrictedExtensions("mp3; ;flac"), "blank segment");
		});

		// 空串 -> 空数组
		yield return ("NormalizeRestrictedExtensions: empty string -> []", delegate
		{
			CheckArray("", OptionsDialog.NormalizeRestrictedExtensions(""), "empty");
		});

		// 纯分号 -> 空数组(全空段移除)
		yield return ("NormalizeRestrictedExtensions: only semicolons \";;\" -> []", delegate
		{
			CheckArray("", OptionsDialog.NormalizeRestrictedExtensions(";;"), "only semicolons");
		});

		// 纯空白(无分号 -> 单非空段 "  " -> Trim "" -> 过滤)-> 空数组
		yield return ("NormalizeRestrictedExtensions: whitespace-only \"  \" -> []", delegate
		{
			CheckArray("", OptionsDialog.NormalizeRestrictedExtensions("  "), "whitespace-only");
		});
	}
}
