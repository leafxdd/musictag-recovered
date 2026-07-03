using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using MusicTag.States;

namespace MusicTagWinApp.Instances;

// 三个含封面右键菜单的弹窗(封面搜索 / 综合 tag 搜索 / 从 tag 提取图片)共用的封面动作,
// 原各自私有方法逐字节重复,收敛于此。选中项守卫与图片来源(缓存文件 / tag 内嵌)留在各弹窗调用点。
internal static class CoverImageActions
{
	// 打开:写入图片缓存目录临时文件后交给系统关联程序。
	internal static void OpenCoverImage(ConfigDescriptorState.PictureData pictureData)
	{
		string extension = ImageUtilities.GetImageExtensionForMimeType(pictureData.MimeType, "");
		string tempCoverPath = PathFileUtilities.GetPictureCacheDirectory() + "tempcover" + extension;
		File.WriteAllBytes(tempCoverPath, pictureData.ImageBytes);
		Process.Start(tempCoverPath);
	}

	// 另存为:按 MIME 设置过滤器(未知 MIME 保留上次过滤器)后弹出各弹窗自己的 SaveFileDialog。
	internal static void SaveCoverImageAs(SaveFileDialog saveCoverDialog, ConfigDescriptorState.PictureData pictureData)
	{
		string filter = ImageUtilities.GetImageFileDialogFilterForMimeType(pictureData.MimeType);
		if (!string.IsNullOrWhiteSpace(filter))
		{
			saveCoverDialog.Filter = filter;
		}
		if (saveCoverDialog.ShowDialog() == DialogResult.OK)
		{
			File.WriteAllBytes(saveCoverDialog.FileName, pictureData.ImageBytes);
		}
	}

	// 加载:读缓存文件字节并解码校验(MIME/宽/高全有效)通过才回调;解码出的 Image 用完即弃。
	internal static void LoadAndUseCoverImage(string imagePath, Action<ConfigDescriptorState.PictureData> useImage)
	{
		ConfigDescriptorState.PictureData pictureData = new ConfigDescriptorState.PictureData
		{
			ImageBytes = File.ReadAllBytes(imagePath)
		};
		using (ConfigDescriptorState.LoadPictureImage(pictureData))
		{
			if (pictureData.MimeType != null && pictureData.Width > 0 && pictureData.Height > 0)
			{
				useImage(pictureData);
			}
		}
	}
}
