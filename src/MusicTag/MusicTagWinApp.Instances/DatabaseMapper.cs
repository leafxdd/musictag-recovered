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
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Windows.Forms;
using MusicTag.Readers;
using MusicTag.Schemes;
using MusicTag.Services;
using MusicTag.States;
using MusicTagWinApp.Containers;
using MusicTagWinApp.Properties;

namespace MusicTagWinApp.Instances;

internal static class DatabaseMapper
{
	private static float dpiScale;
	
	private static Dictionary<string, (string ext, string dlgFilter)> imageMimeMappings;
	
	private static readonly string startupLogFileName;
	
	private static Dictionary<string, Bitmap> resourceImageCache;
	
	public static float GetDpiScale()
	{
		if (dpiScale > 0f)
		{
			return dpiScale;
		}
		return dpiScale = GetSystemDpi().Width / 96f;
	}

	public static string FormatFileSize(long byteCount)
	{
		if (byteCount >= 1000L)
		{
			if (byteCount < 1024000L)
			{
				return $"{(double)byteCount / 1024.0:N}KB";
			}
			if (byteCount < 1048576000L)
			{
				return $"{(double)byteCount / 1024.0 / 1024.0:N}MB";
			}
			return $"{(double)byteCount / 1024.0 / 1024.0 / 1024.0:N}GB";
		}
		return $"{byteCount}Byte";
	}

