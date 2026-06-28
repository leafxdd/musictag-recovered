using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using MusicTagWinApp.Containers;
using MusicTagWinApp.Properties;

namespace MusicTagWinApp.Instances;

// 图像缩放 / DPI / 编解码 / 资源位图缓存。原 DatabaseMapper（误名神类）拆分而来（详见 docs/SIMPLIFICATION_PLAN.md Phase 2）。
internal static class ImageUtilities
{
	private static float dpiScale;

	private static Dictionary<string, (string ext, string dlgFilter)> imageMimeMappings;

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

	static ImageUtilities()
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
		resourceImageCache = new Dictionary<string, Bitmap>();
	}
}
