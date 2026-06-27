namespace MusicTagWinApp.Web;

// 单个联网搜索源在一次合并搜索过程中的状态,供底部状态标识聚合渲染。
// 详见 docs/SEARCH_STATUS_INDICATOR_DESIGN.md §5.1。
internal enum SourceSearchPhase
{
	Pending,
	Searching,
	Completed,
	Error,
	Retrying
}

internal sealed class SourceSearchStatus
{
	public SearchSource Source { get; set; }

	public SourceSearchPhase Phase { get; set; }

	// 业务码(如 QQ 限流 2001)或 HTTP 状态码 / 异常归类(Phase=Error/Retrying 时有值)。
	public string ErrorCode { get; set; }

	// 当前第几次重试(Phase=Retrying 时有值,从 1 起)。
	public int RetryAttempt { get; set; }

	// 总重试次数(Phase=Retrying 时有值)。
	public int RetryTotal { get; set; }

	// 重试倒计时剩余秒数(Phase=Retrying 时有值)。
	public int RetrySecondsLeft { get; set; }
}