	public static string TrimNonEmptyLines(string text)
	{
		if (text != null)
		{
			string[] lines = text.Split(new char[1] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
			StringBuilder trimmedText = new StringBuilder();
			foreach (string line in lines)
			{
				if (trimmedText.Length > 0)
				{
					trimmedText.AppendLine();
				}
				trimmedText.Append(line.Trim());
			}
			return trimmedText.ToString();
		}
		return null;
	}

	public static string DecodeBase64String(string encodedText, string encodingName = "UniCode")
	{
		try
		{
			byte[] bytes = Convert.FromBase64String(encodedText);
			return Encoding.GetEncoding(encodingName).GetString(bytes);
		}
		catch
		{
			return encodedText;
		}
	}

	public static byte[] ComputeMd5Hash(byte[] bytes)
	{
		return new MD5CryptoServiceProvider().ComputeHash(bytes);
	}
	
	public static string ComputeMd5HashString(byte[] bytes)
	{
		return BitConverter.ToString(ComputeMd5Hash(bytes));
	}
	
	public static string ComputeMd5HashString(string text, string encodingName = "UniCode")
	{
		byte[] bytes = Encoding.GetEncoding(encodingName).GetBytes(text);
		return BitConverter.ToString(new MD5CryptoServiceProvider().ComputeHash(bytes));
	}

	public static Bitmap ScaleImage(Image image, float scale, InterpolationMode interpolationMode = InterpolationMode.HighQualityBicubic)
	{
		try
		{
			Size size = new Size((int)Math.Round((float)image.Width * scale), (int)Math.Round((float)image.Height * scale));
			Bitmap bitmap = new Bitmap(size.Width, size.Height);
			Graphics graphics = Graphics.FromImage(bitmap);
			graphics.InterpolationMode = interpolationMode;
			graphics.DrawImage(image, new Rectangle(new Point(0, 0), size), new Rectangle(new Point(0, 0), image.Size), GraphicsUnit.Pixel);
			graphics.Dispose();
			return bitmap;
		}
		catch (Exception ex)
		{
			Console.WriteLine("resize bitmap fail " + ex.Message);
			return null;
		}
	}

	public static Bitmap ResizeImageToFit(Image image, Size targetSize, bool centerOnCanvas = false, InterpolationMode interpolationMode = InterpolationMode.HighQualityBicubic)
	{
		try
		{
			float widthScale = (float)targetSize.Width / (float)image.Width;
			float heightScale = (float)targetSize.Height / (float)image.Height;
			Size scaledSize = ((widthScale == heightScale) ? targetSize : ((!(widthScale < heightScale)) ? new Size((int)Math.Round((float)image.Width * heightScale), targetSize.Height) : new Size(targetSize.Width, (int)Math.Round((float)image.Height * widthScale))));
			Rectangle destRect;
			Bitmap bitmap = default(Bitmap);
			if (centerOnCanvas)
			{
				destRect = new Rectangle(new Point((targetSize.Width - scaledSize.Width) / 2, (targetSize.Height - scaledSize.Height) / 2), scaledSize);
				bitmap = new Bitmap(targetSize.Width, targetSize.Height);
			}
			else
			{
				destRect = new Rectangle(new Point(0, 0), scaledSize);
				bitmap = new Bitmap(scaledSize.Width, scaledSize.Height);
			}
			Graphics graphics = Graphics.FromImage(bitmap);
			graphics.InterpolationMode = interpolationMode;
			graphics.DrawImage(image, destRect, new Rectangle(new Point(0, 0), image.Size), GraphicsUnit.Pixel);
			graphics.Dispose();
			return bitmap;
		}
		catch (Exception ex)
		{
			Console.WriteLine("resize bitmap fail " + ex.Message);
			return null;
		}
	}

	public static void SaveJpeg(Image image, Stream output, long quality, InterpolationMode interpolationMode = InterpolationMode.HighQualityBicubic)
	{
		EncoderParameters encoderParameters = new EncoderParameters(1);
		encoderParameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
		ImageCodecInfo encoder = GetImageEncoderByMimeType("image/jpeg");
		image.Save(output, encoder, encoderParameters);
	}

	public static byte[] EncodeJpeg(Image image, long quality, InterpolationMode interpolationMode = InterpolationMode.HighQualityBicubic)
	{
		try
		{
			using MemoryStream memoryStream = new MemoryStream();
			SaveJpeg(image, memoryStream, quality, interpolationMode);
			return memoryStream.ToArray();
		}
		catch (Exception ex)
		{
			Console.WriteLine("BitmapToJpeg fail " + ex.Message);
			return null;
		}
	}

	public static ImageCodecInfo GetImageEncoderByMimeType(string mimeType)
	{
		return ImageCodecInfo.GetImageEncoders().First(encoder => encoder.MimeType == mimeType);
	}

	public static ImageCodecInfo GetImageDecoderByFormatId(Guid formatId)
	{
		return ImageCodecInfo.GetImageDecoders().First(decoder => decoder.FormatID == formatId);
	}

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

	public static string UrlEncodeUtf8(string text)
	{
		return HttpUtility.UrlEncode(text, Encoding.UTF8).Replace("+", "%20");
	}

	public static DateTime UnixMillisecondsToDateTime(long unixMilliseconds)
	{
		return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(unixMilliseconds);
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

	public static string GetUndoTempDirectory()
	{
		return EnsureDirectoryExists(GetApplicationDirectory() + "temp\\Undo") + "\\";
	}

	public static string GetUndoTempDirectoryPath()
	{
		return GetApplicationDirectory() + "temp\\Undo\\";
	}

	public static string GetSaveTagsLogDirectory()
	{
		return EnsureDirectoryExists(GetApplicationDirectory() + "temp\\Log\\SaveTags") + "\\";
	}

	public static string GetClearTagsLogDirectory()
	{
		return EnsureDirectoryExists(GetApplicationDirectory() + "temp\\Log\\ClearTags") + "\\";
	}

	public static string GetSaveLyricsLogDirectory()
	{
		return EnsureDirectoryExists(GetApplicationDirectory() + "temp\\Log\\SaveLrcFiles") + "\\";
	}

	public static string GetSaveCoversLogDirectory()
	{
		return EnsureDirectoryExists(GetApplicationDirectory() + "temp\\Log\\SaveCovers") + "\\";
	}

	public static string GetAutoMatchLogDirectory()
	{
		return EnsureDirectoryExists(GetApplicationDirectory() + "temp\\Log\\AutoMatchTags") + "\\";
	}

	public static string GetRenameLogDirectory()
	{
		return EnsureDirectoryExists(GetApplicationDirectory() + "temp\\Log\\Rename") + "\\";
	}

	public static string GetExceptionLogDirectory()
	{
		return EnsureDirectoryExists(GetApplicationDirectory() + "temp\\Log\\Exception\\" + ApplicationInfoService.GetFileVersion()) + "\\";
	}

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

	public static string GetImageExtensionForMimeType(string mimeType, string fallbackExtension)
	{
			if (imageMimeMappings.TryGetValue(mimeType, out (string, string) value))
		{
			return value.Item1;
		}
		return fallbackExtension;
	}
	
	public static string GetImageFileDialogFilterForMimeType(string mimeType)
	{
			if (imageMimeMappings.TryGetValue(mimeType, out (string, string) value))
		{
			return value.Item2;
		}
		return "";
	}

	public static string GetSiblingPathWithExtension(string filePath, string extension)
	{
		return Path.GetDirectoryName(filePath) + "\\" + Path.GetFileNameWithoutExtension(filePath) + extension;
	}
	
	public static string FindExistingSiblingImageFile(string filePath)
	{
		string text = default(string);
				foreach (var value in imageMimeMappings.Values)
		{
			string item = value.ext;
			text = GetSiblingPathWithExtension(filePath, item);
			if (File.Exists(text))
			{
				return text;
			}
		}
		return null;
	}

	public static int GetWebSearchResultLimit()
	{
		int webSearchItemsLimit = Settings.Default.WebSearchItemsLimit;
		if (webSearchItemsLimit <= 99 && webSearchItemsLimit > 0)
		{
			return webSearchItemsLimit;
		}
		return int.MaxValue;
	}

	public static Icon GetSmallFileIcon(string filePath)
	{
		NativeMethods.ShellFileInfo fileInfo = default(NativeMethods.ShellFileInfo);
		NativeMethods.GetShellFileInfo(filePath, 0u, ref fileInfo, (uint)Marshal.SizeOf(fileInfo), 257u);
		return Icon.FromHandle(fileInfo.IconHandle);
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

	public static string CoalesceNonBlank(string value, string fallback = "")
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			return value;
		}
		return fallback;
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

	public static string GetMessageChain(this Exception exception)
	{
		StringBuilder exceptionMessages = new StringBuilder();
		if (exception is AggregateException aggregateException)
		{
			foreach (Exception innerException in aggregateException.InnerExceptions)
			{
				if (exceptionMessages.Length > 0)
				{
					exceptionMessages.Append(";");
				}
				exceptionMessages.Append(innerException.Message);
			}
		}
		if (exceptionMessages.Length == 0)
		{
			exceptionMessages.Append(exception.InnerException?.Message ?? exception.Message);
		}
		return exceptionMessages.ToString();
	}

	public static string GetStringRespectingUtf16Bom(this Encoding encoding, byte[] bytes)
	{
		if (encoding.HeaderName.Equals("UTF-16", StringComparison.OrdinalIgnoreCase) && bytes.Length >= 2)
		{
			if (bytes[0] == byte.MaxValue && bytes[1] == 254)
			{
				return Encoding.GetEncoding("UTF-16LE").GetString(bytes.Skip(2).ToArray());
			}
			if (bytes[0] == 254 && bytes[1] == byte.MaxValue)
			{
				return Encoding.GetEncoding("UTF-16BE").GetString(bytes.Skip(2).ToArray());
			}
		}
		return encoding.GetString(bytes);
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

	public static SizeF GetSystemDpi()
	{
		Graphics graphics = Graphics.FromHwnd(IntPtr.Zero);
		try
		{
			return new SizeF(graphics.DpiX, graphics.DpiY);
		}
		finally
		{
			if (graphics != null)
			{
					graphics.Dispose();
			}
		}
	}

	public static Bitmap LoadResourceBitmap(string resourceName, bool scaleSmallIconForDpi = false)
	{
			Bitmap bitmap = Resources.ResourceManager.GetObject(resourceName + ((GetDpiScale() >= 1.5f) ? "2X" : "")) as Bitmap;
		if (!scaleSmallIconForDpi || GetDpiScale() <= 1f || GetDpiScale() >= 1.5f)
		{
			return bitmap;
		}
		Size size = new Size(ScaleByDpi(16f), ScaleByDpi(16f));
		Bitmap bitmap2 = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
		Graphics graphics = Graphics.FromImage(bitmap2);
		graphics.InterpolationMode = InterpolationMode.Bicubic;
		graphics.CompositingQuality = CompositingQuality.HighQuality;
		graphics.SmoothingMode = SmoothingMode.AntiAlias;
		graphics.DrawImage(bitmap, new Rectangle(Point.Empty, size));
		return bitmap2;
	}

	public static Bitmap ResizeBitmapIfNeeded(Bitmap bitmap, Size size)
	{
		if (bitmap.Width == size.Width && bitmap.Height == size.Height)
		{
			return bitmap;
		}
		return new Bitmap(bitmap, size);
	}

	public static Bitmap LoadResourceBitmap(string resourceName, Size size)
	{
		return ResizeBitmapIfNeeded(LoadResourceBitmap(resourceName), size);
	}

	public static Bitmap LoadCachedResourceBitmap(string resourceName, Size size)
	{
			if (resourceImageCache.TryGetValue(resourceName, out var value))
		{
			return value;
		}
			return resourceImageCache[resourceName] = LoadResourceBitmap(resourceName, size);
	}

	public static int ScaleByDpi(float value, bool roundUp = false)
	{
			float scaledValue = value * GetDpiScale();
		if (!roundUp)
		{
			return (int)scaledValue;
		}
		return (int)Math.Ceiling(scaledValue);
	}

	public static bool ContainsChinese(string text)
	{
		return Regex.Match(text, "[\\u4e00-\\u9fa5]").Success;
	}

	static DatabaseMapper()
	{
			imageMimeMappings = new Dictionary<string, (string, string)>
		{
			{
				"image/jpeg",
				(".jpg", "jpg|*.jpg")
			},
			{
				"image/png",
				(".png", "png|*.png")
			},
			{
				"image/bmp",
				(".bmp", "bmp|*.bmp")
			},
			{
				"image/gif",
				(".gif", "gif|*.gif")
			}
		};
			startupLogFileName = Program.StartupTime().ToString("yyyy-MM-dd HH_mm_ss") + ".log";
			resourceImageCache = new Dictionary<string, Bitmap>();
	}

}
