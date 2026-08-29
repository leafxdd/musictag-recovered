using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using MusicTagWinApp.Properties;

namespace MusicTagWinApp.Web;

// 联网搜索状态标识的共享渲染器:聚合各源(SourceSearchStatus)状态,按单/双行规则
// 渲染到一个 Label,并管理 QQ 限流重试的逐秒倒计时。供合并标签 / 封面 / 歌词三个
// 搜索弹窗复用,确保行为一致。详见 docs/SEARCH_STATUS_INDICATOR_DESIGN.md。
//
// 线程约定:所有公共方法仅在 UI 线程调用。后台搜索线程的上报须先经 Progress<T>
// 编组回 UI 线程,再调用 Report。
internal sealed class SearchStatusIndicator
{
	private static readonly SearchSource[] DisplayOrder = new SearchSource[4]
	{
		SearchSource.Music163,
		SearchSource.QQ,
		SearchSource.Kugou,
		SearchSource.Kuwo
	};

	private readonly Label label;

	private readonly Func<bool> hasResultsProvider;

	private readonly System.Windows.Forms.Timer retryCountdownTimer;

	private readonly Dictionary<SearchSource, SourceSearchStatus> statuses = new Dictionary<SearchSource, SourceSearchStatus>();

	private bool searchInProgress;

	private bool searchHasRun;

	private string unexpectedErrorMessage;

	public SearchStatusIndicator(Label label, Func<bool> hasResultsProvider, IContainer components)
	{
		this.label = label;
		this.hasResultsProvider = hasResultsProvider;
		retryCountdownTimer = (components != null) ? new System.Windows.Forms.Timer(components) : new System.Windows.Forms.Timer();
		retryCountdownTimer.Interval = 1000;
		retryCountdownTimer.Tick += OnRetryCountdownTick;
	}

	// 开始一轮新搜索:清空上一轮残留,标记搜索中。
	public void Begin()
	{
		statuses.Clear();
		searchInProgress = true;
		searchHasRun = true;
		unexpectedErrorMessage = null;
		retryCountdownTimer.Stop();
		Refresh();
	}

	// 开始一轮新搜索并建立状态上报通道(仅 UI 线程调用):在 UI 线程构造 Progress<T>
	// 以捕获当前 SynchronizationContext,调用 Begin() 重置本轮状态,返回经 Progress
	// 编组回 UI 线程的上报委托。三个搜索弹窗共用,替代各自的内联三行。
	public Action<SourceSearchStatus> BeginReporting()
	{
		Progress<SourceSearchStatus> statusProgress = new Progress<SourceSearchStatus>(Report);
		Action<SourceSearchStatus> reporter = (SourceSearchStatus status) => ((IProgress<SourceSearchStatus>)statusProgress).Report(status);
		Begin();
		return reporter;
	}

	// 搜索整体结束:任何仍处于"搜索中/重试中"的源都视为已完成,避免边角路径残留
	// "正在搜索";错误状态保留(常驻到关窗或下次搜索,D3)。
	public void End()
	{
		searchInProgress = false;
		bool keepCooldownTimer = false;
		foreach (SourceSearchStatus status in statuses.Values)
		{
			if (status.Phase == SourceSearchPhase.CoolingDown)
			{
				keepCooldownTimer = true;
			}
			else if (status.Phase != SourceSearchPhase.Error && status.Phase != SourceSearchPhase.CredentialsExpired)
			{
				status.Phase = SourceSearchPhase.Completed;
			}
		}
		if (!keepCooldownTimer)
		{
			retryCountdownTimer.Stop();
		}
		Refresh();
	}

	// 进入弹窗即重置:清空残留且不视为"已搜索过"(缓存命中等不联网路径用)。
	public void Reset()
	{
		statuses.Clear();
		searchInProgress = false;
		searchHasRun = false;
		unexpectedErrorMessage = null;
		retryCountdownTimer.Stop();
		Refresh();
	}

	public void StopCountdown()
	{
		retryCountdownTimer.Stop();
	}

	public void ReportUnexpectedError()
	{
		if (label.IsDisposed)
		{
			return;
		}
		unexpectedErrorMessage = UiText.Get("Search failed", "搜索失败", "搜尋失敗");
		Refresh();
	}

	// 后台搜索线程经 Progress<T> 编组到 UI 线程后回调。
	public void Report(SourceSearchStatus status)
	{
		if (label.IsDisposed || status == null)
		{
			return;
		}
		statuses[status.Source] = status;
		if ((status.Phase == SourceSearchPhase.Retrying || status.Phase == SourceSearchPhase.CoolingDown) && !retryCountdownTimer.Enabled)
		{
			retryCountdownTimer.Start();
		}
		Refresh();
	}

