using System;
using System.Collections.Generic;
using MusicTag.States;

namespace MusicTag.Tests;

// ConfigDescriptorState 的显示/格式化纯逻辑 characterization（无音频 fixture）。
// GetDisplayValue 是读标签/重命名路径的公共格式化层(RenameFiles 经它取 title/artist/album/...);
// 空构造 + indexer 填 TagValues 字典即可隔离测试(不打开音频文件、不碰 tagFile)。
// 另含两个 duration 静态格式化器(GetDisplayValue("durationinms") 的底层)。
internal static class ConfigDescriptorStateDisplayCharacterization
{
	public static IEnumerable<(string, Action)> All()
	{
		// --- GetDisplayValue：按字段名分派的格式化 ---
		yield return ("GetDisplayValue bitrate -> \"<n>kbps\"", delegate
		{
			var state = new ConfigDescriptorState();
			state["bitrate"] = 320;
			Check.Equal("320kbps", state.GetDisplayValue("bitrate"), "bitrate");
		});

		yield return ("GetDisplayValue samplerate -> \"<n>Hz\"", delegate
		{
			var state = new ConfigDescriptorState();
			state["samplerate"] = 44100;
			Check.Equal("44100Hz", state.GetDisplayValue("samplerate"), "samplerate");
		});

		yield return ("GetDisplayValue haspicture true -> check mark", delegate
		{
			var state = new ConfigDescriptorState();
			state["haspicture"] = true;
			Check.Equal("√", state.GetDisplayValue("haspicture"), "haspicture");
		});

		yield return ("GetDisplayValue haspicture false -> empty", delegate
		{
			var state = new ConfigDescriptorState();
			state["haspicture"] = false;
			Check.Equal("", state.GetDisplayValue("haspicture"), "haspicture");
		});

		yield return ("GetDisplayValue hasvideotrack true -> check mark", delegate
		{
			var state = new ConfigDescriptorState();
			state["hasvideotrack"] = true;
			Check.Equal("√", state.GetDisplayValue("hasvideotrack"), "hasvideotrack");
		});

		yield return ("GetDisplayValue durationinms -> mm:ss.fff", delegate
		{
			var state = new ConfigDescriptorState();
			state["durationinms"] = 65432;
			Check.Equal("01:05.432", state.GetDisplayValue("durationinms"), "durationinms");
		});

		yield return ("GetDisplayValue text field -> ToString (default)", delegate
		{
			var state = new ConfigDescriptorState();
			state["title"] = "Hello World";
			Check.Equal("Hello World", state.GetDisplayValue("title"), "title");
		});

		yield return ("GetDisplayValue missing key -> empty", delegate
		{
			var state = new ConfigDescriptorState();
			Check.Equal("", state.GetDisplayValue("title"), "missing");
		});

		yield return ("GetDisplayValue null value -> empty", delegate
		{
			var state = new ConfigDescriptorState();
			state["title"] = null;
			Check.Equal("", state.GetDisplayValue("title"), "null");
		});

		yield return ("GetDisplayValue numeric default -> ToString", delegate
		{
			var state = new ConfigDescriptorState();
			state["track"] = 5;
			Check.Equal("5", state.GetDisplayValue("track"), "track");
		});

		// --- FormatDurationWithMilliseconds：mm:ss.fff,分钟不进位到小时(与 Hms 有别) ---
		yield return ("FormatDurationWithMilliseconds 0 -> 00:00.000", delegate
		{
			Check.Equal("00:00.000", ConfigDescriptorState.FormatDurationWithMilliseconds(0), "fmt");
		});

		yield return ("FormatDurationWithMilliseconds 65432 -> 01:05.432", delegate
		{
			Check.Equal("01:05.432", ConfigDescriptorState.FormatDurationWithMilliseconds(65432), "fmt");
		});

		yield return ("FormatDurationWithMilliseconds 3661500 -> 61:01.500 (minutes not rolled to hours)", delegate
		{
			Check.Equal("61:01.500", ConfigDescriptorState.FormatDurationWithMilliseconds(3661500), "fmt");
		});

		// --- FormatDurationHms：hh:mm:ss,分钟进位到小时 ---
		yield return ("FormatDurationHms 0 -> 00:00:00", delegate
		{
			Check.Equal("00:00:00", ConfigDescriptorState.FormatDurationHms(0), "fmt");
		});

		yield return ("FormatDurationHms 3661500 -> 01:01:01", delegate
		{
			Check.Equal("01:01:01", ConfigDescriptorState.FormatDurationHms(3661500), "fmt");
		});

		yield return ("FormatDurationHms 65432 -> 00:01:05", delegate
		{
			Check.Equal("00:01:05", ConfigDescriptorState.FormatDurationHms(65432), "fmt");
		});
	}
}
