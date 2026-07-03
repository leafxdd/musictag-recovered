using System;
using System.Collections.Generic;
using System.IO;
using MusicTag.Schemes;

namespace MusicTag.Tests;

// FilenameRelatedBatchDialog.MoveRelatedFileBestEffort 的【单元测试】(注入 moveFile/reportFailure,不碰真实文件系统)。
// 该方法由 route A form 3 从 RenameFilesBatchWorker.RenameFiles 的【两段对称的关联文件(lrc/封面)尽力而为移动】
// 内联块合并提取(byte-identical:MoveFileAllowingCaseOnlyRename -> 注入 moveFile、ReportFailure -> 注入 reportFailure)。
// 锁定 RenameFiles 的【韧性不变式】(major-finding 核心):
//   - 仅当 source 与 destination 均非 null 才移动(gate,与下游 ResolveRelatedFileTarget 返回 null 配合);
//   - 移动失败【只告警(reportFailure(sourcePath, ex.Message)),异常被本方法吞掉、不向上抛出】—— 这正是
//     "歌词/封面移动失败不回退已成功的音频改名":异常到不了外层 catch,故 successCount 不回退、failureCount 不触发。
// 注入 fake(Recorder)记录 moveFile/reportFailure 调用,moveFile 可配置抛异常 —— 完全不依赖真实文件系统。
internal static class MoveRelatedFileBestEffortCharacterization
{
	// 记录 moveFile / reportFailure 的调用;MoveThrows 非 null 时 moveFile 抛该异常(模拟移动失败)。
	private sealed class Recorder
	{
		public List<(string src, string dst)> Moves = new List<(string src, string dst)>();
		public List<(string path, string message)> Failures = new List<(string path, string message)>();
		public Exception MoveThrows;

		public void Move(string src, string dst)
		{
			Moves.Add((src, dst));
			if (MoveThrows != null)
			{
				throw MoveThrows;
			}
		}

		public void Report(string path, string message)
		{
			Failures.Add((path, message));
		}
	}

	public static IEnumerable<(string, Action)> All()
	{
		// --- 都非 null + 移动成功 -> moveFile 调一次(参数正确)、无告警、不抛 ---
		yield return ("MoveRelatedFileBestEffort: both non-null, move succeeds -> moved once, no failure", delegate
		{
			Recorder r = new Recorder();
			FilenameRelatedBatchDialog.MoveRelatedFileBestEffort("C:\\m\\a.lrc", "C:\\m\\b.lrc", r.Move, r.Report);
			Check.Equal(1, r.Moves.Count, "moveFile called once");
			Check.Equal("C:\\m\\a.lrc", r.Moves[0].src, "move src");
			Check.Equal("C:\\m\\b.lrc", r.Moves[0].dst, "move dst");
			Check.Equal(0, r.Failures.Count, "no failure reported");
		});

		// --- 都非 null + 移动抛异常 -> 异常被吞(不传播)、告警一次(sourcePath, ex.Message) ---
		// 【韧性不变式核心】:不传播 == 不触发外层 catch == 不 failureCount++ == 不回退已成功的音频改名。
		yield return ("MoveRelatedFileBestEffort: both non-null, move throws -> swallowed, failure reported (resilience invariant)", delegate
		{
			Recorder r = new Recorder { MoveThrows = new IOException("disk full") };
			bool propagated = false;
			try
			{
				FilenameRelatedBatchDialog.MoveRelatedFileBestEffort("C:\\m\\a.lrc", "C:\\m\\b.lrc", r.Move, r.Report);
			}
			catch (Exception)
			{
				propagated = true;
			}
			Check.True(!propagated, "exception swallowed, NOT propagated (prevents rollback of already-succeeded audio rename)");
			Check.Equal(1, r.Moves.Count, "moveFile attempted once");
			Check.Equal(1, r.Failures.Count, "failure reported once");
			Check.Equal("C:\\m\\a.lrc", r.Failures[0].path, "reportFailure path = sourcePath");
			Check.Equal("disk full", r.Failures[0].message, "reportFailure message = exception.Message");
		});

		// --- source == null -> gate 短路,move/report 都不调 ---
		yield return ("MoveRelatedFileBestEffort: null source -> no-op (gate)", delegate
		{
			Recorder r = new Recorder();
			FilenameRelatedBatchDialog.MoveRelatedFileBestEffort(null, "C:\\m\\b.lrc", r.Move, r.Report);
			Check.Equal(0, r.Moves.Count, "no move (source null)");
			Check.Equal(0, r.Failures.Count, "no failure");
		});

		// --- destination == null -> gate 短路(对应 ResolveRelatedFileTarget 返回 null 的防覆盖路径) ---
		yield return ("MoveRelatedFileBestEffort: null destination -> no-op (gate)", delegate
		{
			Recorder r = new Recorder();
			FilenameRelatedBatchDialog.MoveRelatedFileBestEffort("C:\\m\\a.lrc", null, r.Move, r.Report);
			Check.Equal(0, r.Moves.Count, "no move (dest null)");
			Check.Equal(0, r.Failures.Count, "no failure");
		});

		// --- 都 null -> no-op ---
		yield return ("MoveRelatedFileBestEffort: both null -> no-op", delegate
		{
			Recorder r = new Recorder();
			FilenameRelatedBatchDialog.MoveRelatedFileBestEffort(null, null, r.Move, r.Report);
			Check.Equal(0, r.Moves.Count, "no move");
			Check.Equal(0, r.Failures.Count, "no failure");
		});
	}
}
