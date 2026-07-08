using System;
using System.Collections.Generic;
using System.Text;
using MusicTag.Composer;
using MusicTagWinApp.Writers;

namespace MusicTag.Tests;

// 歌词处理三簇 characterization(均不改产品代码 / DecodeLyricPayload 仅可见性提升,纯净回归网):
//   LyricTextProcessor.FormatTimestamp(public static):ms -> "[mm:ss.ff]"(2 位)/"[mm:ss.fff]"(3 位),
//     InvariantCulture,2 位为四舍五入到最近 10ms,分钟不 wrap(可 >60)。
//   LyricTextProcessor.AlignAndSplitTranslatedLyric(public,经 public ctor 解析 lrc):双语行对齐并拆回
//     (原文, 译文)。此处只锁"时间戳完全对齐"主干(每个 attach 分支极微妙,留待后续 extraction 时扩展)。
//   QqMusicTagProvider.DecodeLyricPayload(private static -> internal static):jsonp 回调剥壳 +
//     lyric/trans 的 base64(UTF-8) 解码,字面 "null" 与"纯音乐占位 base64"归一为 ""。
internal static class LyricProcessingCharacterization
{
	// 与 QqMusicTagProvider.EmptyLyricPlaceholderBase64(L37)逐字一致。若产品常量变更导致此串不再被当占位
	// 跳过,本用例应亮红(回归信号:占位识别失效会把提示文案当真歌词写入)。
	private const string EmptyLyricPlaceholderBase64 = "WzAwOjAwOjAwXeatpOatjOabsuS4uuayoeacieWhq+ivjeeahOe6r+mfs+S5kO+8jOivt+aCqOaso+i1jw==";

	private const string QqCallbackName = "MusicJsonCallback34475857153687595";

	private static string Base64Utf8(string text)
	{
		return Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
	}

