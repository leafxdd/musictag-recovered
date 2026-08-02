namespace MusicTagWinApp.Adapter;

internal enum KuwoLyricQuality
{
	None,
	Legacy,
	HighPrecision
}

internal class KuwoSongInfo
{
	public string Album;

	public string Artist;

	public string ArtistId;

	public string TrackId;

	public string OriginalTitle;

	public string Title;

	public string CoverUrl;

	public string LargeCoverUrl;

	public string SearchAlbumCoverUrl;

	public LyricSearchResult LoadedLyric;

	internal bool LrcxAttempted;

	internal bool LegacyDetailsLoaded;

	internal KuwoLyricQuality LoadedLyricQuality;
}
