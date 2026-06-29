using System;
using System.Collections.Generic;

namespace MusicTag.Tests;

internal static class Program
{
	// STAThread：主程序集是 WinForms 应用，类型加载可能触及需 STA 的 COM 组件（剪贴板 / shell）。
	[STAThread]
	private static int Main()
	{
		Console.WriteLine("MusicTag characterization tests");
		Console.WriteLine();
		List<(string, Action)> tests = new List<(string, Action)>();
		tests.AddRange(TextUtilitiesTests.All());
		tests.AddRange(NetEaseProviderCharacterization.All());
		tests.AddRange(QqProviderCharacterization.All());
		tests.AddRange(KuwoProviderCharacterization.All());
		tests.AddRange(KugouProviderCharacterization.All());
		tests.AddRange(ConfigDescriptorStateCharacterization.All());
		tests.AddRange(FilenameRegexCaptureExtractorCharacterization.All());
		tests.AddRange(RenderRenameFilenameCharacterization.All());
		tests.AddRange(PendingTagUpdateCharacterization.All());
		tests.AddRange(BuildFilenameMatchRegexCharacterization.All());
		tests.AddRange(SplitCombinedDiscTrackCaptureCharacterization.All());
		tests.AddRange(ConfigDescriptorStateDisplayCharacterization.All());
		return TestRunner.RunAll(tests);
	}
}
