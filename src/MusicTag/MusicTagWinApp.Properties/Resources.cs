using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Resources;
using System.Runtime.CompilerServices;

namespace MusicTagWinApp.Properties;

[DebuggerNonUserCode]
[GeneratedCode("System.Resources.Tools.StronglyTypedResourceBuilder", "15.0.0.0")]
[CompilerGenerated]
internal class Resources
{
	private static ResourceManager resourceManager;

	private static CultureInfo resourceCulture;

	[EditorBrowsable(EditorBrowsableState.Advanced)]
	internal static ResourceManager ResourceManager
	{
		get
		{
			if (resourceManager == null)
			{
				resourceManager = new ResourceManager("MusicTagWinApp.Properties.Resources", typeof(Resources).Assembly);
			}
			return resourceManager;
		}
	}

	[EditorBrowsable(EditorBrowsableState.Advanced)]
	internal static CultureInfo Culture
	{
		get
		{
			return resourceCulture;
		}
		set
		{
			resourceCulture = value;
		}
	}

	internal Resources()
	{
	}

	internal static string about => ResourceManager.GetString("about", resourceCulture);

	internal static Bitmap addDirsToolStripMenuItem_Image => (Bitmap)ResourceManager.GetObject("addDirsToolStripMenuItem_Image", resourceCulture);

	internal static Bitmap addDirsToolStripMenuItem_Image2X => (Bitmap)ResourceManager.GetObject("addDirsToolStripMenuItem_Image2X", resourceCulture);

	internal static string adjusttimetag => ResourceManager.GetString("adjusttimetag", resourceCulture);

	internal static string album => ResourceManager.GetString("album", resourceCulture);

	internal static string albumartist => ResourceManager.GetString("albumartist", resourceCulture);

	internal static string Alipay => ResourceManager.GetString("Alipay", resourceCulture);

	internal static string any => ResourceManager.GetString("any", resourceCulture);

	internal static Icon AppIcon => (Icon)ResourceManager.GetObject("AppIcon", resourceCulture);

	internal static string AppName => ResourceManager.GetString("AppName", resourceCulture);

	internal static string artist => ResourceManager.GetString("artist", resourceCulture);

	internal static string Auto => ResourceManager.GetString("Auto", resourceCulture);

	internal static string AutoMatchTags => ResourceManager.GetString("AutoMatchTags", resourceCulture);

	internal static Bitmap batchAutoMatchTagsToolStripButton_Image => (Bitmap)ResourceManager.GetObject("batchAutoMatchTagsToolStripButton_Image", resourceCulture);

	internal static Bitmap batchAutoMatchTagsToolStripButton_Image2X => (Bitmap)ResourceManager.GetObject("batchAutoMatchTagsToolStripButton_Image2X", resourceCulture);

	internal static Bitmap batchExtractCoverToolStripButton_Image => (Bitmap)ResourceManager.GetObject("batchExtractCoverToolStripButton_Image", resourceCulture);

	internal static Bitmap batchExtractCoverToolStripButton_Image2X => (Bitmap)ResourceManager.GetObject("batchExtractCoverToolStripButton_Image2X", resourceCulture);

	internal static Bitmap batchFilenameRelToolStripButton_Image => (Bitmap)ResourceManager.GetObject("batchFilenameRelToolStripButton_Image", resourceCulture);

	internal static Bitmap batchFilenameRelToolStripButton_Image2X => (Bitmap)ResourceManager.GetObject("batchFilenameRelToolStripButton_Image2X", resourceCulture);

	internal static Bitmap batchSaveAsLrcFileToolStripSplitButton_Image => (Bitmap)ResourceManager.GetObject("batchSaveAsLrcFileToolStripSplitButton_Image", resourceCulture);

	internal static Bitmap batchSaveAsLrcFileToolStripSplitButton_Image2X => (Bitmap)ResourceManager.GetObject("batchSaveAsLrcFileToolStripSplitButton_Image2X", resourceCulture);

