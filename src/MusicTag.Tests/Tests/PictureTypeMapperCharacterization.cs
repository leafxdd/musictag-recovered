using System;
using System.Collections.Generic;
using MusicTag.States;

namespace MusicTag.Tests;

// ConfigDescriptorState 图片类型名 <-> ID3v2 APIC code 映射 characterization(picture-type mapper)。
// PictureTypeToName / NameToPictureType 由 private static 提升 internal static(body 逐字不变)。
// pictureTypeNames 是逐字保留的 native 20 项列表(index = APIC code),锁定两处易被"顺手修好"的刻意行为:
//   1) 英式拼写 "Coloured Fish"(APIC 17,而 TagLib 枚举名是美式 ColoredFish)。
//   2) native 无 "Publisher Logo":APIC 20(TagLib.PictureType.PublisherLogo)越界 -> 回退 "Other"。
internal static class PictureTypeMapperCharacterization
{
	public static IEnumerable<(string, Action)> All()
	{
		// ===== PictureTypeToName(code -> name)=====

		yield return ("PictureTypeToName: Other(0) -> \"Other\"", delegate
		{
			Check.Equal("Other", ConfigDescriptorState.PictureTypeToName(TagLib.PictureType.Other), "0");
		});

		yield return ("PictureTypeToName: FrontCover(3) -> \"Front Cover\"", delegate
		{
			Check.Equal("Front Cover", ConfigDescriptorState.PictureTypeToName(TagLib.PictureType.FrontCover), "3");
		});

		yield return ("PictureTypeToName: ColoredFish(17) -> British \"Coloured Fish\"", delegate
		{
			Check.Equal("Coloured Fish", ConfigDescriptorState.PictureTypeToName(TagLib.PictureType.ColoredFish), "17 british spelling");
		});

		yield return ("PictureTypeToName: BandLogo(19) -> \"Band Logo\" (last entry)", delegate
		{
			Check.Equal("Band Logo", ConfigDescriptorState.PictureTypeToName(TagLib.PictureType.BandLogo), "19");
		});

		yield return ("PictureTypeToName: PublisherLogo(20) out of range -> fallback \"Other\"", delegate
		{
			Check.Equal("Other", ConfigDescriptorState.PictureTypeToName(TagLib.PictureType.PublisherLogo), "20 -> Other");
		});

		// ===== NameToPictureType(name -> code)=====

		yield return ("NameToPictureType: \"Other\" -> Other", delegate
		{
			Check.Equal(TagLib.PictureType.Other, ConfigDescriptorState.NameToPictureType("Other"), "Other");
		});

		yield return ("NameToPictureType: \"Front Cover\" -> FrontCover", delegate
		{
			Check.Equal(TagLib.PictureType.FrontCover, ConfigDescriptorState.NameToPictureType("Front Cover"), "FrontCover");
		});

		yield return ("NameToPictureType: \"Coloured Fish\" -> ColoredFish(17)", delegate
		{
			Check.Equal(TagLib.PictureType.ColoredFish, ConfigDescriptorState.NameToPictureType("Coloured Fish"), "british -> 17");
		});

		yield return ("NameToPictureType: null -> Other (null guard)", delegate
		{
			Check.Equal(TagLib.PictureType.Other, ConfigDescriptorState.NameToPictureType(null), "null");
		});

		yield return ("NameToPictureType: unknown \"Publisher Logo\" -> Other (not in native list)", delegate
		{
			Check.Equal(TagLib.PictureType.Other, ConfigDescriptorState.NameToPictureType("Publisher Logo"), "not found");
		});

		// ===== round-trip =====

		yield return ("Round-trip: BackCover -> name -> BackCover", delegate
		{
			string name = ConfigDescriptorState.PictureTypeToName(TagLib.PictureType.BackCover);
			Check.Equal(TagLib.PictureType.BackCover, ConfigDescriptorState.NameToPictureType(name), "round-trip BackCover");
		});
	}
}
