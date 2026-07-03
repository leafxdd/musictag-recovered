using System;
using System.Collections.Generic;
using MusicTag.Candidates;
using MusicTagWinApp.Listeners;
using MusicTagWinApp.Writers;

namespace MusicTag.Tests;

// Trie 最长匹配引擎 characterization —— ChineseTextConverter 繁简/简繁转换的底层匹配引擎，此前零覆盖
// (grep 测试目录 Trie/NextMatch = 0)。纯逻辑、零产品改动，直接构造：
//   PhraseTrie + PhraseTrieBuilder.AddMapping(...) + (PhraseMatcher)GetMatcher(text).NextMatch()。
// 手工 trace FindNextMatch 状态机锁定的行为：
//   - Prefix 节点续走；IntermediateWord 记 pending 回退点(较短完整词)后继续找更长；Word 立即返回。
//   - 下一字符无子节点时：有 pending -> 用 pending 中间词返回；否则起点前移一位重扫。
//   - 最长匹配优先(更长 Word 覆盖较短 IntermediateWord)。
//   - ApplyAsciiBoundaryFilter 仅当匹配 key 的首/末字符为 ASCII(<0x7f) 且与相邻字符同 token-class
//     (letter-like 或 digit-like，见 AsciiTokenClassifier)时抑制该匹配并【消耗】其文本(不回退子串重试)；
//     CJK 首/末字符(>=0x7f)从不触发抑制。
// 断言全 locale 无关、确定性：CJK 作主匹配以避开边界过滤；ASCII 边界行为单列用例。
internal static class TrieMatcherCharacterization
{
	private static PhraseTrie BuildTrie(params PhraseMapping[] mappings)
	{
		PhraseTrie trie = new PhraseTrie();
		foreach (PhraseMapping mapping in mappings)
		{
			PhraseTrieBuilder.AddMapping(trie, mapping);
		}
		return trie;
	}

