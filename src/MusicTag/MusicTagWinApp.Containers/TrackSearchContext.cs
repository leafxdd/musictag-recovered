using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using MusicTag.States;
using MusicTagWinApp.Instances;
using MusicTagWinApp.Properties;
using Newtonsoft.Json.Linq;

namespace MusicTagWinApp.Containers;

internal class TrackSearchContext
{
	public string FilePath { get; }

	public string Title { get; }

	public string Artist { get; }

	public string Album { get; }

	public string DurationMillisecondsText { get; }

	public bool UsedFileNameForTitle { get; private set; }

	public ConfigDescriptorState TagState { get; }

	public (long musicId, string title, string artist, string album, string picUrl) LinkedMusicMetadata { get; private set; }

	public TrackSearchContext(ConfigDescriptorState tagState, string title, string artist, string album, string comment = null)
	{
		TagState = tagState;
		FilePath = tagState.GetFilePath();

		ApplyLinkedMusicMetadata(comment, ref title, ref artist, ref album);

		Artist = Settings.Default.SearchCondition_UseArtist ? artist.Trim() : "";
		Album = Settings.Default.SearchCondition_UseAlbum ? album.Trim() : "";
		DurationMillisecondsText = tagState["durationinms"].ToString();
		Title = ResolveSearchTitle(title);
	}

	public TrackSearchContext(ConfigDescriptorState tagState)
		: this(tagState, tagState.GetDisplayValue("title"), tagState.GetDisplayValue("artist"), tagState.GetDisplayValue("album"), tagState.GetDisplayValue("comment"))
	{
	}

	public bool HasTitle()
	{
		return !string.IsNullOrWhiteSpace(Title);
	}

	private void ApplyLinkedMusicMetadata(string comment, ref string title, ref string artist, ref string album)
	{
		if (string.IsNullOrWhiteSpace(comment))
		{
			return;
		}

		string decodedComment = ConfigDescriptorState.ReadAndFreeNativeString(DecodeMusicComment(comment));
		if (string.IsNullOrWhiteSpace(decodedComment))
		{
			return;
		}

		Match match = Regex.Match(decodedComment, "music:(\\{[\\s\\S]+\\})[^}]*");
		if (!match.Success || string.IsNullOrWhiteSpace(match.Groups[1].Value))
		{
			return;
		}

		try
		{
			JObject metadata = JObject.Parse(match.Groups[1].Value);
			LinkedMusicMetadata = (
				musicId: long.TryParse(metadata["musicId"]?.ToString(), out var linkedMusicId) ? linkedMusicId : 0L,
				title: metadata["musicName"]?.ToString() ?? "",
				artist: metadata["artist"]?[0]?[0]?.ToString() ?? "",
				album: metadata["album"]?.ToString() ?? "",
				picUrl: metadata["albumPic"]?.ToString() ?? "");

			if (string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(LinkedMusicMetadata.title))
			{
				title = LinkedMusicMetadata.title.Trim();
			}

			if (string.IsNullOrWhiteSpace(artist) && !string.IsNullOrWhiteSpace(LinkedMusicMetadata.artist))
			{
				artist = LinkedMusicMetadata.artist.Trim();
			}

			if (string.IsNullOrWhiteSpace(album) && !string.IsNullOrWhiteSpace(LinkedMusicMetadata.album))
			{
				album = LinkedMusicMetadata.album.Trim();
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine("SearchCondition error:" + ex.GetMessageChain());
		}
	}

	private string ResolveSearchTitle(string title)
	{
		if (!string.IsNullOrWhiteSpace(title) && !Settings.Default.SearchCondition_UseOnlyFilename)
		{
			return title;
		}

		FileInfo fileInfo = new FileInfo(FilePath);
		if (!fileInfo.Exists)
		{
			return title;
		}

		string fileName = fileInfo.Name;
		int extensionIndex = fileName.LastIndexOf('.');
		title = extensionIndex > 0 ? fileName.Substring(0, extensionIndex) : "";
		UsedFileNameForTitle = true;
		return title;
	}

	[DllImport("MusicTag.dll", CharSet = CharSet.Unicode, EntryPoint = "rc3")]
	private static extern IntPtr DecodeMusicComment(string info);
}
