using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using MusicTagWinApp.Adapter;
using MusicTagWinApp.Instances;
using MusicTagWinApp.Listeners;
using MusicTagWinApp.Properties;
using MusicTagWinApp.Structs;
using MusicTagWinApp.Web;
using MusicTagWinApp.Writers;
using Newtonsoft.Json;

namespace MusicTagWinApp.Roles;

	internal class TrackSearchResult
	{
	private const int TitleSimilarityScoreIndex = 0;

	private const int ArtistSimilarityScoreIndex = 1;

	private const int AlbumSimilarityScoreIndex = 2;

	private const int SimilarityScoreCount = 3;

	private const double StrongSimilarityThreshold = 0.8;

	private const double LooseSimilarityThreshold = 0.5;

	private const int TopProviderResultOrder = 0;

	private const int SecondarySearchPass = 1;

	private const int AlbumArtistFallbackSearchPass = 2;

	private const int UnassignedSortOrder = -1;

		private static readonly List<SourceItem> tagSourceSettings;

		private static readonly Regex WhitespaceRegex = new Regex("\\s+");

		private static readonly Regex LeftParenRegex = new Regex("（");

		private static readonly Regex RightParenRegex = new Regex("）");

		private static readonly Regex ExclamationRegex = new Regex("！");

		private static readonly Regex QuestionRegex = new Regex("？");

		private static readonly Regex EllipsisRegex = new Regex("…");

		private static readonly Regex CommaRegex = new Regex("，");

		private static readonly Regex PeriodRegex = new Regex("。");

		private static readonly Regex ColonRegex = new Regex("：");

		private static readonly Regex FullWidthPeriodRegex = new Regex("．");
	
	public float[] SimilarityScores { get; }

	public float TitleSimilarityScore => SimilarityScores[TitleSimilarityScoreIndex];

	public float ArtistSimilarityScore => SimilarityScores[ArtistSimilarityScoreIndex];

	public float AlbumSimilarityScore => SimilarityScores[AlbumSimilarityScoreIndex];

	public SearchSource SearchSource { get; set; }
	
	public string SourceTrackId { get; set; }

	public string QqMusicMid { get; set; }

	public string KugouHash { get; set; }

	public int KugouDurationMs { get; set; }

	public string NetEaseAlbumId { get; set; }

	public string Year { get; set; }
	
	public string Genre { get; set; }

	public string TrackLabel { get; set; }

	public string OriginalTitle { get; set; }

	public CoverSearchResult Cover { get; set; }
		
	public LyricSearchResult LyricResult { get; set; }

	public int ResultOrder { get; set; }

	public int SearchPass { get; set; }

	public int SourceOrder { get; set; }

	public string Title { get; set; }
	
	public string Artist { get; set; }
	
	public string Album { get; set; }
	
	public int Track { get; set; }

	public int Disc { get; set; }

	public string Comment { get; set; }

	public static List<SourceItem> GetTagSourceSettings()
	{
		return tagSourceSettings;
	}
	
	public static List<SourceItem> GetSortedTagSourceSettings()
	{
		return SourceItem.GetSortedBySequence(GetTagSourceSettings());
	}
	
	static TrackSearchResult()
	{
		tagSourceSettings = new List<SourceItem>
		{
			new SourceItem(SearchSource.Music163, 0),
			new SourceItem(SearchSource.QQ, 1),
			new SourceItem(SearchSource.Kuwo, 3, enabled: false)
		};
		SourceItem.ApplySavedSourceSettings(Settings.Default.CombTagsInfo_SourceItemList, GetTagSourceSettings());
	}
	
	public static void SaveTagSourceSettings()
	{
		string serializedSourceSettings = JsonConvert.SerializeObject(GetTagSourceSettings());
		Settings.Default.CombTagsInfo_SourceItemList = serializedSourceSettings;
	}
	
	public void UpdateSimilarityScores(string title, string artist, string album)
	{
		CalculateSimilarityScores(title, artist, album, Title, Artist, Album, OriginalTitle, SimilarityScores);
	}
	
	public static void CalculateSimilarityScores(string title, string artist, string album, string candidateTitle, string candidateArtist, string candidateAlbum, string candidateOriginalTitle, float[] scores)
	{
		const int TextCandidateCount = 11;
		const int TargetTitleTextIndex = 0;
		const int CandidateTitleTextIndex = 1;
		const int TargetArtistTextIndex = 2;
		const int CandidateArtistTextIndex = 3;
		const int TargetAlbumTextIndex = 4;
		const int CandidateAlbumTextIndex = 5;
		const int CandidateOriginalTitleTextIndex = 6;
		const int ArtistTitleVariantTextIndex = 7;
		const int ArtistOriginalTitleVariantTextIndex = 8;
		const int TitleOnlyVariantTextIndex = 9;
		const int OriginalTitleOnlyVariantTextIndex = 10;

		string normalizedCandidateTitle = candidateTitle ?? "";
		string normalizedCandidateArtist = candidateArtist ?? "";
		string normalizedCandidateAlbum = candidateAlbum ?? "";
		string normalizedCandidateOriginalTitle = (candidateOriginalTitle ?? "").Trim();
		string artistTitleVariant = "";
		string artistOriginalTitleVariant = "";
		string titleOnlyVariant = "";
		string originalTitleOnlyVariant = "";
		if (string.IsNullOrWhiteSpace(artist))
		{
			string titleOnly = normalizedCandidateTitle;
			string originalTitleOnly = normalizedCandidateOriginalTitle;
			titleOnlyVariant = titleOnly;
			normalizedCandidateTitle = titleOnly + " " + normalizedCandidateArtist;
			artistTitleVariant = normalizedCandidateArtist + " " + titleOnly;
			if (!string.IsNullOrWhiteSpace(originalTitleOnly))
			{
				originalTitleOnlyVariant = originalTitleOnly;
				normalizedCandidateOriginalTitle = originalTitleOnly + " " + normalizedCandidateArtist;
				artistOriginalTitleVariant = normalizedCandidateArtist + " " + originalTitleOnly;
			}
			normalizedCandidateArtist = "";
		}
		string[] textCandidates = new string[TextCandidateCount];
		textCandidates[TargetTitleTextIndex] = title;
		textCandidates[CandidateTitleTextIndex] = normalizedCandidateTitle;
		textCandidates[TargetArtistTextIndex] = artist;
		textCandidates[CandidateArtistTextIndex] = normalizedCandidateArtist;
		textCandidates[TargetAlbumTextIndex] = album;
		textCandidates[CandidateAlbumTextIndex] = normalizedCandidateAlbum;
		textCandidates[CandidateOriginalTitleTextIndex] = normalizedCandidateOriginalTitle;
		textCandidates[ArtistTitleVariantTextIndex] = artistTitleVariant;
		textCandidates[ArtistOriginalTitleVariantTextIndex] = artistOriginalTitleVariant;
		textCandidates[TitleOnlyVariantTextIndex] = titleOnlyVariant;
		textCandidates[OriginalTitleOnlyVariantTextIndex] = originalTitleOnlyVariant;
		NormalizeSimilarityTextCandidates(textCandidates);
		scores[TitleSimilarityScoreIndex] = TextSimilarityCalculator.CalculateTextSimilarity(textCandidates[TargetTitleTextIndex], textCandidates[CandidateTitleTextIndex]);
		for (int alternateTitleIndex = CandidateOriginalTitleTextIndex; alternateTitleIndex <= OriginalTitleOnlyVariantTextIndex; alternateTitleIndex++)
		{
			if (!string.IsNullOrEmpty(textCandidates[alternateTitleIndex]))
			{
				scores[TitleSimilarityScoreIndex] = Math.Max(TextSimilarityCalculator.CalculateTextSimilarity(textCandidates[TargetTitleTextIndex], textCandidates[alternateTitleIndex]), scores[TitleSimilarityScoreIndex]);
			}
		}
		scores[ArtistSimilarityScoreIndex] = TextSimilarityCalculator.CalculateArtistSimilarity(textCandidates[TargetArtistTextIndex], textCandidates[CandidateArtistTextIndex]);
		scores[AlbumSimilarityScoreIndex] = TextSimilarityCalculator.CalculateTextSimilarity(textCandidates[TargetAlbumTextIndex], textCandidates[CandidateAlbumTextIndex]);
	}
	
	public static void SortBySimilarity(List<TrackSearchResult> tracks)
	{
		tracks.Sort(CompareBySimilarity);
	}
	
	public static void PromoteBestMatch(string targetTitle, string targetArtist, string targetAlbum, List<TrackSearchResult> results)
	{
		if (!results.Any())
		{
			return;
		}
		targetTitle = NormalizeForMatch(targetTitle);
		targetArtist = NormalizeForMatch(targetArtist);
		targetAlbum = NormalizeForMatch(targetAlbum);
		bool promoted = false;
		TrackSearchResult currentBest = results[0];
		if (targetArtist.Any() && currentBest.ArtistSimilarityScore < StrongSimilarityThreshold)
		{
			List<TrackSearchResult> artistRankedCandidates = new List<TrackSearchResult>();
			List<TrackSearchResult> earlyPassCandidates = new List<TrackSearchResult>();
			List<TrackSearchResult> primaryCandidates = new List<TrackSearchResult>();
			artistRankedCandidates.Add(currentBest);
			primaryCandidates.Add(currentBest);
			foreach (TrackSearchResult candidate in results)
			{
				if (candidate.ResultOrder != TopProviderResultOrder)
				{
					continue;
				}
				if (candidate.SearchPass < AlbumArtistFallbackSearchPass && candidate != currentBest)
				{
					artistRankedCandidates.Add(candidate);
				}
				if (candidate.SearchPass < SecondarySearchPass)
				{
					earlyPassCandidates.Add(candidate);
					if (candidate != currentBest)
					{
						primaryCandidates.Add(candidate);
					}
				}
			}
			SortByArtistSimilarity(artistRankedCandidates);
			string currentBestTitle = NormalizeForMatch(currentBest.Title);
			string currentBestCompositeTitle = NormalizeForMatch(DatabaseMapper.CoalesceNonBlank(currentBest.OriginalTitle, currentBest.Title));
			string currentBestArtist = NormalizeForMatch(currentBest.Artist);
			string currentBestAlbum = NormalizeForMatch(currentBest.Album);
			foreach (TrackSearchResult artistRankedCandidate in artistRankedCandidates)
			{
				if (artistRankedCandidate == currentBest || !(artistRankedCandidate.ArtistSimilarityScore >= StrongSimilarityThreshold))
				{
					continue;
				}
				string artistRankedCandidateTitle = NormalizeForMatch(artistRankedCandidate.Title);
				string artistRankedCandidateCompositeTitle = NormalizeForMatch(DatabaseMapper.CoalesceNonBlank(artistRankedCandidate.OriginalTitle, artistRankedCandidate.Title));
				string artistRankedCandidateAlbum = NormalizeForMatch(artistRankedCandidate.Album);
				if (ContainsEitherWay(artistRankedCandidateCompositeTitle, targetTitle) && artistRankedCandidateAlbum.Any() && targetAlbum.Any() && ContainsEitherWay(artistRankedCandidateAlbum, targetAlbum))
				{
					TrackSearchResult candidateToPromoteByAlbum = (IsCandidateInstrumentalVariant(targetTitle, artistRankedCandidateTitle) ? FindNonInstrumentalBaseTrack(results, artistRankedCandidate) : artistRankedCandidate);
					if (candidateToPromoteByAlbum == null)
					{
						candidateToPromoteByAlbum = artistRankedCandidate;
					}
					MoveTrackToFront(results, candidateToPromoteByAlbum);
					promoted = true;
					break;
				}
			}
			if (!promoted)
			{
				foreach (TrackSearchResult titleCandidate in artistRankedCandidates)
				{
					if (titleCandidate == currentBest || !(titleCandidate.ArtistSimilarityScore >= StrongSimilarityThreshold))
					{
						continue;
					}
					string titleCandidateTitle = NormalizeForMatch(titleCandidate.Title);
					if (ContainsEitherWay(NormalizeForMatch(DatabaseMapper.CoalesceNonBlank(titleCandidate.OriginalTitle, titleCandidate.Title)), targetTitle))
					{
						TrackSearchResult candidateToPromoteByTitle = (IsCandidateInstrumentalVariant(targetTitle, titleCandidateTitle) ? FindNonInstrumentalBaseTrack(results, titleCandidate) : titleCandidate);
						if (candidateToPromoteByTitle == null)
						{
							candidateToPromoteByTitle = titleCandidate;
						}
						MoveTrackToFront(results, candidateToPromoteByTitle);
						promoted = true;
						break;
					}
				}
			}
			if (!promoted && currentBestAlbum.Any())
			{
				foreach (TrackSearchResult secondPassCandidate in results)
				{
					if (secondPassCandidate != currentBest && secondPassCandidate.SearchPass == AlbumArtistFallbackSearchPass)
					{
						string secondPassCandidateTitle = NormalizeForMatch(secondPassCandidate.Title);
						string secondPassCandidateCompositeTitle = NormalizeForMatch(DatabaseMapper.CoalesceNonBlank(secondPassCandidate.OriginalTitle, secondPassCandidate.Title));
						string secondPassCandidateArtist = NormalizeForMatch(secondPassCandidate.Artist);
						string secondPassCandidateAlbum = NormalizeForMatch(secondPassCandidate.Album);
						if (secondPassCandidateAlbum.Any() && secondPassCandidateAlbum == targetAlbum && secondPassCandidateArtist == targetArtist && ContainsEitherWay(secondPassCandidateCompositeTitle, targetTitle) && !IsCandidateInstrumentalVariant(targetTitle, secondPassCandidateTitle))
						{
							MoveTrackToFront(results, secondPassCandidate);
							promoted = true;
							break;
						}
					}
				}
			}
			if (!promoted && currentBestAlbum.Any())
			{
				foreach (TrackSearchResult albumArtistCandidate in results)
				{
					if (albumArtistCandidate != currentBest && albumArtistCandidate.SearchPass < AlbumArtistFallbackSearchPass)
					{
						string albumArtistCandidateTitle = NormalizeForMatch(albumArtistCandidate.Title);
						string albumArtistCandidateCompositeTitle = NormalizeForMatch(DatabaseMapper.CoalesceNonBlank(albumArtistCandidate.OriginalTitle, albumArtistCandidate.Title));
						string albumArtistCandidateArtist = NormalizeForMatch(albumArtistCandidate.Artist);
						string albumArtistCandidateAlbum = NormalizeForMatch(albumArtistCandidate.Album);
						if (albumArtistCandidateAlbum.Any() && ContainsEitherWay(albumArtistCandidateAlbum, targetAlbum) && albumArtistCandidateArtist == targetArtist && ContainsEitherWay(albumArtistCandidateCompositeTitle, targetTitle) && !IsCandidateInstrumentalVariant(targetTitle, albumArtistCandidateTitle))
						{
							MoveTrackToFront(results, albumArtistCandidate);
							promoted = true;
							break;
						}
					}
				}
			}
			if (!promoted && currentBestAlbum.Any())
			{
				foreach (TrackSearchResult artistAlbumCandidate in artistRankedCandidates)
				{
					if (!ContainsEitherWay(NormalizeForMatch(artistAlbumCandidate.Artist), targetArtist))
					{
						continue;
					}
					string artistAlbumCandidateTitle = NormalizeForMatch(artistAlbumCandidate.Title);
					string artistAlbumCandidateCompositeTitle = NormalizeForMatch(DatabaseMapper.CoalesceNonBlank(artistAlbumCandidate.OriginalTitle, artistAlbumCandidate.Title));
					string artistAlbumCandidateAlbum = NormalizeForMatch(artistAlbumCandidate.Album);
					if (artistAlbumCandidateAlbum.Any() && ContainsEitherWay(artistAlbumCandidateCompositeTitle, targetTitle) && ContainsEitherWay(artistAlbumCandidateAlbum, targetAlbum))
					{
						TrackSearchResult candidateToPromoteByArtistAlbum = (IsCandidateInstrumentalVariant(targetTitle, artistAlbumCandidateTitle) ? FindNonInstrumentalBaseTrack(results, artistAlbumCandidate) : artistAlbumCandidate);
						if (candidateToPromoteByArtistAlbum == null)
						{
							candidateToPromoteByArtistAlbum = artistAlbumCandidate;
						}
						MoveTrackToFront(results, candidateToPromoteByArtistAlbum);
						promoted = true;
						break;
					}
				}
			}
			if (!promoted)
			{
				foreach (TrackSearchResult primaryCandidate in primaryCandidates)
				{
					if (!ContainsEitherWay(NormalizeForMatch(primaryCandidate.Artist), targetArtist))
					{
						continue;
					}
					string primaryCandidateTitle = NormalizeForMatch(primaryCandidate.Title);
					if (ContainsEitherWay(NormalizeForMatch(DatabaseMapper.CoalesceNonBlank(primaryCandidate.OriginalTitle, primaryCandidate.Title)), targetTitle))
					{
						TrackSearchResult candidateToPromoteFromPrimaryList = (IsCandidateInstrumentalVariant(targetTitle, primaryCandidateTitle) ? FindNonInstrumentalBaseTrack(results, primaryCandidate) : primaryCandidate);
						if (candidateToPromoteFromPrimaryList == null)
						{
							candidateToPromoteFromPrimaryList = primaryCandidate;
						}
						MoveTrackToFront(results, candidateToPromoteFromPrimaryList);
						promoted = true;
						break;
					}
				}
			}
			TrackSearchResult artistRankedBestCandidate = artistRankedCandidates[0];
			if (!promoted && artistRankedBestCandidate != currentBest && artistRankedBestCandidate.ArtistSimilarityScore >= StrongSimilarityThreshold && !ContainsEitherWay(currentBestCompositeTitle, targetTitle) && currentBest.TitleSimilarityScore < StrongSimilarityThreshold)
			{
				string artistRankedBestTitle = NormalizeForMatch(artistRankedBestCandidate.Title);
				TrackSearchResult candidateToPromoteFromArtistRank = (IsCandidateInstrumentalVariant(targetTitle, artistRankedBestTitle) ? FindNonInstrumentalBaseTrack(results, artistRankedBestCandidate) : artistRankedBestCandidate);
				if (candidateToPromoteFromArtistRank == null)
				{
					candidateToPromoteFromArtistRank = artistRankedBestCandidate;
				}
				MoveTrackToFront(results, candidateToPromoteFromArtistRank);
				promoted = true;
			}
			bool currentBestAlreadyMatches;
			bool currentBestAlbumMatchesTarget = currentBestAlbum.Any() && targetAlbum.Any() && ContainsEitherWay(currentBestAlbum, targetAlbum);
			bool currentBestInstrumentalStateMatchesTarget = IsInstrumentalTitle(targetTitle) == IsInstrumentalTitle(currentBestTitle);
			currentBestAlreadyMatches = ContainsEitherWay(currentBestCompositeTitle, targetTitle) && currentBestAlbumMatchesTarget && currentBestInstrumentalStateMatchesTarget;
			if (!promoted && earlyPassCandidates.Any() && (!currentBestAlreadyMatches || !ContainsEitherWay(currentBestArtist, targetArtist)))
			{
				foreach (TrackSearchResult albumMatchCandidate in earlyPassCandidates)
				{
					if (albumMatchCandidate != currentBest)
					{
						string albumMatchCandidateTitle = NormalizeForMatch(albumMatchCandidate.Title);
						string albumMatchCandidateCompositeTitle = NormalizeForMatch(DatabaseMapper.CoalesceNonBlank(albumMatchCandidate.OriginalTitle, albumMatchCandidate.Title));
						string albumMatchCandidateArtist = NormalizeForMatch(albumMatchCandidate.Artist);
						string albumMatchCandidateAlbum = NormalizeForMatch(albumMatchCandidate.Album);
						bool albumCandidateMatchesTarget = ContainsEitherWay(albumMatchCandidateCompositeTitle, targetTitle) && albumMatchCandidateAlbum.Any() && targetAlbum.Any() && ContainsEitherWay(albumMatchCandidateAlbum, targetAlbum);
						bool currentBestScoresHigherThanAlbumCandidate = !(currentBest.TitleSimilarityScore <= albumMatchCandidate.TitleSimilarityScore);
						bool shouldKeepCurrentBest = !IsCandidateInstrumentalVariant(targetTitle, currentBestTitle) && currentBestAlreadyMatches && currentBestScoresHigherThanAlbumCandidate;
						if (!albumCandidateMatchesTarget || shouldKeepCurrentBest)
						{
							continue;
						}
						TrackSearchResult candidateToPromoteFromAlbumMatch = (IsCandidateInstrumentalVariant(targetTitle, albumMatchCandidateTitle) ? FindNonInstrumentalBaseTrack(results, albumMatchCandidate) : albumMatchCandidate);
						if (candidateToPromoteFromAlbumMatch != null)
						{
							if (albumMatchCandidateTitle != currentBestTitle || albumMatchCandidateArtist != currentBestArtist || albumMatchCandidateAlbum != currentBestAlbum)
							{
								MoveTrackToFront(results, candidateToPromoteFromAlbumMatch);
							}
							currentBestAlreadyMatches = true;
							break;
						}
					}
					else if (currentBestAlreadyMatches)
					{
						break;
					}
				}
				bool currentBestAlbumMatchIsWeak = !targetAlbum.Any() || !currentBestAlbum.Any() || (currentBest.AlbumSimilarityScore < StrongSimilarityThreshold && !ContainsEitherWay(currentBestAlbum, targetAlbum));
				if (!currentBestAlreadyMatches && !ContainsEitherWay(currentBestArtist, targetArtist) && currentBestAlbumMatchIsWeak)
				{
					foreach (TrackSearchResult earlyPassCandidate in earlyPassCandidates)
					{
						if (earlyPassCandidate == currentBest)
						{
							break;
						}
						string earlyPassCandidateTitle = NormalizeForMatch(earlyPassCandidate.Title);
						string earlyPassCandidateCompositeTitle = NormalizeForMatch(DatabaseMapper.CoalesceNonBlank(earlyPassCandidate.OriginalTitle, earlyPassCandidate.Title));
						string earlyPassCandidateArtist = NormalizeForMatch(earlyPassCandidate.Artist);
						bool earlyPassTitleMatchesTarget = ContainsEitherWay(earlyPassCandidateCompositeTitle, targetTitle);
						bool currentBestTitleMatchIsWeak = !ContainsEitherWay(currentBestCompositeTitle, targetTitle) && currentBest.TitleSimilarityScore < StrongSimilarityThreshold;
						bool earlyPassTitleMatchIsStrong = earlyPassCandidate.TitleSimilarityScore >= StrongSimilarityThreshold;
						bool earlyPassArtistIsAcceptable = ContainsEitherWay(earlyPassCandidateArtist, targetArtist) || currentBest.ArtistSimilarityScore < LooseSimilarityThreshold || earlyPassCandidate.ArtistSimilarityScore >= currentBest.ArtistSimilarityScore;
						if (earlyPassTitleMatchesTarget && (currentBestTitleMatchIsWeak || earlyPassTitleMatchIsStrong) && earlyPassArtistIsAcceptable)
						{
							TrackSearchResult candidateToPromoteFromEarlyPass = (IsCandidateInstrumentalVariant(targetTitle, earlyPassCandidateTitle) ? FindNonInstrumentalBaseTrack(results, earlyPassCandidate) : earlyPassCandidate);
							if (candidateToPromoteFromEarlyPass != null)
							{
								MoveTrackToFront(results, candidateToPromoteFromEarlyPass);
								break;
							}
						}
					}
				}
			}
		}
		else if (targetArtist.Any() && currentBest.ArtistSimilarityScore >= StrongSimilarityThreshold)
		{
			string currentBestCompositeTitleForReplacementCheck = NormalizeForMatch(DatabaseMapper.CoalesceNonBlank(currentBest.OriginalTitle, currentBest.Title));
			string currentBestAlbumForReplacementCheck = NormalizeForMatch(currentBest.Album);
			bool currentBestTitleMatchesReplacementTarget = ContainsEitherWay(currentBestCompositeTitleForReplacementCheck, targetTitle) || currentBest.TitleSimilarityScore >= StrongSimilarityThreshold;
			bool currentBestNeedsAlbumReplacement = currentBestTitleMatchesReplacementTarget && targetAlbum.Any() && currentBestAlbumForReplacementCheck.Any() && !ContainsEitherWay(currentBestAlbumForReplacementCheck, targetAlbum) && currentBest.AlbumSimilarityScore < StrongSimilarityThreshold;
			if (currentBestNeedsAlbumReplacement)
			{
				foreach (TrackSearchResult albumReplacementCandidate in results)
				{
					if (albumReplacementCandidate == currentBest)
					{
						continue;
					}
					string albumReplacementCandidateTitle = NormalizeForMatch(albumReplacementCandidate.Title);
					string albumReplacementCandidateCompositeTitle = NormalizeForMatch(DatabaseMapper.CoalesceNonBlank(albumReplacementCandidate.OriginalTitle, albumReplacementCandidate.Title));
					string albumReplacementCandidateArtist = NormalizeForMatch(albumReplacementCandidate.Artist);
					string albumReplacementCandidateAlbum = NormalizeForMatch(albumReplacementCandidate.Album);
					bool replacementTitleMatchesTarget = ContainsEitherWay(albumReplacementCandidateCompositeTitle, targetTitle) && (albumReplacementCandidate.TitleSimilarityScore >= StrongSimilarityThreshold || albumReplacementCandidateCompositeTitle.Contains(currentBestCompositeTitleForReplacementCheck));
					bool replacementArtistMatchesTarget = ContainsEitherWay(albumReplacementCandidateArtist, targetArtist) && albumReplacementCandidate.ArtistSimilarityScore >= StrongSimilarityThreshold;
					bool replacementAlbumMatchesTarget = albumReplacementCandidateAlbum.Any() && ContainsEitherWay(albumReplacementCandidateAlbum, targetAlbum);
					bool replacementAlbumScoresAtLeastCurrent = albumReplacementCandidate.AlbumSimilarityScore >= currentBest.AlbumSimilarityScore;
					if (replacementTitleMatchesTarget && replacementArtistMatchesTarget && replacementAlbumMatchesTarget && replacementAlbumScoresAtLeastCurrent && !IsCandidateInstrumentalVariant(targetTitle, albumReplacementCandidateTitle))
					{
						MoveTrackToFront(results, albumReplacementCandidate);
						break;
					}
				}
			}
		}
		else if (!targetArtist.Any())
		{
			string currentBestCompositeTitleWithoutArtist = NormalizeForMatch(DatabaseMapper.CoalesceNonBlank(currentBest.OriginalTitle, currentBest.Title));
			if (currentBest.TitleSimilarityScore < LooseSimilarityThreshold && !ContainsEitherWay(currentBestCompositeTitleWithoutArtist, targetTitle))
			{
				foreach (TrackSearchResult titleOnlyCandidate in results)
				{
					if (titleOnlyCandidate == currentBest)
					{
						continue;
					}
					bool titleOnlyCandidateIsFromEarlyTopResult = titleOnlyCandidate.ResultOrder == TopProviderResultOrder && titleOnlyCandidate.SearchPass < AlbumArtistFallbackSearchPass;
					bool titleOnlyCandidateMatchesTarget = ContainsEitherWay(NormalizeForMatch(DatabaseMapper.CoalesceNonBlank(titleOnlyCandidate.OriginalTitle, titleOnlyCandidate.Title)), targetTitle);
					if (titleOnlyCandidateIsFromEarlyTopResult && titleOnlyCandidateMatchesTarget)
					{
						MoveTrackToFront(results, titleOnlyCandidate);
						break;
					}
				}
			}
		}
		if (!targetArtist.Any() || results.Count <= 1)
		{
			return;
		}
		string currentBestNormalizedTitle = NormalizeForMatch(currentBest.Title);
		if (IsCandidateInstrumentalVariant(targetTitle, currentBestNormalizedTitle))
		{
			TrackSearchResult baseTrackCandidate = FindNonInstrumentalBaseTrack(results, results[0]);
			if (baseTrackCandidate != null)
			{
				MoveTrackToFront(results, baseTrackCandidate);
			}
		}
	}

		private static void ReplaceRegexInTextCandidates(Regex regex, string replacement, string[] textCandidates)
		{
			for (int index = 0; index < textCandidates.Length; index++)
			{
				if (textCandidates[index] != null)
				{
				textCandidates[index] = regex.Replace(textCandidates[index], replacement);
			}
		}
	}

	private static void ConvertTextCandidatesChineseCharacters(string[] textCandidates)
	{
		for (int index = 0; index < textCandidates.Length; index++)
		{
			if (textCandidates[index] != null)
			{
				textCandidates[index] = ConvertChineseCharactersOnly(textCandidates[index]);
			}
		}
	}

	private static string ConvertChineseCharactersOnly(string text)
	{
		return ChineseTextConverter.TraditionalToSimplified().ConvertCharactersOnly(text);
	}

		private static void NormalizeSimilarityTextCandidates(string[] textCandidates)
		{
			ReplaceRegexInTextCandidates(WhitespaceRegex, "", textCandidates);
			ReplaceRegexInTextCandidates(LeftParenRegex, "(", textCandidates);
			ReplaceRegexInTextCandidates(RightParenRegex, ")", textCandidates);
			ReplaceRegexInTextCandidates(ExclamationRegex, "!", textCandidates);
			ReplaceRegexInTextCandidates(QuestionRegex, "?", textCandidates);
			ReplaceRegexInTextCandidates(EllipsisRegex, "...", textCandidates);
			ReplaceRegexInTextCandidates(CommaRegex, ",", textCandidates);
			ReplaceRegexInTextCandidates(PeriodRegex, ".", textCandidates);
			ReplaceRegexInTextCandidates(ColonRegex, ":", textCandidates);
			ReplaceRegexInTextCandidates(FullWidthPeriodRegex, ".", textCandidates);
			ConvertTextCandidatesChineseCharacters(textCandidates);
		}

	private static string NormalizeForMatch(string text)
	{
		text = DatabaseMapper.CoalesceNonBlank(text);
		text = text.Replace("（", "(");
		text = text.Replace("）", ")");
		text = text.Replace("！", "!");
		text = text.Replace("？", "?");
		text = text.Replace("…", "...");
		text = text.Replace("，", ",");
		text = text.Replace("。", ".");
		text = text.Replace("：", ":");
		text = text.Replace("．", ".");
		text = Regex.Replace(text, "\\s", " ");
		text = Regex.Replace(text, "[:] +", ":");
		text = Regex.Replace(text, "[,] +", ",");
		text = Regex.Replace(text, "[.] +", ".");
		text = Regex.Replace(text, "[!] +", "!");
		text = Regex.Replace(text, "[?] +", "?");
		text = Regex.Replace(text, " +[/]", "/");
		text = Regex.Replace(text, "[/] +", "/");
		text = Regex.Replace(text, " {2,}", " ");
		text = ConvertChineseCharactersOnly(text);
		return text.ToLower().Trim();
	}

	public static bool ContainsEitherWay(string firstValue, string secondValue)
	{
		if (!firstValue.Contains(secondValue))
		{
			return secondValue.Contains(firstValue);
		}
		return true;
	}

	private static void MoveTrackToFront(List<TrackSearchResult> results, TrackSearchResult resultToPromote)
	{
		results.Remove(resultToPromote);
		results.Insert(0, resultToPromote);
	}

	private static bool IsCandidateInstrumentalVariant(string targetTitle, string candidateTitle)
	{
		if (!IsInstrumentalTitle(targetTitle))
		{
			return IsInstrumentalTitle(candidateTitle);
		}
		return false;
	}

	public static bool IsInstrumentalTitle(string title)
	{
		return title.Contains("instrumental") || title.Contains("off vocal") || title.Contains("伴奏") || title.Contains("纯音乐");
	}

	private static TrackSearchResult FindNonInstrumentalBaseTrack(List<TrackSearchResult> candidates, TrackSearchResult instrumentalCandidate)
	{
		string instrumentalTitle = NormalizeForMatch(instrumentalCandidate.Title);
		string instrumentalArtist = NormalizeForMatch(instrumentalCandidate.Artist);
		string instrumentalAlbum = NormalizeForMatch(instrumentalCandidate.Album);
		foreach (TrackSearchResult candidate in candidates)
		{
			if (candidate != instrumentalCandidate)
			{
				string candidateTitle = NormalizeForMatch(candidate.Title);
				string candidateArtist = NormalizeForMatch(candidate.Artist);
				string candidateAlbum = NormalizeForMatch(candidate.Album);
				if (!IsInstrumentalTitle(candidateTitle) && instrumentalTitle.StartsWith(candidateTitle) && candidateArtist == instrumentalArtist && (candidateAlbum == instrumentalAlbum || (instrumentalAlbum.StartsWith(candidateAlbum) && IsInstrumentalTitle(instrumentalAlbum))))
				{
					return candidate;
				}
			}
		}
		return null;
	}

	public static void SortByArtistSimilarity(List<TrackSearchResult> tracks)
	{
		tracks.Sort(CompareByArtistSimilarity);
	}

	private static int CompareBySimilarity(TrackSearchResult lhs, TrackSearchResult rhs)
	{
		return CompareByScoreOrder(lhs, rhs, artistScoreFirst: false);
	}

	private static int CompareByArtistSimilarity(TrackSearchResult lhs, TrackSearchResult rhs)
	{
		return CompareByScoreOrder(lhs, rhs, artistScoreFirst: true);
	}

	private static int CompareByScoreOrder(TrackSearchResult lhs, TrackSearchResult rhs, bool artistScoreFirst)
	{
		for (int rankIndex = 0; rankIndex < SimilarityScoreCount; rankIndex++)
		{
			int scoreIndex = GetScoreIndex(rankIndex, artistScoreFirst);
			int scoreComparison = CompareDescending(lhs.SimilarityScores[scoreIndex], rhs.SimilarityScores[scoreIndex]);
			if (scoreComparison != 0)
			{
				return scoreComparison;
			}
		}
		int sourceComparison = lhs.SourceOrder.CompareTo(rhs.SourceOrder);
		if (sourceComparison != 0)
		{
			return sourceComparison;
		}
		int searchPassComparison = lhs.SearchPass.CompareTo(rhs.SearchPass);
		if (searchPassComparison != 0)
		{
			return searchPassComparison;
		}
		return lhs.ResultOrder.CompareTo(rhs.ResultOrder);
	}

	private static int GetScoreIndex(int rankIndex, bool artistScoreFirst)
	{
		if (!artistScoreFirst || rankIndex == AlbumSimilarityScoreIndex)
		{
			return rankIndex;
		}
		return rankIndex == TitleSimilarityScoreIndex ? ArtistSimilarityScoreIndex : TitleSimilarityScoreIndex;
	}

	private static int CompareDescending(float leftScore, float rightScore)
	{
		return rightScore.CompareTo(leftScore);
	}

	public TrackSearchResult()
	{
		SimilarityScores = new float[SimilarityScoreCount];
		ResultOrder = UnassignedSortOrder;
		SearchPass = UnassignedSortOrder;
		SourceOrder = UnassignedSortOrder;
	}

}