	internal static string bitpersample => ResourceManager.GetString("bitpersample", resourceCulture);

	internal static string bitrate => ResourceManager.GetString("bitrate", resourceCulture);

	internal static string Cancel => ResourceManager.GetString("Cancel", resourceCulture);

	internal static string Changed => ResourceManager.GetString("Changed", resourceCulture);

	internal static string channels => ResourceManager.GetString("channels", resourceCulture);

	internal static string characterset => ResourceManager.GetString("characterset", resourceCulture);

	internal static Bitmap characterSetToolStripMenuItem_Image => (Bitmap)ResourceManager.GetObject("characterSetToolStripMenuItem_Image", resourceCulture);

	internal static Bitmap characterSetToolStripMenuItem_Image2X => (Bitmap)ResourceManager.GetObject("characterSetToolStripMenuItem_Image2X", resourceCulture);

	internal static Bitmap check => (Bitmap)ResourceManager.GetObject("check", resourceCulture);

	internal static Bitmap check2X => (Bitmap)ResourceManager.GetObject("check2X", resourceCulture);

	internal static Bitmap chgDirToolStripMenuItem_Image => (Bitmap)ResourceManager.GetObject("chgDirToolStripMenuItem_Image", resourceCulture);

	internal static Bitmap chgDirToolStripMenuItem_Image2X => (Bitmap)ResourceManager.GetObject("chgDirToolStripMenuItem_Image2X", resourceCulture);

	internal static string ChooseFromFileTags => ResourceManager.GetString("ChooseFromFileTags", resourceCulture);

	internal static Bitmap chschtToolStripMenuItem_Image => (Bitmap)ResourceManager.GetObject("chschtToolStripMenuItem_Image", resourceCulture);

	internal static Bitmap chschtToolStripMenuItem_Image2X => (Bitmap)ResourceManager.GetObject("chschtToolStripMenuItem_Image2X", resourceCulture);

	internal static string Close => ResourceManager.GetString("Close", resourceCulture);

	internal static Bitmap combTagsSrcToolStripMenuItem_Image => (Bitmap)ResourceManager.GetObject("combTagsSrcToolStripMenuItem_Image", resourceCulture);

	internal static Bitmap combTagsSrcToolStripMenuItem_Image2X => (Bitmap)ResourceManager.GetObject("combTagsSrcToolStripMenuItem_Image2X", resourceCulture);

	internal static string comment => ResourceManager.GetString("comment", resourceCulture);

	internal static string composer => ResourceManager.GetString("composer", resourceCulture);

	internal static string Confirmation => ResourceManager.GetString("Confirmation", resourceCulture);

	internal static string content => ResourceManager.GetString("content", resourceCulture);

	internal static string cover => ResourceManager.GetString("cover", resourceCulture);

	internal static string customcolumns => ResourceManager.GetString("customcolumns", resourceCulture);

	internal static string DeleteItems => ResourceManager.GetString("DeleteItems", resourceCulture);

	internal static string discstr => ResourceManager.GetString("discstr", resourceCulture);

	internal static string Donate => ResourceManager.GetString("Donate", resourceCulture);

	internal static string DontDownloadLyricWithInstrumentInTitle => ResourceManager.GetString("DontDownloadLyricWithInstrumentInTitle", resourceCulture);

	internal static Bitmap download_failed => (Bitmap)ResourceManager.GetObject("download_failed", resourceCulture);

	internal static Bitmap download_failed2X => (Bitmap)ResourceManager.GetObject("download_failed2X", resourceCulture);

	internal static Bitmap downloading => (Bitmap)ResourceManager.GetObject("downloading", resourceCulture);

	internal static Bitmap downloading2X => (Bitmap)ResourceManager.GetObject("downloading2X", resourceCulture);

	internal static string durationinms => ResourceManager.GetString("durationinms", resourceCulture);

