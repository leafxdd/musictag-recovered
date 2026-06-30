using System;
using System.Collections.Generic;
using MusicTagWinApp.Adapter;

namespace MusicTag.Tests;

// AutoMatchTagsDialog 三个文本标签写入门控的 characterization(零行为改动提取自 AutoMatchWorker 的三个嵌套上下文:
// LoadedTagContext.MarkTextTagNeededIfMissing / TextTagUpdateFilter.RemoveUnchangedOrBlockedField /
// TagSaveContext.ApplyTextTagUpdate)。同一字段经三阶段流水线:
//   探测 IsExistingTextTagUpdatable(决定是否发起联网搜索)
//     -> 过滤 ShouldDiscardTextTagCandidate(搜到候选后剔除不该写的字段)
//       -> 写入 ShouldWriteTextTagUpdate(最终是否落盘)。
//
// 【本文件锁定三个 gate 的当前行为。其中曾有一处已确认可达的 latent bug,现已修正】:
//   三个 gate 对"现有值算不算空"判定曾不一致 —— 探测/过滤用 string.IsNullOrWhiteSpace、写入【曾】用
//   string.IsNullOrEmpty。后果:现有标签为【纯空白】(如 " ")+ overwrite=false + 搜到合法新值时,探测判定
//   需更新、过滤判定保留候选(空白被当空)、textTagUpdates 拿到新值,但写入阶段把空白当"非空且禁覆盖"
//   -> 静默不写 = 更新丢失。修正:写入 gate 改用 IsNullOrWhiteSpace,三阶段对空判定统一(行为修正,非逐字节
//   等价 —— commit 87ff27a5 先锁定 bug、本 commit 翻转对应断言为修正后行为)。见下方标 "fixed" 的 case。
// 全部为 internal static 纯函数(参数 object/string + bool),无任何状态依赖。
internal static class AutoMatchTextTagGatingCharacterization
{
	public static IEnumerable<(string, Action)> All()
	{
		// ===== 阶段 1:IsExistingTextTagUpdatable(existingValue, overwrite) —— 现有标签是否需要更新 =====

		// 非 string(此处 null:索引器对缺失字段可能返回 null)-> is string 短路 -> false
		yield return ("IsExistingTextTagUpdatable: null (non-string) -> false", delegate
		{
			Check.True(!AutoMatchTagsDialog.IsExistingTextTagUpdatable(null, false), "null is not string");
		});

		// 非 string 即便 overwrite=true 仍 false(is string 守卫在 && 最左,先短路)
		yield return ("IsExistingTextTagUpdatable: non-string + overwrite=true -> false (is-string short-circuits)", delegate
		{
			Check.True(!AutoMatchTagsDialog.IsExistingTextTagUpdatable(123, true), "non-string short-circuits even with overwrite");
		});

		// 已有非空白内容 + 不允许覆盖 -> 不需更新 -> false
		yield return ("IsExistingTextTagUpdatable: non-blank + overwrite=false -> false", delegate
		{
			Check.True(!AutoMatchTagsDialog.IsExistingTextTagUpdatable("abc", false), "non-blank, no overwrite");
		});

		// 已有内容但允许覆盖 -> 需更新 -> true
		yield return ("IsExistingTextTagUpdatable: non-blank + overwrite=true -> true", delegate
		{
			Check.True(AutoMatchTagsDialog.IsExistingTextTagUpdatable("abc", true), "overwrite forces update");
		});

		// 空字符串 -> IsNullOrWhiteSpace=true -> 需更新 -> true
		yield return ("IsExistingTextTagUpdatable: empty string -> true", delegate
		{
			Check.True(AutoMatchTagsDialog.IsExistingTextTagUpdatable("", false), "empty counts as blank");
		});

		// 纯空白 " " -> IsNullOrWhiteSpace=true -> 需更新 -> true(探测阶段把空白当空)
		yield return ("IsExistingTextTagUpdatable: whitespace-only -> true (treats blank as empty)", delegate
		{
			Check.True(AutoMatchTagsDialog.IsExistingTextTagUpdatable("   ", false), "whitespace counts as blank");
		});

		// ===== 阶段 2:ShouldDiscardTextTagCandidate(currentValue, newValue, overwrite) —— 是否丢弃候选(true=丢弃) =====

		// 候选非 string(as string -> null)-> 保留(不丢弃),交由后续阶段
		yield return ("ShouldDiscardTextTagCandidate: newValue null -> keep (false)", delegate
		{
			Check.True(!AutoMatchTagsDialog.ShouldDiscardTextTagCandidate("old", null, false), "null new value kept");
		});

		// 现值非 string -> 保留
		yield return ("ShouldDiscardTextTagCandidate: currentValue null -> keep (false)", delegate
		{
			Check.True(!AutoMatchTagsDialog.ShouldDiscardTextTagCandidate(null, "new", false), "null current value kept");
		});

		// 候选为纯空白 -> 丢弃(不拿空白覆盖)
		yield return ("ShouldDiscardTextTagCandidate: blank newValue -> discard (true)", delegate
		{
			Check.True(AutoMatchTagsDialog.ShouldDiscardTextTagCandidate("old", "   ", false), "blank candidate discarded");
		});

		// 候选为空串 -> 丢弃
		yield return ("ShouldDiscardTextTagCandidate: empty newValue -> discard (true)", delegate
		{
			Check.True(AutoMatchTagsDialog.ShouldDiscardTextTagCandidate("old", "", false), "empty candidate discarded");
		});

		// 现值非空白 + 不允许覆盖 -> 丢弃(保护已有内容)
		yield return ("ShouldDiscardTextTagCandidate: non-blank current + overwrite=false -> discard (true)", delegate
		{
			Check.True(AutoMatchTagsDialog.ShouldDiscardTextTagCandidate("old", "new", false), "protect existing when no overwrite");
		});

		// 现值非空白 + 允许覆盖 + 有变化 -> 保留
		yield return ("ShouldDiscardTextTagCandidate: non-blank current + overwrite=true + changed -> keep (false)", delegate
		{
			Check.True(!AutoMatchTagsDialog.ShouldDiscardTextTagCandidate("old", "new", true), "overwrite keeps changed candidate");
		});

		// 现值与候选相等(ordinal)-> 丢弃(无变化)
		yield return ("ShouldDiscardTextTagCandidate: equal values -> discard (true)", delegate
		{
			Check.True(AutoMatchTagsDialog.ShouldDiscardTextTagCandidate("same", "same", true), "unchanged value discarded");
		});

		// string.Equals 为 ordinal(大小写敏感):仅大小写不同视为有变化 + overwrite=true -> 保留
		yield return ("ShouldDiscardTextTagCandidate: case-only diff + overwrite=true -> keep (false, ordinal Equals)", delegate
		{
			Check.True(!AutoMatchTagsDialog.ShouldDiscardTextTagCandidate("abc", "ABC", true), "ordinal Equals treats case diff as changed");
		});

		// *** 关键:现值纯空白 + 有合法新值 + overwrite=false -> 保留(false)。空白现值被当"空",候选通过过滤。
		// 探测/过滤阶段对空白用 IsNullOrWhiteSpace;修正后写入阶段(下方)亦如此 -> 全流水线一致(此前写入误用
		// IsNullOrEmpty 与此处矛盾,即已修正的 latent bug 的【上半场】)。
		yield return ("ShouldDiscardTextTagCandidate: whitespace current + valid new + overwrite=false -> keep (false)", delegate
		{
			Check.True(!AutoMatchTagsDialog.ShouldDiscardTextTagCandidate("   ", "RealTitle", false), "blank current treated as empty -> candidate kept");
		});

		// ===== 阶段 3:ShouldWriteTextTagUpdate(newValue, currentValue, overwrite) —— 是否写入(true=写)【含已修正的 bug 翻转】 =====

		// 新值非 string -> false
		yield return ("ShouldWriteTextTagUpdate: newValue null -> false", delegate
		{
			Check.True(!AutoMatchTagsDialog.ShouldWriteTextTagUpdate(null, "old", true), "null new value not written");
		});

		// 现值非 string -> false
		yield return ("ShouldWriteTextTagUpdate: currentValue null -> false", delegate
		{
			Check.True(!AutoMatchTagsDialog.ShouldWriteTextTagUpdate("new", null, true), "null current value not written");
		});

		// 新值为空串 -> false(!IsNullOrEmpty 守卫;即便 overwrite 也不写空)
		yield return ("ShouldWriteTextTagUpdate: empty newValue -> false", delegate
		{
			Check.True(!AutoMatchTagsDialog.ShouldWriteTextTagUpdate("", "old", true), "empty new value not written");
		});

		// 新值合法 + 现值为空串 + overwrite=false -> true(现值空,写入)
		yield return ("ShouldWriteTextTagUpdate: valid new + empty current + overwrite=false -> true", delegate
		{
			Check.True(AutoMatchTagsDialog.ShouldWriteTextTagUpdate("new", "", false), "empty current allows write");
		});

		// 新值合法 + 现值非空 + overwrite=false -> false(禁覆盖)
		yield return ("ShouldWriteTextTagUpdate: valid new + non-empty current + overwrite=false -> false", delegate
		{
			Check.True(!AutoMatchTagsDialog.ShouldWriteTextTagUpdate("new", "old", false), "non-empty current blocks write");
		});

		// 新值合法 + 现值非空 + overwrite=true -> true(覆盖)
		yield return ("ShouldWriteTextTagUpdate: valid new + non-empty current + overwrite=true -> true", delegate
		{
			Check.True(AutoMatchTagsDialog.ShouldWriteTextTagUpdate("new", "old", true), "overwrite allows write");
		});

		// 修正后(曾是 *** LATENT BUG ***):新值合法 + 现值【纯空白】+ overwrite=false -> 写入(true)。
		// 写入 gate 改用 IsNullOrWhiteSpace 后,纯空白现值与探测/过滤一致地被当空 -> 接受搜到的真实值。
		// 此前误用 IsNullOrEmpty(" ")=false 把空白当"非空且禁覆盖"而静默拒写(过滤已保留候选,写入却丢弃)= 更新丢失。
		yield return ("ShouldWriteTextTagUpdate: valid new + WHITESPACE current + overwrite=false -> true (fixed: was silent update loss)", delegate
		{
			Check.True(AutoMatchTagsDialog.ShouldWriteTextTagUpdate("RealTitle", "   ", false), "fixed: whitespace current now allows write (blank treated as empty)");
		});

		// 对照:同样纯空白现值,overwrite=true 时绕过空判定 -> 写入(证明 bug 仅在 overwrite=false 下显形)
		yield return ("ShouldWriteTextTagUpdate: valid new + whitespace current + overwrite=true -> true (overwrite bypasses)", delegate
		{
			Check.True(AutoMatchTagsDialog.ShouldWriteTextTagUpdate("RealTitle", "   ", true), "overwrite bypasses the blank-vs-empty mismatch");
		});

		// ===== 跨阶段一致(修正后):同一输入(current=" ", new="RealTitle", overwrite=false)过滤保留且写入接受 =====
		yield return ("Pipeline consistency (fixed): blank current kept by filter AND written", delegate
		{
			// 过滤:保留(false=不丢弃)-> 候选进入 textTagUpdates,字段保留在 MatchConditionSettings
			Check.True(!AutoMatchTagsDialog.ShouldDiscardTextTagCandidate("   ", "RealTitle", false), "filter keeps candidate");
			// 写入:true=写入 -> 候选落盘,与过滤决定一致(此前两者矛盾导致静默更新丢失)。
			Check.True(AutoMatchTagsDialog.ShouldWriteTextTagUpdate("RealTitle", "   ", false), "write now accepts the kept candidate");
		});
	}
}
