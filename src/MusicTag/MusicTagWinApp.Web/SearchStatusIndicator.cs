using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Forms;

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
		retryCountdownTimer.Stop();
		Refresh();
	}

	// 搜索整体结束:任何仍处于"搜索中/重试中"的源都视为已完成,避免边角路径残留
	// "正在搜索";错误状态保留(常驻到关窗或下次搜索,D3)。
	public void End()
	{
		searchInProgress = false;
		retryCountdownTimer.Stop();
		foreach (SourceSearchStatus status in statuses.Values)
		{
			if (status.Phase != SourceSearchPhase.Error)
			{
				status.Phase = SourceSearchPhase.Completed;
			}
		}
		Refresh();
	}

	// 进入弹窗即重置:清空残留且不视为"已搜索过"(缓存命中等不联网路径用)。
	public void Reset()
	{
		statuses.Clear();
		searchInProgress = false;
		searchHasRun = false;
		retryCountdownTimer.Stop();
		Refresh();
	}

	public void StopCountdown()
	{
		retryCountdownTimer.Stop();
	}

	// 后台搜索线程经 Progress<T> 编组到 UI 线程后回调。
	public void Report(SourceSearchStatus status)
	{
		if (label.IsDisposed || status == null)
		{
			return;
		}
		statuses[status.Source] = status;
		if (status.Phase == SourceSearchPhase.Retrying && !retryCountdownTimer.Enabled)
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
		string errorLine = BuildErrorOrRetryLine();
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
			text = "未找到匹配结果";
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
		List<string> sourceNames = new List<string>();
		foreach (SourceSearchStatus status in GetStatusesInDisplayOrder())
		{
			if (status.Phase == SourceSearchPhase.Searching || status.Phase == SourceSearchPhase.Pending)
			{
				sourceNames.Add(GetSourceDisplayName(status.Source));
			}
		}
		if (sourceNames.Count == 0)
		{
			return null;
		}
		return "正在搜索: " + string.Join("/", sourceNames);
	}

	// 错误 / 重试行:重试中优先;多个普通错误时合并源名(最多一行)。
	private string BuildErrorOrRetryLine()
	{
		foreach (SourceSearchStatus status in GetStatusesInDisplayOrder())
		{
			if (status.Phase == SourceSearchPhase.Retrying)
			{
				return GetSourceDisplayName(status.Source) + " API错误(" + FormatErrorCode(status.ErrorCode) + "), " + Math.Max(0, status.RetrySecondsLeft) + " 秒后重试 (" + status.RetryAttempt + "/" + status.RetryTotal + ")";
			}
		}
		List<SourceSearchStatus> erroredSources = new List<SourceSearchStatus>();
		foreach (SourceSearchStatus status in GetStatusesInDisplayOrder())
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
			return GetSourceDisplayName(erroredSources[0].Source) + " API错误(" + FormatErrorCode(erroredSources[0].ErrorCode) + ")";
		}
		List<string> erroredNames = new List<string>();
		foreach (SourceSearchStatus status in erroredSources)
		{
			erroredNames.Add(GetSourceDisplayName(status.Source));
		}
		return string.Join("/", erroredNames) + " API错误";
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

	private static string FormatErrorCode(string errorCode)
	{
		return string.IsNullOrEmpty(errorCode) ? "未知" : errorCode;
	}

	public static string GetSourceDisplayName(SearchSource source)
	{
		switch (source)
		{
			case SearchSource.Music163:
				return "网易云";
			case SearchSource.QQ:
				return "QQ";
			case SearchSource.Kugou:
				return "酷狗";
			case SearchSource.Kuwo:
				return "酷我";
			default:
				return source.ToString();
		}
	}
}