	private void OnRetryCountdownTick(object sender, EventArgs e)
	{
		if (label.IsDisposed)
		{
			retryCountdownTimer.Stop();
			return;
		}
		bool anyRetrying = false;
		foreach (SourceSearchStatus status in statuses.Values)
		{
			if (status.Phase == SourceSearchPhase.Retrying)
			{
				anyRetrying = true;
				if (status.RetrySecondsLeft > 0)
				{
					status.RetrySecondsLeft--;
				}
			}
			else if (status.Phase == SourceSearchPhase.CoolingDown)
			{
				if (status.CooldownSecondsLeft > 0)
				{
					status.CooldownSecondsLeft--;
				}
				if (status.CooldownSecondsLeft > 0)
				{
					anyRetrying = true;
				}
				else
				{
					status.Phase = SourceSearchPhase.Error;
				}
			}
		}
		if (!anyRetrying)
		{
			retryCountdownTimer.Stop();
		}
		Refresh();
	}

	private void Refresh()
	{
		if (label.IsDisposed)
		{
			return;
		}
		string searchingLine = BuildSearchingLine();
		string errorLine = unexpectedErrorMessage ?? BuildErrorOrRetryLine();
		string text;
		if (searchingLine != null && errorLine != null)
		{
			text = searchingLine + "\n" + errorLine;
		}
		else if (searchingLine != null)
		{
			text = searchingLine;
		}
		else if (errorLine != null)
		{
			text = errorLine;
		}
		else if (!searchInProgress && searchHasRun && !hasResultsProvider())
		{
			text = UiText.Get("No matching results", "未找到匹配结果", "未找到符合結果");
		}
		else
		{
			text = "";
		}
		label.Text = text;
		label.Visible = text.Length > 0;
	}

	// 仍在搜索(尚未完成)的源:出错 / 重试中的源不出现在此行。
	private string BuildSearchingLine()
	{
		return BuildSearchingLine(GetStatusesInDisplayOrder());
	}

	// 从已按显示序枚举的源状态构造"正在搜索"行(纯逻辑,提取供 characterization;实例方法委托)。
	internal static string BuildSearchingLine(IEnumerable<SourceSearchStatus> statusesInDisplayOrder)
	{
		return BuildSearchingLine(statusesInDisplayOrder, CultureInfo.CurrentUICulture);
	}

	internal static string BuildSearchingLine(IEnumerable<SourceSearchStatus> statusesInDisplayOrder, CultureInfo culture)
	{
		List<string> sourceNames = new List<string>();
		foreach (SourceSearchStatus status in statusesInDisplayOrder)
		{
			if (status.Phase == SourceSearchPhase.Searching || status.Phase == SourceSearchPhase.Pending)
			{
				sourceNames.Add(GetSourceDisplayName(status.Source, culture));
			}
		}
		if (sourceNames.Count == 0)
		{
			return null;
		}
		return UiText.Get("Searching: ", "正在搜索: ", "正在搜尋: ", culture) + string.Join("/", sourceNames);
	}

	// 错误 / 重试行:重试中优先;多个普通错误时合并源名(最多一行)。
	private string BuildErrorOrRetryLine()
	{
		return BuildErrorOrRetryLine(new List<SourceSearchStatus>(GetStatusesInDisplayOrder()));
	}

	// 从已按显示序【物化】的源状态构造错误/重试行(纯逻辑,提取供 characterization;实例方法委托)。
	// 内部二次遍历(先找 Retrying 再收集 Error),故须传已物化序列(非惰性迭代器),
	// 与原实例方法两次独立枚举 statuses 字典的结果等价。
	internal static string BuildErrorOrRetryLine(IEnumerable<SourceSearchStatus> statusesInDisplayOrder)
	{
		return BuildErrorOrRetryLine(statusesInDisplayOrder, CultureInfo.CurrentUICulture);
	}

