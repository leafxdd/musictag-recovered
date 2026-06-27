using System.Drawing;
using MusicTag.Candidates;

namespace MusicTagWinApp.Instances;

internal class CoverImageListViewItem : AssociatedValueListViewItem
{
	public Image CoverImage { get; set; }

	public CoverImageListViewItem()
	{
	}

	public CoverImageListViewItem(string text)
	{
		base.Text = text;
	}

	public CoverImageListViewItem(Image coverImage)
	{
		CoverImage = coverImage;
	}

	public CoverImageListViewItem(string text, Image coverImage)
	{
		CoverImage = coverImage;
		base.Text = text;
	}

	public CoverImageListViewItem(string text, Image coverImage, string associatedValue)
	{
		base.Text = text;
		CoverImage = coverImage;
		AssociatedValue = associatedValue;
	}
}

