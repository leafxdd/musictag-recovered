using System;
using System.Collections.Generic;
using MusicTag.States;

namespace MusicTag.Tests;

// ConfigDescriptorState 纯文本/数值辅助的 characterization（无 fixture、确定性、locale 无关）。
// 锁定 track/disc 的 "N/M" 解析与 native 兼容的 single-value 契约——write/rename 结构批次将触及这些逻辑。
// 注入方式：这两个 helper 由 private static 放宽为 internal static（仅可见性，零逻辑改动），经 IVT 可达。
internal static class ConfigDescriptorStateCharacterization
{
	public static IEnumerable<(string, Action)> All()
	{
		yield return ("ParseNumberAndCount \"3/12\" -> (3,12)", delegate
		{
			ConfigDescriptorState.ParseNumberAndCount("3/12", out uint number, out uint count);
			Check.Equal(3u, number, "number");
			Check.Equal(12u, count, "count");
		});

		yield return ("ParseNumberAndCount \"5\" (no slash) -> (5,0)", delegate
		{
			ConfigDescriptorState.ParseNumberAndCount("5", out uint number, out uint count);
			Check.Equal(5u, number, "number");
			Check.Equal(0u, count, "count");
		});

		yield return ("ParseNumberAndCount empty -> (0,0)", delegate
		{
			ConfigDescriptorState.ParseNumberAndCount("", out uint number, out uint count);
			Check.Equal(0u, number, "number");
			Check.Equal(0u, count, "count");
		});

		yield return ("ParseNumberAndCount null -> (0,0)", delegate
		{
			ConfigDescriptorState.ParseNumberAndCount(null, out uint number, out uint count);
			Check.Equal(0u, number, "number");
			Check.Equal(0u, count, "count");
		});

		yield return ("ParseNumberAndCount non-numeric -> (0,0)", delegate
		{
			ConfigDescriptorState.ParseNumberAndCount("abc", out uint number, out uint count);
			Check.Equal(0u, number, "number");
			Check.Equal(0u, count, "count");
		});

		yield return ("ParseNumberAndCount trims whitespace \" 3 / 12 \" -> (3,12)", delegate
		{
			ConfigDescriptorState.ParseNumberAndCount(" 3 / 12 ", out uint number, out uint count);
			Check.Equal(3u, number, "number");
			Check.Equal(12u, count, "count");
		});

		yield return ("ParseNumberAndCount ignores third part \"3/12/15\" -> (3,12)", delegate
		{
			ConfigDescriptorState.ParseNumberAndCount("3/12/15", out uint number, out uint count);
			Check.Equal(3u, number, "number");
			Check.Equal(12u, count, "count");
		});

		yield return ("ParseNumberAndCount trailing slash \"3/\" -> (3,0)", delegate
		{
			ConfigDescriptorState.ParseNumberAndCount("3/", out uint number, out uint count);
			Check.Equal(3u, number, "number");
			Check.Equal(0u, count, "count");
		});

		yield return ("ParseNumberAndCount negative -> (0,0) (uint parse fails)", delegate
		{
			ConfigDescriptorState.ParseNumberAndCount("-1", out uint number, out uint count);
			Check.Equal(0u, number, "number");
			Check.Equal(0u, count, "count");
		});

		yield return ("ToSingleValue null -> empty array", delegate
		{
			string[] result = ConfigDescriptorState.ToSingleValue(null);
			Check.Equal(0, result.Length, "Length");
		});

		yield return ("ToSingleValue empty -> empty array", delegate
		{
			string[] result = ConfigDescriptorState.ToSingleValue("");
			Check.Equal(0, result.Length, "Length");
		});

		yield return ("ToSingleValue value -> single-element array", delegate
		{
			string[] result = ConfigDescriptorState.ToSingleValue("Artist");
			Check.Equal(1, result.Length, "Length");
			Check.Equal("Artist", result[0], "[0]");
		});

		yield return ("ToSingleValue whitespace -> single-element (not blank-stripped)", delegate
		{
			string[] result = ConfigDescriptorState.ToSingleValue(" ");
			Check.Equal(1, result.Length, "Length");
			Check.Equal(" ", result[0], "[0]");
		});
	}
}
