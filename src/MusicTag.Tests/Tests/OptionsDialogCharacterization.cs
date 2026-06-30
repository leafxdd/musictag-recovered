using System;
using System.Collections.Generic;
using System.Linq;
using MusicTag.Importers;
using MusicTagWinApp.Web;

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

		// ===== FindSourceItemByName:遍历 List<SourceItem> 找 SearchSource.ToString()==name(大小写敏感) =====

		yield return ("FindSourceItemByName: found by enum name -> that item", delegate
		{
			SourceItem qq = new SourceItem(SearchSource.QQ, 1);
			List<SourceItem> list = new List<SourceItem> { new SourceItem(SearchSource.Music163, 0), qq };
			Check.True(OptionsDialog.FindSourceItemByName(list, "QQ") == qq, "found QQ ref");
		});

		yield return ("FindSourceItemByName: name not in list -> null", delegate
		{
			List<SourceItem> list = new List<SourceItem> { new SourceItem(SearchSource.Music163, 0), new SourceItem(SearchSource.QQ, 1) };
			Check.Null(OptionsDialog.FindSourceItemByName(list, "Kugou"), "Kugou absent");
		});

		yield return ("FindSourceItemByName: empty list -> null", delegate
		{
			Check.Null(OptionsDialog.FindSourceItemByName(new List<SourceItem>(), "QQ"), "empty list");
		});

		// SearchSource.ToString() 区分大小写,小写 name 不匹配
		yield return ("FindSourceItemByName: case-sensitive (\"qq\" != \"QQ\") -> null", delegate
		{
			List<SourceItem> list = new List<SourceItem> { new SourceItem(SearchSource.QQ, 1) };
			Check.Null(OptionsDialog.FindSourceItemByName(list, "qq"), "case-sensitive");
		});

		// ===== ClampToRange:Math.Max(min, Math.Min(max, value)) — 夹到 [min,max](从 ClampSearchResultLimit 分离的纯核) =====

		yield return ("ClampToRange: within range -> value", delegate
		{
			Check.Equal(5, OptionsDialog.ClampToRange(5, 0, 10), "within");
		});

		yield return ("ClampToRange: below min -> min", delegate
		{
			Check.Equal(0, OptionsDialog.ClampToRange(-3, 0, 10), "below");
		});

		yield return ("ClampToRange: above max -> max", delegate
		{
			Check.Equal(10, OptionsDialog.ClampToRange(15, 0, 10), "above");
		});

		yield return ("ClampToRange: at min -> min", delegate
		{
			Check.Equal(0, OptionsDialog.ClampToRange(0, 0, 10), "at min");
		});

		yield return ("ClampToRange: at max -> max", delegate
		{
			Check.Equal(10, OptionsDialog.ClampToRange(10, 0, 10), "at max");
		});

		// min>max 反常:Math.Min(max,value) 先压到 <=max,Math.Max(min,..) 再抬到 min -> min 胜出
		yield return ("ClampToRange: inverted min>max -> min wins", delegate
		{
			Check.Equal(10, OptionsDialog.ClampToRange(5, 10, 0), "inverted");
		});

		yield return ("ClampToRange: min==max -> that value", delegate
		{
			Check.Equal(3, OptionsDialog.ClampToRange(5, 3, 3), "min==max");
		});

		// ===== EnumeratePictureSizeOptions:20 起,<100 段步进 20、>=100 段步进 100,至 10000(从 LoadSavedOptions 内联 for 提取的纯序列 iterator) =====

		yield return ("EnumeratePictureSizeOptions: first=20, last=10000, count=104", delegate
		{
			List<int> options = OptionsDialog.EnumeratePictureSizeOptions().ToList();
			Check.Equal(20, options[0], "first 20");
			Check.Equal(10000, options[options.Count - 1], "last 10000");
			Check.Equal(104, options.Count, "104 entries");
		});

		// 步进切换点:索引4=100(20/40/60/80/100 步进20段末),索引5=200(步进100段始)
		yield return ("EnumeratePictureSizeOptions: step switches 20->100 at value 100", delegate
		{
			List<int> options = OptionsDialog.EnumeratePictureSizeOptions().ToList();
			Check.Equal(100, options[4], "index4=100");
			Check.Equal(200, options[5], "index5=200 (step jumps to 100)");
		});

		// 步进跳过的值不在序列(150 被 100->200 跨过;50 被 40->60 跨过)
		yield return ("EnumeratePictureSizeOptions: skipped values absent", delegate
		{
			List<int> options = OptionsDialog.EnumeratePictureSizeOptions().ToList();
			Check.True(options.Contains(100), "has 100");
			Check.True(!options.Contains(150), "no 150 (step-100 skips)");
			Check.True(!options.Contains(50), "no 50 (step-20 skips)");
		});

		// ===== EnumeratePictureResolutionOptions:0 起,首步 +100 到 100,之后步进 10,至 4000 =====

		yield return ("EnumeratePictureResolutionOptions: first=0, second=100, last=4000, count=392", delegate
		{
			List<int> options = OptionsDialog.EnumeratePictureResolutionOptions().ToList();
			Check.Equal(0, options[0], "first 0");
			Check.Equal(100, options[1], "second 100 (first step +100)");
			Check.Equal(4000, options[options.Count - 1], "last 4000");
			Check.Equal(392, options.Count, "392 entries");
		});

		// 0 之后步进 10:索引2=110;非网格值(>100 的非 10 倍数)不在序列
		yield return ("EnumeratePictureResolutionOptions: step-10 after 100, off-grid absent", delegate
		{
			List<int> options = OptionsDialog.EnumeratePictureResolutionOptions().ToList();
			Check.Equal(110, options[2], "index2=110");
			Check.True(options.Contains(0) && options.Contains(100) && options.Contains(4000), "has 0/100/4000");
			Check.True(!options.Contains(50), "no 50 (0 jumps straight to 100)");
			Check.True(!options.Contains(105), "no 105 (step-10 off-grid)");
		});
	}
}
