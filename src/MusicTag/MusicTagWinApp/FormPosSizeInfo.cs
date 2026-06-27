using System;
using System.Drawing;

namespace MusicTagWinApp;

[Serializable]
internal class FormPosSizeInfo
{
	public Point? Location { get; set; }

	public Size? Size { get; set; }

	public bool Maximized { get; set; }

	public FormPosSizeInfo()
	{
	}
}
