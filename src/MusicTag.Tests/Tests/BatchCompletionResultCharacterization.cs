using System;
using System.Collections.Generic;
using MusicTag.Schemes;
using MusicTagWinApp.Properties;

namespace MusicTag.Tests;

// FilenameRelatedBatchDialog.BuildBatchCompletionResult characterization。该完成消息构建核由实例方法提取
// (pure-core extraction:计数字段 + batchMessages.ToString() 参数化,原实例方法转发)。原方法所有路径均恰好
// 求值一次 batchMessages.ToString(),故提取为无条件实参不改变求值次数(避开 eager-eval 陷阱)。
// 期望值引用 Resources.Msg_*(与产品同源),锁定分支选择 / 拼接结构 / isErr 三态 / 多文件 format 参数序,
// 而非硬编码语言文本(i18n 安全)。
internal static class BatchCompletionResultCharacterization
{
	public static IEnumerable<(string, Action)> All()
	{
		yield return ("BuildBatchCompletionResult: single file, success -> SaveCompleted, not error", delegate
		{
			var (msg, isErr) = FilenameRelatedBatchDialog.BuildBatchCompletionResult(1, 1, 0, 0, 1, "log");
			Check.Equal(Resources.Msg_SaveCompleted + "\n" + "log", msg, "save completed msg");
			Check.True(!isErr, "not error");
		});

		yield return ("BuildBatchCompletionResult: single file, skipped -> Skipped, not error", delegate
		{
			var (msg, isErr) = FilenameRelatedBatchDialog.BuildBatchCompletionResult(1, 0, 0, 1, 1, "log");
			Check.Equal(Resources.Msg_Skipped + "\n" + "log", msg, "skipped msg");
			Check.True(!isErr, "not error");
		});

		yield return ("BuildBatchCompletionResult: single file, neither success nor skip -> raw messages, isErr=true", delegate
		{
			var (msg, isErr) = FilenameRelatedBatchDialog.BuildBatchCompletionResult(1, 0, 1, 0, 1, "err");
			Check.Equal("err", msg, "raw batch messages");
			Check.True(isErr, "error flag set");
		});

		yield return ("BuildBatchCompletionResult: totalCount 0 counts as single-file path (success)", delegate
		{
			var (msg, isErr) = FilenameRelatedBatchDialog.BuildBatchCompletionResult(0, 2, 0, 0, 0, "log");
			Check.Equal(Resources.Msg_SaveCompleted + "\n" + "log", msg, "0 -> single path");
			Check.True(!isErr, "not error");
		});

		yield return ("BuildBatchCompletionResult: single file, empty messages, all-zero -> empty + isErr", delegate
		{
			var (msg, isErr) = FilenameRelatedBatchDialog.BuildBatchCompletionResult(1, 0, 0, 0, 0, "");
			Check.Equal("", msg, "empty");
			Check.True(isErr, "error flag set");
		});

		yield return ("BuildBatchCompletionResult: multi-file -> OK_Fail_Skip_Count format (arg order success,failure,skip,processed)", delegate
		{
			var (msg, isErr) = FilenameRelatedBatchDialog.BuildBatchCompletionResult(3, 2, 1, 0, 3, "log");
			string expected = string.Format(Resources.Msg_SaveCompleted + "\n" + Resources.Msg_OK_Fail_Skip_Count, 2, 1, 0, 3) + "\n" + "log";
			Check.Equal(expected, msg, "multi-file statistics");
			Check.True(!isErr, "not error");
		});
	}
}