	public static IEnumerable<(string, Action)> All()
	{
		// 1. 单字符 CJK 映射：匹配 key + MatchStartIndex + replacement，随后 null 结束。
		yield return ("Trie: single-char CJK mapping matches once then null", delegate
		{
			PhraseMatcher matcher = (PhraseMatcher)BuildTrie(new PhraseMapping("中", "zhong")).GetMatcher("中");
			Check.Equal("中", matcher.NextMatch(), "first match key");
			Check.Equal(0, matcher.MatchStartIndex, "match start index");
			Check.Equal("zhong", matcher.GetReplacementAt(0), "replacement[0]");
			Check.Null(matcher.NextMatch(), "no further match -> null");
		});

		// 2. 最长匹配优先：更长的 Word 覆盖较短的 IntermediateWord。
		yield return ("Trie: longest-match wins over shorter intermediate word", delegate
		{
			PhraseMatcher matcher = (PhraseMatcher)BuildTrie(
				new PhraseMapping("中", "A"),
				new PhraseMapping("中国", "B")).GetMatcher("中国");
			Check.Equal("中国", matcher.NextMatch(), "longest match key");
			Check.Equal(0, matcher.MatchStartIndex, "match start index");
			Check.Equal("B", matcher.GetReplacementAt(0), "longest replacement");
			Check.Null(matcher.NextMatch(), "no further match -> null");
		});

		// 3. IntermediateWord 回退：更长词不成立时，退回较短完整词。
		yield return ("Trie: falls back to shorter intermediate word when longer fails", delegate
		{
			PhraseMatcher matcher = (PhraseMatcher)BuildTrie(
				new PhraseMapping("中", "A"),
				new PhraseMapping("中国", "B")).GetMatcher("中人");
			Check.Equal("中", matcher.NextMatch(), "fallback shorter match");
			Check.Equal(0, matcher.MatchStartIndex, "match start index");
			Check.Equal("A", matcher.GetReplacementAt(0), "shorter replacement");
			Check.Null(matcher.NextMatch(), "trailing non-match -> null");
		});

		// 4. 前导不匹配字符被跳过，MatchStartIndex 反映匹配偏移。
		yield return ("Trie: leading non-match chars skipped, MatchStartIndex offset", delegate
		{
			PhraseMatcher matcher = (PhraseMatcher)BuildTrie(new PhraseMapping("国", "G")).GetMatcher("甲国乙");
			Check.Equal("国", matcher.NextMatch(), "match after skip");
			Check.Equal(1, matcher.MatchStartIndex, "offset index 1");
			Check.Equal("G", matcher.GetReplacementAt(0), "replacement");
			Check.Null(matcher.NextMatch(), "trailing non-match -> null");
		});

		// 5. 多个独立匹配按序返回，各自 MatchStartIndex/replacement 正确。
		yield return ("Trie: multiple sequential matches in order", delegate
		{
			PhraseMatcher matcher = (PhraseMatcher)BuildTrie(
				new PhraseMapping("中", "A"),
				new PhraseMapping("国", "B")).GetMatcher("中国");
			Check.Equal("中", matcher.NextMatch(), "match 1 key");
			Check.Equal(0, matcher.MatchStartIndex, "match 1 index");
			Check.Equal("A", matcher.GetReplacementAt(0), "match 1 replacement");
			Check.Equal("国", matcher.NextMatch(), "match 2 key");
			Check.Equal(1, matcher.MatchStartIndex, "match 2 index");
			Check.Equal("B", matcher.GetReplacementAt(0), "match 2 replacement");
			Check.Null(matcher.NextMatch(), "no further match -> null");
		});

		// 6. ASCII 边界抑制(字母)：ASCII 词嵌在同类字母 token 内被抑制并消耗 -> 整体无匹配。
		yield return ("Trie: ASCII match suppressed between same-class letters", delegate
		{
			PhraseMatcher matcher = (PhraseMatcher)BuildTrie(new PhraseMapping("ab", "X")).GetMatcher("cabd");
			Check.Null(matcher.NextMatch(), "ab abutting letters c/d -> suppressed -> null");
		});

		// 7. ASCII 边界不抑制：空格分隔时正常匹配。
		yield return ("Trie: ASCII match not suppressed when space-delimited", delegate
		{
			PhraseMatcher matcher = (PhraseMatcher)BuildTrie(new PhraseMapping("ab", "X")).GetMatcher(" ab ");
			Check.Equal("ab", matcher.NextMatch(), "space-delimited match");
			Check.Equal(1, matcher.MatchStartIndex, "match index");
			Check.Equal("X", matcher.GetReplacementAt(0), "replacement");
			Check.Null(matcher.NextMatch(), "no further match -> null");
		});

		// 8. ASCII 词夹在 CJK 之间不抑制(CJK 非 ASCII token-class)。
		yield return ("Trie: ASCII match not suppressed between CJK chars", delegate
		{
			PhraseMatcher matcher = (PhraseMatcher)BuildTrie(new PhraseMapping("ab", "X")).GetMatcher("中ab中");
			Check.Equal("ab", matcher.NextMatch(), "cjk-delimited match");
			Check.Equal(1, matcher.MatchStartIndex, "match index");
			Check.Null(matcher.NextMatch(), "no further match -> null");
		});

		// 9. ASCII 边界抑制(数字)：数字 token 同类邻接被抑制。
		yield return ("Trie: ASCII digit match suppressed between same-class digits", delegate
		{
			PhraseMatcher matcher = (PhraseMatcher)BuildTrie(new PhraseMapping("12", "N")).GetMatcher("312");
			Check.Null(matcher.NextMatch(), "12 abutting digit 3 -> suppressed -> null");
		});

		// 10. 空文本 -> 立即 null。
		yield return ("Trie: empty text yields null", delegate
		{
			PhraseMatcher matcher = (PhraseMatcher)BuildTrie(new PhraseMapping("中", "A")).GetMatcher("");
			Check.Null(matcher.NextMatch(), "empty text -> null");
		});

		// 11. 全不在词典 -> null。
		yield return ("Trie: no dictionary hit yields null", delegate
		{
			PhraseMatcher matcher = (PhraseMatcher)BuildTrie(new PhraseMapping("中", "A")).GetMatcher("甲乙");
			Check.Null(matcher.NextMatch(), "no hit -> null");
		});

		// 12. 多 replacement + GetReplacementAt 越界返回 null。
		yield return ("Trie: multi-replacement GetReplacementAt with out-of-range null", delegate
		{
			PhraseMatcher matcher = (PhraseMatcher)BuildTrie(new PhraseMapping("中", "A", "B")).GetMatcher("中");
			Check.Equal("中", matcher.NextMatch(), "match key");
			Check.Equal("A", matcher.GetReplacementAt(0), "replacement[0]");
			Check.Equal("B", matcher.GetReplacementAt(1), "replacement[1]");
			Check.Null(matcher.GetReplacementAt(2), "index >= length -> null");
		});
	}
}
