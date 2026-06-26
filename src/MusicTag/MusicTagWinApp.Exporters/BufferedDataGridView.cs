using System.Windows.Forms;

namespace MusicTagWinApp.Exporters;

// 文件列表使用的 DataGridView:构造即开启 protected 的 DoubleBuffered —— 这是 DGV 横向/纵向
// 滚动顺滑的关键前提(对照 MusicTag.Composer/DoubleBufferedSplitContainer 的派生双缓冲先例)。
// 横向滚动卡顿("文字一行一行刷出来")是 WinForms ListView 的引擎弱项,迁到双缓冲 DataGridView
// 根治 —— 实测可跑满显示器刷新率。
internal sealed class BufferedDataGridView : DataGridView
{
	public BufferedDataGridView()
	{
		DoubleBuffered = true;
	}
}
