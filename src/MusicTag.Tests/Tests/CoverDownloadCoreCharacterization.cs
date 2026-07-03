using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using MusicTag.Serialization;
using MusicTagWinApp.Instances;
using MusicTagWinApp.Listeners;

namespace MusicTag.Tests;

// CoverDownloadCore.LoadOrDownloadCover characterization(提取自两个搜索弹窗嵌套类的共用封面
// 下载核,逐分支等价搬移)。CoverDownloader 为可注入 Func;LocalCoverPath 预设临时文件路径,
// 避开真实图片缓存目录(EnsureDirectoryExists)副作用;编解码走真实 GDI+(4x4 PNG)。
internal static class CoverDownloadCoreCharacterization
{
	private static string NewTempPath()
	{
		return Path.Combine(Path.GetTempPath(), "musictag-coverdl-" + Guid.NewGuid().ToString("N") + ".png");
	}

	private static void WritePng(string path, int width, int height)
	{
		using Bitmap bitmap = new Bitmap(width, height);
		bitmap.Save(path, ImageFormat.Png);
	}

	private static void TryDelete(string path)
	{
		try
		{
			File.Delete(path);
		}
		catch
		{
		}
	}

	public static IEnumerable<(string, Action)> All()
	{
		yield return ("LoadOrDownloadCover: cache hit -> decoded+resized, downloader never called, path reserved", delegate
		{
			string path = NewTempPath();
			WritePng(path, 4, 4);
			bool downloaderCalled = false;
			CoverSearchResult candidate = new CoverSearchResult
			{
				LocalCoverPath = path,
				CoverDownloader = delegate
				{
					downloaderCalled = true;
					return (RemoteTagProviderBase.DownloadStatus.Success, 0L);
				}
			};
			HashSet<string> reserved = new HashSet<string>();
			CoverDownloadOutcome outcome = CoverDownloadCore.LoadOrDownloadCover(candidate, reserved, new CancellationTokenSource(), new Size(16, 16));
			Check.True(outcome.PathReserved, "path reserved");
			Check.NotNull(outcome.Bitmap, "bitmap decoded");
			Check.Equal(new Size(16, 16), outcome.Bitmap.Size, "resized to target (equal scale -> exact target)");
			Check.Equal((Size?)new Size(4, 4), outcome.OriginalSize, "original size recorded");
			Check.Equal(false, downloaderCalled, "downloader not called on cache hit");
			Check.True(reserved.Contains(path), "path added to reservation set");
			Check.Equal(path, candidate.LocalCoverPath, "LocalCoverPath kept");
			outcome.Bitmap.Dispose();
			TryDelete(path);
		});

		yield return ("LoadOrDownloadCover: cache miss -> downloader writes file, Success -> decoded, bytes recorded", delegate
		{
			string path = NewTempPath();
			CoverSearchResult candidate = new CoverSearchResult
			{
				LocalCoverPath = path,
				CoverDownloader = delegate(CancellationTokenSource cancellation, string targetPath, int timeout)
				{
					WritePng(targetPath, 4, 4);
					return (RemoteTagProviderBase.DownloadStatus.Success, 123L);
				}
			};
			CoverDownloadOutcome outcome = CoverDownloadCore.LoadOrDownloadCover(candidate, new HashSet<string>(), new CancellationTokenSource(), new Size(16, 16));
			Check.True(outcome.PathReserved, "path reserved");
			Check.Equal(RemoteTagProviderBase.DownloadStatus.Success, outcome.Status, "download success");
			Check.Equal(123L, outcome.DownloadedBytes, "downloaded bytes recorded");
			Check.NotNull(outcome.Bitmap, "bitmap decoded after download");
			Check.Equal((Size?)new Size(4, 4), outcome.OriginalSize, "original size from downloaded file");
			outcome.Bitmap.Dispose();
			TryDelete(path);
		});

		yield return ("LoadOrDownloadCover: downloader NotFound -> leftover file deleted, no bitmap", delegate
		{
			string path = NewTempPath();
			CoverSearchResult candidate = new CoverSearchResult
			{
				LocalCoverPath = path,
				CoverDownloader = delegate(CancellationTokenSource cancellation, string targetPath, int timeout)
				{
					File.WriteAllText(targetPath, "partial");
					return (RemoteTagProviderBase.DownloadStatus.NotFound, 7L);
				}
			};
			CoverDownloadOutcome outcome = CoverDownloadCore.LoadOrDownloadCover(candidate, new HashSet<string>(), new CancellationTokenSource(), new Size(16, 16));
			Check.Equal(RemoteTagProviderBase.DownloadStatus.NotFound, outcome.Status, "status NotFound");
			Check.Null(outcome.Bitmap, "no bitmap");
			Check.Equal(false, File.Exists(path), "failed download leftover deleted");
			Check.Equal(7L, outcome.DownloadedBytes, "bytes recorded even on failure");
		});

		yield return ("LoadOrDownloadCover: path already reserved -> PathReserved=false, downloader not called", delegate
		{
			string path = NewTempPath();
			bool downloaderCalled = false;
			CoverSearchResult candidate = new CoverSearchResult
			{
				LocalCoverPath = path,
				CoverDownloader = delegate
				{
					downloaderCalled = true;
					return (RemoteTagProviderBase.DownloadStatus.Success, 0L);
				}
			};
			HashSet<string> reserved = new HashSet<string> { path };
			CoverDownloadOutcome outcome = CoverDownloadCore.LoadOrDownloadCover(candidate, reserved, new CancellationTokenSource(), new Size(16, 16));
			Check.Equal(false, outcome.PathReserved, "not reserved (duplicate)");
			Check.Null(outcome.Bitmap, "no bitmap");
			Check.Equal(false, downloaderCalled, "downloader not called");
		});

		yield return ("LoadOrDownloadCover: downloader throws -> Status=Error, no bitmap, bytes stay 0", delegate
		{
			string path = NewTempPath();
			CoverSearchResult candidate = new CoverSearchResult
			{
				LocalCoverPath = path,
				CoverDownloader = delegate
				{
					throw new InvalidOperationException("boom");
				}
			};
			CoverDownloadOutcome outcome = CoverDownloadCore.LoadOrDownloadCover(candidate, new HashSet<string>(), new CancellationTokenSource(), new Size(16, 16));
			Check.Equal(RemoteTagProviderBase.DownloadStatus.Error, outcome.Status, "status Error on throw");
			Check.Null(outcome.Bitmap, "no bitmap");
			Check.Equal(0L, outcome.DownloadedBytes, "bytes stay 0 when downloader throws");
		});

		yield return ("LoadOrDownloadCover: corrupt cached file -> falls through to download and succeeds", delegate
		{
			string path = NewTempPath();
			File.WriteAllText(path, "not a png");
			CoverSearchResult candidate = new CoverSearchResult
			{
				LocalCoverPath = path,
				CoverDownloader = delegate(CancellationTokenSource cancellation, string targetPath, int timeout)
				{
					WritePng(targetPath, 4, 4);
					return (RemoteTagProviderBase.DownloadStatus.Success, 55L);
				}
			};
			CoverDownloadOutcome outcome = CoverDownloadCore.LoadOrDownloadCover(candidate, new HashSet<string>(), new CancellationTokenSource(), new Size(16, 16));
			Check.NotNull(outcome.Bitmap, "bitmap from re-download after corrupt cache");
			Check.Equal(RemoteTagProviderBase.DownloadStatus.Success, outcome.Status, "download success");
			Check.Equal(55L, outcome.DownloadedBytes, "bytes recorded");
			outcome.Bitmap.Dispose();
			TryDelete(path);
		});
	}
}
