using System;
using System.Collections.Generic;
using MusicTagWinApp.Instances;

namespace MusicTag.Tests;

// StateFieldInstance.ResolveTagFieldTemplateValue characterization —— 批量写回时单 tag 字段的模板值解析
// (从 SaveTags 提取的中风险纯核,载荷用户可见的写标签行为)。锁定四分支 + skip-vs-empty 区分 +
// lazy 求值语义:<blank> -> (写,"");纯 <keep> -> (不写,null);含 <keep> -> (写,Replace(current));
// 否则 -> (写,原文)。currentValueProvider 仅在"含 <keep>"分支调用(用计数器闭包验证,锁定原
// currentValue 只在该分支读 tagState 索引器的求值语义 —— eager-eval 陷阱的对抗性反面验证)。
internal static class TagFieldTemplateCharacterization
{
	public static IEnumerable<(string, Action)> All()
	{
		// 1. <blank> -> 写空串;provider 不调用(lazy)。
		yield return ("ResolveTagFieldTemplateValue: <blank> -> assign empty, provider not called", delegate
		{
			int calls = 0;
			var result = StateFieldInstance.ResolveTagFieldTemplateValue("<blank>", delegate { calls++; return "CUR"; });
			Check.True(result.ShouldAssign, "blank assigns");
			Check.Equal("", result.Value, "blank -> empty string");
			Check.Equal(0, calls, "provider NOT evaluated for <blank>");
		});

		// 2. 纯 <keep> -> 不写(ShouldAssign=false);provider 不调用。
		yield return ("ResolveTagFieldTemplateValue: pure <keep> -> skip, provider not called", delegate
		{
			int calls = 0;
			var result = StateFieldInstance.ResolveTagFieldTemplateValue("<keep>", delegate { calls++; return "CUR"; });
			Check.True(!result.ShouldAssign, "keep does NOT assign");
			Check.Null(result.Value, "keep value null");
			Check.Equal(0, calls, "provider NOT evaluated for pure <keep>");
		});

		// 3. 含 <keep>(非纯)-> 写 Replace(current);provider 调用恰一次。
		yield return ("ResolveTagFieldTemplateValue: contains <keep> -> replace with current, provider called once", delegate
		{
			int calls = 0;
			var result = StateFieldInstance.ResolveTagFieldTemplateValue("pre<keep>post", delegate { calls++; return "CUR"; });
			Check.True(result.ShouldAssign, "assigns");
			Check.Equal("preCURpost", result.Value, "<keep> replaced by current value");
			Check.Equal(1, calls, "provider evaluated exactly once");
		});

		// 4. 普通 text -> 写原文;provider 不调用。
		yield return ("ResolveTagFieldTemplateValue: literal text -> assign as-is, provider not called", delegate
		{
			int calls = 0;
			var result = StateFieldInstance.ResolveTagFieldTemplateValue("plain", delegate { calls++; return "CUR"; });
			Check.True(result.ShouldAssign, "assigns");
			Check.Equal("plain", result.Value, "literal");
			Check.Equal(0, calls, "provider NOT evaluated for literal");
		});

		// 5. 多个 <keep> 全部替换(string.Replace 替换所有出现)。
		yield return ("ResolveTagFieldTemplateValue: multiple <keep> all replaced", delegate
		{
			var result = StateFieldInstance.ResolveTagFieldTemplateValue("<keep>-<keep>", delegate { return "X"; });
			Check.Equal("X-X", result.Value, "all <keep> occurrences replaced");
		});

		// 6. current 为空串 -> <keep> 被替换为空(等效移除)。
		yield return ("ResolveTagFieldTemplateValue: empty current -> <keep> removed", delegate
		{
			var result = StateFieldInstance.ResolveTagFieldTemplateValue("a<keep>b", delegate { return ""; });
			Check.Equal("ab", result.Value, "empty current collapses <keep>");
		});

		// 7. 空 text -> 走 literal 分支 (写,"")，与 <blank> 同值但不同分支(skip-vs-empty 之外的第三种 "")。
		yield return ("ResolveTagFieldTemplateValue: empty text -> assign empty via literal branch", delegate
		{
			int calls = 0;
			var result = StateFieldInstance.ResolveTagFieldTemplateValue("", delegate { calls++; return "CUR"; });
			Check.True(result.ShouldAssign, "empty text assigns");
			Check.Equal("", result.Value, "empty text literal");
			Check.Equal(0, calls, "provider NOT evaluated for empty literal");
		});

		// 8. <keep> 位于开头 -> 前缀替换。
		yield return ("ResolveTagFieldTemplateValue: leading <keep>", delegate
		{
			var result = StateFieldInstance.ResolveTagFieldTemplateValue("<keep>tail", delegate { return "H"; });
			Check.Equal("Htail", result.Value, "leading <keep> replaced");
		});
	}
}
