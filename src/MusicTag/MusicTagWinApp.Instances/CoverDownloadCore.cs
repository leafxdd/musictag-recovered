using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using MusicTag.Serialization;
using MusicTagWinApp.Listeners;

namespace MusicTagWinApp.Instances;

// 两个搜索弹窗(封面 / 综合 tag)后台封面加载的结果载体。
internal sealed class CoverDownloadOutcome
{
	// false = 该路径已有任务在下载(调用方返回 null,不更新列表)。
	public bool PathReserved;

	// 非 null = 已按目标尺寸缩放的位图;null 时由调用方按 Status 选占位图。
	public Bitmap Bitmap;

	// 仅在实际发起下载后有意义;缓存命中路径保持初值 Error(与原逻辑一致,此时 Bitmap 非 null,调用方不读它)。
	public RemoteTagProviderBase.DownloadStatus Status = RemoteTagProviderBase.DownloadStatus.Error;

	// 每次成功解码(new Bitmap 未抛)都会写入原始尺寸;解码失败保持上一次值(初始 null)。
	public Size? OriginalSize;

	// 实际下载返回的字节数;未下载或下载抛异常时保持 0。
	public long DownloadedBytes;

	public string CoverPath;
}

// 封面后台加载共用核(原 CoverSearchDialog.CoverImageLoader 与 CombinedTagSearchDialog.CoverDownloadFile
// 逐分支相同,收敛于此):解析缓存路径并回写 candidate.LocalCoverPath → 以弹窗的路径集合去重
// (同一路径只允许一个任务,锁对象即各弹窗自己的 HashSet 字段,粒度不变)→ 缓存命中直接解码 →
// 未命中经 CoverDownloader 下载(300s 上限)成功再解码、失败删残档。
// 弹窗差异(占位图选择顺序、失败日志、OriginalSize 回写目标)留在各自调用方。
internal static class CoverDownloadCore
{
	internal static CoverDownloadOutcome LoadOrDownloadCover(CoverSearchResult candidate, HashSet<string> reservedPaths, CancellationTokenSource cancellation, Size targetSize)
	{
		string coverPath = candidate.LocalCoverPath ?? PathFileUtilities.GetCoverCacheFilePath(candidate.CoverUrl);
		candidate.LocalCoverPath = coverPath;
		CoverDownloadOutcome outcome = new CoverDownloadOutcome
		{
			CoverPath = coverPath
		};
		lock (reservedPaths)
		{
			if (reservedPaths.Contains(coverPath))
			{
				return outcome;
			}
			reservedPaths.Add(coverPath);
		}
		outcome.PathReserved = true;
		if (File.Exists(coverPath))
		{
			outcome.Bitmap = DecodeAndResize(coverPath, targetSize, outcome);
		}
		if (outcome.Bitmap == null)
		{
			outcome.Status = DownloadCoverFile(candidate, cancellation, coverPath, outcome);
			if (outcome.Status == RemoteTagProviderBase.DownloadStatus.Success)
			{
				outcome.Bitmap = DecodeAndResize(coverPath, targetSize, outcome);
			}
			else
			{
				DeleteFailedCoverFile(coverPath);
			}
		}
		return outcome;
	}

	internal static Bitmap LoadCachedCoverThumbnail(string coverPath, Size targetSize)
	{
		if (string.IsNullOrWhiteSpace(coverPath) || !File.Exists(coverPath))
		{
			return null;
		}
		return DecodeAndResize(coverPath, targetSize, new CoverDownloadOutcome());
	}

	private static RemoteTagProviderBase.DownloadStatus DownloadCoverFile(CoverSearchResult candidate, CancellationTokenSource cancellation, string coverPath, CoverDownloadOutcome outcome)
	{
		try
		{
			var (downloadStatus, downloadedBytes) = candidate.CoverDownloader(cancellation, coverPath, 300000);
			outcome.DownloadedBytes = downloadedBytes;
			return downloadStatus;
		}
		catch (Exception ex)
		{
			Console.WriteLine("downloadfile fail:" + ex.Message);
			return RemoteTagProviderBase.DownloadStatus.Error;
		}
	}

	private static void DeleteFailedCoverFile(string coverPath)
	{
		try
		{
			if (File.Exists(coverPath))
			{
				File.Delete(coverPath);
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine("deletefile fail:" + ex.Message);
		}
	}

	private static Bitmap DecodeAndResize(string coverPath, Size targetSize, CoverDownloadOutcome outcome)
	{
		try
		{
			if (new FileInfo(coverPath).Length > RemoteTagProviderBase.MaxCoverDownloadBytes)
			{
				throw new InvalidDataException("Cover file exceeds the maximum allowed size.");
			}
			using Bitmap bitmap = new Bitmap(coverPath);
			if ((long)bitmap.Width * bitmap.Height > 64L * 1024L * 1024L)
			{
				throw new InvalidDataException("Cover image dimensions exceed the maximum allowed pixel count.");
			}
			outcome.OriginalSize = bitmap.Size;
			return ImageUtilities.ResizeImageToFit(bitmap, targetSize, centerOnCanvas: true);
		}
		catch (Exception ex)
		{
			Console.WriteLine("decode bitmap fail " + ex.Message);
			return null;
		}
	}
}