	internal static string Enable => ResourceManager.GetString("Enable", resourceCulture);

	internal static string encoding => ResourceManager.GetString("encoding", resourceCulture);

	internal static string EncodingLabel => ResourceManager.GetString("EncodingLabel", resourceCulture);

	internal static string Enum_Kugou => ResourceManager.GetString("Enum_Kugou", resourceCulture);

	internal static string Enum_Kuwo => ResourceManager.GetString("Enum_Kuwo", resourceCulture);

	internal static string Enum_Music163 => ResourceManager.GetString("Enum_Music163", resourceCulture);

	internal static string Enum_Xiami => ResourceManager.GetString("Enum_Xiami", resourceCulture);

	internal static string Error => ResourceManager.GetString("Error", resourceCulture);

	internal static Bitmap exitToolStripMenuItem_Image => (Bitmap)ResourceManager.GetObject("exitToolStripMenuItem_Image", resourceCulture);

	internal static Bitmap exitToolStripMenuItem_Image2X => (Bitmap)ResourceManager.GetObject("exitToolStripMenuItem_Image2X", resourceCulture);

	internal static string ExtractCover => ResourceManager.GetString("ExtractCover", resourceCulture);

	internal static string filedir => ResourceManager.GetString("filedir", resourceCulture);

	internal static Bitmap fileext_lrc => (Bitmap)ResourceManager.GetObject("fileext_lrc", resourceCulture);

	internal static Bitmap fileext_lrc2X => (Bitmap)ResourceManager.GetObject("fileext_lrc2X", resourceCulture);

	internal static Bitmap fileext_txt => (Bitmap)ResourceManager.GetObject("fileext_txt", resourceCulture);

	internal static Bitmap fileext_txt2X => (Bitmap)ResourceManager.GetObject("fileext_txt2X", resourceCulture);

	internal static string FileFilterByDurationList => ResourceManager.GetString("FileFilterByDurationList", resourceCulture);

	internal static string filename => ResourceManager.GetString("filename", resourceCulture);

	internal static string FilenameRel => ResourceManager.GetString("FilenameRel", resourceCulture);

	internal static string FilenameRelByBatch => ResourceManager.GetString("FilenameRelByBatch", resourceCulture);

	internal static string FindNext => ResourceManager.GetString("FindNext", resourceCulture);

	internal static string FindOrReplace => ResourceManager.GetString("FindOrReplace", resourceCulture);

	internal static string FindPrevious => ResourceManager.GetString("FindPrevious", resourceCulture);

	internal static string genre => ResourceManager.GetString("genre", resourceCulture);

	internal static string haspicture => ResourceManager.GetString("haspicture", resourceCulture);

	internal static string his_ctime => ResourceManager.GetString("his_ctime", resourceCulture);

	internal static Bitmap imagenotfound => (Bitmap)ResourceManager.GetObject("imagenotfound", resourceCulture);

	internal static Bitmap imagenotfound2X => (Bitmap)ResourceManager.GetObject("imagenotfound2X", resourceCulture);

	internal static Bitmap img_wait => (Bitmap)ResourceManager.GetObject("img_wait", resourceCulture);

	internal static Bitmap img_wait2X => (Bitmap)ResourceManager.GetObject("img_wait2X", resourceCulture);

	internal static string Information => ResourceManager.GetString("Information", resourceCulture);

	internal static string Item => ResourceManager.GetString("Item", resourceCulture);

	internal static Bitmap loading => (Bitmap)ResourceManager.GetObject("loading", resourceCulture);

	internal static Bitmap loading2X => (Bitmap)ResourceManager.GetObject("loading2X", resourceCulture);

	internal static string LrcFileEncodings => ResourceManager.GetString("LrcFileEncodings", resourceCulture);

	internal static string LrcFilenameFormat => ResourceManager.GetString("LrcFilenameFormat", resourceCulture);

