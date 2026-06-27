using System.Windows.Forms;

namespace MusicTag.Candidates;

internal class AssociatedValueListViewItem : ListViewItem
{
	public string AssociatedValue { get; set; }

	public AssociatedValueListViewItem()
	{
	}

	public AssociatedValueListViewItem(string text)
	{
		base.Text = text;
	}

}
