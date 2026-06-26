using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Resources;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MusicTag.Candidates;
using MusicTag.Composer;
using MusicTag.Consumers;
using MusicTag.Serialization;
using MusicTag.Services;
using MusicTag.States;
using MusicTagWinApp.Adapter;
using MusicTagWinApp.Common;
using MusicTagWinApp.Containers;
using MusicTagWinApp.Exporters;
using MusicTagWinApp.Properties;
using MusicTagWinApp.Web;
using wyDay.Controls;

namespace MusicTagWinApp.Instances;

internal class LyricEditorDialog : Form
{
	private TrackSearchContext searchContext;

	private readonly CancellationTokenSource downloadCancellation;

	private TextBoxFindReplaceController findReplaceController;

	private string lastLoadedLyricText;

	private bool saveAfterCloseRequested;

	private IContainer components;

	private FlowLayoutPanel mainLayoutPanel;

	private TextBox lyricTextBox;

	private FlowLayoutPanel footerPanel;

	private FlowLayoutPanel buttonPanel;

	private Button okButton;

	private Button cancelButton;

	private ContextMenuStrip lyricToolsMenu;

	private ToolStripMenuItem reformatTimestampMenuItem;

	private ToolStripMenuItem removeTimestampMenuItem;

	private ToolStripMenuItem deleteBlankLinesMenuItem;

	private ToolStripMenuItem deleteHeaderTagsMenuItem;

	private ToolStripMenuItem adjustTimestampMenuItem;

	private Panel downloadProgressPanel;

	private Label downloadingLabel;

	private SplitButton searchButton;

	private ContextMenuStrip lyricSourceMenu;

	private ToolStripMenuItem importLrcMenuItem;

	private OpenFileDialog importLrcOpenFileDialog;

	private ToolStripMenuItem resetLyricMenuItem;

	private SplitButton saveAsLrcButton;

	private Button okAndSaveButton;

	private PictureBox progressPictureBox;

	private SplitButton findReplaceButton;

	private ContextMenuStrip findMenu;

	private ToolStripMenuItem findPreviousMenuItem;

	private ToolStripMenuItem findNextMenuItem;

	public void SetSearchContext(TrackSearchContext context)
	{
		searchContext = context;
	}

	private TrackSearchContext GetSearchContext()
	{
		return searchContext;
	}

	private CancellationTokenSource GetDownloadCancellationSource()
	{
		return downloadCancellation;
	}

	public string GetLyricText()
	{
		return lyricTextBox.Text;
	}

	public void SetLyricText(string lyricText)
	{
		if (!base.IsDisposed)
		{
			lyricTextBox.Text = DatabaseMapper.TrimNonEmptyLines(lyricText);
		}
	}

	public bool ShouldSaveAfterClose()
	{
		return saveAfterCloseRequested;
	}

	public LyricEditorDialog()
	{
		downloadCancellation = new CancellationTokenSource();
		lastLoadedLyricText = "";
		InitializeComponent();
		InitializeLocalizedText();
		FontAwesome.Properties searchIconProperties = new FontAwesome.Properties
		{
			Size = DatabaseMapper.ScaleByDpi(24f),
			ShowBorder = false
		};
		FontAwesome.Properties saveIconProperties = new FontAwesome.Properties
		{
			Size = DatabaseMapper.ScaleByDpi(21f),
			ShowBorder = false
		};
		searchButton.Image = FontAwesome.Type.Search.AsImage(searchIconProperties);
		searchButton.Size = new Size(DatabaseMapper.ScaleByDpi(100f), DatabaseMapper.ScaleByDpi(35f));
		searchButton.AutoSize = false;
		saveAsLrcButton.Image = FontAwesome.Type.FloppyO.AsImage(saveIconProperties);
		saveAsLrcButton.Size = new Size(DatabaseMapper.ScaleByDpi(120f), DatabaseMapper.ScaleByDpi(35f));
		saveAsLrcButton.AutoSize = false;
		foreach (SourceItem sourceItem in LyricSearchResult.GetLyricSourceSettings())
		{
			ToolStripItem menuItem = lyricSourceMenu.Items.Add(sourceItem.SearchSource.GetDisplayName());
			menuItem.Tag = sourceItem.SearchSource;
			menuItem.Click += SearchLyric_Click;
		}
		progressPictureBox.Image = DatabaseMapper.LoadResourceBitmap("img_wait");
		UpdateEditorLayout();
		findReplaceController = new TextBoxFindReplaceController(lyricTextBox);
	}

