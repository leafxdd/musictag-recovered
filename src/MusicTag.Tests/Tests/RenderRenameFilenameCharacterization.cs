using System;
using System.Collections.Generic;
using MusicTag.Schemes;

namespace MusicTag.Tests;

// FilenameRelatedBatchDialog.RenderRenameFilename 的 characterization（纯逻辑、无 fixture）。
// 锁定"文件名模板 @1..@8 占位符替换 + 非法字符清理"的现状——RenameFilesBatchWorker 重命名核心,
// 由内联逻辑提取为 internal static(行为逐字保持)。清理规则:路径分隔符 \ / -> ;,
// 其余非法字符(空白及 " : * ? < > |)-> 空格。
internal static class RenderRenameFilenameCharacterization
{
	// 参数顺序与 RenameFiles 调用一致：title, artist, album, disc, trackNumber, year, comment, albumArtist。
	private static string Render(string pattern, string title = "", string artist = "", string album = "", string disc = "", string track = "", string year = "", string comment = "", string albumArtist = "")
	{
		return FilenameRelatedBatchDialog.RenderRenameFilename(pattern, title, artist, album, disc, track, year, comment, albumArtist);
	}

	public static IEnumerable<(string, Action)> All()
	{
		yield return ("RenderRenameFilename: basic \"@2 - @1\" -> \"artist - title\"", delegate
		{
			Check.Equal("Artist - Title", Render("@2 - @1", title: "Title", artist: "Artist"), "result");
		});

		yield return ("RenderRenameFilename: all eight placeholders substituted in order", delegate
		{
			string result = Render("@1-@2-@3-@4-@5-@6-@7-@8", "T", "A", "Al", "D", "05", "2020", "C", "AA");
			Check.Equal("T-A-Al-D-05-2020-C-AA", result, "result");
		});

		yield return ("RenderRenameFilename: path separators / and \\ -> ;", delegate
		{
			Check.Equal("a;b;c", Render("@1", title: "a/b\\c"), "result");
		});

		yield return ("RenderRenameFilename: illegal chars : * ? -> space", delegate
		{
			Check.Equal("a b c d", Render("@1", title: "a:b*c?d"), "result");
		});

		yield return ("RenderRenameFilename: illegal chars \" < > | -> space", delegate
		{
			Check.Equal("a b c d e", Render("@1", title: "a\"b<c>d|e"), "result");
		});

		yield return ("RenderRenameFilename: whitespace (tab) -> space", delegate
		{
			Check.Equal("a b", Render("@1", title: "a\tb"), "result");
		});

		yield return ("RenderRenameFilename: empty placeholders (missing tags) collapse out", delegate
		{
			Check.Equal("Title", Render("@1@2@3@4@5@6@7@8", title: "Title"), "result");
		});

		yield return ("RenderRenameFilename: later placeholder pollutes earlier-substituted value (current behavior)", delegate
		{
			// 现状：Replace 链顺序导致已替换进结果的 "@2" 文本被随后的 @2->artist 二次替换。
			Check.Equal("xAy", Render("@1", title: "x@2y", artist: "A"), "result");
		});
	}
}
