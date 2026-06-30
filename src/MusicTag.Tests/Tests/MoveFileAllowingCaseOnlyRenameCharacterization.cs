using System;
using System.Collections.Generic;
using System.IO;
using MusicTagWinApp.Instances;

namespace MusicTag.Tests;

// PathFileUtilities.MoveFileAllowingCaseOnlyRename 的【集成测试】(真实临时文件系统)。
// 它是 RenameFiles 改名/移动的实际核心原语:case-only rename(源/目标仅大小写不同)在 Windows
// 大小写不敏感文件系统上无法直接 File.Move,故经【同目录随机 temp 两步中转】(source->temp->dest),
// 第二步失败回滚到源名;非 case-only 直接 File.Move。每个 case 自建临时目录、try/finally 删除,CI 可复现。
// 注:这是真实文件 I/O 集成测试(非纯逻辑),与 ConfigDescriptorState round-trip 同属集成层。
internal static class MoveFileAllowingCaseOnlyRenameCharacterization
{
	private static string NewTempDir()
	{
		string dir = Path.Combine(Path.GetTempPath(), "mtmove_" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(dir);
		return dir;
	}

	public static IEnumerable<(string, Action)> All()
	{
		// --- 普通改名(不同名):File.Move 直通,内容保留,源消失 ---
		yield return ("MoveFileAllowingCaseOnlyRename: plain rename (different name) moves content", delegate
		{
			string dir = NewTempDir();
			try
			{
				string src = Path.Combine(dir, "a.mp3");
				string dst = Path.Combine(dir, "b.mp3");
				File.WriteAllText(src, "AUDIO");
				PathFileUtilities.MoveFileAllowingCaseOnlyRename(src, dst);
				Check.True(File.Exists(dst), "dest exists");
				Check.True(!File.Exists(src), "source gone");
				Check.Equal("AUDIO", File.ReadAllText(dst), "content preserved");
			}
			finally { Directory.Delete(dir, true); }
		});

		// --- case-only rename:Windows 大小写不敏感,经 temp 中转使磁盘 casing 真正变更,无 temp 残留 ---
		yield return ("MoveFileAllowingCaseOnlyRename: case-only rename changes on-disk casing via temp", delegate
		{
			string dir = NewTempDir();
			try
			{
				string src = Path.Combine(dir, "song.mp3");
				string dst = Path.Combine(dir, "Song.mp3");
				File.WriteAllText(src, "LOWER");
				PathFileUtilities.MoveFileAllowingCaseOnlyRename(src, dst);
				string[] files = Directory.GetFiles(dir);
				Check.Equal(1, files.Length, "exactly one file (no temp leftover)");
				Check.Equal("Song.mp3", Path.GetFileName(files[0]), "on-disk casing is Song.mp3");
				Check.Equal("LOWER", File.ReadAllText(dst), "content preserved");
			}
			finally { Directory.Delete(dir, true); }
		});

		// --- case-only + 相对路径(无目录段) -> ArgumentException(拒绝相对路径,防 temp 落到工作目录) ---
		yield return ("MoveFileAllowingCaseOnlyRename: case-only with relative path -> ArgumentException", delegate
		{
			bool threwArg = false;
			try
			{
				PathFileUtilities.MoveFileAllowingCaseOnlyRename("song.mp3", "Song.mp3");
			}
			catch (ArgumentException)
			{
				threwArg = true;
			}
			Check.True(threwArg, "relative case-only dest rejected with ArgumentException");
		});

		// --- 源不存在 -> 抛(非 case-only,直通 File.Move) ---
		yield return ("MoveFileAllowingCaseOnlyRename: missing source -> throws", delegate
		{
			string dir = NewTempDir();
			try
			{
				string src = Path.Combine(dir, "missing.mp3");
				string dst = Path.Combine(dir, "x.mp3");
				bool threw = false;
				try
				{
					PathFileUtilities.MoveFileAllowingCaseOnlyRename(src, dst);
				}
				catch (Exception)
				{
					threw = true;
				}
				Check.True(threw, "missing source throws");
			}
			finally { Directory.Delete(dir, true); }
		});

		// --- 目标已存在(非 case-only) -> 抛(File.Move 不覆盖),源保留、目标内容不变 ---
		yield return ("MoveFileAllowingCaseOnlyRename: existing dest (non-case-only) -> throws, source kept", delegate
		{
			string dir = NewTempDir();
			try
			{
				string src = Path.Combine(dir, "a.mp3");
				string dst = Path.Combine(dir, "b.mp3");
				File.WriteAllText(src, "SRC");
				File.WriteAllText(dst, "DST");
				bool threw = false;
				try
				{
					PathFileUtilities.MoveFileAllowingCaseOnlyRename(src, dst);
				}
				catch (Exception)
				{
					threw = true;
				}
				Check.True(threw, "existing dest throws");
				Check.True(File.Exists(src), "source kept after failed move");
				Check.Equal("DST", File.ReadAllText(dst), "dest unchanged");
			}
			finally { Directory.Delete(dir, true); }
		});
	}
}
