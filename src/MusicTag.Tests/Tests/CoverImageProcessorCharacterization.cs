using System;
using System.Collections.Generic;
using System.IO;
using MusicTag.States;
using MusicTagWinApp.Instances;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace MusicTag.Tests;

internal static class CoverImageProcessorCharacterization
{
	private static byte[] CreatePng(int width, int height, int compressionLevel = 6)
	{
		using Image<Rgba32> image = new Image<Rgba32>(width, height);
		for (int y = 0; y < height; y++)
		{
			for (int x = 0; x < width; x++)
			{
				image[x, y] = new Rgba32((byte)(x % 256), (byte)(y % 256), (byte)((x + y) % 256), (byte)(128 + (x % 128)));
			}
		}
		using MemoryStream output = new MemoryStream();
		image.Save(output, new PngEncoder { CompressionLevel = (PngCompressionLevel)compressionLevel });
		return output.ToArray();
	}

	private static byte[] CreateJpeg(int width, int height, int quality)
	{
		using Image<Rgb24> image = new Image<Rgb24>(width, height);
		for (int y = 0; y < height; y++)
		{
			for (int x = 0; x < width; x++)
			{
				image[x, y] = new Rgb24((byte)(x % 256), (byte)(y % 256), (byte)((x * 3 + y) % 256));
			}
		}
		using MemoryStream output = new MemoryStream();
		image.Save(output, new JpegEncoder { Quality = quality });
		return output.ToArray();
	}

	private static ConfigDescriptorState.PictureData Picture(byte[] bytes, string mimeType)
	{
		return new ConfigDescriptorState.PictureData { ImageBytes = bytes, MimeType = mimeType, PictureType = "Front Cover" };
	}

	private static CoverImageProcessingOptions Options(string format, long maxBytes = long.MaxValue, int maxResolution = 0, int jpegQuality = 85, int pngLevel = 6)
	{
		return new CoverImageProcessingOptions
		{
			FormatMode = format,
			MaxByteLength = maxBytes,
			MaxResolution = maxResolution,
			JpegQuality = jpegQuality,
			PngCompressionLevel = pngLevel
		};
	}

	public static IEnumerable<(string, Action)> All()
	{
		yield return ("CoverImageProcessor AUTO preserves compliant PNG bytes exactly", delegate
		{
			byte[] original = CreatePng(32, 24);
			ConfigDescriptorState.PictureData picture = Picture((byte[])original.Clone(), "image/png");
			Check.True(CoverImageProcessor.Process(picture, Options("AUTO")), "processed");
			Check.Equal(Convert.ToBase64String(original), Convert.ToBase64String(picture.ImageBytes), "bytes unchanged");
			Check.Equal("image/png", picture.MimeType, "mime");
		});

		yield return ("CoverImageProcessor AUTO detects missing MIME without rewriting bytes", delegate
		{
			byte[] original = CreatePng(48, 32);
			ConfigDescriptorState.PictureData picture = Picture((byte[])original.Clone(), null);
			Check.True(CoverImageProcessor.Process(picture, new CoverImageProcessingOptions { FormatMode = "AUTO" }), "processed");
			Check.Equal("image/png", picture.MimeType, "detected MIME");
			Check.True(original.SequenceEqual(picture.ImageBytes), "original bytes preserved");
		});

		yield return ("CoverImageProcessor AUTO resizes PNG and keeps PNG", delegate
		{
			ConfigDescriptorState.PictureData picture = Picture(CreatePng(120, 80), "image/png");
			Check.True(CoverImageProcessor.Process(picture, Options("AUTO", maxResolution: 60)), "processed");
			Check.Equal("image/png", picture.MimeType, "mime");
			Check.Equal(60, picture.Width, "width");
			Check.Equal(40, picture.Height, "height");
			using Image<Rgba32> decoded = Image.Load<Rgba32>(picture.ImageBytes);
			Check.Equal(60, decoded.Width, "decoded width");
		});

		yield return ("CoverImageProcessor forced PNG converts JPEG", delegate
		{
			ConfigDescriptorState.PictureData picture = Picture(CreateJpeg(48, 32, 90), "image/jpeg");
			Check.True(CoverImageProcessor.Process(picture, Options("PNG", pngLevel: 9)), "processed");
			Check.Equal("image/png", picture.MimeType, "mime");
			using Image decoded = Image.Load(picture.ImageBytes);
			Check.Equal(48, decoded.Width, "width");
		});

		yield return ("CoverImageProcessor forced JPEG converts PNG", delegate
		{
			ConfigDescriptorState.PictureData picture = Picture(CreatePng(48, 32), "image/png");
			Check.True(CoverImageProcessor.Process(picture, Options("JPG", jpegQuality: 75)), "processed");
			Check.Equal("image/jpeg", picture.MimeType, "mime");
			using Image decoded = Image.Load(picture.ImageBytes);
			Check.Equal(32, decoded.Height, "height");
		});

		yield return ("CoverImageProcessor PNG compression levels remain lossless", delegate
		{
			byte[] source = CreatePng(96, 96, 0);
			ConfigDescriptorState.PictureData fast = Picture((byte[])source.Clone(), "image/png");
			ConfigDescriptorState.PictureData compact = Picture((byte[])source.Clone(), "image/png");
			Check.True(CoverImageProcessor.Process(fast, Options("PNG", maxResolution: 95, pngLevel: 0)), "level 0");
			Check.True(CoverImageProcessor.Process(compact, Options("PNG", maxResolution: 95, pngLevel: 9)), "level 9");
			using Image<Rgba32> fastImage = Image.Load<Rgba32>(fast.ImageBytes);
			using Image<Rgba32> compactImage = Image.Load<Rgba32>(compact.ImageBytes);
			Check.Equal(fastImage[20, 20], compactImage[20, 20], "same pixel");
			Check.True(compact.ImageBytes.Length <= fast.ImageBytes.Length, "higher compression is not larger");
		});
	}
}
