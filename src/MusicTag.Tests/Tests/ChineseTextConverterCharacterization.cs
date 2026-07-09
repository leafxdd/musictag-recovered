using System;
using System.Collections.Generic;
using MusicTagWinApp.Structs;

namespace MusicTag.Tests;

// ChineseTextConverter:简繁转换字库(tsmap/tcmap 内嵌资源)经 BinaryFormatter 反序列化——
// net8 需 EnableUnsafeBinaryFormatterSerialization(主工程与本测试工程各开一份),net9 起
// 该 API 整体移除。此组用例锁住字库加载路径与逐字/词组转换行为,作为将来换序列化格式
// (或升 net9)时的回归网。
internal static class ChineseTextConverterCharacterization
{
	public static IEnumerable<(string, Action)> All()
	{
		yield return ("ChineseTextConverter: T2S singleton loads (BinaryFormatter tsmap) + char conversion", delegate
		{
			ChineseTextConverter converter = ChineseTextConverter.TraditionalToSimplified();
			Check.True(converter != null, "T2S singleton constructed (tsmap deserialized)");
			Check.Equal('乐', converter.ConvertCharacter('樂'), "樂 -> 乐 (single char map)");
			Check.Equal('a', converter.ConvertCharacter('a'), "non-CJK char passes through");
		});

		yield return ("ChineseTextConverter: S2T singleton loads (BinaryFormatter tcmap) + text conversion", delegate
		{
			ChineseTextConverter converter = ChineseTextConverter.SimplifiedToTraditional();
			Check.True(converter != null, "S2T singleton constructed (tcmap deserialized)");
			Check.Equal("音樂標籤", converter.ConvertText("音乐标签"), "音乐标签 -> 音樂標籤 (matches zh-CHT app title)");
		});

		yield return ("ChineseTextConverter: T2S ConvertText mixed CJK/ASCII passthrough", delegate
		{
			ChineseTextConverter converter = ChineseTextConverter.TraditionalToSimplified();
			Check.Equal("音乐标签 Music Tag 123", converter.ConvertText("音樂標籤 Music Tag 123"), "mixed content: CJK converted, ASCII untouched");
		});
	}
}
