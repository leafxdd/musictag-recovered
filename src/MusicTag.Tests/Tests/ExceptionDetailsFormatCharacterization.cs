using System;
using System.Collections.Generic;
using MusicTagWinApp.Instances;

namespace MusicTag.Tests;

// LogService.FormatExceptionDetails characterization。该纯格式化核由 WriteExceptionDetails 提取(pure-core
// extraction:方法体逐字节移入,原方法改调 WriteExceptionLog(FormatExceptionDetails(...)))。锁定:
//   - "\r\n" 前导 + "Type: {context, }{type}\r\nMessage: {chain}\r\nStackTrace: {stack}\r\n" 布局
//   - InnerException 优先(Type.Name / StackTrace 取内层);Message 走 GetMessageChain(Aggregate 用 ";" 连接)
//   - 全空导航安全(null 异常 / 未抛异常的 null StackTrace)
// 用未抛出的异常,StackTrace 恒为 null,输出稳定。
internal static class ExceptionDetailsFormatCharacterization
{
	public static IEnumerable<(string, Action)> All()
	{
		yield return ("FormatExceptionDetails: plain exception, no context", delegate
		{
			Check.Equal("\r\nType: Exception\r\nMessage: boom\r\nStackTrace: \r\n", LogService.FormatExceptionDetails(new Exception("boom"), null), "plain");
		});

		yield return ("FormatExceptionDetails: plain exception, with context prefix", delegate
		{
			Check.Equal("\r\nType: ctx, Exception\r\nMessage: boom\r\nStackTrace: \r\n", LogService.FormatExceptionDetails(new Exception("boom"), "ctx"), "context");
		});

		yield return ("FormatExceptionDetails: inner exception takes precedence (type + message)", delegate
		{
			Exception exception = new Exception("outer", new InvalidOperationException("inner"));
			Check.Equal("\r\nType: InvalidOperationException\r\nMessage: inner\r\nStackTrace: \r\n", LogService.FormatExceptionDetails(exception, null), "inner precedence");
		});

		yield return ("FormatExceptionDetails: null exception, no context -> empty fields", delegate
		{
			Check.Equal("\r\nType: \r\nMessage: \r\nStackTrace: \r\n", LogService.FormatExceptionDetails(null, null), "null exception");
		});

		yield return ("FormatExceptionDetails: null exception, with context", delegate
		{
			Check.Equal("\r\nType: c, \r\nMessage: \r\nStackTrace: \r\n", LogService.FormatExceptionDetails(null, "c"), "null + context");
		});

		yield return ("FormatExceptionDetails: AggregateException -> first inner type + \";\"-joined messages", delegate
		{
			AggregateException exception = new AggregateException(new Exception("a"), new Exception("b"));
			Check.Equal("\r\nType: Exception\r\nMessage: a;b\r\nStackTrace: \r\n", LogService.FormatExceptionDetails(exception, null), "aggregate");
		});
	}
}
