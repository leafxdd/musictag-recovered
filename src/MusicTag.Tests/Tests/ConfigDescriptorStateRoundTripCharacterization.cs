using System;
using System.Collections.Generic;
using System.IO;
using MusicTag.States;

namespace MusicTag.Tests;

// ConfigDescriptorState 的标签读写 round-trip characterization（自包含音频 fixture）。
// 在临时文件上跑 LoadBasicTagFields -> 改字段 -> SaveTagFields -> 重新打开读回的完整往返,
// 锁定 ChangeTags(批量改标签)端到端写盘行为。fixture 为程序化构造的最小有效 MP3
// (MPEG-1 Layer III 帧头 + 静音),不依赖 gitignored 的真实音频,CI 可复现。
internal static class ConfigDescriptorStateRoundTripCharacterization
{
	// MPEG-1 Layer III, 128kbps, 44100Hz, joint-stereo 帧头(0xFF 0xFB 0x90 0x64) + 静音填充,共 4 帧。
	// 帧长 = 144*128000/44100 = 417 字节。TagLib.File.Create 实测可识别为有效 MP3 并读音频属性;
	// 首次 SaveTagFields 时 TagLib 追加 ID3v2,故覆盖 ID3v2 写路径。
	private static byte[] BuildMinimalMp3()
	{
		byte[] header = { 0xFF, 0xFB, 0x90, 0x64 };
		int frameSize = 417;
		int frameCount = 4;
		byte[] mp3 = new byte[frameSize * frameCount];
		for (int i = 0; i < frameCount; i++)
		{
			Array.Copy(header, 0, mp3, i * frameSize, header.Length);
		}
		return mp3;
	}

	private static string WriteFixture()
	{
		string path = Path.Combine(Path.GetTempPath(), "mtchar_" + Guid.NewGuid().ToString("N") + ".mp3");
		File.WriteAllBytes(path, BuildMinimalMp3());
		return path;
	}

