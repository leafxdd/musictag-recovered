using System;
using System.Collections.Generic;
using MusicTag.Schemes;

namespace MusicTag.Tests;

// FilenameRelatedBatchDialog.FilenameRegexCaptureExtractor 的 characterization（纯逻辑、无 fixture）。
// 锁定"从文件名按正则提取捕获组 + 括号/书名号保护段 masking"的现状行为——这是 write/rename
// 结构批次最易错处。可见性由 private sealed 放宽为 internal sealed（仅可见性、零逻辑改动）。
//
// probe-first 已确认一处 latent bug：保护段 masking 对捕获分组**并未生效**。构造函数用
// maskedVariants[i].Text 做匹配,而 variant.Text 始终是该轮 mask **前**的原文(最深 mask 版本
// 从未进入列表),故括号内的分隔符仍被正则当作分隔。下方 middle-parenthesized 用例锁定此现状,
// 供后续 write/rename 批次决策是否修复(characterization 锁定 IS,不在此改 SHOULD)。
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

		// 尾部括号:良性——括号落入贪婪尾组 group2,结果恰好合理(括号原样保留在 title 里)。
		// 注意这并非保护机制之功(masking 未生效),仅因 (.+)$ 贪婪吃尾。
		yield return ("Extractor: trailing parenthesized segment falls into greedy tail group", delegate
		{
			List<string> captures = Extract("A - B (x - y)", "^(.+?) - (.+)$");
			Check.Equal(2, captures.Count, "count");
			Check.Equal("A", captures[0], "[0]");
			Check.Equal("B (x - y)", captures[1], "[1]");
		});

		// 中部括号:暴露 latent bug——保护段 masking 未生效,括号内 " - " 仍被当分隔符,
		// 导致 "A (b - c)" 被错误切成 "A (b" / "c) - D"(本应整体落入 group1)。锁定现状。
		yield return ("Extractor: middle parenthesized inner separator is NOT shielded (current behavior, latent bug)", delegate
		{
			List<string> captures = Extract("A (b - c) - D", "^(.+?) - (.+)$");
			Check.Equal(2, captures.Count, "count");
			Check.Equal("A (b", captures[0], "[0]");
			Check.Equal("c) - D", captures[1], "[1]");
		});
	}
}