	internal static string LrcFilenameFormatDesc => ResourceManager.GetString("LrcFilenameFormatDesc", resourceCulture);

	internal static Bitmap ly => (Bitmap)ResourceManager.GetObject("ly", resourceCulture);

	internal static Bitmap ly2X => (Bitmap)ResourceManager.GetObject("ly2X", resourceCulture);

	internal static string lyricist => ResourceManager.GetString("lyricist", resourceCulture);

	internal static string lyrics => ResourceManager.GetString("lyrics", resourceCulture);

	internal static Bitmap lyricSrcToolStripMenuItem_Image => (Bitmap)ResourceManager.GetObject("lyricSrcToolStripMenuItem_Image", resourceCulture);

	internal static Bitmap lyricSrcToolStripMenuItem_Image2X => (Bitmap)ResourceManager.GetObject("lyricSrcToolStripMenuItem_Image2X", resourceCulture);

	internal static string managedirs => ResourceManager.GetString("managedirs", resourceCulture);

	internal static Bitmap manageDirsToolStripMenuItem_Image => (Bitmap)ResourceManager.GetObject("manageDirsToolStripMenuItem_Image", resourceCulture);

	internal static Bitmap manageDirsToolStripMenuItem_Image2X => (Bitmap)ResourceManager.GetObject("manageDirsToolStripMenuItem_Image2X", resourceCulture);

	internal static string ms => ResourceManager.GetString("ms", resourceCulture);

	internal static string Msg_AddTagsHistoryFail => ResourceManager.GetString("Msg_AddTagsHistoryFail", resourceCulture);

	internal static string Msg_AddUndoRecordFail => ResourceManager.GetString("Msg_AddUndoRecordFail", resourceCulture);

	internal static string Msg_AdjustTimetagFail => ResourceManager.GetString("Msg_AdjustTimetagFail", resourceCulture);

	internal static string Msg_ApplicationExceptionWillExit => ResourceManager.GetString("Msg_ApplicationExceptionWillExit", resourceCulture);

	internal static string Msg_CannotConnectToUpdateSite => ResourceManager.GetString("Msg_CannotConnectToUpdateSite", resourceCulture);

	internal static string Msg_CannotFindText => ResourceManager.GetString("Msg_CannotFindText", resourceCulture);

	internal static string Msg_CapturegroupCannotBeDuplicate => ResourceManager.GetString("Msg_CapturegroupCannotBeDuplicate", resourceCulture);

	internal static string Msg_CapturegroupCannotBeEmpty => ResourceManager.GetString("Msg_CapturegroupCannotBeEmpty", resourceCulture);

	internal static string Msg_ClearAllTagsHistoryComplete => ResourceManager.GetString("Msg_ClearAllTagsHistoryComplete", resourceCulture);

	internal static string Msg_ClearAllTagsHistoryFail => ResourceManager.GetString("Msg_ClearAllTagsHistoryFail", resourceCulture);

	internal static string Msg_Cleartag => ResourceManager.GetString("Msg_Cleartag", resourceCulture);

	internal static string Msg_CleartagsCompleted => ResourceManager.GetString("Msg_CleartagsCompleted", resourceCulture);

	internal static string Msg_CollectingData => ResourceManager.GetString("Msg_CollectingData", resourceCulture);

	internal static string Msg_CompressPictureFail => ResourceManager.GetString("Msg_CompressPictureFail", resourceCulture);

	internal static string Msg_ConfirmClearAllTagsHistory => ResourceManager.GetString("Msg_ConfirmClearAllTagsHistory", resourceCulture);

	internal static string Msg_ConfirmClearTags => ResourceManager.GetString("Msg_ConfirmClearTags", resourceCulture);

	internal static string Msg_ConfirmExtractCovers => ResourceManager.GetString("Msg_ConfirmExtractCovers", resourceCulture);

	internal static string Msg_ConfirmRemoveFiles => ResourceManager.GetString("Msg_ConfirmRemoveFiles", resourceCulture);

