using System;
using System.Collections.Generic;
using MusicTag.States;

namespace MusicTag.Tests;

// ConfigDescriptorState 的 3 个 raw-field 标识符映射(Id3v2FrameId/XiphFieldId/ApeFieldId)characterization。
// 这三个纯 field->常量映射由 private static 放宽为 internal static(仅可见性、零逻辑改动),锁定 golden master
// ——Tier C「字段词汇表」把三者合成单张 rawFieldVocabulary 表后,此网确保每列映射 + 不对称逐字节不变。
// 关键不对称:comment/lyrics 在 Id3v2 返回 null(走 CommentsFrame/UnsynchronisedLyricsFrame 特殊 frame,
// 不经 Id3v2FrameId),在 Xiph/Ape 有普通映射;track/disc/trackstr/discstr 纯读派生、不在任何 raw 映射。
// 不规则点(词汇表易错):Xiph year->DATE(非 YEAR)、Id3v2 year->TDRC、Ape albumartist->"Album Artist"(带空格)。
internal static class ConfigDescriptorFrameIdCharacterization
{
	public static IEnumerable<(string, Action)> All()
	{
		// ---- Id3v2FrameId: 8 个普通 text frame 映射 ----
		yield return ("Id3v2FrameId: title -> TIT2", delegate { Check.Equal("TIT2", ConfigDescriptorState.Id3v2FrameId("title"), "title"); });
		yield return ("Id3v2FrameId: artist -> TPE1", delegate { Check.Equal("TPE1", ConfigDescriptorState.Id3v2FrameId("artist"), "artist"); });
		yield return ("Id3v2FrameId: album -> TALB", delegate { Check.Equal("TALB", ConfigDescriptorState.Id3v2FrameId("album"), "album"); });
		yield return ("Id3v2FrameId: year -> TDRC", delegate { Check.Equal("TDRC", ConfigDescriptorState.Id3v2FrameId("year"), "year"); });
		yield return ("Id3v2FrameId: genre -> TCON", delegate { Check.Equal("TCON", ConfigDescriptorState.Id3v2FrameId("genre"), "genre"); });
		yield return ("Id3v2FrameId: albumartist -> TPE2", delegate { Check.Equal("TPE2", ConfigDescriptorState.Id3v2FrameId("albumartist"), "albumartist"); });
		yield return ("Id3v2FrameId: composer -> TCOM", delegate { Check.Equal("TCOM", ConfigDescriptorState.Id3v2FrameId("composer"), "composer"); });
		yield return ("Id3v2FrameId: lyricist -> TEXT", delegate { Check.Equal("TEXT", ConfigDescriptorState.Id3v2FrameId("lyricist"), "lyricist"); });
		// comment/lyrics: Id3v2 走特殊 frame -> null(不对称)
		yield return ("Id3v2FrameId: comment -> null (special CommentsFrame)", delegate { Check.Null(ConfigDescriptorState.Id3v2FrameId("comment"), "comment"); });
		yield return ("Id3v2FrameId: lyrics -> null (special UnsynchronisedLyricsFrame)", delegate { Check.Null(ConfigDescriptorState.Id3v2FrameId("lyrics"), "lyrics"); });
		// 派生/未知/大小写
		yield return ("Id3v2FrameId: track (derived, not raw) -> null", delegate { Check.Null(ConfigDescriptorState.Id3v2FrameId("track"), "track"); });
		yield return ("Id3v2FrameId: discstr (derived) -> null", delegate { Check.Null(ConfigDescriptorState.Id3v2FrameId("discstr"), "discstr"); });
		yield return ("Id3v2FrameId: unknown -> null", delegate { Check.Null(ConfigDescriptorState.Id3v2FrameId("bogus"), "bogus"); });
		yield return ("Id3v2FrameId: case-sensitive TITLE != title -> null", delegate { Check.Null(ConfigDescriptorState.Id3v2FrameId("TITLE"), "TITLE"); });
		yield return ("Id3v2FrameId: empty -> null", delegate { Check.Null(ConfigDescriptorState.Id3v2FrameId(""), "empty"); });

		// ---- XiphFieldId: 10 映射(注意 year->DATE 非 YEAR) ----
		yield return ("XiphFieldId: title -> TITLE", delegate { Check.Equal("TITLE", ConfigDescriptorState.XiphFieldId("title"), "title"); });
		yield return ("XiphFieldId: artist -> ARTIST", delegate { Check.Equal("ARTIST", ConfigDescriptorState.XiphFieldId("artist"), "artist"); });
		yield return ("XiphFieldId: album -> ALBUM", delegate { Check.Equal("ALBUM", ConfigDescriptorState.XiphFieldId("album"), "album"); });
		yield return ("XiphFieldId: year -> DATE (not YEAR)", delegate { Check.Equal("DATE", ConfigDescriptorState.XiphFieldId("year"), "year"); });
		yield return ("XiphFieldId: genre -> GENRE", delegate { Check.Equal("GENRE", ConfigDescriptorState.XiphFieldId("genre"), "genre"); });
		yield return ("XiphFieldId: albumartist -> ALBUMARTIST", delegate { Check.Equal("ALBUMARTIST", ConfigDescriptorState.XiphFieldId("albumartist"), "albumartist"); });
		yield return ("XiphFieldId: composer -> COMPOSER", delegate { Check.Equal("COMPOSER", ConfigDescriptorState.XiphFieldId("composer"), "composer"); });
		yield return ("XiphFieldId: lyricist -> LYRICIST", delegate { Check.Equal("LYRICIST", ConfigDescriptorState.XiphFieldId("lyricist"), "lyricist"); });
		yield return ("XiphFieldId: comment -> COMMENT", delegate { Check.Equal("COMMENT", ConfigDescriptorState.XiphFieldId("comment"), "comment"); });
		yield return ("XiphFieldId: lyrics -> LYRICS", delegate { Check.Equal("LYRICS", ConfigDescriptorState.XiphFieldId("lyrics"), "lyrics"); });
		yield return ("XiphFieldId: track (derived) -> null", delegate { Check.Null(ConfigDescriptorState.XiphFieldId("track"), "track"); });
		yield return ("XiphFieldId: unknown -> null", delegate { Check.Null(ConfigDescriptorState.XiphFieldId("bogus"), "bogus"); });

		// ---- ApeFieldId: 10 映射(albumartist 带空格) ----
		yield return ("ApeFieldId: title -> Title", delegate { Check.Equal("Title", ConfigDescriptorState.ApeFieldId("title"), "title"); });
		yield return ("ApeFieldId: artist -> Artist", delegate { Check.Equal("Artist", ConfigDescriptorState.ApeFieldId("artist"), "artist"); });
		yield return ("ApeFieldId: album -> Album", delegate { Check.Equal("Album", ConfigDescriptorState.ApeFieldId("album"), "album"); });
		yield return ("ApeFieldId: year -> Year", delegate { Check.Equal("Year", ConfigDescriptorState.ApeFieldId("year"), "year"); });
		yield return ("ApeFieldId: genre -> Genre", delegate { Check.Equal("Genre", ConfigDescriptorState.ApeFieldId("genre"), "genre"); });
		yield return ("ApeFieldId: albumartist -> \"Album Artist\" (space)", delegate { Check.Equal("Album Artist", ConfigDescriptorState.ApeFieldId("albumartist"), "albumartist"); });
		yield return ("ApeFieldId: composer -> Composer", delegate { Check.Equal("Composer", ConfigDescriptorState.ApeFieldId("composer"), "composer"); });
		yield return ("ApeFieldId: lyricist -> Lyricist", delegate { Check.Equal("Lyricist", ConfigDescriptorState.ApeFieldId("lyricist"), "lyricist"); });
		yield return ("ApeFieldId: comment -> Comment", delegate { Check.Equal("Comment", ConfigDescriptorState.ApeFieldId("comment"), "comment"); });
		yield return ("ApeFieldId: lyrics -> Lyrics", delegate { Check.Equal("Lyrics", ConfigDescriptorState.ApeFieldId("lyrics"), "lyrics"); });
		yield return ("ApeFieldId: disc (derived) -> null", delegate { Check.Null(ConfigDescriptorState.ApeFieldId("disc"), "disc"); });
		yield return ("ApeFieldId: unknown -> null", delegate { Check.Null(ConfigDescriptorState.ApeFieldId("bogus"), "bogus"); });
	}
}
