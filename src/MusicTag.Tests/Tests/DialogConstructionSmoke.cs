using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace MusicTag.Tests;

// 原 Verify-Build.ps1 的两个反射构造冒烟(net481 时代经外部 Windows PowerShell LoadFrom 反射),
// net8 迁移后外部宿主(netfx CLR 的 powershell.exe / x64 的 pwsh)无法加载 x86 net8 程序集,
// 并入特征套件:本进程即 x86 net8 WinForms + [STAThread],构造走完整 designer/资源路径。
internal static class DialogConstructionSmoke
{
	public static IEnumerable<(string, Action)> All()
	{
		yield return ("Smoke: FilenameRelatedBatchDialog reflective construction + dispose (zh-CHS ui culture)", delegate
		{
			RunWithAppUiCulture(delegate
			{
				object dialog = ConstructNonPublic(typeof(MusicTag.Schemes.FilenameRelatedBatchDialog));
				Check.True(dialog != null, "constructed");
				((IDisposable)dialog).Dispose();
			});
		});

		yield return ("Smoke: OptionsDialog reflective construction + dispose (zh-CHS ui culture)", delegate
		{
			RunWithAppUiCulture(delegate
			{
				object dialog = ConstructNonPublic(typeof(MusicTag.Importers.OptionsDialog));
				Check.True(dialog != null, "constructed");
				((IDisposable)dialog).Dispose();
			});
		});
	}

	// app 启动即显式设 CurrentUICulture ∈ {zh-CHS, zh-CHT, en}(StateFieldInstance 语言初始化),
	// 部分对话框资源(如 MusicTag.Schemes.EventRulesSchema)只存在于三个卫星程序集,neutral
	// 程序集没有兜底副本。netfx 下测试进程的 zh-CN 经 legacy 父链 zh-CN→zh-CHS 恰好命中卫星;
	// net8/ICU 的父链是 zh-CN→zh-Hans→zh→neutral,不再路过 zh-CHS → MissingManifestResourceException。
	// 冒烟按 app 真实运行环境显式设 zh-CHS(用完复位),不依赖宿主系统 culture。
	private static void RunWithAppUiCulture(Action action)
	{
		CultureInfo saved = Thread.CurrentThread.CurrentUICulture;
		Thread.CurrentThread.CurrentUICulture = CultureInfo.CreateSpecificCulture("zh-CHS");
		try
		{
			action();
		}
		finally
		{
			Thread.CurrentThread.CurrentUICulture = saved;
		}
	}

	private static object ConstructNonPublic(Type type)
	{
		try
		{
			return Activator.CreateInstance(type, nonPublic: true);
		}
		catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException != null)
		{
			// 冒烟诊断:剥掉反射包装,直接暴露构造器内的真实异常(类型+消息+栈)。
			throw new Exception(type.Name + " ctor threw: " + ex.InnerException.GetType().Name + ": " + ex.InnerException.Message + Environment.NewLine + ex.InnerException.StackTrace, ex.InnerException);
		}
	}
}
