using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MusicTag.Consumers;
using MusicTag.Mocks;
using MusicTag.Readers;
using MusicTagWinApp.Adapter;
using MusicTagWinApp.Exporters;
using MusicTagWinApp.Instances;
using MusicTagWinApp.Listeners;
using MusicTagWinApp.Properties;
using MusicTagWinApp.Roles;
using MusicTagWinApp.Stubs;
using MusicTagWinApp.Web;

namespace MusicTag.Importers;

internal class OptionsDialog : Form
{
	private readonly ComponentResourceManager dialogResources;

	private readonly List<int> pictureSizeLimitOptions;

	private readonly List<int> pictureResolutionLimitOptions;

	private readonly List<int> durationFilterOptions;

	private readonly List<string> itunesCountryCodes;

	private readonly Dictionary<SourceItem, int> searchResultLimitsBySource;

	private const string DefaultLrcFileEncodings = "UTF-8|UTF-16|GBK|GB18030|GB2312|BIG5";

	private const string DefaultLrcFilenameFormatDescriptions = "Same as song file name|Artist - Title|Title - Artist";

	private const string DefaultLrcFilenameFormatValues = "SameAsSongFileName|Artist_Title|Title_Artist";

	private const string DefaultPictureFormatLimitEntries = "Auto|Fixed to jpg format";

	private const string DefaultPictureFormatLimitValues = "AUTO|JPG";

	public bool FileFilterSettingsChanged { get; private set; }

	public bool NotifyAreaSettingsChanged { get; private set; }

	public static readonly string[] BuiltInLyricTranslationSeparators = new string[11]
	{
		" ", " - ", " / ", " | ", "（）", "「」", "『』", "〖〗", "【】", "; ",
		", "
	};

	private IContainer components;

	private FlowLayoutPanel rootLayoutPanel;

	private FlowLayoutPanel navigationPanel;

	private FlowLayoutPanel detailsPanel;

	private FlowLayoutPanel commandButtonPanel;

	private Button okButton;

	private Button cancelButton;

	private SplitContainer mainSplitContainer;

	private TreeView optionsTreeView;

	private FlowLayoutPanel sourceOrderPanel;

	private SourceOrderControl coverSourceOrderControl;

	private SourceOrderControl lyricSourceOrderControl;

	private SourceOrderControl tagSourceOrderControl;

	private GroupBox translatedLyricGroupBox;

	private FlowLayoutPanel lyricCleanupOptionsPanel;

	private CheckBox reformatTimestampCheckBox;

	private CheckBox removeTimestampCheckBox;

	private CheckBox removeBlankLyricLinesCheckBox;

	private CheckBox removeLyricHeaderTagsCheckBox;

	private FlowLayoutPanel translatedLyricOptionsPanel;

	private CheckBox downloadTranslatedLyricsCheckBox;

	private CheckBox skipOriginalLyricCheckBox;

	private Label translatedLyricFormatLabel;

	private FlowLayoutPanel translatedLyricFormatPanel;

	private RadioButton translatedLyricFormat1RadioButton;

	private RadioButton translatedLyricFormat2RadioButton;

	private RadioButton translatedLyricFormat3RadioButton;

	private Label chineseConversionLabel;

	private FlowLayoutPanel chineseConversionPanel;

	private RadioButton noChineseConversionRadioButton;

	private RadioButton traditionalToSimplifiedRadioButton;

	private RadioButton simplifiedToTraditionalRadioButton;

	private FlowLayoutPanel searchAndTagOptionsPanel;

	private Label webSearchCriteriaLabel;

	private FlowLayoutPanel webSearchCriteriaPanel;

	private CheckBox titleSearchEnabledCheckBox;

	private ComboBox titleSearchModeComboBox;

	private CheckBox artistSearchConditionCheckBox;

	private CheckBox albumSearchConditionCheckBox;

	private Label pictureSizeLimitLabel;

	private TrackBar pictureSizeLimitTrackBar;

	private FlowLayoutPanel id3v2VersionPanel;

	private Label id3v2VersionLabel;

	private RadioButton id3v24RadioButton;

	private RadioButton id3v23RadioButton;

	private GroupBox itunesParametersGroupBox;

	private FlowLayoutPanel itunesParametersPanel;

	private FlowLayoutPanel itunesCountryPanel;

	private Label itunesCountryLabel;

	private ComboBox itunesCountryComboBox;

	private Label fileFilterLabel;

	private FlowLayoutPanel fileFilterPanel;

	private Label durationFilterLabel;

	private ComboBox durationFilterComboBox;

	private CheckBox ignoreVideoFilesCheckBox;

	private FlowLayoutPanel sourceLimitPanel;

	private Label webSearchItemLimitLabel;

	private TrackBar webSearchItemLimitTrackBar;

	private ToolTip optionsToolTip;

	private GroupBox webSearchLimitGroupBox;

	private FlowLayoutPanel webSearchLimitPanel;

	private Label coverSourceLimitLabel;

	private TrackBar coverSourceLimitTrackBar;

	private Label lyricSourceLimitLabel;

	private TrackBar lyricSourceLimitTrackBar;

	private Label tagSourceLimitLabel;

	private TrackBar tagSourceLimitTrackBar;

	private Label lyricTranslationSeparatorLabel;

	private ComboBox lyricTranslationSeparatorComboBox;

	private Label pictureFormatLimitLabel;

	private ComboBox pictureFormatLimitComboBox;

	private Label artistConnectorLabel;

	private ComboBox artistConnectorComboBox;

	private FlowLayoutPanel saveAndNotificationOptionsPanel;

	private GroupBox lrcFileGroupBox;

	private FlowLayoutPanel lrcFileOptionsPanel;

	private FlowLayoutPanel lrcEncodingPanel;

	private Label lrcEncodingLabel;

	private ComboBox lrcEncodingComboBox;

	private FlowLayoutPanel lrcDirectoryPanel;

	private Label lrcDirectoryLabel;

	private TextBox lrcDirectoryTextBox;

	private Button browseLrcDirectoryButton;

	private Button useLocalLrcDirectoryButton;

	private FlowLayoutPanel lrcFilenameFormatPanel;

	private Label lrcFilenameFormatLabel;

	private ComboBox lrcFilenameFormatComboBox;

	private CheckBox saveLrcWhileSavingTagsCheckBox;

	private FlowLayoutPanel restrictedExtensionsPanel;

	private Label restrictedExtensionsLabel;

	private FlowLayoutPanel restrictedExtensionsEditorPanel;

	private TextBox restrictedExtensionsTextBox;

	private Button resetRestrictedExtensionsButton;

	private CheckBox keepFileUpdateTimeCheckBox;

	private CheckBox checkForUpdatesOnStartupCheckBox;

	private FlowLayoutPanel tagHistoryPanel;

	private Button clearAllTagHistoryButton;

	private TrackBar pictureResolutionLimitTrackBar;

	private Label pictureResolutionLimitLabel;

	private GroupBox notifyAreaGroupBox;

	private FlowLayoutPanel notifyAreaOptionsPanel;

	private CheckBox alwaysShowNotifyIconCheckBox;

	private CheckBox minimizeToNotifyAreaCheckBox;

	private Label commentTagLabel;

	private CheckBox writeNetEaseCommentKeyCheckBox;

	private RadioButton translatedLyricFormat4RadioButton;

	public OptionsDialog()
	{
		dialogResources = new ComponentResourceManager(typeof(StateFieldInstance));
		pictureSizeLimitOptions = new List<int>();
		pictureResolutionLimitOptions = new List<int>();
		durationFilterOptions = new List<int>();
		itunesCountryCodes = new List<string>();
		searchResultLimitsBySource = new Dictionary<SourceItem, int>();
		InitializeComponent();
		optionsTreeView.ExpandAll();
		base.Width = MinimumSize.Width;
		base.Height = MinimumSize.Height;
		sourceOrderPanel.WrapContents = false;
		mainSplitContainer.SplitterDistance = DatabaseMapper.ScaleByDpi(120f);
		AddSourceTreeNodes();
		ApplyLocalizedText();
		LoadSavedOptions();
		UpdateResponsiveLayout();
	}

	private void AddSourceTreeNodes()
	{
		new SearchSource[8]
		{
			SearchSource.Music163,
			SearchSource.QQ,
			SearchSource.Kugou,
			SearchSource.Kuwo,
			SearchSource.ITunes,
			SearchSource.Lastfm,
			SearchSource.Brainz,
			SearchSource.Vgmdb
			}.ForEachItem(AddSourceTreeNode);
	}

	private void ApplyLocalizedText()
	{
		Text = Resources.options;
		okButton.Text = Resources.OK;
		cancelButton.Text = Resources.Cancel;
		foreach (TreeNode treeNode in optionsTreeView.Nodes)
		{
			treeNode.Text = GetDialogText(treeNode.Name, treeNode.Text);
		}
		coverSourceOrderControl.Title = GetDialogText("panelTagSrcPicture", "Cover sources");
		lyricSourceOrderControl.Title = GetDialogText("panelTagSrcLyric", "Lyric sources");
		tagSourceOrderControl.Title = GetDialogText("panelTagSrcComb", "Tag sources");
		itunesParametersGroupBox.Text = GetDialogText("panelTagSrcItunesParams", itunesParametersGroupBox.Text);
		translatedLyricGroupBox.Text = GetDialogText("panelDownloadTrans", translatedLyricGroupBox.Text);
		downloadTranslatedLyricsCheckBox.Text = Resources.Enable;
		skipOriginalLyricCheckBox.Text = GetDialogText("cbDontDownloadOrigLyric", skipOriginalLyricCheckBox.Text);
		translatedLyricFormatLabel.Text = GetDialogText("lblLyricFormatForDownloadTrans", translatedLyricFormatLabel.Text);
		translatedLyricFormat1RadioButton.Text = GetDialogText("rbDlTransLyFmt1", translatedLyricFormat1RadioButton.Text);
		translatedLyricFormat2RadioButton.Text = GetDialogText("rbDlTransLyFmt2", translatedLyricFormat2RadioButton.Text);
		translatedLyricFormat3RadioButton.Text = GetDialogText("rbDlTransLyFmt3", translatedLyricFormat3RadioButton.Text);
		translatedLyricFormat4RadioButton.Text = GetDialogText("rbDlTransLyFmt4", translatedLyricFormat4RadioButton.Text);
		chineseConversionLabel.Text = GetDialogText("lblCHSCHTConv", chineseConversionLabel.Text);
		noChineseConversionRadioButton.Text = GetDialogText("rbZhConvUndefine", noChineseConversionRadioButton.Text);
		traditionalToSimplifiedRadioButton.Text = GetDialogText("rbZhConvCHTToCHS", traditionalToSimplifiedRadioButton.Text);
		simplifiedToTraditionalRadioButton.Text = GetDialogText("rbZhConvCHSToCHT", simplifiedToTraditionalRadioButton.Text);
		lyricTranslationSeparatorLabel.Text = GetDialogText("lblConnectorsLyricAndTLyric", lyricTranslationSeparatorLabel.Text);
		reformatTimestampCheckBox.Text = GetDialogText("cbLyricDlReformatTimetag", reformatTimestampCheckBox.Text);
		removeTimestampCheckBox.Text = GetDialogText("cbLyricDlRemoveTimetag", removeTimestampCheckBox.Text);
		removeBlankLyricLinesCheckBox.Text = GetDialogText("cbLyricDlDeletelinesofblanktext", removeBlankLyricLinesCheckBox.Text);
		removeLyricHeaderTagsCheckBox.Text = GetDialogText("cbLyricDlDeleteheadtags", removeLyricHeaderTagsCheckBox.Text);
		webSearchCriteriaLabel.Text = GetDialogText("lblWebSearchCriteria", webSearchCriteriaLabel.Text);
		titleSearchModeComboBox.Items.Add(GetDialogText("cbWebSearchConditionTitle.TitleOrFilename", "Title or filename"));
		titleSearchModeComboBox.Items.Add(GetDialogText("cbWebSearchConditionTitle.Filename", "Filename"));
		artistSearchConditionCheckBox.Text = Resources.artist;
		albumSearchConditionCheckBox.Text = Resources.album;
		titleSearchModeComboBox.SelectedIndex = 0;
		pictureSizeLimitLabel.Text = GetDialogText("lblPictureSizeLimits", pictureSizeLimitLabel.Text);
		pictureResolutionLimitLabel.Text = GetDialogText("lblPictureResolution", pictureResolutionLimitLabel.Text);
		pictureFormatLimitLabel.Text = GetDialogText("lblPictureFormatLimits", pictureFormatLimitLabel.Text);
		artistConnectorLabel.Text = GetDialogText("lblConnectorsArtists", artistConnectorLabel.Text);
		fileFilterLabel.Text = GetDialogText("lblFileFilter", fileFilterLabel.Text);
		durationFilterLabel.Text = GetDialogText("lblFileFilterByDuration", durationFilterLabel.Text);
		ignoreVideoFilesCheckBox.Text = GetDialogText("cbFileFilterIgnoreVideoFile", ignoreVideoFilesCheckBox.Text);
		lrcEncodingLabel.Text = GetDialogText("lblLrcEncoding", lrcEncodingLabel.Text);
		itunesCountryLabel.Text = GetDialogText("lblItunesCountry", itunesCountryLabel.Text);
		keepFileUpdateTimeCheckBox.Text = GetDialogText("cbKeepFileUpdateTime", keepFileUpdateTimeCheckBox.Text);
		saveLrcWhileSavingTagsCheckBox.Text = GetDialogText("cbSaveLrcFileWhileSaveTags", saveLrcWhileSavingTagsCheckBox.Text);
		webSearchLimitGroupBox.Text = GetDialogText("gbWebSearchLimit", webSearchLimitGroupBox.Text);
		coverSourceLimitLabel.Text = GetDialogText("lblWSILPictureSources", coverSourceLimitLabel.Text);
		lyricSourceLimitLabel.Text = GetDialogText("lblWSILLyricSources", lyricSourceLimitLabel.Text);
		tagSourceLimitLabel.Text = GetDialogText("lblWSILCombSources", tagSourceLimitLabel.Text);
		commentTagLabel.Text = GetDialogText("lblCommentTag", commentTagLabel.Text);
		writeNetEaseCommentKeyCheckBox.Text = GetDialogText("cbCommentTagWrite163Key", writeNetEaseCommentKeyCheckBox.Text);
		lrcFileGroupBox.Text = GetDialogText("gbLrcFile", lrcFileGroupBox.Text);
		lrcDirectoryLabel.Text = GetDialogText("lblLrcSaveDir", lrcDirectoryLabel.Text);
		lrcFilenameFormatLabel.Text = GetDialogText("lblLrcFilenameFormat", lrcFilenameFormatLabel.Text);
		notifyAreaGroupBox.Text = GetDialogText("gbNotifyArea", notifyAreaGroupBox.Text);
		alwaysShowNotifyIconCheckBox.Text = GetDialogText("cbAlwaysShowIconInNofiArea", alwaysShowNotifyIconCheckBox.Text);
		minimizeToNotifyAreaCheckBox.Text = GetDialogText("cbMinimizeToNotiArea", minimizeToNotifyAreaCheckBox.Text);
		restrictedExtensionsLabel.Text = GetDialogText("lblRestrictFileExts", restrictedExtensionsLabel.Text);
		resetRestrictedExtensionsButton.Text = GetDialogText("btnResetRestrictFileExts", resetRestrictedExtensionsButton.Text);
		checkForUpdatesOnStartupCheckBox.Text = GetDialogText("cbCheckForUpdatesOnStartup", checkForUpdatesOnStartupCheckBox.Text);
		clearAllTagHistoryButton.Text = GetDialogText("btnClearAllTagsHistory", clearAllTagHistoryButton.Text);
		optionsToolTip.SetToolTip(webSearchItemLimitTrackBar, dialogResources.GetString("tbWebSearchLimitTip"));
		optionsToolTip.SetToolTip(coverSourceLimitTrackBar, dialogResources.GetString("tbWebSearchLimitTip"));
		optionsToolTip.SetToolTip(lyricSourceLimitTrackBar, dialogResources.GetString("tbWebSearchLimitTip"));
		optionsToolTip.SetToolTip(tagSourceLimitTrackBar, dialogResources.GetString("tbWebSearchLimitTip"));
		optionsToolTip.SetToolTip(lyricTranslationSeparatorComboBox, dialogResources.GetString("cbConnectorsLyricAndTLyricTip"));
		optionsToolTip.SetToolTip(browseLrcDirectoryButton, dialogResources.GetString("btnLrcSaveDirTip"));
		optionsToolTip.SetToolTip(useLocalLrcDirectoryButton, dialogResources.GetString("btnLrcSaveLocalDirTip"));
		DatabaseMapper.SetTextBoxCueBanner(lrcDirectoryTextBox, dialogResources.GetString("tbLrcSaveDirHint"));
	}

