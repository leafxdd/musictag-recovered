using System.Windows.Forms;
using MusicTagWinApp.Common;

namespace MusicTag.Consumers;

internal class EditableColumnHeader : CustomColumnHeader
{
	public Control EditorControl { get; private set; }

	public EditableColumnHeader()
	{
	}

	public EditableColumnHeader(string text)
	{
		Text = text;
	}

	public EditableColumnHeader(string text, int width)
	{
		Text = text;
		Width = width;
	}

	public EditableColumnHeader(string text, Control editorControl)
	{
		Text = text;
		SetEditorControl(editorControl);
	}

	public EditableColumnHeader(string text, Control editorControl, int width)
	{
		Text = text;
		SetEditorControl(editorControl);
		Width = width;
	}

	private void SetEditorControl(Control editorControl)
	{
		EditorControl = editorControl;
		EditorControl.Visible = false;
		EditorControl.Tag = "not_init";
	}
}

