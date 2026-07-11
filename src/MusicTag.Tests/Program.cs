using System;
using System.Collections.Generic;
using MusicTagWinApp.Properties;

namespace MusicTag.Tests;

internal static class Program
{
	// STAThread：主程序集是 WinForms 应用，类型加载可能触及需 STA 的 COM 组件（剪贴板 / shell）。
	[STAThread]
	private static int Main()
	{
		// net8:注册 ANSI 代码页编码提供程序(gb2312/GBK/Big5/Shift_JIS 等在 .NET Core+ 非内置),
		// 与主程序 Program.Main 首行一致;必须先于任何触发 TagTextEncoding 静态构造器的用例。
		System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
		// net8:与主程序一致复位默认字体(netfx 基准),对话框构造冒烟的布局路径才与 app 相同。
		System.Windows.Forms.Application.SetDefaultFont(new System.Drawing.Font(new System.Drawing.FontFamily("Microsoft Sans Serif"), 8.25f));
		Console.WriteLine("MusicTag characterization tests");
		Console.WriteLine();
		List<(string, Action)> tests = new List<(string, Action)>();
		tests.AddRange(TextUtilitiesTests.All());
		tests.AddRange(NetEaseProviderCharacterization.All());
		tests.AddRange(QqProviderCharacterization.All());
		tests.AddRange(KuwoProviderCharacterization.All());
		tests.AddRange(KugouProviderCharacterization.All());
		tests.AddRange(TrackIdLookupCharacterization.All());
		tests.AddRange(ProviderCoverCharacterization.All());
		tests.AddRange(ProviderLyricCharacterization.All());
		tests.AddRange(ConfigDescriptorStateCharacterization.All());
		tests.AddRange(FilenameRegexCaptureExtractorCharacterization.All());
		tests.AddRange(RenderRenameFilenameCharacterization.All());
		tests.AddRange(PendingTagUpdateCharacterization.All());
		tests.AddRange(BuildFilenameMatchRegexCharacterization.All());
		tests.AddRange(SplitCombinedDiscTrackCaptureCharacterization.All());
		tests.AddRange(ConfigDescriptorStateDisplayCharacterization.All());
		tests.AddRange(ConfigDescriptorStateRoundTripCharacterization.All());
		tests.AddRange(Id3WritePolicyCharacterization.All());
		tests.AddRange(ValidateFilenamePatternCoreCharacterization.All());
		tests.AddRange(PendingTagUpdateTextTagCharacterization.All());
		tests.AddRange(SetRegexCaptureTagCharacterization.All());
		tests.AddRange(ResolveDestinationAudioPathCharacterization.All());
		tests.AddRange(IsRequiredTagMissingCharacterization.All());
		tests.AddRange(ResolveRelatedFileTargetCharacterization.All());
		tests.AddRange(GetSiblingPathWithExtensionCharacterization.All());
		tests.AddRange(MoveFileAllowingCaseOnlyRenameCharacterization.All());
		tests.AddRange(MoveRelatedFileBestEffortCharacterization.All());
		tests.AddRange(TextSimilarityCalculatorCharacterization.All());
		tests.AddRange(AutoMatchTextTagGatingCharacterization.All());
		tests.AddRange(NaturalSortCharacterization.All());
		tests.AddRange(PromoteBestMatchCharacterization.All());
		tests.AddRange(OptionsDialogCharacterization.All());
		tests.AddRange(StateFieldInstancePureLogicCharacterization.All());
		tests.AddRange(CombinedTagSearchDialogCharacterization.All());
		tests.AddRange(ApplicationInfoServiceCharacterization.All());
		tests.AddRange(SourceItemCharacterization.All());
		tests.AddRange(SearchProviderPolicyCharacterization.All());
		tests.AddRange(SearchStatusIndicatorCharacterization.All());
		tests.AddRange(NetEaseCryptoCharacterization.All());
		tests.AddRange(RemoteTagProviderBaseCharacterization.All());
		tests.AddRange(TrackSearchResultCharacterization.All());
		tests.AddRange(ProviderDecodersCharacterization.All());
		tests.AddRange(LyricProcessingCharacterization.All());
		tests.AddRange(KuwoLyricBuildCharacterization.All());
		tests.AddRange(PictureTypeMapperCharacterization.All());
		tests.AddRange(UtilityMappingCharacterization.All());
		tests.AddRange(LyricFileNameCharacterization.All());
		tests.AddRange(ExceptionDetailsFormatCharacterization.All());
		tests.AddRange(BatchCompletionResultCharacterization.All());
		tests.AddRange(ConfigDescriptorFrameIdCharacterization.All());
			tests.AddRange(SerializationEncodingCharacterization.All());
			tests.AddRange(AutoMatchFieldClassificationCharacterization.All());
			tests.AddRange(PureHelperTailCharacterization.All());
			tests.AddRange(AutoMatchDialogCharacterization.All());
			tests.AddRange(PureCoreExtractionTailCharacterization.All());
			tests.AddRange(TrieMatcherCharacterization.All());
			tests.AddRange(LyricTextProcessorCharacterization.All());
			tests.AddRange(TrackResultLimitCharacterization.All());
			tests.AddRange(TagFieldTemplateCharacterization.All());
			tests.AddRange(BuildTextTagCandidatesCharacterization.All());
			tests.AddRange(CoverDownloadCoreCharacterization.All());
			tests.AddRange(ChineseTextConverterCharacterization.All());
			tests.AddRange(DialogConstructionSmoke.All());
			tests.AddRange(OptionsDialogInitialVisibilityCharacterization.All());
			tests.AddRange(UndoTempCleanupCharacterization.All());
		// Characterization 基线:歌词时间轴精度现由 LyricDownload_ReformatTimetag 控制(见
		// LyricTextProcessor.FormatTimestamp 的两位/三位分支)。统一 pin 为 true(格式化开 → 2 位
		// 百分秒、四舍五入),使既有断言确定;验证"关 → 保留 3 位"的用例在其内部局部置 false 并在
		// finally 复位,避免泄漏到后续用例。运行时该项默认读作 false(XmlSettingsProvider 无持久值)。
		Settings.Default.LyricDownload_ReformatTimetag = true;
		return TestRunner.RunAll(tests);
	}
}
