using System;
using System.Collections.Generic;
using MusicTagWinApp.Writers;

namespace MusicTag.Tests;

// TextSimilarityCalculator 的 characterization(零生产改动:两方法已 public static,IVT 可见)。
// 它是 4 provider 候选排序的相似度【基元】:TrackSearchResult.CalculateSimilarityScores -> 排序候选列表。
// 全纯函数(无 IO/UI/Settings;CalculateEditDistance 用 char.ToUpperInvariant + float 运算,无 CurrentCulture 依赖)。
// 锁定:
//   - CalculateTextSimilarity = 1 - 大小写不敏感 Levenshtein 编辑距离 / max(长度);双空 -> 0f(maxLength<=0 短路);
//   - CalculateArtistSimilarity:base >= 0.6 直接返回(不拆分);否则按 [/&,]|，|、 拆 candidate,
//     拆分后(或 RemoveAll 空白后)份数 <= 1 则返回 base;否则对每份取 Max(sim(ref, part) - 0.01f, base)。
// float 边界(0f/1f)用 Check.Equal 精确锁;分数用 CheckClose 容差(避免浮点字面漂移)。
internal static class TextSimilarityCalculatorCharacterization
{
	// 分数容差断言(0f/1f 精确边界仍用 Check.Equal;此处用于 1-1/3、1-3/7、0.99 等计算值)。
	private static void CheckClose(string name, float expected, float actual)
	{
		Check.True(Math.Abs(expected - actual) < 0.001f, name + " (expected ~" + expected + ", got " + actual + ")");
	}

	public static IEnumerable<(string, Action)> All()
	{
		// ===== CalculateTextSimilarity =====

		// 完全相同 -> 编辑距离 0 -> 1f(精确)
		yield return ("TextSimilarity: identical -> 1f", delegate
		{
			Check.Equal(1f, TextSimilarityCalculator.CalculateTextSimilarity("abc", "abc"), "identical -> 1");
		});

		// 双空 -> maxLength<=0 短路 -> 0f(精确)
		yield return ("TextSimilarity: both empty -> 0f (maxLength<=0 short-circuit)", delegate
		{
			Check.Equal(0f, TextSimilarityCalculator.CalculateTextSimilarity("", ""), "both empty -> 0");
		});

		// reference 空、candidate 非空 -> editDist=len -> 1-len/len=0f
		yield return ("TextSimilarity: reference empty -> 0f", delegate
		{
			Check.Equal(0f, TextSimilarityCalculator.CalculateTextSimilarity("", "abc"), "ref empty -> 0");
		});

		// candidate 空、reference 非空 -> 0f
		yield return ("TextSimilarity: candidate empty -> 0f", delegate
		{
			Check.Equal(0f, TextSimilarityCalculator.CalculateTextSimilarity("abc", ""), "candidate empty -> 0");
		});

		// 大小写不敏感(ToUpperInvariant)-> 编辑距离 0 -> 1f
		yield return ("TextSimilarity: case-insensitive ABC/abc -> 1f", delegate
		{
			Check.Equal(1f, TextSimilarityCalculator.CalculateTextSimilarity("ABC", "abc"), "case-insensitive -> 1");
		});

		// 单字符替换 cat/car -> editDist 1, maxLen 3 -> 1-1/3
		yield return ("TextSimilarity: single substitution cat/car -> 1-1/3", delegate
		{
			CheckClose("cat/car", 1f - 1f / 3f, TextSimilarityCalculator.CalculateTextSimilarity("cat", "car"));
		});

		// 经典 kitten/sitting -> editDist 3, maxLen 7 -> 1-3/7
		yield return ("TextSimilarity: kitten/sitting -> 1-3/7", delegate
		{
			CheckClose("kitten/sitting", 1f - 3f / 7f, TextSimilarityCalculator.CalculateTextSimilarity("kitten", "sitting"));
		});

		// 长度不同 abc/ab -> editDist 1(删 1), maxLen 3 -> 1-1/3
		yield return ("TextSimilarity: length diff abc/ab -> 1-1/3", delegate
		{
			CheckClose("abc/ab", 1f - 1f / 3f, TextSimilarityCalculator.CalculateTextSimilarity("abc", "ab"));
		});

		// ===== CalculateArtistSimilarity =====

		// base >= 0.6(完全相同=1)-> 直接返回,不拆分 -> 1f
		yield return ("ArtistSimilarity: base >= 0.6 (identical) short-circuits -> 1f", delegate
		{
			Check.Equal(1f, TextSimilarityCalculator.CalculateArtistSimilarity("周杰伦", "周杰伦"), "identical artist -> 1");
		});

		// base < 0.6 但拆分后某份完全匹配 ref -> Max(1 - 0.01, base) = 0.99
		yield return ("ArtistSimilarity: split lifts low base via matching part (-0.01 penalty) -> ~0.99", delegate
		{
			CheckClose("周杰伦/周杰伦、群星", 0.99f, TextSimilarityCalculator.CalculateArtistSimilarity("周杰伦", "周杰伦、群星"));
		});

		// 分隔符 '/' 被识别拆分 -> 匹配份提升 -> ~0.99
		yield return ("ArtistSimilarity: separator '/' -> split -> ~0.99", delegate
		{
			CheckClose("a / a/b", 0.99f, TextSimilarityCalculator.CalculateArtistSimilarity("a", "a/b"));
		});

		// 分隔符 '&'
		yield return ("ArtistSimilarity: separator '&' -> split -> ~0.99", delegate
		{
			CheckClose("a / a&b", 0.99f, TextSimilarityCalculator.CalculateArtistSimilarity("a", "a&b"));
		});

		// 分隔符 ',' (ASCII)
		yield return ("ArtistSimilarity: separator ',' (ASCII) -> split -> ~0.99", delegate
		{
			CheckClose("a / a,b", 0.99f, TextSimilarityCalculator.CalculateArtistSimilarity("a", "a,b"));
		});

		// 分隔符 '，' (全角逗号)
		yield return ("ArtistSimilarity: separator fullwidth ',' -> split -> ~0.99", delegate
		{
			CheckClose("a / a，b", 0.99f, TextSimilarityCalculator.CalculateArtistSimilarity("a", "a，b"));
		});

		// 分隔符 '、' (顿号)
		yield return ("ArtistSimilarity: separator '、' -> split -> ~0.99", delegate
		{
			CheckClose("a / a、b", 0.99f, TextSimilarityCalculator.CalculateArtistSimilarity("a", "a、b"));
		});

		// 无分隔符、base < 0.6 -> Split 返回单元素 -> Count<=1 第一守卫 -> 返回 base(0)
		yield return ("ArtistSimilarity: no separator, low base -> first Count<=1 guard -> base", delegate
		{
			Check.Equal(0f, TextSimilarityCalculator.CalculateArtistSimilarity("xyz", "abc"), "no separator -> base 0");
		});

		// 拆出空白份,RemoveAll 后只剩 1 份 -> 第二守卫 -> 返回 base(0)
		yield return ("ArtistSimilarity: RemoveAll empties to Count<=1 -> second guard -> base", delegate
		{
			Check.Equal(0f, TextSimilarityCalculator.CalculateArtistSimilarity("x", "a,"), "trailing-sep collapses to one part -> base 0");
		});

		// 拆分有多份但都不匹配 ref -> 每份 sim-0.01 < base -> 返回 base(0)
		yield return ("ArtistSimilarity: parts all mismatch -> base", delegate
		{
			Check.Equal(0f, TextSimilarityCalculator.CalculateArtistSimilarity("zzz", "a、b"), "no part matches -> base 0");
		});
	}
}
