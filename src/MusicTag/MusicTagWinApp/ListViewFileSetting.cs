using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace MusicTagWinApp;

[Serializable]
internal class ListViewFileSetting
{
	[NonSerialized]
	private Dictionary<string, ListViewFileSettingFileInfo> listDirMap;

	[NonSerialized]
	private HashSet<string> listAnyFileSet;

	public List<ListViewFileSettingFileInfo> List { get; } = new List<ListViewFileSettingFileInfo>();

	[MethodImpl(MethodImplOptions.Synchronized)]
	public void AddForDir(ListViewFileSettingFileInfo fileInfo)
	{
		AddForDir(fileInfo.DirPath, fileInfo.IncludeSubDir);
	}

	[MethodImpl(MethodImplOptions.Synchronized)]
	public void AddForDir(string path, bool includeSubDir = true)
	{
		ClearForAnyFile();
		if (listDirMap.TryGetValue(path, out ListViewFileSettingFileInfo existingFileInfo))
		{
			existingFileInfo.IncludeSubDir = includeSubDir;
			existingFileInfo.Disabled = false;
			return;
		}

		ListViewFileSettingFileInfo directoryFileInfo = new ListViewFileSettingFileInfo
		{
			DirPath = path,
			IncludeSubDir = includeSubDir
		};
		listDirMap.Add(path, directoryFileInfo);
		List.Add(directoryFileInfo);
	}

	[MethodImpl(MethodImplOptions.Synchronized)]
	public void AddForAnyFile(string path)
	{
		ListViewFileSettingFileInfo anyFileInfo = EnableForAnyFile();
		if (!listAnyFileSet.Contains(path))
		{
			listAnyFileSet.Add(path);
			anyFileInfo.AnyFileList.Add(path);
		}
	}

	[MethodImpl(MethodImplOptions.Synchronized)]
	public void UpdateForAnyFile(string path, string newPath)
	{
		if (!IsAnyFileMode() || path == newPath)
		{
			return;
		}

		ListViewFileSettingFileInfo anyFileInfo = EnableForAnyFile();
		if (!listAnyFileSet.Contains(path))
		{
			return;
		}

		listAnyFileSet.Remove(path);
		listAnyFileSet.Add(newPath);
		List<string> anyFileList = anyFileInfo.AnyFileList;
		anyFileList.Insert(anyFileList.IndexOf(path), newPath);
		anyFileList.Remove(path);
	}

	[MethodImpl(MethodImplOptions.Synchronized)]
	public void RemoveForDir(ListViewFileSettingFileInfo fileInfo)
	{
		if (!listDirMap.ContainsKey(fileInfo.DirPath))
		{
			return;
		}

		listDirMap.Remove(fileInfo.DirPath);
		List.Remove(fileInfo);
	}

	[MethodImpl(MethodImplOptions.Synchronized)]
	public void RemoveForAnyFile(string path)
	{
		if (!listAnyFileSet.Contains(path))
		{
			return;
		}

		listAnyFileSet.Remove(path);
		listDirMap[ListViewFileSettingFileInfo.ANY_FILE_DUMMY_PATH].AnyFileList.Remove(path);
	}

	[MethodImpl(MethodImplOptions.Synchronized)]
	public void ClearForAnyFile(bool isDisable = true)
	{
		if (listDirMap.TryGetValue(ListViewFileSettingFileInfo.ANY_FILE_DUMMY_PATH, out ListViewFileSettingFileInfo anyFileInfo) && !anyFileInfo.Disabled)
		{
			if (isDisable)
			{
				anyFileInfo.Disabled = true;
			}
			anyFileInfo.AnyFileList.Clear();
			listAnyFileSet.Clear();
		}
	}

	[MethodImpl(MethodImplOptions.Synchronized)]
	public void DisableForDir()
	{
		List.ForEach(DisableDirectoryEntry);
	}

	[MethodImpl(MethodImplOptions.Synchronized)]
	public ListViewFileSettingFileInfo EnableForAnyFile()
	{
		if (!listDirMap.TryGetValue(ListViewFileSettingFileInfo.ANY_FILE_DUMMY_PATH, out ListViewFileSettingFileInfo anyFileInfo))
		{
			anyFileInfo = new ListViewFileSettingFileInfo
			{
				DirPath = ListViewFileSettingFileInfo.ANY_FILE_DUMMY_PATH,
				AnyFileList = new List<string>()
			};
			listDirMap.Add(ListViewFileSettingFileInfo.ANY_FILE_DUMMY_PATH, anyFileInfo);
			List.Add(anyFileInfo);
		}

		if (anyFileInfo.AnyFileList == null)
		{
			anyFileInfo.AnyFileList = new List<string>();
		}
		anyFileInfo.Disabled = false;
		return anyFileInfo;
	}

	[MethodImpl(MethodImplOptions.Synchronized)]
	public bool IsAnyFileMode()
	{
		if (listDirMap.TryGetValue(ListViewFileSettingFileInfo.ANY_FILE_DUMMY_PATH, out ListViewFileSettingFileInfo anyFileInfo))
		{
			return !anyFileInfo.Disabled;
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.Synchronized)]
	public void Clear()
	{
		List.Clear();
		listDirMap.Clear();
		listAnyFileSet.Clear();
	}

	[MethodImpl(MethodImplOptions.Synchronized)]
	public void ResetListSet()
	{
		if (listDirMap == null)
		{
			listDirMap = new Dictionary<string, ListViewFileSettingFileInfo>();
		}
		listDirMap.Clear();

		if (listAnyFileSet == null)
		{
			listAnyFileSet = new HashSet<string>();
		}
		listAnyFileSet.Clear();

		List<string> anyFileList = null;
		for (int index = 0; index < List.Count;)
		{
			ListViewFileSettingFileInfo fileInfo = List[index];
			if (string.IsNullOrWhiteSpace(fileInfo.DirPath) || listDirMap.ContainsKey(fileInfo.DirPath))
			{
				List.RemoveAt(index);
				continue;
			}

			listDirMap.Add(fileInfo.DirPath, fileInfo);
			if (fileInfo.IsAnyFile())
			{
				if (fileInfo.AnyFileList == null)
				{
					fileInfo.AnyFileList = new List<string>();
				}
				anyFileList = fileInfo.AnyFileList;
			}
			index++;
		}

		if (anyFileList == null)
		{
			return;
		}

		foreach (string path in anyFileList)
		{
			if (!listAnyFileSet.Contains(path))
			{
				listAnyFileSet.Add(path);
			}
		}
	}

	[MethodImpl(MethodImplOptions.Synchronized)]
	public IEnumerable<string> ToAnyFilePathList()
	{
		if (listDirMap.TryGetValue(ListViewFileSettingFileInfo.ANY_FILE_DUMMY_PATH, out ListViewFileSettingFileInfo anyFileInfo) && !anyFileInfo.Disabled)
		{
			return anyFileInfo.AnyFileList;
		}
		return new List<string>();
	}

	[MethodImpl(MethodImplOptions.Synchronized)]
	public IEnumerable<ListViewFileSettingFileInfo> ToDirPathList()
	{
		return List.Where(fileInfo => !fileInfo.Disabled && !fileInfo.IsAnyFile());
	}

	private static void DisableDirectoryEntry(ListViewFileSettingFileInfo fileInfo)
	{
		if (!fileInfo.IsAnyFile() && !fileInfo.Disabled)
		{
			fileInfo.Disabled = true;
		}
	}
}
