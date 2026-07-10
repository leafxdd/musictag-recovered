using System;
using System.Collections.Generic;
using System.IO;
using MusicTag.States;
using MusicTagWinApp.Properties;

namespace MusicTag.Tests;

// 保存前标签写入策略(设置-杂项1 三开关)的行为锁定:FLAC 错误 ID3 移除、
// 不写 ID3v1、保留已有 ID3v2 版本。fixture 与 RoundTrip 套件同思路——程序化
// 构造最小有效 MP3/FLAC,不依赖真实音频;Settings 只做内存赋值并 finally 复位
// (不调 Save,不落盘)。
internal static class Id3WritePolicyCharacterization
{
	// 与 ConfigDescriptorStateRoundTripCharacterization.BuildMinimalMp3 同配方:
	// MPEG-1 Layer III 128kbps/44100Hz joint-stereo 帧头 + 静音,4 帧 × 417 字节。
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

	// 最小合法 FLAC(42 字节):"fLaC" magic + last-block STREAMINFO 块头(类型 0,长 34)
	// + STREAMINFO(blocksize 4096/4096,44100Hz/2ch/16bps 位打包,总样本 0,MD5 全零)。
	// TagLib 按扩展名解析为 Flac.File,零音频帧不影响 parse 与 Save。
	private static byte[] BuildMinimalFlac()
	{
		byte[] flac = new byte[42];
		flac[0] = (byte)'f';
		flac[1] = (byte)'L';
		flac[2] = (byte)'a';
		flac[3] = (byte)'C';
		flac[4] = 0x80;
		flac[7] = 0x22;
		flac[8] = 0x10;
		flac[10] = 0x10;
		flac[18] = 0x0A;
		flac[19] = 0xC4;
		flac[20] = 0x42;
		flac[21] = 0xF0;
		return flac;
	}

	private static string WriteFixture(string extension, byte[] bytes)
	{
		string path = Path.Combine(Path.GetTempPath(), "mtchar_" + Guid.NewGuid().ToString("N") + extension);
		File.WriteAllBytes(path, bytes);
		return path;
	}

	private static TagLib.TagTypes ReadTagTypesOnDisk(string path)
	{
		using (TagLib.File file = TagLib.File.Create(path))
		{
			return file.TagTypesOnDisk;
		}
	}

	private static int ReadId3v2VersionOnDisk(string path)
	{
		// 进程默认 ForceDefaultVersion=false(SaveWithId3v2Version 在 finally 还原),
		// Version 读的是盘上 header 的真实版本。
		using (TagLib.File file = TagLib.File.Create(path))
		{
			TagLib.Id3v2.Tag id3v2 = (TagLib.Id3v2.Tag)file.GetTag(TagLib.TagTypes.Id3v2, create: false);
			Check.NotNull(id3v2, "ID3v2 present on disk");
			return id3v2.Version;
		}
	}