	private void InitializeLocalizedText()
	{
		// 这 6 个右键菜单项的本地化文本只存在于卫星资源 DLL 的 BaseFieldInstance 资源集中
		// (BaseFieldInstance 是本类在反编译恢复前的原始类型名)。恢复时类型改名为
		// LyricEditorDialog,若按新类型名(typeof(LyricEditorDialog))推导资源基名,主程序集与
		// en/zh-CHS/zh-CHT 卫星都没有对应资源集 → MissingManifestResourceException。因此显式按
		// 原始基名读取;配套补回的中性(英文)资源集见 MusicTagWinApp.Instances.BaseFieldInstance.resx。
		ResourceManager resources = new ResourceManager("MusicTagWinApp.Instances.BaseFieldInstance", typeof(LyricEditorDialog).Assembly);
		Text = Resources.lyrics;
		searchButton.Text = Resources.search;
		saveAsLrcButton.Text = Resources.SaveAsLrc;
		okButton.Text = Resources.OK;
		okAndSaveButton.Text = Resources.OkAndSave;
		cancelButton.Text = Resources.Cancel;
		findReplaceButton.Text = Resources.FindOrReplace;
		reformatTimestampMenuItem.Text = resources.GetString("reformatTimetagToolStripMenuItem");
		removeTimestampMenuItem.Text = resources.GetString("removeTimetagToolStripMenuItem");
		deleteBlankLinesMenuItem.Text = resources.GetString("deleteLinesOfBlankTextToolStripMenuItem");
		deleteHeaderTagsMenuItem.Text = resources.GetString("deleteHeadTagsToolStripMenuItem");
		adjustTimestampMenuItem.Text = Resources.adjusttimetag;
		importLrcMenuItem.Text = resources.GetString("importLrcFileToolStripMenuItem");
		resetLyricMenuItem.Text = resources.GetString("resetLyricToolStripMenuItem");
		downloadingLabel.Text = Resources.Msg_Downloading;
		findPreviousMenuItem.Text = Resources.FindPrevious;
		findNextMenuItem.Text = Resources.FindNext;
	}

	protected override void OnShown(EventArgs e)
	{
		base.OnShown(e);
		lastLoadedLyricText = GetLyricText();
		lyricTextBox.SelectionStart = 0;
		lyricTextBox.Focus();
	}

	protected override void OnClosed(EventArgs e)
	{
		base.OnClosed(e);
		GetDownloadCancellationSource().Cancel();
	}

	private void MainLayout_SizeChanged(object sender, EventArgs e)
	{
		UpdateEditorLayout();
	}

	private void UpdateEditorLayout()
	{
		lyricTextBox.Width = mainLayoutPanel.Width;
		lyricTextBox.Height = mainLayoutPanel.Height - footerPanel.Height;
		footerPanel.Width = mainLayoutPanel.Width;
		int horizontalMargin = (footerPanel.Width - buttonPanel.Width) / 2;
		int verticalMargin = (footerPanel.Height - buttonPanel.Height) / 2;
		buttonPanel.Margin = new Padding(horizontalMargin, verticalMargin, 0, 0);
	}

	private void OkButton_Click(object sender, EventArgs e)
	{
		base.DialogResult = DialogResult.OK;
		Close();
	}

	private void OkAndSaveButton_Click(object sender, EventArgs e)
	{
		saveAfterCloseRequested = true;
		base.DialogResult = DialogResult.OK;
		Close();
	}

	private void CancelButton_Click(object sender, EventArgs e)
	{
		base.DialogResult = DialogResult.Cancel;
		Close();
	}

	private void ShowDownloadProgress()
	{
		foreach (Control control in Controls)
		{
			control.Enabled = control == downloadProgressPanel;
		}

		int left = (ClientSize.Width - downloadProgressPanel.Width) / 2;
		int top = (ClientSize.Height - downloadProgressPanel.Height) / 2;
		downloadProgressPanel.Location = new Point(left, top);
		downloadProgressPanel.Show();
	}

