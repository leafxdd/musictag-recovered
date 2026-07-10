using System;
using System.Collections.Generic;
using MusicTagWinApp.Adapter;
using MusicTagWinApp.Properties;

namespace MusicTag.Tests;

// AutoMatchTagsDialog 两处 pure-core 提取的 golden master:
// 1) BuildAutoMatchCompletionResult —— 从 StartAutoMatchTags 提取的完成文案核(4 路径,与
//    FilenameRelatedBatchDialog.BuildBatchCompletionResult 同构:totalCount>1 批量统计 / success>0 已保存 /
//    skipped>0 已跳过 / else 纯日志+isErr)。期望值引 Resources.Msg_*(与产品同源,i18n 安全),锁定分支选择、
//    拼接结构、isErr 三态、多文件 Format 参数序(success,fail,skip,processed)。原 4 路径 BEFORE 均求值一次
//    autoMatchLog.ToString(),故 logText 无条件预求值不改求值次数;volatile int 计数字段读无副作用。
// 2) IsOnlyWriteFileModeSelected(static 重载) —— 锁定空集 All 陷阱:Enumerable.All 对空集返回 true,
//    但 .Any() 守卫使空选择返回 false(不算"仅写文件")。
internal static class AutoMatchDialogCharacterization
{
	public static IEnumerable<(string, Action)> All()
	{
		yield return ("BuildAutoMatchCompletionResult: single file, success -> SaveCompleted, not error", delegate
		{
			var (msg, isErr) = AutoMatchTagsDialog.BuildAutoMatchCompletionResult(1, 1, 0, 0, 1, "log");
			Check.Equal(Resources.Msg_SaveCompleted + "\n" + "log", msg, "save completed msg");
			Check.True(!isErr, "not error");
		});

		yield return ("BuildAutoMatchCompletionResult: single file, skipped -> Skipped, not error", delegate
		{
			var (msg, isErr) = AutoMatchTagsDialog.BuildAutoMatchCompletionResult(1, 0, 0, 1, 1, "log");
			Check.Equal(Resources.Msg_Skipped + "\n" + "log", msg, "skipped msg");
			Check.True(!isErr, "not error");
		});

		yield return ("BuildAutoMatchCompletionResult: single file, neither success nor skip -> raw log, isErr=true", delegate
		{
			var (msg, isErr) = AutoMatchTagsDialog.BuildAutoMatchCompletionResult(1, 0, 1, 0, 1, "err");
			Check.Equal("err", msg, "raw log");
			Check.True(isErr, "error flag set");
		});

		yield return ("BuildAutoMatchCompletionResult: success precedence over skip when single file", delegate
		{
			// paths.Length<=1 时 success>0 分支先于 skipped>0(else-if 链序)。
			var (msg, isErr) = AutoMatchTagsDialog.BuildAutoMatchCompletionResult(1, 2, 0, 3, 5, "log");
			Check.Equal(Resources.Msg_SaveCompleted + "\n" + "log", msg, "success wins over skip");
			Check.True(!isErr, "not error");
		});

		yield return ("BuildAutoMatchCompletionResult: totalCount>1 takes batch path even when all counts zero", delegate
		{
			// paths.Length>1 优先于 success/skipped(即使全 0),走批量统计而非 else 的 isErr=true。
			var (msg, isErr) = AutoMatchTagsDialog.BuildAutoMatchCompletionResult(5, 0, 0, 0, 0, "log");
			string expected = string.Format(Resources.Msg_SaveCompleted + "\n" + Resources.Msg_OK_Fail_Skip_Count, 0, 0, 0, 0) + "\n" + "log";
			Check.Equal(expected, msg, "batch path even all-zero");
			Check.True(!isErr, "batch path not error");
		});

		yield return ("BuildAutoMatchCompletionResult: multi-file -> OK_Fail_Skip_Count format (arg order success,fail,skip,processed)", delegate
		{
			var (msg, isErr) = AutoMatchTagsDialog.BuildAutoMatchCompletionResult(3, 2, 1, 0, 3, "log");
			string expected = string.Format(Resources.Msg_SaveCompleted + "\n" + Resources.Msg_OK_Fail_Skip_Count, 2, 1, 0, 3) + "\n" + "log";
			Check.Equal(expected, msg, "multi-file statistics");
			Check.True(!isErr, "not error");
		});

		yield return ("IsOnlyWriteFileModeSelected: empty -> false (All-over-empty trap guarded by Any)", delegate
		{
			var empty = new Dictionary<string, (string writeMode, bool overwrite)>();
			Check.True(!AutoMatchTagsDialog.IsOnlyWriteFileModeSelected(empty), "empty -> false (NOT vacuous-true)");

			var allFile = new Dictionary<string, (string writeMode, bool overwrite)>
			{
				{ "title", ("SaveToFile", false) },
				{ "artist", ("SaveToFile", true) },
			};
			Check.True(AutoMatchTagsDialog.IsOnlyWriteFileModeSelected(allFile), "all SaveToFile -> true");

			var mixed = new Dictionary<string, (string writeMode, bool overwrite)>
			{
				{ "title", ("SaveToFile", false) },
				{ "artist", ("SaveToTagAndFile", false) },
			};
			Check.True(!AutoMatchTagsDialog.IsOnlyWriteFileModeSelected(mixed), "mixed writeMode -> false");

			var single = new Dictionary<string, (string writeMode, bool overwrite)>
			{
				{ "title", ("SaveToTag", false) },
			};
			Check.True(!AutoMatchTagsDialog.IsOnlyWriteFileModeSelected(single), "single non-SaveToFile -> false");
		});

		yield return ("AutoMatch cancellation: never spawn replacement worker after cancellation", delegate
		{
			Check.True(AutoMatchTagsDialog.ShouldSpawnNextParallelWorker(true, false, true), "normal queued parallel result continues pipeline");
			Check.True(!AutoMatchTagsDialog.ShouldSpawnNextParallelWorker(true, true, true), "cancelled pipeline stops");
			Check.True(!AutoMatchTagsDialog.ShouldSpawnNextParallelWorker(true, false, false), "unqueued result does not spawn");
			Check.True(!AutoMatchTagsDialog.ShouldSpawnNextParallelWorker(false, false, true), "sequential worker does not spawn");
		});

		yield return ("AutoMatch cancellation: wait until active search workers become idle", delegate
		{
			Check.True(AutoMatchTagsDialog.ShouldWaitForActiveWorkersAfterCancellation(true, false), "cancelled with active workers -> wait");
			Check.True(!AutoMatchTagsDialog.ShouldWaitForActiveWorkersAfterCancellation(true, true), "cancelled and idle -> close");
			Check.True(!AutoMatchTagsDialog.ShouldWaitForActiveWorkersAfterCancellation(false, false), "normal path uses queue processing");
		});
	}
}
