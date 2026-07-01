using System;
using System.Collections.Generic;
using System.Text;
using MusicTagWinApp.Instances;

namespace MusicTag.Tests;

// 自检：证明测试程序集经 InternalsVisibleTo 可达主程序集的 internal 成员，且断言 / 退出码机制工作。
// 只用纯确定性、locale 无关、可推理的不变式（无录制魔法值；MD5("") 为公认常量）。
// 注意：刻意不断言 FormatFileSize —— 其 ":N" 格式化按 CurrentCulture，非确定性。
internal static class TextUtilitiesTests
{
	public static IEnumerable<(string, Action)> All()
	{
		yield return ("TextUtilities.UnixMillisecondsToDateTime(0) -> 1970-01-01 UTC", delegate
		{
			DateTime epoch = TextUtilities.UnixMillisecondsToDateTime(0L);
			Check.Equal(1970, epoch.Year, "year");
			Check.Equal(1, epoch.Month, "month");
			Check.Equal(1, epoch.Day, "day");
			Check.Equal(DateTimeKind.Utc, epoch.Kind, "kind");
		});
		yield return ("TextUtilities.UrlEncodeUtf8(space) -> %20", delegate
		{
			Check.Equal("%20", TextUtilities.UrlEncodeUtf8(" "), "encoded");
		});
		yield return ("TextUtilities.CoalesceNonBlank blank/value", delegate
		{
			Check.Equal("fb", TextUtilities.CoalesceNonBlank("", "fb"), "coalesce-blank");
			Check.Equal("x", TextUtilities.CoalesceNonBlank("x", "fb"), "coalesce-value");
		});
		yield return ("TextUtilities.ComputeMd5HashString(empty) -> MD5(\"\")", delegate
		{
			Check.Equal("D4-1D-8C-D9-8F-00-B2-04-E9-80-09-98-EC-F8-42-7E", TextUtilities.ComputeMd5HashString(new byte[0]), "md5-empty");
		});

		// GetStringRespectingUtf16Bom:UTF-16 编码 + 前导 BOM 时剥离 2 字节 BOM(LE=FF FE / BE=FE FF);
		// 无 BOM 或 length<2 时走 encoding.GetString 原样解码。
		yield return ("TextUtilities.GetStringRespectingUtf16Bom: strips LE/BE BOM under UTF-16", delegate
		{
			Encoding utf16 = Encoding.GetEncoding("UTF-16");
			Check.Equal("A", utf16.GetStringRespectingUtf16Bom(new byte[] { 0xFF, 0xFE, 0x41, 0x00 }), "LE BOM (FF FE) stripped -> A");
			Check.Equal("A", utf16.GetStringRespectingUtf16Bom(new byte[] { 0xFE, 0xFF, 0x00, 0x41 }), "BE BOM (FE FF) stripped -> A");
			Check.Equal("AB", utf16.GetStringRespectingUtf16Bom(new byte[] { 0x41, 0x00, 0x42, 0x00 }), "no BOM -> decode all (UTF-16LE)");
			Check.Equal("", utf16.GetStringRespectingUtf16Bom(new byte[0]), "length<2 -> GetString(empty)=empty");
		});

		// HeaderName gate:非 "UTF-16"(如 UTF-16BE 的 HeaderName "utf-16BE")不触发剥离,BOM 字符原样保留。
		yield return ("TextUtilities.GetStringRespectingUtf16Bom: non-UTF-16 HeaderName does not strip", delegate
		{
			Check.Equal("﻿A", Encoding.BigEndianUnicode.GetStringRespectingUtf16Bom(new byte[] { 0xFE, 0xFF, 0x00, 0x41 }), "utf-16BE HeaderName -> no strip, BOM char kept");
		});

		// TrimNonEmptyLines:按 '\n' 拆分丢空行、逐行 Trim、行间以 Environment.NewLine 连接、无尾换行;null->null。
		yield return ("TextUtilities.TrimNonEmptyLines: drop-empty + trim + NewLine join", delegate
		{
			Check.Null(TextUtilities.TrimNonEmptyLines(null), "null -> null");
			Check.Equal("", TextUtilities.TrimNonEmptyLines(""), "empty -> empty");
			Check.Equal("a" + Environment.NewLine + "b", TextUtilities.TrimNonEmptyLines("  a  \n\n  b  "), "trim each, blank line dropped, NewLine between");
			Check.Equal("a" + Environment.NewLine + "b", TextUtilities.TrimNonEmptyLines("a\r\nb"), "stray CR removed by Trim");
		});

		// DecodeBase64String:默认 UniCode(UTF-16LE)解码;显式编码名可覆盖;非法 base64 被 catch 吞成 ""。
		yield return ("TextUtilities.DecodeBase64String: UniCode default + UTF-8 override + catch->empty", delegate
		{
			Check.Equal("Hi", TextUtilities.DecodeBase64String(Convert.ToBase64String(Encoding.Unicode.GetBytes("Hi"))), "default UniCode round-trip");
			Check.Equal("中", TextUtilities.DecodeBase64String(Convert.ToBase64String(Encoding.UTF8.GetBytes("中")), "UTF-8"), "explicit UTF-8 round-trip");
			Check.Equal("", TextUtilities.DecodeBase64String("not!base64"), "malformed -> catch -> empty");
		});

		// ContainsChinese:[一-龥] 命中任一即 true;纯 ASCII / 空串 false;全角标点在区外 false。
		yield return ("TextUtilities.ContainsChinese: CJK range predicate", delegate
		{
			Check.True(TextUtilities.ContainsChinese("a中b"), "contains CJK -> true");
			Check.True(!TextUtilities.ContainsChinese("abc"), "ASCII only -> false");
			Check.True(!TextUtilities.ContainsChinese(""), "empty -> false");
			Check.True(!TextUtilities.ContainsChinese("，"), "fullwidth comma (outside range) -> false");
		});
	}
}
