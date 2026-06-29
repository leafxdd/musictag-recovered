using System;
using System.Collections.Generic;
using MusicTag.Schemes;

namespace MusicTag.Tests;

// FilenameRelatedBatchDialog.FilenameRegexCaptureExtractor 的 characterization（纯逻辑、无 fixture）。
// 锁定"从文件名按正则提取捕获组 + 括号/书名号保护段 masking"的行为。可见性由 private sealed 放宽
// 为 internal sealed（仅可见性、零逻辑改动）。
//
// 历史:此处原先 probe 出一处 latent bug——保护段 masking 对捕获分组并未生效(variant.Text 误存
// mask **前**原文、最深 masked 版本从未进入列表、还原循环又从 matchedVariantIndex+1 起跳过匹配
// 变体自身),致括号内的分隔符仍被正则当作分隔。该 bug 已作为**显式行为修正**修复(两处:variant.Text
// 改存 mask **后**文本 + 还原循环起点含匹配变体本身),本测试断言**修复后的正确行为**:括号/嵌套/
// 同层多段保护内部的分隔符被屏蔽,整体落入同一捕获组,再逐层还原为原文。
internal static class FilenameRegexCaptureExtractorCharacterization
{
	private static List<string> Extract(string filename, string pattern)
	{
		return new FilenameRelatedBatchDialog.FilenameRegexCaptureExtractor(filename, pattern).Captures;
	}

	public static IEnumerable<(string, Action)> All()
	{
		yield return ("Extractor: no protected segment, non-greedy two-group split", delegate
		{
			List<string> captures = Extract("Artist - Title", "^(.+?) - (.+)$");
			Check.Equal(2, captures.Count, "count");
			Check.Equal("Artist", captures[0], "[0]");
			Check.Equal("Title", captures[1], "[1]");
		});

		yield return ("Extractor: no regex match -> no captures", delegate
		{
			List<string> captures = Extract("NoSeparatorHere", "^(.+?) - (.+)$");
			Check.Equal(0, captures.Count, "count");
		});

		yield return ("Extractor: single capture group returns whole filename", delegate
		{
			List<string> captures = Extract("Song", "^(.+)$");
			Check.Equal(1, captures.Count, "count");
			Check.Equal("Song", captures[0], "[0]");
		});

		// 尾部括号:括号段被 masking 为占位符,落入贪婪尾组 group2,再还原为原括号文本。
		// (修复前此用例靠"masking 未生效、括号原样被贪婪尾组吃掉"巧合得到同一结果;修复后改由
		//  保护段屏蔽 + 还原得到,结果一致。)
		yield return ("Extractor: trailing parenthesized segment falls into greedy tail group", delegate
		{
			List<string> captures = Extract("A - B (x - y)", "^(.+?) - (.+)$");
			Check.Equal(2, captures.Count, "count");
			Check.Equal("A", captures[0], "[0]");
			Check.Equal("B (x - y)", captures[1], "[1]");
		});

		// 中部括号(修复的 latent bug):masking 生效后 "(b - c)" 整体被占位符屏蔽,内部 " - " 不再
		// 被当分隔符,故 "A (b - c)" 完整落入 group1。修复前曾被错切成 "A (b" / "c) - D"。
		yield return ("Extractor: middle parenthesized inner separator is shielded (fixed)", delegate
		{
			List<string> captures = Extract("A (b - c) - D", "^(.+?) - (.+)$");
			Check.Equal(2, captures.Count, "count");
			Check.Equal("A (b - c)", captures[0], "[0]");
			Check.Equal("D", captures[1], "[1]");
		});

		// 嵌套括号:逐层 mask(内层 depth0、外层 depth1)后匹配最深变体,还原循环自最深逐层恢复占位符
		// -> 整体作为单一捕获组返回。验证多层 masking + 逐层还原方向(深->浅)正确。
		yield return ("Extractor: nested parentheses fully restored into single group (fixed)", delegate
		{
			List<string> captures = Extract("((a - b))", "^(.+)$");
			Check.Equal(1, captures.Count, "count");
			Check.Equal("((a - b))", captures[0], "[0]");
		});

		// 同层两个保护段:两段各被独立占位符屏蔽,中间的 " - " 是唯一真正的分隔符 -> 正确两分,
		// 各段还原为原括号文本。修复前会被错切成 "(a" / "b) - (c - d)"。
		yield return ("Extractor: two same-level protected segments split on the real separator (fixed)", delegate
		{
			List<string> captures = Extract("(a - b) - (c - d)", "^(.+?) - (.+)$");
			Check.Equal(2, captures.Count, "count");
			Check.Equal("(a - b)", captures[0], "[0]");
			Check.Equal("(c - d)", captures[1], "[1]");
		});
	}
}
