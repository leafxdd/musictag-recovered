using System;
using System.Windows.Forms;
using Microsoft.VisualBasic;
using MusicTagWinApp.Instances;
using MusicTagWinApp.Properties;

namespace MusicTag.Serialization;

internal sealed class TextBoxFindReplaceController
{
	private readonly TextBox textBox;

	public bool MatchCase { get; set; }

	public string SearchText { get; set; } = string.Empty;

	public TextBoxFindReplaceController(TextBox textBox)
	{
		this.textBox = textBox;
		this.textBox.KeyDown += TextBox_KeyDown;
	}

	public void FindPrevious()
	{
		if (string.IsNullOrEmpty(SearchText))
		{
			return;
		}

		int matchIndex = textBox.SelectionStart > 0
			? textBox.Text.LastIndexOf(SearchText, textBox.SelectionStart - 1, GetStringComparison())
			: -1;
		SelectMatchOrShowNotFound(matchIndex);
	}

	public void FindNext()
	{
		if (string.IsNullOrEmpty(SearchText))
		{
			return;
		}

		int startIndex = textBox.SelectionStart + textBox.SelectionLength;
		int matchIndex = textBox.Text.IndexOf(SearchText, startIndex, GetStringComparison());
		SelectMatchOrShowNotFound(matchIndex);
	}

	public void ReplaceCurrentAndFindNext(string replacementText)
	{
		if (string.IsNullOrEmpty(SearchText))
		{
			return;
		}

		if ((textBox.SelectedText ?? string.Empty).Equals(SearchText, GetStringComparison()))
		{
			int selectionStart = textBox.SelectionStart;
			textBox.Paste(replacementText);
			textBox.SelectionStart = selectionStart;
			textBox.SelectionLength = replacementText.Length;
		}

		FindNext();
	}

	public void ReplaceAll(string replacementText)
	{
		if (string.IsNullOrEmpty(SearchText) || replacementText == SearchText)
		{
			return;
		}

		int matchCount = 0;
		int startIndex = 0;
		int singleMatchIndex = 0;
		while (true)
		{
			startIndex = textBox.Text.IndexOf(SearchText, startIndex, GetStringComparison());
			if (startIndex < 0)
			{
				break;
			}

			matchCount++;
			singleMatchIndex = startIndex;
			startIndex += SearchText.Length;
		}

		if (matchCount == 1)
		{
			textBox.SelectionStart = singleMatchIndex;
			textBox.SelectionLength = SearchText.Length;
			textBox.Paste(replacementText);
			textBox.SelectionStart = 0;
			textBox.SelectionLength = 0;
			textBox.ScrollToCaret();
			return;
		}

		if (matchCount <= 1)
		{
			return;
		}

		textBox.SelectAll();
		textBox.Paste(Strings.Replace(textBox.Text, SearchText, replacementText, 1, -1, MatchCase ? CompareMethod.Binary : CompareMethod.Text));
		textBox.SelectionStart = 0;
		textBox.SelectionLength = 0;
		textBox.ScrollToCaret();
	}

	private void SelectMatchOrShowNotFound(int matchIndex)
	{
		if (matchIndex >= 0)
		{
			textBox.SelectionStart = matchIndex;
			textBox.SelectionLength = SearchText.Length;
			textBox.ScrollToCaret();
			return;
		}

		DatabaseMapper.ShowInformationMessage(string.Format(Resources.Msg_CannotFindText, SearchText));
	}

	private StringComparison GetStringComparison()
	{
		return MatchCase ? StringComparison.CurrentCulture : StringComparison.CurrentCultureIgnoreCase;
	}

	private void TextBox_KeyDown(object sender, KeyEventArgs e)
	{
		if (e.Control && e.KeyCode == Keys.A)
		{
			textBox.SelectAll();
			return;
		}

		if (e.Control && e.KeyCode == Keys.Z)
		{
			if (textBox.CanUndo)
			{
				textBox.Undo();
				textBox.Undo();
			}
			return;
		}

		if (e.KeyCode == Keys.F2)
		{
			FindPrevious();
			return;
		}

		if (e.KeyCode == Keys.F3)
		{
			FindNext();
		}
	}
}
