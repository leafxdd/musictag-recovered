using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Forms;

namespace MusicTag.Tests;

// OptionsDialog 初始页显隐:全部页面控件同住 sourceOrderPanel(FlowLayoutPanel),靠
// TreeView 首节点自动选中触发 AfterSelect 的 Hide/Show 切换。net8 迁移期实测过"打开设置
// 全部选项一股脑显示"(高 DPI + 默认字体变化连锁),此用例锁住句柄创建 + 消息泵后的
// 初始状态:TagSources 页四件套可见、其余六页隐藏。
internal static class OptionsDialogInitialVisibilityCharacterization
{
	private static readonly string[] VisiblePageFields =
	{
		"coverSourceOrderControl", "lyricSourceOrderControl", "tagSourceOrderControl", "sourceLimitPanel"
	};

	private static readonly string[] HiddenPageFields =
	{
		"webSearchLimitGroupBox", "translatedLyricGroupBox", "lyricCleanupOptionsPanel",
		"searchAndTagOptionsPanel", "saveAndNotificationOptionsPanel", "networkOptionsGroupBox"
	};

	public static IEnumerable<(string, Action)> All()
	{
		yield return ("OptionsDialog: initial visibility = TagSources page only (after Show + message pump)", delegate
		{
			Form dialog = (Form)Activator.CreateInstance(typeof(MusicTag.Importers.OptionsDialog), nonPublic: true);
			try
			{
				dialog.Show();
				Application.DoEvents();
				TreeView tree = (TreeView)GetField(dialog, "optionsTreeView");
				Check.Equal("TagSources", tree.SelectedNode?.Name, "first node auto-selected");
				foreach (string fieldName in VisiblePageFields)
				{
					Check.True(((Control)GetField(dialog, fieldName)).Visible, fieldName + " visible on TagSources page");
				}
				foreach (string fieldName in HiddenPageFields)
				{
					Check.True(!((Control)GetField(dialog, fieldName)).Visible, fieldName + " hidden on TagSources page");
				}
			}
			finally
			{
				dialog.Dispose();
			}
		});
	}

	private static object GetField(object instance, string fieldName)
	{
		FieldInfo field = typeof(MusicTag.Importers.OptionsDialog).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
		if (field == null)
		{
			throw new Exception("field not found: " + fieldName);
		}
		return field.GetValue(instance);
	}
}