	private void HideDownloadProgress()
	{
		foreach (Control control in Controls)
		{
			control.Enabled = true;
		}

		downloadProgressPanel.Hide();
	}

	private void SearchLyric_Click(object sender, EventArgs e)
	{
		using LyricSearchDialog searchDialog = new LyricSearchDialog();
		searchDialog.SetTrackInfo(GetSearchContext());
		if (sender is ToolStripItem toolStripItem)
		{
			searchDialog.SetSelectedSource((SearchSource)toolStripItem.Tag);
		}
		if (searchDialog.ShowDialog() == DialogResult.OK)
		{
			LyricSearchResult selectedLyric = searchDialog.GetSelectedLyric();
			if (selectedLyric.DeferredLyricLoader != null)
			{
				DownloadDeferredLyricAsync(selectedLyric);
				return;
			}

			SetLyricText(lastLoadedLyricText = selectedLyric.GetFormattedLyricText());
		}
	}

	private async void DownloadDeferredLyricAsync(LyricSearchResult lyricInfo)
	{
		try
		{
			ShowDownloadProgress();
			CancellationTokenSource cancellationSource = GetDownloadCancellationSource();
			string lyricText = await Task.Run(() => LoadDeferredLyricText(lyricInfo), cancellationSource.Token);
			SetLyricText(lastLoadedLyricText = lyricText);
		}
		catch (OperationCanceledException)
		{
		}
		catch (System.Exception ex)
		{
			DatabaseMapper.ShowErrorMessage(ex.Message);
		}
		finally
		{
			HideDownloadProgress();
		}
	}

	private string LoadDeferredLyricText(LyricSearchResult lyricInfo)
	{
		LyricSearchResult downloadedLyric = lyricInfo.DeferredLyricLoader(GetDownloadCancellationSource());
		return downloadedLyric?.GetFormattedLyricText() ?? "";
	}

	private void ReformatTimetag_Click(object sender, EventArgs e)
	{
		SetLyricText(LyricTextProcessor.ReformatLyric(lyricTextBox.Text, removeBlankLines: false, removeHeaderTags: false));
	}

	private void RemoveTimetag_Click(object sender, EventArgs e)
	{
		SetLyricText(LyricTextProcessor.RemoveTimestamps(lyricTextBox.Text));
	}

	private void DeleteBlankLines_Click(object sender, EventArgs e)
	{
		SetLyricText(LyricTextProcessor.ReformatLyric(lyricTextBox.Text, removeBlankLines: true, removeHeaderTags: false));
	}

	private void DeleteHeaderTags_Click(object sender, EventArgs e)
	{
		SetLyricText(LyricTextProcessor.ReformatLyric(lyricTextBox.Text, removeBlankLines: false, removeHeaderTags: true));
	}

	private void AdjustTimetag_Click(object sender, EventArgs e)
	{
		using LyricTimeOffsetDialog offsetDialog = new LyricTimeOffsetDialog();
		if (offsetDialog.ShowDialog() != DialogResult.OK)
		{
			return;
		}
		string adjustedLyric = LyricTextProcessor.ShiftLyricTimestamps(GetLyricText(), offsetDialog.OffsetMilliseconds);
		if (adjustedLyric == null)
		{
			DatabaseMapper.ShowErrorMessage(Resources.Msg_AdjustTimetagFail);
			return;
		}
		SetLyricText(adjustedLyric);
	}

	private void SaveLyricFile_Click(object sender, EventArgs e)
	{
		try
		{
			LyricSaveFileDialog lyricSaveDialog = new LyricSaveFileDialog();
			FileInfo fileInfo = new FileInfo(GetSearchContext().FilePath);
			if (fileInfo.Exists)
			{
				lyricSaveDialog.InitialDirectory = DatabaseMapper.GetLyricSaveDirectory(fileInfo.FullName);
			}

			lyricSaveDialog.FileName = DatabaseMapper.BuildLyricFileName(fileInfo.FullName, GetSearchContext().TagState);
			string defaultSavePath = lyricSaveDialog.InitialDirectory + "\\" + lyricSaveDialog.FileName;
			if (!File.Exists(defaultSavePath))
			{
				File.WriteAllText(defaultSavePath, GetLyricText(), Encoding.GetEncoding(Settings.Default.SaveLrcFileDefaultEncoding));
				DatabaseMapper.ShowInformationMessage(string.Format(Resources.Msg_FilesSavedInSpecPath, defaultSavePath));
				return;
			}

			if (lyricSaveDialog.ShowDialog() == DialogResult.OK)
			{
				File.WriteAllText(lyricSaveDialog.FileName, GetLyricText(), Encoding.GetEncoding(lyricSaveDialog.SelectedEncoding));
			}
		}
		catch (System.Exception ex)
		{
			DatabaseMapper.ShowErrorMessage(ex.Message);
		}
	}

