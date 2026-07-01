using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using MusicTagWinApp;
using MusicTagWinApp.Common;
using MusicTagWinApp.Instances;
using MusicTagWinApp.Properties;

namespace MusicTag.Tests;

// StateFieldInstance(MusicTagWinApp.Instances,~8654 行主 god-form,churn 最高 42 commits)纯逻辑
// characterization。10 个无状态 helper 由 private[static] 提升为 internal static(9 个仅可见性、
// ParseLeadingNumber 由 instance->static,body 全部零改动、behavior-preserving),锁定 golden master
// 为后续渐进拆分铺路。均无 UI 控件 / 实例字段 / I/O 依赖,static 直调、无需实例化 Form。
internal static class StateFieldInstancePureLogicCharacterization
{
	public static IEnumerable<(string, Action)> All()
	{
		// ===== BuildBatchResultMessage:4 分支(totalCount>1 / primaryCount>0 / includeSkip&&skipped>0 / else)=====
		// 分支 1/3 含本地化 Resources(Msg_OK_Fail_Skip_Count / Msg_Skipped)-> 结构断言(避免 culture 脆性);
		// 分支 2/4 纯参数组合 -> 精确断言。返回 (string Item1, bool Item2):Item2 仅 else 分支为 true。

		// 分支1:totalCount>1(优先,不看其他)。Item1 = Format(completed+"\n"+模板,...) + "\n" + log。
		yield return ("BuildBatchResultMessage: totalCount>1 -> branch1 (Item2 false, wraps completed/log)", delegate
		{
			(string, bool) r = StateFieldInstance.BuildBatchResultMessage(2, "DONE", 1, 0, 0, 1, "LOG", includeSkippedBranch: true);
			Check.True(!r.Item2, "branch1 Item2 false");
			Check.True(r.Item1.StartsWith("DONE\n"), "branch1 starts with completed+newline");
			Check.True(r.Item1.EndsWith("\nLOG"), "branch1 ends with newline+log");
		});

		// 分支2:totalCount<=1 且 primaryCount>0。Item1 == completed+"\n"+log(纯参数,culture 无关)。
		yield return ("BuildBatchResultMessage: totalCount<=1 & primary>0 -> branch2 (completed\\nlog)", delegate
		{
			(string, bool) r = StateFieldInstance.BuildBatchResultMessage(1, "DONE", 1, 0, 0, 0, "LOG", includeSkippedBranch: true);
			Check.Equal("DONE\nLOG", r.Item1, "branch2 exact");
			Check.True(!r.Item2, "branch2 Item2 false");
		});

		// 分支3:totalCount<=1 & primary==0 & includeSkip & skipped>0。Item1 = Msg_Skipped+"\n"+log。
		yield return ("BuildBatchResultMessage: includeSkip & skipped>0 -> branch3 (skipped prefix + \\nlog)", delegate
		{
			(string, bool) r = StateFieldInstance.BuildBatchResultMessage(1, "DONE", 0, 0, 2, 0, "LOG", includeSkippedBranch: true);
			Check.True(!r.Item2, "branch3 Item2 false");
			Check.True(r.Item1.EndsWith("\nLOG"), "branch3 ends with newline+log");
			Check.True(r.Item1.Length > 3, "branch3 has skipped prefix before log");
		});

		// 分支4 else:totalCount<=1 & primary==0 & 无跳过分支。Item1 == log,Item2 == true。
		yield return ("BuildBatchResultMessage: nothing matched -> branch4 (log only, Item2 true)", delegate
		{
			(string, bool) r = StateFieldInstance.BuildBatchResultMessage(1, "DONE", 0, 0, 0, 0, "LOG", includeSkippedBranch: true);
			Check.Equal("LOG", r.Item1, "branch4 exact log");
			Check.True(r.Item2, "branch4 Item2 true");
		});

		// includeSkippedBranch 门控:skipped>0 但 includeSkip=false -> 跳过分支3,落入 else(branch4)。
		yield return ("BuildBatchResultMessage: skipped>0 but includeSkip=false -> branch4 (gate works)", delegate
		{
			(string, bool) r = StateFieldInstance.BuildBatchResultMessage(1, "DONE", 0, 0, 2, 0, "LOG", includeSkippedBranch: false);
			Check.Equal("LOG", r.Item1, "gated skip -> log only");
			Check.True(r.Item2, "gated skip -> Item2 true");
		});

		// 边界:totalCount=0(<=1)& 全 0 -> branch4。
		yield return ("BuildBatchResultMessage: totalCount=0 all-zero -> branch4", delegate
		{
			(string, bool) r = StateFieldInstance.BuildBatchResultMessage(0, "DONE", 0, 0, 0, 0, "LOG", includeSkippedBranch: true);
			Check.Equal("LOG", r.Item1, "count0 -> log only");
			Check.True(r.Item2, "count0 -> Item2 true");
		});

		// ===== IsCancellationException:cts 取消中 + (OCE | AggregateException 全 inner 取消) =====

		yield return ("IsCancellationException: null cts -> false", delegate
		{
			Check.True(!StateFieldInstance.IsCancellationException(new OperationCanceledException(), null), "null cts");
		});

		yield return ("IsCancellationException: cts not cancelled -> false", delegate
		{
			CancellationTokenSource cts = new CancellationTokenSource();
			Check.True(!StateFieldInstance.IsCancellationException(new OperationCanceledException(), cts), "not cancelled");
		});

		yield return ("IsCancellationException: cancelled + OperationCanceledException -> true", delegate
		{
			CancellationTokenSource cts = new CancellationTokenSource();
			cts.Cancel();
			Check.True(StateFieldInstance.IsCancellationException(new OperationCanceledException(), cts), "cancelled OCE");
		});

		yield return ("IsCancellationException: cancelled + AggregateException(OCE) -> true", delegate
		{
			CancellationTokenSource cts = new CancellationTokenSource();
			cts.Cancel();
			AggregateException agg = new AggregateException(new OperationCanceledException());
			Check.True(StateFieldInstance.IsCancellationException(agg, cts), "agg all-cancel");
		});

		yield return ("IsCancellationException: cancelled + AggregateException(OCE, plain) -> false", delegate
		{
			CancellationTokenSource cts = new CancellationTokenSource();
			cts.Cancel();
			AggregateException agg = new AggregateException(new OperationCanceledException(), new Exception("x"));
			Check.True(!StateFieldInstance.IsCancellationException(agg, cts), "agg not all-cancel");
		});

		yield return ("IsCancellationException: cancelled + plain Exception -> false", delegate
		{
			CancellationTokenSource cts = new CancellationTokenSource();
			cts.Cancel();
			Check.True(!StateFieldInstance.IsCancellationException(new Exception("x"), cts), "plain exception");
		});

		// 空 AggregateException(InnerExceptions.Count==0)-> 不进 All 分支 -> false。
		yield return ("IsCancellationException: cancelled + empty AggregateException -> false", delegate
		{
			CancellationTokenSource cts = new CancellationTokenSource();
			cts.Cancel();
			Check.True(!StateFieldInstance.IsCancellationException(new AggregateException(), cts), "empty agg");
		});

		// ===== UnwrapAsyncOperationException:单 inner 的 AggregateException 展开为该 inner,否则原样 =====

		yield return ("UnwrapAsyncOperationException: single-inner agg -> the inner", delegate
		{
			Exception inner = new Exception("inner");
			AggregateException agg = new AggregateException(inner);
			Check.True(ReferenceEquals(inner, StateFieldInstance.UnwrapAsyncOperationException(agg)), "single inner unwrapped");
		});

		yield return ("UnwrapAsyncOperationException: multi-inner agg -> the agg itself", delegate
		{
			AggregateException agg = new AggregateException(new Exception("a"), new Exception("b"));
			Check.True(ReferenceEquals(agg, StateFieldInstance.UnwrapAsyncOperationException(agg)), "multi inner kept");
		});

		yield return ("UnwrapAsyncOperationException: plain exception -> itself", delegate
		{
			Exception ex = new Exception("plain");
			Check.True(ReferenceEquals(ex, StateFieldInstance.UnwrapAsyncOperationException(ex)), "plain kept");
		});

		yield return ("UnwrapAsyncOperationException: empty agg (Count!=1) -> itself", delegate
		{
			AggregateException agg = new AggregateException();
			Check.True(ReferenceEquals(agg, StateFieldInstance.UnwrapAsyncOperationException(agg)), "empty agg kept");
		});

		// ===== MapColumnAlignment:HorizontalAlignment -> DataGridViewContentAlignment(Center/Right/default-Left)=====

		yield return ("MapColumnAlignment: Center -> MiddleCenter", delegate
		{
			Check.Equal(DataGridViewContentAlignment.MiddleCenter, StateFieldInstance.MapColumnAlignment(HorizontalAlignment.Center), "center");
		});

		yield return ("MapColumnAlignment: Right -> MiddleRight", delegate
		{
			Check.Equal(DataGridViewContentAlignment.MiddleRight, StateFieldInstance.MapColumnAlignment(HorizontalAlignment.Right), "right");
		});

		yield return ("MapColumnAlignment: Left -> MiddleLeft (default)", delegate
		{
			Check.Equal(DataGridViewContentAlignment.MiddleLeft, StateFieldInstance.MapColumnAlignment(HorizontalAlignment.Left), "left/default");
		});

		// ===== FormatCountDurationSize:$"{count} ({FormatDurationHms(dur)} | {FormatFileSize(size)})" =====
		// size<1000 走 FormatFileSize 的 "XByte" 分支(无 :N culture 依赖);时分秒固定 00:00:00 模式。

		yield return ("FormatCountDurationSize: (5,0,0) -> \"5 (00:00:00 | 0Byte)\"", delegate
		{
			Check.Equal("5 (00:00:00 | 0Byte)", StateFieldInstance.FormatCountDurationSize(5, 0L, 0L), "zero");
		});

		yield return ("FormatCountDurationSize: (0, 61000ms, 999B) -> \"0 (00:01:01 | 999Byte)\"", delegate
		{
			Check.Equal("0 (00:01:01 | 999Byte)", StateFieldInstance.FormatCountDurationSize(0, 61000L, 999L), "min-sec carry");
		});

		yield return ("FormatCountDurationSize: (2, 3661000ms, 512B) -> \"2 (01:01:01 | 512Byte)\"", delegate
		{
			Check.Equal("2 (01:01:01 | 512Byte)", StateFieldInstance.FormatCountDurationSize(2, 3661000L, 512L), "hour carry");
		});

		// ===== GetCachedDurationAndFileSize:object is ValueTuple<long,long> ? 该值 : (0,0)(类型严格)=====

		yield return ("GetCachedDurationAndFileSize: (long,long) box -> that tuple", delegate
		{
			object boxed = (10L, 20L);
			(long, long) r = StateFieldInstance.GetCachedDurationAndFileSize(boxed);
			Check.Equal(10L, r.Item1, "dur");
			Check.Equal(20L, r.Item2, "size");
		});

		yield return ("GetCachedDurationAndFileSize: null -> (0,0)", delegate
		{
			(long, long) r = StateFieldInstance.GetCachedDurationAndFileSize(null);
			Check.Equal(0L, r.Item1, "null dur");
			Check.Equal(0L, r.Item2, "null size");
		});

		yield return ("GetCachedDurationAndFileSize: string -> (0,0)", delegate
		{
			(long, long) r = StateFieldInstance.GetCachedDurationAndFileSize("not a tuple");
			Check.Equal(0L, r.Item1, "string dur");
			Check.Equal(0L, r.Item2, "string size");
		});

		// (int,int) != (long,long) -> 类型不匹配 -> (0,0)
		yield return ("GetCachedDurationAndFileSize: (int,int) box -> (0,0) (type-strict)", delegate
		{
			object boxedInt = (10, 20);
			(long, long) r = StateFieldInstance.GetCachedDurationAndFileSize(boxedInt);
			Check.Equal(0L, r.Item1, "int-tuple dur");
			Check.Equal(0L, r.Item2, "int-tuple size");
		});

		// ===== AddSelectedFilterValue:空白->"" 规范化;首现 Add+记 changed;再现仅计数++ =====

		yield return ("AddSelectedFilterValue: first occurrence -> count=1 + changed entry", delegate
		{
			Dictionary<string, int> counts = new Dictionary<string, int>();
			List<(string, bool)> changed = new List<(string, bool)>();
			StateFieldInstance.AddSelectedFilterValue(counts, changed, "x");
			Check.Equal(1, counts["x"], "count 1");
			Check.Equal(1, changed.Count, "one changed");
			Check.Equal("x", changed[0].Item1, "changed value");
			Check.True(!changed[0].Item2, "changed flag false");
		});

		yield return ("AddSelectedFilterValue: second occurrence -> count++ only (no new changed)", delegate
		{
			Dictionary<string, int> counts = new Dictionary<string, int> { { "x", 1 } };
			List<(string, bool)> changed = new List<(string, bool)>();
			StateFieldInstance.AddSelectedFilterValue(counts, changed, "x");
			Check.Equal(2, counts["x"], "count 2");
			Check.Equal(0, changed.Count, "no new changed");
		});

		yield return ("AddSelectedFilterValue: null value -> normalized to \"\"", delegate
		{
			Dictionary<string, int> counts = new Dictionary<string, int>();
			List<(string, bool)> changed = new List<(string, bool)>();
			StateFieldInstance.AddSelectedFilterValue(counts, changed, null);
			Check.Equal(1, counts[""], "null -> empty key");
			Check.Equal("", changed[0].Item1, "null -> empty changed");
		});

		yield return ("AddSelectedFilterValue: whitespace value -> normalized to \"\"", delegate
		{
			Dictionary<string, int> counts = new Dictionary<string, int>();
			List<(string, bool)> changed = new List<(string, bool)>();
			StateFieldInstance.AddSelectedFilterValue(counts, changed, "   ");
			Check.Equal(1, counts[""], "whitespace -> empty key");
		});

		yield return ("AddSelectedFilterValue: existing empty key + empty value -> count++", delegate
		{
			Dictionary<string, int> counts = new Dictionary<string, int> { { "", 1 } };
			List<(string, bool)> changed = new List<(string, bool)>();
			StateFieldInstance.AddSelectedFilterValue(counts, changed, "");
			Check.Equal(2, counts[""], "empty count 2");
			Check.Equal(0, changed.Count, "no new changed for existing empty");
		});

		// ===== IsEnabledConfiguredDirectory:!Disabled && !IsAnyFile()(IsAnyFile = DirPath=="(Any files)")=====

		yield return ("IsEnabledConfiguredDirectory: enabled real dir -> true", delegate
		{
			ListViewFileSettingFileInfo dir = new ListViewFileSettingFileInfo { Disabled = false, DirPath = "C:/music" };
			Check.True(StateFieldInstance.IsEnabledConfiguredDirectory(dir), "enabled real");
		});

		yield return ("IsEnabledConfiguredDirectory: disabled real dir -> false", delegate
		{
			ListViewFileSettingFileInfo dir = new ListViewFileSettingFileInfo { Disabled = true, DirPath = "C:/music" };
			Check.True(!StateFieldInstance.IsEnabledConfiguredDirectory(dir), "disabled real");
		});

		yield return ("IsEnabledConfiguredDirectory: enabled any-file -> false", delegate
		{
			ListViewFileSettingFileInfo dir = new ListViewFileSettingFileInfo { Disabled = false, DirPath = ListViewFileSettingFileInfo.ANY_FILE_DUMMY_PATH };
			Check.True(!StateFieldInstance.IsEnabledConfiguredDirectory(dir), "enabled any-file");
		});

		yield return ("IsEnabledConfiguredDirectory: disabled any-file -> false", delegate
		{
			ListViewFileSettingFileInfo dir = new ListViewFileSettingFileInfo { Disabled = true, DirPath = ListViewFileSettingFileInfo.ANY_FILE_DUMMY_PATH };
			Check.True(!StateFieldInstance.IsEnabledConfiguredDirectory(dir), "disabled any-file");
		});

		// ===== NormalizeDroppedFilePath:Replace("\"","")(去所有引号)再 Trim(去首尾空白)=====

		yield return ("NormalizeDroppedFilePath: quoted path -> unquoted", delegate
		{
			Check.Equal("C:/x", StateFieldInstance.NormalizeDroppedFilePath("\"C:/x\""), "quoted");
		});

		yield return ("NormalizeDroppedFilePath: surrounding whitespace -> trimmed", delegate
		{
			Check.Equal("C:/x", StateFieldInstance.NormalizeDroppedFilePath("  C:/x  "), "whitespace");
		});

		// 去引号在前、Trim 在后:内部空格保留,仅首尾空白被 trim。
		yield return ("NormalizeDroppedFilePath: quotes+spaces, inner space kept -> \"C:/a b\"", delegate
		{
			Check.Equal("C:/a b", StateFieldInstance.NormalizeDroppedFilePath("\" C:/a b \""), "inner space kept");
		});

		// Replace 去所有引号(非仅首尾)。
		yield return ("NormalizeDroppedFilePath: inner quote removed too", delegate
		{
			Check.Equal("ab", StateFieldInstance.NormalizeDroppedFilePath("a\"b"), "inner quote");
		});

		yield return ("NormalizeDroppedFilePath: plain path -> unchanged", delegate
		{
			Check.Equal("plain", StateFieldInstance.NormalizeDroppedFilePath("plain"), "plain");
		});

		// ===== ParseLeadingNumber:Regex "^\\d+" -> Success?(TryParse?result:0):-1(三路径)=====

		yield return ("ParseLeadingNumber: leading digits -> number", delegate
		{
			Check.Equal(123, StateFieldInstance.ParseLeadingNumber("123abc"), "123abc");
		});

		yield return ("ParseLeadingNumber: no leading digit -> -1", delegate
		{
			Check.Equal(-1, StateFieldInstance.ParseLeadingNumber("abc"), "abc");
		});

		yield return ("ParseLeadingNumber: empty -> -1", delegate
		{
			Check.Equal(-1, StateFieldInstance.ParseLeadingNumber(""), "empty");
		});

		yield return ("ParseLeadingNumber: leading zeros -> parsed int", delegate
		{
			Check.Equal(7, StateFieldInstance.ParseLeadingNumber("007"), "007");
		});

		// 全数字但溢出 int -> Match.Success 但 int.TryParse 失败 -> 0(非 -1)。
		yield return ("ParseLeadingNumber: overflow digits -> 0 (matched but TryParse fails)", delegate
		{
			Check.Equal(0, StateFieldInstance.ParseLeadingNumber("99999999999999999999"), "overflow");
		});

		yield return ("ParseLeadingNumber: \"0\" -> 0", delegate
		{
			Check.Equal(0, StateFieldInstance.ParseLeadingNumber("0"), "zero");
		});

		// 前导空格 -> "^\\d+" 不匹配 -> -1。
		yield return ("ParseLeadingNumber: leading space -> -1 (anchored regex)", delegate
		{
			Check.Equal(-1, StateFieldInstance.ParseLeadingNumber(" 5"), "leading space");
		});

		// ===== 对抗审计完备性补充(Workflow 3-lens 审计产出,每条已逐一 trace 独立复核 + probe-first 锁定)=====
		// 补未覆盖分支(递归/短路/类型守卫/switch default)、边界(null/溢出/arity/嵌套)与 latent 行为。

		// BuildBatchResultMessage:分支优先级 + 插值 + branch3 公式 + completedMessage 进入 format 模板的 latent 特性。
		yield return ("BuildBatchResultMessage: primary>0 & skipped>0 -> branch2 wins over branch3 (short-circuit)", delegate
		{
			(string, bool) r = StateFieldInstance.BuildBatchResultMessage(1, "DONE", 1, 0, 2, 0, "LOG", includeSkippedBranch: true);
			Check.Equal("DONE\nLOG", r.Item1, "primary>0 short-circuits before skipped branch");
			Check.True(!r.Item2, "branch2 Item2 false");
		});

		// totalCount>1 直取 branch1(不看 includeSkip/skipped);StartsWith completed 排除 branch3 的 Msg_Skipped 前缀。
		yield return ("BuildBatchResultMessage: totalCount>1 -> branch1 wins over branch3 + primary interpolated", delegate
		{
			(string, bool) r = StateFieldInstance.BuildBatchResultMessage(2, "DONE", 11, 22, 33, 44, "LOG", includeSkippedBranch: true);
			Check.True(!r.Item2, "branch1 Item2 false");
			Check.True(r.Item1.StartsWith("DONE\n"), "branch1 keeps completed prefix (excludes branch3 skipped-prefix)");
			Check.True(r.Item1.EndsWith("\nLOG"), "branch1 ends with log");
			Check.True(r.Item1.Contains("11"), "branch1 interpolates primaryCount");
		});

		// branch3 = Msg_Skipped + "\n" + log(同源 Resources,culture 自洽);且不含 completedMessage。
		yield return ("BuildBatchResultMessage: branch3 = Msg_Skipped + newline + log (excludes completedMessage)", delegate
		{
			(string, bool) r = StateFieldInstance.BuildBatchResultMessage(1, "DONE", 0, 0, 2, 0, "LOG", includeSkippedBranch: true);
			Check.True(!r.Item1.Contains("DONE"), "branch3 excludes completedMessage");
			Check.Equal(MusicTagWinApp.Properties.Resources.Msg_Skipped + "\nLOG", r.Item1, "branch3 = Msg_Skipped + newline + log");
		});

		// latent:branch1 把 completedMessage 拼进 format 模板,故其中的 {0} 被当占位符插值(primaryCount=7)。
		yield return ("BuildBatchResultMessage: branch1 completedMessage is part of format template (braces interpolate)", delegate
		{
			(string, bool) r = StateFieldInstance.BuildBatchResultMessage(2, "{0}", 7, 0, 0, 0, "LOG", includeSkippedBranch: false);
			Check.True(r.Item1.StartsWith("7\n"), "completedMessage '{0}' interpolated with primaryCount(=7)");
		});

		// IsCancellationException:嵌套 AggregateException 递归(正/负)、多 inner All、派生类型、depth-2 Count 守卫、null 安全。
		yield return ("IsCancellationException: cancelled + AggregateException(AggregateException(OCE)) -> true (depth-2 recursion)", delegate
		{
			CancellationTokenSource cts = new CancellationTokenSource();
			cts.Cancel();
			AggregateException nested = new AggregateException(new AggregateException(new OperationCanceledException()));
			Check.True(StateFieldInstance.IsCancellationException(nested, cts), "nested all-cancel recurses to true");
		});

		yield return ("IsCancellationException: cancelled + nested agg with deep plain -> false (propagates up)", delegate
		{
			CancellationTokenSource cts = new CancellationTokenSource();
			cts.Cancel();
			AggregateException nested = new AggregateException(new AggregateException(new OperationCanceledException(), new Exception("x")));
			Check.True(!StateFieldInstance.IsCancellationException(nested, cts), "deep plain makes nested All() false, propagates up");
		});

		yield return ("IsCancellationException: cancelled + AggregateException(OCE, OCE) -> true (All over multiple inners)", delegate
		{
			CancellationTokenSource cts = new CancellationTokenSource();
			cts.Cancel();
			AggregateException agg = new AggregateException(new OperationCanceledException(), new OperationCanceledException());
			Check.True(StateFieldInstance.IsCancellationException(agg, cts), "multi-inner all-OCE -> true");
		});

		// TaskCanceledException : OperationCanceledException,故 is OCE 命中 -> true(真实异步取消类型)。
		yield return ("IsCancellationException: cancelled + TaskCanceledException -> true (derived from OCE)", delegate
		{
			CancellationTokenSource cts = new CancellationTokenSource();
			cts.Cancel();
			Check.True(StateFieldInstance.IsCancellationException(new System.Threading.Tasks.TaskCanceledException(), cts), "TaskCanceledException is-a OCE");
		});

		// depth-2 的 Count>0 守卫:内层空 agg(Count==0)不进 All -> false。
		yield return ("IsCancellationException: cancelled + AggregateException(empty agg) -> false (depth-2 Count guard)", delegate
		{
			CancellationTokenSource cts = new CancellationTokenSource();
			cts.Cancel();
			AggregateException agg = new AggregateException(new AggregateException());
			Check.True(!StateFieldInstance.IsCancellationException(agg, cts), "inner empty agg hits Count>0 guard -> false");
		});

		yield return ("IsCancellationException: cancelled + null exception -> false (no NRE)", delegate
		{
			CancellationTokenSource cts = new CancellationTokenSource();
			cts.Cancel();
			Check.True(!StateFieldInstance.IsCancellationException(null, cts), "null is neither OCE nor AggregateException");
		});

		// UnwrapAsyncOperationException:null 透传 + 单层非递归(对照 IsCancellationException 的递归)。
		yield return ("UnwrapAsyncOperationException: null -> null (no NRE)", delegate
		{
			Check.Null(StateFieldInstance.UnwrapAsyncOperationException(null), "null in -> null out");
		});

		yield return ("UnwrapAsyncOperationException: nested single-inner agg -> inner agg (one level, no recursion)", delegate
		{
			Exception deepest = new Exception("deepest");
			AggregateException innerAgg = new AggregateException(deepest);
			AggregateException outerAgg = new AggregateException(innerAgg);
			Check.True(ReferenceEquals(innerAgg, StateFieldInstance.UnwrapAsyncOperationException(outerAgg)), "unwraps exactly one level: inner agg, not deepest");
		});

		// ParseLeadingNumber:null 抛 ArgumentNullException(Regex.Match input 契约,非三路径)+ int.MaxValue 精确边界。
		yield return ("ParseLeadingNumber: null -> throws ArgumentNullException (Regex.Match input contract)", delegate
		{
			bool threw = false;
			try { StateFieldInstance.ParseLeadingNumber(null); }
			catch (ArgumentNullException) { threw = true; }
			Check.True(threw, "null input throws ArgumentNullException");
		});

		yield return ("ParseLeadingNumber: int.MaxValue -> self, MaxValue+1 -> 0 (overflow boundary)", delegate
		{
			Check.Equal(2147483647, StateFieldInstance.ParseLeadingNumber("2147483647"), "int.MaxValue still parses");
			Check.Equal(0, StateFieldInstance.ParseLeadingNumber("2147483648"), "MaxValue+1 overflows to 0");
		});

		// AddSelectedFilterValue:仅 IsNullOrWhiteSpace 归一为 "",【从不】Trim;" x " 保留 verbatim、与 "x" 不同键。
		yield return ("AddSelectedFilterValue: ' x ' (not all-whitespace) -> kept verbatim, distinct from 'x' (no Trim)", delegate
		{
			Dictionary<string, int> counts = new Dictionary<string, int> { { "x", 5 } };
			List<(string, bool)> changed = new List<(string, bool)>();
			StateFieldInstance.AddSelectedFilterValue(counts, changed, " x ");
			Check.Equal(5, counts["x"], "existing 'x' untouched");
			Check.Equal(1, counts[" x "], "' x ' added as distinct key (not trimmed to 'x')");
			Check.Equal(1, changed.Count, "one changed entry");
			Check.Equal(" x ", changed[0].Item1, "changed records ' x ' verbatim");
		});

		// GetCachedDurationAndFileSize:类型守卫对 arity 严格(3-tuple 拒绝);match 路径 verbatim 透传(无 clamp)。
		yield return ("GetCachedDurationAndFileSize: 3-tuple (long,long,long) -> (0,0) (arity-strict)", delegate
		{
			object boxed = (1L, 2L, 3L);
			(long, long) r = StateFieldInstance.GetCachedDurationAndFileSize(boxed);
			Check.Equal(0L, r.Item1, "3-tuple -> dur 0");
			Check.Equal(0L, r.Item2, "3-tuple -> size 0");
		});

		yield return ("GetCachedDurationAndFileSize: negative/extreme (long,long) -> verbatim (no clamp)", delegate
		{
			object boxed = (-5L, long.MaxValue);
			(long, long) r = StateFieldInstance.GetCachedDurationAndFileSize(boxed);
			Check.Equal(-5L, r.Item1, "negative dur returned verbatim (no clamp)");
			Check.Equal(long.MaxValue, r.Item2, "max size returned verbatim");
		});

		// MapColumnAlignment:default 是 catch-all,未定义枚举值也归 MiddleLeft(不抛)。
		yield return ("MapColumnAlignment: undefined enum value -> MiddleLeft (default catch-all, no throw)", delegate
		{
			Check.Equal(DataGridViewContentAlignment.MiddleLeft, StateFieldInstance.MapColumnAlignment((HorizontalAlignment)999), "undefined enum -> default MiddleLeft");
		});

		// FormatCountDurationSize:size>=1000 走 FormatFileSize 的 KB 半边(结构断言避 :N culture);hours min-width 不截断。
		yield return ("FormatCountDurationSize: size>=1000 -> KB branch composed (culture-safe structural)", delegate
		{
			string r = StateFieldInstance.FormatCountDurationSize(7, 0L, 2048L);
			Check.True(r.StartsWith("7 (00:00:00 | "), "count+duration prefix intact when size routes to KB branch");
			Check.True(r.EndsWith("KB)"), "size>=1000 surfaces KB suffix inside outer template");
		});

		yield return ("FormatCountDurationSize: hours>=100 -> 3-digit hours, no wrap (min-width specifier)", delegate
		{
			Check.Equal("1 (100:00:00 | 0Byte)", StateFieldInstance.FormatCountDurationSize(1, 360000000L, 0L), "100h renders as 100, not rolled to days");
		});

		// IsEnabledConfiguredDirectory:无 null 守卫(null -> NRE);null DirPath 经 == 空安全 -> 非 any-file -> enabled。
		yield return ("IsEnabledConfiguredDirectory: null -> NullReferenceException (locks absence of null guard)", delegate
		{
			bool threw = false;
			try { StateFieldInstance.IsEnabledConfiguredDirectory(null); }
			catch (NullReferenceException) { threw = true; }
			Check.True(threw, "null arg dereferenced -> NRE");
		});

		yield return ("IsEnabledConfiguredDirectory: enabled + null DirPath -> true (== null-safe; null != any-file)", delegate
		{
			ListViewFileSettingFileInfo dir = new ListViewFileSettingFileInfo { Disabled = false, DirPath = null };
			Check.True(StateFieldInstance.IsEnabledConfiguredDirectory(dir), "null DirPath treated as enabled real (non-any-file) dir");
		});

		// NormalizeDroppedFilePath:无 null 守卫(null -> NRE on .Replace);纯空白经 Trim 整体塌缩为 ""。
		yield return ("NormalizeDroppedFilePath: null -> NullReferenceException (locks absence of null guard)", delegate
		{
			bool threw = false;
			try { StateFieldInstance.NormalizeDroppedFilePath(null); }
			catch (NullReferenceException) { threw = true; }
			Check.True(threw, "null arg -> NRE on .Replace");
		});

		yield return ("NormalizeDroppedFilePath: empty / pure-whitespace -> \"\" (Trim collapses whole string)", delegate
		{
			Check.Equal("", StateFieldInstance.NormalizeDroppedFilePath(""), "empty -> empty");
			Check.Equal("", StateFieldInstance.NormalizeDroppedFilePath("   "), "pure whitespace -> empty after Trim");
		});

		// ===== 第二轮:纯核提取(从混杂实例方法分离的 behavior-preserving 纯核,probe-first + 对抗验证)=====

		// UpdateSelectedFilterValue:AddSelectedFilterValue 的 decrement 对称体,4+ 分支(增/减/归零移除/缺席)。
		yield return ("UpdateSelectedFilterValue: select absent -> add count=1 + changed(value,false)", delegate
		{
			Dictionary<string, int> counts = new Dictionary<string, int>();
			List<(string, bool)> changed = new List<(string, bool)>();
			StateFieldInstance.UpdateSelectedFilterValue(counts, changed, "x", isSelected: true);
			Check.Equal(1, counts["x"], "added count 1");
			Check.Equal(1, changed.Count, "one changed");
			Check.Equal("x", changed[0].Item1, "changed value x");
			Check.True(!changed[0].Item2, "changed flag false (add)");
		});

		yield return ("UpdateSelectedFilterValue: select present(count=2) -> count=3, no new changed", delegate
		{
			Dictionary<string, int> counts = new Dictionary<string, int> { { "x", 2 } };
			List<(string, bool)> changed = new List<(string, bool)>();
			StateFieldInstance.UpdateSelectedFilterValue(counts, changed, "x", isSelected: true);
			Check.Equal(3, counts["x"], "count incremented to 3");
			Check.Equal(0, changed.Count, "no new changed on existing increment");
		});

		yield return ("UpdateSelectedFilterValue: deselect present(count=3) -> count=2, kept, no changed", delegate
		{
			Dictionary<string, int> counts = new Dictionary<string, int> { { "x", 3 } };
			List<(string, bool)> changed = new List<(string, bool)>();
			StateFieldInstance.UpdateSelectedFilterValue(counts, changed, "x", isSelected: false);
			Check.Equal(2, counts["x"], "count decremented to 2 (still > 0)");
			Check.Equal(0, changed.Count, "no changed while count stays positive");
		});

		yield return ("UpdateSelectedFilterValue: deselect present(count=1) -> remove + changed(value,true)", delegate
		{
			Dictionary<string, int> counts = new Dictionary<string, int> { { "x", 1 } };
			List<(string, bool)> changed = new List<(string, bool)>();
			StateFieldInstance.UpdateSelectedFilterValue(counts, changed, "x", isSelected: false);
			Check.True(!counts.ContainsKey("x"), "count hit zero -> key removed");
			Check.Equal(1, changed.Count, "one changed on removal");
			Check.Equal("x", changed[0].Item1, "changed value x");
			Check.True(changed[0].Item2, "changed flag TRUE (removal, unlike add)");
		});

		// 缺席 + 取消选中:既不进 TryGetValue 分支,也不满足 else-if(isSelected) -> 完全 no-op。
		yield return ("UpdateSelectedFilterValue: deselect absent -> no-op", delegate
		{
			Dictionary<string, int> counts = new Dictionary<string, int>();
			List<(string, bool)> changed = new List<(string, bool)>();
			StateFieldInstance.UpdateSelectedFilterValue(counts, changed, "x", isSelected: false);
			Check.Equal(0, counts.Count, "no key added on deselect-absent");
			Check.Equal(0, changed.Count, "no changed on deselect-absent");
		});

		yield return ("UpdateSelectedFilterValue: whitespace value select -> normalized to \"\" key", delegate
		{
			Dictionary<string, int> counts = new Dictionary<string, int>();
			List<(string, bool)> changed = new List<(string, bool)>();
			StateFieldInstance.UpdateSelectedFilterValue(counts, changed, "  ", isSelected: true);
			Check.Equal(1, counts[""], "whitespace -> empty-string key");
			Check.Equal("", changed[0].Item1, "changed records empty string");
		});

		// AccumulateClampedTotals:选中累加 / 取消选中扣减,两者各 clamp 到 >=0。
		yield return ("AccumulateClampedTotals: select -> add both", delegate
		{
			(long, long) r = StateFieldInstance.AccumulateClampedTotals(100L, 200L, 10L, 20L, isSelected: true);
			Check.Equal(110L, r.Item1, "duration added");
			Check.Equal(220L, r.Item2, "size added");
		});

		yield return ("AccumulateClampedTotals: deselect -> subtract both (positive)", delegate
		{
			(long, long) r = StateFieldInstance.AccumulateClampedTotals(100L, 200L, 10L, 20L, isSelected: false);
			Check.Equal(90L, r.Item1, "duration subtracted");
			Check.Equal(180L, r.Item2, "size subtracted");
		});

		// 取消选中导致负值 -> 双 clamp 到 0(防负漂移,load-bearing)。
		yield return ("AccumulateClampedTotals: deselect underflow -> both clamped to 0", delegate
		{
			(long, long) r = StateFieldInstance.AccumulateClampedTotals(5L, 5L, 10L, 20L, isSelected: false);
			Check.Equal(0L, r.Item1, "duration clamped to 0");
			Check.Equal(0L, r.Item2, "size clamped to 0");
		});

		// 独立 clamp:时长下溢归 0、字节数仍为正(各自判定)。
		yield return ("AccumulateClampedTotals: deselect partial underflow -> only negative axis clamped", delegate
		{
			(long, long) r = StateFieldInstance.AccumulateClampedTotals(5L, 100L, 10L, 20L, isSelected: false);
			Check.Equal(0L, r.Item1, "duration underflow -> 0");
			Check.Equal(80L, r.Item2, "size stays positive (80)");
		});

		yield return ("AccumulateClampedTotals: select from zero -> item totals", delegate
		{
			(long, long) r = StateFieldInstance.AccumulateClampedTotals(0L, 0L, 10L, 20L, isSelected: true);
			Check.Equal(10L, r.Item1, "duration from 0");
			Check.Equal(20L, r.Item2, "size from 0");
		});

		// ResolveSearchValue:非空白 string 原样 / int>0 -> ToString / 其余 -> null(不写)。
		yield return ("ResolveSearchValue: non-blank string -> itself", delegate
		{
			Check.Equal("abc", StateFieldInstance.ResolveSearchValue("abc"), "non-blank string");
		});

		yield return ("ResolveSearchValue: whitespace string -> null (not written)", delegate
		{
			Check.Null(StateFieldInstance.ResolveSearchValue("  "), "whitespace -> null");
		});

		yield return ("ResolveSearchValue: empty string -> null", delegate
		{
			Check.Null(StateFieldInstance.ResolveSearchValue(""), "empty -> null");
		});

		// 非空白但含首尾空格的串原样返回(不 Trim)。
		yield return ("ResolveSearchValue: surrounded-by-space string -> verbatim (no trim)", delegate
		{
			Check.Equal("  x  ", StateFieldInstance.ResolveSearchValue("  x  "), "non-blank verbatim");
		});

		yield return ("ResolveSearchValue: int > 0 -> ToString", delegate
		{
			Check.Equal("5", StateFieldInstance.ResolveSearchValue(5), "positive int");
		});

		yield return ("ResolveSearchValue: int 0 -> null (not > 0)", delegate
		{
			Check.Null(StateFieldInstance.ResolveSearchValue(0), "zero int -> null");
		});

		yield return ("ResolveSearchValue: negative int -> null", delegate
		{
			Check.Null(StateFieldInstance.ResolveSearchValue(-3), "negative int -> null");
		});

		yield return ("ResolveSearchValue: null -> null", delegate
		{
			Check.Null(StateFieldInstance.ResolveSearchValue(null), "null -> null");
		});

		// 其他类型(double 等,非 string 非 int)-> null。
		yield return ("ResolveSearchValue: other type (double) -> null", delegate
		{
			Check.Null(StateFieldInstance.ResolveSearchValue(3.14), "double -> null");
		});

		// BuildSelectedFilePreview:前 10 行各 "<text>\n";第 11 行起 "..." 并停止;空集 -> ""。
		yield return ("BuildSelectedFilePreview: empty -> \"\"", delegate
		{
			Check.Equal("", StateFieldInstance.BuildSelectedFilePreview(new List<string>()), "empty -> empty");
		});

		yield return ("BuildSelectedFilePreview: single row -> \"row\\n\"", delegate
		{
			Check.Equal("a\n", StateFieldInstance.BuildSelectedFilePreview(new List<string> { "a" }), "one row");
		});

		yield return ("BuildSelectedFilePreview: three rows -> joined with trailing newlines", delegate
		{
			Check.Equal("a\nb\nc\n", StateFieldInstance.BuildSelectedFilePreview(new List<string> { "a", "b", "c" }), "three rows");
		});

		// 恰 10 行:全部进入 <10 分支,无 "..."。
		yield return ("BuildSelectedFilePreview: exactly 10 rows -> 10 lines, NO ellipsis", delegate
		{
			List<string> rows = new List<string>();
			string expected = "";
			for (int i = 0; i < 10; i++)
			{
				rows.Add("x");
				expected += "x\n";
			}
			Check.Equal(expected, StateFieldInstance.BuildSelectedFilePreview(rows), "10 rows no ellipsis");
		});

		// 11 行:前 10 行 + 第 11 行触发 "..." 并 break(故仅 10 个 "x\n" + "...",其后元素不再处理)。
		yield return ("BuildSelectedFilePreview: 11 rows -> 10 lines + ellipsis (break on 11th)", delegate
		{
			List<string> rows = new List<string>();
			for (int i = 0; i < 11; i++)
			{
				rows.Add("x");
			}
			string expected = "";
			for (int i = 0; i < 10; i++)
			{
				expected += "x\n";
			}
			expected += "...";
			Check.Equal(expected, StateFieldInstance.BuildSelectedFilePreview(rows), "11 rows -> 10 + ellipsis");
		});

		// ===== 第二轮对抗验证补充(4 纯核行为等价 PRESERVED,以下为完备性 gap 补强,逐条 trace 复核)=====

		// count>0 阈值最小正边界(count=2 deselect ->1 保留):抓 count>0 误变 count>1 的 off-by-one 突变。
		yield return ("UpdateSelectedFilterValue: deselect present(count=2) -> count=1 (minimal positive kept, >0 boundary)", delegate
		{
			Dictionary<string, int> counts = new Dictionary<string, int> { { "x", 2 } };
			List<(string, bool)> changed = new List<(string, bool)>();
			StateFieldInstance.UpdateSelectedFilterValue(counts, changed, "x", isSelected: false);
			Check.True(counts.ContainsKey("x"), "key retained when decrement lands on exactly 1");
			Check.Equal(1, counts["x"], "count decremented to minimal-positive 1 and kept (catches count>1 mutant)");
			Check.Equal(0, changed.Count, "no changed entry recorded when result is 1 (not removed)");
		});

		// clamp 独立性反向(size 轴下溢、duration 不下溢):抓 size clamp 误置 duration=0 的 cross-wiring 突变。
		yield return ("AccumulateClampedTotals: deselect partial underflow (size axis) -> only size clamped, duration kept", delegate
		{
			(long, long) r = StateFieldInstance.AccumulateClampedTotals(100L, 5L, 10L, 20L, isSelected: false);
			Check.Equal(90L, r.Item1, "duration stays positive (90), NOT zeroed by size clamp (axis independence)");
			Check.Equal(0L, r.Item2, "size underflow (5-20=-15) -> clamped to 0");
		});

		// unchecked 溢出 + clamp 耦合的 latent 行为:MaxValue+1 wrap 为负 -> clamp 0(非朴素预期的大正数)。
		yield return ("AccumulateClampedTotals: select overflow (MaxValue + positive) wraps negative -> clamped to 0", delegate
		{
			(long, long) r = StateFieldInstance.AccumulateClampedTotals(long.MaxValue, 0L, 1L, 0L, isSelected: true);
			Check.Equal(0L, r.Item1, "MaxValue+1 wraps to MinValue (<0, unchecked) then clamps to 0");
			Check.Equal(0L, r.Item2, "size unchanged (0+0=0)");
		});

		// 数字样 string("0"/"-3")原样返回:string 路径无 >0 过滤(对照 int 0/-3 -> null)。
		yield return ("ResolveSearchValue: numeric-looking string -> verbatim (string path has NO >0 filter)", delegate
		{
			Check.Equal("0", StateFieldInstance.ResolveSearchValue("0"), "string \"0\" verbatim, not null (contrast int 0 -> null)");
			Check.Equal("-3", StateFieldInstance.ResolveSearchValue("-3"), "string \"-3\" verbatim, not null (contrast int -3 -> null)");
		});

		// is int 类型严格:正 long(非 int)落入 null(无数字 widening)。
		yield return ("ResolveSearchValue: positive long (not int) -> null (is-int type-strict)", delegate
		{
			Check.Null(StateFieldInstance.ResolveSearchValue(5L), "positive long is not a boxed int -> null");
		});

		// 锁定提取的 load-bearing 惰性语义:15 元素源仅 pull 11(10 append + 第 11 projected-then-discarded),非 eager 全求值。
		yield return ("BuildSelectedFilePreview: lazy projection pulls exactly 11 of 15 (no eager full eval) + 11th discarded", delegate
		{
			int pulls = 0;
			IEnumerable<string> CountingSource()
			{
				for (int i = 0; i < 15; i++)
				{
					pulls++;
					yield return "r" + i;
				}
			}
			string result = StateFieldInstance.BuildSelectedFilePreview(CountingSource());
			string expected = "";
			for (int i = 0; i < 10; i++)
			{
				expected += "r" + i + "\n";
			}
			expected += "...";
			Check.Equal(expected, result, "15-source -> r0..r9 lines + ellipsis (r10..r14 absent)");
			Check.Equal(11, pulls, "lazy early-stop: pulled exactly 11 (10 appended + 1 boundary projected-then-discarded), not all 15");
		});

		// null 元素经 string 连接合并为空段(firstColumnText + "\n" -> "\n"),与原 CellTexts[0]==null 路径一致、不抛、不渲染 "null"。
		yield return ("BuildSelectedFilePreview: null element -> \"\\n\" (concat coalesces null)", delegate
		{
			Check.Equal("\n", StateFieldInstance.BuildSelectedFilePreview(new List<string> { null }), "single null -> newline only");
			Check.Equal("a\n\nb\n", StateFieldInstance.BuildSelectedFilePreview(new List<string> { "a", null, "b" }), "null in middle -> empty segment, order kept");
		});

		// ===== ResolveFailureMessage:收敛 3 个 *FailureReporter 失败消息优先级(loadError 优先,回退 fallback ?? Msg_SaveFail)=====
		// SaveTag/UndoSaveTag/TagSave 三处消息选择逻辑归约到此纯三元式,锁定优先级 + IsNullOrWhiteSpace 三态 + ?? 回退链。

		// loadError 非空时优先返回它,完全忽略 fallback(fallback 为 null 或非空皆然)。
		yield return ("ResolveFailureMessage: non-empty loadError wins over any fallback", delegate
		{
			Check.Equal("ERR", StateFieldInstance.ResolveFailureMessage("ERR", "FB"), "loadError beats non-null fallback");
			Check.Equal("ERR", StateFieldInstance.ResolveFailureMessage("ERR", null), "loadError beats null fallback");
		});

		// loadError 为 null/empty/whitespace(IsNullOrWhiteSpace 三态皆真)时落到 fallback。
		yield return ("ResolveFailureMessage: null/empty/whitespace loadError -> fallback", delegate
		{
			Check.Equal("FB", StateFieldInstance.ResolveFailureMessage(null, "FB"), "null loadError -> fallback");
			Check.Equal("FB", StateFieldInstance.ResolveFailureMessage("", "FB"), "empty loadError -> fallback");
			Check.Equal("FB", StateFieldInstance.ResolveFailureMessage("   ", "FB"), "whitespace loadError -> fallback");
		});

		// loadError 与 fallback 皆空 -> 回退链终点 Resources.Msg_SaveFail(与产品同源常量)。
		yield return ("ResolveFailureMessage: both empty -> Msg_SaveFail", delegate
		{
			Check.Equal(Resources.Msg_SaveFail, StateFieldInstance.ResolveFailureMessage(null, null), "null+null -> Msg_SaveFail");
			Check.Equal(Resources.Msg_SaveFail, StateFieldInstance.ResolveFailureMessage("", null), "empty loadError + null fallback -> Msg_SaveFail");
		});

		// IsNullOrWhiteSpace 仅对纯空白为真:含实字符(即便前后有空格)的 loadError 视为非空并原样返回(不 trim)。
		yield return ("ResolveFailureMessage: loadError with real chars kept verbatim (no trim)", delegate
		{
			Check.Equal(" x ", StateFieldInstance.ResolveFailureMessage(" x ", "FB"), "padded real content is non-empty, returned verbatim");
		});

		// ===== BuildClearTagsResultMessage:收敛 StartClearTags 完成段三路(itemCount>1 / success>0 / else)=====
		// 传 Page(非预 ToString 的 string)以保 errorLog.ToString 调用位置/次数逐字节不变。返回 (消息 Item1, 是否错误 Item2)。
		// 分支1 含本地化 Resources -> 结构断言(EndsWith/Length,避 culture 脆性);分支2/3/边界 -> 精确断言。
		yield return ("BuildClearTagsResultMessage: itemCount>1 -> header + trailing errorLog, not error", delegate
		{
			Page log = new Page();
			log.AddLine("E1");
			log.AddLine("E2");
			(string, bool) r = StateFieldInstance.BuildClearTagsResultMessage(3, 2, 1, 3, log);
			Check.True(!r.Item2, "multi-file -> Item2 false (not error)");
			Check.True(r.Item1.EndsWith("\n" + log.ToString()), "multi-file appends newline+errorLog at tail");
			Check.True(r.Item1.Length > log.ToString().Length + 1, "multi-file has header before errorLog (distinguishes else branch)");
		});

		yield return ("BuildClearTagsResultMessage: single-file success -> bare completed message", delegate
		{
			Page log = new Page();
			log.AddLine("ignored");
			(string, bool) r = StateFieldInstance.BuildClearTagsResultMessage(1, 1, 0, 1, log);
			Check.Equal(Resources.Msg_CleartagsCompleted, r.Item1, "success branch -> bare Msg_CleartagsCompleted (no count, no errorLog)");
			Check.True(!r.Item2, "success branch -> Item2 false");
		});

		yield return ("BuildClearTagsResultMessage: single-file all-fail -> bare errorLog + error flag", delegate
		{
			Page log = new Page();
			log.AddLine("boom");
			(string, bool) r = StateFieldInstance.BuildClearTagsResultMessage(1, 0, 1, 1, log);
			Check.Equal(log.ToString(), r.Item1, "else branch -> bare errorLog (no header)");
			Check.True(r.Item2, "else branch -> Item2 true (error)");
		});

		yield return ("BuildClearTagsResultMessage: itemCount<=1 boundary -> else branch (empty log / zero count)", delegate
		{
			(string, bool) r1 = StateFieldInstance.BuildClearTagsResultMessage(1, 0, 0, 1, new Page());
			Check.Equal("", r1.Item1, "single empty-log all-fail -> empty message");
			Check.True(r1.Item2, "single empty-log all-fail -> Item2 true");
			(string, bool) r2 = StateFieldInstance.BuildClearTagsResultMessage(0, 0, 0, 0, new Page());
			Check.True(r2.Item2, "itemCount 0 -> else (0>1 false, 0>0 false) -> Item2 true");
		});

		// ===== TruncateLyricsOrCommentDisplayValue:lyrics/comment 列值 >20 字符截断前 20 + Trim,余列原样 =====
		yield return ("TruncateLyricsOrCommentDisplayValue: lyrics/comment >20 truncate, else verbatim", delegate
		{
			Check.Equal(new string('a', 20), StateFieldInstance.TruncateLyricsOrCommentDisplayValue("lyrics", new string('a', 26)), "lyrics 26 -> first 20");
			Check.Equal(new string('b', 20), StateFieldInstance.TruncateLyricsOrCommentDisplayValue("comment", new string('b', 21)), "comment 21 -> first 20");
			Check.Equal("short", StateFieldInstance.TruncateLyricsOrCommentDisplayValue("comment", "short"), "len<=20 verbatim");
			Check.Equal(new string('a', 30), StateFieldInstance.TruncateLyricsOrCommentDisplayValue("title", new string('a', 30)), "non-lyrics/comment column verbatim even if >20");
			Check.Equal(new string('a', 20), StateFieldInstance.TruncateLyricsOrCommentDisplayValue("lyrics", new string('a', 20)), "exactly 20 (not >20) -> verbatim");
			Check.Equal("abcdefghijklmnopqr", StateFieldInstance.TruncateLyricsOrCommentDisplayValue("lyrics", "abcdefghijklmnopqr  Z"), "cut@20 (18 letters + 2 spaces) then Trim trailing spaces");
		});

		// ===== BuildDeleteFilesResultMessage:收敛 DeleteFilesTaskContext.ShowCompletionResult 三路(itemCount>1 / deleted>0 / else)=====
		// 与 BuildClearTags 同构:传 Page 保 errorLog.ToString 次数;返回 (消息 Item1, 是否错误 Item2);Item2 只在单项全败为 true。
		yield return ("BuildDeleteFilesResultMessage: itemCount>1 -> completed header + trailing errorLog, not error", delegate
		{
			Page log = new Page();
			log.AddLine("D1");
			log.AddLine("D2");
			(string, bool) r = StateFieldInstance.BuildDeleteFilesResultMessage(3, 2, 1, 3, log);
			Check.True(!r.Item2, "multi-file -> Item2 false (not error)");
			Check.True(r.Item1.StartsWith(Resources.Msg_DeleteFilesCompleted), "multi-file starts with completed header");
			Check.True(r.Item1.EndsWith("\n" + log.ToString()), "multi-file appends newline+errorLog at tail");
			Check.True(r.Item1.Length > log.ToString().Length + 1, "multi-file has header before errorLog (distinguishes else branch)");
		});

		yield return ("BuildDeleteFilesResultMessage: single-file success -> bare completed message", delegate
		{
			Page log = new Page();
			log.AddLine("ignored");
			(string, bool) r = StateFieldInstance.BuildDeleteFilesResultMessage(1, 1, 0, 1, log);
			Check.Equal(Resources.Msg_DeleteFilesCompleted, r.Item1, "success branch -> bare Msg_DeleteFilesCompleted (no count, no errorLog)");
			Check.True(!r.Item2, "success branch -> Item2 false");
		});

		yield return ("BuildDeleteFilesResultMessage: single-file all-fail -> bare errorLog + error flag", delegate
		{
			Page log = new Page();
			log.AddLine("boom");
			(string, bool) r = StateFieldInstance.BuildDeleteFilesResultMessage(1, 0, 1, 1, log);
			Check.Equal(log.ToString(), r.Item1, "else branch -> bare errorLog (no header)");
			Check.True(r.Item2, "else branch -> Item2 true (error)");
		});

		yield return ("BuildDeleteFilesResultMessage: itemCount<=1 boundary -> else branch (empty log / zero count)", delegate
		{
			(string, bool) r1 = StateFieldInstance.BuildDeleteFilesResultMessage(1, 0, 0, 1, new Page());
			Check.Equal("", r1.Item1, "single empty-log all-fail -> empty message");
			Check.True(r1.Item2, "single empty-log all-fail -> Item2 true");
			(string, bool) r2 = StateFieldInstance.BuildDeleteFilesResultMessage(0, 0, 0, 0, new Page());
			Check.True(r2.Item2, "itemCount 0 -> else (0>1 false, 0>0 false) -> Item2 true");
		});

		// ===== BuildExtractCoversResultMessage:收敛 ExtractCoversTaskContext.ShowCompletionResult 三路(with-skip 变体)=====
		// 同构 with-skip:参数 (itemCount, extractedCount, failedCount, skippedCount, processedCount, errorLog);多项用 Msg_OK_Fail_Skip_Count。
		yield return ("BuildExtractCoversResultMessage: itemCount>1 -> completed header + trailing errorLog, not error", delegate
		{
			Page log = new Page();
			log.AddLine("X1");
			log.AddLine("X2");
			(string, bool) r = StateFieldInstance.BuildExtractCoversResultMessage(3, 2, 1, 0, 3, log);
			Check.True(!r.Item2, "multi-file -> Item2 false (not error)");
			Check.True(r.Item1.StartsWith(Resources.Msg_ExtractCoversComplete), "multi-file starts with completed header");
			Check.True(r.Item1.EndsWith("\n" + log.ToString()), "multi-file appends newline+errorLog at tail");
			Check.True(r.Item1.Length > log.ToString().Length + 1, "multi-file has header before errorLog (distinguishes else branch)");
		});

		yield return ("BuildExtractCoversResultMessage: single-file success -> bare completed message", delegate
		{
			Page log = new Page();
			log.AddLine("ignored");
			(string, bool) r = StateFieldInstance.BuildExtractCoversResultMessage(1, 1, 0, 0, 1, log);
			Check.Equal(Resources.Msg_ExtractCoversComplete, r.Item1, "success branch -> bare Msg_ExtractCoversComplete (no count, no errorLog)");
			Check.True(!r.Item2, "success branch -> Item2 false");
		});

		yield return ("BuildExtractCoversResultMessage: single-file all-fail -> bare errorLog + error flag", delegate
		{
			Page log = new Page();
			log.AddLine("boom");
			(string, bool) r = StateFieldInstance.BuildExtractCoversResultMessage(1, 0, 1, 0, 1, log);
			Check.Equal(log.ToString(), r.Item1, "else branch -> bare errorLog (no header)");
			Check.True(r.Item2, "else branch -> Item2 true (error)");
		});

		yield return ("BuildExtractCoversResultMessage: itemCount<=1 boundary -> else branch (empty log / zero count)", delegate
		{
			(string, bool) r1 = StateFieldInstance.BuildExtractCoversResultMessage(1, 0, 0, 0, 1, new Page());
			Check.Equal("", r1.Item1, "single empty-log all-fail -> empty message");
			Check.True(r1.Item2, "single empty-log all-fail -> Item2 true");
			(string, bool) r2 = StateFieldInstance.BuildExtractCoversResultMessage(0, 0, 0, 0, 0, new Page());
			Check.True(r2.Item2, "itemCount 0 -> else -> Item2 true");
		});
	}
}
