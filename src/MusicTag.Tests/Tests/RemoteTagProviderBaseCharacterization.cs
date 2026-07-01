using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MusicTag.Serialization;
using Newtonsoft.Json.Linq;

namespace MusicTag.Tests;

// RemoteTagProviderBase(MusicTag.Serialization)传输层错误 taxonomy 的 characterization。
// 现有 provider 测试只覆盖 SetTransportError(ParseFailed 回填),从不触发传输层的 Timeout/Network/None
// 与非-2xx HttpStatus 归类 —— 而这套 taxonomy 直接驱动搜索状态指示器(用户可见)。本批:
//   HttpResult.FromHttpStatus(已 public static):非-2xx 响应归类工厂,code -> {HttpStatus,Error=HttpStatus,ErrorCode}。
//   HttpResult.IsSuccess:Error==None <=> 成功。
//   ClassifyException(private 提取为 internal static,收 cancellationRequested bool;实例方法委托,读取消时机不变):
//     取消优先 -> None;AggregateException 经 GetBaseException 解包后 TaskCanceled/Timeout -> Timeout;否则 -> Network。
//   GetFirstField(protected static -> protected internal static):按序返回首个非 JSON-null 字段;非 Object -> null。
internal static class RemoteTagProviderBaseCharacterization
{
	public static IEnumerable<(string, Action)> All()
	{
		// ===== HttpResult.FromHttpStatus + IsSuccess =====

		yield return ("FromHttpStatus: 404 -> HttpStatus/HttpStatus/\"404\", not success", delegate
		{
			HttpResult result = HttpResult.FromHttpStatus(404);
			Check.Equal(404, result.HttpStatus ?? -1, "HttpStatus");
			Check.True(result.Error == RemoteErrorKind.HttpStatus, "Error kind");
			Check.Equal("404", result.ErrorCode, "ErrorCode");
			Check.True(!result.IsSuccess, "not success");
		});

		yield return ("FromHttpStatus: 503 -> ErrorCode \"503\" (ToString)", delegate
		{
			Check.Equal("503", HttpResult.FromHttpStatus(503).ErrorCode, "503 code");
		});

		yield return ("HttpResult.IsSuccess: None -> true", delegate
		{
			Check.True(new HttpResult { Error = RemoteErrorKind.None }.IsSuccess, "None is success");
		});

		yield return ("HttpResult.IsSuccess: Network -> false", delegate
		{
			Check.True(!new HttpResult { Error = RemoteErrorKind.Network }.IsSuccess, "Network not success");
		});

		// ===== ClassifyException:取消优先 / 解包 / Timeout / Network =====

		yield return ("ClassifyException: TimeoutException -> Timeout/\"timeout\"", delegate
		{
			HttpResult r = RemoteTagProviderBase.ClassifyException(new TimeoutException(), false);
			Check.True(r.Error == RemoteErrorKind.Timeout, "Timeout kind");
			Check.Equal("timeout", r.ErrorCode, "timeout code");
		});

		yield return ("ClassifyException: TaskCanceledException (not cancelled) -> Timeout", delegate
		{
			Check.True(RemoteTagProviderBase.ClassifyException(new TaskCanceledException(), false).Error == RemoteErrorKind.Timeout, "TaskCanceled -> Timeout");
		});

		yield return ("ClassifyException: AggregateException(TaskCanceled) -> Timeout (GetBaseException unwrap)", delegate
		{
			Check.True(RemoteTagProviderBase.ClassifyException(new AggregateException(new TaskCanceledException()), false).Error == RemoteErrorKind.Timeout, "unwrap to Timeout");
		});

		yield return ("ClassifyException: IOException -> Network/\"network\"", delegate
		{
			HttpResult r = RemoteTagProviderBase.ClassifyException(new IOException(), false);
			Check.True(r.Error == RemoteErrorKind.Network, "Network kind");
			Check.Equal("network", r.ErrorCode, "network code");
		});

		yield return ("ClassifyException: AggregateException(IOException) -> Network (unwrap, non-timeout)", delegate
		{
			Check.True(RemoteTagProviderBase.ClassifyException(new AggregateException(new IOException()), false).Error == RemoteErrorKind.Network, "unwrap to Network");
		});

		yield return ("ClassifyException: cancellation requested -> None (priority, no ErrorCode)", delegate
		{
			HttpResult r = RemoteTagProviderBase.ClassifyException(new TimeoutException(), true);
			Check.True(r.Error == RemoteErrorKind.None, "None on cancel");
			Check.Null(r.ErrorCode, "no ErrorCode on cancel");
		});

		// ===== GetFirstField:别名回退 + JSON-null 跳过 + 非 Object 守卫 =====

		yield return ("GetFirstField: second alias hit (al missing, album present)", delegate
		{
			JObject obj = JObject.Parse("{\"album\":\"myalbum\"}");
			JToken field = RemoteTagProviderBase.GetFirstField(obj, "al", "album");
			Check.NotNull(field, "album found");
			Check.Equal("myalbum", field.ToString(), "album value");
		});

		yield return ("GetFirstField: skips JSON-null first alias, returns second", delegate
		{
			JObject obj = JObject.Parse("{\"al\":null,\"album\":\"myalbum\"}");
			JToken field = RemoteTagProviderBase.GetFirstField(obj, "al", "album");
			Check.NotNull(field, "album found past null al");
			Check.Equal("myalbum", field.ToString(), "album value");
		});

		yield return ("GetFirstField: non-Object token -> null", delegate
		{
			JToken scalar = new JValue("scalar");
			Check.Null(RemoteTagProviderBase.GetFirstField(scalar, "x"), "non-object guard");
		});

		yield return ("GetFirstField: all aliases missing -> null", delegate
		{
			JObject obj = JObject.Parse("{\"x\":1}");
			Check.Null(RemoteTagProviderBase.GetFirstField(obj, "a", "b"), "all missing");
		});
	}
}
