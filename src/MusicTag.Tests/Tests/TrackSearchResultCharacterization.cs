using System;
using System.Collections.Generic;
using MusicTagWinApp.Roles;

namespace MusicTag.Tests;

// TrackSearchResult(MusicTagWinApp.Roles)候选相似度/排序"大脑"的 characterization。叶子
// TextSimilarityCalculator(CalculateTextSimilarity/CalculateArtistSimilarity)与 PromoteBestMatch 的
// move-to-front 已覆盖,但【相似度编排】(11 路变体生成 + Max 选取 + 全角/繁简归一)与【多键排序比较器】
// (三分降序 + SourceOrder/SearchPass/ResultOrder 平手链 + artist-first 维度交换)零直测。本批:
//   CalculateSimilarityScores(已 public static):对 (target, candidate, scores[3]) 就地写三维相似度。
//   SortBySimilarity / SortByArtistSimilarity(已 public static):List.Sort + 多键比较器(后者交换 title/artist 维度)。
//   NormalizeForMatch(private static -> internal static):全角->半角/空白折叠/繁->简/ToLower/Trim。
internal static class TrackSearchResultCharacterization
{
	private static TrackSearchResult Track(float titleScore, float artistScore, float albumScore, int sourceOrder, int searchPass, int resultOrder)
	{
		TrackSearchResult track = new TrackSearchResult();
		track.SimilarityScores[0] = titleScore;
		track.SimilarityScores[1] = artistScore;
		track.SimilarityScores[2] = albumScore;
		track.SourceOrder = sourceOrder;
		track.SearchPass = searchPass;
		track.ResultOrder = resultOrder;
		return track;
	}

