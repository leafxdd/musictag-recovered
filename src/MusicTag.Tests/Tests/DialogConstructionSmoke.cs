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

		yield return ("Smoke: CombinedTagSearchDialog track ID toolbar layout + dispose (zh-CHS ui culture)", delegate
		{
			RunWithAppUiCulture(delegate
			{
				Form dialog = (Form)ConstructNonPublic(typeof(MusicTag.Mocks.CombinedTagSearchDialog));
				try
				{
					TableLayoutPanel lookupPanel = (TableLayoutPanel)GetField(dialog, "trackIdLookupPanel");
					ComboBox source = (ComboBox)GetField(dialog, "trackIdSourceComboBox");
					TextBox idInput = (TextBox)GetField(dialog, "trackIdTextBox");
					Button lookup = (Button)GetField(dialog, "trackIdLookupButton");
					Check.Equal(4, source.Items.Count, "four ID lookup sources");
					Check.True(lookupPanel.Height > 0, "lookup panel has stable height");
					Check.True(idInput.Width > 100, "ID input remains usable");
					Check.True(lookup.Width > 0 && lookup.Right <= lookupPanel.ClientSize.Width, "lookup button fits panel");
					InvokeMethod(dialog, "UpdateTrackIdLookupIconForDpi", 144);
					Check.Equal(27, lookup.Image.Width, "150 percent lookup icon width");
					Check.Equal(27, lookup.Image.Height, "150 percent lookup icon height");
					InvokeMethod(dialog, "UpdateTrackIdLookupIconForDpi", 96);
					Check.Equal(18, lookup.Image.Width, "100 percent lookup icon width after downscale");
					Check.Equal(18, lookup.Image.Height, "100 percent lookup icon height after downscale");
					Check.True(ReferenceEquals(dialog.AcceptButton, GetField(dialog, "okSplitButton")), "dialog Enter remains OK outside ID input");
				}
				finally
				{
					dialog.Dispose();
				}
			});
		});

		yield return ("TagSearchCandidatePanel DPI metrics are absolute and round-trip safely", delegate
		{
			Control panel = (Control)ConstructNonPublic(typeof(MusicTag.Consumers.TagSearchCandidatePanel));
			try
			{
				Label sourceLabel = (Label)GetField(panel, "sourceLabel");
				PictureBox lyricPictureBox = (PictureBox)GetField(panel, "lyricPictureBox");
				InvokeMethod(panel, "ApplyDpiMetrics", 144);
				Check.Equal(30, sourceLabel.Height, "150 percent source label height");
				Check.Equal(24, lyricPictureBox.Height, "150 percent lyric icon height");
				Check.Equal(3, lyricPictureBox.Margin.Top, "150 percent lyric margin");
				InvokeMethod(panel, "ApplyDpiMetrics", 96);
				Check.Equal(20, sourceLabel.Height, "100 percent source label height");
				Check.Equal(16, lyricPictureBox.Height, "100 percent lyric icon height");
				Check.Equal(2, lyricPictureBox.Margin.Top, "100 percent lyric margin");
				InvokeMethod(panel, "ApplyDpiMetrics", 144);
				Check.Equal(30, sourceLabel.Height, "round-trip source label does not compound");
			}
			finally
			{
				panel.Dispose();
			}
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

	private static object InvokeMethod(object instance, string methodName, params object[] arguments)
	{
		MethodInfo method = instance.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
		if (method == null)
		{
			throw new Exception("method not found: " + methodName);
		}
		return method.Invoke(instance, arguments);
	}
}
