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

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[SettingsProvider(typeof(XmlSettingsProvider))]
	[SettingsManageability(SettingsManageability.Roaming)]
	[DefaultSettingValue("85")]
	public int PictureJpegQuality
	{
		get => (int)this["PictureJpegQuality"];
		set => this["PictureJpegQuality"] = value;
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[SettingsProvider(typeof(XmlSettingsProvider))]
	[SettingsManageability(SettingsManageability.Roaming)]
	[DefaultSettingValue("6")]
	public int PicturePngCompressionLevel
	{
		get => (int)this["PicturePngCompressionLevel"];
		set => this["PicturePngCompressionLevel"] = value;
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

	// 保存时移除 FLAC 文件上的 ID3v2/ID3v1 标签(FLAC 的标准标签容器是 Vorbis Comment,
	// 这些 ID3 是其它工具留下的错误标签)。默认开启=借保存自动修正。
	[UserScopedSetting]
	[DebuggerNonUserCode]
	[SettingsProvider(typeof(XmlSettingsProvider))]
	[DefaultSettingValue("True")]
	[SettingsManageability(SettingsManageability.Roaming)]
	public bool RemoveMisplacedId3OnSave
	{
		get
		{
			return (bool)this["RemoveMisplacedId3OnSave"];
		}
		set
		{
			this["RemoveMisplacedId3OnSave"] = value;
		}
	}

	// 保存时不写入 ID3v1 标签(TagLib 对 mp3 默认自动补建 ID3v1+ID3v2,保存即写出;
	// 开启后保存前移除,文件上已有的 ID3v1 一并物理清除)。
	[UserScopedSetting]
	[DebuggerNonUserCode]
	[SettingsProvider(typeof(XmlSettingsProvider))]
	[DefaultSettingValue("False")]
	[SettingsManageability(SettingsManageability.Roaming)]
	public bool RemoveId3v1OnSave
	{
		get
		{
			return (bool)this["RemoveId3v1OnSave"];
		}
		set
		{
			this["RemoveId3v1OnSave"] = value;
		}
	}

	// 保留文件已有 ID3v2 标签的原版本(TagLib ForceDefaultVersion=false):v2.4 文件保存后
	// 仍是 v2.4;新建标签仍按 ID3v2Version 所选版本落盘(TagLib header 版本 0 时回落 DefaultVersion)。
	[UserScopedSetting]
	[DebuggerNonUserCode]
	[SettingsProvider(typeof(XmlSettingsProvider))]
	[DefaultSettingValue("False")]
	[SettingsManageability(SettingsManageability.Roaming)]
	public bool KeepExistingId3v2Version
	{
		get
		{
			return (bool)this["KeepExistingId3v2Version"];
		}
		set
		{
			this["KeepExistingId3v2Version"] = value;
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

	// 持久化列宽的 DPI 戳(PMv2 实验分支):记录 ColumnHeader 里 width 值的刻度基准
	// (= 存盘时的 startupDpi)。0 = 旧版存量(无戳),恢复端按"本次启动屏刻度"恒等
	// 读入(与历史行为一致)。有戳时恢复端按 本次DPI/戳 换算,跨启动屏(上次主屏
	// 150% 存、这次副屏 100% 启)列宽不再整体偏大/缩水。
	[UserScopedSetting]
	[DebuggerNonUserCode]
	[SettingsProvider(typeof(XmlSettingsProvider))]
	[SettingsManageability(SettingsManageability.Roaming)]
	[DefaultSettingValue("0")]
	public int FileListColumnWidthsDpi
	{
		get
		{
			return (int)this["FileListColumnWidthsDpi"];
		}
		set
		{
			this["FileListColumnWidthsDpi"] = value;
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
	public bool ConnectorsArtists_PadSpaces
	{
		get
		{
			return (bool)this["ConnectorsArtists_PadSpaces"];
		}
		set
		{
			this["ConnectorsArtists_PadSpaces"] = value;
		}
	}

	// 艺术家分隔符的有效形态:按 ConnectorsArtists_PadSpaces 决定是否在两侧补空格(如 "/" ↔ " / ")。
	// 供 QQ / 网易云拼接多艺术家时统一取用(酷我/酷狗的艺术家由 API 直接返回整串,不经此处)。
	public string GetArtistConnector()
	{
		string connector = ConnectorsArtists;
		if (ConnectorsArtists_PadSpaces && !string.IsNullOrEmpty(connector))
		{
			return " " + connector + " ";
		}
		return connector;
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

	[SettingsProvider(typeof(XmlSettingsProvider))]
	[UserScopedSetting]
	[SettingsManageability(SettingsManageability.Roaming)]
	[DefaultSettingValue("")]
	[DebuggerNonUserCode]
	public string QQMusic_Cookie
	{
		get
		{
			return (string)this["QQMusic_Cookie"];
		}
		set
		{
			this["QQMusic_Cookie"] = value;
		}
	}

	[SettingsProvider(typeof(XmlSettingsProvider))]
	[UserScopedSetting]
	[SettingsManageability(SettingsManageability.Roaming)]
	[DefaultSettingValue("")]
	[DebuggerNonUserCode]
	public string WebSearch_CustomUserAgent
	{
		get
		{
			return (string)this["WebSearch_CustomUserAgent"];
		}
		set
		{
			this["WebSearch_CustomUserAgent"] = value;
		}
	}

	private void SettingChangingEventHandler(object sender, SettingChangingEventArgs e)
	{
	}

	private void SettingsSavingEventHandler(object sender, CancelEventArgs e)
	{
	}

}
