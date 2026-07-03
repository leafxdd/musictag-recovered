using System;
using System.Collections.Generic;
using MusicTag.Schemes;

namespace MusicTag.Tests;

// FilenameRelatedBatchDialog.ResolveDestinationAudioPath 的 characterization。
// 该方法由本批从 RenameFilesBatchWorker.RenameFiles 提取(byte-identical move:仅把内联的 File.Exists
// 换成注入的 fileExists predicate,末尾加 return;生产端传 File.Exists 方法组,逐字节等价)。
// 它由渲染后的新文件名构造目标音频路径并处理同名冲突,锁定:
//   - 目标不存在 -> 直接返回 base 路径(originalPath 的目录 + newFilename + originalPath 的扩展名);
//   - 目标存在【但】newFilename 与原文件名(无扩展,OrdinalIgnoreCase)相同 -> 不去重(原地/仅大小写改名豁免);
//   - 目标存在【且】异名 -> 追加 " (N)",N 从 1 递增至首个空位(裸整数,无零填充);
//   - 目录与扩展名均取自 originalPath。
// fileExists 注入,用 HashSet<string>(OrdinalIgnoreCase) 模拟 Windows 文件系统(大小写不敏感);
// 测试不触碰真实文件系统(Path.GetDirectoryName/GetExtension 是纯字符串运算)。
internal static class ResolveDestinationAudioPathCharacterization
{
	private static Func<string, bool> Exists(params string[] existing)
	{
		HashSet<string> set = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
		return path => set.Contains(path);
	}

	public static IEnumerable<(string, Action)> All()
	{
		// --- 目标不存在 -> base 路径(目录 + newFilename + 原扩展名) ---
		yield return ("ResolveDestinationAudioPath: target absent -> base path", delegate
		{
			string result = FilenameRelatedBatchDialog.ResolveDestinationAudioPath("C:\\m\\old.mp3", "new", Exists());
			Check.Equal("C:\\m\\new.mp3", result, "base path");
		});

		// --- 扩展名取自 originalPath(非 newFilename) ---
		yield return ("ResolveDestinationAudioPath: extension from originalPath (.flac)", delegate
		{
			string result = FilenameRelatedBatchDialog.ResolveDestinationAudioPath("C:\\m\\old.flac", "new", Exists());
			Check.Equal("C:\\m\\new.flac", result, "flac extension preserved");
		});

		// --- 目录取自 originalPath(嵌套路径) ---
		yield return ("ResolveDestinationAudioPath: directory from originalPath (nested)", delegate
		{
			string result = FilenameRelatedBatchDialog.ResolveDestinationAudioPath("C:\\music\\album\\01 - x.mp3", "renamed", Exists());
			Check.Equal("C:\\music\\album\\renamed.mp3", result, "directory preserved");
		});

		// --- 目标存在且异名 -> (1) ---
		yield return ("ResolveDestinationAudioPath: target exists, different name -> (1)", delegate
		{
			string result = FilenameRelatedBatchDialog.ResolveDestinationAudioPath("C:\\m\\old.mp3", "new", Exists("C:\\m\\new.mp3"));
			Check.Equal("C:\\m\\new (1).mp3", result, "first dedup");
		});

		// --- base + (1) 都存在 -> (2) ---
		yield return ("ResolveDestinationAudioPath: base + (1) exist -> (2)", delegate
		{
			string result = FilenameRelatedBatchDialog.ResolveDestinationAudioPath("C:\\m\\old.mp3", "new", Exists("C:\\m\\new.mp3", "C:\\m\\new (1).mp3"));
			Check.Equal("C:\\m\\new (2).mp3", result, "second dedup");
		});

		// --- 首个空位:base 与 (2) 占用、(1) 空 -> (1)(从 1 起首个空位,不跳过) ---
		yield return ("ResolveDestinationAudioPath: gap at (1) -> (1) (first free slot)", delegate
		{
			string result = FilenameRelatedBatchDialog.ResolveDestinationAudioPath("C:\\m\\old.mp3", "new", Exists("C:\\m\\new.mp3", "C:\\m\\new (2).mp3"));
			Check.Equal("C:\\m\\new (1).mp3", result, "first free slot is (1) even if (2) taken");
		});

		// --- 连续冲突跨越两位数:base..(10) 存在 -> (11)(裸整数,无零填充) ---
		yield return ("ResolveDestinationAudioPath: contiguous conflicts -> (11) bare integer", delegate
		{
			List<string> existing = new List<string> { "C:\\m\\new.mp3" };
			for (int n = 1; n <= 10; n++)
			{
				existing.Add("C:\\m\\new (" + n + ").mp3");
			}
			string result = FilenameRelatedBatchDialog.ResolveDestinationAudioPath("C:\\m\\old.mp3", "new", Exists(existing.ToArray()));
			Check.Equal("C:\\m\\new (11).mp3", result, "N increments to 11, no zero-pad");
		});

		// --- 同名豁免:目标存在但 newFilename == 原文件名(无扩展) -> 不去重,返回 base ---
		yield return ("ResolveDestinationAudioPath: same name (in-place) -> NOT deduped", delegate
		{
			string result = FilenameRelatedBatchDialog.ResolveDestinationAudioPath("C:\\m\\song.mp3", "song", Exists("C:\\m\\song.mp3"));
			Check.Equal("C:\\m\\song.mp3", result, "in-place rename returns base, no (N)");
		});

		// --- 同名豁免(大小写不敏感):newFilename 仅大小写不同 -> 不去重 ---
		yield return ("ResolveDestinationAudioPath: case-only rename -> NOT deduped (OrdinalIgnoreCase)", delegate
		{
			string result = FilenameRelatedBatchDialog.ResolveDestinationAudioPath("C:\\m\\Song.mp3", "song", Exists("C:\\m\\song.mp3"));
			Check.Equal("C:\\m\\song.mp3", result, "case-only rename returns base");
		});

		// --- 现实边界:newFilename 本身已含括号后缀(如 "(Live)"/"(Remix)") -> 朴素拼接追加 " (1)",
		//     不识别/不复用已有括号索引。锁定此现状以防未来"括号感知去重"重构悄悄改变槽位而仍通过其余 case。
		yield return ("ResolveDestinationAudioPath: newFilename with parenthetical suffix -> naive ' (1)' appended", delegate
		{
			string result = FilenameRelatedBatchDialog.ResolveDestinationAudioPath("C:\\m\\old.mp3", "Song (Live)", Exists("C:\\m\\Song (Live).mp3"));
			Check.Equal("C:\\m\\Song (Live) (1).mp3", result, "existing parenthetical not reused; ' (1)' appended verbatim");
		});

		// --- base 构造:originalPath 无目录段 -> GetDirectoryName 返回空串,硬编码 "\" 分隔符产生前导反斜杠。
		//     锁定字符串拼接现状(防 Path.Combine 式清理重构改变结果)。
		yield return ("ResolveDestinationAudioPath: originalPath without directory -> leading backslash", delegate
		{
			string result = FilenameRelatedBatchDialog.ResolveDestinationAudioPath("old.mp3", "new", Exists());
			Check.Equal("\\new.mp3", result, "empty dir + hardcoded separator -> leading backslash");
		});

		// --- base 构造:扩展名大小写原样保留(Path.GetExtension 不归一化大小写) ---
		yield return ("ResolveDestinationAudioPath: extension case preserved (.MP3)", delegate
		{
			string result = FilenameRelatedBatchDialog.ResolveDestinationAudioPath("C:\\m\\old.MP3", "new", Exists());
			Check.Equal("C:\\m\\new.MP3", result, "extension case preserved verbatim");
		});
	}
}