	private string GetDialogText(string resourceName, string fallbackText)
	{
		string resourceText = dialogResources.GetString(resourceName);
		return string.IsNullOrEmpty(resourceText) ? fallbackText : resourceText;
	}

	private static string GetResourceText(string resourceText, string fallbackText)
	{
		return string.IsNullOrEmpty(resourceText) ? fallbackText : resourceText;
	}

	private void LoadSavedOptions()
	{
		coverSourceOrderControl.SetSources(CoverSearchResult.GetSortedCoverSourceSettings());
		lyricSourceOrderControl.SetSources(LyricSearchResult.GetSortedLyricSourceSettings());
		tagSourceOrderControl.SetSources(TrackSearchResult.GetSortedTagSourceSettings());

		webSearchItemLimitTrackBar.Value = Settings.Default.WebSearchItemsLimit;
		UpdateWebSearchLimitLabel(null, null);
		downloadTranslatedLyricsCheckBox.Checked = Settings.Default.LyricDownload_DownloadTrans_Enable;
		skipOriginalLyricCheckBox.Checked = Settings.Default.LyricDownload_DownloadTrans_DontDownloadOrigLyric;
		switch (Settings.Default.LyricDownload_DownloadTrans_LyricFormat)
		{
		case 1:
			translatedLyricFormat2RadioButton.Checked = true;
			break;
		case 2:
			translatedLyricFormat3RadioButton.Checked = true;
			break;
		case 3:
			translatedLyricFormat4RadioButton.Checked = true;
			break;
		default:
			translatedLyricFormat1RadioButton.Checked = true;
			break;
		}
		switch (Settings.Default.LyricDownload_DownloadTrans_ChineseConvMode)
		{
		case 1:
			traditionalToSimplifiedRadioButton.Checked = true;
			break;
		case 2:
			simplifiedToTraditionalRadioButton.Checked = true;
			break;
		default:
			noChineseConversionRadioButton.Checked = true;
			break;
		}
		UpdateTranslatedLyricControlsEnabled(null, null);

		lyricTranslationSeparatorComboBox.Items.Clear();
		lyricTranslationSeparatorComboBox.Items.AddRange(BuiltInLyricTranslationSeparators);
		lyricTranslationSeparatorComboBox.Text = DatabaseMapper.CoalesceNonBlank(Settings.Default.ConnectorsLyricAndTLyric, " ");
		reformatTimestampCheckBox.Checked = Settings.Default.LyricDownload_ReformatTimetag;
		removeTimestampCheckBox.Checked = Settings.Default.LyricDownload_RemoveTimetag;
		removeBlankLyricLinesCheckBox.Checked = Settings.Default.LyricDownload_DeleteLinesOfBlankText;
		removeLyricHeaderTagsCheckBox.Checked = Settings.Default.LyricDownload_DeleteHeadTag;
		titleSearchModeComboBox.SelectedIndex = Settings.Default.SearchCondition_UseOnlyFilename ? 1 : 0;
		artistSearchConditionCheckBox.Checked = Settings.Default.SearchCondition_UseArtist;
		albumSearchConditionCheckBox.Checked = Settings.Default.SearchCondition_UseAlbum;

		int selectedPictureSizeIndex = 0;
		for (int sizeLimit = 20; sizeLimit <= 10000; sizeLimit = (sizeLimit >= 100) ? (sizeLimit + 100) : (sizeLimit + 20))
		{
			if (sizeLimit == Settings.Default.PictureSizeLimitsKB)
			{
				selectedPictureSizeIndex = pictureSizeLimitOptions.Count;
			}
			pictureSizeLimitOptions.Add(sizeLimit);
		}
		pictureSizeLimitTrackBar.Minimum = 0;
		pictureSizeLimitTrackBar.Maximum = pictureSizeLimitOptions.Count - 1;
		pictureSizeLimitTrackBar.Value = selectedPictureSizeIndex;
		UpdatePictureSizeLimitLabel(null, null);

		int selectedResolutionIndex = 0;
		for (int resolutionLimit = 0; resolutionLimit <= 4000; resolutionLimit = (resolutionLimit != 0) ? (resolutionLimit + 10) : (resolutionLimit + 100))
		{
			if (resolutionLimit == Settings.Default.PictureResolutionLimits)
			{
				selectedResolutionIndex = pictureResolutionLimitOptions.Count;
			}
			pictureResolutionLimitOptions.Add(resolutionLimit);
		}
		pictureResolutionLimitTrackBar.Minimum = 0;
		pictureResolutionLimitTrackBar.Maximum = pictureResolutionLimitOptions.Count - 1;
		pictureResolutionLimitTrackBar.Value = selectedResolutionIndex;
		UpdatePictureResolutionLimitLabel(null, null);

		artistConnectorComboBox.Text = Settings.Default.ConnectorsArtists;
		lrcEncodingComboBox.Items.AddRange(GetResourceText(Resources.LrcFileEncodings, DefaultLrcFileEncodings).Split('|'));
		lrcEncodingComboBox.SelectedIndex = 0;
		lrcEncodingComboBox.SelectedItem = Settings.Default.SaveLrcFileDefaultEncoding;
		if (Settings.Default.ID3v2Version == 3)
		{
			id3v23RadioButton.Checked = true;
		}
		else
		{
			id3v24RadioButton.Checked = true;
		}

		foreach (string durationFilterEntry in Resources.FileFilterByDurationList.Split(';'))
		{
			string[] durationFilterParts = durationFilterEntry.Split('|');
			durationFilterComboBox.Items.Add(durationFilterParts[0]);
			durationFilterOptions.Add(int.Parse(durationFilterParts[1]));
		}
		int durationFilterIndex = durationFilterOptions.IndexOf(Settings.Default.FileFilterByDuration);
		durationFilterComboBox.SelectedIndex = durationFilterIndex >= 0 ? durationFilterIndex : 0;
		ignoreVideoFilesCheckBox.Checked = Settings.Default.FileFilterIgnoreVideoFile;

		foreach (string countryEntry in Resources.CountryList.Split(';'))
		{
			string[] countryParts = countryEntry.Split('|');
			string countryCode = countryParts[0];
			string countryName = countryParts[1];
			itunesCountryCodes.Add(countryCode);
			itunesCountryComboBox.Items.Add(countryCode + " - " + countryName);
		}
		int countryIndex = itunesCountryCodes.IndexOf(Settings.Default.ItunesSearchParams_Country);
		itunesCountryComboBox.SelectedIndex = countryIndex >= 0 ? countryIndex : 0;

		keepFileUpdateTimeCheckBox.Checked = Settings.Default.SaveTagsKeepUpdateTime;
		saveLrcWhileSavingTagsCheckBox.Checked = Settings.Default.SaveLrcWhileSaveTags;
		checkForUpdatesOnStartupCheckBox.Checked = Settings.Default.CheckForUpdatesOnStartup;
		writeNetEaseCommentKeyCheckBox.Checked = Settings.Default.CommentTagWrite163Key;
		lrcFilenameFormatComboBox.Items.AddRange(GetResourceText(Resources.LrcFilenameFormatDesc, DefaultLrcFilenameFormatDescriptions).Split('|'));
		int lyricFilenameFormatIndex = GetResourceText(Resources.LrcFilenameFormat, DefaultLrcFilenameFormatValues)
			.Split('|')
			.ToList()
			.IndexOf(Settings.Default.SaveLrcFilenameFormat);
		lrcFilenameFormatComboBox.SelectedIndex = lyricFilenameFormatIndex >= 0 ? lyricFilenameFormatIndex : 0;
		lrcDirectoryTextBox.Text = Settings.Default.SaveLrcDirectory;
		alwaysShowNotifyIconCheckBox.Checked = Settings.Default.AlwaysShowIconInNofiArea;
		minimizeToNotifyAreaCheckBox.Checked = Settings.Default.MinimizeToNotiArea;
		restrictedExtensionsTextBox.Text = Settings.Default.RestrictFileExts;

		pictureFormatLimitComboBox.Items.AddRange(GetResourceText(Resources.PictureFormatLimitsEntries, DefaultPictureFormatLimitEntries).Split('|'));
		int pictureFormatLimitIndex = GetResourceText(Resources.PictureFormatLimitsKeys, DefaultPictureFormatLimitValues)
			.Split('|')
			.ToList()
			.IndexOf(Settings.Default.PictureFormatLimits);
		pictureFormatLimitComboBox.SelectedIndex = pictureFormatLimitIndex >= 0 ? pictureFormatLimitIndex : 0;
	}

	protected override void OnShown(EventArgs e)
	{
		base.OnShown(e);
		int requiredHeightIncrease = sourceLimitPanel.Location.Y + sourceLimitPanel.Height - sourceOrderPanel.Height;
		if (requiredHeightIncrease > 0)
		{
			base.Height += requiredHeightIncrease;
		}
		UpdateTranslatedLyricConnectorEnabled();
	}

	private void OptionsTreeSelectionChanged(object sender, TreeViewEventArgs e)
	{
		coverSourceOrderControl.Hide();
		lyricSourceOrderControl.Hide();
		tagSourceOrderControl.Hide();
		sourceLimitPanel.Hide();
		itunesParametersGroupBox.Hide();
		webSearchLimitGroupBox.Hide();
		translatedLyricGroupBox.Hide();
		lyricCleanupOptionsPanel.Hide();
		searchAndTagOptionsPanel.Hide();
		saveAndNotificationOptionsPanel.Hide();

		string nodeName = e.Node.Name;
		switch (nodeName)
		{
		case "TagSources":
			coverSourceOrderControl.Show();
			lyricSourceOrderControl.Show();
			tagSourceOrderControl.Show();
			sourceLimitPanel.Show();
			return;
		case "LyricDownload":
			translatedLyricGroupBox.Show();
			lyricCleanupOptionsPanel.Show();
			return;
		case "TagSrcITunes":
			itunesParametersGroupBox.Show();
			break;
		case "Others":
			searchAndTagOptionsPanel.Show();
			return;
		case "Others1":
			saveAndNotificationOptionsPanel.Show();
			return;
		default:
			if (!nodeName.StartsWith("TagSrc"))
			{
				return;
			}
			break;
		}

		string sourceName = nodeName.Substring(6);
		SourceItem coverSource = FindSourceItemByName(CoverSearchResult.GetCoverSourceSettings(), sourceName);
		SourceItem lyricSource = FindSourceItemByName(LyricSearchResult.GetLyricSourceSettings(), sourceName);
		SourceItem tagSource = FindSourceItemByName(TrackSearchResult.GetTagSourceSettings(), sourceName);

		coverSourceLimitTrackBar.Enabled = coverSource != null;
		lyricSourceLimitTrackBar.Enabled = lyricSource != null;
		tagSourceLimitTrackBar.Enabled = tagSource != null;
		coverSourceLimitTrackBar.Tag = coverSource;
		lyricSourceLimitTrackBar.Tag = lyricSource;
		tagSourceLimitTrackBar.Tag = tagSource;
		coverSourceLimitLabel.Enabled = coverSourceLimitTrackBar.Enabled;
		lyricSourceLimitLabel.Enabled = lyricSourceLimitTrackBar.Enabled;
		tagSourceLimitLabel.Enabled = tagSourceLimitTrackBar.Enabled;

		coverSourceLimitTrackBar.Value = GetSearchResultLimit(coverSource);
		lyricSourceLimitTrackBar.Value = GetSearchResultLimit(lyricSource);
		tagSourceLimitTrackBar.Value = GetSearchResultLimit(tagSource);
		UpdateCoverSearchLimit(null, null);
		UpdateLyricSearchLimit(null, null);
		UpdateTagSearchLimit(null, null);
		webSearchLimitGroupBox.Show();
	}

	private void ResizeSettingsLayout(object sender, EventArgs e)
	{
		UpdateResponsiveLayout();
	}

	private void UpdateResponsiveLayout()
	{
		int availableRootWidth = rootLayoutPanel.Width - rootLayoutPanel.Padding.Left - rootLayoutPanel.Padding.Right;
		int availableRootHeight = rootLayoutPanel.Height - rootLayoutPanel.Padding.Top - rootLayoutPanel.Padding.Bottom;

		mainSplitContainer.Width = availableRootWidth;
		navigationPanel.Width = availableRootWidth;
		detailsPanel.Width = availableRootWidth;
		mainSplitContainer.Height = availableRootHeight - detailsPanel.Height;
		navigationPanel.Height = mainSplitContainer.Height;

		Padding padding = commandButtonPanel.Margin;
		commandButtonPanel.Margin = new Padding(detailsPanel.Width - commandButtonPanel.Width, padding.Top, padding.Right, padding.Bottom);

		int availableContentWidth = sourceOrderPanel.Width - sourceOrderPanel.Padding.Left - sourceOrderPanel.Padding.Right;
		webSearchLimitGroupBox.Width = availableContentWidth;
		webSearchItemLimitTrackBar.Width = availableContentWidth;
		sourceLimitPanel.Width = availableContentWidth;
		itunesParametersGroupBox.Width = availableContentWidth;
		tagSourceOrderControl.Width = availableContentWidth;
		lyricSourceOrderControl.Width = availableContentWidth;
		coverSourceOrderControl.Width = availableContentWidth;
		translatedLyricGroupBox.Width = availableContentWidth;
		lrcFileGroupBox.Width = availableContentWidth;
		notifyAreaGroupBox.Width = availableContentWidth;
		searchAndTagOptionsPanel.Width = availableContentWidth;
		saveAndNotificationOptionsPanel.Width = availableContentWidth;

		int availableWebSearchLimitWidth = webSearchLimitPanel.Width - webSearchLimitPanel.Padding.Left - webSearchLimitPanel.Padding.Right;
		coverSourceLimitTrackBar.Width = availableWebSearchLimitWidth;
		lyricSourceLimitTrackBar.Width = availableWebSearchLimitWidth;
		tagSourceLimitTrackBar.Width = availableWebSearchLimitWidth;
		webSearchLimitGroupBox.Height = tagSourceLimitTrackBar.Location.Y + tagSourceLimitTrackBar.Height + DatabaseMapper.ScaleByDpi(20f);
	}

