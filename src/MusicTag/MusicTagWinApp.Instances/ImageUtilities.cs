using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
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

	// 把静态启动基线对齐到指定控件句柄所在屏(PMv2 实验分支):从副屏启动时,shell 可以把
	// 主窗体句柄直接建在副屏——Designer/框架布局按句柄屏自洽,而 GetSystemDpi()(桌面 DC)
	// 恒返回主屏刻度,静态 ScaleByDpi 生成的资产(图标、最小宽)会按主屏刻度塞进副屏刻度的
	// 控件树(图标截断/左栏被顶宽)。主窗体构造期调用一次,把静态基线与句柄屏钉齐。
	public static void RefreshDpiScaleCache(Control control)
	{
		if (control != null)
		{
			dpiScale = (float)control.DeviceDpi / 96f;
		}
	}

	// Per-Monitor V2(实验分支):取控件当前所在显示器的缩放(DeviceDpi 随 WM_DPICHANGED 更新)。
	// 构造期布局仍用无参 GetDpiScale()(启动基线;跨屏时框架把整棵控件树按比例重缩放,基线即正确)。
	// 只有"构造后反复执行"的重排/绘制代码(SizeChanged/Paint)必须按当前显示器取值,否则会把启动
	// 基线的像素值重新套回已被框架缩放过的窗体——正是"跨屏拖动后越变越大/错位"的来源。
	public static float GetDpiScale(Control control)
	{
		if (control != null)
		{
			return (float)control.DeviceDpi / 96f;
		}
		return GetDpiScale();
	}

	public static Bitmap ScaleImage(Image image, float scale)
	{
		try
		{
			Size size = new Size((int)Math.Round((float)image.Width * scale), (int)Math.Round((float)image.Height * scale));
			Bitmap bitmap = new Bitmap(size.Width, size.Height);
			Graphics graphics = Graphics.FromImage(bitmap);
			graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
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

	public static Bitmap ResizeImageToFit(Image image, Size targetSize, bool centerOnCanvas = false)
	{
		try
		{
			float widthScale = (float)targetSize.Width / (float)image.Width;
			float heightScale = (float)targetSize.Height / (float)image.Height;
			Size scaledSize = ((widthScale == heightScale) ? targetSize : ((widthScale < heightScale) ? new Size(targetSize.Width, (int)Math.Round((float)image.Height * widthScale)) : new Size((int)Math.Round((float)image.Width * heightScale), targetSize.Height)));
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
			graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
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

	// ToolStrip/MenuItem 会长期持有传入的 Image,且不会替调用方管理共享资源对象的生命周期。
	// 返回始终由调用方独占的副本,便于跨屏重载时安全释放旧位图而不污染 ResourceManager 缓存。
	public static Bitmap LoadOwnedResourceBitmap(string resourceName, bool scaleSmallIconForDpi = false, Control dpiControl = null)
	{
		float currentDpiScale = GetDpiScale(dpiControl);
		Bitmap sourceBitmap = Resources.ResourceManager.GetObject(resourceName + GetResourceScaleSuffix(currentDpiScale)) as Bitmap;
		if (sourceBitmap == null)
		{
			return null;
		}
		if (!scaleSmallIconForDpi || currentDpiScale <= 1f || currentDpiScale >= 1.5f)
		{
			return new Bitmap(sourceBitmap);
		}
		int scaledIconSize = (int)Math.Round(16f * currentDpiScale);
		Size size = new Size(scaledIconSize, scaledIconSize);
		Bitmap scaledBitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
		using Graphics graphics = Graphics.FromImage(scaledBitmap);
		graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
		graphics.CompositingQuality = CompositingQuality.HighQuality;
		graphics.SmoothingMode = SmoothingMode.AntiAlias;
		graphics.DrawImage(sourceBitmap, new Rectangle(Point.Empty, size));
		return scaledBitmap;
	}

	internal static string GetResourceScaleSuffix(float dpiScale)
	{
		return dpiScale >= 1.5f ? "2X" : "";
	}

	// Returns an owned bitmap rendered from the best source resource for the requested DPI.
	// Callers may safely dispose it after ImageList.Images.Add has copied the pixels.
	internal static Bitmap LoadResourceBitmapForDpi(string resourceName, Size size, int dpi)
	{
		float scale = Math.Max(dpi, 1) / 96f;
		Bitmap sourceBitmap = Resources.ResourceManager.GetObject(resourceName + GetResourceScaleSuffix(scale)) as Bitmap;
		if (sourceBitmap == null)
		{
			sourceBitmap = Resources.ResourceManager.GetObject(resourceName) as Bitmap;
		}
		if (sourceBitmap == null)
		{
			return null;
		}
		return new Bitmap(sourceBitmap, size);
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

	// 跨屏 DPI 变更后作废缓存位图,下次按新刻度尺寸重建。只移除、不 Dispose:旧实例可能仍被
	// 控件显示(如 coverPictureBox 的 no_cover 占位图),由调用方的换图路径按引用判断释放。
	public static void EvictCachedResourceBitmap(string resourceName)
	{
		resourceImageCache.Remove(resourceName);
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

	// 见 GetDpiScale(Control):构造后重复执行的重排/绘制用此重载,按控件当前显示器缩放。
	public static int ScaleByDpi(float value, Control control, bool roundUp = false)
	{
		float scaledValue = value * GetDpiScale(control);
		if (!roundUp)
		{
			return (int)scaledValue;
		}
		return (int)Math.Ceiling(scaledValue);
	}

	// Convert a 96-DPI design metric to an absolute device-pixel value. DPI-aware controls
	// should prefer this method (or the Control overload above) over the process-wide startup
	// cache so two windows can remain correct on monitors with different scaling factors.
	internal static int ScaleLogicalPixels(float value, int dpi, bool roundUp = false)
	{
		float scaledValue = value * Math.Max(dpi, 1) / 96f;
		if (!roundUp)
		{
			return (int)scaledValue;
		}
		return (int)Math.Ceiling(scaledValue);
	}

	// 搜索/图片弹窗 ImageList 的统一 DPI 预备(原 4 个弹窗各自内联重复):
	// 清空 → 按当前 DPI 缩放自身 ImageSize → 24 位色 → 透明背景。
	public static void PrepareScaledImageList(ImageList imageList)
	{
		imageList.Images.Clear();
		imageList.ImageSize = new Size(ScaleByDpi(imageList.ImageSize.Width), ScaleByDpi(imageList.ImageSize.Height));
		imageList.ColorDepth = ColorDepth.Depth24Bit;
		imageList.TransparentColor = Color.Transparent;
	}

	// ListView 各列宽按 DPI 缩放(Designer 以 96 DPI 基准写死的列宽,原 5 个弹窗各自内联重复)。
	public static void ScaleColumnWidthsForDpi(ListView listView)
	{
		foreach (ColumnHeader column in listView.Columns)
		{
			column.Width = ScaleByDpi(column.Width);
		}
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
