using System.Drawing;
using MusicTagWinApp.Common;

namespace MusicTagWinApp.Stubs;

internal class CheckBoxColumnHeader : CustomColumnHeader
{
	public Image CheckedImage { get; set; }

	public Image UncheckedImage { get; set; }

	public bool IsEditable { get; set; }

	public CheckBoxColumnHeader()
	{
	}

	public CheckBoxColumnHeader(string text)
	{
		Text = text;
	}

	public CheckBoxColumnHeader(string text, int width)
	{
		Text = text;
		Width = width;
	}

	public CheckBoxColumnHeader(string text, Image checkedImage, Image uncheckedImage)
	{
		Text = text;
		CheckedImage = checkedImage;
		UncheckedImage = uncheckedImage;
	}

	public CheckBoxColumnHeader(string text, Image checkedImage, Image uncheckedImage, int width)
	{
		Text = text;
		CheckedImage = checkedImage;
		UncheckedImage = uncheckedImage;
		Width = width;
	}
}

