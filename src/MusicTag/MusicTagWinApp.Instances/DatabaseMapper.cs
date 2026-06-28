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

	public static void SaveJpeg(Image image, Stream output, long quality)
	{
		EncoderParameters encoderParameters = new EncoderParameters(1);
		encoderParameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
		ImageCodecInfo encoder = GetImageEncoderByMimeType("image/jpeg");
		image.Save(output, encoder, encoderParameters);
	}

	public static byte[] EncodeJpeg(Image image, long quality)
	{
		try
		{
			using MemoryStream memoryStream = new MemoryStream();
			SaveJpeg(image, memoryStream, quality);
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

	
	public static string FindExistingSiblingImageFile(string filePath)
	{
		string text = default(string);
				foreach (var value in imageMimeMappings.Values)
		{
			string item = value.ext;
			text = PathFileUtilities.GetSiblingPathWithExtension(filePath, item);
			if (File.Exists(text))
			{
				return text;
			}
		}
		return null;
	}

	public static Icon GetSmallFileIcon(string filePath)
	{
		NativeMethods.ShellFileInfo fileInfo = default(NativeMethods.ShellFileInfo);
		NativeMethods.GetShellFileInfo(filePath, 0u, ref fileInfo, (uint)Marshal.SizeOf(fileInfo), 257u);
		if (fileInfo.IconHandle == IntPtr.Zero)
		{
			return (Icon)SystemIcons.WinLogo.Clone();
		}
		try
		{
			using Icon icon = Icon.FromHandle(fileInfo.IconHandle);
			return (Icon)icon.Clone();
		}
		finally
		{
			NativeMethods.DestroyIcon(fileInfo.IconHandle);
		}
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
		Bitmap scaledBitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
		using Graphics graphics = Graphics.FromImage(scaledBitmap);
		graphics.InterpolationMode = InterpolationMode.Bicubic;
		graphics.CompositingQuality = CompositingQuality.HighQuality;
		graphics.SmoothingMode = SmoothingMode.AntiAlias;
		graphics.DrawImage(bitmap, new Rectangle(Point.Empty, size));
		return scaledBitmap;
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

	public static void FillAndCenterButtons(Control listControl, Control mainPanel, Control buttonPanel)
	{
		listControl.Width = mainPanel.Width;
		listControl.Height = mainPanel.Height - buttonPanel.Height - buttonPanel.Margin.Top - buttonPanel.Margin.Bottom;
		int horizontalMargin = (mainPanel.Width - buttonPanel.Width) / 2;
		buttonPanel.Margin = new Padding(horizontalMargin, buttonPanel.Margin.Top, horizontalMargin, buttonPanel.Margin.Bottom);
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
