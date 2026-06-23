using System;
using System.Collections.Generic;
using System.Threading;
using MusicTag.Composer;
using MusicTagWinApp.Containers;
using MusicTagWinApp.Properties;
using MusicTagWinApp.Roles;
using MusicTagWinApp.Structs;
using MusicTagWinApp.Web;
using Newtonsoft.Json;

namespace MusicTagWinApp.Adapter;

internal class LyricSearchResult
{
	private static readonly List<SourceItem> lyricSourceSettings;

	private readonly float[] similarityScores = new float[3];

	public string LyricUrl { get; set; }

	public string TrackId { get; set; }

	public string Title { get; set; }

	public string Artist { get; set; }

	public string Album { get; set; }

	public string OriginalTitle { get; set; }

	public SearchSource SearchSource { get; set; }

	public int LyricType { get; set; }

	public string Lyric { get; set; }

	public string TranslatedLyric { get; set; }

	public int ListItemIndex { get; set; }

	public bool IsLoaded { get; set; }

	public Func<CancellationTokenSource, LyricSearchResult> DeferredLyricLoader { get; set; }

	public int SourceOrder { get; set; }

	public int ResultOrder { get; set; }

	static LyricSearchResult()
	{
		lyricSourceSettings = new List<SourceItem>
		{
			new SourceItem(SearchSource.Music163, 0),
			new SourceItem(SearchSource.QQ, 1),
			new SourceItem(SearchSource.Kugou, 3),
			new SourceItem(SearchSource.Kuwo, 3, enabled: false)
		};
		SourceItem.ApplySavedSourceSettings(Settings.Default.LyricInfo_SourceItemList, GetLyricSourceSettings());
	}

	public static List<SourceItem> GetLyricSourceSettings()
	{
		return lyricSourceSettings;
	}

	public static List<SourceItem> GetSortedLyricSourceSettings()
	{
		return SourceItem.GetSortedBySequence(GetLyricSourceSettings());
	}

	public static void SaveLyricSourceSettings()
	{
		string serializedSourceSettings = JsonConvert.SerializeObject(GetLyricSourceSettings());
		Settings.Default.LyricInfo_SourceItemList = serializedSourceSettings;
	}

	private float[] GetSimilarityScores()
	{
		return similarityScores;
	}

	public string GetFormattedLyricText()
	{
		Settings settings = Settings.Default;
		string lyricText;
		string translatedLyricText;
		if (settings.LyricDownload_DownloadTrans_LyricFormat != 3)
		{
			lyricText = Lyric ?? "";
			translatedLyricText = TranslatedLyric;
		}
		else
		{
			lyricText = TranslatedLyric;
			translatedLyricText = Lyric ?? "";
		}

		if (!string.IsNullOrWhiteSpace(translatedLyricText))
		{
			switch (settings.LyricDownload_DownloadTrans_ChineseConvMode)
			{
			case 1:
				translatedLyricText = ChineseTextConverter.TraditionalToSimplified().ConvertText(translatedLyricText);
				break;
			case 2:
				translatedLyricText = ChineseTextConverter.SimplifiedToTraditional().ConvertText(translatedLyricText);
				break;
			}
		}

		if (settings.LyricDownload_DownloadTrans_Enable)
		{
			if (settings.LyricDownload_RemoveTimetag)
			{
				return LyricTextProcessor.RemoveTimestampsFromDownloadedLyrics(lyricText, translatedLyricText, settings.LyricDownload_DownloadTrans_DontDownloadOrigLyric);
			}

			if (settings.LyricDownload_ReformatTimetag || settings.LyricDownload_DeleteLinesOfBlankText || settings.LyricDownload_DeleteHeadTag)
			{
				return LyricTextProcessor.ReformatDownloadedLyrics(lyricText, translatedLyricText, settings.LyricDownload_DeleteLinesOfBlankText, settings.LyricDownload_DeleteHeadTag, settings.LyricDownload_DownloadTrans_DontDownloadOrigLyric);
			}

			return LyricTextProcessor.MergeDownloadedLyrics(lyricText, translatedLyricText, settings.LyricDownload_DownloadTrans_DontDownloadOrigLyric);
		}

		if (settings.LyricDownload_RemoveTimetag)
		{
			return LyricTextProcessor.RemoveTimestamps(lyricText);
		}

		if (settings.LyricDownload_ReformatTimetag || settings.LyricDownload_DeleteLinesOfBlankText || settings.LyricDownload_DeleteHeadTag)
		{
			return LyricTextProcessor.ReformatLyric(lyricText, settings.LyricDownload_DeleteLinesOfBlankText, settings.LyricDownload_DeleteHeadTag);
		}

		return lyricText;
	}

	public bool HasDownloadableLyric()
	{
		Settings settings = Settings.Default;
		if (!settings.LyricDownload_DownloadTrans_Enable)
		{
			return !string.IsNullOrEmpty(Lyric);
		}

		if (!settings.LyricDownload_DownloadTrans_DontDownloadOrigLyric)
		{
			return !string.IsNullOrEmpty(Lyric);
		}

		if (string.IsNullOrEmpty(TranslatedLyric))
		{
			return !string.IsNullOrEmpty(Lyric);
		}

		return true;
	}

	public void UpdateSimilarityScores(string title, string artist, string album)
	{
		TrackSearchResult.CalculateSimilarityScores(title, artist, album, Title, Artist, Album, OriginalTitle, GetSimilarityScores());
	}

	public static void SortLyricResults(List<LyricSearchResult> lyrics)
	{
		lyrics.Sort(CompareLyricResults);
	}

	private static int CompareLyricResults(LyricSearchResult left, LyricSearchResult right)
	{
		float[] leftScores = left.GetSimilarityScores();
		float[] rightScores = right.GetSimilarityScores();
		for (int scoreIndex = 0; scoreIndex < 3; scoreIndex++)
		{
			if (leftScores[scoreIndex] > rightScores[scoreIndex])
			{
				return -1;
			}

			if (leftScores[scoreIndex] < rightScores[scoreIndex])
			{
				return 1;
			}
		}

		int sourceOrderComparison = left.SourceOrder.CompareTo(right.SourceOrder);
		if (sourceOrderComparison != 0)
		{
			return sourceOrderComparison;
		}
		
		return left.ResultOrder.CompareTo(right.ResultOrder);
	}

}