	public static IEnumerable<(string, Action)> All()
	{
		yield return ("RoundTrip: basic text fields survive save/reload", delegate
		{
			string path = WriteFixture();
			try
			{
				using (var state = new ConfigDescriptorState(path))
				{
					Check.True(state.IsLoadedSuccessfully(), "loaded");
					state.LoadBasicTagFields();
					state.LoadLyrics();
					state["title"] = "MyTitle";
					state["artist"] = "MyArtist";
					state["album"] = "MyAlbum";
					state["year"] = "2021";
					state["genre"] = "Rock";
					state["albumartist"] = "MyAlbumArtist";
					state["composer"] = "MyComposer";
					state["comment"] = "MyComment";
					state["lyrics"] = "la la la";
					Check.True(state.SaveTagFields(), "saved");
				}
				using (var reloaded = new ConfigDescriptorState(path))
				{
					reloaded.LoadBasicTagFields();
					reloaded.LoadLyrics();
					Check.Equal("MyTitle", reloaded.GetDisplayValue("title"), "title");
					Check.Equal("MyArtist", reloaded.GetDisplayValue("artist"), "artist");
					Check.Equal("MyAlbum", reloaded.GetDisplayValue("album"), "album");
					Check.Equal("2021", reloaded.GetDisplayValue("year"), "year");
					Check.Equal("Rock", reloaded.GetDisplayValue("genre"), "genre");
					Check.Equal("MyAlbumArtist", reloaded.GetDisplayValue("albumartist"), "albumartist");
					Check.Equal("MyComposer", reloaded.GetDisplayValue("composer"), "composer");
					Check.Equal("MyComment", reloaded.GetDisplayValue("comment"), "comment");
					Check.Equal("la la la", reloaded.GetDisplayValue("lyrics"), "lyrics");
				}
			}
			finally
			{
				File.Delete(path);
			}
		});

		yield return ("RoundTrip: trackstr/discstr with counts \"3/12\" \"1/2\"", delegate
		{
			string path = WriteFixture();
			try
			{
				using (var state = new ConfigDescriptorState(path))
				{
					state.LoadBasicTagFields();
					state.LoadLyrics();
					state["trackstr"] = "3/12";
					state["discstr"] = "1/2";
					Check.True(state.SaveTagFields(), "saved");
				}
				using (var reloaded = new ConfigDescriptorState(path))
				{
					reloaded.LoadBasicTagFields();
					Check.Equal("3/12", reloaded.GetDisplayValue("trackstr"), "trackstr");
					Check.Equal("1/2", reloaded.GetDisplayValue("discstr"), "discstr");
					Check.Equal(3, (int)reloaded["track"], "track int");
					Check.Equal(1, (int)reloaded["disc"], "disc int");
				}
			}
			finally
			{
				File.Delete(path);
			}
		});

		yield return ("RoundTrip: trackstr without count \"5\" -> \"5\", empty discstr", delegate
		{
			string path = WriteFixture();
			try
			{
				using (var state = new ConfigDescriptorState(path))
				{
					state.LoadBasicTagFields();
					state.LoadLyrics();
					state["trackstr"] = "5";
					state["discstr"] = "";
					Check.True(state.SaveTagFields(), "saved");
				}
				using (var reloaded = new ConfigDescriptorState(path))
				{
					reloaded.LoadBasicTagFields();
					Check.Equal("5", reloaded.GetDisplayValue("trackstr"), "trackstr");
					Check.Equal(5, (int)reloaded["track"], "track int");
					Check.Equal("", reloaded.GetDisplayValue("discstr"), "discstr empty");
				}
			}
			finally
			{
				File.Delete(path);
			}
		});

		yield return ("RoundTrip: 163-key COMM (non-empty description) dropped on comment edit", delegate
		{
			string path = WriteFixture();
			try
			{
				// 预置一个带非空 description 的 COMM(模拟网易云 163 key)+ 一个默认 comment,经 TagLib 直接写盘。
				using (TagLib.File seeded = TagLib.File.Create(path))
				{
					TagLib.Id3v2.Tag id3v2 = (TagLib.Id3v2.Tag)seeded.GetTag(TagLib.TagTypes.Id3v2, create: true);
					TagLib.Id3v2.CommentsFrame key163 = new TagLib.Id3v2.CommentsFrame("163 key", "XXX")
					{
						Text = "encrypted-blob"
					};
					id3v2.AddFrame(key163);
					seeded.Tag.Comment = "OldComment";
					seeded.Save();
				}
				// 经 ConfigDescriptorState 改 comment 并保存:SaveTagFields 内部 RemoveFrames("COMM") 清掉全部旧 COMM。
				using (var state = new ConfigDescriptorState(path))
				{
					state.LoadBasicTagFields();
					state.LoadLyrics();
					state["comment"] = "NewComment";
					Check.True(state.SaveTagFields(), "saved");
				}
				// 验证:163-key COMM 已消失,只剩新 comment(锁定 native 行为:163 key 不随 comment 编辑存活)。
				using (TagLib.File verify = TagLib.File.Create(path))
				{
					TagLib.Id3v2.Tag id3v2 = (TagLib.Id3v2.Tag)verify.GetTag(TagLib.TagTypes.Id3v2, create: false);
					string key163Text = null;
					foreach (TagLib.Id3v2.CommentsFrame comm in id3v2.GetFrames<TagLib.Id3v2.CommentsFrame>("COMM"))
					{
						if (comm.Description == "163 key")
						{
							key163Text = comm.Text;
						}
					}
					Check.Null(key163Text, "163-key COMM removed");
					Check.Equal("NewComment", verify.Tag.Comment, "new comment present");
				}
			}
			finally
			{
				File.Delete(path);
			}
		});

		yield return ("RoundTrip: ClearTagFields removes text, lyrics and pictures from disk", delegate
		{
			string path = WriteFixture();
			try
			{
				using (var seeded = new ConfigDescriptorState(path))
				{
					seeded.LoadBasicTagFields();
					seeded.LoadLyrics();
					seeded["title"] = "ToBeCleared";
					seeded["artist"] = "Artist";
					seeded["lyrics"] = "lyrics";
					seeded["allpicturedata"] = new List<ConfigDescriptorState.PictureData>
					{
						new ConfigDescriptorState.PictureData
						{
							ImageBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="),
							PictureType = "Front Cover"
						}
					};
					Check.True(seeded.SaveTagFields(), "seeded");
				}
				using (var state = new ConfigDescriptorState(path))
				{
					Check.True(state.ClearTagFields(), "cleared");
				}
				using (TagLib.File verify = TagLib.File.Create(path))
				{
					Check.True(string.IsNullOrEmpty(verify.Tag.Title), "title removed");
					Check.True(verify.Tag.Performers.Length == 0, "artist removed");
					Check.True(string.IsNullOrEmpty(verify.Tag.Lyrics), "lyrics removed");
					Check.True(verify.Tag.Pictures.Length == 0, "pictures removed");
					Check.Equal(TagLib.TagTypes.None, verify.TagTypesOnDisk, "no tags remain on disk");
				}
			}
			finally
			{
				File.Delete(path);
			}
		});
	}
}
