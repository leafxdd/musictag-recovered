using System;
using System.Collections.Generic;
using MusicTag.Schemes;
using V = MusicTag.Schemes.FilenameRelatedBatchDialog.FilenamePatternValidation;

namespace MusicTag.Tests;

// FilenameRelatedBatchDialog.ValidateFilenamePatternCore 的 characterization（纯逻辑、无 UI）。
// 锁定文件名模板输入校验的现状——由 ValidateFilenamePattern 剥离出的纯判定(UI 层据返回值翻译
// 错误消息)。@5@4 与 strip 首段后相邻都归 AdjacentParams(原本同一条 Msg_ParamsInPatternCannotAdjacent)。
internal static class ValidateFilenamePatternCoreCharacterization
{
	private static V Validate(string pattern, bool changeTags)
	{
		return FilenameRelatedBatchDialog.ValidateFilenamePatternCore(pattern, changeTags);
	}

	public static IEnumerable<(string, Action)> All()
	{
		yield return ("ValidateFilenamePatternCore \"@1 - @2\" (rename) -> Valid", delegate
		{
			Check.Equal(V.Valid, Validate("@1 - @2", false), "result");
		});

		yield return ("ValidateFilenamePatternCore empty -> Empty", delegate
		{
			Check.Equal(V.Empty, Validate("", false), "result");
		});

		yield return ("ValidateFilenamePatternCore whitespace-only -> Empty", delegate
		{
			Check.Equal(V.Empty, Validate("   ", false), "result");
		});

		yield return ("ValidateFilenamePatternCore duplicate \"@1 @1\" -> DuplicateParam", delegate
		{
			Check.Equal(V.DuplicateParam, Validate("@1 @1", false), "result");
		});

		yield return ("ValidateFilenamePatternCore no params \"abc\" -> NoParam", delegate
		{
			Check.Equal(V.NoParam, Validate("abc", false), "result");
		});

		yield return ("ValidateFilenamePatternCore \"@0@1\" rename mode -> Pattern0NotAllowed", delegate
		{
			Check.Equal(V.Pattern0NotAllowed, Validate("@0@1", false), "result");
		});

		yield return ("ValidateFilenamePatternCore \"@0@1\" change-tags mode -> AdjacentParams (@0 allowed but adjacent)", delegate
		{
			Check.Equal(V.AdjacentParams, Validate("@0@1", true), "result");
		});

		yield return ("ValidateFilenamePatternCore \"@5@4@1\" -> AdjacentParams (leading @5@4)", delegate
		{
			Check.Equal(V.AdjacentParams, Validate("@5@4@1", false), "result");
		});

		yield return ("ValidateFilenamePatternCore \"@1@2\" -> AdjacentParams", delegate
		{
			Check.Equal(V.AdjacentParams, Validate("@1@2", false), "result");
		});

		yield return ("ValidateFilenamePatternCore \"@4@5@1\" -> Valid (leading disc-track stripped)", delegate
		{
			Check.Equal(V.Valid, Validate("@4@5@1", false), "result");
		});
	}
}
