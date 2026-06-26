using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
		RunApplication(args);
	}

	[STAThread]
	private static void RunApplication(string[] args)
	{
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
		// DPI awareness is declared in the embedded application manifest (app.manifest).
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

	private static async void OnThreadException(object sender, ThreadExceptionEventArgs args)
	{
		await HandleUnhandledException(args.Exception, "Application.ThreadException");
	}

	private static async void OnUnhandledException(object sender, UnhandledExceptionEventArgs args)
	{
		await HandleUnhandledException((Exception)args.ExceptionObject, "AppDomain.CurrentDomain.UnhandledException");
	}

	private static async Task HandleUnhandledException(Exception exception, string exceptionSource)
	{
		Task taskLogMessage = Task.Run(() => LogUnhandledException(exception, exceptionSource));
		await Task.Yield();
		DatabaseMapper.ShowErrorMessage(Resources.Msg_ApplicationExceptionWillExit);
		await taskLogMessage;
		Environment.Exit(0);
	}

	private static void LogUnhandledException(Exception exception, string exceptionSource)
	{
		lock (exceptionLogLock)
		{
			DatabaseMapper.WriteExceptionDetails(exception, exceptionSource);
		}
	}
}
