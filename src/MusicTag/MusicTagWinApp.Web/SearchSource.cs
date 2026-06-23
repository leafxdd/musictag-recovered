using System.ComponentModel;

namespace MusicTagWinApp.Web;

internal enum SearchSource
{
	// Explicit values preserve the original ordinals so existing persisted
	// SourceItem JSON ("Src") keeps matching after the unused sources
	// (Xiami=2, MiniLyrics=4, ITunes=5, Lastfm=6, Brainz=7, Vgmdb=8) were removed.
	[Description("163")]
	Music163 = 0,
	[Description("QQ")]
	QQ = 1,
	[Description("Kugou")]
	Kugou = 3,
	[Description("Kuwo")]
	Kuwo = 9
}
