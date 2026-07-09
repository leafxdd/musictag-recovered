using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using MusicTagWinApp.Containers;
using MusicTagWinApp.Instances;
using MusicTagWinApp.Properties;

namespace MusicTag.Schemes;

internal static class Program
{
	private static readonly DateTime startupTime = DateTime.Now;

	private static readonly object exceptionLogLock = new object();

	public static DateTime StartupTime()
	{
		return startupTime;
	}

	[STAThread]
	private static void Main(string[] args)
	{
		// net8 迁移:注册 ANSI 代码页编码提供程序(gb2312/GBK/Big5/Shift_JIS 等在 .NET Core+
		// 非内置)。乱码修复、标签编码列表与 SystemAnsiEncoding 都依赖,必须最先执行。
		Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
		// net8 迁移:Per-Monitor V2 从 app.manifest 移到托管配置(manifest 声明触发 WFAC010,
		// 且手写 Main 使 csproj 的 ApplicationHighDpiMode 属性不生效)。必须先于任何句柄创建。
		Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
		if (TryFindExistingInstance(out IntPtr existingWindowHandle))
		{
			if (existingWindowHandle != IntPtr.Zero)
			{
				ActivateExistingInstance(existingWindowHandle, args);
			}
			return;
		}
		Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
		Application.ThreadException += OnThreadException;
		AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
		Application.EnableVisualStyles();
		Application.SetCompatibleTextRenderingDefault(defaultValue: false);
		Application.Run(new StateFieldInstance(args));
	}

	private static bool TryFindExistingInstance(out IntPtr windowHandle)
	{
		Process currentProcess = Process.GetCurrentProcess();
		string currentExecutablePath = Assembly.GetExecutingAssembly().Location.Replace("/", "\\");
		foreach (Process process in Process.GetProcessesByName(currentProcess.ProcessName))
		{
			if (process.Id == currentProcess.Id || !IsSameExecutable(process, currentExecutablePath))
			{
				continue;
			}

			windowHandle = GetProcessMainWindowHandle(process);
			return true;
		}

		windowHandle = IntPtr.Zero;
		return false;
	}

	private static bool IsSameExecutable(Process process, string currentExecutablePath)
	{
		try
		{
			string processExecutablePath = process.MainModule.FileName;
			return string.Equals(Path.GetFullPath(processExecutablePath), Path.GetFullPath(currentExecutablePath), StringComparison.OrdinalIgnoreCase);
		}
		catch
		{
			return false;
		}
	}

	private static IntPtr GetProcessMainWindowHandle(Process process)
	{
		if (process.MainWindowHandle != IntPtr.Zero)
		{
			return process.MainWindowHandle;
		}

		IntPtr windowHandle = IntPtr.Zero;
		while (true)
		{
			windowHandle = NativeMethods.FindWindowEx(IntPtr.Zero, windowHandle, null, null);
			if (windowHandle == IntPtr.Zero)
			{
				break;
			}

				NativeMethods.GetWindowThreadProcessId(windowHandle, out int processId);
			if (processId != process.Id)
			{
				continue;
			}

			StringBuilder windowTitle = new StringBuilder(256);
				NativeMethods.GetWindowText(windowHandle, windowTitle, windowTitle.Capacity);
			string title = windowTitle.ToString();
			if (title.StartsWith("Music Tag") || title.StartsWith("音乐标签") || title.StartsWith("音樂標籤"))
			{
				return windowHandle;
			}
		}

		return IntPtr.Zero;
	}

	private static void ActivateExistingInstance(IntPtr windowHandle, string[] args)
	{
		NativeMethods.ShowWindowAsync(windowHandle, NativeMethods.ShowWindowRestore);
		NativeMethods.SetForegroundWindow(windowHandle);
		if (args.Any())
		{
			string commandLine = BuildForwardedCommandLine(args);
			NativeMethods.CopyDataStruct copyData = new NativeMethods.CopyDataStruct
			{
				DataIdentifier = IntPtr.Zero,
				Data = commandLine,
				DataLength = Encoding.Unicode.GetByteCount(commandLine + "\0")
			};
			NativeMethods.SendCopyDataMessage(windowHandle, 74, IntPtr.Zero, ref copyData);
		}
	}

	private static string BuildForwardedCommandLine(IEnumerable<string> args)
	{
		StringBuilder commandLine = new StringBuilder();
		foreach (string arg in args)
		{
			commandLine.Append("\"").Append(arg).Append("\" ");
		}
		return commandLine.ToString();
	}

	private static void OnThreadException(object sender, ThreadExceptionEventArgs args)
	{
		HandleUnhandledException(args.Exception, "Application.ThreadException");
	}

	private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs args)
	{
		HandleUnhandledException((Exception)args.ExceptionObject, "AppDomain.CurrentDomain.UnhandledException");
	}

	private static void HandleUnhandledException(Exception exception, string exceptionSource)
	{
		try
		{
			LogUnhandledException(exception, exceptionSource);
			DialogService.ShowErrorMessage(Resources.Msg_ApplicationExceptionWillExit);
		}
		finally
		{
			Environment.Exit(0);
		}
	}

	private static void LogUnhandledException(Exception exception, string exceptionSource)
	{
		lock (exceptionLogLock)
		{
			LogService.WriteExceptionDetails(exception, exceptionSource);
		}
	}
}