	private void SaveOptionsAndClose(object sender, EventArgs e)
	{
		string[] restrictedExtensions = restrictedExtensionsTextBox.Text.Split(new char[1] { ';' }, StringSplitOptions.RemoveEmptyEntries);
		if (!restrictedExtensions.Any())
		{
			DatabaseMapper.ShowErrorMessage(Resources.Msg_PleaseInputValidExt);
			optionsTreeView.SelectedNode = optionsTreeView.Nodes["Others"];
			restrictedExtensionsTextBox.Focus();
			return;
		}

		string unsupportedExtension = restrictedExtensions.FirstOrDefault(IsUnknownRestrictedExtension);
		if (unsupportedExtension != null)
		{
			DatabaseMapper.ShowErrorMessage(string.Format(Resources.Msg_NoSupportExt, unsupportedExtension));
			optionsTreeView.SelectedNode = optionsTreeView.Nodes["Others"];
			restrictedExtensionsTextBox.Focus();
			return;
		}

		Dictionary<string, string> previousExtensions = new Dictionary<string, string>(StateFieldInstance.EnabledTagTypesByExtension);
		StateFieldInstance.EnabledTagTypesByExtension.Clear();
		foreach (string extension in restrictedExtensions)
		{
			StateFieldInstance.EnabledTagTypesByExtension.Add(extension, StateFieldInstance.KnownTagTypesByExtension[extension]);
			if (!FileFilterSettingsChanged && !previousExtensions.ContainsKey(extension))
			{
				FileFilterSettingsChanged = true;
			}
		}
		if (!FileFilterSettingsChanged && StateFieldInstance.EnabledTagTypesByExtension.Count != previousExtensions.Count)
		{
			FileFilterSettingsChanged = true;
		}

		int previousDurationFilter = Settings.Default.FileFilterByDuration;
		bool previousIgnoreVideoFile = Settings.Default.FileFilterIgnoreVideoFile;
		bool previousAlwaysShowIcon = Settings.Default.AlwaysShowIconInNofiArea;
		bool previousMinimizeToNotifyArea = Settings.Default.MinimizeToNotiArea;

		searchResultLimitsBySource.ForEachItem(ApplySearchResultLimit);
		coverSourceOrderControl.ApplyListViewOrder();
		lyricSourceOrderControl.ApplyListViewOrder();
		tagSourceOrderControl.ApplyListViewOrder();
		CoverSearchResult.SaveCoverSourceSettings();
		LyricSearchResult.SaveLyricSourceSettings();
		TrackSearchResult.SaveTagSourceSettings();

		Settings.Default.WebSearchItemsLimit = webSearchItemLimitTrackBar.Value;
		Settings.Default.ConnectorsLyricAndTLyric = lyricTranslationSeparatorComboBox.Text;
		Settings.Default.LyricDownload_DownloadTrans_Enable = downloadTranslatedLyricsCheckBox.Checked;
		Settings.Default.LyricDownload_DownloadTrans_DontDownloadOrigLyric = skipOriginalLyricCheckBox.Checked;
		Settings.Default.LyricDownload_DownloadTrans_LyricFormat = translatedLyricFormat2RadioButton.Checked ? 1 : (translatedLyricFormat3RadioButton.Checked ? 2 : (translatedLyricFormat4RadioButton.Checked ? 3 : 0));
		Settings.Default.LyricDownload_DownloadTrans_ChineseConvMode = traditionalToSimplifiedRadioButton.Checked ? 1 : (simplifiedToTraditionalRadioButton.Checked ? 2 : 0);
		Settings.Default.LyricDownload_ReformatTimetag = reformatTimestampCheckBox.Checked;
		Settings.Default.LyricDownload_RemoveTimetag = removeTimestampCheckBox.Checked;
		Settings.Default.LyricDownload_DeleteLinesOfBlankText = removeBlankLyricLinesCheckBox.Checked;
		Settings.Default.LyricDownload_DeleteHeadTag = removeLyricHeaderTagsCheckBox.Checked;
		Settings.Default.SearchCondition_UseOnlyFilename = titleSearchModeComboBox.SelectedIndex == 1;
		Settings.Default.SearchCondition_UseArtist = artistSearchConditionCheckBox.Checked;
		Settings.Default.SearchCondition_UseAlbum = albumSearchConditionCheckBox.Checked;
		Settings.Default.PictureSizeLimitsKB = pictureSizeLimitOptions[pictureSizeLimitTrackBar.Value];
		Settings.Default.PictureResolutionLimits = pictureResolutionLimitOptions[pictureResolutionLimitTrackBar.Value];
		Settings.Default.SaveLrcFileDefaultEncoding = lrcEncodingComboBox.SelectedItem.ToString();
		Settings.Default.SaveLrcFilenameFormat = GetResourceText(Resources.LrcFilenameFormat, DefaultLrcFilenameFormatValues).Split('|')[lrcFilenameFormatComboBox.SelectedIndex];
		Settings.Default.SaveLrcDirectory = lrcDirectoryTextBox.Text;
		Settings.Default.ID3v2Version = (id3v23RadioButton.Checked ? 3 : 4);
		Settings.Default.FileFilterByDuration = durationFilterOptions[durationFilterComboBox.SelectedIndex];
		Settings.Default.FileFilterIgnoreVideoFile = ignoreVideoFilesCheckBox.Checked;
		Settings.Default.ItunesSearchParams_Country = itunesCountryCodes[itunesCountryComboBox.SelectedIndex];
		Settings.Default.CommentTagWrite163Key = writeNetEaseCommentKeyCheckBox.Checked;
		Settings.Default.SaveTagsKeepUpdateTime = keepFileUpdateTimeCheckBox.Checked;
		Settings.Default.SaveLrcWhileSaveTags = saveLrcWhileSavingTagsCheckBox.Checked;
		Settings.Default.AlwaysShowIconInNofiArea = alwaysShowNotifyIconCheckBox.Checked;
		Settings.Default.MinimizeToNotiArea = minimizeToNotifyAreaCheckBox.Checked;
		Settings.Default.CheckForUpdatesOnStartup = checkForUpdatesOnStartupCheckBox.Checked;
		Settings.Default.RestrictFileExts = string.Join(";", StateFieldInstance.EnabledTagTypesByExtension.Keys) + ";";
		Settings.Default.PictureFormatLimits = GetResourceText(Resources.PictureFormatLimitsKeys, DefaultPictureFormatLimitValues).Split('|')[pictureFormatLimitComboBox.SelectedIndex];
		Settings.Default.ConnectorsArtists = artistConnectorComboBox.Text;
		Settings.Default.Save();
		CoverSearchDialog.ClearCachedCandidates();
		LyricSearchDialog.ClearCachedLyrics();
		CombinedTagSearchDialog.ClearCachedSearchResults();

		if (previousDurationFilter != Settings.Default.FileFilterByDuration || previousIgnoreVideoFile != Settings.Default.FileFilterIgnoreVideoFile)
		{
			FileFilterSettingsChanged = true;
		}
		if (previousAlwaysShowIcon != Settings.Default.AlwaysShowIconInNofiArea || previousMinimizeToNotifyArea != Settings.Default.MinimizeToNotiArea)
		{
			NotifyAreaSettingsChanged = true;
		}
		base.DialogResult = DialogResult.OK;
		Close();
	}

	private static void ApplySearchResultLimit(KeyValuePair<SourceItem, int> entry)
	{
		entry.Key.SearchResultLimit = entry.Value;
	}

	private static SourceItem FindSourceItemByName(List<SourceItem> sourceItems, string sourceName)
	{
		foreach (SourceItem sourceItem in sourceItems)
		{
			if (sourceItem.SearchSource.ToString() == sourceName)
			{
				return sourceItem;
			}
		}
		return null;
	}

	private static bool IsUnknownRestrictedExtension(string extension)
	{
		return !StateFieldInstance.KnownTagTypesByExtension.ContainsKey(extension);
	}

	private void CancelOptionsDialog(object sender, EventArgs e)
	{
		base.DialogResult = DialogResult.Cancel;
		Close();
	}

	private void UpdateTranslatedLyricControlsEnabled(object sender, EventArgs e)
	{
		foreach (Control control in downloadTranslatedLyricsCheckBox.Parent.Controls)
		{
			if (control != downloadTranslatedLyricsCheckBox)
			{
				control.Enabled = downloadTranslatedLyricsCheckBox.Checked;
			}
		}
	}

	private void UpdatePictureSizeLimitLabel(object sender, EventArgs e)
	{
		pictureSizeLimitLabel.Text = string.Concat(GetDialogText("lblPictureSizeLimits", "Picture size limits: "), pictureSizeLimitOptions[pictureSizeLimitTrackBar.Value], "KB");
	}

	private void UpdatePictureResolutionLimitLabel(object sender, EventArgs e)
	{
		int resolution = pictureResolutionLimitOptions[pictureResolutionLimitTrackBar.Value];
		string resolutionText = (pictureResolutionLimitTrackBar.Value == pictureResolutionLimitTrackBar.Minimum) ? Resources.Auto : $"{resolution}x{resolution}";
		pictureResolutionLimitLabel.Text = GetDialogText("lblPictureResolution", "Picture resolution: ") + resolutionText;
	}

	private void UpdateWebSearchLimitLabel(object sender, EventArgs e)
	{
		UpdateSearchLimitLabel(webSearchItemLimitTrackBar, webSearchItemLimitLabel);
	}

	private void UpdateCoverSearchLimit(object sender, EventArgs e)
	{
		UpdateSearchLimitLabel(coverSourceLimitTrackBar, coverSourceLimitLabel);
		if (coverSourceLimitTrackBar.Tag is SourceItem sourceItem)
		{
			searchResultLimitsBySource[sourceItem] = coverSourceLimitTrackBar.Value;
		}
	}

	private void UpdateLyricSearchLimit(object sender, EventArgs e)
	{
		UpdateSearchLimitLabel(lyricSourceLimitTrackBar, lyricSourceLimitLabel);
		if (lyricSourceLimitTrackBar.Tag is SourceItem sourceItem)
		{
			searchResultLimitsBySource[sourceItem] = lyricSourceLimitTrackBar.Value;
		}
	}

	private void UpdateTagSearchLimit(object sender, EventArgs e)
	{
		UpdateSearchLimitLabel(tagSourceLimitTrackBar, tagSourceLimitLabel);
		if (tagSourceLimitTrackBar.Tag is SourceItem sourceItem)
		{
			searchResultLimitsBySource[sourceItem] = tagSourceLimitTrackBar.Value;
		}
	}

	private void UpdateSearchLimitLabel(TrackBar limitTrackBar, Label label)
	{
		string limitText = (limitTrackBar.Value != limitTrackBar.Maximum) ? limitTrackBar.Value.ToString() : Resources.Unlimited;
		label.Text = GetSearchLimitLabelPrefix(label) + (limitTrackBar.Enabled ? limitText : GetDialogText("LabelNone", "None"));
	}

	private string GetSearchLimitLabelPrefix(Label label)
	{
		string resourceText = dialogResources.GetString(label.Name);
		if (!string.IsNullOrEmpty(resourceText))
		{
			return resourceText;
		}
		if (label == webSearchItemLimitLabel)
		{
			return "Web search items limit: ";
		}
		if (label == coverSourceLimitLabel)
		{
			return "Cover sources: ";
		}
		if (label == lyricSourceLimitLabel)
		{
			return "Lyric sources: ";
		}
		if (label == tagSourceLimitLabel)
		{
			return "Tag sources: ";
		}
		return string.Empty;
	}

	private void UpdateTranslatedLyricConnectorState(object sender, EventArgs e)
	{
		UpdateTranslatedLyricConnectorEnabled();
	}

	private void UpdateTranslatedLyricConnectorEnabled()
	{
		lyricTranslationSeparatorComboBox.Enabled = translatedLyricFormat1RadioButton.Checked && downloadTranslatedLyricsCheckBox.Checked;
	}

	private void BrowseLyricSaveDirectory(object sender, EventArgs e)
	{
		FolderSelectionDialog folderSelectionDialog = new FolderSelectionDialog();
		if (folderSelectionDialog.ShowDialog(allowMultiSelect: false, showIncludeSubdirectories: false) == DialogResult.OK)
		{
			lrcDirectoryTextBox.Text = folderSelectionDialog.SelectedPaths[0];
		}
	}

	private void UseLocalLyricSaveDirectory(object sender, EventArgs e)
	{
		lrcDirectoryTextBox.Text = "";
	}

	private void ResetRestrictedFileExtensions(object sender, EventArgs e)
	{
		restrictedExtensionsTextBox.Text = string.Join(";", StateFieldInstance.KnownTagTypesByExtension.Keys) + ";";
	}