	internal static string BuildErrorOrRetryLine(IEnumerable<SourceSearchStatus> statusesInDisplayOrder, CultureInfo culture)
	{
		foreach (SourceSearchStatus status in statusesInDisplayOrder)
		{
			if (status.Phase == SourceSearchPhase.Retrying)
			{
				string sourceName = GetSourceDisplayName(status.Source, culture);
				string errorCode = FormatErrorCode(status.ErrorCode, culture);
				return UiText.Get(
					sourceName + " API error (" + errorCode + "), retrying in " + Math.Max(0, status.RetrySecondsLeft) + " seconds (" + status.RetryAttempt + "/" + status.RetryTotal + ")",
					sourceName + " API错误(" + errorCode + "), " + Math.Max(0, status.RetrySecondsLeft) + " 秒后重试 (" + status.RetryAttempt + "/" + status.RetryTotal + ")",
					sourceName + " API錯誤(" + errorCode + "), " + Math.Max(0, status.RetrySecondsLeft) + " 秒後重試 (" + status.RetryAttempt + "/" + status.RetryTotal + ")",
					culture);
			}
		}
		foreach (SourceSearchStatus status in statusesInDisplayOrder)
		{
			if (status.Phase == SourceSearchPhase.CoolingDown)
			{
				string sourceName = GetSourceDisplayName(status.Source, culture);
				string errorCode = FormatErrorCode(status.ErrorCode, culture);
				return UiText.Get(
					sourceName + " temporarily limited (" + errorCode + "), retry after " + Math.Max(0, status.CooldownSecondsLeft) + " seconds",
					sourceName + " 暂时受限(" + errorCode + ")，" + Math.Max(0, status.CooldownSecondsLeft) + " 秒后可重试",
					sourceName + " 暫時受限(" + errorCode + ")，" + Math.Max(0, status.CooldownSecondsLeft) + " 秒後可重試",
					culture);
			}
		}
		foreach (SourceSearchStatus status in statusesInDisplayOrder)
		{
			if (status.Phase == SourceSearchPhase.CredentialsExpired)
			{
				string sourceName = GetSourceDisplayName(status.Source, culture);
				return UiText.Get(
					sourceName + " Music Cookie expired, please update it",
					sourceName + " 音乐 Cookie 已过期，请重新复制",
					sourceName + " 音樂 Cookie 已過期，請重新複製",
					culture);
			}
		}
		List<SourceSearchStatus> erroredSources = new List<SourceSearchStatus>();
		foreach (SourceSearchStatus status in statusesInDisplayOrder)
		{
			if (status.Phase == SourceSearchPhase.Error)
			{
				erroredSources.Add(status);
			}
		}
		if (erroredSources.Count == 0)
		{
			return null;
		}
		if (erroredSources.Count == 1)
		{
			string sourceName = GetSourceDisplayName(erroredSources[0].Source, culture);
			string errorCode = FormatErrorCode(erroredSources[0].ErrorCode, culture);
			return UiText.Get(sourceName + " API error (" + errorCode + ")", sourceName + " API错误(" + errorCode + ")", sourceName + " API錯誤(" + errorCode + ")", culture);
		}
		List<string> erroredNames = new List<string>();
		foreach (SourceSearchStatus status in erroredSources)
		{
			erroredNames.Add(GetSourceDisplayName(status.Source, culture));
		}
		string joinedNames = string.Join("/", erroredNames);
		return UiText.Get(joinedNames + " API error", joinedNames + " API错误", joinedNames + " API錯誤", culture);
	}

	// 按固定显示顺序(网易云/QQ/酷狗/酷我)枚举已上报状态,保证渲染稳定。
	private IEnumerable<SourceSearchStatus> GetStatusesInDisplayOrder()
	{
		foreach (SearchSource source in DisplayOrder)
		{
			if (statuses.TryGetValue(source, out SourceSearchStatus status))
			{
				yield return status;
			}
		}
	}

	private static string FormatErrorCode(string errorCode, CultureInfo culture)
	{
		return string.IsNullOrEmpty(errorCode) ? UiText.Get("unknown", "未知", "未知", culture) : errorCode;
	}

	// footerPanel 为普通 Panel,子控件绝对定位:按钮恒定居中(与状态标签显隐无关),
	// 状态标签置于按钮右侧、垂直中线与按钮对齐。详见 docs/SEARCH_STATUS_INDICATOR_DESIGN.md。
	public static void LayoutFooterStatus(Control footerPanel, Control buttonPanel, Control statusLabel)
	{
		int buttonLeft = Math.Max(0, (footerPanel.Width - buttonPanel.Width) / 2);
		int buttonTop = Math.Max(0, (footerPanel.Height - buttonPanel.Height) / 2);
		buttonPanel.Location = new Point(buttonLeft, buttonTop);
		int statusGap = 12;
		int statusLeft = buttonPanel.Location.X + buttonPanel.Width + statusGap;
		int statusTop = buttonPanel.Location.Y + buttonPanel.Height / 2 - statusLabel.Height / 2;
		statusLabel.Location = new Point(statusLeft, statusTop);
		statusLabel.Width = Math.Max(0, footerPanel.Width - statusLeft - 8);
	}

	public static string GetSourceDisplayName(SearchSource source)
	{
		return GetSourceDisplayName(source, CultureInfo.CurrentUICulture);
	}

	internal static string GetSourceDisplayName(SearchSource source, CultureInfo culture)
	{
		switch (source)
		{
			case SearchSource.Music163:
				return UiText.Get("NetEase", "网易云", "網易雲", culture);
			case SearchSource.QQ:
				return "QQ";
			case SearchSource.Kugou:
				return UiText.Get("Kugou", "酷狗", "酷狗", culture);
			case SearchSource.Kuwo:
				return UiText.Get("Kuwo", "酷我", "酷我", culture);
			default:
				return source.ToString();
		}
	}
}
