using System;
using System.Collections.Generic;
using System.IO;
using MusicTag.States;
using MusicTagWinApp.Instances;
using MusicTagWinApp.Properties;

namespace MusicTag.Tests;

// PathFileUtilities.BuildLyricFileName characterization(already-testable public static,零产品改动)。
// 依 Settings.Default.SaveLrcFilenameFormat 三分支:Title_Artist / Artist_Title(需 title&artist 均非空,
// 否则 null)、其它值回退音频文件名去扩展 + ".lrc"。tagState 经 ConfigDescriptorState 无参构造 + 索引器注入。
internal static class LyricFileNameCharacterization
{
	private static ConfigDescriptorState Tag(string title, string artist)
	{
		ConfigDescriptorState tag = new ConfigDescriptorState();
		tag["title"] = title;
		tag["artist"] = artist;
		return tag;
	}

	public static IEnumerable<(string, Action)> All()
	{
		yield return ("BuildLyricFileName: Title_Artist -> \"T - A.lrc\"", delegate
		{
			Settings.Default.SaveLrcFilenameFormat = "Title_Artist";
			Check.Equal("T - A.lrc", PathFileUtilities.BuildLyricFileName("x.mp3", Tag("T", "A")), "title-artist");
		});

		yield return ("BuildLyricFileName: Artist_Title -> \"A - T.lrc\"", delegate
		{
			Settings.Default.SaveLrcFilenameFormat = "Artist_Title";
			Check.Equal("A - T.lrc", PathFileUtilities.BuildLyricFileName("x.mp3", Tag("T", "A")), "artist-title");
		});

		yield return ("BuildLyricFileName: Title_Artist with blank artist -> null", delegate
		{
			Settings.Default.SaveLrcFilenameFormat = "Title_Artist";
			Check.Null(PathFileUtilities.BuildLyricFileName("x.mp3", Tag("T", "  ")), "blank artist -> null");
		});

		yield return ("BuildLyricFileName: Title_Artist with blank title -> null", delegate
		{
			Settings.Default.SaveLrcFilenameFormat = "Title_Artist";
			Check.Null(PathFileUtilities.BuildLyricFileName("x.mp3", Tag("", "A")), "blank title -> null");
		});

		yield return ("BuildLyricFileName: other format -> audio filename + .lrc", delegate
		{
			Settings.Default.SaveLrcFilenameFormat = "Filename";
			Check.Equal("song.lrc", PathFileUtilities.BuildLyricFileName("C:\\music\\song.mp3", Tag("T", "A")), "fallback to filename");
		});

		yield return ("BuildLyricFileName: Artist_Title with both null -> null", delegate
		{
			Settings.Default.SaveLrcFilenameFormat = "Artist_Title";
			Check.Null(PathFileUtilities.BuildLyricFileName("x.mp3", Tag(null, null)), "both null -> null");
		});

		yield return ("BuildLyricFileName: metadata path separators and invalid characters are sanitized", delegate
		{
			Settings.Default.SaveLrcFilenameFormat = "Title_Artist";
			Check.Equal(".._.._Track_Name_ - A_B.lrc", PathFileUtilities.BuildLyricFileName("x.mp3", Tag("..\\..\\Track/Name?", "A:B")), "sanitized");
		});

		yield return ("BuildLyricSavePath: sanitized metadata stays inside configured directory", delegate
		{
			string previousDirectory = Settings.Default.SaveLrcDirectory;
			string directory = Path.Combine(Path.GetTempPath(), "mtlrc_" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(directory);
			try
			{
				Settings.Default.SaveLrcDirectory = directory;
				Settings.Default.SaveLrcFilenameFormat = "Title_Artist";
				string result = PathFileUtilities.BuildLyricSavePath(Path.Combine(directory, "song.mp3"), Tag("..\\outside", "Artist"));
				Check.True(result.StartsWith(Path.GetFullPath(directory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "inside directory");
				Check.Equal(".._outside - Artist.lrc", Path.GetFileName(result), "safe filename");
			}
			finally
			{
				Settings.Default.SaveLrcDirectory = previousDirectory;
				Directory.Delete(directory);
			}
		});
	}
}
