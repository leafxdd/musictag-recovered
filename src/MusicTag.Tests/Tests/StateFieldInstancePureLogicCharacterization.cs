using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using MusicTagWinApp;
using MusicTagWinApp.Instances;

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
	}
}