	private void ImportLrcFile_Click(object sender, EventArgs e)
	{
		try
		{
			string text;
			if ((text = ImportLrcText(GetSearchContext().FilePath, GetSearchContext().TagState, importLrcOpenFileDialog)) != null)
			{
				SetLyricText(lastLoadedLyricText = text);
			}
		}
		catch (System.Exception ex)
		{
			DatabaseMapper.ShowErrorMessage(ex.Message);
		}
	}

	private void ResetLyric_Click(object sender, EventArgs e)
	{
		SetLyricText(lastLoadedLyricText);
	}

	private void FindOrReplace_Click(object sender, EventArgs e)
	{
		FindReplaceDialog findReplaceDialog = new FindReplaceDialog();
		findReplaceDialog.FindReplaceController = findReplaceController;
		findReplaceDialog.Show(this);
	}

	private void FindPrevious_Click(object sender, EventArgs e)
	{
		findReplaceController.FindPrevious();
	}

	private void FindNext_Click(object sender, EventArgs e)
	{
		findReplaceController.FindNext();
	}

	private void FindMenu_Opened(object sender, EventArgs e)
	{
		bool hasSearchText = findReplaceController.SearchText.Any();
		findNextMenuItem.Enabled = hasSearchText;
		findPreviousMenuItem.Enabled = hasSearchText;
	}

	public static string ImportLrcText(string audioFilePath, ConfigDescriptorState tagState, OpenFileDialog openFileDialog)
	{
		string lrcFilePath = DatabaseMapper.FindExistingLyricFile(audioFilePath, tagState, allowLocalFallback: true);
		if ((lrcFilePath == null || !File.Exists(lrcFilePath)) && openFileDialog != null)
		{
			openFileDialog.Filter = "lrc file (*.lrc)|*.lrc";
			openFileDialog.InitialDirectory = Path.GetDirectoryName(lrcFilePath ?? audioFilePath);
			openFileDialog.FileName = "";
			lrcFilePath = openFileDialog.ShowDialog() == DialogResult.OK ? openFileDialog.FileName : null;
		}
		if (lrcFilePath != null)
		{
			string encodingName = Tokenizer.DetectFileEncoding(lrcFilePath);
			return File.ReadAllText(lrcFilePath, Encoding.GetEncoding(encodingName));
		}
		return null;
	}

	private void LyricEditor_KeyDown(object sender, KeyEventArgs e)
	{
		if (e.Control && e.KeyCode == Keys.F)
		{
			findReplaceButton.PerformClick();
		}
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing && components != null)
		{
			searchButton.Image?.Dispose();
			searchButton.Image = null;
			saveAsLrcButton.Image?.Dispose();
			saveAsLrcButton.Image = null;
			components.Dispose();
		}

