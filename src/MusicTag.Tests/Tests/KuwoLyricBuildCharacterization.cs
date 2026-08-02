using System;
using System.Collections.Generic;
using MusicTagWinApp.Adapter;

namespace MusicTag.Tests;

// 酷我详情歌词构建 characterization(KuwoTagProvider.PopulateSongDetails,private void -> internal void,body 逐字不变)。
// PopulateSongDetails 纯解析传入 detailsJson(无网络),写回 song.CoverUrl / LargeCoverUrl / LoadedLyric。
// 覆盖单语、重复时间戳 alternate、中英文重排、日文 ContainsChinese 双态、时间戳借用、尾部特判和封面解析。
// Lyric 末尾保留 "\n"(源不 Trim)。这些输出是 legacy songinfoandlrc 的兼容基线,不代表 LRCX 协议语义。
internal static class KuwoLyricBuildCharacterization
{
	public static IEnumerable<(string, Action)> All()
	{
		yield return ("PopulateSongDetails: two mono-lingual lines -> lyric double line, no translation", delegate
		{
			KuwoSongInfo song = new KuwoSongInfo { Title = "T", Artist = "A", Album = "Al", OriginalTitle = "O" };
			new KuwoTagProvider().PopulateSongDetails(song, "{\"data\":{\"lrclist\":[{\"time\":\"1\",\"lineLyric\":\"Hello\"},{\"time\":\"2\",\"lineLyric\":\"World\"}],\"songinfo\":{\"pic\":\"http://x.jpg\"}}}");
			Check.NotNull(song.LoadedLyric, "lyric produced");
			Check.Equal("[00:01.00]Hello\n[00:02.00]World\n", song.LoadedLyric.Lyric, "two lines");
			Check.Equal("", song.LoadedLyric.TranslatedLyric, "no translation");
			Check.Equal("http://x.jpg", song.CoverUrl, "cover url");
		});

		yield return ("PopulateSongDetails: single line with fractional second -> [00:01.50]", delegate
		{
			KuwoSongInfo song = new KuwoSongInfo { Title = "T" };
			new KuwoTagProvider().PopulateSongDetails(song, "{\"data\":{\"lrclist\":[{\"time\":\"1.5\",\"lineLyric\":\"Solo\"}],\"songinfo\":{\"pic\":\"\"}}}");
			Check.NotNull(song.LoadedLyric, "lyric produced");
			Check.Equal("[00:01.50]Solo\n", song.LoadedLyric.Lyric, "1.5s -> [00:01.50]");
			Check.Equal("", song.CoverUrl, "empty cover");
		});

		yield return ("PopulateSongDetails: two lines same timestamp -> merged with space (Count==1 branch)", delegate
		{
			KuwoSongInfo song = new KuwoSongInfo();
			new KuwoTagProvider().PopulateSongDetails(song, "{\"data\":{\"lrclist\":[{\"time\":\"1\",\"lineLyric\":\"A\"},{\"time\":\"1\",\"lineLyric\":\"你好\"}],\"songinfo\":{\"pic\":\"\"}}}");
			Check.NotNull(song.LoadedLyric, "lyric produced");
			Check.Equal("[00:01.00]A 你好\n", song.LoadedLyric.Lyric, "same-ts merged with space");
		});

		yield return ("PopulateSongDetails: empty lrclist -> no LoadedLyric, cover still set", delegate
		{
			KuwoSongInfo song = new KuwoSongInfo();
			new KuwoTagProvider().PopulateSongDetails(song, "{\"data\":{\"lrclist\":[],\"songinfo\":{\"pic\":\"http://y.jpg\"}}}");
			Check.Null(song.LoadedLyric, "no lyric");
			Check.Equal("http://y.jpg", song.CoverUrl, "cover still set");
		});

		yield return ("PopulateSongDetails: 3 lines same tail ts (multi-ts) -> alternates split to +5s line (tail-split branch)", delegate
		{
			KuwoSongInfo song = new KuwoSongInfo();
			new KuwoTagProvider().PopulateSongDetails(song, "{\"data\":{\"lrclist\":[{\"time\":\"1\",\"lineLyric\":\"L1\"},{\"time\":\"2\",\"lineLyric\":\"A\"},{\"time\":\"2\",\"lineLyric\":\"B\"},{\"time\":\"2\",\"lineLyric\":\"C\"}],\"songinfo\":{\"pic\":\"\"}}}");
			Check.NotNull(song.LoadedLyric, "lyric produced");
			Check.Equal("[00:01.00]L1\n[00:02.00]B\n", song.LoadedLyric.Lyric, "tail alternates: B stays at 2s primary track");
			Check.Equal("[00:01.00]A\n[00:02.00]C\n", song.LoadedLyric.TranslatedLyric, "A/C flow to translated track (split C rejoins alignment)");
		});

		yield return ("PopulateSongDetails: middle duplicate timestamps preserve current English/Chinese split", delegate
		{
			KuwoSongInfo song = new KuwoSongInfo();
			new KuwoTagProvider().PopulateSongDetails(song, "{\"data\":{\"lrclist\":[{\"time\":\"1\",\"lineLyric\":\"Intro\"},{\"time\":\"2\",\"lineLyric\":\"Hello\"},{\"time\":\"2\",\"lineLyric\":\"你好\"},{\"time\":\"3\",\"lineLyric\":\"World\"},{\"time\":\"3\",\"lineLyric\":\"世界\"}],\"songinfo\":{\"pic\":\"\"}}}");
			Check.NotNull(song.LoadedLyric, "lyric produced");
			Check.Equal("[00:01.00]Intro\n[00:02.00]Hello\n[00:03.00]World\n", song.LoadedLyric.Lyric, "English primary track");
			Check.Equal("[00:01.00]你好\n[00:02.00]世界\n", song.LoadedLyric.TranslatedLyric, "Chinese alternate track borrows previous timestamps");
		});

		yield return ("PopulateSongDetails: Japanese kana and kanji take different legacy ContainsChinese branches", delegate
		{
			KuwoSongInfo song = new KuwoSongInfo();
			new KuwoTagProvider().PopulateSongDetails(song, "{\"data\":{\"lrclist\":[{\"time\":\"1\",\"lineLyric\":\"Intro\"},{\"time\":\"2\",\"lineLyric\":\"どれほどよかったでしょう\"},{\"time\":\"2\",\"lineLyric\":\"如果只是一场梦\"},{\"time\":\"3\",\"lineLyric\":\"未だにあなたのことを夢にみる\"},{\"time\":\"3\",\"lineLyric\":\"至今仍会梦见你\"}],\"songinfo\":{\"pic\":\"\"}}}");
			Check.NotNull(song.LoadedLyric, "lyric produced");
			Check.Equal("[00:01.00]Intro\n[00:02.00]どれほどよかったでしょう\n[00:03.00]至今仍会梦见你\n", song.LoadedLyric.Lyric, "kana swaps while kanji remains in legacy primary slot");
			Check.Equal("[00:01.00]如果只是一场梦\n[00:02.00]未だにあなたのことを夢にみる\n", song.LoadedLyric.TranslatedLyric, "legacy language heuristic baseline");
		});

		yield return ("PopulateSongDetails: untranslated gap borrows next timestamp after translation starts", delegate
		{
			KuwoSongInfo song = new KuwoSongInfo();
			new KuwoTagProvider().PopulateSongDetails(song, "{\"data\":{\"lrclist\":[{\"time\":\"1\",\"lineLyric\":\"Intro\"},{\"time\":\"2\",\"lineLyric\":\"Hello\"},{\"time\":\"2\",\"lineLyric\":\"你好\"},{\"time\":\"3\",\"lineLyric\":\"Middle\"},{\"time\":\"4\",\"lineLyric\":\"End\"}],\"songinfo\":{\"pic\":\"\"}}}");
			Check.NotNull(song.LoadedLyric, "lyric produced");
			Check.Equal("[00:01.00]Intro\n[00:02.00]Hello\n[00:04.00]End\n", song.LoadedLyric.Lyric, "next line becomes primary at its own timestamp");
			Check.Equal("[00:01.00]你好\n[00:02.00]Middle\n", song.LoadedLyric.TranslatedLyric, "untranslated middle line follows current borrowing heuristic");
		});

		yield return ("PopulateSongDetails: missing lrclist -> no LoadedLyric", delegate
		{
			KuwoSongInfo song = new KuwoSongInfo();
			new KuwoTagProvider().PopulateSongDetails(song, "{\"data\":{\"songinfo\":{\"pic\":\"z\"}}}");
			Check.Null(song.LoadedLyric, "no lyric");
			Check.Equal("z", song.CoverUrl, "cover z");
		});

		yield return ("PopulateSongDetails: cover pic size segment rewritten to 700", delegate
		{
			KuwoSongInfo song = new KuwoSongInfo();
			new KuwoTagProvider().PopulateSongDetails(song, "{\"data\":{\"lrclist\":[],\"songinfo\":{\"pic\":\"a/1/2/3/z\"}}}");
			Check.Equal("a/1/2/3/z", song.CoverUrl, "raw cover");
			Check.Equal("a/700/2/3/z", song.LargeCoverUrl, "size segment -> 700");
		});
	}
}
