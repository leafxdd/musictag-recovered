using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using MusicTag.Candidates;
using MusicTagWinApp.Exporters;
using MusicTagWinApp.Instances;
using MusicTagWinApp.Writers;

namespace MusicTag.Tests;

// 4 provider 的边角解码器 characterization(SearchTracks/SearchLyrics/SearchCovers 主路径已覆盖,这些
// private helper 的边界分支零覆盖)。均 visibility-lift(private->internal,含两处 加 static),逐字节不变:
//   NetEase.FormatPublishYear:epoch ms(>0)-> "yyyy"(UTC/InvariantCulture);null/<=0/溢出 -> null。
//   NetEase.ParseCoverDocId:正则 /(\d+)\.\w+$ 提 albumPicDocId;无数字尾段/无扩展名 -> 0。
//   NetEase.ExtractLyricTexts:lrc.lyric / tlyric.lyric,字面 "null" 与缺失归一为 ""。
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
