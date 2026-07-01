using System;
using System.Text;
using System.Windows.Forms;
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

		string sourceText = textBox.Text;
		int matchCount = 0;
		int startIndex = 0;
		int singleMatchIndex = 0;
		while (true)
		{
			startIndex = sourceText.IndexOf(SearchText, startIndex, GetStringComparison());
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

		// 用 SelectAll + Paste 写回(可撤销),与单匹配分支一致;
		// 直接给 textBox.Text 赋值会清空撤销缓冲区,导致整批替换无法 Ctrl+Z 撤销。
		string replacedText = ReplaceAllMatches(sourceText, SearchText, replacementText, GetStringComparison());
		textBox.SelectAll();
		textBox.Paste(replacedText);
		textBox.SelectionStart = 0;
		textBox.SelectionLength = 0;
		textBox.ScrollToCaret();
	}

	// 从实例方法提取 static 纯核:全量替换(所有 searchText 出现处替为 replacementText,与 ReplaceAll 计数循环
	// 同一 comparison)。SearchText(纯 auto-property)/GetStringComparison()(纯,基于 MatchCase)在同步 ReplaceAll
	// 执行期间不变,故循环内每迭代读归约为调用点一次求值等价。
	internal static string ReplaceAllMatches(string text, string searchText, string replacementText, StringComparison comparison)
	{
		StringBuilder result = new StringBuilder(text.Length);
		int startIndex = 0;
		while (true)
		{
			int matchIndex = text.IndexOf(searchText, startIndex, comparison);
			if (matchIndex < 0)
			{
				result.Append(text, startIndex, text.Length - startIndex);
				return result.ToString();
			}

			result.Append(text, startIndex, matchIndex - startIndex);
			result.Append(replacementText);
			startIndex = matchIndex + searchText.Length;
		}
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

		DialogService.ShowInformationMessage(string.Format(Resources.Msg_CannotFindText, SearchText));
	}

	private StringComparison GetStringComparison()
	{
		return MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
	}

	private void TextBox_KeyDown(object sender, KeyEventArgs e)
	{
		if (e.Control && e.KeyCode == Keys.A)
		{
			textBox.SelectAll();
			e.SuppressKeyPress = true;
			return;
		}

		if (e.Control && e.KeyCode == Keys.Z)
		{
			if (textBox.CanUndo)
			{
				textBox.Undo();
			}
			e.SuppressKeyPress = true;
			return;
		}

		if (e.KeyCode == Keys.F2)
		{
			FindPrevious();
			e.SuppressKeyPress = true;
			return;
		}

		if (e.KeyCode == Keys.F3)
		{
			FindNext();
			e.SuppressKeyPress = true;
		}
	}
}
