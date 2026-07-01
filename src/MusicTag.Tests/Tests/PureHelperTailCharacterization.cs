using System;
using System.Collections.Generic;
using System.Windows.Forms;
using MusicTag.States;
using MusicTagWinApp.Common;
using MusicTagWinApp.Roles;

namespace MusicTag.Tests;

// scout 尾批(area 1/3/4)甄别出的低风险纯逻辑 golden master:Page 有界日志累加器、ConfigDescriptorState
// 原始字段编码对(StringTypeName / EncodeByStringType,可见性 private->internal)、两个列排序比较器
// (EditableListView.CompareSortableText 分层数值/日期/字典序、CustomColumnsDialog.CompareColumnDisplayOrder)。
// 均行为保持(仅可见性关键字或 Page 已 IVT 可达,零逻辑改动)。byte[] 以 BitConverter.ToString 比内容;
// comparer 断言符号/tie 精确值(避 string.Compare 具体值 culture 脆性,数值/tie 分支确定)。
internal static class PureHelperTailCharacterization
{
	public static IEnumerable<(string, Action)> All()
	{
		// ===== Page:前 20 行 AddLine 累加 message+"\n",第 21+ 追加单个 "..." 并停止计数(幂等溢出)=====
		yield return ("Page: AddLine accumulates then caps at 20 lines with single idempotent '...'", delegate
		{
			Page empty = new Page();
			Check.Equal(0, empty.LineCount, "new -> LineCount 0");
			Check.Equal("", empty.ToString(), "new -> empty string");

			Page two = new Page();
			two.AddLine("a");
			two.AddLine("b");
			Check.Equal(2, two.LineCount, "2 lines -> LineCount 2");
			Check.Equal("a\nb\n", two.ToString(), "2 lines -> 'a\\nb\\n'");

			Page cap = new Page();
			for (int i = 0; i < 20; i++) { cap.AddLine("L" + i); }
			Check.Equal(20, cap.LineCount, "exactly 20 -> LineCount 20");
			Check.True(!cap.ToString().EndsWith("..."), "exactly 20 -> no ellipsis yet");

			cap.AddLine("overflow");
			Check.Equal(20, cap.LineCount, "21st -> LineCount stays 20");
			Check.True(cap.ToString().EndsWith("..."), "21st -> ends with '...'");

			for (int i = 0; i < 5; i++) { cap.AddLine("more"); }
			Check.Equal(20, cap.LineCount, "further -> LineCount still 20");
			Check.True(cap.ToString().EndsWith("...") && !cap.ToString().EndsWith("......"), "further -> single '...' idempotent (not doubled)");
		});

		// ===== ConfigDescriptorState.StringTypeName:5 显式分支 + default UTF8 =====
		yield return ("ConfigDescriptorState.StringTypeName: explicit branches + UTF8 default", delegate
		{
			Check.Equal("Latin1", ConfigDescriptorState.StringTypeName(TagLib.StringType.Latin1), "Latin1");
			Check.Equal("UTF16", ConfigDescriptorState.StringTypeName(TagLib.StringType.UTF16), "UTF16");
			Check.Equal("UTF16BE", ConfigDescriptorState.StringTypeName(TagLib.StringType.UTF16BE), "UTF16BE");
			Check.Equal("UTF16LE", ConfigDescriptorState.StringTypeName(TagLib.StringType.UTF16LE), "UTF16LE");
			Check.Equal("UTF8", ConfigDescriptorState.StringTypeName(TagLib.StringType.UTF8), "UTF8");
			Check.Equal("UTF8", ConfigDescriptorState.StringTypeName((TagLib.StringType)999), "unknown -> UTF8 default");
		});

		// ===== ConfigDescriptorState.EncodeByStringType:null->"" 归一 + 逐类型编码;UTF16/UTF16LE/unknown -> Unicode(LE) =====
		yield return ("ConfigDescriptorState.EncodeByStringType: null-normalize + per-StringType bytes (UTF16/LE/unknown -> Unicode LE)", delegate
		{
			Check.Equal("", BitConverter.ToString(ConfigDescriptorState.EncodeByStringType(null, TagLib.StringType.Latin1)), "null -> empty bytes");
			Check.Equal("41-42-43", BitConverter.ToString(ConfigDescriptorState.EncodeByStringType("ABC", TagLib.StringType.Latin1)), "Latin1 'ABC' -> 41 42 43");
			Check.Equal("3F", BitConverter.ToString(ConfigDescriptorState.EncodeByStringType("中", TagLib.StringType.Latin1)), "Latin1 out-of-range -> '?' 0x3F");
			Check.Equal("00-41", BitConverter.ToString(ConfigDescriptorState.EncodeByStringType("A", TagLib.StringType.UTF16BE)), "UTF16BE 'A' -> 00 41");
			Check.Equal("E4-B8-AD", BitConverter.ToString(ConfigDescriptorState.EncodeByStringType("中", TagLib.StringType.UTF8)), "UTF8 '中' -> E4 B8 AD");
			Check.Equal("41-00", BitConverter.ToString(ConfigDescriptorState.EncodeByStringType("A", TagLib.StringType.UTF16)), "UTF16 -> Unicode LE 41 00");
			Check.Equal("41-00", BitConverter.ToString(ConfigDescriptorState.EncodeByStringType("A", TagLib.StringType.UTF16LE)), "UTF16LE -> Unicode LE 41 00 (default arm)");
		});

		// ===== EditableListView.CompareSortableText:双 decimal -> decimal.Compare;双 DateTime -> DateTime.Compare;否则 string.Compare =====
		yield return ("EditableListView.CompareSortableText: numeric > date > string tiered comparison", delegate
		{
			Check.True(EditableListView.CompareSortableText("10", "9") > 0, "numeric 10>9 (NOT lexicographic where '10'<'9')");
			Check.True(EditableListView.CompareSortableText("9", "10") < 0, "numeric 9<10");
			Check.Equal(0, EditableListView.CompareSortableText("5", "5"), "numeric equal -> 0");
			Check.True(EditableListView.CompareSortableText("2020-01-01", "2021-06-30") < 0, "ISO date earlier < later (date branch)");
			Check.True(EditableListView.CompareSortableText("abc", "abd") < 0, "string.Compare fallback abc<abd");
			Check.True(EditableListView.CompareSortableText("banana", "apple") > 0, "string.Compare banana>apple");
			Check.True(EditableListView.CompareSortableText("10", "abc") < 0, "mixed (abc not numeric/date) -> string.Compare, '1'<'a'");
		});

		// ===== CustomColumnsDialog.CompareColumnDisplayOrder:有效序(isShow?displayIndex:beforeHideDisplayIndex)升序;平局时 shown 排在 hidden 之后 =====
		yield return ("CustomColumnsDialog.CompareColumnDisplayOrder: effective-order asc, tie -> shown after hidden", delegate
		{
			CustomColumnsDialog.ColumnHeaderInfo a = new CustomColumnsDialog.ColumnHeaderInfo("a", 0, 0);
			CustomColumnsDialog.ColumnHeaderInfo b = new CustomColumnsDialog.ColumnHeaderInfo("b", 1, 0);
			Check.True(CustomColumnsDialog.CompareColumnDisplayOrder(a, b) < 0, "shown eff 0 < shown eff 1");

			CustomColumnsDialog.ColumnHeaderInfo shown5 = new CustomColumnsDialog.ColumnHeaderInfo("s", 5, 0);
			CustomColumnsDialog.ColumnHeaderInfo hidden2 = new CustomColumnsDialog.ColumnHeaderInfo("h", 99, 0, HorizontalAlignment.Left, false);
			hidden2.beforeHideDisplayIndex = 2;
			Check.True(CustomColumnsDialog.CompareColumnDisplayOrder(shown5, hidden2) > 0, "shown eff 5 > hidden eff 2 (hidden uses beforeHideDisplayIndex)");

			CustomColumnsDialog.ColumnHeaderInfo shownTie = new CustomColumnsDialog.ColumnHeaderInfo("st", 3, 0);
			CustomColumnsDialog.ColumnHeaderInfo hiddenTie = new CustomColumnsDialog.ColumnHeaderInfo("ht", 99, 0, HorizontalAlignment.Left, false);
			hiddenTie.beforeHideDisplayIndex = 3;
			Check.Equal(1, CustomColumnsDialog.CompareColumnDisplayOrder(shownTie, hiddenTie), "tie eff 3: shown after hidden -> 1");
			Check.Equal(-1, CustomColumnsDialog.CompareColumnDisplayOrder(hiddenTie, shownTie), "tie eff 3: hidden before shown -> -1");

			CustomColumnsDialog.ColumnHeaderInfo s1 = new CustomColumnsDialog.ColumnHeaderInfo("s1", 7, 0);
			CustomColumnsDialog.ColumnHeaderInfo s2 = new CustomColumnsDialog.ColumnHeaderInfo("s2", 7, 0);
			Check.Equal(0, CustomColumnsDialog.CompareColumnDisplayOrder(s1, s2), "same eff order + same show-state -> 0");
		});
	}
}