	internal static string Msg_ConfirmRenameFiles => ResourceManager.GetString("Msg_ConfirmRenameFiles", resourceCulture);

	internal static string Msg_ConfirmSaveLrcFiles => ResourceManager.GetString("Msg_ConfirmSaveLrcFiles", resourceCulture);

	internal static string Msg_ConfirmSaveTags => ResourceManager.GetString("Msg_ConfirmSaveTags", resourceCulture);

	internal static string Msg_ConfirmUndoRename => ResourceManager.GetString("Msg_ConfirmUndoRename", resourceCulture);

	internal static string Msg_ConfirmUndoTags => ResourceManager.GetString("Msg_ConfirmUndoTags", resourceCulture);

	internal static string Msg_CoverNotFound => ResourceManager.GetString("Msg_CoverNotFound", resourceCulture);

	internal static string Msg_Deletefile => ResourceManager.GetString("Msg_Deletefile", resourceCulture);

	internal static string Msg_DeleteFilesCompleted => ResourceManager.GetString("Msg_DeleteFilesCompleted", resourceCulture);

	internal static string Msg_Downloading => ResourceManager.GetString("Msg_Downloading", resourceCulture);

	internal static string Msg_ErrorMessage => ResourceManager.GetString("Msg_ErrorMessage", resourceCulture);

	internal static string Msg_ExtractCover => ResourceManager.GetString("Msg_ExtractCover", resourceCulture);

	internal static string Msg_ExtractCoverFail => ResourceManager.GetString("Msg_ExtractCoverFail", resourceCulture);

	internal static string Msg_ExtractCoversComplete => ResourceManager.GetString("Msg_ExtractCoversComplete", resourceCulture);

	internal static string Msg_FailedToGetNewVersion => ResourceManager.GetString("Msg_FailedToGetNewVersion", resourceCulture);

	internal static string Msg_FileNotFound => ResourceManager.GetString("Msg_FileNotFound", resourceCulture);

	internal static string Msg_FilesSavedInLocalDir => ResourceManager.GetString("Msg_FilesSavedInLocalDir", resourceCulture);

	internal static string Msg_FilesSavedInSpecPath => ResourceManager.GetString("Msg_FilesSavedInSpecPath", resourceCulture);

	internal static string Msg_FoundNewVersion => ResourceManager.GetString("Msg_FoundNewVersion", resourceCulture);

	internal static string Msg_InitDatabaseFail => ResourceManager.GetString("Msg_InitDatabaseFail", resourceCulture);

	internal static string Msg_InvalidDiscFormat => ResourceManager.GetString("Msg_InvalidDiscFormat", resourceCulture);

	internal static string Msg_InvalidFile => ResourceManager.GetString("Msg_InvalidFile", resourceCulture);

	internal static string Msg_InvalidFileWithNoSupportMultiTrackAudioFile => ResourceManager.GetString("Msg_InvalidFileWithNoSupportMultiTrackAudioFile", resourceCulture);

	internal static string Msg_InvalidFileWithPossiableExt => ResourceManager.GetString("Msg_InvalidFileWithPossiableExt", resourceCulture);

	internal static string Msg_InvalidTrackFormat => ResourceManager.GetString("Msg_InvalidTrackFormat", resourceCulture);

	internal static string Msg_LyricNotFound => ResourceManager.GetString("Msg_LyricNotFound", resourceCulture);

	internal static string Msg_NoSupportExt => ResourceManager.GetString("Msg_NoSupportExt", resourceCulture);

	internal static string Msg_OK_Fail_Count => ResourceManager.GetString("Msg_OK_Fail_Count", resourceCulture);

	internal static string Msg_OK_Fail_Skip_Count => ResourceManager.GetString("Msg_OK_Fail_Skip_Count", resourceCulture);

	internal static string Msg_OpenFileFail => ResourceManager.GetString("Msg_OpenFileFail", resourceCulture);

