using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Configuration;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using MusicTag.Serialization;

namespace MusicTagWinApp.Properties;

[GeneratedCode("Microsoft.VisualStudio.Editors.SettingsDesigner.SettingsSingleFileGenerator", "15.9.0.0")]
[CompilerGenerated]
internal sealed class Settings : ApplicationSettingsBase
{
	private static Settings defaultInstance = (Settings)SettingsBase.Synchronized(new Settings());

	public static Settings Default => defaultInstance;

	[SettingsProvider(typeof(XmlSettingsProvider))]
	[UserScopedSetting]
	[SettingsManageability(SettingsManageability.Roaming)]
	[DefaultSettingValue("")]
	[DebuggerNonUserCode]
	public string Language
	{
		get
		{
			return (string)this["Language"];
		}
		set
		{
			this["Language"] = value;
		}
	}

	[DefaultSettingValue("True")]
	[SettingsManageability(SettingsManageability.Roaming)]
	[UserScopedSetting]
	[DebuggerNonUserCode]
	[SettingsProvider(typeof(XmlSettingsProvider))]
	public bool LyricDownload_DownloadTrans_Enable
	{
		get
		{
			return (bool)this["LyricDownload_DownloadTrans_Enable"];
		}
		set
		{
			this["LyricDownload_DownloadTrans_Enable"] = value;
		}
	}

	[SettingsProvider(typeof(XmlSettingsProvider))]
	[DebuggerNonUserCode]
	[UserScopedSetting]
	[SettingsManageability(SettingsManageability.Roaming)]
	[DefaultSettingValue("False")]
	public bool LyricDownload_DownloadTrans_DontDownloadOrigLyric
	{
		get
		{
			return (bool)this["LyricDownload_DownloadTrans_DontDownloadOrigLyric"];
		}
		set
		{
			this["LyricDownload_DownloadTrans_DontDownloadOrigLyric"] = value;
		}
	}

	[SettingsProvider(typeof(XmlSettingsProvider))]
	[UserScopedSetting]
	[DebuggerNonUserCode]
	[SettingsManageability(SettingsManageability.Roaming)]
	[DefaultSettingValue("0")]
	public int LyricDownload_DownloadTrans_LyricFormat
	{
		get
		{
			return (int)this["LyricDownload_DownloadTrans_LyricFormat"];
		}
		set
		{
			this["LyricDownload_DownloadTrans_LyricFormat"] = value;
		}
	}

	[UserScopedSetting]
	[SettingsProvider(typeof(XmlSettingsProvider))]
	[DefaultSettingValue("0")]
	[DebuggerNonUserCode]
	[SettingsManageability(SettingsManageability.Roaming)]
	public int LyricDownload_DownloadTrans_ChineseConvMode
	{
		get
		{
			return (int)this["LyricDownload_DownloadTrans_ChineseConvMode"];
		}
		set
		{
			this["LyricDownload_DownloadTrans_ChineseConvMode"] = value;
		}
	}

	[SettingsProvider(typeof(XmlSettingsProvider))]
	[UserScopedSetting]
	[DebuggerNonUserCode]
	[SettingsManageability(SettingsManageability.Roaming)]
	[DefaultSettingValue("True")]
	public bool LyricDownload_ReformatTimetag
	{
		get
		{
			return (bool)this["LyricDownload_ReformatTimetag"];
		}
		set
		{
			this["LyricDownload_ReformatTimetag"] = value;
		}
	}

	[SettingsProvider(typeof(XmlSettingsProvider))]
	[DebuggerNonUserCode]
	[SettingsManageability(SettingsManageability.Roaming)]
	[DefaultSettingValue("False")]
	[UserScopedSetting]
	public bool LyricDownload_RemoveTimetag
	{
		get
		{
			return (bool)this["LyricDownload_RemoveTimetag"];
		}
		set
		{
			this["LyricDownload_RemoveTimetag"] = value;
		}
	}

