using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

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
				Form dialog = (Form)ConstructNonPublic(typeof(MusicTag.Schemes.FilenameRelatedBatchDialog));
				try
				{
					Check.True(dialog != null, "constructed");
					TabControl tabs = (TabControl)GetField(dialog, "tabControl");
					Button patternOk = (Button)GetField(dialog, "patternOkButton");
					Button patternCancel = (Button)GetField(dialog, "patternCancelButton");
					Button regexOk = (Button)GetField(dialog, "regexOkButton");
					Button regexCancel = (Button)GetField(dialog, "regexCancelButton");
					dialog.Show();
					Application.DoEvents();
					tabs.SelectedIndex = 0;
					Application.DoEvents();
					Check.True(ReferenceEquals(dialog.AcceptButton, patternOk), "pattern tab Enter button");
					Check.True(ReferenceEquals(dialog.CancelButton, patternCancel), "pattern tab Escape button");
					tabs.SelectedIndex = 1;
					Application.DoEvents();
					Check.True(ReferenceEquals(dialog.AcceptButton, regexOk), "regex tab Enter button");
					Check.True(ReferenceEquals(dialog.CancelButton, regexCancel), "regex tab Escape button");
				}
				finally
				{
					dialog.Dispose();
				}
			});
		});

		yield return ("Smoke: OptionsDialog reflective construction + dispose (zh-CHS ui culture)", delegate
		{
			RunWithAppUiCulture(delegate
			{
				Form dialog = (Form)ConstructNonPublic(typeof(MusicTag.Importers.OptionsDialog));
				try
				{
					Check.True(dialog != null, "constructed");
					Check.True(ReferenceEquals(dialog.AcceptButton, GetField(dialog, "okButton")), "Enter invokes OK");
					CheckTranslatedLyricConnectorState(dialog);
				}
				finally
				{
					dialog.Dispose();
				}
			});
		});
	}

	private static void CheckTranslatedLyricConnectorState(Form dialog)
	{
		CheckBox downloadTranslatedLyrics = (CheckBox)GetField(dialog, "downloadTranslatedLyricsCheckBox");
		RadioButton sameLineFormat = (RadioButton)GetField(dialog, "translatedLyricFormat1RadioButton");
		RadioButton separateLineFormat = (RadioButton)GetField(dialog, "translatedLyricFormat2RadioButton");
		ComboBox separator = (ComboBox)GetField(dialog, "lyricTranslationSeparatorComboBox");

		downloadTranslatedLyrics.Checked = true;
		separateLineFormat.Checked = true;
		Check.True(!separator.Enabled, "separator disabled for separate-line format");

		// 用户复现路径：取消“下载翻译”后重新勾选。通用控件启用逻辑不得覆盖格式限制。
		downloadTranslatedLyrics.Checked = false;
		downloadTranslatedLyrics.Checked = true;
		Check.True(!separator.Enabled, "separator stays disabled after translation toggle in separate-line format");

		sameLineFormat.Checked = true;
		Check.True(separator.Enabled, "separator enabled for same-line format while translation is enabled");

		downloadTranslatedLyrics.Checked = false;
		Check.True(!separator.Enabled, "separator disabled when translated lyric download is disabled");
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

	private static object GetField(object instance, string fieldName)
	{
		FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
		if (field == null)
		{
			throw new Exception("field not found: " + fieldName);
		}
		return field.GetValue(instance);
	}
}
