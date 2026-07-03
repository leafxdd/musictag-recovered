using System;
using System.Collections.Generic;
using MusicTagWinApp.Instances;

namespace MusicTag.Tests;

// TextUtilities 自然排序 characterization(零行为改动从 StateFieldInstance.ListViewItemNaturalComparer 提取的
// CompareNaturalText / SplitNaturalSortSegments,trackstr/discstr 列排序用)。锁定:
//   - 数字段按【数值】比(非字典序):"2" < "10";
//   - 前导零数值相等时 fallback string.Compare:"01" < "1";
//   - 数字段超 Int64 时 long.TryParse 失败 -> fallback string.Compare(字典序);
//   - 文本段不同 -> 比 text+number 拼接串;段数不等 -> 比段数;
//   - 分段正则 (\D*)(\d*):交替「非数字段 + 数字段」,空匹配被过滤。
// 全纯函数(参数 + 一个 static Regex),无状态依赖。
internal static class NaturalSortCharacterization
{
	// CompareNaturalText 只保证返回值【符号】(IComparer 语义),故断言符号而非具体值。
	private static void CheckSign(string name, int expectedSign, int actual)
	{
		Check.Equal(expectedSign, Math.Sign(actual), name + " (raw=" + actual + ")");
	}

	public static IEnumerable<(string, Action)> All()
	{
		// ===== CompareNaturalText:数值序(自然排序核心) =====

		// "2" vs "10":纯数字按数值 -> 2 < 10(字典序会判 "10" < "2",此处不然)
		yield return ("CompareNaturalText: \"2\" < \"10\" (numeric not lexical)", delegate
		{
			CheckSign("2 vs 10", -1, TextUtilities.CompareNaturalText("2", "10"));
		});

		// 反向对称
		yield return ("CompareNaturalText: \"10\" > \"2\"", delegate
		{
			CheckSign("10 vs 2", 1, TextUtilities.CompareNaturalText("10", "2"));
		});

		// 相同 -> 0
		yield return ("CompareNaturalText: equal -> 0", delegate
		{
			CheckSign("5 vs 5", 0, TextUtilities.CompareNaturalText("5", "5"));
		});

		// 个位数值序
		yield return ("CompareNaturalText: \"1\" < \"2\"", delegate
		{
			CheckSign("1 vs 2", -1, TextUtilities.CompareNaturalText("1", "2"));
		});

		// 前导零:数值相等(1==1)-> fallback string.Compare("01","1") -> '0'<'1' -> "01" 在前
		yield return ("CompareNaturalText: \"01\" < \"1\" (equal numeric -> string.Compare fallback)", delegate
		{
			CheckSign("01 vs 1", -1, TextUtilities.CompareNaturalText("01", "1"));
		});

		// 文本前缀相同 + 数字段数值序:"track2" < "track10"
		yield return ("CompareNaturalText: \"track2\" < \"track10\" (shared prefix, numeric tail)", delegate
		{
			CheckSign("track2 vs track10", -1, TextUtilities.CompareNaturalText("track2", "track10"));
		});

		// 文本段不同 -> 比 text+number 拼接:"a1" vs "b1" -> 'a'<'b'
		yield return ("CompareNaturalText: differing text segment -> compare concatenation (\"a1\" < \"b1\")", delegate
		{
			CheckSign("a1 vs b1", -1, TextUtilities.CompareNaturalText("a1", "b1"));
		});

		// 数字段超 Int64(20 位 9 > long.MaxValue 的 19 位)-> long.TryParse 双失败 -> fallback string.Compare 末位
		yield return ("CompareNaturalText: overflow Int64 -> string.Compare fallback (last digit)", delegate
		{
			CheckSign("big99 vs big98", 1, TextUtilities.CompareNaturalText("99999999999999999999", "99999999999999999998"));
		});

		// 段数不等:"a" vs "a1" -> 段0 text 同、number ""!="1"、TryParse("") 失败 -> string.Compare("","1") -> <0
		yield return ("CompareNaturalText: \"a\" < \"a1\" (empty number segment vs digit)", delegate
		{
			CheckSign("a vs a1", -1, TextUtilities.CompareNaturalText("a", "a1"));
		});

		// 双空 -> 两边 0 段 -> 段数差 0-0 -> 0
		yield return ("CompareNaturalText: both empty -> 0", delegate
		{
			CheckSign("empty vs empty", 0, TextUtilities.CompareNaturalText("", ""));
		});

		// 空 vs 非空 -> 0 段 vs 1 段 -> 段数差 0-1 -> <0
		yield return ("CompareNaturalText: empty < non-empty (segment-count diff)", delegate
		{
			CheckSign("empty vs 1", -1, TextUtilities.CompareNaturalText("", "1"));
		});

		// ===== SplitNaturalSortSegments:分段结构 =====

		// 交替文本/数字:"a1b2" -> [("a","1"),("b","2")]
		yield return ("SplitNaturalSortSegments: \"a1b2\" -> 2 alternating segments", delegate
		{
			List<(string Text, string Number)> segments = TextUtilities.SplitNaturalSortSegments("a1b2");
			Check.Equal(2, segments.Count, "a1b2 count");
			Check.Equal("a", segments[0].Text, "seg0 text");
			Check.Equal("1", segments[0].Number, "seg0 number");
			Check.Equal("b", segments[1].Text, "seg1 text");
			Check.Equal("2", segments[1].Number, "seg1 number");
		});

		// 纯数字 -> 单段空文本 + 数字
		yield return ("SplitNaturalSortSegments: \"123\" -> [(\"\",\"123\")]", delegate
		{
			List<(string Text, string Number)> segments = TextUtilities.SplitNaturalSortSegments("123");
			Check.Equal(1, segments.Count, "123 count");
			Check.Equal("", segments[0].Text, "seg0 text empty");
			Check.Equal("123", segments[0].Number, "seg0 number");
		});

		// 纯文本 -> 单段文本 + 空数字
		yield return ("SplitNaturalSortSegments: \"abc\" -> [(\"abc\",\"\")]", delegate
		{
			List<(string Text, string Number)> segments = TextUtilities.SplitNaturalSortSegments("abc");
			Check.Equal(1, segments.Count, "abc count");
			Check.Equal("abc", segments[0].Text, "seg0 text");
			Check.Equal("", segments[0].Number, "seg0 number empty");
		});

		// 空串 -> 0 段(空匹配被过滤)
		yield return ("SplitNaturalSortSegments: \"\" -> 0 segments (empty match filtered)", delegate
		{
			List<(string Text, string Number)> segments = TextUtilities.SplitNaturalSortSegments("");
			Check.Equal(0, segments.Count, "empty count");
		});

		// 数字在前:"1a" -> [("","1"),("a","")] (位置0 \D* 空 + \d* "1";位置1 \D* "a" + \d* 空)
		yield return ("SplitNaturalSortSegments: \"1a\" -> [(\"\",\"1\"),(\"a\",\"\")]", delegate
		{
			List<(string Text, string Number)> segments = TextUtilities.SplitNaturalSortSegments("1a");
			Check.Equal(2, segments.Count, "1a count");
			Check.Equal("", segments[0].Text, "seg0 text empty");
			Check.Equal("1", segments[0].Number, "seg0 number");
			Check.Equal("a", segments[1].Text, "seg1 text");
			Check.Equal("", segments[1].Number, "seg1 number empty");
		});
	}
}