	[DebuggerNonUserCode]
	[SettingsManageability(SettingsManageability.Roaming)]
	[SettingsProvider(typeof(XmlSettingsProvider))]
	[DefaultSettingValue("False")]
	[UserScopedSetting]
	public bool LyricDownload_DeleteLinesOfBlankText
	{
		get
		{
			return (bool)this["LyricDownload_DeleteLinesOfBlankText"];
		}
		set
		{
			this["LyricDownload_DeleteLinesOfBlankText"] = value;
		}
	}

	[SettingsManageability(SettingsManageability.Roaming)]
	[DebuggerNonUserCode]
	[DefaultSettingValue("False")]
	[UserScopedSetting]
	[SettingsProvider(typeof(XmlSettingsProvider))]
	public bool LyricDownload_DeleteHeadTag
	{
		get
		{
			return (bool)this["LyricDownload_DeleteHeadTag"];
		}
		set
		{
			this["LyricDownload_DeleteHeadTag"] = value;
		}
	}

	[UserScopedSetting]
	[DefaultSettingValue("False")]
	[SettingsManageability(SettingsManageability.Roaming)]
	[DebuggerNonUserCode]
	[SettingsProvider(typeof(XmlSettingsProvider))]
	public bool SearchCondition_UseOnlyFilename
	{
		get
		{
			return (bool)this["SearchCondition_UseOnlyFilename"];
		}
		set
		{
			this["SearchCondition_UseOnlyFilename"] = value;
		}
	}

	[SettingsManageability(SettingsManageability.Roaming)]
	[UserScopedSetting]
	[DebuggerNonUserCode]
	[SettingsProvider(typeof(XmlSettingsProvider))]
	[DefaultSettingValue("True")]
	public bool SearchCondition_UseArtist
	{
		get
		{
			return (bool)this["SearchCondition_UseArtist"];
		}
		set
		{
			this["SearchCondition_UseArtist"] = value;
		}
	}