	public static IEnumerable<(string, Action)> All()
	{
		// ===== FormatTimestamp =====

		yield return ("FormatTimestamp: (0, 2-digit ms) -> [00:00.00]", delegate
		{
			Check.Equal("[00:00.00]", LyricTextProcessor.FormatTimestamp(0L, useThreeDigitMilliseconds: false), "zero 2-digit");
		});

		yield return ("FormatTimestamp: (0, 3-digit ms) -> [00:00.000]", delegate
		{
			Check.Equal("[00:00.000]", LyricTextProcessor.FormatTimestamp(0L, useThreeDigitMilliseconds: true), "zero 3-digit");
		});

		yield return ("FormatTimestamp: (1000, 2-digit) -> [00:01.00]", delegate
		{
			Check.Equal("[00:01.00]", LyricTextProcessor.FormatTimestamp(1000L, useThreeDigitMilliseconds: false), "1s");
		});

		yield return ("FormatTimestamp: (61500, 2-digit) -> [01:01.50] (round to nearest centisecond)", delegate
		{
			Check.Equal("[01:01.50]", LyricTextProcessor.FormatTimestamp(61500L, useThreeDigitMilliseconds: false), "1m1.5s 2-digit");
		});

		yield return ("FormatTimestamp: (61500, 3-digit) -> [01:01.500]", delegate
		{
			Check.Equal("[01:01.500]", LyricTextProcessor.FormatTimestamp(61500L, useThreeDigitMilliseconds: true), "1m1.5s 3-digit");
		});

		yield return ("FormatTimestamp: (3661234, 2-digit) -> minutes do not wrap at 60", delegate
		{
			// ms=234->23, s=3661%60=1, min=3661/60=61 -> "[61:01.23]"
			Check.Equal("[61:01.23]", LyricTextProcessor.FormatTimestamp(3661234L, useThreeDigitMilliseconds: false), "min 61 no wrap");
		});

		// ===== AlignAndSplitTranslatedLyric(时间戳完全对齐主干)=====

		yield return ("AlignAndSplit: no translation -> original kept, translated empty", delegate
		{
			LyricTextProcessor lyric = new LyricTextProcessor("[00:01.00]Hello");
			var (original, translated) = lyric.AlignAndSplitTranslatedLyric(new LyricTextProcessor(""));
			Check.Equal("[00:01.00]Hello", original, "original");
			Check.Equal("", translated, "no translation");
		});

		yield return ("AlignAndSplit: single aligned bilingual line", delegate
		{
			LyricTextProcessor lyric = new LyricTextProcessor("[00:01.00]Hello");
			var (original, translated) = lyric.AlignAndSplitTranslatedLyric(new LyricTextProcessor("[00:01.00]你好"));
			Check.Equal("[00:01.00]Hello", original, "original");
			Check.Equal("[00:01.00]你好", translated, "translated");
		});

		yield return ("AlignAndSplit: two aligned bilingual lines", delegate
		{
			LyricTextProcessor lyric = new LyricTextProcessor("[00:01.00]Hello\n[00:02.00]World");
			var (original, translated) = lyric.AlignAndSplitTranslatedLyric(new LyricTextProcessor("[00:01.00]你好\n[00:02.00]世界"));
			Check.Equal("[00:01.00]Hello\n[00:02.00]World", original, "two original lines");
			Check.Equal("[00:01.00]你好\n[00:02.00]世界", translated, "two translated lines");
		});

		// ===== DecodeLyricPayload =====

		yield return ("DecodeLyricPayload: callback wrapper + base64 lyric & trans -> decoded", delegate
		{
			string body = QqCallbackName + "({\"lyric\":\"" + Base64Utf8("[00:01.00]Hi") + "\",\"trans\":\"" + Base64Utf8("[00:01.00]嗨") + "\"})";
			var (lyric, translation) = QqMusicTagProvider.DecodeLyricPayload(body);
			Check.Equal("[00:01.00]Hi", lyric, "decoded lyric");
			Check.Equal("[00:01.00]嗨", translation, "decoded trans");
		});

		yield return ("DecodeLyricPayload: no callback wrapper -> (\"\",\"\")", delegate
		{
			var (lyric, translation) = QqMusicTagProvider.DecodeLyricPayload("not a jsonp callback");
			Check.Equal("", lyric, "no lyric");
			Check.Equal("", translation, "no trans");
		});

		yield return ("DecodeLyricPayload: literal \"null\" lyric + missing trans -> (\"\",\"\")", delegate
		{
			string body = QqCallbackName + "({\"lyric\":\"null\"})";
			var (lyric, translation) = QqMusicTagProvider.DecodeLyricPayload(body);
			Check.Equal("", lyric, "null lyric normalized");
			Check.Equal("", translation, "missing trans");
		});

		yield return ("DecodeLyricPayload: pure-music placeholder base64 -> lyric not decoded", delegate
		{
			string body = QqCallbackName + "({\"lyric\":\"" + EmptyLyricPlaceholderBase64 + "\"})";
			var (lyric, translation) = QqMusicTagProvider.DecodeLyricPayload(body);
			Check.Equal("", lyric, "placeholder skipped");
			Check.Equal("", translation, "no trans");
		});

		yield return ("DecodeLyricPayload: lyric only (no trans field) -> (decoded, \"\")", delegate
		{
			string body = QqCallbackName + "({\"lyric\":\"" + Base64Utf8("[00:02.00]Solo") + "\"})";
			var (lyric, translation) = QqMusicTagProvider.DecodeLyricPayload(body);
			Check.Equal("[00:02.00]Solo", lyric, "decoded solo lyric");
			Check.Equal("", translation, "no trans");
		});

		// ===== FormatTimestamp 2 位分支:四舍五入到最近 10ms(取代原先 ms/10 丢弃末位)=====
		yield return ("FormatTimestamp: (345, 2-digit) -> [00:00.35] rounds up (was .34 truncated)", delegate
		{
			Check.Equal("[00:00.35]", LyricTextProcessor.FormatTimestamp(345L, useThreeDigitMilliseconds: false), "345ms -> .35 round half up");
		});

		yield return ("FormatTimestamp: (344, 2-digit) -> [00:00.34] rounds down", delegate
		{
			Check.Equal("[00:00.34]", LyricTextProcessor.FormatTimestamp(344L, useThreeDigitMilliseconds: false), "344ms -> .34 round down");
		});

		yield return ("FormatTimestamp: (999, 2-digit) -> [00:01.00] carry into second", delegate
		{
			Check.Equal("[00:01.00]", LyricTextProcessor.FormatTimestamp(999L, useThreeDigitMilliseconds: false), "999ms rounds up to 1.00s");
		});

		yield return ("FormatTimestamp: (345, 3-digit) -> [00:00.345] full precision (no rounding)", delegate
		{
			Check.Equal("[00:00.345]", LyricTextProcessor.FormatTimestamp(345L, useThreeDigitMilliseconds: true), "345ms -> .345 exact");
		});
	}
}
