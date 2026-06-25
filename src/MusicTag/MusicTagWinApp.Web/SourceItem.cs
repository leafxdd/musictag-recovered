using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace MusicTagWinApp.Web;

[Serializable]
internal class SourceItem
{
	[JsonProperty("Src")]
	public SearchSource SearchSource { get; private set; }

	public bool Enabled { get; set; }

	[JsonProperty("Seq")]
	public int Sequence { get; set; }

	[JsonProperty("IsOther")]
	public bool IsSecondarySource { get; set; }

	[JsonProperty("WebSearchItemsLimit")]
	public int SearchResultLimit { get; set; }

	[JsonConstructor]
	public SourceItem(
		[JsonProperty("Src")] SearchSource searchSource,
		[JsonProperty("Seq")] int sequence,
		[JsonProperty("Enabled")] bool enabled = true,
		[JsonProperty("IsOther")] bool isSecondarySource = false,
		[JsonProperty("WebSearchItemsLimit")] int searchResultLimit = 100)
	{
		SearchResultLimit = searchResultLimit;
		SearchSource = searchSource;
		Sequence = sequence;
		Enabled = enabled;
		IsSecondarySource = isSecondarySource;
	}

	public int GetEffectiveSearchResultLimit()
	{
		if (SearchResultLimit <= 99 && SearchResultLimit > 0)
		{
			return SearchResultLimit;
		}
		return int.MaxValue;
	}

	public static List<SourceItem> GetSortedBySequence(List<SourceItem> sourceSettings)
	{
		List<SourceItem> sortedSources = new List<SourceItem>(sourceSettings);
		sortedSources.Sort(CompareBySequence);
		return sortedSources;
	}

	public static void ApplySavedSourceSettings(string json, List<SourceItem> sourceSettings)
	{
		if (!string.IsNullOrWhiteSpace(json))
		{
			try
			{
				List<SourceItem> savedSources = JsonConvert.DeserializeObject<List<SourceItem>>(json);
				if (savedSources != null)
				{
					foreach (SourceItem savedSource in savedSources)
					{
						foreach (SourceItem source in sourceSettings)
						{
							if (savedSource.SearchSource == source.SearchSource)
							{
								source.Enabled = savedSource.Enabled;
								source.Sequence = savedSource.Sequence;
								source.SearchResultLimit = savedSource.SearchResultLimit;
								break;
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine("DeserializeObject error:" + ex.Message);
			}
		}

		List<SourceItem> sortedSources = new List<SourceItem>(sourceSettings);
		sortedSources.Sort(CompareSearchSourceOrder);
		int sequence = 0;
		foreach (SourceItem source in sortedSources)
		{
			source.Sequence = sequence++;
		}
	}

	private static int CompareBySequence(SourceItem left, SourceItem right)
	{
		return left.Sequence.CompareTo(right.Sequence);
	}

	private static int CompareSearchSourceOrder(SourceItem left, SourceItem right)
	{
		if (left.IsSecondarySource != right.IsSecondarySource)
		{
			return left.IsSecondarySource ? 1 : -1;
		}
		return left.Sequence.CompareTo(right.Sequence);
	}
}
