using System;
using System.Collections.Generic;
using MusicTag.Consumers;

namespace MusicTagWinApp.Instances;

internal class VgmdbAlbum
{
	public string CatalogNumber { get; set; }

	public string AlbumUrl { get; set; }

	public string ReleaseDate { get; set; }

	public string Category { get; set; }

	public string Classification { get; set; }

	public string CoverImageUrl { get; set; }

	public string DisplayName { get; set; }

	private readonly Dictionary<string, string> albumNames;

	private readonly List<Dictionary<string, string>> performerNames;

	private readonly List<Dictionary<string, string>> composerNames;

	private readonly List<VgmdbDisc> discs;

	public Dictionary<string, string> AlbumNames => albumNames;

	public List<Dictionary<string, string>> PerformerNames => performerNames;

	public List<Dictionary<string, string>> ComposerNames => composerNames;

	public List<VgmdbDisc> Discs => discs;

	public string GetPerformerName(string language)
	{
		foreach (Dictionary<string, string> performer in PerformerNames)
		{
			string performerName = GetLocalizedValue(performer, language);
			if (!string.IsNullOrWhiteSpace(performerName))
			{
				return performerName;
			}
		}

		return "";
	}

	public string[] GetAlbumTitleParts(string language, bool preferDisplayName)
	{
		string albumTitle = "";
		string albumSubtitle = "";
		if (AlbumNames.ContainsKey(language))
		{
			albumTitle = AlbumNames[language];
		}

		if (preferDisplayName)
		{
			if (!string.IsNullOrWhiteSpace(DisplayName))
			{
				albumTitle = DisplayName;
			}
			else
			{
				albumTitle = GetLocalizedValue(AlbumNames, "en");
			}
		}

		int subtitleSeparator = albumTitle.LastIndexOf('/');
		if (subtitleSeparator >= 0)
		{
			string fullTitle = albumTitle;
			albumTitle = albumTitle.Substring(0, subtitleSeparator).Trim();
			try
			{
				albumSubtitle = fullTitle.Substring(subtitleSeparator + 1).Trim();
			}
			catch (System.Exception ex)
			{
				Console.WriteLine("substring error:" + ex.Message);
			}
		}
		return new string[2] { albumTitle, albumSubtitle };
	}

	private static string GetLocalizedValue(Dictionary<string, string> values, string preferredLanguage)
	{
		foreach (string language in new[] { preferredLanguage, "en", "ja", "ja-latn" })
		{
			if (language != null && values.TryGetValue(language, out string value))
			{
				return value;
			}
		}

		return "";
	}

	public VgmdbAlbum()
	{
		albumNames = new Dictionary<string, string>();
		performerNames = new List<Dictionary<string, string>>();
		composerNames = new List<Dictionary<string, string>>();
		discs = new List<VgmdbDisc>();
	}
}

