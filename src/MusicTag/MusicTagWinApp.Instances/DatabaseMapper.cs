using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Resources;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using MusicTag.Readers;
using MusicTag.Schemes;
using MusicTag.Services;
using MusicTagWinApp.Containers;
using MusicTagWinApp.Properties;

namespace MusicTagWinApp.Instances;

internal static class DatabaseMapper
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
	
	public static void ShowInformationMessage(string message)
	{
			MessageBox.Show(message, Resources.Information, MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
	}

	public static void ShowErrorMessage(string message)
	{
		MessageBox.Show(message, Resources.PolicyTokenExporter, MessageBoxButtons.OK, MessageBoxIcon.Hand);
	}

	public static bool TrySaveApplicationSettings(bool showErrorMessage = true)
	{
		try
		{
			Settings.Default.Save();
			return true;
		}
		catch (Exception exception)
		{
			if (showErrorMessage)
			{
				ShowErrorMessage(exception.GetMessageChain());
			}
			return false;
		}
	}

	public static bool ConfirmYesNo(string message)
	{
		return MessageBox.Show(message, Resources.Confirmation, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
	}

	public static DialogResult ConfirmYesNoCancel(string message)
	{
		return MessageBox.Show(message, Resources.Confirmation, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
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

	

	

	public static void ShowInExplorer(string path)
	{
		if (!File.Exists(path) && !Directory.Exists(path))
		{
			return;
		}
		try
		{
			if (Directory.Exists(path))
			{
				Process.Start("explorer.exe", "/select,\"" + path + "\"");
				return;
			}
			IntPtr intPtr = NativeMethods.CreateItemIdListFromPath(path);
			if (!(intPtr != IntPtr.Zero))
			{
				return;
			}
			try
			{
				Marshal.ThrowExceptionForHR(NativeMethods.OpenFolderAndSelectItems(intPtr, 0u, IntPtr.Zero, 0u));
			}
			catch (Exception)
			{
				Process.Start("explorer.exe", "/select,\"" + path + "\"");
			}
			finally
			{
				NativeMethods.FreeItemIdList(intPtr);
			}
		}
		catch (Exception ex2)
		{
			ShowErrorMessage("Open folder and select item fail: " + ex2.Message);
		}
	}

	public static void SetTextBoxCueBanner(Control control, string text)
	{
		NativeMethods.SendStringMessage(control.Handle, 5377, IntPtr.Zero, text);
	}

	public static void FillAndCenterButtons(Control listControl, Control mainPanel, Control buttonPanel)
	{
		listControl.Width = mainPanel.Width;
		listControl.Height = mainPanel.Height - buttonPanel.Height - buttonPanel.Margin.Top - buttonPanel.Margin.Bottom;
		int horizontalMargin = (mainPanel.Width - buttonPanel.Width) / 2;
		buttonPanel.Margin = new Padding(horizontalMargin, buttonPanel.Margin.Top, horizontalMargin, buttonPanel.Margin.Bottom);
	}

	static DatabaseMapper()
	{
			startupLogFileName = Program.StartupTime().ToString("yyyy-MM-dd HH_mm_ss") + ".log";
	}

}
