using System.Collections.Generic;
using System.Text;
using MusicTagWinApp.Listeners;
using MusicTagWinApp.Properties;
using Newtonsoft.Json.Linq;

namespace MusicTagWinApp.Exporters;

internal class NetEaseSongInfo
{
	public long Id;

	public string Title;

	public string DiscNumberText;

	public int? TrackNumber;

	public string ExtraInfo;

	public long? MvId;

	public long? Flag;

	public NetEaseAlbumInfo Album { get; }

	public List<string> ArtistNames { get; }

	public List<string> Aliases { get; }

	public JArray ArtistJson { get; }

	public JArray AliasJson { get; }

	public string CommentJson;

	public string GetArtistDisplayText()
	{
		StringBuilder stringBuilder = new StringBuilder();
		foreach (string item in ArtistNames)
		{
			if (stringBuilder.Length > 0)
			{
				stringBuilder.Append(Settings.Default.ConnectorsArtists);
			}
			stringBuilder.Append(item);
		}
		return stringBuilder.ToString();
	}

	public string GetAliasCommentText()
	{
		StringBuilder stringBuilder = new StringBuilder();
		foreach (string item in Aliases)
		{
			if (stringBuilder.Length > 0)
			{
				stringBuilder.Append("\n");
			}
			stringBuilder.Append(item);
		}
		return stringBuilder.ToString();
	}

	public NetEaseSongInfo()
	{
		Album = new NetEaseAlbumInfo();
		ArtistNames = new List<string>();
		Aliases = new List<string>();
		ArtistJson = new JArray();
		AliasJson = new JArray();
	}
}