	public static IEnumerable<(string, Action)> All()
	{
		// ===== CalculateSimilarityScores:11 路变体 + Max + 归一 =====

		yield return ("CalculateSimilarityScores: identical -> all 1.0", delegate
		{
			float[] scores = new float[3];
			TrackSearchResult.CalculateSimilarityScores("Song", "Artist", "Album", "Song", "Artist", "Album", null, scores);
			Check.Equal(1f, scores[0], "title identical");
			Check.Equal(1f, scores[1], "artist identical");
			Check.Equal(1f, scores[2], "album identical");
		});

		yield return ("CalculateSimilarityScores: originalTitle better than title -> scores[0] takes Max", delegate
		{
			float[] scores = new float[3];
			// 候选主标题完全不匹配,但 originalTitle 精确命中 -> scores[0] 经 alternate Max 取到 1.0。
			TrackSearchResult.CalculateSimilarityScores("RealName", "A", "Al", "WrongTitleXYZ", "A", "Al", "RealName", scores);
			Check.Equal(1f, scores[0], "title score picks originalTitle exact match via Max");
		});

		yield return ("CalculateSimilarityScores: empty artist -> title/artist swap variant matches", delegate
		{
			float[] scores = new float[3];
			// target title "A B" + artist 空;candidate title "B" artist "A" -> artistTitleVariant "A B" -> 归一 "AB" == target "AB"。
			TrackSearchResult.CalculateSimilarityScores("A B", "", "Al", "B", "A", "Al", null, scores);
			Check.Equal(1f, scores[0], "swap variant reaches title match");
		});

		yield return ("CalculateSimilarityScores: full-width punctuation normalized before scoring", delegate
		{
			float[] scores = new float[3];
			// target "歌曲！"(全角!) vs candidate "歌曲!"(半角) -> 归一后同 -> 命中。
			TrackSearchResult.CalculateSimilarityScores("歌曲！", "A", "Al", "歌曲!", "A", "Al", null, scores);
			Check.Equal(1f, scores[0], "full-width ! normalized -> title match");
		});

		// ===== SortBySimilarity:三分降序 + SourceOrder/SearchPass/ResultOrder 平手链 =====

		yield return ("SortBySimilarity: higher title score first (descending)", delegate
		{
			TrackSearchResult low = Track(0.5f, 0.5f, 0.5f, 0, 0, 0);
			TrackSearchResult high = Track(0.9f, 0.5f, 0.5f, 0, 0, 0);
			List<TrackSearchResult> list = new List<TrackSearchResult> { low, high };
			TrackSearchResult.SortBySimilarity(list);
			Check.True(ReferenceEquals(list[0], high) && ReferenceEquals(list[1], low), "0.9 title before 0.5");
		});

		yield return ("SortBySimilarity: score tie -> SourceOrder ascending", delegate
		{
			TrackSearchResult src1 = Track(0.5f, 0.5f, 0.5f, 1, 0, 0);
			TrackSearchResult src0 = Track(0.5f, 0.5f, 0.5f, 0, 0, 0);
			List<TrackSearchResult> list = new List<TrackSearchResult> { src1, src0 };
			TrackSearchResult.SortBySimilarity(list);
			Check.True(ReferenceEquals(list[0], src0), "SourceOrder 0 before 1 on score tie");
		});

		yield return ("SortBySimilarity: score+SourceOrder tie -> SearchPass ascending (before ResultOrder)", delegate
		{
			TrackSearchResult pass1 = Track(0.5f, 0.5f, 0.5f, 0, 1, 0);
			TrackSearchResult pass0 = Track(0.5f, 0.5f, 0.5f, 0, 0, 9);
			List<TrackSearchResult> list = new List<TrackSearchResult> { pass1, pass0 };
			TrackSearchResult.SortBySimilarity(list);
			Check.True(ReferenceEquals(list[0], pass0), "SearchPass 0 before 1 despite larger ResultOrder");
		});

		yield return ("SortBySimilarity: all tie except ResultOrder -> ResultOrder ascending", delegate
		{
			TrackSearchResult r5 = Track(0.5f, 0.5f, 0.5f, 0, 0, 5);
			TrackSearchResult r2 = Track(0.5f, 0.5f, 0.5f, 0, 0, 2);
			List<TrackSearchResult> list = new List<TrackSearchResult> { r5, r2 };
			TrackSearchResult.SortBySimilarity(list);
			Check.True(ReferenceEquals(list[0], r2), "ResultOrder 2 before 5");
		});

		// ===== SortByArtistSimilarity:artist 维度先于 title(GetScoreIndex 交换),对比 SortBySimilarity =====

		yield return ("SortByArtistSimilarity vs SortBySimilarity: dimension swap", delegate
		{
			TrackSearchResult artistHigh = Track(0.1f, 0.9f, 0.5f, 0, 0, 0);
			TrackSearchResult titleHigh = Track(0.9f, 0.1f, 0.5f, 0, 0, 0);
			List<TrackSearchResult> byArtist = new List<TrackSearchResult> { titleHigh, artistHigh };
			TrackSearchResult.SortByArtistSimilarity(byArtist);
			Check.True(ReferenceEquals(byArtist[0], artistHigh), "artist score leads in SortByArtistSimilarity");
			List<TrackSearchResult> byTitle = new List<TrackSearchResult> { artistHigh, titleHigh };
			TrackSearchResult.SortBySimilarity(byTitle);
			Check.True(ReferenceEquals(byTitle[0], titleHigh), "title score leads in SortBySimilarity (contrast)");
		});

		// ===== NormalizeForMatch:全角->半角 / 空白折叠 / 繁->简 / ToLower / Trim =====

		yield return ("NormalizeForMatch: full-width punctuation -> half-width", delegate
		{
			Check.Equal("(!)", TrackSearchResult.NormalizeForMatch("（！）"), "full-width ( ! ) -> half-width");
		});

		yield return ("NormalizeForMatch: whitespace collapsed", delegate
		{
			Check.Equal("a b", TrackSearchResult.NormalizeForMatch("a   b"), "runs of whitespace -> single space");
		});

		yield return ("NormalizeForMatch: uppercase -> lowercase", delegate
		{
			Check.Equal("abc", TrackSearchResult.NormalizeForMatch("ABC"), "ToLower");
		});

		yield return ("NormalizeForMatch: leading/trailing whitespace trimmed", delegate
		{
			Check.Equal("hi", TrackSearchResult.NormalizeForMatch("  hi  "), "Trim");
		});

		yield return ("NormalizeForMatch: traditional -> simplified", delegate
		{
			Check.Equal("爱", TrackSearchResult.NormalizeForMatch("愛"), "繁->简 (ConvertCharactersOnly)");
		});
	}
}
