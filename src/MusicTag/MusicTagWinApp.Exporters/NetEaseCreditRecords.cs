using System;
using System.Text;
using System.Text.RegularExpressions;
using MusicTag.Composer;
using Newtonsoft.Json.Linq;

namespace MusicTagWinApp.Exporters;

// 网易云 v1 接口在普通歌词(lrc)开头放作词、作曲、编曲等署名,一行一条 JSON 记录:
//   {"t":0,"c":[{"tx":"作词: "},{"tx":"卫某人","li":"…","or":"…"},{"tx":"/"},{"tx":"刘炫廷",…}]}
// t 是毫秒,各条依次递增、排在第一句歌词之前;把各段 tx 依次拼起来就是客户端显示的文字。
// 这里把它们转成 "[00:00.00]作词: 卫某人/刘炫廷" 这样的普通歌词行(做法同 lx-music);原样留下会把
// JSON 写进标签。时间戳位数跟随这份歌词自己的写法(有三位毫秒就用三位,否则两位),不受
// "格式化时间轴"设置影响 —— 该设置在下载后的统一整理里生效。逐字歌词(yrc)里的同类记录 t 全是 0、
// 与逐字时间轴对不上,由 NetEaseYrcDecoder 丢弃,不经过这里。
internal static class NetEaseCreditRecords
{
	private static readonly Regex LineSplitRegex = new Regex(@"(\r\n|\n|\r)", RegexOptions.Compiled);

	private static readonly Regex TimestampRegex = new Regex(@"\[\d+:\d{2}(?:\.(\d+))?\]", RegexOptions.Compiled);

	internal static string ConvertToLyricLines(string lyricText)
	{
		if (string.IsNullOrEmpty(lyricText) || lyricText.IndexOf('{') < 0)
		{
			return lyricText ?? "";
		}

		// 分隔符一并保留,没被转换的行(含换行符)原样输出。
		string[] parts = LineSplitRegex.Split(lyricText);
		bool? useThreeDigitMilliseconds = DetectTimestampPrecision(lyricText);
		StringBuilder output = new StringBuilder(lyricText.Length);
		bool dropNextSeparator = false;
		bool changed = false;
		for (int i = 0; i < parts.Length; i++)
		{
			string part = parts[i];
			if (i % 2 == 1)
			{
				if (!dropNextSeparator)
				{
					output.Append(part);
				}
				dropNextSeparator = false;
				continue;
			}

			if (!part.TrimStart().StartsWith("{", StringComparison.Ordinal) || !TryConvertRecord(part, useThreeDigitMilliseconds, out string convertedLine))
			{
				output.Append(part);
				continue;
			}

			changed = true;
			if (convertedLine == null)
			{
				// 没有文字的记录整行去掉,连同它后面的换行。
				dropNextSeparator = true;
				continue;
			}
			output.Append(convertedLine);
		}

		return changed ? output.ToString() : lyricText;
	}

	// true = 三位毫秒,false = 两位,null = 歌词里没有时间戳(不带时间轴的歌词,署名也不加时间戳)。
	private static bool? DetectTimestampPrecision(string lyricText)
	{
		bool? precision = null;
		foreach (Match timestampMatch in TimestampRegex.Matches(lyricText))
		{
			if (timestampMatch.Groups[1].Value.Length >= 3)
			{
				return true;
			}
			precision = false;
		}
		return precision;
	}

	// 认得的记录返回 true:convertedLine 为转换后的行,记录里没有文字时为 null(整行去掉)。
	// 不是 {"c":[…]} 形状或解析失败返回 false,调用方原样保留这一行。
	private static bool TryConvertRecord(string line, bool? useThreeDigitMilliseconds, out string convertedLine)
	{
		convertedLine = null;
		JObject record;
		try
		{
			record = JObject.Parse(line);
		}
		catch (Exception)
		{
			return false;
		}
		if (!(record["c"] is JArray segments))
		{
			return false;
		}

		StringBuilder text = new StringBuilder();
		foreach (JToken segment in segments)
		{
			if (segment is JObject segmentObject && segmentObject["tx"] is JValue textValue && textValue.Type == JTokenType.String)
			{
				text.Append((string)textValue);
			}
		}
		string creditText = text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').ToString().Trim();
		if (creditText.Length == 0)
		{
			return true;
		}

		convertedLine = (useThreeDigitMilliseconds.HasValue && TryGetStartMilliseconds(record["t"], out long startMilliseconds))
			? LyricTextProcessor.FormatTimestamp(startMilliseconds, useThreeDigitMilliseconds.Value) + creditText
			: creditText;
		return true;
	}

	private static bool TryGetStartMilliseconds(JToken startToken, out long startMilliseconds)
	{
		startMilliseconds = 0L;
		if (!(startToken is JValue startValue))
		{
			return false;
		}
		try
		{
			switch (startValue.Type)
			{
			case JTokenType.Integer:
				startMilliseconds = Convert.ToInt64(startValue.Value);
				break;
			case JTokenType.Float:
				startMilliseconds = (long)Math.Round(Convert.ToDouble(startValue.Value), MidpointRounding.AwayFromZero);
				break;
			default:
				return false;
			}
		}
		catch (Exception ex) when (ex is OverflowException || ex is InvalidCastException)
		{
			// 超出 long 的整数(Json.NET 存为 BigInteger)等无法表示的时间。
			return false;
		}
		return startMilliseconds >= 0L;
	}
}
