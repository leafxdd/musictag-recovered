using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using MusicTag.Schemes;

namespace MusicTag.Tests;

// FilenameRelatedBatchDialog.FilenameRegexCaptureExtractor 的 characterization（纯逻辑、无 fixture）。
// 锁定"从文件名按正则提取捕获组 + 括号/书名号/引号保护段 masking"的行为。可见性由 private sealed
// 放宽为 internal sealed（仅可见性、零逻辑改动）。
//
// 历史:此处原先 probe 出一处 latent bug——保护段 masking 对捕获分组并未生效。根因(经对抗验证收敛)
// 只有一个:variant.Text 误存 mask **前**原文,致最深 masked 版本从未进入 maskedVariants、匹配退化到
// 未屏蔽原文(还原循环从 matchedVariantIndex+1 起跳过命中变体,只是旧 Text 语义下的自洽配套)。
// 已作为**显式行为修正**修复,两处耦合 + 一处硬化:
//   (1) variant.Text 改存 mask **后**文本;(2) 还原循环起点含匹配变体自身——(1)(2) 合起来使 masking
//       对捕获分组真正生效;
//   (3) 占位符 segmentIndex 定宽 D5——消除「masking 生效后还原路径首次真正运行」暴露的【同层 ≥11 段】
//       前缀串扰(seg1 占位符曾是 seg10 的前缀,String.Replace 会损坏第 11 段)。
//
// 另:同源的引号保护缺陷亦已作为行为修正修复——ProtectedSegmentRegex 的 4 个引号分支(“”/‘’/『』/「」)
// 原缺 * 量词(写作 “[^“”]”),只能匹配【单字符】引号段,多字符段(如「a - b」)不被保护而错切;各补 *
// 量词后多字符引号段与括号/书名号一致地被屏蔽(* 是修复前 exactly-1 的严格超集,单字符/空段仍被保护)。
//
// 本测试断言修复后的正确行为:括号/嵌套/同层多段/书名号/引号保护段内部的分隔符被屏蔽,整体落入同一捕获组。
internal static class FilenameRegexCaptureExtractorCharacterization
{
	private static List<string> Extract(string filename, string pattern)
	{
		return new FilenameRelatedBatchDialog.FilenameRegexCaptureExtractor(filename, pattern).Captures;
	}

	public static IEnumerable<(string, Action)> All()
	{
		yield return ("Extractor: user regex carries a finite two-second timeout", delegate
		{
			Regex regex = FilenameRelatedBatchDialog.CreateFilenameRegex("^(.+)$");
			Check.Equal(TimeSpan.FromSeconds(2.0), regex.MatchTimeout, "timeout");
		});

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

		// ≥11 同层保护段(回归守卫):占位符 segmentIndex 定宽 D5 后,seg1 不再是 seg10 的前缀,
		// String.Replace 还原不串扰,全部 11 段完整复原。masking 生效令此还原路径首次真正运行;
		// 若 segmentIndex 不定宽,第 11 段会被静默损坏——此用例锁定该回归已修复。
		yield return ("Extractor: 11 same-level segments restore without placeholder-prefix collision (fixed)", delegate
		{
			List<string> captures = Extract("(a)(b)(c)(d)(e)(f)(g)(h)(i)(j)(k)", "^(.+)$");
			Check.Equal(1, captures.Count, "count");
			Check.Equal("(a)(b)(c)(d)(e)(f)(g)(h)(i)(j)(k)", captures[0], "[0]");
		});

		// 非圆括号保护段(书名号):masking 对 ProtectedSegmentRegex 的带 * 量词分支同样生效,
		// 《》内的 " - " 被屏蔽,整体落入 group1。(CLAUDE.md 明确把书名号/括号列为保护用例。)
		yield return ("Extractor: CJK book-title marks shield inner separator (fixed)", delegate
		{
			List<string> captures = Extract("《b - c》 - D", "^(.+?) - (.+)$");
			Check.Equal(2, captures.Count, "count");
			Check.Equal("《b - c》", captures[0], "[0]");
			Check.Equal("D", captures[1], "[1]");
		});

		// 引号保护(双引号多字符,branch 8,修复的引号缺 * bug):4 个引号分支补 * 量词后,“a - b” 整体被屏蔽。
		// 修复前 “[^“”]” 只匹配单字符引号段,“a - b” 不被保护 -> 错切成 "“a" / "b” - D"。
		yield return ("Extractor: multi-char double-quote segment shields inner separator (fixed)", delegate
		{
			List<string> captures = Extract("“a - b” - D", "^(.+?) - (.+)$");
			Check.Equal(2, captures.Count, "count");
			Check.Equal("“a - b”", captures[0], "[0]");
			Check.Equal("D", captures[1], "[1]");
		});

		// 引号保护(单引号多字符,branch 9):锁定第 2 个引号分支补 * 生效。
		yield return ("Extractor: multi-char single-quote segment shields inner separator (fixed)", delegate
		{
			List<string> captures = Extract("‘a - b’ - D", "^(.+?) - (.+)$");
			Check.Equal(2, captures.Count, "count");
			Check.Equal("‘a - b’", captures[0], "[0]");
			Check.Equal("D", captures[1], "[1]");
		});

		// 引号保护(日文白角括号多字符,branch 10):锁定第 3 个引号分支补 * 生效。
		yield return ("Extractor: multi-char white-corner-bracket segment shields inner separator (fixed)", delegate
		{
			List<string> captures = Extract("『a - b』 - D", "^(.+?) - (.+)$");
			Check.Equal(2, captures.Count, "count");
			Check.Equal("『a - b』", captures[0], "[0]");
			Check.Equal("D", captures[1], "[1]");
		});

		// 引号保护(日文角括号多字符,branch 11):同理「a - b」整体屏蔽,验证补 * 对全部 4 个引号分支生效。
		yield return ("Extractor: multi-char CJK corner-bracket segment shields inner separator (fixed)", delegate
		{
			List<string> captures = Extract("「a - b」 - D", "^(.+?) - (.+)$");
			Check.Equal(2, captures.Count, "count");
			Check.Equal("「a - b」", captures[0], "[0]");
			Check.Equal("D", captures[1], "[1]");
		});

		// 单字符引号段(回归守卫):补 * (0+) 是修复前 exactly-1 的严格超集,单字符段仍被保护,
		// 结果与修复前一致——证明补 * 未破坏单字符路径。
		yield return ("Extractor: single-char quote segment still protected after adding * (regression guard)", delegate
		{
			List<string> captures = Extract("“X” - D", "^(.+?) - (.+)$");
			Check.Equal(2, captures.Count, "count");
			Check.Equal("“X”", captures[0], "[0]");
			Check.Equal("D", captures[1], "[1]");
		});

		// 空引号段:补 * 后 “” 也匹配(0 字符),被屏蔽为占位符再原样还原 -> 对普通捕获完全透明
		// (空段内无分隔符,还原回 “”)。锁定该副作用无害。
		yield return ("Extractor: empty quote segment masks transparently (fixed)", delegate
		{
			List<string> captures = Extract("“” - D", "^(.+?) - (.+)$");
			Check.Equal(2, captures.Count, "count");
			Check.Equal("“”", captures[0], "[0]");
			Check.Equal("D", captures[1], "[1]");
		});
	}
}
