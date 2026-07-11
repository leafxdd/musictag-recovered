using System;
using System.IO;
using MusicTag.States;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;

namespace MusicTagWinApp.Instances;

internal sealed class CoverImageProcessingOptions
{
	public long MaxByteLength { get; init; }

	public int MaxResolution { get; init; }

	public string FormatMode { get; init; }

	public int JpegQuality { get; init; } = 85;

	public int PngCompressionLevel { get; init; } = 6;
}

internal static class CoverImageProcessor
{
	private const int MinimumJpegQuality = 20;

	private const double ResizeRatio = 0.9d;

	internal static bool Process(ConfigDescriptorState.PictureData pictureData, CoverImageProcessingOptions options)
	{
		if (pictureData?.ImageBytes == null || pictureData.ImageBytes.Length == 0 || options == null)
		{
			return false;
		}

		string sourceMimeType = NormalizeMimeType(pictureData.MimeType);
		string targetMimeType = ResolveTargetMimeType(options.FormatMode, sourceMimeType);
		bool formatAlreadyMatches = IsOriginalFormatAllowed(options.FormatMode, sourceMimeType, targetMimeType);
		bool byteLimitSatisfied = options.MaxByteLength <= 0L || pictureData.ImageBytes.LongLength <= options.MaxByteLength;
		if (formatAlreadyMatches && byteLimitSatisfied && options.MaxResolution <= 0)
		{
			return true;
		}

		using Image image = Image.Load(pictureData.ImageBytes, out IImageFormat detectedFormat);
		sourceMimeType = NormalizeMimeType(detectedFormat?.DefaultMimeType) ?? sourceMimeType;
		targetMimeType = ResolveTargetMimeType(options.FormatMode, sourceMimeType);
		formatAlreadyMatches = IsOriginalFormatAllowed(options.FormatMode, sourceMimeType, targetMimeType);
		bool resolutionLimitSatisfied = options.MaxResolution <= 0 || Math.Max(image.Width, image.Height) <= options.MaxResolution;
		if (formatAlreadyMatches && byteLimitSatisfied && resolutionLimitSatisfied)
		{
			pictureData.MimeType = sourceMimeType;
			pictureData.Width = image.Width;
			pictureData.Height = image.Height;
			return true;
		}
		if (options.MaxResolution > 0 && Math.Max(image.Width, image.Height) > options.MaxResolution)
		{
			ResizeToMaximumDimension(image, options.MaxResolution);
		}

		int jpegQuality = Math.Clamp(options.JpegQuality, 1, 100);
		int pngCompressionLevel = Math.Clamp(options.PngCompressionLevel, 0, 9);
		while (true)
		{
			byte[] encodedBytes = Encode(image, targetMimeType, jpegQuality, pngCompressionLevel);
			if (options.MaxByteLength <= 0L || encodedBytes.LongLength <= options.MaxByteLength)
			{
				pictureData.ImageBytes = encodedBytes;
				pictureData.MimeType = targetMimeType;
				pictureData.Width = image.Width;
				pictureData.Height = image.Height;
				return true;
			}

			if (targetMimeType == "image/jpeg" && jpegQuality > MinimumJpegQuality)
			{
				jpegQuality = Math.Max(MinimumJpegQuality, jpegQuality - 5);
				continue;
			}
			if (image.Width <= 1 && image.Height <= 1)
			{
				return false;
			}
			ResizeByRatio(image, ResizeRatio);
		}
	}

	private static bool IsOriginalFormatAllowed(string formatMode, string sourceMimeType, string targetMimeType)
	{
		if (string.Equals(formatMode, "AUTO", StringComparison.OrdinalIgnoreCase))
		{
			return sourceMimeType == "image/jpeg" || sourceMimeType == "image/png" || sourceMimeType == "image/gif";
		}
		return string.Equals(sourceMimeType, targetMimeType, StringComparison.OrdinalIgnoreCase);
	}

	private static string ResolveTargetMimeType(string formatMode, string sourceMimeType)
	{
		if (string.Equals(formatMode, "PNG", StringComparison.OrdinalIgnoreCase))
		{
			return "image/png";
		}
		if (string.Equals(formatMode, "JPG", StringComparison.OrdinalIgnoreCase))
		{
			return "image/jpeg";
		}
		if (sourceMimeType == "image/png" || sourceMimeType == "image/jpeg")
		{
			return sourceMimeType;
		}
		return "image/jpeg";
	}

	private static byte[] Encode(Image image, string mimeType, int jpegQuality, int pngCompressionLevel)
	{
		using MemoryStream output = new MemoryStream();
		if (mimeType == "image/png")
		{
			image.Save(output, new PngEncoder
			{
				CompressionLevel = (PngCompressionLevel)pngCompressionLevel
			});
		}
		else
		{
			image.Save(output, new JpegEncoder { Quality = jpegQuality });
		}
		return output.ToArray();
	}

	private static void ResizeToMaximumDimension(Image image, int maximumDimension)
	{
		double ratio = Math.Min((double)maximumDimension / image.Width, (double)maximumDimension / image.Height);
		Resize(image, ratio);
	}

	private static void ResizeByRatio(Image image, double ratio)
	{
		Resize(image, ratio);
	}

	private static void Resize(Image image, double ratio)
	{
		int width = Math.Max(1, (int)Math.Round(image.Width * ratio));
		int height = Math.Max(1, (int)Math.Round(image.Height * ratio));
		if (width == image.Width && height == image.Height)
		{
			if (width > 1)
			{
				width--;
			}
			else if (height > 1)
			{
				height--;
			}
		}
		image.Mutate(context => context.Resize(width, height));
	}

	private static string NormalizeMimeType(string mimeType)
	{
		if (string.Equals(mimeType, "image/jpg", StringComparison.OrdinalIgnoreCase))
		{
			return "image/jpeg";
		}
		return string.IsNullOrWhiteSpace(mimeType) ? null : mimeType.ToLowerInvariant();
	}
}
