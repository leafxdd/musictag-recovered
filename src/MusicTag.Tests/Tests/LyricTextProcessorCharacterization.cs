using System;
using System.Collections.Generic;
using MusicTag.Composer;
using MusicTagWinApp.Properties;

namespace MusicTag.Tests;

// LyricTextProcessor characterization —— 下载歌词写回前的 LRC 解析 + 静态变换(用户可见),此前零覆盖
// (grep 测试目录 ShiftLyricTimestamps/RemoveTimestamps/MergeDownloadedLyrics/ReformatLyric = 0)。
// 手工 trace 锁定的行为:
//   - LRC 元数据解析 [ti:]/[ar:]/[al:]:Substring(4,len-5).Trim();无 [ti:] 时 Title 保持 null
//     (ctor 未初始化 Title),Artist/Album 默认 ""。
//   - ParseMillisecondPart:毫秒片段按缺位数 *10 补足到 3 位("5"->500,"05"->50,"500"->500);
//     FormatTimestamp(ms,false) 显示厘秒 = ms/10。
//   - [offset:N] 加到每行时间戳(Math.Max(0, parsed+offset))。
//   - ShiftTimestamps:偏移后 <0 clamp 到 0,key 碰撞则 ++nudge(两行都保留,不丢行)。
//   - RemoveTimestamps / ReformatLyric / ShiftLyricTimestamps:空或"处理后为空"时原样回退。
//   - MergeDownloadedLyrics / RemoveTimestampsFromDownloadedLyrics / ReformatDownloadedLyrics:
//     按 preferTranslatedOnly + 输入空判定选择返回哪路(避开 merge 内部翻译分隔符,仅测分支选择)。
// 凡经 FormatLyricLine 输出格式的用例先 pin Settings.Default.LyricDownload_DownloadTrans_LyricFormat = 1
// (格式 1:每行原文;无 translated 时与 default 分支等价),使输出确定;纯解析属性用例不依赖 Settings。
internal static class LyricTextProcessorCharacterization
{
	public static IEnumerable<(string, Action)> All()
	{
		// ===== LRC 元数据解析(不经 FormatLyricLine,无 Settings 依赖)=====
		yield return ("LyricTextProcessor: parses [ti:]/[ar:]/[al:] metadata", delegate
		{
			LyricTextProcessor p = new LyricTextProcessor("[ti:MyTitle]\n[ar:MyArtist]\n[al:MyAlbum]\n[00:01.00]line");
			Check.Equal("MyTitle", p.Title, "title");
			Check.Equal("MyArtist", p.Artist, "artist");
			Check.Equal("MyAlbum", p.Album, "album");
		});

		yield return ("LyricTextProcessor: no [ti:] -> Title null, Artist/Album default empty", delegate
		{
			LyricTextProcessor p = new LyricTextProcessor("[00:01.00]line");
			Check.Null(p.Title, "title null when absent");
			Check.Equal("", p.Artist, "artist default empty");
			Check.Equal("", p.Album, "album default empty");
		});

		// ===== 毫秒补位陷阱(via ReformatLyric, pin format=1)=====
		yield return ("LyricTextProcessor: millisecond pad-to-3 (*10 per missing digit)", delegate
		{
			Settings.Default.LyricDownload_DownloadTrans_LyricFormat = 1;
			// "5" -> 500ms -> 厘秒 50; "05" -> 50ms -> 厘秒 05; "500" -> 500ms -> 厘秒 50
			Check.Equal("[00:00.50]x", LyricTextProcessor.ReformatLyric("[00:00.5]x", false, true), "1-digit ms '5' -> 500");
			Check.Equal("[00:00.05]x", LyricTextProcessor.ReformatLyric("[00:00.05]x", false, true), "2-digit ms '05' -> 50");
			Check.Equal("[00:00.50]x", LyricTextProcessor.ReformatLyric("[00:00.500]x", false, true), "3-digit ms '500' -> 500");
		});

		yield return ("LyricTextProcessor: [offset:N] shifts every timestamp", delegate
		{
			Settings.Default.LyricDownload_DownloadTrans_LyricFormat = 1;
			Check.Equal("[00:00.50]x", LyricTextProcessor.ReformatLyric("[offset:500]\n[00:00.00]x", false, true), "offset 500 -> 0+500ms");
		});

		// ===== RemoveTimestamps =====
		yield return ("LyricTextProcessor.RemoveTimestamps: strips timestamps", delegate
		{
			Settings.Default.LyricDownload_DownloadTrans_LyricFormat = 1;
			Check.Equal("a\nb", LyricTextProcessor.RemoveTimestamps("[00:01.00]a\n[00:02.00]b"), "two timed lines -> bare text");
		});

		yield return ("LyricTextProcessor.RemoveTimestamps: no-timestamp text returned verbatim", delegate
		{
			Settings.Default.LyricDownload_DownloadTrans_LyricFormat = 1;
			Check.Equal("plain text", LyricTextProcessor.RemoveTimestamps("plain text"), "no timestamps -> original returned");
		});

		// ===== ShiftLyricTimestamps =====
		yield return ("LyricTextProcessor.ShiftLyricTimestamps: positive shift", delegate
		{
			Settings.Default.LyricDownload_DownloadTrans_LyricFormat = 1;
			Check.Equal("[00:03.00]a", LyricTextProcessor.ShiftLyricTimestamps("[00:01.00]a", 2000), "1000ms +2000 -> 3000ms");
		});

		yield return ("LyricTextProcessor.ShiftLyricTimestamps: negative shift clamps to 0", delegate
		{
			Settings.Default.LyricDownload_DownloadTrans_LyricFormat = 1;
			Check.Equal("[00:00.00]a", LyricTextProcessor.ShiftLyricTimestamps("[00:01.00]a", -5000), "1000ms -5000 -> clamp 0");
		});

		yield return ("LyricTextProcessor.ShiftLyricTimestamps: collision after clamp keeps both lines", delegate
		{
			Settings.Default.LyricDownload_DownloadTrans_LyricFormat = 1;
			// 两行都 clamp 到 0，第二行 key ++nudge 到 1（显示仍 00.00），两行都保留不丢
			Check.Equal("[00:00.00]a\n[00:00.00]b", LyricTextProcessor.ShiftLyricTimestamps("[00:01.00]a\n[00:02.00]b", -5000), "both clamp 0, nudged, both kept");
		});

		yield return ("LyricTextProcessor.ShiftLyricTimestamps: empty -> verbatim", delegate
		{
			Check.Equal("", LyricTextProcessor.ShiftLyricTimestamps("", 100), "empty input returned as-is");
		});

		// ===== ReformatLyric =====
		yield return ("LyricTextProcessor.ReformatLyric: header kept vs removed", delegate
		{
			Settings.Default.LyricDownload_DownloadTrans_LyricFormat = 1;
			Check.Equal("[ti:S]\n[00:01.00]a", LyricTextProcessor.ReformatLyric("[ti:S]\n[00:01.00]a", false, false), "removeHeaderTags=false keeps [ti:]");
			Check.Equal("[00:01.00]a", LyricTextProcessor.ReformatLyric("[ti:S]\n[00:01.00]a", false, true), "removeHeaderTags=true drops [ti:]");
		});

		yield return ("LyricTextProcessor.ReformatLyric: empty -> verbatim", delegate
		{
			Check.Equal("", LyricTextProcessor.ReformatLyric("", false, false), "empty input returned as-is");
		});

		// ===== MergeDownloadedLyrics 分支选择(避开 merge 内部翻译格式)=====
		yield return ("LyricTextProcessor.MergeDownloadedLyrics: preferTranslatedOnly returns translated verbatim", delegate
		{
			Check.Equal("T", LyricTextProcessor.MergeDownloadedLyrics("[00:01.00]a", "T", true), "prefer + translated present -> translated");
		});

		yield return ("LyricTextProcessor.MergeDownloadedLyrics: empty translated returns original verbatim", delegate
		{
			Check.Equal("[00:01.00]a", LyricTextProcessor.MergeDownloadedLyrics("[00:01.00]a", "", false), "no translated -> original untouched");
		});

		yield return ("LyricTextProcessor.MergeDownloadedLyrics: empty original returns original", delegate
		{
			Check.Equal("", LyricTextProcessor.MergeDownloadedLyrics("", "T", false), "empty original, no prefer -> original ('')");
		});

		// ===== RemoveTimestampsFromDownloadedLyrics 分支 =====
		yield return ("LyricTextProcessor.RemoveTimestampsFromDownloadedLyrics: prefer routes to translated strip", delegate
		{
			Settings.Default.LyricDownload_DownloadTrans_LyricFormat = 1;
			Check.Equal("t", LyricTextProcessor.RemoveTimestampsFromDownloadedLyrics("x", "[00:01.00]t", true), "prefer -> RemoveTimestamps(translated)");
		});

		yield return ("LyricTextProcessor.RemoveTimestampsFromDownloadedLyrics: original strip when no translated", delegate
		{
			Settings.Default.LyricDownload_DownloadTrans_LyricFormat = 1;
			Check.Equal("a", LyricTextProcessor.RemoveTimestampsFromDownloadedLyrics("[00:01.00]a", "", false), "no prefer, no translated -> strip original");
		});

		// ===== ReformatDownloadedLyrics 分支 =====
		yield return ("LyricTextProcessor.ReformatDownloadedLyrics: original path keeps header", delegate
		{
			Settings.Default.LyricDownload_DownloadTrans_LyricFormat = 1;
			Check.Equal("[ti:S]\n[00:01.00]a", LyricTextProcessor.ReformatDownloadedLyrics("[ti:S]\n[00:01.00]a", "", false, false, false), "no prefer -> reformat original, keep header");
		});

		yield return ("LyricTextProcessor.ReformatDownloadedLyrics: prefer routes to translated reformat", delegate
		{
			Settings.Default.LyricDownload_DownloadTrans_LyricFormat = 1;
			Check.Equal("[00:02.00]t", LyricTextProcessor.ReformatDownloadedLyrics("x", "[00:02.00]t", false, true, true), "prefer -> reformat translated, drop header");
		});
	}
}
