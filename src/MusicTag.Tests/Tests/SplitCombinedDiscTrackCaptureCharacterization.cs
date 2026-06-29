using System;
using System.Collections.Generic;
using MusicTag.Schemes;

namespace MusicTag.Tests;

// FilenameRelatedBatchDialog.SplitCombinedDiscTrackCapture 的 characterization（纯逻辑、无 fixture）。
// 锁定 ChangeTags(文件名->标签)处理"disc/track 组合占位符 + 后续占位符"连写 token 的现状:
// token 形如 ^(@4@5|@4|@5)(@[0-8])$ 时,把捕获文本按数字前缀 ^(\d*)(.*)$ 拆成两段分别赋值
// (各段非空白才设置);否则原样单段赋值。由 ChangeTags 内联逻辑提取为 internal static(行为逐字保持)。
internal static class SplitCombinedDiscTrackCaptureCharacterization
{
	private static List<(string parameter, string value)> Split(string token, string captured)
	{
		return FilenameRelatedBatchDialog.SplitCombinedDiscTrackCapture(token, captured);
	}

	public static IEnumerable<(string, Action)> All()
	{
		yield return ("SplitCombinedDiscTrackCapture non-combined \"@1\" -> single passthrough", delegate
		{
			var result = Split("@1", "SomeTitle");
			Check.Equal(1, result.Count, "count");
			Check.Equal("@1", result[0].Item1, "[0].param");
			Check.Equal("SomeTitle", result[0].Item2, "[0].value");
		});

		yield return ("SplitCombinedDiscTrackCapture \"@4@5@1\" \"30501Title\" -> disc-track 30501 + title", delegate
		{
			var result = Split("@4@5@1", "30501Title");
			Check.Equal(2, result.Count, "count");
			Check.Equal("@4@5", result[0].Item1, "[0].param");
			Check.Equal("30501", result[0].Item2, "[0].value");
			Check.Equal("@1", result[1].Item1, "[1].param");
			Check.Equal("Title", result[1].Item2, "[1].value");
		});

		yield return ("SplitCombinedDiscTrackCapture \"@4@1\" \"5Title\" -> disc 5 + title", delegate
		{
			var result = Split("@4@1", "5Title");
			Check.Equal(2, result.Count, "count");
			Check.Equal("@4", result[0].Item1, "[0].param");
			Check.Equal("5", result[0].Item2, "[0].value");
			Check.Equal("@1", result[1].Item1, "[1].param");
			Check.Equal("Title", result[1].Item2, "[1].value");
		});

		yield return ("SplitCombinedDiscTrackCapture \"@5@1\" \"Title\" -> empty numeric prefix dropped, title only", delegate
		{
			var result = Split("@5@1", "Title");
			Check.Equal(1, result.Count, "count");
			Check.Equal("@1", result[0].Item1, "[0].param");
			Check.Equal("Title", result[0].Item2, "[0].value");
		});

		yield return ("SplitCombinedDiscTrackCapture \"@4@5@1\" \"12\" -> disc-track only, empty tail dropped", delegate
		{
			var result = Split("@4@5@1", "12");
			Check.Equal(1, result.Count, "count");
			Check.Equal("@4@5", result[0].Item1, "[0].param");
			Check.Equal("12", result[0].Item2, "[0].value");
		});

		yield return ("SplitCombinedDiscTrackCapture \"@4@1\" empty captured -> no assignments", delegate
		{
			var result = Split("@4@1", "");
			Check.Equal(0, result.Count, "count");
		});
	}
}