	[SettingsProvider(typeof(XmlSettingsProvider))]
	[SettingsManageability(SettingsManageability.Roaming)]
	[DefaultSettingValue("True")]
	[DebuggerNonUserCode]
	[UserScopedSetting]
	public bool SearchCondition_UseAlbum
	{
		get
		{
			return (bool)this["SearchCondition_UseAlbum"];
		}
		set
		{
			this["SearchCondition_UseAlbum"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[SettingsProvider(typeof(XmlSettingsProvider))]
	[SettingsManageability(SettingsManageability.Roaming)]
	[DefaultSettingValue("2000")]
	public int PictureSizeLimitsKB
	{
		get
		{
			return (int)this["PictureSizeLimitsKB"];
		}
		set
		{
			this["PictureSizeLimitsKB"] = value;
		}
	}

	[DebuggerNonUserCode]
	[UserScopedSetting]
	[SettingsProvider(typeof(XmlSettingsProvider))]
	[SettingsManageability(SettingsManageability.Roaming)]
	[DefaultSettingValue("UTF-8")]
	public string SaveLrcFileDefaultEncoding
	{
		get
		{
			return (string)this["SaveLrcFileDefaultEncoding"];
		}
		set
		{
			this["SaveLrcFileDefaultEncoding"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[SettingsProvider(typeof(XmlSettingsProvider))]
	[DefaultSettingValue("3")]
	[SettingsManageability(SettingsManageability.Roaming)]
	public int ID3v2Version
	{
		get
		{
			return (int)this["ID3v2Version"];
		}
		set
		{
			this["ID3v2Version"] = value;
		}
	}

	[DebuggerNonUserCode]
	[SettingsProvider(typeof(XmlSettingsProvider))]
	[UserScopedSetting]
	[SettingsManageability(SettingsManageability.Roaming)]
	[DefaultSettingValue("")]
	public string LyricInfo_SourceItemList
	{
		get
		{
			return (string)this["LyricInfo_SourceItemList"];
		}
		set
		{
			this["LyricInfo_SourceItemList"] = value;
		}
	}

	[UserScopedSetting]
	[SettingsManageability(SettingsManageability.Roaming)]
	[DefaultSettingValue("")]
	[SettingsProvider(typeof(XmlSettingsProvider))]
	[DebuggerNonUserCode]
	public string PictureInfo_SourceItemList
	{
		get
		{
			return (string)this["PictureInfo_SourceItemList"];
		}
		set
		{
			this["PictureInfo_SourceItemList"] = value;
		}
	}

	[DebuggerNonUserCode]
	[SettingsProvider(typeof(XmlSettingsProvider))]
	[SettingsManageability(SettingsManageability.Roaming)]
	[DefaultSettingValue("")]
	[UserScopedSetting]
	public string CombTagsInfo_SourceItemList
	{
		get
		{
			return (string)this["CombTagsInfo_SourceItemList"];
		}
		set
		{
			this["CombTagsInfo_SourceItemList"] = value;
		}
	}

	[DefaultSettingValue("US")]
	[SettingsProvider(typeof(XmlSettingsProvider))]
	[DebuggerNonUserCode]
	[UserScopedSetting]
	[SettingsManageability(SettingsManageability.Roaming)]
	public string ItunesSearchParams_Country
	{
		get
		{
			return (string)this["ItunesSearchParams_Country"];
		}
		set
		{
			this["ItunesSearchParams_Country"] = value;
		}
	}

	[DebuggerNonUserCode]
	[DefaultSettingValue("")]
	[SettingsManageability(SettingsManageability.Roaming)]
	[SettingsProvider(typeof(XmlSettingsProvider))]
	[UserScopedSetting]
	public string ListviewColumnHeader
	{
		get
		{
			return (string)this["ListviewColumnHeader"];
		}
		set
		{
			this["ListviewColumnHeader"] = value;
		}
	}

	[SettingsManageability(SettingsManageability.Roaming)]
	[DefaultSettingValue("True")]
	[SettingsProvider(typeof(XmlSettingsProvider))]
	[DebuggerNonUserCode]
	[UserScopedSetting]
	public bool OverwritePictureboxPicture
	{
		get
		{
			return (bool)this["OverwritePictureboxPicture"];
		}
		set
		{
			this["OverwritePictureboxPicture"] = value;
		}
	}

	[DebuggerNonUserCode]
	[DefaultSettingValue("")]
	[SettingsManageability(SettingsManageability.Roaming)]
	[UserScopedSetting]
	[SettingsProvider(typeof(XmlSettingsProvider))]
	public string SortSetting
	{
		get
		{
			return (string)this["SortSetting"];
		}
		set
		{
			this["SortSetting"] = value;
		}
	}

	[DefaultSettingValue("0")]
	[SettingsManageability(SettingsManageability.Roaming)]
	[DebuggerNonUserCode]
	[UserScopedSetting]
	[SettingsProvider(typeof(XmlSettingsProvider))]
	public int FileFilterByDuration
	{
		get
		{
			return (int)this["FileFilterByDuration"];
		}
		set
		{
			this["FileFilterByDuration"] = value;
		}
	}

	[SettingsManageability(SettingsManageability.Roaming)]
	[SettingsProvider(typeof(XmlSettingsProvider))]
	[DefaultSettingValue("True")]
	[UserScopedSetting]
	[DebuggerNonUserCode]
	public bool FileFilterIgnoreVideoFile
	{
		get
		{
			return (bool)this["FileFilterIgnoreVideoFile"];
		}
		set
		{
			this["FileFilterIgnoreVideoFile"] = value;
		}
	}

	[DebuggerNonUserCode]
	[DefaultSettingValue("")]
	[SettingsManageability(SettingsManageability.Roaming)]
	[SettingsProvider(typeof(XmlSettingsProvider))]
	[UserScopedSetting]
	public string MainFormPosSizeInfo
	{
		get
		{
			return (string)this["MainFormPosSizeInfo"];
		}
		set
		{
			this["MainFormPosSizeInfo"] = value;
		}
	}

	[SettingsProvider(typeof(XmlSettingsProvider))]
	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("")]
	[SettingsManageability(SettingsManageability.Roaming)]
	public string CombTagsSearchOverwriteOptions
	{
		get
		{
			return (string)this["CombTagsSearchOverwriteOptions"];
		}
		set
		{
			this["CombTagsSearchOverwriteOptions"] = value;
		}
	}

	[SettingsManageability(SettingsManageability.Roaming)]
	[DebuggerNonUserCode]
	[DefaultSettingValue("False")]
	[UserScopedSetting]
	[SettingsProvider(typeof(XmlSettingsProvider))]
	public bool SaveTagsKeepUpdateTime
	{
		get
		{
			return (bool)this["SaveTagsKeepUpdateTime"];
		}
		set
		{
			this["SaveTagsKeepUpdateTime"] = value;
		}
	}

	[DefaultSettingValue("10")]
	[SettingsManageability(SettingsManageability.Roaming)]
	[DebuggerNonUserCode]
	[UserScopedSetting]
	[SettingsProvider(typeof(XmlSettingsProvider))]
	public int WebSearchItemsLimit
	{
		get
		{
			return (int)this["WebSearchItemsLimit"];
		}
		set
		{
			this["WebSearchItemsLimit"] = value;
		}
	}

	[DefaultSettingValue("False")]
	[SettingsManageability(SettingsManageability.Roaming)]
	[UserScopedSetting]
	[SettingsProvider(typeof(XmlSettingsProvider))]
	[DebuggerNonUserCode]
	public bool SaveLrcWhileSaveTags
	{
		get
		{
			return (bool)this["SaveLrcWhileSaveTags"];
		}
		set
		{
			this["SaveLrcWhileSaveTags"] = value;
		}
	}

	[UserScopedSetting]
	[SettingsProvider(typeof(XmlSettingsProvider))]
	[SettingsManageability(SettingsManageability.Roaming)]
	[DebuggerNonUserCode]
	[DefaultSettingValue("4")]
	public int AutoMatchTagsWebSearchThreadCount
	{
		get
		{
			return (int)this["AutoMatchTagsWebSearchThreadCount"];
		}
		set
		{
			this["AutoMatchTagsWebSearchThreadCount"] = value;
		}
	}

	[SettingsProvider(typeof(XmlSettingsProvider))]
	[SettingsManageability(SettingsManageability.Roaming)]
	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("0")]
	public int LastVersionCode
	{
		get
		{
			return (int)this["LastVersionCode"];
		}
		set
		{
			this["LastVersionCode"] = value;
		}
	}

	[SettingsManageability(SettingsManageability.Roaming)]
	[UserScopedSetting]
	[SettingsProvider(typeof(XmlSettingsProvider))]
	[DefaultSettingValue("")]
	[DebuggerNonUserCode]
	public string SaveLrcDirectory
	{
		get
		{
			return (string)this["SaveLrcDirectory"];
		}
		set
		{
			this["SaveLrcDirectory"] = value;
		}
	}

	[DefaultSettingValue("SameAsSongFileName")]
	[SettingsProvider(typeof(XmlSettingsProvider))]
	[DebuggerNonUserCode]
	[SettingsManageability(SettingsManageability.Roaming)]
	[UserScopedSetting]
	public string SaveLrcFilenameFormat
	{
		get
		{
			return (string)this["SaveLrcFilenameFormat"];
		}
		set
		{
			this["SaveLrcFilenameFormat"] = value;
		}
	}

	[DebuggerNonUserCode]
	[SettingsManageability(SettingsManageability.Roaming)]
	[SettingsProvider(typeof(XmlSettingsProvider))]
	[UserScopedSetting]
	[DefaultSettingValue("")]
	public string ConnectorsLyricAndTLyric
	{
		get
		{
			return (string)this["ConnectorsLyricAndTLyric"];
		}
		set
		{
			this["ConnectorsLyricAndTLyric"] = value;
		}
	}

	[DefaultSettingValue("")]
	[DebuggerNonUserCode]
	[UserScopedSetting]
	[SettingsProvider(typeof(XmlSettingsProvider))]
	[SettingsManageability(SettingsManageability.Roaming)]
	public string FilenameCustomPattern
	{
		get
		{
			return (string)this["FilenameCustomPattern"];
		}
		set
		{
			this["FilenameCustomPattern"] = value;
		}
	}

	[DebuggerNonUserCode]
	[SettingsProvider(typeof(XmlSettingsProvider))]
	[UserScopedSetting]
	[DefaultSettingValue(".aac;.aiff;.aif;.aifc;.ape;.dff;.dsf;.flac;.mpc;.mp3;.mp4;.m4a;.ogg;.opus;.tak;.wav;.wma;.wv;")]
	[SettingsManageability(SettingsManageability.Roaming)]
	public string RestrictFileExts
	{
		get
		{
			return (string)this["RestrictFileExts"];
		}
		set
		{
			this["RestrictFileExts"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("")]
	[SettingsManageability(SettingsManageability.Roaming)]
	[SettingsProvider(typeof(XmlSettingsProvider))]
	public string IgnoreCheckSpecAppVersion
	{
		get
		{
			return (string)this["IgnoreCheckSpecAppVersion"];
		}
		set
		{
			this["IgnoreCheckSpecAppVersion"] = value;
		}
	}

	[UserScopedSetting]
	[DefaultSettingValue("True")]
	[SettingsProvider(typeof(XmlSettingsProvider))]
	[SettingsManageability(SettingsManageability.Roaming)]
	[DebuggerNonUserCode]
	public bool CheckForUpdatesOnStartup
	{
		get
		{
			return (bool)this["CheckForUpdatesOnStartup"];
		}
		set
		{
			this["CheckForUpdatesOnStartup"] = value;
		}
	}

	[SettingsProvider(typeof(XmlSettingsProvider))]
	[UserScopedSetting]
	[DebuggerNonUserCode]
	[SettingsManageability(SettingsManageability.Roaming)]
	[DefaultSettingValue("AUTO")]
	public string PictureFormatLimits
	{
		get
		{
			return (string)this["PictureFormatLimits"];
		}
		set
		{
			this["PictureFormatLimits"] = value;
		}
	}

	[DefaultSettingValue("")]
	[DebuggerNonUserCode]
	[SettingsManageability(SettingsManageability.Roaming)]
	[SettingsProvider(typeof(XmlSettingsProvider))]
	[UserScopedSetting]
	public string AutoMatchTagsCondition
	{
		get
		{
			return (string)this["AutoMatchTagsCondition"];
		}
		set
		{
			this["AutoMatchTagsCondition"] = value;
		}
	}

	[DebuggerNonUserCode]
	[DefaultSettingValue("0")]
	[SettingsManageability(SettingsManageability.Roaming)]
	[UserScopedSetting]
	[SettingsProvider(typeof(XmlSettingsProvider))]
	public int DatabaseVersion
	{
		get
		{
			return (int)this["DatabaseVersion"];
		}
		set
		{
			this["DatabaseVersion"] = value;
		}
	}

	[SettingsManageability(SettingsManageability.Roaming)]
	[DefaultSettingValue("")]
	[DebuggerNonUserCode]
	[SettingsProvider(typeof(XmlSettingsProvider))]
	[UserScopedSetting]
	public string FilenameRelCondition
	{
		get
		{
			return (string)this["FilenameRelCondition"];
		}
		set
		{
			this["FilenameRelCondition"] = value;
		}
	}

	[UserScopedSetting]
	[SettingsManageability(SettingsManageability.Roaming)]
	[DebuggerNonUserCode]
	[DefaultSettingValue("")]
	[SettingsProvider(typeof(XmlSettingsProvider))]
	public string FilterListViewType
	{
		get
		{
			return (string)this["FilterListViewType"];
		}
		set
		{
			this["FilterListViewType"] = value;
		}
	}

	[DebuggerNonUserCode]
	[UserScopedSetting]
	[SettingsProvider(typeof(XmlSettingsProvider))]
	[SettingsManageability(SettingsManageability.Roaming)]
	[DefaultSettingValue("")]
	public string FilterListViewKeyword
	{
		get
		{
			return (string)this["FilterListViewKeyword"];
		}
		set
		{
			this["FilterListViewKeyword"] = value;
		}
	}

	[DebuggerNonUserCode]
	[SettingsProvider(typeof(XmlSettingsProvider))]
	[SettingsManageability(SettingsManageability.Roaming)]
	[DefaultSettingValue("")]
	[UserScopedSetting]
	public string FilenameRelRegexCondition
	{
		get
		{
			return (string)this["FilenameRelRegexCondition"];
		}
		set
		{
			this["FilenameRelRegexCondition"] = value;
		}
	}

	[SettingsManageability(SettingsManageability.Roaming)]
	[DebuggerNonUserCode]
	[DefaultSettingValue("")]
	[SettingsProvider(typeof(XmlSettingsProvider))]
	[UserScopedSetting]
	public string FilenameRelSelectedTab
	{
		get
		{
			return (string)this["FilenameRelSelectedTab"];
		}
		set
		{
			this["FilenameRelSelectedTab"] = value;
		}
	}

	[DefaultSettingValue("/")]
	[SettingsManageability(SettingsManageability.Roaming)]
	[SettingsProvider(typeof(XmlSettingsProvider))]
	[DebuggerNonUserCode]
	[UserScopedSetting]
	public string ConnectorsArtists
	{
		get
		{
			return (string)this["ConnectorsArtists"];
		}
		set
		{
			this["ConnectorsArtists"] = value;
		}
	}

	[DebuggerNonUserCode]
	[DefaultSettingValue("False")]
	[SettingsProvider(typeof(XmlSettingsProvider))]
	[SettingsManageability(SettingsManageability.Roaming)]
	[UserScopedSetting]
	public bool DontDownloadLyricWithInstrumentInTitle
	{
		get
		{
			return (bool)this["DontDownloadLyricWithInstrumentInTitle"];
		}
		set
		{
			this["DontDownloadLyricWithInstrumentInTitle"] = value;
		}
	}

	[DebuggerNonUserCode]
	[DefaultSettingValue("0")]
	[UserScopedSetting]
	[SettingsProvider(typeof(XmlSettingsProvider))]
	[SettingsManageability(SettingsManageability.Roaming)]
	public int PictureResolutionLimits
	{
		get
		{
			return (int)this["PictureResolutionLimits"];
		}
		set
		{
			this["PictureResolutionLimits"] = value;
		}
	}

	[SettingsProvider(typeof(XmlSettingsProvider))]
	[DefaultSettingValue("False")]
	[UserScopedSetting]
	[SettingsManageability(SettingsManageability.Roaming)]
	[DebuggerNonUserCode]
	public bool AlwaysShowIconInNofiArea
	{
		get
		{
			return (bool)this["AlwaysShowIconInNofiArea"];
		}
		set
		{
			this["AlwaysShowIconInNofiArea"] = value;
		}
	}

	[SettingsProvider(typeof(XmlSettingsProvider))]
	[UserScopedSetting]
	[DefaultSettingValue("False")]
	[DebuggerNonUserCode]
	[SettingsManageability(SettingsManageability.Roaming)]
	public bool MinimizeToNotiArea
	{
		get
		{
			return (bool)this["MinimizeToNotiArea"];
		}
		set
		{
			this["MinimizeToNotiArea"] = value;
		}
	}

	[DefaultSettingValue("False")]
	[SettingsManageability(SettingsManageability.Roaming)]
	[DebuggerNonUserCode]
	[SettingsProvider(typeof(XmlSettingsProvider))]
	[UserScopedSetting]
	public bool CommentTagWrite163Key
	{
		get
		{
			return (bool)this["CommentTagWrite163Key"];
		}
		set
		{
			this["CommentTagWrite163Key"] = value;
		}
	}

	private void SettingChangingEventHandler(object sender, SettingChangingEventArgs e)
	{
	}

	private void SettingsSavingEventHandler(object sender, CancelEventArgs e)
	{
	}

}
