using System;
using System.Collections.Generic;
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
	}
}