	internal static string Msg_ParamsInPatternCannotAdjacent => ResourceManager.GetString("Msg_ParamsInPatternCannotAdjacent", resourceCulture);

	internal static string Msg_ParamsInPatternCannotDuplicate => ResourceManager.GetString("Msg_ParamsInPatternCannotDuplicate", resourceCulture);

	internal static string Msg_ParamsInPatternCannotUsePattern0 => ResourceManager.GetString("Msg_ParamsInPatternCannotUsePattern0", resourceCulture);

	internal static string Msg_ParamsInPatternNotFound => ResourceManager.GetString("Msg_ParamsInPatternNotFound", resourceCulture);

	internal static string Msg_PatternCannotBeEmpty => ResourceManager.GetString("Msg_PatternCannotBeEmpty", resourceCulture);

	internal static string Msg_PleaseChooseOperationMode => ResourceManager.GetString("Msg_PleaseChooseOperationMode", resourceCulture);

	internal static string Msg_PleaseChoosePattern => ResourceManager.GetString("Msg_PleaseChoosePattern", resourceCulture);

	internal static string Msg_PleaseInputCustomPattern => ResourceManager.GetString("Msg_PleaseInputCustomPattern", resourceCulture);

	internal static string Msg_PleaseInputRegularexpression => ResourceManager.GetString("Msg_PleaseInputRegularexpression", resourceCulture);

	internal static string Msg_PleaseInputValidExt => ResourceManager.GetString("Msg_PleaseInputValidExt", resourceCulture);

	internal static string Msg_PleaseSelectAtLeastOneItem => ResourceManager.GetString("Msg_PleaseSelectAtLeastOneItem", resourceCulture);

	internal static string Msg_PleaseSelectItem => ResourceManager.GetString("Msg_PleaseSelectItem", resourceCulture);

	internal static string Msg_Readfilefail => ResourceManager.GetString("Msg_Readfilefail", resourceCulture);

	internal static string Msg_Readtag => ResourceManager.GetString("Msg_Readtag", resourceCulture);

	internal static string Msg_Removeitem => ResourceManager.GetString("Msg_Removeitem", resourceCulture);

	internal static string Msg_Rename => ResourceManager.GetString("Msg_Rename", resourceCulture);

	internal static string Msg_RestoreLyricsOrCoverFail => ResourceManager.GetString("Msg_RestoreLyricsOrCoverFail", resourceCulture);

	internal static string Msg_SaveCompleted => ResourceManager.GetString("Msg_SaveCompleted", resourceCulture);

	internal static string Msg_SaveFail => ResourceManager.GetString("Msg_SaveFail", resourceCulture);

	internal static string Msg_SaveFileAs => ResourceManager.GetString("Msg_SaveFileAs", resourceCulture);

	internal static string Msg_SaveingData => ResourceManager.GetString("Msg_SaveingData", resourceCulture);

	internal static string Msg_SaveLrcFile => ResourceManager.GetString("Msg_SaveLrcFile", resourceCulture);

	internal static string Msg_SaveLrcFilesComplete => ResourceManager.GetString("Msg_SaveLrcFilesComplete", resourceCulture);

	internal static string Msg_SaveLrcFilesComplete1 => ResourceManager.GetString("Msg_SaveLrcFilesComplete1", resourceCulture);

	internal static string Msg_Savetag => ResourceManager.GetString("Msg_Savetag", resourceCulture);

	internal static string Msg_Searching => ResourceManager.GetString("Msg_Searching", resourceCulture);

	internal static string Msg_Skipped => ResourceManager.GetString("Msg_Skipped", resourceCulture);

	internal static string Msg_TheLocalDir => ResourceManager.GetString("Msg_TheLocalDir", resourceCulture);

	internal static string Msg_UndoCompleted => ResourceManager.GetString("Msg_UndoCompleted", resourceCulture);

