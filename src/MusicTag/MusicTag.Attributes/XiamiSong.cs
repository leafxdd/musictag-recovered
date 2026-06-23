using System.Collections.Generic;
using System.Text;
using MusicTag.Serialization;
using MusicTagWinApp.Instances;
using MusicTagWinApp.Properties;

namespace MusicTag.Attributes;

internal class XiamiSong
{
	public string SongId;

	public string SongName;

	public string Subtitle;

	public string NewSubtitle;

	public string AlbumId;

	public string ArtistId;

	public string Singers;

	public int? DiscNumber;

	public int? TrackNumber;

	public string Songwriters;

	public string Composer;

	public string Arrangement;

	public long? CreatedAt;

	public string AlbumName;

	public List<XiamiArtist> Artists = new List<XiamiArtist>();

	public string LyricUrl;

	public int LyricType;

	public string CoverUrl;

	public string GetArtistName()
	{
		StringBuilder artistBuilder = new StringBuilder();
		string remainingArtists = DatabaseMapper.CoalesceNonBlank(Singers);
		foreach (XiamiArtist artist in Artists)
		{
			if (string.IsNullOrWhiteSpace(artist.Name))
			{
				continue;
			}
			string updatedArtists = remainingArtists.Replace(artist.Name, "");
			if (remainingArtists == updatedArtists)
			{
				continue;
			}
			if (artistBuilder.Length > 0)
			{
				artistBuilder.Append(Settings.Default.ConnectorsArtists);
			}
			artistBuilder.Append(artist.Name);
			remainingArtists = updatedArtists;
		}
		foreach (char value in remainingArtists)
		{
			if (value != '/' && value != ' ')
			{
				return Singers;
			}
		}
		return artistBuilder.ToString();
	}
}
