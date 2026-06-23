using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MusicTag.Bridges;
using MusicTagWinApp.Containers;
using MusicTagWinApp.Properties;
using MusicTagWinApp.Win32.FileDialog;

namespace MusicTag.Consumers;

internal class FolderSelectionDialog
{
	private const int IncludeSubdirectoriesCheckBoxId = 1;

	private const FOS BaseFolderDialogOptions = FOS.FOS_DONTADDTORECENT | FOS.FOS_NOTESTFILECREATE | FOS.FOS_NOVALIDATE | FOS.FOS_FORCEFILESYSTEM | FOS.FOS_PICKFOLDERS;

	private IFileDialogCustomize fileDialogCustomize;

	public FolderSelectionDialog()
	{
		SelectedPaths = new List<string>();
	}

	public string InitialFolder { get; set; }

	public string DefaultFolder { get; set; }

	public bool IncludeSubdirectories { get; private set; } = true;

	public List<string> SelectedPaths { get; }

	public DialogResult ShowDialog(bool allowMultiSelect, bool showIncludeSubdirectories = true)
	{
		return ShowDialog(Form.ActiveForm, allowMultiSelect, showIncludeSubdirectories);
	}

	public DialogResult ShowDialog(IWin32Window owner, bool allowMultiSelect, bool showIncludeSubdirectories = true)
	{
		if (Environment.OSVersion.Version.Major >= 6)
		{
			return ShowVistaFolderDialog(owner, allowMultiSelect, showIncludeSubdirectories);
		}
		return ShowLegacyFolderDialog(owner);
	}

	private DialogResult ShowVistaFolderDialog(IWin32Window owner, bool allowMultiSelect, bool showIncludeSubdirectories)
	{
		IFileOpenDialog fileOpenDialog = new FileOpenDialogRCW() as IFileOpenDialog;
		fileOpenDialog.GetOptions(out FOS options);
		options |= BaseFolderDialogOptions;
		if (allowMultiSelect)
		{
			options |= FOS.FOS_ALLOWMULTISELECT;
		}
		fileOpenDialog.SetOptions(options);

		if (showIncludeSubdirectories)
		{
			fileDialogCustomize = fileOpenDialog as IFileDialogCustomize;
			fileDialogCustomize.AddCheckButton(IncludeSubdirectoriesCheckBoxId, Resources.subdirectories, IncludeSubdirectories);
		}

		Guid shellItemGuid = typeof(IShellItem).GUID;
		if (InitialFolder != null && NativeMethods.CreateShellItemFromPath(InitialFolder, IntPtr.Zero, ref shellItemGuid, out IShellItem initialFolderShellItem) == 0L)
		{
			fileOpenDialog.SetFolder(initialFolderShellItem);
		}

		if (DefaultFolder != null && NativeMethods.CreateShellItemFromPath(DefaultFolder, IntPtr.Zero, ref shellItemGuid, out IShellItem defaultFolderShellItem) == 0L)
		{
			fileOpenDialog.SetDefaultFolder(defaultFolderShellItem);
		}

		if (fileOpenDialog.Show(owner.Handle) == 0 && fileOpenDialog.GetResults(out IShellItemArray results) == 0 && results.GetCount(out uint selectedCount) == 0L && selectedCount != 0)
		{
			for (uint index = 0; index < selectedCount; index++)
			{
					if (results.GetItemAt(index, out IShellItem item) == 0L && item.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, out IntPtr pathPointer) == 0 && pathPointer != IntPtr.Zero)
				{
					try
					{
						SelectedPaths.Add(Marshal.PtrToStringAuto(pathPointer));
					}
					finally
					{
						Marshal.FreeCoTaskMem(pathPointer);
					}
				}
			}

			if (SelectedPaths.Any())
			{
				bool includeSubdirectories = false;
				fileDialogCustomize?.GetCheckButtonState(IncludeSubdirectoriesCheckBoxId, out includeSubdirectories);
				IncludeSubdirectories = includeSubdirectories;
				return DialogResult.OK;
			}
		}

		return DialogResult.Cancel;
	}

	private DialogResult ShowLegacyFolderDialog(IWin32Window owner)
	{
		using FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog();
		if (InitialFolder != null)
		{
			folderBrowserDialog.SelectedPath = InitialFolder;
		}

		if (folderBrowserDialog.ShowDialog(owner) != DialogResult.OK)
		{
			return DialogResult.Cancel;
		}

		SelectedPaths.Add(Path.GetDirectoryName(folderBrowserDialog.SelectedPath));
		return DialogResult.OK;
	}
}
