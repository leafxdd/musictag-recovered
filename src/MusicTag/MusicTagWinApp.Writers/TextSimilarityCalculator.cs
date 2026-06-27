using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace MusicTagWinApp.Writers;

internal class TextSimilarityCalculator
{
	private static readonly Regex ArtistSeparatorRegex = new Regex("[/&,]|，|、");

	private static int CalculateEditDistance(string source, string target)
	{
		int sourceLength = source.Length;
		int targetLength = target.Length;
		if (sourceLength == 0)
		{
			return targetLength;
		}

		if (targetLength == 0)
		{
			return sourceLength;
		}

		int[,] distances = new int[sourceLength + 1, targetLength + 1];
		for (int sourceIndex = 0; sourceIndex <= sourceLength; sourceIndex++)
		{
			distances[sourceIndex, 0] = sourceIndex;
		}

		for (int targetIndex = 0; targetIndex <= targetLength; targetIndex++)
		{
			distances[0, targetIndex] = targetIndex;
		}

		for (int sourceIndex = 1; sourceIndex <= sourceLength; sourceIndex++)
		{
			for (int targetIndex = 1; targetIndex <= targetLength; targetIndex++)
			{
				int substitutionCost = char.ToUpperInvariant(source[sourceIndex - 1]) == char.ToUpperInvariant(target[targetIndex - 1]) ? 0 : 1;
				distances[sourceIndex, targetIndex] = Math.Min(
					Math.Min(distances[sourceIndex - 1, targetIndex] + 1, distances[sourceIndex, targetIndex - 1] + 1),
					distances[sourceIndex - 1, targetIndex - 1] + substitutionCost);
			}
		}

		return distances[sourceLength, targetLength];
	}

	public static float CalculateTextSimilarity(string reference, string candidate)
	{
		int maxLength = Math.Max(reference.Length, candidate.Length);
		if (maxLength <= 0)
		{
			return 0f;
		}
		return 1f - (float)CalculateEditDistance(reference, candidate) / (float)maxLength;
	}

	public static float CalculateArtistSimilarity(string referenceArtist, string candidateArtist)
	{
		float similarity = CalculateTextSimilarity(referenceArtist, candidateArtist);
		if (similarity >= 0.6f)
		{
			return similarity;
		}

		try
		{
			List<string> artistParts = ArtistSeparatorRegex.Split(candidateArtist).ToList();
			if (artistParts.Count <= 1)
			{
				return similarity;
			}

			artistParts.RemoveAll(string.IsNullOrWhiteSpace);
			if (artistParts.Count <= 1)
			{
				return similarity;
			}

			foreach (string artistPart in artistParts)
			{
				similarity = Math.Max(CalculateTextSimilarity(referenceArtist, artistPart) - 0.01f, similarity);
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine("getSimilarityRadio error:" + ex.Message);
		}

		return similarity;
	}
}
