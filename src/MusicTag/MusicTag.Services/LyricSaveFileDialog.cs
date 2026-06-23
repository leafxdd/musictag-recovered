using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MusicTag.Bridges;
using MusicTag.Serialization;
using MusicTagWinApp.Containers;
using MusicTagWinApp.Properties;
using MusicTagWinApp.Win32.FileDialog;

namespace MusicTag.Services;

internal class LyricSaveFileDialog
{
	private const int EncodingGroupId = 1000;

	private const int EncodingComboBoxId = 1100;

	private readonly List<string> encodingOptions;

	private IFileDialogCustomize dialogCustomizer;

	public string InitialDirectory { get; set; }

	public string FileName { get; set; }

	public string SelectedEncoding { get; private set; }

	public LyricSaveFileDialog()
	{
		encodingOptions = Resources.LrcFileEncodings.Split('|').ToList();
	}

	public DialogResult ShowDialog()
	{
		return ShowDialog(Form.ActiveForm);
	}

	public DialogResult ShowDialog(IWin32Window owner)
	{
		if (Environment.OSVersion.Version.Major >= 6)
		{
			return ShowVistaDialog(owner);
		}

		return ShowLegacyDialog(owner);
	}

	private DialogResult ShowVistaDialog(IWin32Window owner)
	{
		IFileDialog fileDialog = new FileSaveDialogRCW() as IFileDialog;
		dialogCustomizer = fileDialog as IFileDialogCustomize;
		dialogCustomizer.StartVisualGroup(EncodingGroupId, Resources.EncodingLabel);
		dialogCustomizer.AddComboBox(EncodingComboBoxId);
		dialogCustomizer.EndVisualGroup();

		for (int index = 0; index < encodingOptions.Count; index++)
		{
			dialogCustomizer.AddControlItem(EncodingComboBoxId, index, encodingOptions[index]);
		}

		int defaultEncodingIndex = encodingOptions.IndexOf(Settings.Default.SaveLrcFileDefaultEncoding);
		if (defaultEncodingIndex < 0)
		{
			defaultEncodingIndex = 0;
		}
		dialogCustomizer.SetSelectedControlItem(EncodingComboBoxId, defaultEncodingIndex);
		dialogCustomizer.MakeProminent(EncodingGroupId);
		fileDialog.SetFileTypes(1u, new FileDialogFilterSpec[1]
		{
			new FileDialogFilterSpec
			{
				DisplayName = "Lrc file (*.lrc)",
				Pattern = "*.lrc"
			}
		});

		SetInitialFolder(fileDialog);
		if (FileName != null)
		{
			fileDialog.SetFileName(FileName);
		}

		IntPtr ownerHandle = owner?.Handle ?? IntPtr.Zero;
		if (fileDialog.Show(ownerHandle) != 0 || fileDialog.GetResult(out var shellItem) != 0 || shellItem.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, out var pathPointer) != 0 || pathPointer == IntPtr.Zero)
		{
			return DialogResult.Cancel;
		}

		try
		{
			FileName = Marshal.PtrToStringAuto(pathPointer);
			dialogCustomizer.GetSelectedControlItem(EncodingComboBoxId, out int selectedEncodingIndex);
			SelectedEncoding = (selectedEncodingIndex >= 0 && selectedEncodingIndex < encodingOptions.Count) ? encodingOptions[selectedEncodingIndex] : encodingOptions[defaultEncodingIndex];
			return DialogResult.OK;
		}
		finally
		{
			Marshal.FreeCoTaskMem(pathPointer);
		}
	}

	private void SetInitialFolder(IFileDialog fileDialog)
	{
		if (InitialDirectory == null)
		{
			return;
		}

		Guid shellItemGuid = typeof(IShellItem).GUID;
		if (NativeMethods.CreateShellItemFromPath(InitialDirectory, IntPtr.Zero, ref shellItemGuid, out var shellItem) == 0L)
		{
			fileDialog.SetFolder(shellItem);
		}
	}

	private DialogResult ShowLegacyDialog(IWin32Window owner)
	{
		using SaveFileDialog saveFileDialog = new SaveFileDialog
		{
			Filter = "Lrc file (*.lrc)|*.lrc"
		};

		if (InitialDirectory != null)
		{
			saveFileDialog.InitialDirectory = InitialDirectory;
		}
		if (FileName != null)
		{
			saveFileDialog.FileName = FileName;
		}

		if (saveFileDialog.ShowDialog(owner) != DialogResult.OK)
		{
			return DialogResult.Cancel;
		}

		FileName = saveFileDialog.FileName;
		SelectedEncoding = Settings.Default.SaveLrcFileDefaultEncoding;
		return DialogResult.OK;
	}
}
