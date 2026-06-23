using System;
using System.Collections.Generic;
using System.Threading;
using MusicTag.Serialization;
using MusicTagWinApp.Properties;
using MusicTagWinApp.Web;
using Newtonsoft.Json;

namespace MusicTagWinApp.Listeners;

internal class CoverSearchResult
{
	private static readonly List<SourceItem> sources;

	public string CoverUrl { get; set; }

	public string LocalCoverPath { get; set; }

	public SearchSource SearchSource { get; set; }

	public int ListViewIndex { get; set; }

	public bool CoverDownloadQueued { get; set; }

	public Func<CancellationTokenSource, string, int, (RemoteTagProviderBase.DownloadStatus, long)> CoverDownloader { get; set; }

	public static List<SourceItem> GetCoverSourceSettings()
	{
		return sources;
	}

	public static List<SourceItem> GetSortedCoverSourceSettings()
	{
		return SourceItem.GetSortedBySequence(GetCoverSourceSettings());
	}

	static CoverSearchResult()
	{
		sources = new List<SourceItem>
		{
			new SourceItem(SearchSource.Music163, 0),
			new SourceItem(SearchSource.QQ, 1),
			new SourceItem(SearchSource.Kuwo, 3, enabled: false)
			};
			SourceItem.ApplySavedSourceSettings(Settings.Default.PictureInfo_SourceItemList, GetCoverSourceSettings());
	}

	public static void SaveCoverSourceSettings()
	{
		string serializedSourceSettings = JsonConvert.SerializeObject(GetCoverSourceSettings());
		Settings.Default.PictureInfo_SourceItemList = serializedSourceSettings;
	}
}
