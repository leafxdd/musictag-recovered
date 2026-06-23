using System.ComponentModel;

namespace MusicTagWinApp.Web;

internal enum SearchSource
{
	[Description("163")]
	Music163,
	[Description("QQ")]
	QQ,
	[Description("Xiami")]
	Xiami,
	[Description("Kugou")]
	Kugou,
	[Description("MiniLyrics")]
	MiniLyrics,
	[Description("iTunes")]
	ITunes,
	[Description("Last.fm")]
	Lastfm,
	[Description("Brainz")]
	Brainz,
	[Description("VGMdb")]
	Vgmdb,
	[Description("Kuwo")]
	Kuwo
}
