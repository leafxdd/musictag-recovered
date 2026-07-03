using System.Collections;
using MusicTag.Candidates;

namespace MusicTagWinApp.Exporters;

internal class MultiValueListViewItem : AssociatedValueListViewItem
{
	public ArrayList Values { get; set; }

	public MultiValueListViewItem()
	{
	}

	public MultiValueListViewItem(string value)
	{
		base.Text = value;
	}

	public MultiValueListViewItem(ArrayList param)
	{
		Values = param;
	}

	public MultiValueListViewItem(string first, ArrayList attr)
	{
		base.Text = first;
		Values = attr;
	}

	public MultiValueListViewItem(string config, ArrayList caller, string comp)
	{
		base.Text = config;
		Values = caller;
		AssociatedValue = comp;
	}
}