	public static IEnumerable<(string, Action)> All()
	{
		yield return ("Id3Policy: settings defaults (RemoveMisplacedId3OnSave=true, others=false)", delegate
		{
			// 附带的 MusicTag.config 不含这三个新键 -> XmlSettingsProvider 回退 DefaultSettingValue;
			// RemoveMisplacedId3OnSave 是首个默认非 False 的后加键,在此锁定回退路径。
			Check.True(Settings.Default.RemoveMisplacedId3OnSave, "RemoveMisplacedId3OnSave default true");
			Check.True(!Settings.Default.RemoveId3v1OnSave, "RemoveId3v1OnSave default false");
			Check.True(!Settings.Default.KeepExistingId3v2Version, "KeepExistingId3v2Version default false");
		});

		yield return ("Id3Policy: FLAC misplaced ID3v2+ID3v1 removed on save, Xiph fields survive", delegate
		{
			string path = WriteFixture(".flac", BuildMinimalFlac());
			bool previousRemoveMisplaced = Settings.Default.RemoveMisplacedId3OnSave;
			try
			{
				// 种入错误标签:combined Tag 写 title 会同时进 Xiph 与新建的 ID3v2/ID3v1。
				using (TagLib.File seeded = TagLib.File.Create(path))
				{
					seeded.GetTag(TagLib.TagTypes.Id3v2, create: true);
					seeded.GetTag(TagLib.TagTypes.Id3v1, create: true);
					seeded.Tag.Title = "DirtyTitle";
					seeded.Save();
				}
				TagLib.TagTypes seededTypes = ReadTagTypesOnDisk(path);
				Check.True((seededTypes & TagLib.TagTypes.Id3v2) != 0, "seeded ID3v2 on disk");
				Check.True((seededTypes & TagLib.TagTypes.Id3v1) != 0, "seeded ID3v1 on disk");

				Settings.Default.RemoveMisplacedId3OnSave = true;
				using (var state = new ConfigDescriptorState(path))
				{
					state.LoadBasicTagFields();
					state.LoadLyrics();
					Check.Equal("ID3v2.3,ID3v1,FLAC", state.GetDisplayValue("tagtypes"), "dirty tagtypes summary");
					state["title"] = "CleanTitle";
					Check.True(state.SaveTagFields(), "saved");
				}
				TagLib.TagTypes cleanedTypes = ReadTagTypesOnDisk(path);
				Check.True((cleanedTypes & TagLib.TagTypes.Id3v2) == 0, "ID3v2 physically removed");
				Check.True((cleanedTypes & TagLib.TagTypes.Id3v1) == 0, "ID3v1 physically removed");
				using (var reloaded = new ConfigDescriptorState(path))
				{
					reloaded.LoadBasicTagFields();
					Check.Equal("CleanTitle", reloaded.GetDisplayValue("title"), "Xiph title survives");
					Check.Equal("FLAC", reloaded.GetDisplayValue("tagtypes"), "clean tagtypes summary");
					// 干净文件再保存(清除标签路径同款空 writeBody):策略对不存在的标签是 no-op。
					Check.True(reloaded.SaveCurrentTagFile(), "clean re-save ok");
				}
				Check.True((ReadTagTypesOnDisk(path) & (TagLib.TagTypes.Id3v2 | TagLib.TagTypes.Id3v1)) == 0, "still clean after re-save");
			}
			finally
			{
				Settings.Default.RemoveMisplacedId3OnSave = previousRemoveMisplaced;
				File.Delete(path);
			}
		});

		yield return ("Id3Policy: FLAC misplaced ID3 kept when switch off", delegate
		{
			string path = WriteFixture(".flac", BuildMinimalFlac());
			bool previousRemoveMisplaced = Settings.Default.RemoveMisplacedId3OnSave;
			try
			{
				using (TagLib.File seeded = TagLib.File.Create(path))
				{
					seeded.GetTag(TagLib.TagTypes.Id3v2, create: true);
					seeded.Tag.Title = "DirtyTitle";
					seeded.Save();
				}
				Settings.Default.RemoveMisplacedId3OnSave = false;
				using (var state = new ConfigDescriptorState(path))
				{
					state.LoadBasicTagFields();
					state.LoadLyrics();
					state["title"] = "EditedTitle";
					Check.True(state.SaveTagFields(), "saved");
				}
				Check.True((ReadTagTypesOnDisk(path) & TagLib.TagTypes.Id3v2) != 0, "ID3v2 kept (legacy behavior)");
			}
			finally
			{
				Settings.Default.RemoveMisplacedId3OnSave = previousRemoveMisplaced;
				File.Delete(path);
			}
		});

		yield return ("Id3Policy: MP3 ID3v1 written by default, removed when RemoveId3v1OnSave", delegate
		{
			string path = WriteFixture(".mp3", BuildMinimalMp3());
			bool previousRemoveId3v1 = Settings.Default.RemoveId3v1OnSave;
			try
			{
				// 现状锁定:TagLib 对 mp3 自动补建 ID3v1,默认保存写出。
				Settings.Default.RemoveId3v1OnSave = false;
				using (var state = new ConfigDescriptorState(path))
				{
					state.LoadBasicTagFields();
					state.LoadLyrics();
					state["title"] = "Title1";
					Check.True(state.SaveTagFields(), "saved with ID3v1");
				}
				Check.True((ReadTagTypesOnDisk(path) & TagLib.TagTypes.Id3v1) != 0, "ID3v1 on disk by default");

				Settings.Default.RemoveId3v1OnSave = true;
				using (var state = new ConfigDescriptorState(path))
				{
					state.LoadBasicTagFields();
					state.LoadLyrics();
					Check.True(state.SaveTagFields(), "saved without ID3v1");
				}
				TagLib.TagTypes types = ReadTagTypesOnDisk(path);
				Check.True((types & TagLib.TagTypes.Id3v1) == 0, "ID3v1 physically removed");
				Check.True((types & TagLib.TagTypes.Id3v2) != 0, "ID3v2 survives");
				using (var reloaded = new ConfigDescriptorState(path))
				{
					reloaded.LoadBasicTagFields();
					Check.Equal("Title1", reloaded.GetDisplayValue("title"), "title survives in ID3v2");
				}
			}
			finally
			{
				Settings.Default.RemoveId3v1OnSave = previousRemoveId3v1;
				File.Delete(path);
			}
		});

		yield return ("Id3Policy: MP3 version forced by default, preserved with KeepExistingId3v2Version", delegate
		{
			string path = WriteFixture(".mp3", BuildMinimalMp3());
			int previousId3v2Version = Settings.Default.ID3v2Version;
			bool previousKeepVersion = Settings.Default.KeepExistingId3v2Version;
			try
			{
				// 种 v2.4 文件。
				Settings.Default.KeepExistingId3v2Version = false;
				Settings.Default.ID3v2Version = 4;
				using (var state = new ConfigDescriptorState(path))
				{
					state.LoadBasicTagFields();
					state.LoadLyrics();
					state["title"] = "T";
					Check.True(state.SaveTagFields(), "seed v2.4");
				}
				Check.Equal(4, ReadId3v2VersionOnDisk(path), "seeded as v2.4");

				// 现状锁定:选 2.3 且不保留 -> 已有 v2.4 被强制转成 v2.3。
				Settings.Default.ID3v2Version = 3;
				using (var state = new ConfigDescriptorState(path))
				{
					state.LoadBasicTagFields();
					state.LoadLyrics();
					Check.True(state.SaveTagFields(), "force-convert save");
				}
				Check.Equal(3, ReadId3v2VersionOnDisk(path), "forced to v2.3");

				// 种回 v2.4,再开"保留已有版本":选 2.3 保存后仍是 v2.4。
				Settings.Default.ID3v2Version = 4;
				using (var state = new ConfigDescriptorState(path))
				{
					state.LoadBasicTagFields();
					state.LoadLyrics();
					Check.True(state.SaveTagFields(), "re-seed v2.4");
				}
				Check.Equal(4, ReadId3v2VersionOnDisk(path), "re-seeded as v2.4");
				Settings.Default.ID3v2Version = 3;
				Settings.Default.KeepExistingId3v2Version = true;
				using (var state = new ConfigDescriptorState(path))
				{
					state.LoadBasicTagFields();
					state.LoadLyrics();
					state["title"] = "T2";
					Check.True(state.SaveTagFields(), "keep-version save");
				}
				Check.Equal(4, ReadId3v2VersionOnDisk(path), "existing v2.4 preserved");
				using (var reloaded = new ConfigDescriptorState(path))
				{
					reloaded.LoadBasicTagFields();
					Check.Equal("T2", reloaded.GetDisplayValue("title"), "edit still lands");
				}
			}
			finally
			{
				Settings.Default.ID3v2Version = previousId3v2Version;
				Settings.Default.KeepExistingId3v2Version = previousKeepVersion;
				File.Delete(path);
			}
		});
	}
}
