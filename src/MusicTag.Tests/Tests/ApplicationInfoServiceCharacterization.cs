using System;
using System.Collections.Generic;
using MusicTag.Services;

namespace MusicTag.Tests;

// ApplicationInfoService(MusicTag.Services,code-health untested hotspot −2.0)的版本比较纯逻辑
// characterization。两个无状态 static helper 由 private 提升为 internal(仅可见性,逻辑零改动),
// 锁定 golden master:更新检查的版本解析/比较是用户可见"发现新版本"提示的判定核心。
// 二者无 I/O / UI / 反射依赖(GetFileVersion 经反射读程序集版本,不在此 characterize)。
//
//   ParseVersionParts(v) = v.Split('.') 必须恰 4 段,各段 int.Parse;非 4 段 -> null;非数字段 -> 抛 FormatException。
//   CompareVersionParts(cur,latest) = 逐段比较,首个不等段定胜负(cur<latest:-1 / cur>latest:1),全等 -> 0(高位优先)。
internal static class ApplicationInfoServiceCharacterization
{
	private static string Join(int[] parts)
	{
		return parts == null ? "null" : string.Join(",", parts);
	}

	public static IEnumerable<(string, Action)> All()
	{
		// ===== ParseVersionParts:恰 4 段 int,否则 null;非数字段抛异常 =====

		yield return ("ParseVersionParts: typical \"1.2.3.4\" -> [1,2,3,4]", delegate
		{
			Check.Equal("1,2,3,4", Join(ApplicationInfoService.ParseVersionParts("1.2.3.4")), "typical");
		});

		yield return ("ParseVersionParts: multi-digit \"10.20.30.40\" -> [10,20,30,40]", delegate
		{
			Check.Equal("10,20,30,40", Join(ApplicationInfoService.ParseVersionParts("10.20.30.40")), "multi-digit");
		});

		// 前导零:int.Parse("04")=4
		yield return ("ParseVersionParts: leading zero \"1.2.3.04\" -> [1,2,3,4]", delegate
		{
			Check.Equal("1,2,3,4", Join(ApplicationInfoService.ParseVersionParts("1.2.3.04")), "leading zero");
		});

		// 3 段(!=4)-> null
		yield return ("ParseVersionParts: three parts \"1.2.3\" -> null", delegate
		{
			Check.Null(ApplicationInfoService.ParseVersionParts("1.2.3"), "three parts");
		});

		// 5 段(!=4)-> null
		yield return ("ParseVersionParts: five parts \"1.2.3.4.5\" -> null", delegate
		{
			Check.Null(ApplicationInfoService.ParseVersionParts("1.2.3.4.5"), "five parts");
		});

		// 空串 -> Split 得 [""](1 段)-> null
		yield return ("ParseVersionParts: empty string -> null (1 part)", delegate
		{
			Check.Null(ApplicationInfoService.ParseVersionParts(""), "empty");
		});

		// 非数字段 -> int.Parse 抛 FormatException(由 CheckForUpdates 内层 catch 兜住)
		yield return ("ParseVersionParts: non-numeric part \"1.2.3.x\" -> throws FormatException", delegate
		{
			bool threw = false;
			try
			{
				ApplicationInfoService.ParseVersionParts("1.2.3.x");
			}
			catch (FormatException)
			{
				threw = true;
			}
			Check.True(threw, "non-numeric throws");
		});

		// 尾部空段(4 段但末段为 "")-> int.Parse("") 抛 FormatException
		yield return ("ParseVersionParts: trailing empty part \"1.2.3.\" -> throws FormatException", delegate
		{
			bool threw = false;
			try
			{
				ApplicationInfoService.ParseVersionParts("1.2.3.");
			}
			catch (FormatException)
			{
				threw = true;
			}
			Check.True(threw, "empty segment throws");
		});

		// ===== CompareVersionParts:逐段比较,首个不等段定胜负(高位优先) =====

		yield return ("CompareVersionParts: equal -> 0", delegate
		{
			Check.Equal(0, ApplicationInfoService.CompareVersionParts(new[] { 1, 2, 3, 4 }, new[] { 1, 2, 3, 4 }), "equal");
		});

		// 末段 current<latest -> -1(当前更旧 = 有新版本)
		yield return ("CompareVersionParts: last part current<latest -> -1", delegate
		{
			Check.Equal(-1, ApplicationInfoService.CompareVersionParts(new[] { 1, 2, 3, 4 }, new[] { 1, 2, 3, 5 }), "last lower");
		});

		// 末段 current>latest -> 1
		yield return ("CompareVersionParts: last part current>latest -> 1", delegate
		{
			Check.Equal(1, ApplicationInfoService.CompareVersionParts(new[] { 1, 2, 3, 5 }, new[] { 1, 2, 3, 4 }), "last higher");
		});

		// 高位优先:第 1 段大压过低位全小 -> 1
		yield return ("CompareVersionParts: high part dominates low parts -> 1", delegate
		{
			Check.Equal(1, ApplicationInfoService.CompareVersionParts(new[] { 2, 0, 0, 0 }, new[] { 1, 9, 9, 9 }), "high dominates");
		});

		// 第 1 段小 -> -1(即便低位更大)
		yield return ("CompareVersionParts: high part current<latest -> -1", delegate
		{
			Check.Equal(-1, ApplicationInfoService.CompareVersionParts(new[] { 1, 9, 9, 9 }, new[] { 2, 0, 0, 0 }), "high lower");
		});

		// 中间段(第 3 段)决定
		yield return ("CompareVersionParts: middle part decides -> -1", delegate
		{
			Check.Equal(-1, ApplicationInfoService.CompareVersionParts(new[] { 1, 2, 0, 9 }, new[] { 1, 2, 1, 0 }), "middle decides");
		});
	}
}
