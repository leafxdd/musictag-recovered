using System;
using System.Collections.Generic;

namespace MusicTagWinApp;

[Serializable]
internal class ListViewFileSettingFileInfo
{
	public static readonly string ANY_FILE_DUMMY_PATH = "(Any files)";

	public string DirPath { get; set; }

	public bool IncludeSubDir { get; set; }

	public List<string> AnyFileList { get; set; }

	public bool Disabled { get; set; }

	public bool IsAnyFile()
	{
		return DirPath == ANY_FILE_DUMMY_PATH;
	}
}
