using System;
using System.Collections.Generic;
using MusicTag.Schemes;

namespace MusicTag.Tests;

// FilenameRelatedBatchDialog.BuildFilenameMatchRegex 的 characterization（纯逻辑、无 fixture）。
// 锁定"文件名模板 -> 匹配正则 + token 列表"的现状——ChangeTags(文件名->标签)路径的解析前处理:
// 字面量里的正则元字符逐一转义,每段连续占位符 (@[0-8])+ 合并为一个 token 并整体替换为捕获组 (.*)。
// 由 ChangeTags 内联逻辑提取为 internal static(行为逐字保持),供 characterization 锁定。
internal static class BuildFilenameMatchRegexCharacterization
{
	private static (string regex, List<string> tokens) Build(string pattern)
	{
		return FilenameRelatedBatchDialog.BuildFilenameMatchRegex(pattern);
	}

	public static IEnumerable<(string, Action)> All()
	{
		yield return ("BuildFilenameMatchRegex \"@1 - @2\" -> \"(.*) - (.*)\", tokens [@1,@2]", delegate
		{
			var (regex, tokens) = Build("@1 - @2");
			Check.Equal("(.*) - (.*)", regex, "regex");
			Check.Equal(2, tokens.Count, "token count");
			Check.Equal("@1", tokens[0], "token[0]");
			Check.Equal("@2", tokens[1], "token[1]");
		});

		yield return ("BuildFilenameMatchRegex \"@4@5. @1\" -> consecutive placeholders one token + dot escaped", delegate
		{
			var (regex, tokens) = Build("@4@5. @1");
			Check.Equal("(.*)\\. (.*)", regex, "regex");
			Check.Equal(2, tokens.Count, "token count");
			Check.Equal("@4@5", tokens[0], "token[0]");
			Check.Equal("@1", tokens[1], "token[1]");
		});

		yield return ("BuildFilenameMatchRegex \"@1 (@2)\" -> parentheses escaped", delegate
		{
			var (regex, tokens) = Build("@1 (@2)");
			Check.Equal("(.*) \\((.*)\\)", regex, "regex");
			Check.Equal(2, tokens.Count, "token count");
			Check.Equal("@1", tokens[0], "token[0]");
			Check.Equal("@2", tokens[1], "token[1]");
		});

		yield return ("BuildFilenameMatchRegex tab whitespace normalized to space", delegate
		{
			var (regex, tokens) = Build("@1\t@2");
			Check.Equal("(.*) (.*)", regex, "regex");
			Check.Equal(2, tokens.Count, "token count");
		});

		yield return ("BuildFilenameMatchRegex single placeholder \"@1\" -> \"(.*)\"", delegate
		{
			var (regex, tokens) = Build("@1");
			Check.Equal("(.*)", regex, "regex");
			Check.Equal(1, tokens.Count, "token count");
			Check.Equal("@1", tokens[0], "token[0]");
		});

		yield return ("BuildFilenameMatchRegex all-adjacent \"@4@5@1\" -> one merged token \"(.*)\"", delegate
		{
			var (regex, tokens) = Build("@4@5@1");
			Check.Equal("(.*)", regex, "regex");
			Check.Equal(1, tokens.Count, "token count");
			Check.Equal("@4@5@1", tokens[0], "token[0]");
		});
	}
}
