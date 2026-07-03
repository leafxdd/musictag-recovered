using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MusicTagWinApp.Containers;
using MusicTagWinApp.Properties;

namespace MusicTagWinApp.Instances;

// 消息框 / 确认对话框 / 资源管理器定位 / 设置保存等 UI 辅助。原 DatabaseMapper（误名神类）拆分而来,至此该类清空并删除（详见 docs/SIMPLIFICATION_PLAN.md Phase 2）。
internal static class DialogService
{
	public static void ShowInformationMessage(string message)
	{
			MessageBox.Show(message, Resources.Information, MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
	}

	public static void ShowErrorMessage(string message)
	{
		MessageBox.Show(message, Resources.PolicyTokenExporter, MessageBoxButtons.OK, MessageBoxIcon.Hand);
	}

	public static bool TrySaveApplicationSettings(bool showErrorMessage = true)
	{
		try
		{
			Settings.Default.Save();
			return true;
		}
		catch (Exception exception)
		{
			if (showErrorMessage)
			{
				ShowErrorMessage(exception.GetMessageChain());
			}
			return false;
		}
	}

	public static bool ConfirmYesNo(string message)
	{
		return MessageBox.Show(message, Resources.Confirmation, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
	}

	public static DialogResult ConfirmYesNoCancel(string message)
	{
		return MessageBox.Show(message, Resources.Confirmation, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
	}

	public static void ShowInExplorer(string path)
	{
		if (!File.Exists(path) && !Directory.Exists(path))
		{
			return;
		}
		try
		{
			if (Directory.Exists(path))
			{
				Process.Start("explorer.exe", "/select,\"" + path + "\"");
				return;
			}
			IntPtr intPtr = NativeMethods.CreateItemIdListFromPath(path);
			if (intPtr == IntPtr.Zero)
			{
				return;
			}
			try
			{
				Marshal.ThrowExceptionForHR(NativeMethods.OpenFolderAndSelectItems(intPtr, 0u, IntPtr.Zero, 0u));
			}
			catch (Exception)
			{
				Process.Start("explorer.exe", "/select,\"" + path + "\"");
			}
			finally
			{
				NativeMethods.FreeItemIdList(intPtr);
			}
		}
		catch (Exception ex2)
		{
			ShowErrorMessage("Open folder and select item fail: " + ex2.Message);
		}
	}

	public static void SetTextBoxCueBanner(Control control, string text)
	{
		NativeMethods.SendStringMessage(control.Handle, 5377, IntPtr.Zero, text);
	}

	public static void FillAndCenterButtons(Control listControl, Control mainPanel, Control buttonPanel)
	{
		listControl.Width = mainPanel.Width;
		listControl.Height = mainPanel.Height - buttonPanel.Height - buttonPanel.Margin.Top - buttonPanel.Margin.Bottom;
		int horizontalMargin = (mainPanel.Width - buttonPanel.Width) / 2;
		buttonPanel.Margin = new Padding(horizontalMargin, buttonPanel.Margin.Top, horizontalMargin, buttonPanel.Margin.Bottom);
	}
}
