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
		IFileOpenDialog fileOpenDialog = null;
		IShellItemArray results = null;
		try
		{
			fileOpenDialog = new FileOpenDialogRCW() as IFileOpenDialog;
			IFileDialogCustomize fileDialogCustomize = null;
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

			SetShellFolder(InitialFolder, shellItem => fileOpenDialog.SetFolder(shellItem));
			SetShellFolder(DefaultFolder, shellItem => fileOpenDialog.SetDefaultFolder(shellItem));

			if (fileOpenDialog.Show(owner.Handle) == 0 && fileOpenDialog.GetResults(out results) == 0 && results.GetCount(out uint selectedCount) == 0L && selectedCount != 0)
			{
				for (uint index = 0; index < selectedCount; index++)
				{
					AddSelectedPath(results, index);
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
		finally
		{
			ReleaseComObject(results);
			ReleaseComObject(fileOpenDialog);
		}
	}

	private static void SetShellFolder(string folderPath, Action<IShellItem> setFolderAction)
	{
		if (folderPath == null)
		{
			return;
		}

		Guid shellItemGuid = typeof(IShellItem).GUID;
		IShellItem shellItem = null;
		try
		{
			if (NativeMethods.CreateShellItemFromPath(folderPath, IntPtr.Zero, ref shellItemGuid, out shellItem) == 0L)
			{
				setFolderAction(shellItem);
			}
		}
		finally
		{
			ReleaseComObject(shellItem);
		}
	}

	private void AddSelectedPath(IShellItemArray results, uint index)
	{
		IShellItem item = null;
		IntPtr pathPointer = IntPtr.Zero;
		try
		{
			if (results.GetItemAt(index, out item) == 0L && item.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, out pathPointer) == 0 && pathPointer != IntPtr.Zero)
			{
				SelectedPaths.Add(Marshal.PtrToStringAuto(pathPointer));
			}
		}
		finally
		{
			if (pathPointer != IntPtr.Zero)
			{
				Marshal.FreeCoTaskMem(pathPointer);
			}
			ReleaseComObject(item);
		}
	}

	private static void ReleaseComObject(object comObject)
	{
		if (comObject != null && Marshal.IsComObject(comObject))
		{
			Marshal.FinalReleaseComObject(comObject);
		}
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