	internal static string Msg_UsingLastestVersion => ResourceManager.GetString("Msg_UsingLastestVersion", resourceCulture);

	internal static string Msg_WantAllowRemoveReadonlyAttribute => ResourceManager.GetString("Msg_WantAllowRemoveReadonlyAttribute", resourceCulture);

	internal static string Msg_WriteCoverFileFail => ResourceManager.GetString("Msg_WriteCoverFileFail", resourceCulture);

	internal static string Msg_WriteLrcFileFail => ResourceManager.GetString("Msg_WriteLrcFileFail", resourceCulture);

	internal static Bitmap no_cover => (Bitmap)ResourceManager.GetObject("no_cover", resourceCulture);

	internal static Bitmap no_cover2X => (Bitmap)ResourceManager.GetObject("no_cover2X", resourceCulture);

	internal static string OK => ResourceManager.GetString("OK", resourceCulture);

	internal static string OkAndSave => ResourceManager.GetString("OkAndSave", resourceCulture);

	internal static string OpenCover => ResourceManager.GetString("OpenCover", resourceCulture);

	internal static string options => ResourceManager.GetString("options", resourceCulture);

	internal static Bitmap optionsToolStripMenuItem_Image => (Bitmap)ResourceManager.GetObject("optionsToolStripMenuItem_Image", resourceCulture);

	internal static Bitmap optionsToolStripMenuItem_Image2X => (Bitmap)ResourceManager.GetObject("optionsToolStripMenuItem_Image2X", resourceCulture);

	internal static string overwrite => ResourceManager.GetString("overwrite", resourceCulture);

	internal static string OverwriteOptions => ResourceManager.GetString("OverwriteOptions", resourceCulture);

	internal static Bitmap picSrcToolStripMenuItem_Image => (Bitmap)ResourceManager.GetObject("picSrcToolStripMenuItem_Image", resourceCulture);

	internal static Bitmap picSrcToolStripMenuItem_Image2X => (Bitmap)ResourceManager.GetObject("picSrcToolStripMenuItem_Image2X", resourceCulture);

	internal static string picture => ResourceManager.GetString("picture", resourceCulture);

	internal static string PictureFormatLimitsEntries => ResourceManager.GetString("PictureFormatLimitsEntries", resourceCulture);

	internal static string PictureFormatLimitsKeys => ResourceManager.GetString("PictureFormatLimitsKeys", resourceCulture);

	internal static string PolicyTokenExporter => ResourceManager.GetString("MusicTagWinApp.Exporters.PolicyTokenExporter", resourceCulture);

	internal static Bitmap qrcode_alipay => (Bitmap)ResourceManager.GetObject("qrcode_alipay", resourceCulture);

	internal static Bitmap qrcode_wechat => (Bitmap)ResourceManager.GetObject("qrcode_wechat", resourceCulture);

	internal static Bitmap readTagsToolStripMenuItem_Image => (Bitmap)ResourceManager.GetObject("readTagsToolStripMenuItem_Image", resourceCulture);

	internal static Bitmap readTagsToolStripMenuItem_Image2X => (Bitmap)ResourceManager.GetObject("readTagsToolStripMenuItem_Image2X", resourceCulture);

	internal static Bitmap refreshToolStripMenuItem_Image => (Bitmap)ResourceManager.GetObject("refreshToolStripMenuItem_Image", resourceCulture);

	internal static Bitmap refreshToolStripMenuItem_Image2X => (Bitmap)ResourceManager.GetObject("refreshToolStripMenuItem_Image2X", resourceCulture);

	internal static Bitmap removeTagToolStripMenuItem_Image => (Bitmap)ResourceManager.GetObject("removeTagToolStripMenuItem_Image", resourceCulture);

	internal static Bitmap removeTagToolStripMenuItem_Image2X => (Bitmap)ResourceManager.GetObject("removeTagToolStripMenuItem_Image2X", resourceCulture);

