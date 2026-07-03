using System;
using System.Collections.Generic;
using MusicTag.Serialization;

namespace MusicTag.Tests;

// MusicTag.Serialization 编码 / ASCII 分类纯逻辑 characterization(均 already-testable public static,零产品改动):
//   AsciiTokenClassifier.IsAsciiLetterLike / IsAsciiDigitLike:半角 + 全角(U+FF21.. / U+FF10..)字母/数字的 char 区间判定。
//   TagTextEncoding.NormalizeEncodingName:5 个显式别名 case(UTF8/UTF16BE/UTF16LE/UTF16/Latin1);
//     **刻意不测 default 分支**——它读 Encoding.Default.HeaderName(机器 ANSI 代码页,locale/OS 相关,非确定)。
//   TagTextEncoding.TranscodeText:src.GetBytes -> tgt.GetStringRespectingUtf16Bom;
//     **限同/跨编码的 ASCII/Latin1 round-trip**(单字节确定),避 UTF-16 跨编码的 BOM/null 字节 quirk。
// 断言全 locale 无关、确定性。
internal static class SerializationEncodingCharacterization
{
	public static IEnumerable<(string, Action)> All()
	{
		// ===== AsciiTokenClassifier.IsAsciiLetterLike:ASCII + 全角字母 =====
		yield return ("AsciiTokenClassifier.IsAsciiLetterLike: ASCII + fullwidth letters", delegate
		{
			Check.True(AsciiTokenClassifier.IsAsciiLetterLike('a'), "ascii lower a");
			Check.True(AsciiTokenClassifier.IsAsciiLetterLike('Z'), "ascii upper Z");
			Check.True(AsciiTokenClassifier.IsAsciiLetterLike('Ａ'), "fullwidth upper A (U+FF21)");
			Check.True(AsciiTokenClassifier.IsAsciiLetterLike('ａ'), "fullwidth lower a (U+FF41)");
			Check.True(!AsciiTokenClassifier.IsAsciiLetterLike('5'), "digit -> false");
			Check.True(!AsciiTokenClassifier.IsAsciiLetterLike(' '), "space -> false");
			Check.True(!AsciiTokenClassifier.IsAsciiLetterLike('中'), "CJK -> false");
		});

		// ===== AsciiTokenClassifier.IsAsciiDigitLike:ASCII + 全角数字 =====
		yield return ("AsciiTokenClassifier.IsAsciiDigitLike: ASCII + fullwidth digits", delegate
		{
			Check.True(AsciiTokenClassifier.IsAsciiDigitLike('0'), "ascii 0");
			Check.True(AsciiTokenClassifier.IsAsciiDigitLike('9'), "ascii 9");
			Check.True(AsciiTokenClassifier.IsAsciiDigitLike('５'), "fullwidth 5 (U+FF15)");
			Check.True(AsciiTokenClassifier.IsAsciiDigitLike('０'), "fullwidth 0 (U+FF10)");
			Check.True(!AsciiTokenClassifier.IsAsciiDigitLike('a'), "letter -> false");
			Check.True(!AsciiTokenClassifier.IsAsciiDigitLike(' '), "space -> false");
		});

		// ===== TagTextEncoding.NormalizeEncodingName:5 显式别名(不测 default,读 Encoding.Default machine ANSI)=====
		yield return ("TagTextEncoding.NormalizeEncodingName: 5 explicit encoding aliases", delegate
		{
			Check.Equal("UTF-8", TagTextEncoding.NormalizeEncodingName("UTF8"), "UTF8");
			Check.Equal("UTF-16BE", TagTextEncoding.NormalizeEncodingName("UTF16BE"), "UTF16BE");
			Check.Equal("UTF-16LE", TagTextEncoding.NormalizeEncodingName("UTF16LE"), "UTF16LE");
			Check.Equal("UTF-16", TagTextEncoding.NormalizeEncodingName("UTF16"), "UTF16");
			Check.Equal("ISO-8859-1", TagTextEncoding.NormalizeEncodingName("Latin1"), "Latin1");
		});

		// ===== TagTextEncoding.TranscodeText:同/跨编码 round-trip(限 ASCII/Latin1,避 UTF-16 BOM quirk)=====
		yield return ("TagTextEncoding.TranscodeText: ASCII/Latin1 round-trips", delegate
		{
			Check.Equal("Hello", TagTextEncoding.TranscodeText("Hello", "UTF-8", "UTF-8"), "UTF-8 identity");
			Check.Equal("ABC123", TagTextEncoding.TranscodeText("ABC123", "ISO-8859-1", "ISO-8859-1"), "Latin1 identity");
			Check.Equal("é", TagTextEncoding.TranscodeText("é", "ISO-8859-1", "ISO-8859-1"), "Latin1 single-byte 0xE9 round-trip");
			Check.Equal("abc", TagTextEncoding.TranscodeText("abc", "ISO-8859-1", "UTF-8"), "ASCII bytes identical Latin1->UTF-8");
		});
	}
}
