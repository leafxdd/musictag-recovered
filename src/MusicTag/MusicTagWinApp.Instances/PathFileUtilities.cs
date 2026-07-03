using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using MusicTag.States;
using MusicTagWinApp.Properties;

namespace MusicTagWinApp.Instances;

// 路径 / 目录 / 文件操作 + 歌词存盘路径推导。原 DatabaseMapper（误名神类）拆分而来（详见 docs/SIMPLIFICATION_PLAN.md Phase 2）。
internal static class PathFileUtilities
{
	public static void DeleteOldestFilesUpToSize(string directoryPath, string preservedPath, long bytesToDelete)
	{
		try
		{
			List<string> filePaths = Directory.GetFiles(directoryPath).ToList();
			filePaths.Sort(CompareFileLastWriteTime);
			string preservedFullPath = !string.IsNullOrWhiteSpace(preservedPath) ? Path.GetFullPath(preservedPath) : null;
			foreach (string filePath in filePaths)
			{
				FileInfo fileInfo = new FileInfo(filePath);
				if (bytesToDelete <= 0L || (preservedFullPath != null && preservedFullPath == fileInfo.FullName))
				{
					break;
				}
				bytesToDelete -= fileInfo.Length;
				File.Delete(filePath);
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine("delete files " + directoryPath + " error:" + ex.Message);
		}
	}

	public static void TrimDirectorySize(string directoryPath, string preservedPath, long targetSizeBytes, long maxSizeBytes)
	{
		try
		{
			long totalSize = 0L;
			foreach (string fileName in Directory.GetFiles(directoryPath))
			{
				totalSize += new FileInfo(fileName).Length;
			}
			if (totalSize > maxSizeBytes)
			{
				long bytesToDelete = (long)Math.Floor((double)(totalSize - targetSizeBytes) / (double)targetSizeBytes) * targetSizeBytes;
				DeleteOldestFilesUpToSize(directoryPath, preservedPath, bytesToDelete);
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine("deletefiles " + directoryPath + " fail, error:" + ex.Message);
		}
	}

	private static int CompareFileLastWriteTime(string leftPath, string rightPath)
	{
		DateTime leftLastWriteTime = new FileInfo(leftPath).LastWriteTime;
		DateTime rightLastWriteTime = new FileInfo(rightPath).LastWriteTime;
		return leftLastWriteTime.CompareTo(rightLastWriteTime);
	}

	public static string EnsureDirectoryExists(string directoryPath)
	{
		if (!Directory.Exists(directoryPath))
		{
			Directory.CreateDirectory(directoryPath);
		}
		return directoryPath;
	}

	public static string GetApplicationDirectory()
	{
		return Path.GetDirectoryName(Application.ExecutablePath) + "\\";
	}

	public static string GetPictureCacheDirectory()
	{
		return EnsureDirectoryExists(GetApplicationDirectory() + "temp\\PictureCache") + "\\";
	}

	// 封面 URL 的本地缓存文件全路径:图片缓存目录(确保存在)+ MD5 十六进制(去连字符)文件名。
	// 两个搜索弹窗(封面 / 综合 tag)共用,原各自内联同一表达式。
	public static string GetCoverCacheFilePath(string coverUrl)
	{
		return GetPictureCacheDirectory() + TextUtilities.ComputeMd5HashString(coverUrl).Replace("-", "");
	}

	public static string GetUndoTempDirectory()
	{
		return EnsureDirectoryExists(GetApplicationDirectory() + "temp\\Undo") + "\\";
	}

	public static string GetUndoTempDirectoryPath()
	{
		return GetApplicationDirectory() + "temp\\Undo\\";
	}

	public static string GetSiblingPathWithExtension(string filePath, string extension)
	{
		return Path.GetDirectoryName(filePath) + "\\" + Path.GetFileNameWithoutExtension(filePath) + extension;
	}

	public static void MoveFileAllowingCaseOnlyRename(string sourcePath, string destinationPath)
	{
		if (string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase) && !string.Equals(sourcePath, destinationPath, StringComparison.Ordinal))
		{
			string destinationDirectory = Path.GetDirectoryName(destinationPath);
			if (string.IsNullOrWhiteSpace(destinationDirectory))
			{
				// 拒绝相对路径:否则临时文件会落到当前工作目录而非源文件所在目录。
				throw new ArgumentException("Case-only rename requires an absolute destination path.", nameof(destinationPath));
			}
			string tempPath;
			do
			{
				tempPath = Path.Combine(destinationDirectory, Path.GetRandomFileName());
			}
			while (File.Exists(tempPath));

			File.Move(sourcePath, tempPath);
			try
			{
				File.Move(tempPath, destinationPath);
			}
			catch
			{
				// 第二步失败时把文件回滚到原名,避免遗留为随机临时名导致原文件名永久丢失。
				try
				{
					File.Move(tempPath, sourcePath);
				}
				catch
				{
				}
				throw;
			}
			return;
		}

		File.Move(sourcePath, destinationPath);
	}

	public static string GetLyricSaveDirectory(string audioFilePath)
	{
		string text = Settings.Default.SaveLrcDirectory.Trim();
		if (!string.IsNullOrWhiteSpace(text) && Directory.Exists(text))
		{
			return text;
		}
		return Path.GetDirectoryName(audioFilePath);
	}

	public static string GetLyricSaveDirectoryDisplayName()
	{
		string configuredDirectory = Settings.Default.SaveLrcDirectory.Trim();
		if (!string.IsNullOrWhiteSpace(configuredDirectory) && Directory.Exists(configuredDirectory))
		{
			return configuredDirectory;
		}
		return Resources.Msg_TheLocalDir;
	}

	public static string BuildLyricFileName(string audioFilePath, ConfigDescriptorState tagState)
	{
		string saveLrcFilenameFormat = Settings.Default.SaveLrcFilenameFormat;
		if (saveLrcFilenameFormat == "Title_Artist")
		{
			if (tagState["title"] is string title && !string.IsNullOrWhiteSpace(title) && tagState["artist"] is string artist && !string.IsNullOrWhiteSpace(artist))
			{
				return title + " - " + artist + ".lrc";
			}
			return null;
		}
		if (saveLrcFilenameFormat == "Artist_Title")
		{
			if (tagState["title"] is string title && !string.IsNullOrWhiteSpace(title) && tagState["artist"] is string artist && !string.IsNullOrWhiteSpace(artist))
			{
				return artist + " - " + title + ".lrc";
			}
			return null;
		}
		return Path.GetFileNameWithoutExtension(audioFilePath) + ".lrc";
	}

	public static string BuildLyricSavePath(string audioFilePath, ConfigDescriptorState tagState)
	{
		string text;
		if ((text = BuildLyricFileName(audioFilePath, tagState)) == null)
		{
			return null;
		}
			return GetLyricSaveDirectory(audioFilePath) + "\\" + text;
	}

	public static string BuildLyricSavePath(string audioFilePath, string title, string artist)
	{
		ConfigDescriptorState configDescriptorState = new ConfigDescriptorState();
		configDescriptorState["title"] = title;
		configDescriptorState["artist"] = artist;
		return BuildLyricSavePath(audioFilePath, configDescriptorState);
	}

	public static string FindExistingLyricFile(string audioFilePath, ConfigDescriptorState tagState, bool allowLocalFallback)
	{
			string proxy = BuildLyricSavePath(audioFilePath, tagState);
		if (proxy != null && File.Exists(proxy))
		{
			return proxy;
		}
		if (allowLocalFallback && !string.IsNullOrWhiteSpace(Settings.Default.SaveLrcDirectory))
		{
				string text = GetSiblingPathWithExtension(audioFilePath, ".lrc");
			if (text != null && File.Exists(text))
			{
				return text;
			}
		}
		return null;
	}

	public static void ClearReadOnlyIfAllowed(FileInfo fileInfo, bool allowChange)
	{
		if (!allowChange)
		{
			return;
		}
		try
		{
			if (fileInfo.Exists && fileInfo.IsReadOnly)
			{
					fileInfo.IsReadOnly = false;
			}
		}
			catch (Exception exception)
			{
				Console.WriteLine("CancelFileReadonly " + exception.GetMessageChain());
			}
	}
}
