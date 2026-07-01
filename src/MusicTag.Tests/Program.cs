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
		return TestRunner.RunAll(tests);
	}
}
