using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using MusicTag.States;
using MusicTagWinApp.Instances;

namespace MusicTag.Tests;

// 未保存标签修改缓存(StateFieldInstance.PendingEdits)的纯逻辑锁定,外加"文件被占用"两种保存失败形态的
// 现状:播放器只读共享打开 -> 能读不能写(SaveTagFields 失败并带错误信息);独占打开 -> 连读取都失败。
// 这两种失败都要让编辑区的修改留在缓存里,下次重新保存。
internal static class PendingTagEditCharacterization
{
	// 与 Id3WritePolicyCharacterization.BuildMinimalFlac 同配方:42 字节最小合法 FLAC。
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

	private static string WriteFlacFixture()
	{
		string path = Path.Combine(Path.GetTempPath(), "mtpending_" + Guid.NewGuid().ToString("N") + ".flac");
		File.WriteAllBytes(path, BuildMinimalFlac());
		return path;
	}

	private static string ReadTitle(string path)
	{
		using (var state = new ConfigDescriptorState(path))
		{
			state.LoadBasicTagFields();
			return state.GetDisplayValue("title");
		}
	}

	public static IEnumerable<(string, Action)> All()
	{
		yield return ("PendingEdit: field differing from file value is recorded", delegate
		{
			var pending = StateFieldInstance.UpdatePendingField(null, "title", "New", "Old");
			Check.NotNull(pending, "pending created");
			Check.Equal("New", pending.Fields["title"], "recorded text");
			Check.Equal(1, pending.Fields.Count, "one field");
		});

		yield return ("PendingEdit: typing back the file value withdraws the field, empty cache becomes null", delegate
		{
			var pending = StateFieldInstance.UpdatePendingField(null, "title", "New", "Old");
			pending = StateFieldInstance.UpdatePendingField(pending, "artist", "A2", "A1");
			pending = StateFieldInstance.UpdatePendingField(pending, "title", "Old", "Old");
			Check.NotNull(pending, "artist still pending");
			Check.True(!pending.Fields.ContainsKey("title"), "title withdrawn");
			pending = StateFieldInstance.UpdatePendingField(pending, "artist", "A1", "A1");
			Check.Null(pending, "all withdrawn -> null");
			Check.Null(StateFieldInstance.UpdatePendingField(null, "title", "Same", "Same"), "unchanged on empty cache stays null");
		});

		yield return ("PendingEdit: comparison is ordinal (case-only edit counts), covers keep the cache alive", delegate
		{
			var pending = StateFieldInstance.UpdatePendingField(null, "title", "Hello World", "hello world");
			Check.NotNull(pending, "case-only edit is a change");
			pending.Covers = new List<ConfigDescriptorState.PictureData>();
			pending = StateFieldInstance.UpdatePendingField(pending, "title", "hello world", "hello world");
			Check.NotNull(pending, "covers still pending");
			Check.True(pending.Fields.Count == 0 && !pending.IsEmpty, "no fields but covers");
		});

		yield return ("PendingEdit: Clone copies fields and cover list", delegate
		{
			var pending = StateFieldInstance.UpdatePendingField(null, "title", "T", "");
			var picture = new ConfigDescriptorState.PictureData { PictureType = "Front Cover" };
			pending.Covers = new List<ConfigDescriptorState.PictureData> { picture };
			var copy = pending.Clone();
			pending.Fields["title"] = "changed";
			pending.Covers.Clear();
			Check.Equal("T", copy.Fields["title"], "field copied");
			Check.Equal(1, copy.Covers.Count, "cover list copied");
			Check.True(ReferenceEquals(picture, copy.Covers[0]), "pictures shared");
		});

		yield return ("PendingEdit: multi view common value counts pending values first", delegate
		{
			// 选中 3 个:两个文件里是 A、一个是 B;两个有缓存(A->X, B->X)。有效值 = {A, X, X}。
			var counts = new Dictionary<string, int> { { "A", 2 }, { "B", 1 } };
			var mixed = StateFieldInstance.ResolveCommonValueWithPendingEdits(counts, new List<(string, string)> { ("A", "X"), ("B", "X") });
			Check.True(!mixed.HasCommonValue, "A + X + X -> no common value");

			// 三个都有缓存且一致 -> 公共值是缓存值,即使文件里各不相同。
			var allPending = StateFieldInstance.ResolveCommonValueWithPendingEdits(counts, new List<(string, string)> { ("A", "X"), ("A", "X"), ("B", "X") });
			Check.True(allPending.HasCommonValue, "all X");
			Check.Equal("X", allPending.Value, "common pending value");

			// 缓存值恰好等于其他文件里的值 -> 一致。
			var sameAsDisk = StateFieldInstance.ResolveCommonValueWithPendingEdits(counts, new List<(string, string)> { ("B", "A") });
			Check.True(sameAsDisk.HasCommonValue, "B->A joins the two A");
			Check.Equal("A", sameAsDisk.Value, "common value A");
		});

		yield return ("PendingEdit: multi view normalizes blanks and tolerates stale disk counts", delegate
		{
			var counts = new Dictionary<string, int> { { "", 1 }, { "A", 1 } };
			var blanks = StateFieldInstance.ResolveCommonValueWithPendingEdits(counts, new List<(string, string)> { ("A", "   ") });
			Check.True(blanks.HasCommonValue, "whitespace pending equals empty");
			Check.Equal("", blanks.Value, "normalized to empty");

			// 计数里找不到文件值(计数已过期)时不减,结果偏保守:多出一个值 -> <keep>。
			var stale = StateFieldInstance.ResolveCommonValueWithPendingEdits(new Dictionary<string, int> { { "A", 1 } }, new List<(string, string)> { ("Z", "A2") });
			Check.True(!stale.HasCommonValue, "stale count stays conservative");
			Check.Equal(1, counts["A"], "input counts not mutated");
		});

		yield return ("PendingEdit: multi save changes skip unchanged and <keep>, map <blank> to empty", delegate
		{
			var baseline = new Dictionary<string, string>
			{
				{ "title", "<keep>" },
				{ "album", "Same Album" },
				{ "genre", "<keep>" },
				{ "year", "2020" },
				{ "comment", "<keep>" }
			};
			var current = new List<KeyValuePair<string, string>>
			{
				new KeyValuePair<string, string>("title", "<keep>"),
				new KeyValuePair<string, string>("album", "Same Album"),
				new KeyValuePair<string, string>("genre", "Pop"),
				new KeyValuePair<string, string>("year", "<keep>"),
				new KeyValuePair<string, string>("comment", "<blank>"),
				new KeyValuePair<string, string>("artist", "<keep> (Live)")
			};
			var changes = StateFieldInstance.CollectMultiSelectionChanges(current, baseline);
			Check.Equal(3, changes.Count, "genre, comment, artist");
			Check.Equal("genre=Pop", changes[0].Key + "=" + changes[0].Value, "changed literal");
			Check.Equal("comment=", changes[1].Key + "=" + changes[1].Value, "<blank> -> empty");
			Check.Equal("artist=<keep> (Live)", changes[2].Key + "=" + changes[2].Value, "template kept raw, missing baseline counts as changed");
		});

		yield return ("PendingEdit: multi save validates cached track/disc only where the multi view keeps them", delegate
		{
			var pending = StateFieldInstance.UpdatePendingField(null, "trackstr", "abc", "1");
			pending = StateFieldInstance.UpdatePendingField(pending, "discstr", "2", "1");
			Check.Equal("trackstr", StateFieldInstance.FindInvalidPendingNumberedField(pending, "<keep>", "<keep>"), "invalid cached track written through <keep>");
			Check.Equal("trackstr", StateFieldInstance.FindInvalidPendingNumberedField(pending, "<keep>/12", "<keep>"), "template still uses the cached track");
			Check.Null(StateFieldInstance.FindInvalidPendingNumberedField(pending, "7", "<keep>"), "explicit multi value overrides the cached track");
			Check.Null(StateFieldInstance.FindInvalidPendingNumberedField(pending, "<blank>", "<keep>"), "<blank> overrides too");
			var badDisc = StateFieldInstance.UpdatePendingField(null, "discstr", "x", "1");
			Check.Equal("discstr", StateFieldInstance.FindInvalidPendingNumberedField(badDisc, "<keep>", "<keep>"), "invalid cached disc");
			Check.Null(StateFieldInstance.FindInvalidPendingNumberedField(null, "<keep>", "<keep>"), "no cache -> nothing to check");
			var valid = StateFieldInstance.UpdatePendingField(null, "trackstr", "3/10", "1");
			Check.Null(StateFieldInstance.FindInvalidPendingNumberedField(valid, "<keep>", "<keep>"), "valid cached track passes");
		});

		yield return ("PendingEdit: unloaded single file maps empty fields to <keep>", delegate
		{
			Check.Equal("<keep>", StateFieldInstance.ResolveUnloadedFileFieldValue(""), "empty -> keep");
			Check.Equal("Title", StateFieldInstance.ResolveUnloadedFileFieldValue("Title"), "typed value kept");
			Check.Equal(" ", StateFieldInstance.ResolveUnloadedFileFieldValue(" "), "whitespace is a typed value");
			Check.Equal("<blank>", StateFieldInstance.ResolveUnloadedFileFieldValue("<blank>"), "explicit clear kept");
		});

		yield return ("PendingEdit: ComboBox.Text setter swaps case-only text for the dropdown item; exact setter keeps it", delegate
		{
			using (ComboBox comboBox = new ComboBox())
			{
				comboBox.Sorted = true;
				IntPtr handle = comboBox.Handle;
				Check.True(handle != IntPtr.Zero, "handle created");
				comboBox.Items.Add("<keep>");
				comboBox.Items.Add("<blank>");
				comboBox.Items.Add("hello world");

				// 现状锁定(WinForms 行为):直接赋值会选中忽略大小写匹配的下拉项,文字变成下拉项的写法。
				comboBox.Text = "Hello World";
				Check.Equal("hello world", comboBox.Text, "plain setter adopts the item's casing");

				comboBox.SelectedIndex = -1;
				comboBox.Text = "";
				StateFieldInstance.SetComboBoxTextExact(comboBox, "Hello World");
				Check.Equal("Hello World", comboBox.Text, "exact setter keeps the case-only edit");
				Check.True(comboBox.Items.Contains("hello world"), "dropdown item restored");
				Check.Equal(3, comboBox.Items.Count, "no item lost or duplicated");

				StateFieldInstance.SetComboBoxTextExact(comboBox, "<keep>");
				Check.Equal("<keep>", comboBox.Text, "exact item still selectable");
			}
		});

		yield return ("PendingEdit: file opened read-shared by a player loads but save fails with a message", delegate
		{
			string path = WriteFlacFixture();
			try
			{
				using (FileStream player = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
				{
					using (var state = new ConfigDescriptorState(path))
					{
						Check.True(state.IsLoadedSuccessfully(), "read still works while the player holds the file");
						state.LoadBasicTagFields();
						state.LoadLyrics();
						state["title"] = "Edited";
						Check.True(!state.SaveTagFields(), "save fails while the file is held");
						Check.True(!string.IsNullOrWhiteSpace(state.GetLoadError()), "failure carries a message");
					}
				}
				// 播放器释放后同样的修改可以保存。
				using (var retry = new ConfigDescriptorState(path))
				{
					retry.LoadBasicTagFields();
					retry.LoadLyrics();
					retry["title"] = "Edited";
					Check.True(retry.SaveTagFields(), "retry after release succeeds");
				}
				Check.Equal("Edited", ReadTitle(path), "retry landed on disk");
			}
			finally
			{
				File.Delete(path);
			}
		});

		yield return ("PendingEdit: file opened exclusively cannot even be loaded", delegate
		{
			string path = WriteFlacFixture();
			try
			{
				using (FileStream exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
				{
					using (var state = new ConfigDescriptorState(path))
					{
						Check.True(!state.IsLoadedSuccessfully(), "load fails under an exclusive lock");
						Check.True(!string.IsNullOrWhiteSpace(state.GetLoadError()), "load error message present");
					}
				}
			}
			finally
			{
				File.Delete(path);
			}
		});
	}
}
