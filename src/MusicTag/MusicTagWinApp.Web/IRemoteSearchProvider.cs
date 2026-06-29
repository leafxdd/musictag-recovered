using System;
using MusicTag.Serialization;

namespace MusicTagWinApp.Web;

// 所有联网搜索 provider 的公共能力契约:可释放 + 暴露最近一次传输结果(供 dialog 渲染状态指示器)。
// RemoteTagProviderBase 已 public 提供 LastTransportResult 并实现 IDisposable,故各 provider 自动满足。
internal interface IRemoteSearchProvider : IDisposable
{
	HttpResult LastTransportResult { get; }
}
