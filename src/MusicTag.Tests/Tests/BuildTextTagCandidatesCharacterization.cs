using System;
using System.Collections.Generic;
using MusicTagWinApp.Adapter;
using MusicTagWinApp.Roles;

namespace MusicTag.Tests;

// AutoMatchTagsDialog.BuildTextTagCandidates characterization(提取自 StartAutoMatchTags 的
// "textTags" 内联字典构造,逐字等价搬移)。锁定:10 键全集与插入顺序、字符串字段 null->"" 合并
// 后 Trim、track/disc 的 int 原值 + 仅 >0 才非空的字符串形态。
internal static class BuildTextTagCandidatesCharacterization
{
	public static IEnumerable<(string, Action)> All()
	{
		yield return ("BuildTextTagCandidates: all fields populated -> trimmed strings + int/str twins", delegate
		{
			TrackSearchResult track = new TrackSearchResult
			{
				Title = " T ",
				Artist = " A ",
				Album = " Al ",
				Year = " 2020 ",
				Track = 3,
				Disc = 2,
				Genre = " G ",
				Comment = " C "
			};
			Dictionary<string, object> values = AutoMatchTagsDialog.BuildTextTagCandidates(track);
			Check.Equal(10, values.Count, "10 keys");
			Check.Equal("T", (string)values["title"], "title trimmed");
			Check.Equal("A", (string)values["artist"], "artist trimmed");
			Check.Equal("Al", (string)values["album"], "album trimmed");
			Check.Equal("2020", (string)values["year"], "year trimmed");
			Check.Equal(3, (int)values["track"], "track int");
			Check.Equal("3", (string)values["trackstr"], "trackstr from positive track");
			Check.Equal(2, (int)values["disc"], "disc int");
			Check.Equal("2", (string)values["discstr"], "discstr from positive disc");
			Check.Equal("G", (string)values["genre"], "genre trimmed");
			Check.Equal("C", (string)values["comment"], "comment trimmed");
		});

		yield return ("BuildTextTagCandidates: null strings -> empty, zero track/disc -> empty str twins", delegate
		{
			Dictionary<string, object> values = AutoMatchTagsDialog.BuildTextTagCandidates(new TrackSearchResult());
			Check.Equal("", (string)values["title"], "null title -> empty");
			Check.Equal("", (string)values["artist"], "null artist -> empty");
			Check.Equal("", (string)values["album"], "null album -> empty");
			Check.Equal("", (string)values["year"], "null year -> empty");
			Check.Equal(0, (int)values["track"], "track default 0");
			Check.Equal("", (string)values["trackstr"], "trackstr empty at 0");
			Check.Equal(0, (int)values["disc"], "disc default 0");
			Check.Equal("", (string)values["discstr"], "discstr empty at 0");
			Check.Equal("", (string)values["genre"], "null genre -> empty");
			Check.Equal("", (string)values["comment"], "null comment -> empty");
		});

		yield return ("BuildTextTagCandidates: key insertion order preserved (title..comment)", delegate
		{
			Dictionary<string, object> values = AutoMatchTagsDialog.BuildTextTagCandidates(new TrackSearchResult());
			Check.Equal("title,artist,album,year,track,trackstr,disc,discstr,genre,comment", string.Join(",", values.Keys), "insertion order");
		});

		yield return ("BuildTextTagCandidates: negative track/disc -> int kept, str twins empty", delegate
		{
			TrackSearchResult track = new TrackSearchResult
			{
				Track = -1,
				Disc = -2
			};
			Dictionary<string, object> values = AutoMatchTagsDialog.BuildTextTagCandidates(track);
			Check.Equal(-1, (int)values["track"], "negative track kept as int");
			Check.Equal("", (string)values["trackstr"], "trackstr empty at -1");
			Check.Equal(-2, (int)values["disc"], "negative disc kept as int");
			Check.Equal("", (string)values["discstr"], "discstr empty at -2");
		});
	}
}
