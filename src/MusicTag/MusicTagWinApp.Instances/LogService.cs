using System;
using System.Globalization;
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
		WriteExceptionLog(FormatExceptionDetails(exception, context));
	}

	// 提取自 WriteExceptionDetails 的纯格式化核(字符串拼接,无 I/O);写日志留在 WriteExceptionDetails。
	// InnerException 优先取其 Type.Name / StackTrace;Message 走 GetMessageChain。逻辑逐字节保持。
	// characterization 见 ExceptionDetailsFormatCharacterization。
	internal static string FormatExceptionDetails(Exception exception, string context = null)
	{
		StringBuilder stringBuilder = new StringBuilder("\r\n");
		string contextText = context != null ? context + ", " : "";
		string exceptionType = exception?.InnerException?.GetType().Name ?? exception?.GetType().Name;
		string stackTrace = exception?.InnerException?.StackTrace ?? exception?.StackTrace;
		stringBuilder.Append("Type: " + contextText + exceptionType + "\r\n");
		stringBuilder.Append("Message: " + exception?.GetMessageChain() + "\r\n");
		stringBuilder.Append("StackTrace: " + stackTrace + "\r\n");
		return stringBuilder.ToString();
	}

	static LogService()
	{
			// 显式行为修正（非纯行为保持）：原 DatabaseMapper 单 cctor 在首次任意成员访问时触发，
		// 实践中早于 StateFieldInstance 的 culture 重置（ApplyLanguageResources），故 startupLogFileName
		// 曾按启动时 OS 区域日历渲染。拆分后本字段改由 LogService cctor 初始化，首次 log 访问发生在
		// culture 重置之后，使非公历默认日历区域（th-TH 泰历等）的日志文件名年份改变。此处固定用
		// InvariantCulture（公历），令渲染与触发时机、与 UI 语言均无关——同时修掉这个潜伏的 i18n 缺陷。
		startupLogFileName = Program.StartupTime().ToString("yyyy-MM-dd HH_mm_ss", CultureInfo.InvariantCulture) + ".log";
	}
}
