using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using MusicTagWinApp.Properties;

namespace MusicTag.Tests;

internal static class AppIconCharacterization
{
	private static byte[] LoadEmbeddedIconBytes()
	{
		using Stream stream = typeof(Resources).Assembly.GetManifestResourceStream("MusicTag.AppIcon.ico");
		Check.NotNull(stream, "embedded app icon stream");
		using MemoryStream copy = new MemoryStream();
		stream.CopyTo(copy);
		return copy.ToArray();
	}

	public static IEnumerable<(string, Action)> All()
	{
		yield return ("App icon contains alpha-preserving PNG frames at every standard size", delegate
		{
			byte[] bytes = LoadEmbeddedIconBytes();
			int[] expectedSizes = { 16, 20, 24, 32, 40, 48, 64, 96, 256 };
			Check.Equal(expectedSizes.Length, BitConverter.ToUInt16(bytes, 4), "frame count");
			for (int index = 0; index < expectedSizes.Length; index++)
			{
				int entryOffset = 6 + (16 * index);
				int width = bytes[entryOffset] == 0 ? 256 : bytes[entryOffset];
				int height = bytes[entryOffset + 1] == 0 ? 256 : bytes[entryOffset + 1];
				int imageOffset = (int)BitConverter.ToUInt32(bytes, entryOffset + 12);
				Check.Equal(expectedSizes[index], width, $"frame {index} width");
				Check.Equal(expectedSizes[index], height, $"frame {index} height");
				Check.True(bytes[imageOffset] == 0x89 && bytes[imageOffset + 1] == 0x50 && bytes[imageOffset + 2] == 0x4e && bytes[imageOffset + 3] == 0x47, $"frame {index} uses PNG alpha");
			}
		});

		yield return ("App icon small frame retains transparent and antialiased pixels", delegate
		{
			byte[] bytes = LoadEmbeddedIconBytes();
			using MemoryStream stream = new MemoryStream(bytes, writable: false);
			using Icon icon = new Icon(stream, new Size(16, 16));
			using Bitmap bitmap = icon.ToBitmap();
			int transparentPixels = 0;
			int partialAlphaPixels = 0;
			for (int y = 0; y < bitmap.Height; y++)
			{
				for (int x = 0; x < bitmap.Width; x++)
				{
					byte alpha = bitmap.GetPixel(x, y).A;
					if (alpha == 0) transparentPixels++;
					else if (alpha < 255) partialAlphaPixels++;
				}
			}
			Check.True(transparentPixels > 0, "transparent background pixels");
			Check.True(partialAlphaPixels > 0, "antialiased edge pixels");
		});
	}
}