	private void ClearAllTagHistory(object sender, EventArgs e)
	{
		if (DatabaseMapper.ConfirmYesNo(Resources.Msg_ConfirmClearAllTagsHistory))
		{
			if (TagHistoryRepository.ClearAllHistory() >= 0)
			{
				DatabaseMapper.ShowInformationMessage(Resources.Msg_ClearAllTagsHistoryComplete);
			}
			else
			{
				DatabaseMapper.ShowInformationMessage(Resources.Msg_ClearAllTagsHistoryFail);
			}
		}
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing && components != null)
		{
			components.Dispose();
		}
		base.Dispose(disposing);
	}

	private void InitializeComponent()
	{
		while (true)
		{
			components = new System.ComponentModel.Container();
			while (true)
			{
				TreeNode treeNode = new TreeNode("Tag sources");
				TreeNode treeNode2 = new TreeNode("Lyric download");
				TreeNode treeNode3 = new TreeNode("Others 1");
				while (true)
				{
					TreeNode treeNode4 = new TreeNode("Others 2");
					rootLayoutPanel = new FlowLayoutPanel();
					navigationPanel = new FlowLayoutPanel();
					mainSplitContainer = new SplitContainer();
					optionsTreeView = new TreeView();
					sourceOrderPanel = new FlowLayoutPanel();
					translatedLyricGroupBox = new GroupBox();
					translatedLyricOptionsPanel = new FlowLayoutPanel();
					downloadTranslatedLyricsCheckBox = new CheckBox();
					skipOriginalLyricCheckBox = new CheckBox();
					translatedLyricFormatLabel = new Label();
					translatedLyricFormatPanel = new FlowLayoutPanel();
					translatedLyricFormat1RadioButton = new RadioButton();
					translatedLyricFormat2RadioButton = new RadioButton();
					translatedLyricFormat3RadioButton = new RadioButton();
					while (true)
					{
						translatedLyricFormat4RadioButton = new RadioButton();
						lyricTranslationSeparatorLabel = new Label();
						lyricTranslationSeparatorComboBox = new ComboBox();
						chineseConversionLabel = new Label();
						chineseConversionPanel = new FlowLayoutPanel();
						noChineseConversionRadioButton = new RadioButton();
						traditionalToSimplifiedRadioButton = new RadioButton();
						simplifiedToTraditionalRadioButton = new RadioButton();
						lyricCleanupOptionsPanel = new FlowLayoutPanel();
						reformatTimestampCheckBox = new CheckBox();
						while (true)
						{
							removeTimestampCheckBox = new CheckBox();
							removeBlankLyricLinesCheckBox = new CheckBox();
							removeLyricHeaderTagsCheckBox = new CheckBox();
							searchAndTagOptionsPanel = new FlowLayoutPanel();
							webSearchCriteriaLabel = new Label();
							webSearchCriteriaPanel = new FlowLayoutPanel();
							titleSearchEnabledCheckBox = new CheckBox();
							titleSearchModeComboBox = new ComboBox();
							artistSearchConditionCheckBox = new CheckBox();
							albumSearchConditionCheckBox = new CheckBox();
							pictureSizeLimitLabel = new Label();
							pictureSizeLimitTrackBar = new TrackBar();
							pictureResolutionLimitLabel = new Label();
							pictureResolutionLimitTrackBar = new TrackBar();
							pictureFormatLimitLabel = new Label();
							pictureFormatLimitComboBox = new ComboBox();
							artistConnectorLabel = new Label();
							while (true)
							{
								artistConnectorComboBox = new ComboBox();
								id3v2VersionPanel = new FlowLayoutPanel();
								id3v2VersionLabel = new Label();
								id3v24RadioButton = new RadioButton();
								id3v23RadioButton = new RadioButton();
								fileFilterLabel = new Label();
								fileFilterPanel = new FlowLayoutPanel();
								durationFilterLabel = new Label();
								durationFilterComboBox = new ComboBox();
								ignoreVideoFilesCheckBox = new CheckBox();
								commentTagLabel = new Label();
								writeNetEaseCommentKeyCheckBox = new CheckBox();
								itunesParametersGroupBox = new GroupBox();
								itunesParametersPanel = new FlowLayoutPanel();
								itunesCountryPanel = new FlowLayoutPanel();
								itunesCountryLabel = new Label();
								itunesCountryComboBox = new ComboBox();
								while (true)
								{
									sourceLimitPanel = new FlowLayoutPanel();
									while (true)
									{
										webSearchItemLimitLabel = new Label();
										webSearchItemLimitTrackBar = new TrackBar();
										webSearchLimitGroupBox = new GroupBox();
										webSearchLimitPanel = new FlowLayoutPanel();
										coverSourceLimitLabel = new Label();
										coverSourceLimitTrackBar = new TrackBar();
										lyricSourceLimitLabel = new Label();
										lyricSourceLimitTrackBar = new TrackBar();
										tagSourceLimitLabel = new Label();
										tagSourceLimitTrackBar = new TrackBar();
										saveAndNotificationOptionsPanel = new FlowLayoutPanel();
										lrcFileGroupBox = new GroupBox();
										lrcFileOptionsPanel = new FlowLayoutPanel();
										while (true)
										{
											lrcEncodingPanel = new FlowLayoutPanel();
											lrcEncodingLabel = new Label();
											lrcEncodingComboBox = new ComboBox();
											lrcDirectoryPanel = new FlowLayoutPanel();
											lrcDirectoryLabel = new Label();
											lrcDirectoryTextBox = new TextBox();
											browseLrcDirectoryButton = new Button();
											useLocalLrcDirectoryButton = new Button();
											lrcFilenameFormatPanel = new FlowLayoutPanel();
											lrcFilenameFormatLabel = new Label();
											lrcFilenameFormatComboBox = new ComboBox();
											saveLrcWhileSavingTagsCheckBox = new CheckBox();
											notifyAreaGroupBox = new GroupBox();
											notifyAreaOptionsPanel = new FlowLayoutPanel();
											while (true)
											{
												alwaysShowNotifyIconCheckBox = new CheckBox();
												minimizeToNotifyAreaCheckBox = new CheckBox();
												restrictedExtensionsPanel = new FlowLayoutPanel();
												restrictedExtensionsLabel = new Label();
												restrictedExtensionsEditorPanel = new FlowLayoutPanel();
												restrictedExtensionsTextBox = new TextBox();
												resetRestrictedExtensionsButton = new Button();
												while (true)
												{
													keepFileUpdateTimeCheckBox = new CheckBox();
													checkForUpdatesOnStartupCheckBox = new CheckBox();
													while (true)
													{
														tagHistoryPanel = new FlowLayoutPanel();
														clearAllTagHistoryButton = new Button();
														detailsPanel = new FlowLayoutPanel();
														commandButtonPanel = new FlowLayoutPanel();
														okButton = new Button();
														cancelButton = new Button();
														while (true)
														{
															optionsToolTip = new ToolTip(components);
															coverSourceOrderControl = new SourceOrderControl();
															lyricSourceOrderControl = new SourceOrderControl();
															tagSourceOrderControl = new SourceOrderControl();
															rootLayoutPanel.SuspendLayout();
															navigationPanel.SuspendLayout();
															((ISupportInitialize)mainSplitContainer).BeginInit();
															mainSplitContainer.Panel1.SuspendLayout();
															mainSplitContainer.Panel2.SuspendLayout();
															mainSplitContainer.SuspendLayout();
															sourceOrderPanel.SuspendLayout();
															translatedLyricGroupBox.SuspendLayout();
															translatedLyricOptionsPanel.SuspendLayout();
															translatedLyricFormatPanel.SuspendLayout();
															chineseConversionPanel.SuspendLayout();
															lyricCleanupOptionsPanel.SuspendLayout();
															searchAndTagOptionsPanel.SuspendLayout();
															webSearchCriteriaPanel.SuspendLayout();
															((ISupportInitialize)pictureSizeLimitTrackBar).BeginInit();
															((ISupportInitialize)pictureResolutionLimitTrackBar).BeginInit();
															id3v2VersionPanel.SuspendLayout();
															fileFilterPanel.SuspendLayout();
															itunesParametersGroupBox.SuspendLayout();
															while (true)
															{
																itunesParametersPanel.SuspendLayout();
																itunesCountryPanel.SuspendLayout();
																sourceLimitPanel.SuspendLayout();
																((ISupportInitialize)webSearchItemLimitTrackBar).BeginInit();
																webSearchLimitGroupBox.SuspendLayout();
																while (true)
																{
																	webSearchLimitPanel.SuspendLayout();
																	((ISupportInitialize)coverSourceLimitTrackBar).BeginInit();
																	((ISupportInitialize)lyricSourceLimitTrackBar).BeginInit();
																	((ISupportInitialize)tagSourceLimitTrackBar).BeginInit();
																	saveAndNotificationOptionsPanel.SuspendLayout();
																	lrcFileGroupBox.SuspendLayout();
																	lrcFileOptionsPanel.SuspendLayout();
																	lrcEncodingPanel.SuspendLayout();
																	lrcDirectoryPanel.SuspendLayout();
																	lrcFilenameFormatPanel.SuspendLayout();
																	notifyAreaGroupBox.SuspendLayout();
																	notifyAreaOptionsPanel.SuspendLayout();
																	restrictedExtensionsPanel.SuspendLayout();
																	restrictedExtensionsEditorPanel.SuspendLayout();
																	tagHistoryPanel.SuspendLayout();
																	detailsPanel.SuspendLayout();
																	commandButtonPanel.SuspendLayout();
																	SuspendLayout();
																	while (true)
																	{
																		rootLayoutPanel.Controls.Add(navigationPanel);
																		rootLayoutPanel.Controls.Add(detailsPanel);
																		rootLayoutPanel.Dock = DockStyle.Fill;
																		rootLayoutPanel.FlowDirection = FlowDirection.TopDown;
																		rootLayoutPanel.Location = new Point(0, 0);
																		rootLayoutPanel.Margin = new Padding(0);
																		rootLayoutPanel.Name = "flowLayoutPanel1";
																		rootLayoutPanel.Padding = new Padding(10, 10, 10, 0);
																		int num;
																		goto IL_345b;
																		IL_11d9:
																		((ISupportInitialize)pictureResolutionLimitTrackBar).EndInit();
																		id3v2VersionPanel.ResumeLayout(performLayout: false);
																		num = 8;
																		goto IL_40fd;
																		IL_345b:
																		rootLayoutPanel.Size = new Size(1584, 1161);
																		rootLayoutPanel.TabIndex = 0;
																		rootLayoutPanel.SizeChanged += ResizeSettingsLayout;
																		navigationPanel.Controls.Add(mainSplitContainer);
																		navigationPanel.Location = new Point(10, 10);
																		goto IL_12e6;
																		IL_12e6:
																		navigationPanel.Margin = new Padding(0);
																		navigationPanel.Name = "flowLayoutPanel2";
																		goto IL_1307;
																		IL_1307:
																		navigationPanel.Size = new Size(1550, 1136);
																		navigationPanel.TabIndex = 0;
																		navigationPanel.WrapContents = false;
																		mainSplitContainer.FixedPanel = FixedPanel.Panel1;
																		mainSplitContainer.Location = new Point(0, 0);
																		goto IL_1357;
																		IL_1357:
																		mainSplitContainer.Margin = new Padding(0);
																		mainSplitContainer.Name = "splitContainer1";
																		mainSplitContainer.Panel1.Controls.Add(optionsTreeView);
																		mainSplitContainer.Panel2.Controls.Add(sourceOrderPanel);
																		mainSplitContainer.Size = new Size(1550, 1136);
																		mainSplitContainer.SplitterDistance = 120;
																		mainSplitContainer.TabIndex = 1;
																		optionsTreeView.Dock = DockStyle.Fill;
																		optionsTreeView.HideSelection = false;
																		optionsTreeView.Location = new Point(0, 0);
																		optionsTreeView.Margin = new Padding(0);
																		optionsTreeView.Name = "treeView1";
																		treeNode.Name = "TagSources";
																		treeNode.Text = "Tag sources";
																		treeNode2.Name = "LyricDownload";
																		treeNode2.Text = "Lyric download";
																		treeNode3.Name = "Others";
																		treeNode3.Text = "Others 1";
																		treeNode4.Name = "Others1";
																		treeNode4.Text = "Others 2";
																		optionsTreeView.Nodes.AddRange(new TreeNode[4] { treeNode, treeNode2, treeNode3, treeNode4 });
																		optionsTreeView.Size = new Size(120, 1136);
																		optionsTreeView.TabIndex = 0;
																		optionsTreeView.AfterSelect += OptionsTreeSelectionChanged;
																		sourceOrderPanel.Controls.Add(coverSourceOrderControl);
																		sourceOrderPanel.Controls.Add(lyricSourceOrderControl);
																		sourceOrderPanel.Controls.Add(tagSourceOrderControl);
																		goto IL_1526;
																		IL_1526:
																		sourceOrderPanel.Controls.Add(translatedLyricGroupBox);
																		sourceOrderPanel.Controls.Add(lyricCleanupOptionsPanel);
																		sourceOrderPanel.Controls.Add(searchAndTagOptionsPanel);
																		sourceOrderPanel.Controls.Add(itunesParametersGroupBox);
																		sourceOrderPanel.Controls.Add(sourceLimitPanel);
																		sourceOrderPanel.Controls.Add(webSearchLimitGroupBox);
																		sourceOrderPanel.Controls.Add(saveAndNotificationOptionsPanel);
																		sourceOrderPanel.Dock = DockStyle.Fill;
																		sourceOrderPanel.FlowDirection = FlowDirection.TopDown;
																		sourceOrderPanel.Location = new Point(0, 0);
																		sourceOrderPanel.Margin = new Padding(0);
																		sourceOrderPanel.Name = "panelSettings";
																		sourceOrderPanel.Padding = new Padding(6, 0, 0, 0);
																		sourceOrderPanel.Size = new Size(1426, 1136);
																		goto IL_1639;
																		IL_1639:
																		sourceOrderPanel.TabIndex = 0;
																		translatedLyricGroupBox.Controls.Add(translatedLyricOptionsPanel);
																		translatedLyricGroupBox.Location = new Point(6, 380);
																		translatedLyricGroupBox.Margin = new Padding(0);
																		translatedLyricGroupBox.Name = "panelDownloadTrans";
																		translatedLyricGroupBox.Padding = new Padding(5);
																		translatedLyricGroupBox.Size = new Size(414, 265);
																		goto IL_16bd;
																		IL_16bd:
																		translatedLyricGroupBox.TabIndex = 4;
																		translatedLyricGroupBox.TabStop = false;
																		translatedLyricGroupBox.Text = "Download translations";
																		translatedLyricOptionsPanel.Controls.Add(downloadTranslatedLyricsCheckBox);
																		translatedLyricOptionsPanel.Controls.Add(skipOriginalLyricCheckBox);
																		translatedLyricOptionsPanel.Controls.Add(translatedLyricFormatLabel);
																		translatedLyricOptionsPanel.Controls.Add(translatedLyricFormatPanel);
																		translatedLyricOptionsPanel.Controls.Add(lyricTranslationSeparatorLabel);
																		goto IL_1753;
																		IL_1753:
																		translatedLyricOptionsPanel.Controls.Add(lyricTranslationSeparatorComboBox);
																		translatedLyricOptionsPanel.Controls.Add(chineseConversionLabel);
																		translatedLyricOptionsPanel.Controls.Add(chineseConversionPanel);
																		translatedLyricOptionsPanel.Dock = DockStyle.Fill;
																		translatedLyricOptionsPanel.FlowDirection = FlowDirection.TopDown;
																		translatedLyricOptionsPanel.Location = new Point(5, 20);
																		translatedLyricOptionsPanel.Margin = new Padding(0);
																		translatedLyricOptionsPanel.Name = "flowLayoutPanel5";
																		translatedLyricOptionsPanel.Size = new Size(404, 240);
																		translatedLyricOptionsPanel.TabIndex = 0;
																		goto IL_1807;
																		IL_1807:
																		downloadTranslatedLyricsCheckBox.AutoSize = true;
																		downloadTranslatedLyricsCheckBox.Location = new Point(5, 3);
																		downloadTranslatedLyricsCheckBox.Margin = new Padding(5, 3, 3, 3);
																		downloadTranslatedLyricsCheckBox.Name = "cbEnableDownloadTrans";
																		downloadTranslatedLyricsCheckBox.Size = new Size(62, 18);
																		downloadTranslatedLyricsCheckBox.TabIndex = 0;
																		downloadTranslatedLyricsCheckBox.Text = "Enable";
																		downloadTranslatedLyricsCheckBox.UseVisualStyleBackColor = true;
																		downloadTranslatedLyricsCheckBox.CheckedChanged += UpdateTranslatedLyricControlsEnabled;
																		skipOriginalLyricCheckBox.AutoSize = true;
																		goto IL_18a8;
																		IL_18a8:
																		skipOriginalLyricCheckBox.Location = new Point(5, 34);
																		skipOriginalLyricCheckBox.Margin = new Padding(5, 10, 3, 3);
																		skipOriginalLyricCheckBox.Name = "cbDontDownloadOrigLyric";
																		skipOriginalLyricCheckBox.Size = new Size(305, 18);
																		skipOriginalLyricCheckBox.TabIndex = 1;
																		skipOriginalLyricCheckBox.Text = "Don't download original lyric if the translation exists";
																		skipOriginalLyricCheckBox.UseVisualStyleBackColor = true;
																		translatedLyricFormatLabel.AutoSize = true;
																		translatedLyricFormatLabel.Location = new Point(3, 65);
																		translatedLyricFormatLabel.Margin = new Padding(3, 10, 3, 0);
																		translatedLyricFormatLabel.Name = "lblLyricFormatForDownloadTrans";
																		translatedLyricFormatLabel.Size = new Size(75, 14);
																		translatedLyricFormatLabel.TabIndex = 2;
																		goto IL_1983;
																		IL_1983:
																		translatedLyricFormatLabel.Text = "Lyric format:";
																		translatedLyricFormatPanel.Controls.Add(translatedLyricFormat1RadioButton);
																		goto IL_19a9;
																		IL_19a9:
																		translatedLyricFormatPanel.Controls.Add(translatedLyricFormat2RadioButton);
																		translatedLyricFormatPanel.Controls.Add(translatedLyricFormat3RadioButton);
																		translatedLyricFormatPanel.Controls.Add(translatedLyricFormat4RadioButton);
																		translatedLyricFormatPanel.Location = new Point(5, 84);
																		translatedLyricFormatPanel.Margin = new Padding(5, 5, 0, 0);
																		translatedLyricFormatPanel.Name = "flowLayoutPanel6";
																		translatedLyricFormatPanel.Size = new Size(604, 46);
																		translatedLyricFormatPanel.TabIndex = 3;
																		translatedLyricFormatPanel.WrapContents = false;
																		translatedLyricFormat1RadioButton.AutoSize = true;
																		translatedLyricFormat1RadioButton.Location = new Point(0, 0);
																		translatedLyricFormat1RadioButton.Margin = new Padding(0);
																		translatedLyricFormat1RadioButton.Name = "rbDlTransLyFmt1";
																		translatedLyricFormat1RadioButton.Size = new Size(145, 18);
																		translatedLyricFormat1RadioButton.TabIndex = 0;
																		translatedLyricFormat1RadioButton.TabStop = true;
																		translatedLyricFormat1RadioButton.Text = "[00:05:12]Lyric TLyric";
																		translatedLyricFormat1RadioButton.UseVisualStyleBackColor = true;
																		translatedLyricFormat1RadioButton.CheckedChanged += UpdateTranslatedLyricConnectorState;
																		translatedLyricFormat2RadioButton.AutoSize = true;
																		translatedLyricFormat2RadioButton.CheckAlign = ContentAlignment.TopLeft;
																		translatedLyricFormat2RadioButton.Location = new Point(145, 0);
																		translatedLyricFormat2RadioButton.Margin = new Padding(0);
																		translatedLyricFormat2RadioButton.Name = "rbDlTransLyFmt2";
																		translatedLyricFormat2RadioButton.Size = new Size(117, 32);
																		translatedLyricFormat2RadioButton.TabIndex = 1;
																		translatedLyricFormat2RadioButton.TabStop = true;
																		translatedLyricFormat2RadioButton.Text = "[00:05:12]Lyric\n[00:05:12]TLyric";
																		translatedLyricFormat2RadioButton.UseVisualStyleBackColor = true;
																		translatedLyricFormat2RadioButton.CheckedChanged += UpdateTranslatedLyricConnectorState;
																		goto IL_1ba0;
																		IL_1ba0:
																		translatedLyricFormat3RadioButton.AutoSize = true;
																		translatedLyricFormat3RadioButton.CheckAlign = ContentAlignment.TopLeft;
																		translatedLyricFormat3RadioButton.Location = new Point(262, 0);
																		translatedLyricFormat3RadioButton.Margin = new Padding(0);
																		goto IL_1bdf;
																		IL_1bdf:
																		translatedLyricFormat3RadioButton.Name = "rbDlTransLyFmt3";
																		translatedLyricFormat3RadioButton.Size = new Size(120, 46);
																		translatedLyricFormat3RadioButton.TabIndex = 2;
																		translatedLyricFormat3RadioButton.TabStop = true;
																		goto IL_1c1b;
																		IL_1c1b:
																		translatedLyricFormat3RadioButton.Text = "[00:05:12]Lyric\n[00:07:05]TLyric\n[00:07:06]Lyric 2";
																		translatedLyricFormat3RadioButton.UseVisualStyleBackColor = true;
																		translatedLyricFormat3RadioButton.CheckedChanged += UpdateTranslatedLyricConnectorState;
																		translatedLyricFormat4RadioButton.AutoSize = true;
																		translatedLyricFormat4RadioButton.CheckAlign = ContentAlignment.TopLeft;
																		goto IL_1c66;
																		IL_1c66:
																		translatedLyricFormat4RadioButton.Location = new Point(382, 0);
																		goto IL_1c7c;
																		IL_1c7c:
																		translatedLyricFormat4RadioButton.Margin = new Padding(0);
																		translatedLyricFormat4RadioButton.Name = "rbDlTransLyFmt4";
																		translatedLyricFormat4RadioButton.Size = new Size(117, 32);
																		translatedLyricFormat4RadioButton.TabIndex = 3;
																		translatedLyricFormat4RadioButton.TabStop = true;
																		translatedLyricFormat4RadioButton.Text = "[00:05:12]Lyric\n[00:05:12]TLyric";
																		translatedLyricFormat4RadioButton.UseVisualStyleBackColor = true;
																		translatedLyricFormat4RadioButton.CheckedChanged += UpdateTranslatedLyricConnectorState;
																		lyricTranslationSeparatorLabel.AutoSize = true;
																		lyricTranslationSeparatorLabel.Location = new Point(3, 140);
																		lyricTranslationSeparatorLabel.Margin = new Padding(3, 10, 3, 0);
																		lyricTranslationSeparatorLabel.Name = "lblConnectorsLyricAndTLyric";
																		lyricTranslationSeparatorLabel.Size = new Size(326, 14);
																		lyricTranslationSeparatorLabel.TabIndex = 6;
																		lyricTranslationSeparatorLabel.Text = "Connectors between the original lyric and the translation: ";
																		lyricTranslationSeparatorComboBox.FormattingEnabled = true;
																		lyricTranslationSeparatorComboBox.Location = new Point(6, 160);
																		lyricTranslationSeparatorComboBox.Margin = new Padding(6, 6, 0, 0);
																		lyricTranslationSeparatorComboBox.Name = "cbConnectorsLyricAndTLyric";
																		lyricTranslationSeparatorComboBox.Size = new Size(100, 22);
																		lyricTranslationSeparatorComboBox.TabIndex = 7;
																		chineseConversionLabel.AutoSize = true;
																		chineseConversionLabel.Location = new Point(3, 192);
																		goto IL_1dfe;
																		IL_1dfe:
																		chineseConversionLabel.Margin = new Padding(3, 10, 3, 0);
																		chineseConversionLabel.Name = "lblCHSCHTConv";
																		chineseConversionLabel.Size = new Size(248, 14);
																		num = 80;
																		goto IL_40fd;
																		IL_40fd:
																		while (true)
																		{
																			switch (num)
																			{
																			case 34:
																				break;
																			case 127:
																				pictureResolutionLimitTrackBar.Name = "tbPictureResolution";
																				pictureResolutionLimitTrackBar.Size = new Size(409, 32);
																				pictureResolutionLimitTrackBar.TabIndex = 20;
																				pictureResolutionLimitTrackBar.TickStyle = TickStyle.TopLeft;
																				pictureResolutionLimitTrackBar.ValueChanged += UpdatePictureResolutionLimitLabel;
																				pictureFormatLimitLabel.AutoSize = true;
																				pictureFormatLimitLabel.Location = new Point(3, 177);
																				pictureFormatLimitLabel.Margin = new Padding(3, 10, 3, 0);
																				pictureFormatLimitLabel.Name = "lblPictureFormatLimits";
																				pictureFormatLimitLabel.Size = new Size(267, 14);
																				pictureFormatLimitLabel.TabIndex = 15;
																				pictureFormatLimitLabel.Text = "Format limits for pictures embedded in file tags:";
																				pictureFormatLimitComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
																				pictureFormatLimitComboBox.FormattingEnabled = true;
																				pictureFormatLimitComboBox.Location = new Point(6, 196);
																				pictureFormatLimitComboBox.Margin = new Padding(6, 5, 0, 0);
																				goto case 58;
																			case 58:
																				pictureFormatLimitComboBox.Name = "cbPictureFormatLimits";
																				goto case 40;
																			case 40:
																				pictureFormatLimitComboBox.Size = new Size(155, 22);
																				pictureFormatLimitComboBox.TabIndex = 16;
																				artistConnectorLabel.AutoSize = true;
																				artistConnectorLabel.Location = new Point(3, 228);
																				artistConnectorLabel.Margin = new Padding(3, 10, 3, 0);
																				artistConnectorLabel.Name = "lblConnectorsArtists";
																				artistConnectorLabel.Size = new Size(356, 14);
																				artistConnectorLabel.TabIndex = 17;
																				artistConnectorLabel.Text = "Connectors for artists (Used when matching to multiple artists):";
																				artistConnectorComboBox.FormattingEnabled = true;
																				artistConnectorComboBox.Items.AddRange(new object[3] { "/", "、", ";" });
																				artistConnectorComboBox.Location = new Point(6, 247);
																				artistConnectorComboBox.Margin = new Padding(6, 5, 0, 0);
																				artistConnectorComboBox.Name = "cbConnectorsArtists";
																				artistConnectorComboBox.Size = new Size(155, 22);
																				artistConnectorComboBox.TabIndex = 18;
																				id3v2VersionPanel.Controls.Add(id3v2VersionLabel);
																				goto case 90;
																			case 90:
																				id3v2VersionPanel.Controls.Add(id3v24RadioButton);
																				id3v2VersionPanel.Controls.Add(id3v23RadioButton);
																				id3v2VersionPanel.Location = new Point(3, 279);
																				id3v2VersionPanel.Margin = new Padding(3, 10, 0, 0);
																				id3v2VersionPanel.Name = "flowLayoutPanel11";
																				goto case 113;
																			case 113:
																				id3v2VersionPanel.Size = new Size(332, 18);
																				id3v2VersionPanel.TabIndex = 5;
																				id3v2VersionLabel.AutoSize = true;
																				goto case 56;
																			case 56:
																				id3v2VersionLabel.Location = new Point(0, 2);
																				id3v2VersionLabel.Margin = new Padding(0, 2, 0, 0);
																				goto case 30;
																			case 30:
																				id3v2VersionLabel.Name = "label6";
																				id3v2VersionLabel.Size = new Size(43, 14);
																				id3v2VersionLabel.TabIndex = 0;
																				goto case 7;
																			case 7:
																				id3v2VersionLabel.Text = "ID3v2:";
																				id3v24RadioButton.AutoSize = true;
																				id3v24RadioButton.Location = new Point(49, 0);
																				id3v24RadioButton.Margin = new Padding(6, 0, 0, 0);
																				id3v24RadioButton.Name = "rbID3v2_4";
																				id3v24RadioButton.Size = new Size(105, 18);
																				goto case 69;
																			case 69:
																				id3v24RadioButton.TabIndex = 1;
																				id3v24RadioButton.TabStop = true;
																				id3v24RadioButton.Text = "ID3v2.4 UTF-8";
																				id3v24RadioButton.UseVisualStyleBackColor = true;
																				id3v23RadioButton.AutoSize = true;
																				id3v23RadioButton.Location = new Point(160, 0);
																				id3v23RadioButton.Margin = new Padding(6, 0, 0, 0);
																				id3v23RadioButton.Name = "rbID3v2_3";
																				id3v23RadioButton.Size = new Size(112, 18);
																				id3v23RadioButton.TabIndex = 2;
																				id3v23RadioButton.TabStop = true;
																				id3v23RadioButton.Text = "ID3v2.3 UTF-16";
																				id3v23RadioButton.UseVisualStyleBackColor = true;
																				fileFilterLabel.AutoSize = true;
																				fileFilterLabel.Location = new Point(3, 307);
																				fileFilterLabel.Margin = new Padding(3, 10, 3, 0);
																				fileFilterLabel.Name = "lblFileFilter";
																				fileFilterLabel.Size = new Size(37, 14);
																				fileFilterLabel.TabIndex = 6;
																				fileFilterLabel.Text = "Filter:";
																				fileFilterPanel.Controls.Add(durationFilterLabel);
																				fileFilterPanel.Controls.Add(durationFilterComboBox);
																				fileFilterPanel.Controls.Add(ignoreVideoFilesCheckBox);
																				fileFilterPanel.Location = new Point(3, 321);
																				fileFilterPanel.Margin = new Padding(3, 0, 0, 0);
																				fileFilterPanel.Name = "flowLayoutPanel13";
																				fileFilterPanel.Size = new Size(355, 22);
																				fileFilterPanel.TabIndex = 7;
																				durationFilterLabel.AutoSize = true;
																				durationFilterLabel.Location = new Point(0, 4);
																				durationFilterLabel.Margin = new Padding(0, 4, 0, 0);
																				goto case 10;
																			case 10:
																				durationFilterLabel.Name = "lblFileFilterByDuration";
																				durationFilterLabel.Size = new Size(73, 14);
																				durationFilterLabel.TabIndex = 0;
																				durationFilterLabel.Text = "By duration:";
																				durationFilterComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
																				durationFilterComboBox.FormattingEnabled = true;
																				durationFilterComboBox.Location = new Point(73, 0);
																				goto default;
																			default:
																				durationFilterComboBox.Margin = new Padding(0);
																				durationFilterComboBox.Name = "cbFileFilterByDuration";
																				durationFilterComboBox.Size = new Size(121, 22);
																				durationFilterComboBox.TabIndex = 1;
																				ignoreVideoFilesCheckBox.AutoSize = true;
																				ignoreVideoFilesCheckBox.Checked = true;
																				ignoreVideoFilesCheckBox.CheckState = CheckState.Checked;
																				ignoreVideoFilesCheckBox.Location = new Point(200, 2);
																				ignoreVideoFilesCheckBox.Margin = new Padding(6, 2, 0, 0);
																				ignoreVideoFilesCheckBox.Name = "cbFileFilterIgnoreVideoFile";
																				ignoreVideoFilesCheckBox.Size = new Size(146, 18);
																				ignoreVideoFilesCheckBox.TabIndex = 2;
																				goto case 104;
																			case 104:
																				ignoreVideoFilesCheckBox.Text = "Ignore MP4 video files";
																				goto case 46;
																			case 46:
																				ignoreVideoFilesCheckBox.UseVisualStyleBackColor = true;
																				commentTagLabel.AutoSize = true;
																				commentTagLabel.Location = new Point(3, 353);
																				commentTagLabel.Margin = new Padding(3, 10, 3, 0);
																				commentTagLabel.Name = "lblCommentTag";
																				commentTagLabel.Size = new Size(86, 14);
																				commentTagLabel.TabIndex = 23;
																				commentTagLabel.Text = "Comment tag:";
																				writeNetEaseCommentKeyCheckBox.AutoSize = true;
																				writeNetEaseCommentKeyCheckBox.Location = new Point(6, 371);
																				goto case 55;
																			case 55:
																				writeNetEaseCommentKeyCheckBox.Margin = new Padding(6, 4, 0, 0);
																				writeNetEaseCommentKeyCheckBox.Name = "cbCommentTagWrite163Key";
																				writeNetEaseCommentKeyCheckBox.Size = new Size(369, 18);
																				writeNetEaseCommentKeyCheckBox.TabIndex = 24;
																				goto case 11;
																			case 11:
																				writeNetEaseCommentKeyCheckBox.Text = "Comment tag write \"163 key\" mark, if you use 163 tag SearchSource";
																				writeNetEaseCommentKeyCheckBox.UseVisualStyleBackColor = true;
																				itunesParametersGroupBox.Controls.Add(itunesParametersPanel);
																				num = 20;
																				continue;
																			case 126:
																				coverSourceLimitTrackBar.Size = new Size(380, 45);
																				coverSourceLimitTrackBar.TabIndex = 5;
																				coverSourceLimitTrackBar.TickStyle = TickStyle.TopLeft;
																				coverSourceLimitTrackBar.Value = 1;
																				coverSourceLimitTrackBar.ValueChanged += UpdateCoverSearchLimit;
																				lyricSourceLimitLabel.AutoSize = true;
																				lyricSourceLimitLabel.Location = new Point(10, 76);
																				lyricSourceLimitLabel.Margin = new Padding(5, 3, 3, 0);
																				lyricSourceLimitLabel.Name = "lblWSILLyricSources";
																				lyricSourceLimitLabel.Size = new Size(80, 14);
																				lyricSourceLimitLabel.TabIndex = 6;
																				lyricSourceLimitLabel.Text = "Lyric sources:";
																				lyricSourceLimitTrackBar.LargeChange = 1;
																				lyricSourceLimitTrackBar.Location = new Point(5, 93);
																				lyricSourceLimitTrackBar.Margin = new Padding(0, 3, 0, 3);
																				lyricSourceLimitTrackBar.Maximum = 100;
																				lyricSourceLimitTrackBar.Minimum = 1;
																				lyricSourceLimitTrackBar.Name = "tbWSILLyricSources";
																				lyricSourceLimitTrackBar.Size = new Size(380, 45);
																				lyricSourceLimitTrackBar.TabIndex = 7;
																				lyricSourceLimitTrackBar.TickStyle = TickStyle.TopLeft;
																				lyricSourceLimitTrackBar.Value = 1;
																				lyricSourceLimitTrackBar.ValueChanged += UpdateLyricSearchLimit;
																				num = 79;
																				continue;
																			case 15:
																				tagSourceLimitLabel.Size = new Size(150, 14);
																				tagSourceLimitLabel.TabIndex = 8;
																				tagSourceLimitLabel.Text = "Combination tags sources:";
																				tagSourceLimitTrackBar.LargeChange = 1;
																				tagSourceLimitTrackBar.Location = new Point(5, 161);
																				goto case 22;
																			case 22:
																				tagSourceLimitTrackBar.Margin = new Padding(0, 3, 0, 3);
																				tagSourceLimitTrackBar.Maximum = 100;
																				goto case 73;
																			case 73:
																				tagSourceLimitTrackBar.Minimum = 1;
																				tagSourceLimitTrackBar.Name = "tbWSILCombSources";
																				tagSourceLimitTrackBar.Size = new Size(380, 45);
																				tagSourceLimitTrackBar.TabIndex = 9;
																				tagSourceLimitTrackBar.TickStyle = TickStyle.TopLeft;
																				tagSourceLimitTrackBar.Value = 1;
																				tagSourceLimitTrackBar.ValueChanged += UpdateTagSearchLimit;
																				saveAndNotificationOptionsPanel.Controls.Add(lrcFileGroupBox);
																				saveAndNotificationOptionsPanel.Controls.Add(notifyAreaGroupBox);
																				saveAndNotificationOptionsPanel.Controls.Add(restrictedExtensionsPanel);
																				saveAndNotificationOptionsPanel.Location = new Point(887, 0);
																				saveAndNotificationOptionsPanel.Margin = new Padding(0);
																				num = 66;
																				continue;
																			case 93:
																				tagHistoryPanel.Size = new Size(383, 30);
																				tagHistoryPanel.TabIndex = 19;
																				clearAllTagHistoryButton.AutoSize = true;
																				clearAllTagHistoryButton.Location = new Point(0, 0);
																				clearAllTagHistoryButton.Margin = new Padding(0);
																				clearAllTagHistoryButton.Name = "btnClearAllTagsHistory";
																				clearAllTagHistoryButton.Size = new Size(216, 24);
																				clearAllTagHistoryButton.TabIndex = 18;
																				clearAllTagHistoryButton.Text = "Clear all tags history";
																				clearAllTagHistoryButton.UseVisualStyleBackColor = true;
																				clearAllTagHistoryButton.Click += ClearAllTagHistory;
																				detailsPanel.Controls.Add(commandButtonPanel);
																				goto case 106;
																			case 106:
																				detailsPanel.Dock = DockStyle.Fill;
																				detailsPanel.Location = new Point(1560, 10);
																				detailsPanel.Margin = new Padding(0);
																				detailsPanel.Name = "flowLayoutPanel3";
																				detailsPanel.Size = new Size(0, 60);
																				detailsPanel.TabIndex = 9;
																				detailsPanel.WrapContents = false;
																				goto IL_0cff;
																			case 21:
																			case 116:
																				goto IL_0cff;
																			case 63:
																				goto IL_0d2b;
																			case 78:
																				goto IL_1079;
																			case 43:
																				goto IL_108a;
																			case 49:
																				goto IL_10a2;
																			case 115:
																				goto IL_11ce;
																			case 86:
																				goto IL_11d9;
																			case 125:
																				saveLrcWhileSavingTagsCheckBox.Size = new Size(252, 18);
																				saveLrcWhileSavingTagsCheckBox.TabIndex = 9;
																				goto case 70;
																			case 70:
																				saveLrcWhileSavingTagsCheckBox.Text = "Save the Lrc file while saving the file tags";
																				saveLrcWhileSavingTagsCheckBox.UseVisualStyleBackColor = true;
																				notifyAreaGroupBox.Controls.Add(notifyAreaOptionsPanel);
																				notifyAreaGroupBox.Location = new Point(0, 165);
																				notifyAreaGroupBox.Margin = new Padding(0, 10, 0, 0);
																				notifyAreaGroupBox.Name = "gbNotifyArea";
																				goto case 5;
																			case 5:
																				notifyAreaGroupBox.Padding = new Padding(5);
																				notifyAreaGroupBox.Size = new Size(449, 85);
																				goto case 67;
																			case 67:
																				notifyAreaGroupBox.TabIndex = 14;
																				notifyAreaGroupBox.TabStop = false;
																				num = 52;
																				continue;
																			case 60:
																				goto IL_12e6;
																			case 81:
																				goto IL_1307;
																			case 32:
																				goto IL_1357;
																			case 14:
																				goto IL_1526;
																			case 23:
																				goto IL_1639;
																			case 74:
																				goto IL_16bd;
																			case 97:
																				goto IL_1753;
																			case 25:
																				goto IL_1807;
																			case 64:
																				goto IL_18a8;
																			case 103:
																				goto IL_1983;
																			case 111:
																				goto IL_19a9;
																			case 72:
																				goto IL_1ba0;
																			case 75:
																				goto IL_1bdf;
																			case 112:
																				goto IL_1c1b;
																			case 101:
																				goto IL_1c66;
																			case 92:
																				goto IL_1c7c;
																			case 98:
																				goto IL_1dfe;
																			case 121:
																				goto IL_1e4a;
																			case 2:
																				goto IL_1e60;
																			case 122:
																				goto IL_1f3c;
																			case 45:
																				goto IL_21bd;
																			case 119:
																				restrictedExtensionsTextBox.TabIndex = 14;
																				resetRestrictedExtensionsButton.Location = new Point(398, 0);
																				resetRestrictedExtensionsButton.Margin = new Padding(0);
																				resetRestrictedExtensionsButton.Name = "btnResetRestrictFileExts";
																				resetRestrictedExtensionsButton.Size = new Size(48, 22);
																				resetRestrictedExtensionsButton.TabIndex = 15;
																				resetRestrictedExtensionsButton.Text = "Reset";
																				resetRestrictedExtensionsButton.UseVisualStyleBackColor = true;
																				resetRestrictedExtensionsButton.Click += ResetRestrictedFileExtensions;
																				keepFileUpdateTimeCheckBox.AutoSize = true;
																				keepFileUpdateTimeCheckBox.Location = new Point(6, 51);
																				keepFileUpdateTimeCheckBox.Margin = new Padding(6, 10, 0, 0);
																				keepFileUpdateTimeCheckBox.Name = "cbKeepFileUpdateTime";
																				keepFileUpdateTimeCheckBox.Size = new Size(288, 18);
																				keepFileUpdateTimeCheckBox.TabIndex = 16;
																				keepFileUpdateTimeCheckBox.Text = "Preserve file modification time when saving tags";
																				goto case 48;
																			case 48:
																				keepFileUpdateTimeCheckBox.UseVisualStyleBackColor = true;
																				checkForUpdatesOnStartupCheckBox.AutoSize = true;
																				checkForUpdatesOnStartupCheckBox.Location = new Point(6, 79);
																				checkForUpdatesOnStartupCheckBox.Margin = new Padding(6, 10, 0, 0);
																				checkForUpdatesOnStartupCheckBox.Name = "cbCheckForUpdatesOnStartup";
																				checkForUpdatesOnStartupCheckBox.Size = new Size(187, 18);
																				checkForUpdatesOnStartupCheckBox.TabIndex = 17;
																				checkForUpdatesOnStartupCheckBox.Text = "Check for updates on startup";
																				checkForUpdatesOnStartupCheckBox.UseVisualStyleBackColor = true;
																				tagHistoryPanel.Controls.Add(clearAllTagHistoryButton);
																				tagHistoryPanel.Location = new Point(6, 112);
																				tagHistoryPanel.Margin = new Padding(6, 15, 0, 0);
																				tagHistoryPanel.Name = "flowLayoutPanel18";
																				goto case 93;
																			case 118:
																				itunesCountryComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
																				itunesCountryComboBox.FormattingEnabled = true;
																				itunesCountryComboBox.Location = new Point(60, 0);
																				itunesCountryComboBox.Margin = new Padding(6, 0, 0, 0);
																				itunesCountryComboBox.Name = "cbItunesCountry";
																				itunesCountryComboBox.Size = new Size(180, 22);
																				goto case 24;
																			case 24:
																				itunesCountryComboBox.TabIndex = 1;
																				sourceLimitPanel.Controls.Add(webSearchItemLimitLabel);
																				sourceLimitPanel.Controls.Add(webSearchItemLimitTrackBar);
																				sourceLimitPanel.Location = new Point(420, 475);
																				sourceLimitPanel.Margin = new Padding(0, 10, 0, 0);
																				sourceLimitPanel.Name = "panelWebSearchLimit";
																				sourceLimitPanel.Size = new Size(389, 50);
																				sourceLimitPanel.TabIndex = 8;
																				webSearchItemLimitLabel.AutoSize = true;
																				goto case 61;
																			case 61:
																				webSearchItemLimitLabel.Location = new Point(3, 0);
																				webSearchItemLimitLabel.Name = "lblWebSearchLimit";
																				goto case 102;
																			case 102:
																				webSearchItemLimitLabel.Size = new Size(134, 14);
																				webSearchItemLimitLabel.TabIndex = 0;
																				webSearchItemLimitLabel.Text = "Web search items limit:";
																				goto case 117;
																			case 117:
																				webSearchItemLimitTrackBar.LargeChange = 1;
																				webSearchItemLimitTrackBar.Location = new Point(3, 17);
																				webSearchItemLimitTrackBar.Maximum = 100;
																				webSearchItemLimitTrackBar.Minimum = 1;
																				webSearchItemLimitTrackBar.Name = "tbWebSearchLimit";
																				webSearchItemLimitTrackBar.Size = new Size(383, 45);
																				webSearchItemLimitTrackBar.TabIndex = 4;
																				webSearchItemLimitTrackBar.TickStyle = TickStyle.TopLeft;
																				webSearchItemLimitTrackBar.Value = 1;
																				goto case 96;
																			case 96:
																				webSearchItemLimitTrackBar.ValueChanged += UpdateWebSearchLimitLabel;
																				webSearchLimitGroupBox.Controls.Add(webSearchLimitPanel);
																				goto case 44;
																			case 44:
																				webSearchLimitGroupBox.Location = new Point(420, 525);
																				goto case 18;
																			case 18:
																				webSearchLimitGroupBox.Margin = new Padding(0);
																				webSearchLimitGroupBox.Name = "gbWebSearchLimit";
																				webSearchLimitGroupBox.Padding = new Padding(0);
																				webSearchLimitGroupBox.Size = new Size(395, 225);
																				goto case 47;
																			case 47:
																				webSearchLimitGroupBox.TabIndex = 11;
																				webSearchLimitGroupBox.TabStop = false;
																				webSearchLimitGroupBox.Text = "Web search items limit";
																				webSearchLimitPanel.BackColor = Color.Transparent;
																				webSearchLimitPanel.Controls.Add(coverSourceLimitLabel);
																				webSearchLimitPanel.Controls.Add(coverSourceLimitTrackBar);
																				webSearchLimitPanel.Controls.Add(lyricSourceLimitLabel);
																				webSearchLimitPanel.Controls.Add(lyricSourceLimitTrackBar);
																				webSearchLimitPanel.Controls.Add(tagSourceLimitLabel);
																				webSearchLimitPanel.Controls.Add(tagSourceLimitTrackBar);
																				webSearchLimitPanel.Dock = DockStyle.Fill;
																				webSearchLimitPanel.Location = new Point(0, 15);
																				webSearchLimitPanel.Margin = new Padding(0);
																				webSearchLimitPanel.Name = "layoutgbWebSearchLimit";
																				webSearchLimitPanel.Padding = new Padding(5);
																				webSearchLimitPanel.Size = new Size(395, 210);
																				webSearchLimitPanel.TabIndex = 0;
																				coverSourceLimitLabel.AutoSize = true;
																				coverSourceLimitLabel.Location = new Point(8, 8);
																				coverSourceLimitLabel.Margin = new Padding(3, 3, 3, 0);
																				coverSourceLimitLabel.Name = "lblWSILPictureSources";
																				coverSourceLimitLabel.Size = new Size(94, 14);
																				coverSourceLimitLabel.TabIndex = 0;
																				coverSourceLimitLabel.Text = "Picture sources:";
																				coverSourceLimitTrackBar.LargeChange = 1;
																				coverSourceLimitTrackBar.Location = new Point(5, 25);
																				coverSourceLimitTrackBar.Margin = new Padding(0, 3, 0, 3);
																				coverSourceLimitTrackBar.Maximum = 100;
																				goto case 91;
																			case 91:
																				coverSourceLimitTrackBar.Minimum = 1;
																				coverSourceLimitTrackBar.Name = "tbWSILPictureSources";
																				goto case 126;
																			case 114:
																				pictureResolutionLimitTrackBar.Maximum = 391;
																				num = 127;
																				continue;
																			case 110:
																				saveLrcWhileSavingTagsCheckBox.Name = "cbSaveLrcFileWhileSaveTags";
																				goto case 125;
																			case 109:
																				goto IL_2923;
																			case 108:
																				itunesCountryPanel.Location = new Point(3, 0);
																				num = 88;
																				continue;
																			case 28:
																				lrcEncodingLabel.Margin = new Padding(0, 4, 0, 0);
																				goto case 53;
																			case 53:
																				lrcEncodingLabel.Name = "lblLrcEncoding";
																				lrcEncodingLabel.Size = new Size(61, 14);
																				lrcEncodingLabel.TabIndex = 0;
																				lrcEncodingLabel.Text = "Encoding:";
																				lrcEncodingComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
																				lrcEncodingComboBox.FormattingEnabled = true;
																				lrcEncodingComboBox.Location = new Point(61, 0);
																				lrcEncodingComboBox.Margin = new Padding(0);
																				lrcEncodingComboBox.Name = "cbLrcEncoding";
																				lrcEncodingComboBox.Size = new Size(121, 22);
																				lrcEncodingComboBox.TabIndex = 1;
																				lrcDirectoryPanel.Controls.Add(lrcDirectoryLabel);
																				lrcDirectoryPanel.Controls.Add(lrcDirectoryTextBox);
																				lrcDirectoryPanel.Controls.Add(browseLrcDirectoryButton);
																				lrcDirectoryPanel.Controls.Add(useLocalLrcDirectoryButton);
																				lrcDirectoryPanel.Location = new Point(0, 37);
																				lrcDirectoryPanel.Margin = new Padding(0, 10, 0, 0);
																				goto case 31;
																			case 31:
																				lrcDirectoryPanel.Name = "flowLayoutPanel15";
																				lrcDirectoryPanel.Size = new Size(390, 22);
																				lrcDirectoryPanel.TabIndex = 10;
																				lrcDirectoryLabel.AutoSize = true;
																				lrcDirectoryLabel.Location = new Point(0, 4);
																				lrcDirectoryLabel.Margin = new Padding(0, 4, 0, 0);
																				lrcDirectoryLabel.Name = "lblLrcSaveDir";
																				lrcDirectoryLabel.Size = new Size(89, 14);
																				lrcDirectoryLabel.TabIndex = 0;
																				lrcDirectoryLabel.Text = "Save directory:";
																				lrcDirectoryTextBox.Location = new Point(89, 0);
																				lrcDirectoryTextBox.Margin = new Padding(0);
																				lrcDirectoryTextBox.Name = "tbLrcSaveDir";
																				lrcDirectoryTextBox.Size = new Size(241, 22);
																				lrcDirectoryTextBox.TabIndex = 1;
																				browseLrcDirectoryButton.Location = new Point(330, 0);
																				browseLrcDirectoryButton.Margin = new Padding(0);
																				browseLrcDirectoryButton.Name = "btnLrcSaveDir";
																				num = 36;
																				continue;
																			case 105:
																				restrictedExtensionsPanel.FlowDirection = FlowDirection.TopDown;
																				restrictedExtensionsPanel.Location = new Point(0, 260);
																				restrictedExtensionsPanel.Margin = new Padding(0, 10, 0, 0);
																				restrictedExtensionsPanel.Name = "panelOthers1Other";
																				restrictedExtensionsPanel.Size = new Size(467, 170);
																				goto case 12;
																			case 12:
																				restrictedExtensionsPanel.TabIndex = 13;
																				restrictedExtensionsLabel.AutoSize = true;
																				restrictedExtensionsLabel.Location = new Point(3, 0);
																				goto case 33;
																			case 33:
																				restrictedExtensionsLabel.Name = "lblRestrictFileExts";
																				restrictedExtensionsLabel.Size = new Size(304, 14);
																				restrictedExtensionsLabel.TabIndex = 21;
																				restrictedExtensionsLabel.Text = "Only files with the following extensions are processed:";
																				restrictedExtensionsEditorPanel.Controls.Add(restrictedExtensionsTextBox);
																				restrictedExtensionsEditorPanel.Controls.Add(resetRestrictedExtensionsButton);
																				restrictedExtensionsEditorPanel.Location = new Point(6, 19);
																				restrictedExtensionsEditorPanel.Margin = new Padding(6, 5, 0, 0);
																				restrictedExtensionsEditorPanel.Name = "flowLayoutPanel17";
																				num = 68;
																				continue;
																			case 99:
																				notifyAreaOptionsPanel.ResumeLayout(performLayout: false);
																				notifyAreaOptionsPanel.PerformLayout();
																				restrictedExtensionsPanel.ResumeLayout(performLayout: false);
																				restrictedExtensionsPanel.PerformLayout();
																				restrictedExtensionsEditorPanel.ResumeLayout(performLayout: false);
																				restrictedExtensionsEditorPanel.PerformLayout();
																				tagHistoryPanel.ResumeLayout(false);
																				tagHistoryPanel.PerformLayout();
																				detailsPanel.ResumeLayout(performLayout: false);
																				commandButtonPanel.ResumeLayout(performLayout: false);
																				ResumeLayout(performLayout: false);
																				num = 128;
																				continue;
																			case 94:
																				lrcFileOptionsPanel.Name = "flowLayoutPanel14";
																				lrcFileOptionsPanel.Size = new Size(442, 130);
																				lrcFileOptionsPanel.TabIndex = 0;
																				lrcEncodingPanel.Controls.Add(lrcEncodingLabel);
																				lrcEncodingPanel.Controls.Add(lrcEncodingComboBox);
																				lrcEncodingPanel.Location = new Point(0, 5);
																				lrcEncodingPanel.Margin = new Padding(0, 5, 0, 0);
																				lrcEncodingPanel.Name = "flowLayoutPanel10";
																				lrcEncodingPanel.Size = new Size(332, 22);
																				lrcEncodingPanel.TabIndex = 9;
																				lrcEncodingLabel.AutoSize = true;
																				lrcEncodingLabel.Location = new Point(0, 4);
																				num = 28;
																				continue;
																			case 88:
																				itunesCountryPanel.Margin = new Padding(3, 0, 0, 0);
																				itunesCountryPanel.Name = "flowLayoutPanel12";
																				itunesCountryPanel.Size = new Size(332, 22);
																				itunesCountryPanel.TabIndex = 5;
																				goto case 35;
																			case 4:
																				itunesCountryLabel.Name = "lblItunesCountry";
																				itunesCountryLabel.Size = new Size(54, 14);
																				itunesCountryLabel.TabIndex = 0;
																				itunesCountryLabel.Text = "Country:";
																				num = 118;
																				continue;
																			case 35:
																				itunesCountryLabel.AutoSize = true;
																				itunesCountryLabel.Location = new Point(0, 4);
																				itunesCountryLabel.Margin = new Padding(0, 4, 0, 0);
																				goto case 4;
																			case 89:
																				lrcFilenameFormatComboBox.Name = "cbLrcFilenameFormat";
																				lrcFilenameFormatComboBox.Size = new Size(155, 22);
																				lrcFilenameFormatComboBox.TabIndex = 1;
																				saveLrcWhileSavingTagsCheckBox.AutoSize = true;
																				saveLrcWhileSavingTagsCheckBox.Location = new Point(3, 101);
																				saveLrcWhileSavingTagsCheckBox.Margin = new Padding(3, 10, 0, 0);
																				goto case 110;
																			case 87:
																				pictureResolutionLimitLabel.TabIndex = 22;
																				pictureResolutionLimitLabel.Text = "picture resolution embedded in file tags: ";
																				pictureResolutionLimitTrackBar.AutoSize = false;
																				goto case 62;
																			case 62:
																				pictureResolutionLimitTrackBar.LargeChange = 1;
																				goto case 19;
																			case 19:
																				pictureResolutionLimitTrackBar.Location = new Point(3, 132);
																				goto case 114;
																			case 85:
																				simplifiedToTraditionalRadioButton.Size = new Size(151, 18);
																				simplifiedToTraditionalRadioButton.TabIndex = 2;
																				simplifiedToTraditionalRadioButton.TabStop = true;
																				simplifiedToTraditionalRadioButton.Text = "Simplified to Traditional";
																				simplifiedToTraditionalRadioButton.UseVisualStyleBackColor = true;
																				lyricCleanupOptionsPanel.Controls.Add(reformatTimestampCheckBox);
																				lyricCleanupOptionsPanel.Controls.Add(removeTimestampCheckBox);
																				lyricCleanupOptionsPanel.Controls.Add(removeBlankLyricLinesCheckBox);
																				lyricCleanupOptionsPanel.Controls.Add(removeLyricHeaderTagsCheckBox);
																				lyricCleanupOptionsPanel.FlowDirection = FlowDirection.TopDown;
																				lyricCleanupOptionsPanel.Location = new Point(6, 650);
																				lyricCleanupOptionsPanel.Margin = new Padding(0, 5, 0, 0);
																				lyricCleanupOptionsPanel.Name = "panelLyricDownload";
																				lyricCleanupOptionsPanel.Padding = new Padding(5);
																				lyricCleanupOptionsPanel.Size = new Size(350, 127);
																				goto IL_327a;
																			case 37:
																				goto IL_327a;
																			case 17:
																				goto IL_32fc;
																			case 65:
																				goto IL_3327;
																			case 26:
																				goto IL_336c;
																			case 84:
																				lrcFileGroupBox.TabIndex = 12;
																				lrcFileGroupBox.TabStop = false;
																				lrcFileGroupBox.Text = "Lrc File";
																				lrcFileOptionsPanel.Controls.Add(lrcEncodingPanel);
																				lrcFileOptionsPanel.Controls.Add(lrcDirectoryPanel);
																				lrcFileOptionsPanel.Controls.Add(lrcFilenameFormatPanel);
																				lrcFileOptionsPanel.Controls.Add(saveLrcWhileSavingTagsCheckBox);
																				lrcFileOptionsPanel.Dock = DockStyle.Fill;
																				lrcFileOptionsPanel.FlowDirection = FlowDirection.TopDown;
																				lrcFileOptionsPanel.Location = new Point(5, 20);
																				lrcFileOptionsPanel.Margin = new Padding(0);
																				goto case 94;
																			case 16:
																			case 82:
																				goto IL_345b;
																			case 80:
																				chineseConversionLabel.TabIndex = 4;
																				chineseConversionLabel.Text = "Chinese simplified and traditional conversion:";
																				chineseConversionPanel.Controls.Add(noChineseConversionRadioButton);
																				chineseConversionPanel.Controls.Add(traditionalToSimplifiedRadioButton);
																				chineseConversionPanel.Controls.Add(simplifiedToTraditionalRadioButton);
																				chineseConversionPanel.Location = new Point(5, 211);
																				goto case 9;
																			case 9:
																				chineseConversionPanel.Margin = new Padding(5, 5, 0, 0);
																				chineseConversionPanel.Name = "flowLayoutPanel7";
																				chineseConversionPanel.Size = new Size(389, 18);
																				chineseConversionPanel.TabIndex = 5;
																				noChineseConversionRadioButton.AutoSize = true;
																				noChineseConversionRadioButton.Location = new Point(0, 0);
																				noChineseConversionRadioButton.Margin = new Padding(0);
																				noChineseConversionRadioButton.Name = "rbZhConvUndefine";
																				noChineseConversionRadioButton.Size = new Size(74, 18);
																				noChineseConversionRadioButton.TabIndex = 0;
																				noChineseConversionRadioButton.TabStop = true;
																				noChineseConversionRadioButton.Text = "Undefine";
																				noChineseConversionRadioButton.UseVisualStyleBackColor = true;
																				traditionalToSimplifiedRadioButton.AutoSize = true;
																				traditionalToSimplifiedRadioButton.Location = new Point(74, 0);
																				traditionalToSimplifiedRadioButton.Margin = new Padding(0);
																				traditionalToSimplifiedRadioButton.Name = "rbZhConvCHTToCHS";
																				traditionalToSimplifiedRadioButton.Size = new Size(151, 18);
																				traditionalToSimplifiedRadioButton.TabIndex = 1;
																				traditionalToSimplifiedRadioButton.TabStop = true;
																				traditionalToSimplifiedRadioButton.Text = "Traditional to Simplified";
																				traditionalToSimplifiedRadioButton.UseVisualStyleBackColor = true;
																				simplifiedToTraditionalRadioButton.AutoSize = true;
																				simplifiedToTraditionalRadioButton.Location = new Point(225, 0);
																				simplifiedToTraditionalRadioButton.Margin = new Padding(0);
																				simplifiedToTraditionalRadioButton.Name = "rbZhConvCHSToCHT";
																				goto case 85;
																			case 79:
																				tagSourceLimitLabel.AutoSize = true;
																				tagSourceLimitLabel.Location = new Point(10, 144);
																				tagSourceLimitLabel.Margin = new Padding(5, 3, 3, 0);
																				tagSourceLimitLabel.Name = "lblWSILCombSources";
																				goto case 15;
																			case 77:
																				pictureSizeLimitLabel.AutoSize = true;
																				pictureSizeLimitLabel.Location = new Point(3, 53);
																				pictureSizeLimitLabel.Margin = new Padding(3, 10, 3, 0);
																				pictureSizeLimitLabel.Name = "lblPictureSizeLimits";
																				pictureSizeLimitLabel.Size = new Size(250, 14);
																				pictureSizeLimitLabel.TabIndex = 2;
																				pictureSizeLimitLabel.Text = "Size limits for pictures embedded in file tags:";
																				pictureSizeLimitTrackBar.AutoSize = false;
																				pictureSizeLimitTrackBar.LargeChange = 1;
																				goto case 27;
																			case 27:
																				pictureSizeLimitTrackBar.Location = new Point(3, 70);
																				pictureSizeLimitTrackBar.Maximum = 44;
																				goto case 41;
																			case 41:
																				pictureSizeLimitTrackBar.Name = "tbPictureSizeLimits";
																				pictureSizeLimitTrackBar.Size = new Size(409, 32);
																				pictureSizeLimitTrackBar.TabIndex = 3;
																				pictureSizeLimitTrackBar.TickStyle = TickStyle.TopLeft;
																		pictureSizeLimitTrackBar.ValueChanged += UpdatePictureSizeLimitLabel;
																				pictureResolutionLimitLabel.AutoSize = true;
																				pictureResolutionLimitLabel.Location = new Point(3, 115);
																				pictureResolutionLimitLabel.Margin = new Padding(3, 10, 3, 0);
																				pictureResolutionLimitLabel.Name = "lblPictureResolution";
																				pictureResolutionLimitLabel.Size = new Size(232, 14);
																				num = 87;
																				continue;
																			case 76:
																				lrcFileGroupBox.Size = new Size(452, 155);
																				goto case 84;
																			case 68:
																				restrictedExtensionsEditorPanel.Size = new Size(446, 22);
																				restrictedExtensionsEditorPanel.TabIndex = 22;
																				restrictedExtensionsTextBox.Location = new Point(0, 0);
																				restrictedExtensionsTextBox.Margin = new Padding(0);
																				restrictedExtensionsTextBox.Name = "tbRestrictFileExts";
																				restrictedExtensionsTextBox.Size = new Size(398, 22);
																				goto case 119;
																			case 66:
																				saveAndNotificationOptionsPanel.Name = "panelOthers1";
																				saveAndNotificationOptionsPanel.Size = new Size(500, 500);
																				saveAndNotificationOptionsPanel.TabIndex = 13;
																				lrcFileGroupBox.Controls.Add(lrcFileOptionsPanel);
																				lrcFileGroupBox.Location = new Point(0, 0);
																				lrcFileGroupBox.Margin = new Padding(0);
																				lrcFileGroupBox.Name = "gbLrcFile";
																				lrcFileGroupBox.Padding = new Padding(5);
																				num = 76;
																				continue;
																			case 59:
																				restrictedExtensionsPanel.Controls.Add(restrictedExtensionsEditorPanel);
																				restrictedExtensionsPanel.Controls.Add(keepFileUpdateTimeCheckBox);
																				restrictedExtensionsPanel.Controls.Add(checkForUpdatesOnStartupCheckBox);
																				restrictedExtensionsPanel.Controls.Add(tagHistoryPanel);
																				goto case 105;
																			case 57:
																				albumSearchConditionCheckBox.CheckState = CheckState.Checked;
																				albumSearchConditionCheckBox.Location = new Point(203, 2);
																				albumSearchConditionCheckBox.Margin = new Padding(6, 2, 0, 0);
																				albumSearchConditionCheckBox.Name = "cbWebSearchConditionAlbum";
																				albumSearchConditionCheckBox.Size = new Size(60, 18);
																				albumSearchConditionCheckBox.TabIndex = 3;
																				albumSearchConditionCheckBox.Text = "Album";
																				albumSearchConditionCheckBox.UseVisualStyleBackColor = true;
																				goto case 77;
																			case 52:
																				notifyAreaGroupBox.Text = "Notification Area";
																				notifyAreaOptionsPanel.Controls.Add(alwaysShowNotifyIconCheckBox);
																				notifyAreaOptionsPanel.Controls.Add(minimizeToNotifyAreaCheckBox);
																				notifyAreaOptionsPanel.Dock = DockStyle.Fill;
																				notifyAreaOptionsPanel.FlowDirection = FlowDirection.TopDown;
																				notifyAreaOptionsPanel.Location = new Point(5, 20);
																				notifyAreaOptionsPanel.Margin = new Padding(0);
																				notifyAreaOptionsPanel.Name = "flowLayoutPanel19";
																				notifyAreaOptionsPanel.Size = new Size(439, 60);
																				notifyAreaOptionsPanel.TabIndex = 0;
																				alwaysShowNotifyIconCheckBox.AutoSize = true;
																				alwaysShowNotifyIconCheckBox.Location = new Point(3, 5);
																				alwaysShowNotifyIconCheckBox.Margin = new Padding(3, 5, 0, 0);
																				alwaysShowNotifyIconCheckBox.Name = "cbAlwaysShowIconInNofiArea";
																				alwaysShowNotifyIconCheckBox.Size = new Size(226, 18);
																				alwaysShowNotifyIconCheckBox.TabIndex = 17;
																				alwaysShowNotifyIconCheckBox.Text = "Always show icon in notification area";
																				alwaysShowNotifyIconCheckBox.UseVisualStyleBackColor = true;
																				minimizeToNotifyAreaCheckBox.AutoSize = true;
																				minimizeToNotifyAreaCheckBox.Location = new Point(3, 33);
																				goto case 0;
																			case 0:
																				minimizeToNotifyAreaCheckBox.Margin = new Padding(3, 10, 0, 0);
																				minimizeToNotifyAreaCheckBox.Name = "cbMinimizeToNotiArea";
																				minimizeToNotifyAreaCheckBox.Size = new Size(177, 18);
																				minimizeToNotifyAreaCheckBox.TabIndex = 18;
																				minimizeToNotifyAreaCheckBox.Text = "Minimize to notification area";
																				minimizeToNotifyAreaCheckBox.UseVisualStyleBackColor = true;
																				restrictedExtensionsPanel.Controls.Add(restrictedExtensionsLabel);
																				num = 59;
																				continue;
																			case 51:
																				useLocalLrcDirectoryButton.Text = ".";
																				useLocalLrcDirectoryButton.UseVisualStyleBackColor = true;
																				useLocalLrcDirectoryButton.Click += UseLocalLyricSaveDirectory;
																				lrcFilenameFormatPanel.Controls.Add(lrcFilenameFormatLabel);
																				lrcFilenameFormatPanel.Controls.Add(lrcFilenameFormatComboBox);
																				lrcFilenameFormatPanel.Location = new Point(0, 69);
																				lrcFilenameFormatPanel.Margin = new Padding(0, 10, 0, 0);
																				lrcFilenameFormatPanel.Name = "flowLayoutPanel16";
																				lrcFilenameFormatPanel.Size = new Size(332, 22);
																				lrcFilenameFormatPanel.TabIndex = 11;
																				lrcFilenameFormatLabel.AutoSize = true;
																				lrcFilenameFormatLabel.Location = new Point(0, 4);
																				lrcFilenameFormatLabel.Margin = new Padding(0, 4, 0, 0);
																				lrcFilenameFormatLabel.Name = "lblLrcFilenameFormat";
																				lrcFilenameFormatLabel.Size = new Size(98, 14);
																				lrcFilenameFormatLabel.TabIndex = 0;
																				lrcFilenameFormatLabel.Text = "Filename format:";
																				lrcFilenameFormatComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
																				lrcFilenameFormatComboBox.FormattingEnabled = true;
																				lrcFilenameFormatComboBox.Location = new Point(98, 0);
																				lrcFilenameFormatComboBox.Margin = new Padding(0);
																				goto case 89;
																			case 39:
																				fileFilterPanel.PerformLayout();
																				itunesParametersGroupBox.ResumeLayout(performLayout: false);
																				itunesParametersPanel.ResumeLayout(performLayout: false);
																				itunesCountryPanel.ResumeLayout(performLayout: false);
																				itunesCountryPanel.PerformLayout();
																				sourceLimitPanel.ResumeLayout(performLayout: false);
																				sourceLimitPanel.PerformLayout();
																				((ISupportInitialize)webSearchItemLimitTrackBar).EndInit();
																				webSearchLimitGroupBox.ResumeLayout(performLayout: false);
																				webSearchLimitPanel.ResumeLayout(performLayout: false);
																				webSearchLimitPanel.PerformLayout();
																				((ISupportInitialize)coverSourceLimitTrackBar).EndInit();
																				((ISupportInitialize)lyricSourceLimitTrackBar).EndInit();
																				((ISupportInitialize)tagSourceLimitTrackBar).EndInit();
																				saveAndNotificationOptionsPanel.ResumeLayout(performLayout: false);
																				lrcFileGroupBox.ResumeLayout(performLayout: false);
																				lrcFileOptionsPanel.ResumeLayout(performLayout: false);
																				lrcFileOptionsPanel.PerformLayout();
																				lrcEncodingPanel.ResumeLayout(performLayout: false);
																				lrcEncodingPanel.PerformLayout();
																				lrcDirectoryPanel.ResumeLayout(performLayout: false);
																				lrcDirectoryPanel.PerformLayout();
																				lrcFilenameFormatPanel.ResumeLayout(performLayout: false);
																				lrcFilenameFormatPanel.PerformLayout();
																				notifyAreaGroupBox.ResumeLayout(performLayout: false);
																				goto case 99;
																			case 36:
																				browseLrcDirectoryButton.Size = new Size(30, 22);
																				browseLrcDirectoryButton.TabIndex = 2;
																				browseLrcDirectoryButton.Text = "...";
																				browseLrcDirectoryButton.UseVisualStyleBackColor = true;
																				browseLrcDirectoryButton.Click += BrowseLyricSaveDirectory;
																				useLocalLrcDirectoryButton.Location = new Point(360, 0);
																				useLocalLrcDirectoryButton.Margin = new Padding(0);
																				useLocalLrcDirectoryButton.Name = "btnLrcSaveLocalDir";
																				useLocalLrcDirectoryButton.Size = new Size(30, 22);
																				useLocalLrcDirectoryButton.TabIndex = 3;
																				goto case 51;
																			case 20:
																				itunesParametersGroupBox.Location = new Point(420, 400);
																				itunesParametersGroupBox.Margin = new Padding(0, 0, 0, 10);
																				itunesParametersGroupBox.Name = "panelTagSrcItunesParams";
																				itunesParametersGroupBox.Padding = new Padding(5);
																				itunesParametersGroupBox.Size = new Size(383, 55);
																				itunesParametersGroupBox.TabIndex = 7;
																				itunesParametersGroupBox.TabStop = false;
																				itunesParametersGroupBox.Text = "Parameters";
																				itunesParametersPanel.Controls.Add(itunesCountryPanel);
																				itunesParametersPanel.Dock = DockStyle.Fill;
																				itunesParametersPanel.Location = new Point(5, 20);
																				goto case 13;
																			case 13:
																				itunesParametersPanel.Margin = new Padding(0);
																				itunesParametersPanel.Name = "flowLayoutPanel8";
																				itunesParametersPanel.Size = new Size(373, 30);
																				itunesParametersPanel.TabIndex = 0;
																				itunesCountryPanel.Controls.Add(itunesCountryLabel);
																				itunesCountryPanel.Controls.Add(itunesCountryComboBox);
																				goto case 108;
																			case 8:
																				id3v2VersionPanel.PerformLayout();
																				fileFilterPanel.ResumeLayout(false);
																				goto case 39;
																			case 71:
																				goto end_IL_0006;
																			case 50:
																				goto end_IL_4311;
																			case 1:
																				goto end_IL_43d7;
																			case 123:
																				goto end_IL_4413;
																			case 38:
																				goto end_IL_4525;
																			case 124:
																				goto end_IL_456c;
																			case 54:
																				goto end_IL_4584;
																			case 83:
																				goto end_IL_45d3;
																			case 95:
																				goto end_IL_4672;
																			case 100:
																				goto end_IL_4706;
																			case 42:
																				goto end_IL_4716;
																			case 29:
																				goto end_IL_47d6;
																			case 6:
																				goto end_IL_4896;
																			case 120:
																				goto end_IL_4909;
																			case 107:
																				goto end_IL_49b3;
																			case 128:
																				return;
																			}
																			break;
																		}
																		continue;
																		IL_327a:
																		lyricCleanupOptionsPanel.TabIndex = 5;
																		reformatTimestampCheckBox.AutoSize = true;
																		reformatTimestampCheckBox.Location = new Point(5, 10);
																		reformatTimestampCheckBox.Margin = new Padding(0, 5, 0, 0);
																		reformatTimestampCheckBox.Name = "cbLyricDlReformatTimetag";
																		reformatTimestampCheckBox.Size = new Size(343, 18);
																		reformatTimestampCheckBox.TabIndex = 0;
																		reformatTimestampCheckBox.Text = "Reformat timetag (Three milliseconds to two milliseconds)";
																		goto IL_32fc;
																		IL_32fc:
																		reformatTimestampCheckBox.UseVisualStyleBackColor = true;
																		removeTimestampCheckBox.AutoSize = true;
																		removeTimestampCheckBox.Location = new Point(5, 38);
																		goto IL_3327;
																		IL_3327:
																		removeTimestampCheckBox.Margin = new Padding(0, 10, 3, 3);
																		removeTimestampCheckBox.Name = "cbLyricDlRemoveTimetag";
																		removeTimestampCheckBox.Size = new Size(116, 18);
																		removeTimestampCheckBox.TabIndex = 1;
																		goto IL_336c;
																		IL_336c:
																		removeTimestampCheckBox.Text = "Remove timetag";
																		removeTimestampCheckBox.UseVisualStyleBackColor = true;
																		removeBlankLyricLinesCheckBox.AutoSize = true;
																		goto IL_2923;
																		IL_2923:
																		removeBlankLyricLinesCheckBox.Location = new Point(5, 69);
																		removeBlankLyricLinesCheckBox.Margin = new Padding(0, 10, 3, 3);
																		removeBlankLyricLinesCheckBox.Name = "cbLyricDlDeletelinesofblanktext";
																		removeBlankLyricLinesCheckBox.Size = new Size(163, 18);
																		removeBlankLyricLinesCheckBox.TabIndex = 2;
																		removeBlankLyricLinesCheckBox.Text = "Delete lines of blank text";
																		removeBlankLyricLinesCheckBox.UseVisualStyleBackColor = true;
																		removeLyricHeaderTagsCheckBox.AutoSize = true;
																		removeLyricHeaderTagsCheckBox.Location = new Point(5, 100);
																		removeLyricHeaderTagsCheckBox.Margin = new Padding(0, 10, 3, 3);
																		removeLyricHeaderTagsCheckBox.Name = "cbLyricDlDeleteheadtags";
																		removeLyricHeaderTagsCheckBox.Size = new Size(120, 18);
																		removeLyricHeaderTagsCheckBox.TabIndex = 3;
																		removeLyricHeaderTagsCheckBox.Text = "Delete head tags";
																		removeLyricHeaderTagsCheckBox.UseVisualStyleBackColor = true;
																		searchAndTagOptionsPanel.Controls.Add(webSearchCriteriaLabel);
																		searchAndTagOptionsPanel.Controls.Add(webSearchCriteriaPanel);
																		searchAndTagOptionsPanel.Controls.Add(pictureSizeLimitLabel);
																		num = 121;
																		goto IL_40fd;
																		IL_1e4a:
																		searchAndTagOptionsPanel.Controls.Add(pictureSizeLimitTrackBar);
																		goto IL_1e60;
																		IL_1e60:
																		searchAndTagOptionsPanel.Controls.Add(pictureResolutionLimitLabel);
																		searchAndTagOptionsPanel.Controls.Add(pictureResolutionLimitTrackBar);
																		searchAndTagOptionsPanel.Controls.Add(pictureFormatLimitLabel);
																		searchAndTagOptionsPanel.Controls.Add(pictureFormatLimitComboBox);
																		searchAndTagOptionsPanel.Controls.Add(artistConnectorLabel);
																		searchAndTagOptionsPanel.Controls.Add(artistConnectorComboBox);
																		searchAndTagOptionsPanel.Controls.Add(id3v2VersionPanel);
																		searchAndTagOptionsPanel.Controls.Add(fileFilterLabel);
																		searchAndTagOptionsPanel.Controls.Add(fileFilterPanel);
																		searchAndTagOptionsPanel.Controls.Add(commentTagLabel);
																		goto IL_1f3c;
																		IL_1f3c:
																		searchAndTagOptionsPanel.Controls.Add(writeNetEaseCommentKeyCheckBox);
																		searchAndTagOptionsPanel.FlowDirection = FlowDirection.TopDown;
																		searchAndTagOptionsPanel.Location = new Point(420, 0);
																		searchAndTagOptionsPanel.Margin = new Padding(0);
																		searchAndTagOptionsPanel.Name = "panelOthers";
																		searchAndTagOptionsPanel.Size = new Size(467, 400);
																		searchAndTagOptionsPanel.TabIndex = 6;
																		webSearchCriteriaLabel.AutoSize = true;
																		webSearchCriteriaLabel.Location = new Point(3, 0);
																		webSearchCriteriaLabel.Name = "lblWebSearchCriteria";
																		webSearchCriteriaLabel.Size = new Size(116, 14);
																		webSearchCriteriaLabel.TabIndex = 0;
																		webSearchCriteriaLabel.Text = "Web search criteria:";
																		webSearchCriteriaPanel.Controls.Add(titleSearchEnabledCheckBox);
																		webSearchCriteriaPanel.Controls.Add(titleSearchModeComboBox);
																		webSearchCriteriaPanel.Controls.Add(artistSearchConditionCheckBox);
																		webSearchCriteriaPanel.Controls.Add(albumSearchConditionCheckBox);
																		webSearchCriteriaPanel.Location = new Point(6, 19);
																		webSearchCriteriaPanel.Margin = new Padding(6, 5, 0, 0);
																		webSearchCriteriaPanel.Name = "flowLayoutPanel9";
																		webSearchCriteriaPanel.Size = new Size(344, 24);
																		webSearchCriteriaPanel.TabIndex = 1;
																		titleSearchEnabledCheckBox.AutoSize = true;
																		titleSearchEnabledCheckBox.Checked = true;
																		titleSearchEnabledCheckBox.CheckState = CheckState.Checked;
																		titleSearchEnabledCheckBox.Enabled = false;
																		titleSearchEnabledCheckBox.Location = new Point(0, 4);
																		titleSearchEnabledCheckBox.Margin = new Padding(0, 4, 0, 0);
																		titleSearchEnabledCheckBox.Name = "checkBox3";
																		titleSearchEnabledCheckBox.Size = new Size(15, 14);
																		titleSearchEnabledCheckBox.TabIndex = 0;
																		titleSearchEnabledCheckBox.UseVisualStyleBackColor = true;
																		titleSearchModeComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
																		titleSearchModeComboBox.FormattingEnabled = true;
																		titleSearchModeComboBox.Location = new Point(15, 0);
																		titleSearchModeComboBox.Margin = new Padding(0);
																		titleSearchModeComboBox.Name = "cbWebSearchConditionTitle";
																		titleSearchModeComboBox.Size = new Size(121, 22);
																		goto IL_21bd;
																		IL_21bd:
																		titleSearchModeComboBox.TabIndex = 1;
																		artistSearchConditionCheckBox.AutoSize = true;
																		artistSearchConditionCheckBox.Checked = true;
																		artistSearchConditionCheckBox.CheckState = CheckState.Checked;
																		artistSearchConditionCheckBox.Location = new Point(142, 2);
																		artistSearchConditionCheckBox.Margin = new Padding(6, 2, 0, 0);
																		artistSearchConditionCheckBox.Name = "cbWebSearchConditionArtist";
																		artistSearchConditionCheckBox.Size = new Size(55, 18);
																		artistSearchConditionCheckBox.TabIndex = 2;
																		artistSearchConditionCheckBox.Text = "Artist";
																		artistSearchConditionCheckBox.UseVisualStyleBackColor = true;
																		albumSearchConditionCheckBox.AutoSize = true;
																		albumSearchConditionCheckBox.Checked = true;
																		num = 57;
																		goto IL_40fd;
																		IL_0cff:
																		commandButtonPanel.Controls.Add(okButton);
																		commandButtonPanel.Controls.Add(cancelButton);
																		goto IL_0d2b;
																		IL_0d2b:
																		commandButtonPanel.Location = new Point(0, 12);
																		commandButtonPanel.Margin = new Padding(0, 12, 0, 0);
																		commandButtonPanel.Name = "flowLayoutPanel4";
																		commandButtonPanel.Size = new Size(180, 30);
																		commandButtonPanel.TabIndex = 6;
																		okButton.Location = new Point(0, 0);
																		okButton.Margin = new Padding(0);
																		okButton.Name = "btnOK";
																		okButton.Size = new Size(80, 30);
																		okButton.TabIndex = 1;
																		okButton.Text = "OK";
																		okButton.UseVisualStyleBackColor = true;
																		okButton.Click += SaveOptionsAndClose;
																		cancelButton.Location = new Point(100, 0);
																		cancelButton.Margin = new Padding(20, 0, 0, 0);
																		cancelButton.Name = "btnCancel";
																		cancelButton.Size = new Size(80, 30);
																		cancelButton.TabIndex = 2;
																		cancelButton.Text = "Cancel";
																		cancelButton.UseVisualStyleBackColor = true;
																		cancelButton.Click += CancelOptionsDialog;
																		coverSourceOrderControl.Font = new Font("Tahoma", 9f, FontStyle.Regular, GraphicsUnit.Point, 0);
																		coverSourceOrderControl.Location = new Point(6, 0);
																		coverSourceOrderControl.Margin = new Padding(0);
																		coverSourceOrderControl.Name = "panelTagSrcPicture";
																		coverSourceOrderControl.Size = new Size(350, 120);
																		coverSourceOrderControl.SetSources(null);
																		coverSourceOrderControl.TabIndex = 0;
																		coverSourceOrderControl.Title = null;
																		lyricSourceOrderControl.Font = new Font("Tahoma", 9f, FontStyle.Regular, GraphicsUnit.Point, 0);
																		lyricSourceOrderControl.Location = new Point(6, 130);
																		lyricSourceOrderControl.Margin = new Padding(0, 10, 0, 0);
																		lyricSourceOrderControl.Name = "panelTagSrcLyric";
																		lyricSourceOrderControl.Size = new Size(350, 120);
																		lyricSourceOrderControl.SetSources(null);
																		lyricSourceOrderControl.TabIndex = 1;
																		lyricSourceOrderControl.Title = null;
																		tagSourceOrderControl.Font = new Font("Tahoma", 9f, FontStyle.Regular, GraphicsUnit.Point, 0);
																		tagSourceOrderControl.Location = new Point(6, 260);
																		tagSourceOrderControl.Margin = new Padding(0, 10, 0, 0);
																		tagSourceOrderControl.Name = "panelTagSrcComb";
																		tagSourceOrderControl.Size = new Size(350, 120);
																		tagSourceOrderControl.SetSources(null);
																		tagSourceOrderControl.TabIndex = 2;
																		tagSourceOrderControl.Title = null;
																		base.AutoScaleDimensions = new SizeF(96f, 96f);
																		base.AutoScaleMode = AutoScaleMode.Dpi;
																		ClientSize = new Size(1584, 1161);
																		goto IL_1079;
																		IL_1079:
																		base.Controls.Add(rootLayoutPanel);
																		goto IL_108a;
																		IL_108a:
																		Font = new Font("Tahoma", 9f, FontStyle.Regular, GraphicsUnit.Point, 0);
																		goto IL_10a2;
																		IL_10a2:
																		MinimumSize = new Size(720, 550);
																		base.Name = "FormOptions";
																		base.ShowIcon = false;
																		base.StartPosition = FormStartPosition.CenterParent;
																		Text = "Options";
																		rootLayoutPanel.ResumeLayout(performLayout: false);
																		navigationPanel.ResumeLayout(performLayout: false);
																		mainSplitContainer.Panel1.ResumeLayout(performLayout: false);
																		mainSplitContainer.Panel2.ResumeLayout(performLayout: false);
																		((ISupportInitialize)mainSplitContainer).EndInit();
																		mainSplitContainer.ResumeLayout(performLayout: false);
																		sourceOrderPanel.ResumeLayout(performLayout: false);
																		translatedLyricGroupBox.ResumeLayout(performLayout: false);
																		translatedLyricOptionsPanel.ResumeLayout(performLayout: false);
																		translatedLyricOptionsPanel.PerformLayout();
																		translatedLyricFormatPanel.ResumeLayout(performLayout: false);
																		translatedLyricFormatPanel.PerformLayout();
																		chineseConversionPanel.ResumeLayout(performLayout: false);
																		chineseConversionPanel.PerformLayout();
																		lyricCleanupOptionsPanel.ResumeLayout(performLayout: false);
																		lyricCleanupOptionsPanel.PerformLayout();
																		searchAndTagOptionsPanel.ResumeLayout(false);
																		searchAndTagOptionsPanel.PerformLayout();
																		webSearchCriteriaPanel.ResumeLayout(performLayout: false);
																		webSearchCriteriaPanel.PerformLayout();
																		goto IL_11ce;
																		IL_11ce:
																		((ISupportInitialize)pictureSizeLimitTrackBar).EndInit();
																		goto IL_11d9;
																		continue;
																		end_IL_0006:
																		break;
																	}
																	continue;
																	end_IL_4311:
																	break;
																}
																continue;
																end_IL_43d7:
																break;
															}
															continue;
															end_IL_4413:
															break;
														}
														continue;
														end_IL_4525:
														break;
													}
													continue;
													end_IL_456c:
													break;
												}
												continue;
												end_IL_4584:
												break;
											}
											continue;
											end_IL_45d3:
											break;
										}
										continue;
										end_IL_4672:
										break;
									}
									continue;
									end_IL_4706:
									break;
								}
								continue;
								end_IL_4716:
								break;
							}
							continue;
							end_IL_47d6:
							break;
						}
						continue;
						end_IL_4896:
						break;
					}
					continue;
					end_IL_4909:
					break;
				}
				continue;
				end_IL_49b3:
				break;
			}
		}
	}

	private void AddSourceTreeNode(SearchSource source)
	{
		TreeNode treeNode = new TreeNode();
		treeNode.Name = "TagSrc" + source;
		treeNode.Text = source.GetDisplayName();
		TreeNode node = treeNode;
		optionsTreeView.Nodes["TagSources"].Nodes.Add(node);
	}

	private int GetSearchResultLimit(SourceItem sourceItem)
	{
		if (sourceItem == null)
		{
			return 1;
		}

		if (searchResultLimitsBySource.TryGetValue(sourceItem, out int limit))
		{
			return limit;
		}

		limit = sourceItem.SearchResultLimit;
		searchResultLimitsBySource[sourceItem] = limit;
		return limit;
	}

}





