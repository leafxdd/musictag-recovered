using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using MusicTag.Candidates;
using MusicTagWinApp.Exporters;
using MusicTagWinApp.Instances;
using MusicTagWinApp.Properties;
using MusicTagWinApp.Writers;

namespace MusicTag.Tests;

// 4 provider 的边角解码器 characterization(SearchTracks/SearchLyrics/SearchCovers 主路径已覆盖,这些
// private helper 的边界分支零覆盖)。均 visibility-lift(private->internal,含两处 加 static),逐字节不变:
//   NetEase.FormatPublishYear:epoch ms(>0)-> "yyyy"(UTC/InvariantCulture);null/<=0/溢出 -> null。
//   NetEase.ParseCoverDocId:正则 /(\d+)\.\w+$ 提 albumPicDocId;无数字尾段/无扩展名 -> 0。
//   NetEase.ExtractLyricTexts:lrc/tlyric 回退、YRC/ytlrc 优先、部分 YTLRC 丢孤立行、字面 "null" 与缺失归一为 ""。
//   QQ QRC conversion:valid line timestamps stay authoritative; malformed line timestamps fall back to the first valid word timestamp.
//   QqSongInfo.GetGenreName(已 public):genre id -> 英文流派名 switch,未知/null -> ""。
//   QQ.IsRateLimited:req_0.code 必须是 Integer 且 ==2001(字符串 "2001" -> false);空导航 -> false。
//   Kugou.BuildEncodedLyricKeyword:artist&title 非空 -> "title - artist";title 空 -> artist;else -> title(先 Trim 再 UrlEncode)。
internal static class ProviderDecodersCharacterization
{
	public static IEnumerable<(string, Action)> All()
	{
		// ===== NetEase.FormatPublishYear =====

		yield return ("FormatPublishYear: 1262304000000 (2010-01-01 UTC) -> \"2010\"", delegate
		{
			Check.Equal("2010", NetEaseMusicTagProvider.FormatPublishYear(1262304000000L), "epoch ms -> year");
		});

		yield return ("FormatPublishYear: 0 -> null (>0 guard)", delegate
		{
			Check.Null(NetEaseMusicTagProvider.FormatPublishYear(0L), "zero -> null");
		});

		yield return ("FormatPublishYear: -1 -> null", delegate
		{
			Check.Null(NetEaseMusicTagProvider.FormatPublishYear(-1L), "negative -> null");
		});

		yield return ("FormatPublishYear: null -> null", delegate
		{
			Check.Null(NetEaseMusicTagProvider.FormatPublishYear(null), "null -> null");
		});

		yield return ("FormatPublishYear: long.MaxValue -> null (AddMilliseconds overflow caught)", delegate
		{
			Check.Null(NetEaseMusicTagProvider.FormatPublishYear(long.MaxValue), "overflow -> null");
		});

		// ===== NetEase.ParseCoverDocId =====

		yield return ("ParseCoverDocId: \"http://p/109951165.jpg\" -> 109951165", delegate
		{
			Check.Equal(109951165L, NetEaseMusicTagProvider.ParseCoverDocId("http://p/109951165.jpg"), "digit tail extracted");
		});

		yield return ("ParseCoverDocId: non-digit tail -> 0", delegate
		{
			Check.Equal(0L, NetEaseMusicTagProvider.ParseCoverDocId("http://p/abc.jpg"), "non-digit -> 0");
		});

		yield return ("ParseCoverDocId: no extension -> 0", delegate
		{
			Check.Equal(0L, NetEaseMusicTagProvider.ParseCoverDocId("http://p/109951165"), "no extension -> 0");
		});

		yield return ("ParseCoverDocId: empty -> 0", delegate
		{
			Check.Equal(0L, NetEaseMusicTagProvider.ParseCoverDocId(""), "empty -> 0");
		});

		// ===== NetEase.ExtractLyricTexts =====

		yield return ("ExtractLyricTexts: lrc + tlyric present", delegate
		{
			var (lyric, translated) = NetEaseMusicTagProvider.ExtractLyricTexts("{\"lrc\":{\"lyric\":\"[00:01]A\"},\"tlyric\":{\"lyric\":\"[00:01]B\"}}");
			Check.Equal("[00:01]A", lyric, "lyric");
			Check.Equal("[00:01]B", translated, "translated");
		});

		yield return ("ExtractLyricTexts: literal \"null\" lyric -> \"\"", delegate
		{
			var (lyric, translated) = NetEaseMusicTagProvider.ExtractLyricTexts("{\"lrc\":{\"lyric\":\"null\"}}");
			Check.Equal("", lyric, "literal null lyric normalized");
			Check.Equal("", translated, "missing tlyric -> empty");
		});

		yield return ("ExtractLyricTexts: empty object -> (\"\",\"\")", delegate
		{
			var (lyric, translated) = NetEaseMusicTagProvider.ExtractLyricTexts("{}");
			Check.Equal("", lyric, "missing lrc -> empty");
			Check.Equal("", translated, "missing tlyric -> empty");
		});

		yield return ("ExtractLyricTexts: literal \"null\" tlyric -> \"\"", delegate
		{
			var (lyric, translated) = NetEaseMusicTagProvider.ExtractLyricTexts("{\"lrc\":{\"lyric\":\"x\"},\"tlyric\":{\"lyric\":\"null\"}}");
			Check.Equal("x", lyric, "lyric x");
			Check.Equal("", translated, "literal null translated normalized");
		});

		yield return ("NetEaseYrcDecoder: strips three-part word markers and keeps millisecond line starts", delegate
		{
			string yrc = "[12867,6282](12867,208,0)岁(13076,208,0)月\n[19878,1000](19878,500,0)下";
			Check.Equal("[00:12.867]岁月\n[00:19.878]下", NetEaseYrcDecoder.ConvertToLineLyric(yrc, useThreeDigitMilliseconds: true), "YRC conversion");
		});

		yield return ("NetEaseYrcDecoder: ignores v1 JSON credit metadata", delegate
		{
			string yrc = "{\"t\":0,\"c\":[{\"tx\":\"作词: \"},{\"tx\":\"作者\"}]}\n{\"t\":0,\"c\":[{\"tx\":\"作曲: \"},{\"tx\":\"作曲者\"}]}\n[1000,500](1000,200,0)A";
			Check.Equal("[00:01.000]A", NetEaseYrcDecoder.ConvertToLineLyric(yrc, useThreeDigitMilliseconds: true), "JSON credit metadata omitted");
		});

		yield return ("ExtractLyricTexts: JSON credit metadata cannot pollute YTLRC alignment", delegate
		{
			bool previousSetting = Settings.Default.LyricDownload_ReformatTimetag;
			try
			{
				Settings.Default.LyricDownload_ReformatTimetag = false;
				string yrc = "{\"t\":0,\"c\":[{\"tx\":\"作词: \"},{\"tx\":\"作者\"}]}\n{\"t\":0,\"c\":[{\"tx\":\"作曲: \"},{\"tx\":\"作曲者\"}]}\n[1007,1000](1007,500,0)原";
				string response = "{\"lrc\":{\"lyric\":\"[00:01.00]普通\"},\"tlyric\":{\"lyric\":\"[00:01.00]旧译\"},\"yrc\":{\"lyric\":\"" + yrc.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"},\"ytlrc\":{\"lyric\":\"[00:01.00]逐译\"}}";
				var (lyric, translated) = NetEaseMusicTagProvider.ExtractLyricTexts(response);
				Check.Equal("[00:01.007]原", lyric, "YRC lyric without credit metadata");
				Check.Equal("[00:01.007]逐译", translated, "YTLRC alignment without credit metadata");
			}
			finally
			{
				Settings.Default.LyricDownload_ReformatTimetag = previousSetting;
			}
		});

		yield return ("ExtractLyricTexts: metadata-only YRC falls back to ordinary LRC", delegate
		{
			string response = "{\"lrc\":{\"lyric\":\"[00:01.00]普通\"},\"tlyric\":{\"lyric\":\"[00:01.00]译\"},\"yrc\":{\"lyric\":\"{\\\"t\\\":0,\\\"c\\\":[{\\\"tx\\\":\\\"作词: 作者\\\"}]}\"}}";
			var (lyric, translated) = NetEaseMusicTagProvider.ExtractLyricTexts(response);
			Check.Equal("[00:01.00]普通", lyric, "ordinary lyric fallback");
			Check.Equal("[00:01.00]译", translated, "ordinary translation fallback");
		});

		yield return ("ExtractLyricTexts: YRC + ytlrc take precedence over ordinary lrc + tlyric", delegate
		{
			bool previousSetting = Settings.Default.LyricDownload_ReformatTimetag;
			try
			{
				Settings.Default.LyricDownload_ReformatTimetag = false;
				string response = "{\"lrc\":{\"lyric\":\"[00:12.720]普通\"},\"tlyric\":{\"lyric\":\"[00:12.500]旧译\"},\"yrc\":{\"lyric\":\"[12867,1000](12867,500,0)原\"},\"ytlrc\":{\"lyric\":\"[00:12.867]逐译\"}}";
				var (lyric, translated) = NetEaseMusicTagProvider.ExtractLyricTexts(response);
				Check.Equal("[00:12.867]原", lyric, "YRC original");
				Check.Equal("[00:12.867]逐译", translated, "YTLRC translation");
			}
			finally
			{
				Settings.Default.LyricDownload_ReformatTimetag = previousSetting;
			}
		});

		yield return ("ExtractLyricTexts: partially aligned ytlrc drops only orphan lines", delegate
		{
			bool previousSetting = Settings.Default.LyricDownload_ReformatTimetag;
			try
			{
				Settings.Default.LyricDownload_ReformatTimetag = false;
				string response = "{\"lrc\":{\"lyric\":\"[00:01.00]普通一\\n[00:02.00]普通二\"},\"tlyric\":{\"lyric\":\"[00:01.00]旧译一\\n[00:02.00]旧译二\"},\"yrc\":{\"lyric\":\"[1007,900](1007,400,0)原一\\n[2003,900](2003,400,0)原二\"},\"ytlrc\":{\"lyric\":\"[00:01.00]逐译一\\n[00:02.00]逐译二\\n[00:05.00]尾注\"}}";
				var (lyric, translated) = NetEaseMusicTagProvider.ExtractLyricTexts(response);
				Check.Equal("[00:01.007]原一\n[00:02.003]原二", lyric, "YRC original without orphan blank line");
				Check.Equal("[00:01.007]逐译一\n[00:02.003]逐译二", translated, "matched YTLRC lines");
			}
			finally
			{
				Settings.Default.LyricDownload_ReformatTimetag = previousSetting;
			}
		});

		yield return ("ExtractLyricTexts: missing ytlrc aligns nearby tlyric to YRC", delegate
		{
			bool previousSetting = Settings.Default.LyricDownload_ReformatTimetag;
			try
			{
				Settings.Default.LyricDownload_ReformatTimetag = false;
				string response = "{\"lrc\":{\"lyric\":\"[00:12.720]普通\"},\"tlyric\":{\"lyric\":\"[00:12.86]旧译\"},\"yrc\":{\"lyric\":\"[12867,1000](12867,500,0)原\"}}";
				var (lyric, translated) = NetEaseMusicTagProvider.ExtractLyricTexts(response);
				Check.Equal("[00:12.867]原", lyric, "YRC original");
				Check.Equal("[00:12.867]旧译", translated, "aligned tlyric");
			}
			finally
			{
				Settings.Default.LyricDownload_ReformatTimetag = previousSetting;
			}
		});

		yield return ("ExtractLyricTexts: misaligned ytlrc falls through to alignable tlyric", delegate
		{
			bool previousSetting = Settings.Default.LyricDownload_ReformatTimetag;
			try
			{
				Settings.Default.LyricDownload_ReformatTimetag = false;
				string response = "{\"lrc\":{\"lyric\":\"[00:01.00]普通\"},\"tlyric\":{\"lyric\":\"[00:01.00]旧译\"},\"yrc\":{\"lyric\":\"[1007,1000](1007,500,0)原\"},\"ytlrc\":{\"lyric\":\"[00:03.00]孤立逐译\"}}";
				var (lyric, translated) = NetEaseMusicTagProvider.ExtractLyricTexts(response);
				Check.Equal("[00:01.007]原", lyric, "YRC original");
				Check.Equal("[00:01.007]旧译", translated, "aligned tlyric fallback");
			}
			finally
			{
				Settings.Default.LyricDownload_ReformatTimetag = previousSetting;
			}
		});

		yield return ("ExtractLyricTexts: orphan tlyric falls back to the legacy lrc pair", delegate
		{
			bool previousSetting = Settings.Default.LyricDownload_ReformatTimetag;
			try
			{
				Settings.Default.LyricDownload_ReformatTimetag = false;
				string response = "{\"lrc\":{\"lyric\":\"[00:01.00]普通\"},\"tlyric\":{\"lyric\":\"[00:03.00]孤立\"},\"yrc\":{\"lyric\":\"[1000,1000](1000,500,0)原\"}}";
				var (lyric, translated) = NetEaseMusicTagProvider.ExtractLyricTexts(response);
				Check.Equal("[00:01.00]普通", lyric, "legacy lrc fallback");
				Check.Equal("[00:03.00]孤立", translated, "legacy tlyric fallback");
			}
			finally
			{
				Settings.Default.LyricDownload_ReformatTimetag = previousSetting;
			}
		});

		yield return ("ExtractLyricTexts: malformed or absent YRC falls back to lrc + tlyric", delegate
		{
			string response = "{\"lrc\":{\"lyric\":\"[00:01.00]普通\"},\"tlyric\":{\"lyric\":\"[00:01.00]译\"},\"yrc\":{\"lyric\":\"not-yrc\"}}";
			var (lyric, translated) = NetEaseMusicTagProvider.ExtractLyricTexts(response);
			Check.Equal("[00:01.00]普通", lyric, "lrc fallback");
			Check.Equal("[00:01.00]译", translated, "tlyric fallback");
		});

		yield return ("ExtractLyricTexts: YRC follows the two-digit reformat setting", delegate
		{
			bool previousSetting = Settings.Default.LyricDownload_ReformatTimetag;
			try
			{
				Settings.Default.LyricDownload_ReformatTimetag = true;
				var (lyric, translated) = NetEaseMusicTagProvider.ExtractLyricTexts("{\"yrc\":{\"lyric\":\"[12867,1000](12867,500,0)原\"}}");
				Check.Equal("[00:12.87]原", lyric, "rounded YRC");
				Check.Equal("", translated, "no translation");
			}
			finally
			{
				Settings.Default.LyricDownload_ReformatTimetag = previousSetting;
			}
		});

		// ===== QQ QRC conversion =====

		yield return ("QqQrcDecoder: valid line timestamp stays authoritative", delegate
		{
			string qrc = "[1000,500]A(1007,500)";
			Check.Equal("[00:01.000]A", QqQrcDecoder.ConvertToLineLyric(qrc, useThreeDigitMilliseconds: true), "valid line timestamp wins");
		});

		yield return ("QqQrcDecoder: invalid line timestamp falls back to first valid word timestamp", delegate
		{
			string qrc = "[99999999999999999999,500]A(99999999999999999999,100)B(1007,400)\n[2000,500]C(2000,500)";
			Check.Equal("[00:01.007]AB\n[00:02.000]C", QqQrcDecoder.ConvertToLineLyric(qrc, useThreeDigitMilliseconds: true), "word timestamp fallback");
		});

		yield return ("QqQrcDecoder: inline metadata cannot attach to the preceding lyric", delegate
		{
			string qrc = "[1000,500]A(1000,500)\n[kana:fixture]\n[2000,500]B(2000,500)";
			Check.Equal("[00:01.000]A\n[00:02.000]B", QqQrcDecoder.ConvertToLineLyric(qrc, useThreeDigitMilliseconds: true), "inline metadata omitted");
		});

		yield return ("QqQrcDecoder: XML LyricContent keeps lines separated across embedded newlines", delegate
		{
			string qrcXml = "<?xml version=\"1.0\"?><QrcInfos><LyricInfo LyricContent=\"[1000,500]A(1000,500)&#10;[2000,500]B(2000,500)\" /></QrcInfos>";
			Check.Equal("[00:01.000]A\n[00:02.000]B", QqQrcDecoder.ConvertToLineLyric(qrcXml, useThreeDigitMilliseconds: true), "XML attribute newline");
		});

		// ===== QqSongInfo.GetGenreName =====

		yield return ("GetGenreName: 1 -> Pop", delegate
		{
			Check.Equal("Pop", new QqSongInfo { GenreId = 1 }.GetGenreName(), "1=Pop");
		});

		yield return ("GetGenreName: 36 -> Rock", delegate
		{
			Check.Equal("Rock", new QqSongInfo { GenreId = 36 }.GetGenreName(), "36=Rock");
		});

		yield return ("GetGenreName: 33 -> R&B", delegate
		{
			Check.Equal("R&B", new QqSongInfo { GenreId = 33 }.GetGenreName(), "33=R&B");
		});

		yield return ("GetGenreName: unknown 999 -> \"\"", delegate
		{
			Check.Equal("", new QqSongInfo { GenreId = 999 }.GetGenreName(), "unknown default");
		});

		yield return ("GetGenreName: null GenreId -> \"\"", delegate
		{
			Check.Equal("", new QqSongInfo().GetGenreName(), "no GenreId -> empty");
		});

		// ===== QQ.IsRateLimited =====

		yield return ("IsRateLimited: code 2001 (Integer) -> true", delegate
		{
			Check.True(QqMusicTagProvider.IsRateLimited(JObject.Parse("{\"req_0\":{\"code\":2001}}")), "2001 int");
		});

		yield return ("IsRateLimited: code 0 -> false", delegate
		{
			Check.True(!QqMusicTagProvider.IsRateLimited(JObject.Parse("{\"req_0\":{\"code\":0}}")), "0");
		});

		yield return ("IsRateLimited: code string \"2001\" -> false (type guard)", delegate
		{
			Check.True(!QqMusicTagProvider.IsRateLimited(JObject.Parse("{\"req_0\":{\"code\":\"2001\"}}")), "string 2001 not Integer");
		});

		yield return ("IsRateLimited: missing req_0 -> false", delegate
		{
			Check.True(!QqMusicTagProvider.IsRateLimited(JObject.Parse("{}")), "missing");
		});

		yield return ("IsRateLimited: null -> false", delegate
		{
			Check.True(!QqMusicTagProvider.IsRateLimited(null), "null guard");
		});

		// ===== Kugou.BuildEncodedLyricKeyword(用 UrlEncodeUtf8 生成期望,锁分支选择/拼接顺序/Trim)=====

		yield return ("BuildEncodedLyricKeyword: both -> \"title - artist\"", delegate
		{
			Check.Equal(TextUtilities.UrlEncodeUtf8("晴天 - 周杰伦"), KugouTagProvider.BuildEncodedLyricKeyword("周杰伦", "晴天"), "title - artist");
		});

		yield return ("BuildEncodedLyricKeyword: empty title -> artist", delegate
		{
			Check.Equal(TextUtilities.UrlEncodeUtf8("周杰伦"), KugouTagProvider.BuildEncodedLyricKeyword("周杰伦", ""), "title empty -> artist");
		});

		yield return ("BuildEncodedLyricKeyword: empty artist -> title", delegate
		{
			Check.Equal(TextUtilities.UrlEncodeUtf8("晴天"), KugouTagProvider.BuildEncodedLyricKeyword("", "晴天"), "artist empty -> title");
		});

		yield return ("BuildEncodedLyricKeyword: both trimmed", delegate
		{
			Check.Equal(TextUtilities.UrlEncodeUtf8("b - a"), KugouTagProvider.BuildEncodedLyricKeyword(" a ", " b "), "trim each part");
		});
	}
}
