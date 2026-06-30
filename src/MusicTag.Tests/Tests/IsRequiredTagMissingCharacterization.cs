using System;
using System.Collections.Generic;
using MusicTag.Schemes;

namespace MusicTag.Tests;

// FilenameRelatedBatchDialog.IsRequiredTagMissing 的 characterization。
// 该方法由本批从 RenameFilesBatchWorker.RenameFiles 提取(byte-identical move:保留 else if + 局部变量,
// 仅把 Owner.selectedFilenamePattern 换成参数 filenamePattern)。语义:重命名模板含 @1(标题)/@2(艺术家)
// 占位符却对应 tag 为空 -> 该文件视为缺必填 tag(生产端跳过、计失败)。锁定:
//   - @1 分支门控于 filenamePattern.Contains("@1");@2 同理;
//   - else if 短路:@1 缺失即判定 true,不再查 @2(但两分支都置 true,故结果不区分走哪支);
//   - 空判据是 !value.Any()(看是否【有字符】,非"非空白"):" "(空格)有字符 -> 不算缺
//     (生产端 title/artist 已 .Trim(),故空白串实际不可达,此 case 仅锁定方法本身语义)。
// 纯 static、参数皆 string,无需任何状态。
internal static class IsRequiredTagMissingCharacterization
{
	public static IEnumerable<(string, Action)> All()
	{
		// --- @1 分支:模板含 @1 且 title 空 -> 缺 ---
		yield return ("IsRequiredTagMissing: @1 in pattern, title empty -> missing", delegate
		{
			Check.True(FilenameRelatedBatchDialog.IsRequiredTagMissing("@1 - @2", "", "A"), "missing title");
		});

		// --- @2 分支(title 已满足,else if 查 @2):artist 空 -> 缺 ---
		yield return ("IsRequiredTagMissing: @2 in pattern, title present, artist empty -> missing (@2 branch)", delegate
		{
			Check.True(FilenameRelatedBatchDialog.IsRequiredTagMissing("@1 - @2", "T", ""), "missing artist via else-if");
		});

		// --- 两者皆满足 -> 不缺 ---
		yield return ("IsRequiredTagMissing: both present -> not missing", delegate
		{
			Check.True(!FilenameRelatedBatchDialog.IsRequiredTagMissing("@1 - @2", "T", "A"), "both present");
		});

		// --- @1 门控:模板不含 @1 时,title 空也不触发(只看模板里出现的占位符) ---
		yield return ("IsRequiredTagMissing: pattern without @1, title empty -> not missing (@1 gated by Contains)", delegate
		{
			Check.True(!FilenameRelatedBatchDialog.IsRequiredTagMissing("@2", "", "A"), "title empty irrelevant when no @1 in pattern");
		});

		// --- else if 短路:模板含 @1@2、title 与 artist 皆空 -> @1 分支即判定 missing ---
		yield return ("IsRequiredTagMissing: @1 & @2 in pattern, both empty -> missing (@1 short-circuits)", delegate
		{
			Check.True(FilenameRelatedBatchDialog.IsRequiredTagMissing("@1 - @2", "", ""), "@1 branch wins");
		});

		// --- 无 @1/@2 占位符:即便 title/artist 皆空也不缺(纯字面量/其他占位符模板) ---
		yield return ("IsRequiredTagMissing: no @1/@2 placeholder, both empty -> not missing", delegate
		{
			Check.True(!FilenameRelatedBatchDialog.IsRequiredTagMissing("@3 - @4", "", ""), "no required placeholder");
		});

		// --- 空判据是 !Any():" "(空格)有字符 -> 不算缺(生产端已 Trim,故仅锁定方法语义) ---
		yield return ("IsRequiredTagMissing: whitespace title with @1 -> not missing (Any() counts space char)", delegate
		{
			Check.True(!FilenameRelatedBatchDialog.IsRequiredTagMissing("@1", " ", ""), "space is a char; Any() true; not flagged");
		});
	}
}