	internal static string samplerate => ResourceManager.GetString("samplerate", resourceCulture);

	internal static string SaveAsLrc => ResourceManager.GetString("SaveAsLrc", resourceCulture);

	internal static string SaveToFile => ResourceManager.GetString("SaveToFile", resourceCulture);

	internal static Bitmap saveToolStripMenuItem_Image => (Bitmap)ResourceManager.GetObject("saveToolStripMenuItem_Image", resourceCulture);

	internal static Bitmap saveToolStripMenuItem_Image2X => (Bitmap)ResourceManager.GetObject("saveToolStripMenuItem_Image2X", resourceCulture);

	internal static string SaveToTag => ResourceManager.GetString("SaveToTag", resourceCulture);

	internal static string SaveToTagAndFile => ResourceManager.GetString("SaveToTagAndFile", resourceCulture);

	internal static string search => ResourceManager.GetString("search", resourceCulture);

	internal static Bitmap selallfilesToolStripMenuItem_Image => (Bitmap)ResourceManager.GetObject("selallfilesToolStripMenuItem_Image", resourceCulture);

	internal static Bitmap selallfilesToolStripMenuItem_Image2X => (Bitmap)ResourceManager.GetObject("selallfilesToolStripMenuItem_Image2X", resourceCulture);

	internal static string source => ResourceManager.GetString("source", resourceCulture);

	internal static string subdirectories => ResourceManager.GetString("subdirectories", resourceCulture);

	internal static string tag => ResourceManager.GetString("tag", resourceCulture);

	internal static string TagsHistory => ResourceManager.GetString("TagsHistory", resourceCulture);

	internal static Bitmap tagsHistoryToolStripMenuItem_Image => (Bitmap)ResourceManager.GetObject("tagsHistoryToolStripMenuItem_Image", resourceCulture);

	internal static Bitmap tagsHistoryToolStripMenuItem_Image2X => (Bitmap)ResourceManager.GetObject("tagsHistoryToolStripMenuItem_Image2X", resourceCulture);

	internal static string tagtypes => ResourceManager.GetString("tagtypes", resourceCulture);

	internal static byte[] tcmap => (byte[])ResourceManager.GetObject("tcmap", resourceCulture);

	internal static string title => ResourceManager.GetString("title", resourceCulture);

	internal static string trackstr => ResourceManager.GetString("trackstr", resourceCulture);

	internal static byte[] tsmap => (byte[])ResourceManager.GetObject("tsmap", resourceCulture);

	internal static Bitmap undoToolStripMenuItem_Image => (Bitmap)ResourceManager.GetObject("undoToolStripMenuItem_Image", resourceCulture);

	internal static Bitmap undoToolStripMenuItem_Image2X => (Bitmap)ResourceManager.GetObject("undoToolStripMenuItem_Image2X", resourceCulture);

	internal static string Unlimited => ResourceManager.GetString("Unlimited", resourceCulture);

	internal static Bitmap unselectAllToolStripMenuItem_Image => (Bitmap)ResourceManager.GetObject("unselectAllToolStripMenuItem_Image", resourceCulture);

	internal static Bitmap unselectAllToolStripMenuItem_Image2X => (Bitmap)ResourceManager.GetObject("unselectAllToolStripMenuItem_Image2X", resourceCulture);

	internal static string updatetime => ResourceManager.GetString("updatetime", resourceCulture);

	internal static string version => ResourceManager.GetString("version", resourceCulture);

	internal static string Warning => ResourceManager.GetString("Warning", resourceCulture);

	internal static string WebSearchThreadCount => ResourceManager.GetString("WebSearchThreadCount", resourceCulture);

	internal static string WechatPay => ResourceManager.GetString("WechatPay", resourceCulture);

	internal static string WriteMode => ResourceManager.GetString("WriteMode", resourceCulture);

	internal static string year => ResourceManager.GetString("year", resourceCulture);
}