		base.Dispose(disposing);
	}

	private void InitializeComponent()
	{
		components = new Container();
		mainLayoutPanel = new FlowLayoutPanel();
		lyricTextBox = new TextBox();
		footerPanel = new FlowLayoutPanel();
		buttonPanel = new FlowLayoutPanel();
		searchButton = new SplitButton();
		lyricSourceMenu = new ContextMenuStrip(components);
		saveAsLrcButton = new SplitButton();
		lyricToolsMenu = new ContextMenuStrip(components);
		reformatTimestampMenuItem = new ToolStripMenuItem();
		removeTimestampMenuItem = new ToolStripMenuItem();
		deleteBlankLinesMenuItem = new ToolStripMenuItem();
		deleteHeaderTagsMenuItem = new ToolStripMenuItem();
		adjustTimestampMenuItem = new ToolStripMenuItem();
		resetLyricMenuItem = new ToolStripMenuItem();
		importLrcMenuItem = new ToolStripMenuItem();
		findReplaceButton = new SplitButton();
		findMenu = new ContextMenuStrip(components);
		findPreviousMenuItem = new ToolStripMenuItem();
		findNextMenuItem = new ToolStripMenuItem();
		okButton = new Button();
		okAndSaveButton = new Button();
		cancelButton = new Button();
		downloadProgressPanel = new Panel();
		progressPictureBox = new PictureBox();
		downloadingLabel = new Label();
		importLrcOpenFileDialog = new OpenFileDialog();

		mainLayoutPanel.SuspendLayout();
		footerPanel.SuspendLayout();
		buttonPanel.SuspendLayout();
		lyricToolsMenu.SuspendLayout();
		findMenu.SuspendLayout();
		downloadProgressPanel.SuspendLayout();
		((ISupportInitialize)progressPictureBox).BeginInit();
		SuspendLayout();

		mainLayoutPanel.Controls.Add(lyricTextBox);
		mainLayoutPanel.Controls.Add(footerPanel);
		mainLayoutPanel.Dock = DockStyle.Fill;
		mainLayoutPanel.Location = new Point(0, 0);
		mainLayoutPanel.Name = "flowLayoutPanel1";
		mainLayoutPanel.Size = new Size(784, 561);
		mainLayoutPanel.TabIndex = 0;
		mainLayoutPanel.SizeChanged += MainLayout_SizeChanged;

		lyricTextBox.Font = new Font("Tahoma", 12f);
		lyricTextBox.HideSelection = false;
		lyricTextBox.Location = new Point(0, 0);
		lyricTextBox.Margin = new Padding(0);
		lyricTextBox.Multiline = true;
		lyricTextBox.Name = "tbLyric";
		lyricTextBox.ScrollBars = ScrollBars.Vertical;
		lyricTextBox.Size = new Size(784, 500);
		lyricTextBox.TabIndex = 2;
		lyricTextBox.TabStop = false;
		lyricTextBox.KeyDown += LyricEditor_KeyDown;

		footerPanel.Controls.Add(buttonPanel);
		footerPanel.Location = new Point(0, 500);
		footerPanel.Margin = new Padding(0);
		footerPanel.Name = "flowLayoutPanel2";
		footerPanel.Size = new Size(784, 60);
		footerPanel.TabIndex = 3;

		buttonPanel.Controls.Add(searchButton);
		buttonPanel.Controls.Add(saveAsLrcButton);
		buttonPanel.Controls.Add(findReplaceButton);
		buttonPanel.Controls.Add(okButton);
		buttonPanel.Controls.Add(okAndSaveButton);
		buttonPanel.Controls.Add(cancelButton);
		buttonPanel.Location = new Point(0, 0);
		buttonPanel.Margin = new Padding(0);
		buttonPanel.Name = "flowLayoutPanel3";
		buttonPanel.Size = new Size(725, 35);
		buttonPanel.TabIndex = 4;

		searchButton.ContextMenuStrip = lyricSourceMenu;
		searchButton.Location = new Point(0, 0);
		searchButton.Margin = new Padding(0);
		searchButton.Name = "btnSearch";
		searchButton.Size = new Size(100, 35);
		searchButton.SplitMenuStrip = lyricSourceMenu;
		searchButton.TabIndex = 4;
		searchButton.Text = "Search";
		searchButton.TextAlign = ContentAlignment.MiddleRight;
		searchButton.TextImageRelation = TextImageRelation.ImageBeforeText;
		searchButton.UseVisualStyleBackColor = true;
		searchButton.Click += SearchLyric_Click;
		lyricSourceMenu.Name = "searchContextMenuStrip";
		lyricSourceMenu.Size = new Size(61, 4);

		saveAsLrcButton.ContextMenuStrip = lyricToolsMenu;
		saveAsLrcButton.Location = new Point(120, 0);
		saveAsLrcButton.Margin = new Padding(20, 0, 0, 0);
		saveAsLrcButton.Name = "btnSaveas";
		saveAsLrcButton.Size = new Size(100, 35);
		saveAsLrcButton.SplitMenuStrip = lyricToolsMenu;
		saveAsLrcButton.TabIndex = 5;
		saveAsLrcButton.Text = "Save as Lrc";
		saveAsLrcButton.TextAlign = ContentAlignment.MiddleRight;
		saveAsLrcButton.TextImageRelation = TextImageRelation.ImageBeforeText;
		saveAsLrcButton.UseVisualStyleBackColor = true;
		saveAsLrcButton.Click += SaveLyricFile_Click;

		lyricToolsMenu.Items.AddRange(new ToolStripItem[7] { reformatTimestampMenuItem, removeTimestampMenuItem, deleteBlankLinesMenuItem, deleteHeaderTagsMenuItem, adjustTimestampMenuItem, resetLyricMenuItem, importLrcMenuItem });
		lyricToolsMenu.Name = "moreContextMenuStrip";
		lyricToolsMenu.Size = new Size(221, 158);
		reformatTimestampMenuItem.Name = "reformatTimetagToolStripMenuItem";
		reformatTimestampMenuItem.Size = new Size(220, 22);
		reformatTimestampMenuItem.Text = "Reformat timetag";
		reformatTimestampMenuItem.Click += ReformatTimetag_Click;
		removeTimestampMenuItem.Name = "removeTimetagToolStripMenuItem";
		removeTimestampMenuItem.Size = new Size(220, 22);
		removeTimestampMenuItem.Text = "Remove timetag";
		removeTimestampMenuItem.Click += RemoveTimetag_Click;
		deleteBlankLinesMenuItem.Name = "deleteLinesOfBlankTextToolStripMenuItem";
		deleteBlankLinesMenuItem.Size = new Size(220, 22);
		deleteBlankLinesMenuItem.Text = "Delete lines of blank text";
		deleteBlankLinesMenuItem.Click += DeleteBlankLines_Click;
		deleteHeaderTagsMenuItem.Name = "deleteHeadTagsToolStripMenuItem";
		deleteHeaderTagsMenuItem.Size = new Size(220, 22);
		deleteHeaderTagsMenuItem.Text = "Delete head tags";
		deleteHeaderTagsMenuItem.Click += DeleteHeaderTags_Click;
		adjustTimestampMenuItem.Name = "adjustTimetagToolStripMenuItem";
		adjustTimestampMenuItem.Size = new Size(220, 22);
		adjustTimestampMenuItem.Text = "Adjust timetag";
		adjustTimestampMenuItem.Click += AdjustTimetag_Click;
		resetLyricMenuItem.Name = "resetLyricToolStripMenuItem";
		resetLyricMenuItem.Size = new Size(220, 22);
		resetLyricMenuItem.Text = "Reset lyric";
		resetLyricMenuItem.Click += ResetLyric_Click;
		importLrcMenuItem.Name = "importLrcFileToolStripMenuItem";
		importLrcMenuItem.Size = new Size(220, 22);
		importLrcMenuItem.Text = "Import Lrc file";
		importLrcMenuItem.Click += ImportLrcFile_Click;

		findReplaceButton.ContextMenuStrip = findMenu;
		findReplaceButton.Location = new Point(240, 0);
		findReplaceButton.Margin = new Padding(20, 0, 0, 0);
		findReplaceButton.Name = "btnFind";
		findReplaceButton.Size = new Size(100, 35);
		findReplaceButton.SplitMenuStrip = findMenu;
		findReplaceButton.TabIndex = 7;
		findReplaceButton.Text = "Find/Replace";
		findReplaceButton.TextAlign = ContentAlignment.MiddleRight;
		findReplaceButton.TextImageRelation = TextImageRelation.ImageBeforeText;
		findReplaceButton.UseVisualStyleBackColor = true;
		findReplaceButton.Click += FindOrReplace_Click;
		findMenu.Items.AddRange(new ToolStripItem[2] { findPreviousMenuItem, findNextMenuItem });
		findMenu.Name = "moreContextMenuStrip";
		findMenu.Size = new Size(176, 48);
		findMenu.Opened += FindMenu_Opened;
		findPreviousMenuItem.Name = "findPreviousToolStripMenuItem";
		findPreviousMenuItem.ShortcutKeys = Keys.F2;
		findPreviousMenuItem.Size = new Size(175, 22);
		findPreviousMenuItem.Text = "Find previous";
		findPreviousMenuItem.Click += FindPrevious_Click;
		findNextMenuItem.Name = "findNextToolStripMenuItem";
		findNextMenuItem.ShortcutKeys = Keys.F3;
		findNextMenuItem.Size = new Size(175, 22);
		findNextMenuItem.Text = "Find next";
		findNextMenuItem.Click += FindNext_Click;

		okButton.Location = new Point(360, 0);
		okButton.Margin = new Padding(20, 0, 0, 0);
		okButton.Name = "btnOK";
		okButton.Size = new Size(100, 35);
		okButton.TabIndex = 1;
		okButton.Text = "OK";
		okButton.UseVisualStyleBackColor = true;
		okButton.Click += OkButton_Click;
		okAndSaveButton.Location = new Point(480, 0);
		okAndSaveButton.Margin = new Padding(20, 0, 0, 0);
		okAndSaveButton.Name = "btnOkAndSave";
		okAndSaveButton.Size = new Size(100, 35);
		okAndSaveButton.TabIndex = 6;
		okAndSaveButton.Text = "OK && Save";
		okAndSaveButton.UseVisualStyleBackColor = true;
		okAndSaveButton.Click += OkAndSaveButton_Click;
		cancelButton.Location = new Point(600, 0);
		cancelButton.Margin = new Padding(20, 0, 0, 0);
		cancelButton.Name = "btnCancel";
		cancelButton.Size = new Size(100, 35);
		cancelButton.TabIndex = 2;
		cancelButton.Text = "Cancel";
		cancelButton.UseVisualStyleBackColor = true;
		cancelButton.Click += CancelButton_Click;

		downloadProgressPanel.Controls.Add(progressPictureBox);
		downloadProgressPanel.Controls.Add(downloadingLabel);
		downloadProgressPanel.Location = new Point(390, 0);
		downloadProgressPanel.Margin = new Padding(0);
		downloadProgressPanel.Name = "panelWait";
		downloadProgressPanel.Size = new Size(200, 64);
		downloadProgressPanel.TabIndex = 1;
		downloadProgressPanel.Visible = false;
		progressPictureBox.Image = Resources.img_wait;
		progressPictureBox.Location = new Point(16, 16);
		progressPictureBox.Name = "pbProgress";
		progressPictureBox.Size = new Size(32, 32);
		progressPictureBox.SizeMode = PictureBoxSizeMode.Zoom;
		progressPictureBox.TabIndex = 12;
		progressPictureBox.TabStop = false;
		downloadingLabel.AutoSize = true;
		downloadingLabel.Location = new Point(64, 25);
		downloadingLabel.Margin = new Padding(0);
		downloadingLabel.Name = "lblDownloading";
		downloadingLabel.Size = new Size(89, 14);
		downloadingLabel.TabIndex = 11;
		downloadingLabel.Text = "Downloading...";

		importLrcOpenFileDialog.FileName = "openFileDialog1";
		importLrcOpenFileDialog.Filter = "lrc file (*.lrc)|*.lrc";
		AutoScaleDimensions = new SizeF(96f, 96f);
		AutoScaleMode = AutoScaleMode.Dpi;
		ClientSize = new Size(784, 561);
		Controls.Add(downloadProgressPanel);
		Controls.Add(mainLayoutPanel);
		Font = new Font("Tahoma", 9f, FontStyle.Regular, GraphicsUnit.Point, 0);
		MinimumSize = new Size(640, 480);
		Name = "FormLyric";
		ShowIcon = false;
		StartPosition = FormStartPosition.CenterParent;
		Text = "Lyric";

		mainLayoutPanel.ResumeLayout(performLayout: false);
		mainLayoutPanel.PerformLayout();
		footerPanel.ResumeLayout(performLayout: false);
		buttonPanel.ResumeLayout(performLayout: false);
		lyricToolsMenu.ResumeLayout(performLayout: false);
		findMenu.ResumeLayout(performLayout: false);
		downloadProgressPanel.ResumeLayout(performLayout: false);
		downloadProgressPanel.PerformLayout();
		((ISupportInitialize)progressPictureBox).EndInit();
		ResumeLayout(performLayout: false);
	}

}


