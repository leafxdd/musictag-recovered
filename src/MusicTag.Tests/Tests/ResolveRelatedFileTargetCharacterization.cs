using System;
using System.Collections.Generic;
using MusicTag.Schemes;

namespace MusicTag.Tests;

// FilenameRelatedBatchDialog.ResolveRelatedFileTarget 的 characterization。
// 该方法由本批从 RenameFilesBatchWorker.RenameFiles 的【两段对称的关联文件(lrc/封面)目标 + 防覆盖】内联
// (byte-identical move,File.Exists 换注入 fileExists)合并提取:lrc 段 extension 传字面 ".lrc"、image 段传
// Path.GetExtension(sourceImagePath)(调用点三元保 source 非 null 时才求值)。锁定:
//   - sourcePath==null -> null(无该关联文件,不移动);
//   - 目标不存在 -> 目标路径(目标音频的同名兄弟文件 + 给定扩展名);
//   - 目标已存在【且】!= source(OrdinalIgnoreCase) -> null(不覆盖一个不同的已存在文件);
//   - 目标已存在【且】== source(精确或仅大小写) -> 返回目标(原地/仅大小写改名,允许);
//   - extension 参数化(lrc=".lrc"、image=任意图片扩展名)。
// fileExists 注入,用 HashSet<string>(OrdinalIgnoreCase) 模拟 Windows 文件系统;测试不触碰真实文件系统。
internal static class ResolveRelatedFileTargetCharacterization
{
	private static Func<string, bool> Exists(params string[] existing)
	{
		HashSet<string> set = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
		return path => set.Contains(path);
	}

	public static IEnumerable<(string, Action)> All()
	{
		// --- sourcePath==null -> null(无该关联文件) ---
		yield return ("ResolveRelatedFileTarget: null source -> null (no related file)", delegate
		{
			string result = FilenameRelatedBatchDialog.ResolveRelatedFileTarget("C:\\m\\new.mp3", null, ".lrc", Exists());
			Check.Null(result, "null source -> null");
		});

		// --- 目标不存在 -> 目标路径(目标音频同名 + .lrc) ---
		yield return ("ResolveRelatedFileTarget: target absent -> sibling target path (.lrc)", delegate
		{
			string result = FilenameRelatedBatchDialog.ResolveRelatedFileTarget("C:\\m\\new.mp3", "C:\\m\\old.lrc", ".lrc", Exists());
			Check.Equal("C:\\m\\new.lrc", result, "sibling .lrc of destination audio");
		});

		// --- 目标已存在且 != source -> null(不覆盖不同文件) ---
		yield return ("ResolveRelatedFileTarget: target exists & differs from source -> null (no overwrite)", delegate
		{
			string result = FilenameRelatedBatchDialog.ResolveRelatedFileTarget("C:\\m\\new.mp3", "C:\\m\\old.lrc", ".lrc", Exists("C:\\m\\new.lrc"));
			Check.Null(result, "would overwrite a different existing file -> skip");
		});

		// --- 目标已存在且 == source(精确) -> 返回目标(原地,允许) ---
		yield return ("ResolveRelatedFileTarget: target exists & equals source (exact) -> target (in-place allowed)", delegate
		{
			string result = FilenameRelatedBatchDialog.ResolveRelatedFileTarget("C:\\m\\new.mp3", "C:\\m\\new.lrc", ".lrc", Exists("C:\\m\\new.lrc"));
			Check.Equal("C:\\m\\new.lrc", result, "target==source -> not skipped");
		});

		// --- 目标已存在且 == source(仅大小写) -> 返回目标(OrdinalIgnoreCase 视为同一) ---
		yield return ("ResolveRelatedFileTarget: target exists & equals source (case-only) -> target", delegate
		{
			string result = FilenameRelatedBatchDialog.ResolveRelatedFileTarget("C:\\m\\NEW.mp3", "C:\\m\\new.lrc", ".lrc", Exists("C:\\m\\new.lrc"));
			Check.Equal("C:\\m\\NEW.lrc", result, "case-only diff treated as same -> not skipped");
		});

		// --- extension 参数化:image 扩展名(.jpg)目标不存在 -> 目标路径 ---
		yield return ("ResolveRelatedFileTarget: image extension (.jpg), target absent -> sibling .jpg", delegate
		{
			string result = FilenameRelatedBatchDialog.ResolveRelatedFileTarget("C:\\m\\new.mp3", "C:\\m\\old.jpg", ".jpg", Exists());
			Check.Equal("C:\\m\\new.jpg", result, "sibling .jpg of destination audio");
		});

		// --- null-vs-empty:仅守卫 sourcePath==null(非 IsNullOrEmpty),空串 source 会 fall through 计算目标。
		//     生产端调用点不会传 ""(source 为 null 或真实路径),此 case 仅锁定 null-only guard 现状(防改 IsNullOrEmpty)。
		yield return ("ResolveRelatedFileTarget: empty-string source falls through (guards null only, not empty)", delegate
		{
			string result = FilenameRelatedBatchDialog.ResolveRelatedFileTarget("C:\\m\\new.mp3", "", ".lrc", Exists());
			Check.Equal("C:\\m\\new.lrc", result, "empty source != null -> computes target");
		});
	}
}
