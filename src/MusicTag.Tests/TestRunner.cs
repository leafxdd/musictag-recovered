using System;
using System.Collections.Generic;

namespace MusicTag.Tests;

// 极简自写断言 + 运行器（零第三方框架，保持主项目零-NuGet 纯净度）。
// characterization 测试锁定「当前实际行为」（golden master），为后续高风险重构提供回归网。
// 详见 docs/SIMPLIFICATION_PLAN.md Phase 2 characterization 基础设施。
internal sealed class TestFailure : Exception
{
	public TestFailure(string message)
		: base(message)
	{
	}
}

internal static class Check
{
	public static void Equal<T>(T expected, T actual, string label)
	{
		if (!EqualityComparer<T>.Default.Equals(expected, actual))
		{
			throw new TestFailure($"{label}: expected [{expected}], got [{actual}]");
		}
	}

	public static void True(bool condition, string label)
	{
		if (!condition)
		{
			throw new TestFailure(label + ": expected true");
		}
	}

	public static void Null(object value, string label)
	{
		if (value != null)
		{
			throw new TestFailure(label + ": expected null, got [" + value + "]");
		}
	}

	public static void NotNull(object value, string label)
	{
		if (value == null)
		{
			throw new TestFailure(label + ": expected non-null");
		}
	}
}

internal static class TestRunner
{
	public static int RunAll(IEnumerable<(string Name, Action Body)> tests)
	{
		int passed = 0;
		int failed = 0;
		foreach ((string Name, Action Body) test in tests)
		{
			try
			{
				test.Body();
				passed++;
				Console.WriteLine("  PASS  " + test.Name);
			}
			catch (Exception ex)
			{
				failed++;
				Console.WriteLine("  FAIL  " + test.Name + ": " + ex.Message);
			}
		}
		Console.WriteLine();
		Console.WriteLine($"{passed} passed, {failed} failed");
		return (failed == 0) ? 0 : 1;
	}
}
