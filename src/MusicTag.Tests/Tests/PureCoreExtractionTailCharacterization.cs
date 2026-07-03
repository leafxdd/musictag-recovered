using System;
using System.Collections.Generic;
using MusicTag.Serialization;
using MusicTag.States;
using MusicTagWinApp.Exporters;

namespace MusicTag.Tests;

// X2c 尾批三处 pure-core 提取的 golden master:
// 1) ConfigDescriptorState.BuildFileExtension(filePath) —— 文件扩展名归一(去点大写,无扩展名/null -> "")。
// 2) TextBoxFindReplaceController.ReplaceAllMatches(text, searchText, replacementText, comparison) —— 全量非重叠替换
//    (SearchText/GetStringComparison 提为参数,便于表征;原循环内每迭代读归约为调用点一次求值)。
// 3) LyricSearchDialog.ResolveLyricIconIndex(lyricUrl, hasDownloadableInlineLyric) —— 歌词图标索引
//    (0=内嵌可下载或 .lrc 链接;1=其他;hasDownloadableInlineLyric 由调用点预计算含短路)。
internal static class PureCoreExtractionTailCharacterization
{
	public static IEnumerable<(string, Action)> All()
	{
		// ===== BuildFileExtension:Path.GetExtension -> 去前导点 -> ToUpperInvariant;null/无扩展名 -> "" =====
		yield return ("ConfigDescriptorState.BuildFileExtension: uppercase no-dot; null/no-ext -> empty", delegate
		{
			Check.Equal("MP3", ConfigDescriptorState.BuildFileExtension("song.mp3"), "mp3 -> MP3");
			Check.Equal("FLAC", ConfigDescriptorState.BuildFileExtension("SONG.FLAC"), "already-upper FLAC");
			Check.Equal("WAV", ConfigDescriptorState.BuildFileExtension("song.Wav"), "mixed case -> WAV");
			Check.Equal("OGG", ConfigDescriptorState.BuildFileExtension("C:\\music\\track.OGG"), "full path -> last ext OGG");
			Check.Equal("GZ", ConfigDescriptorState.BuildFileExtension("archive.tar.gz"), "multi-dot -> last ext GZ");
			Check.Equal("", ConfigDescriptorState.BuildFileExtension("noext"), "no extension -> empty");
			Check.Equal("", ConfigDescriptorState.BuildFileExtension(""), "empty path -> empty");
			Check.Equal("", ConfigDescriptorState.BuildFileExtension(null), "null path -> empty (Path.GetExtension(null)=null)");
		});

		// ===== ReplaceAllMatches:全量非重叠替换,searchText 大小写由 comparison 决定 =====
		yield return ("TextBoxFindReplaceController.ReplaceAllMatches: all non-overlapping matches, comparison-driven", delegate
		{
			Check.Equal("a-b-c", TextBoxFindReplaceController.ReplaceAllMatches("aXbXc", "X", "-", StringComparison.Ordinal), "two matches -> a-b-c");
			Check.Equal("bbbbbb", TextBoxFindReplaceController.ReplaceAllMatches("aaa", "a", "bb", StringComparison.Ordinal), "3x a -> 3x bb");
			Check.Equal("abc", TextBoxFindReplaceController.ReplaceAllMatches("abc", "x", "y", StringComparison.Ordinal), "no match -> unchanged");
			Check.Equal("XX", TextBoxFindReplaceController.ReplaceAllMatches("AbAb", "ab", "X", StringComparison.OrdinalIgnoreCase), "case-insensitive -> both replaced");
			Check.Equal("AbAb", TextBoxFindReplaceController.ReplaceAllMatches("AbAb", "ab", "X", StringComparison.Ordinal), "ordinal case-sensitive -> no match");
			Check.Equal("ba", TextBoxFindReplaceController.ReplaceAllMatches("aaa", "aa", "b", StringComparison.Ordinal), "non-overlapping: first 'aa' then leftover 'a'");
			Check.Equal("", TextBoxFindReplaceController.ReplaceAllMatches("", "x", "y", StringComparison.Ordinal), "empty text -> empty");
		});

		// ===== ResolveLyricIconIndex:0=内嵌可下载(预计算 bool)或 .lrc 链接;否则 1 =====
		yield return ("LyricSearchDialog.ResolveLyricIconIndex: inline-downloadable or .lrc -> 0 else 1", delegate
		{
			Check.Equal(0, LyricSearchDialog.ResolveLyricIconIndex(null, true), "null url + inline downloadable -> 0");
			Check.Equal(1, LyricSearchDialog.ResolveLyricIconIndex(null, false), "null url + no inline -> 1");
			Check.Equal(0, LyricSearchDialog.ResolveLyricIconIndex("http://x/song.lrc", false), ".lrc suffix -> 0");
			Check.Equal(0, LyricSearchDialog.ResolveLyricIconIndex("http://x/song.LRC", false), ".LRC (OrdinalIgnoreCase) -> 0");
			Check.Equal(1, LyricSearchDialog.ResolveLyricIconIndex("http://x/song.txt", false), "non-.lrc url -> 1");
			Check.Equal(1, LyricSearchDialog.ResolveLyricIconIndex("", false), "empty url + no inline -> 1");
			Check.Equal(0, LyricSearchDialog.ResolveLyricIconIndex("http://x/song.txt", true), "inline downloadable precedence -> 0");
		});
	}
}
