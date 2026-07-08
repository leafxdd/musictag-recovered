using System.Collections.Generic;
using System.Text;
using MusicTag.Services;
using MusicTagWinApp.Adapter;
using MusicTagWinApp.Properties;
using MusicTagWinApp.Structs;

namespace MusicTagWinApp.Writers;

internal class QqSongInfo
{
	public long Id;

	public string Mid;

	public string Name;

	public List<QqArtistInfo> Artists { get; } = new List<QqArtistInfo>();

	public int? GenreId;

	public int? TrackNumber;

	public int? DiscNumber;

	public string ReleaseDate;

	public string Title;

	public string Subtitle;

	public QqAlbumInfo Album { get; } = new QqAlbumInfo();

	public string GetArtistNames()
	{
		StringBuilder artistNames = new StringBuilder();

		foreach (QqArtistInfo artist in Artists)
		{
			if (string.IsNullOrWhiteSpace(artist.Name))
			{
				continue;
			}

			if (artistNames.Length > 0)
			{
				artistNames.Append(Settings.Default.GetArtistConnector());
			}

			artistNames.Append(TextEncodingService.DecodeBasicHtmlEntities(artist.Name));
		}

		return artistNames.ToString();
	}

	public string GetGenreName()
	{
		if (!GenreId.HasValue)
		{
			return "";
		}

		switch (GenreId.Value)
		{
		case 1:
			return "Pop";
		case 2:
			return "Classical";
		case 3:
			return "Jazz";
		case 15:
			return "Blues";
		case 19:
			return "Country";
		case 20:
			return "Dance";
		case 21:
			return "Easy Listening";
		case 22:
			return "Electronic";
		case 23:
			return "Folk";
		case 27:
			return "Latin";
		case 28:
			return "Metal";
		case 31:
			return "New Age";
		case 33:
			return "R&B";
		case 34:
			return "Rap";
		case 36:
			return "Rock";
		case 37:
			return "Soundtrack";
		case 39:
			return "World Music";
		case 50:
			return "Alternative";
		case 65:
			return "Religious";
		default:
			return "";
		}
	}
}
