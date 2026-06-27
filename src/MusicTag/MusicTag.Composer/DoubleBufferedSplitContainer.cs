using System.Reflection;
using System.Windows.Forms;

namespace MusicTag.Composer;

internal class DoubleBufferedSplitContainer : SplitContainer
{
	private const ControlStyles DoubleBufferStyles = ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer;

	public DoubleBufferedSplitContainer()
	{
		SetStyle(DoubleBufferStyles, value: true);
		SetPanelDoubleBuffered(base.Panel1);
		SetPanelDoubleBuffered(base.Panel2);
	}

	private static void SetPanelDoubleBuffered(Control panel)
	{
		MethodInfo setStyleMethod = typeof(Control).GetMethod("SetStyle", BindingFlags.Instance | BindingFlags.NonPublic);
		setStyleMethod?.Invoke(panel, new object[]
		{
			DoubleBufferStyles,
			true
		});
	}
}


