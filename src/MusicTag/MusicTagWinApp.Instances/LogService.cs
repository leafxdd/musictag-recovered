using System;
using System.IO;
using System.Text;
using MusicTag.Schemes;
using MusicTag.Services;

namespace MusicTagWinApp.Instances;

// 日志目录解析 + 各类操作日志/异常写入。原 DatabaseMapper（误名神类）拆分而来（详见 docs/SIMPLIFICATION_PLAN.md Phase 2）。
internal static class LogService
{
	private static readonly string startupLogFileName;

	private static string GetLogSubdirectory(string subdirectory)
	{
		return PathFileUtilities.EnsureDirectoryExists(PathFileUtilities.GetApplicationDirectory() + "temp\\Log\\" + subdirectory) + "\\";
	}

	public static string GetSaveTagsLogDirectory() => GetLogSubdirectory("SaveTags");

	public static string GetClearTagsLogDirectory() => GetLogSubdirectory("ClearTags");

	public static string GetSaveLyricsLogDirectory() => GetLogSubdirectory("SaveLrcFiles");

	public static string GetSaveCoversLogDirectory() => GetLogSubdirectory("SaveCovers");

	public static string GetAutoMatchLogDirectory() => GetLogSubdirectory("AutoMatchTags");

	public static string GetRenameLogDirectory() => GetLogSubdirectory("Rename");

	public static string GetExceptionLogDirectory() => GetLogSubdirectory("Exception\\" + ApplicationInfoService.GetFileVersion());

	public static string GetStartupLogFileName()
	{
		return startupLogFileName;
	}

	public static void AppendTextLine(string filePath, string text)
	{
		FileInfo fileInfo = new FileInfo(filePath);
		if (!fileInfo.Exists && !fileInfo.Directory.Exists)
		{
			fileInfo.Directory.Create();
		}
		using StreamWriter streamWriter = new StreamWriter(filePath, append: true);
		streamWriter.WriteLine(text);
	}

	private static void WriteTimestampedLogLine(string filePath, string message)
	{
		message = "[" + DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss") + "]" + message;
		AppendTextLine(filePath, message);
	}

	public static void WriteSaveTagsLog(string message)
	{
		WriteTimestampedLogLine(GetSaveTagsLogDirectory() + GetStartupLogFileName(), message);
	}

	public static void WriteClearTagsLog(string message)
	{
		WriteTimestampedLogLine(GetClearTagsLogDirectory() + GetStartupLogFileName(), message);
	}

	public static void WriteSaveLyricsLog(string message)
	{
		WriteTimestampedLogLine(GetSaveLyricsLogDirectory() + GetStartupLogFileName(), message);
	}

	public static void WriteSaveCoversLog(string message)
	{
		WriteTimestampedLogLine(GetSaveCoversLogDirectory() + GetStartupLogFileName(), message);
	}

	public static void WriteAutoMatchLog(string message)
	{
		WriteTimestampedLogLine(GetAutoMatchLogDirectory() + GetStartupLogFileName(), message);
	}

	public static void WriteRenameLog(string message)
	{
		WriteTimestampedLogLine(GetRenameLogDirectory() + GetStartupLogFileName(), message);
	}

	public static void WriteExceptionLog(string message)
	{
		WriteTimestampedLogLine(GetExceptionLogDirectory() + GetStartupLogFileName(), message);
	}

	public static void WriteExceptionDetails(Exception exception, string context = null)
	{
		StringBuilder stringBuilder = new StringBuilder("\r\n");
		string contextText = context != null ? context + ", " : "";
		string exceptionType = exception?.InnerException?.GetType().Name ?? exception?.GetType().Name;
		string stackTrace = exception?.InnerException?.StackTrace ?? exception?.StackTrace;
		stringBuilder.Append("Type: " + contextText + exceptionType + "\r\n");
		stringBuilder.Append("Message: " + exception?.GetMessageChain() + "\r\n");
		stringBuilder.Append("StackTrace: " + stackTrace + "\r\n");
		WriteExceptionLog(stringBuilder.ToString());
	}

	static LogService()
	{
			startupLogFileName = Program.StartupTime().ToString("yyyy-MM-dd HH_mm_ss") + ".log";
	}
}
