using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MusicTag.States;
using MusicTagWinApp.Exporters;
using MusicTagWinApp.Instances;
using MusicTagWinApp.Properties;
using MusicTagWinApp.Win32.Taskbar;

namespace MusicTag.Importers;

internal class PictureFromTagsDialog : Form
{
	private List<string> audioFilePaths;

	private ConfigDescriptorState.PictureData selectedPicture;

	private readonly HashSet<string> loadedImageHashes;

	private readonly CancellationTokenSource searchCancellation;

	private readonly TaskbarProgressController taskbarProgress;

	private IContainer components;

	private HeaderAwareListView pictureListView;

	private ImageList pictureImageList;

	private FlowLayoutPanel mainLayoutPanel;

	private FlowLayoutPanel footerLayoutPanel;

	private FlowLayoutPanel buttonLayoutPanel;

	private Button okButton;

	private Button cancelButton;

	private SaveFileDialog saveCoverDialog;

	private ContextMenuStrip pictureContextMenu;

	private ToolStripMenuItem openCoverMenuItem;

	private ToolStripMenuItem extractCoverMenuItem;

	private PictureBox progressPictureBox;

	public void SetAudioFilePaths(List<string> filePaths)
	{
		audioFilePaths = filePaths;
	}

	private List<string> GetAudioFilePaths()
	{
		return audioFilePaths;
	}

	private void SetSelectedPicture(ConfigDescriptorState.PictureData pictureData)
	{
		selectedPicture = pictureData;
	}

	public ConfigDescriptorState.PictureData GetSelectedPicture()
	{
		return selectedPicture;
	}

	private HashSet<string> GetLoadedImageHashes()
	{
		return loadedImageHashes;
	}

	private CancellationTokenSource GetSearchCancellation()
	{
		return searchCancellation;
	}

	private TaskbarProgressController GetTaskbarProgress()
	{
		return taskbarProgress;
	}

	public PictureFromTagsDialog()
	{
		loadedImageHashes = new HashSet<string>();
		searchCancellation = new CancellationTokenSource();
		InitializeComponent();
		InitializeImageList();
		taskbarProgress = new TaskbarProgressController(this);
		okButton.Text = Resources.OK;
		cancelButton.Text = Resources.Cancel;
		Text = Resources.ChooseFromFileTags;
		UpdateLayout();
	}

	private void InitializeImageList()
	{
		pictureImageList.Images.Clear();
		pictureImageList.ImageSize = new Size(DatabaseMapper.ScaleByDpi(pictureImageList.ImageSize.Width), DatabaseMapper.ScaleByDpi(pictureImageList.ImageSize.Height));
		pictureImageList.ColorDepth = ColorDepth.Depth24Bit;
		pictureImageList.TransparentColor = Color.Transparent;
		pictureImageList.Images.Add("loading", DatabaseMapper.LoadResourceBitmap("loading", pictureImageList.ImageSize));
		pictureImageList.Images.Add("download_failed", DatabaseMapper.LoadResourceBitmap("download_failed", pictureImageList.ImageSize));
		progressPictureBox.Image = DatabaseMapper.LoadResourceBitmap("img_wait");
	}

	protected override void OnShown(EventArgs e)
	{
		base.OnShown(e);
		StartPictureSearchAsync();
	}

	protected override void OnClosed(EventArgs e)
	{
		base.OnClosed(e);
		GetSearchCancellation().Cancel();
	}

	private void UpdateLayout()
	{
		pictureListView.Width = mainLayoutPanel.Width;
		pictureListView.Height = mainLayoutPanel.Height - footerLayoutPanel.Height;
		int buttonsLeft = (footerLayoutPanel.Width - buttonLayoutPanel.Width) / 2;
		buttonLayoutPanel.Margin = new Padding(buttonsLeft, buttonLayoutPanel.Margin.Top, 0, buttonLayoutPanel.Margin.Bottom);
		int progressLeft = footerLayoutPanel.Width - progressPictureBox.Width - buttonLayoutPanel.Location.X - buttonLayoutPanel.Width - progressPictureBox.Margin.Top;
		progressPictureBox.Margin = new Padding(progressLeft, progressPictureBox.Margin.Top, 0, progressPictureBox.Margin.Bottom);
	}

	private void MainLayout_SizeChanged(object sender, EventArgs e)
	{
		UpdateLayout();
	}

	private void AddPictureCandidates(List<(string AudioFilePath, Image Image, string ImageHash)> candidates)
	{
		int imageIndex = 0;
		foreach (var (audioFilePath, image, imageHash) in candidates)
		{
			if (!GetLoadedImageHashes().Contains(imageHash))
			{
				string imageKey = audioFilePath + "_" + imageIndex;
				pictureImageList.Images.Add(imageKey, image);
				ListViewItem listViewItem = new ListViewItem
				{
					Text = image.Width + "x" + image.Height,
					ImageKey = imageKey,
					Tag = (audioFilePath, imageIndex)
				};
				pictureListView.Items.Add(listViewItem);
				GetLoadedImageHashes().Add(imageHash);
				imageIndex++;
			}
		}
	}

	private async void StartPictureSearchAsync()
	{
		CancellationToken cancellationToken = GetSearchCancellation().Token;
		IProgress<List<(string, Image, string)>> progress = new Progress<List<(string, Image, string)>>(AddPictureCandidatesIfSearchActive);

		GetTaskbarProgress().SetProgressState(TaskbarProgressBarStatus.Indeterminate);
		try
		{
			await Task.Run(() => CollectEmbeddedPictures(progress, cancellationToken), cancellationToken);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception ex)
		{
			Console.WriteLine("PictureSearch error:" + ex.GetMessageChain());
		}
		finally
		{
			progressPictureBox.Hide();
			GetTaskbarProgress().SetProgressState(TaskbarProgressBarStatus.NoProgress);
		}
	}

	private void AddPictureCandidatesIfSearchActive(List<(string, Image, string)> candidates)
	{
		if (!GetSearchCancellation().IsCancellationRequested)
		{
			AddPictureCandidates(candidates);
		}
	}

	private void CollectEmbeddedPictures(IProgress<List<(string, Image, string)>> progress, CancellationToken cancellationToken)
	{
		foreach (string audioFilePath in GetAudioFilePaths() ?? new List<string>())
		{
			if (cancellationToken.IsCancellationRequested)
			{
				break;
			}

			if (!File.Exists(audioFilePath))
			{
				continue;
			}

			using ConfigDescriptorState configDescriptorState = new ConfigDescriptorState(audioFilePath);
			if (!configDescriptorState.IsLoadedSuccessfully())
			{
				continue;
			}

			configDescriptorState.LoadAllPictures();
			List<ConfigDescriptorState.PictureData> pictureData = configDescriptorState["allpicturedata"] as List<ConfigDescriptorState.PictureData>;
			if (pictureData == null)
			{
				continue;
			}

			List<(string, Image, string)> candidates = new List<(string, Image, string)>();
			foreach (ConfigDescriptorState.PictureData picture in pictureData)
			{
				if (cancellationToken.IsCancellationRequested)
				{
					break;
				}

				Image image = ConfigDescriptorState.LoadPictureImage(picture);
				if (image == null)
				{
					continue;
				}

				string imageHash = DatabaseMapper.ComputeMd5HashString(picture.ImageBytes);
				candidates.Add((audioFilePath, image, imageHash));
			}

			progress.Report(candidates);
		}
	}

	private void OkButton_Click(object sender, EventArgs e)
	{
		UseSelectedPictureData(SelectPicture);
		base.DialogResult = DialogResult.OK;
		Close();
	}

	private void CancelButton_Click(object sender, EventArgs e)
	{
		base.DialogResult = DialogResult.Cancel;
		Close();
	}

	private void PictureList_DoubleClick(object sender, EventArgs e)
	{
		if (pictureListView.FocusedItem != null && pictureListView.SelectedItems.Count > 0)
		{
			okButton.PerformClick();
		}
	}

	private void PictureList_MouseUp(object sender, MouseEventArgs e)
	{
		if (e.Button != MouseButtons.Right)
		{
			return;
		}
		if (pictureListView.SelectedItems.Count <= 0)
		{
			return;
		}

		openCoverMenuItem.Text = Resources.OpenCover;
		extractCoverMenuItem.Text = Resources.ExtractCover;
		ListViewItem selectedItem = pictureListView.SelectedItems[0];
		openCoverMenuItem.Enabled = !string.IsNullOrWhiteSpace(selectedItem.ImageKey);
		extractCoverMenuItem.Enabled = openCoverMenuItem.Enabled;
		pictureContextMenu.Show(pictureListView, e.Location);
	}

	private void UseSelectedPictureData(Action<ConfigDescriptorState.PictureData> action)
	{
		if (pictureListView.SelectedItems.Count <= 0)
		{
			return;
		}
		var (audioFilePath, selectedImageIndex) = ((string, int))pictureListView.SelectedItems[0].Tag;
		using ConfigDescriptorState configDescriptorState = new ConfigDescriptorState(audioFilePath);
		if (!configDescriptorState.IsLoadedSuccessfully())
		{
			return;
		}
		configDescriptorState.LoadAllPictures();
		List<ConfigDescriptorState.PictureData> embeddedPictures = configDescriptorState["allpicturedata"] as List<ConfigDescriptorState.PictureData>;
		if (embeddedPictures == null)
		{
			return;
		}

		int currentImageIndex = 0;
		foreach (ConfigDescriptorState.PictureData pictureData in embeddedPictures)
		{
			using Image image = ConfigDescriptorState.LoadPictureImage(pictureData);
			if (image != null && currentImageIndex++ == selectedImageIndex)
			{
				action(pictureData);
				break;
			}
		}
	}

	private void OpenCover_Click(object sender, EventArgs e)
	{
		UseSelectedPictureData(OpenCoverImage);
	}

	private static void OpenCoverImage(ConfigDescriptorState.PictureData pictureData)
	{
		string extension = DatabaseMapper.GetImageExtensionForMimeType(pictureData.MimeType, "");
		string tempCoverPath = DatabaseMapper.GetPictureCacheDirectory() + "tempcover" + extension;
		File.WriteAllBytes(tempCoverPath, pictureData.ImageBytes);
		Process.Start(tempCoverPath);
	}

	private void ExtractCover_Click(object sender, EventArgs e)
	{
		UseSelectedPictureData(ExtractCover);
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
		components = new Container();
		pictureListView = new HeaderAwareListView();
		pictureImageList = new ImageList(components);
		mainLayoutPanel = new FlowLayoutPanel();
		footerLayoutPanel = new FlowLayoutPanel();
		buttonLayoutPanel = new FlowLayoutPanel();
		okButton = new Button();
		cancelButton = new Button();
		progressPictureBox = new PictureBox();
		saveCoverDialog = new SaveFileDialog();
		pictureContextMenu = new ContextMenuStrip(components);
		openCoverMenuItem = new ToolStripMenuItem();
		extractCoverMenuItem = new ToolStripMenuItem();

		mainLayoutPanel.SuspendLayout();
		footerLayoutPanel.SuspendLayout();
		buttonLayoutPanel.SuspendLayout();
		((ISupportInitialize)progressPictureBox).BeginInit();
		pictureContextMenu.SuspendLayout();
		SuspendLayout();

		pictureListView.Font = new Font("Tahoma", 9f, FontStyle.Regular, GraphicsUnit.Point, 0);
		pictureListView.FullRowSelect = true;
		pictureListView.HideSelection = false;
		pictureListView.LargeImageList = pictureImageList;
		pictureListView.Location = new Point(0, 0);
		pictureListView.Margin = new Padding(0);
		pictureListView.MultiSelect = false;
		pictureListView.Name = "listView1";
		pictureListView.Size = new Size(534, 431);
		pictureListView.TabIndex = 0;
		pictureListView.UseCompatibleStateImageBehavior = false;
		pictureListView.DoubleClick += PictureList_DoubleClick;
		pictureListView.MouseUp += PictureList_MouseUp;

		pictureImageList.ColorDepth = ColorDepth.Depth32Bit;
		pictureImageList.ImageSize = new Size(128, 128);
		pictureImageList.TransparentColor = Color.Transparent;

		mainLayoutPanel.Controls.Add(pictureListView);
		mainLayoutPanel.Controls.Add(footerLayoutPanel);
		mainLayoutPanel.Dock = DockStyle.Fill;
		mainLayoutPanel.FlowDirection = FlowDirection.TopDown;
		mainLayoutPanel.Location = new Point(0, 0);
		mainLayoutPanel.Margin = new Padding(0);
		mainLayoutPanel.Name = "flowLayoutPanel1";
		mainLayoutPanel.Size = new Size(534, 661);
		mainLayoutPanel.TabIndex = 1;
		mainLayoutPanel.WrapContents = false;
		mainLayoutPanel.SizeChanged += MainLayout_SizeChanged;

		footerLayoutPanel.Controls.Add(buttonLayoutPanel);
		footerLayoutPanel.Controls.Add(progressPictureBox);
		footerLayoutPanel.Dock = DockStyle.Fill;
		footerLayoutPanel.Location = new Point(0, 431);
		footerLayoutPanel.Margin = new Padding(0);
		footerLayoutPanel.Name = "flowLayoutPanel2";
		footerLayoutPanel.Size = new Size(534, 60);
		footerLayoutPanel.TabIndex = 8;
		footerLayoutPanel.WrapContents = false;

		buttonLayoutPanel.Controls.Add(okButton);
		buttonLayoutPanel.Controls.Add(cancelButton);
		buttonLayoutPanel.Location = new Point(0, 12);
		buttonLayoutPanel.Margin = new Padding(0, 12, 0, 0);
		buttonLayoutPanel.Name = "flowLayoutPanel3";
		buttonLayoutPanel.Size = new Size(220, 35);
		buttonLayoutPanel.TabIndex = 6;

		okButton.Location = new Point(0, 0);
		okButton.Margin = new Padding(0);
		okButton.Name = "btnOK";
		okButton.Size = new Size(100, 35);
		okButton.TabIndex = 1;
		okButton.Text = "OK";
		okButton.UseVisualStyleBackColor = true;
		okButton.Click += OkButton_Click;

		cancelButton.Location = new Point(120, 0);
		cancelButton.Margin = new Padding(20, 0, 0, 0);
		cancelButton.Name = "btnCancel";
		cancelButton.Size = new Size(100, 35);
		cancelButton.TabIndex = 2;
		cancelButton.Text = "Cancel";
		cancelButton.UseVisualStyleBackColor = true;
		cancelButton.Click += CancelButton_Click;

		progressPictureBox.Image = Resources.img_wait;
		progressPictureBox.Location = new Point(220, 13);
		progressPictureBox.Margin = new Padding(0, 13, 0, 0);
		progressPictureBox.Name = "pbProgress";
		progressPictureBox.Size = new Size(32, 32);
		progressPictureBox.SizeMode = PictureBoxSizeMode.Zoom;
		progressPictureBox.TabIndex = 7;
		progressPictureBox.TabStop = false;

		saveCoverDialog.RestoreDirectory = true;

		pictureContextMenu.Items.AddRange(new ToolStripItem[2] { openCoverMenuItem, extractCoverMenuItem });
		pictureContextMenu.Name = "pictureBoxContextMenuStrip";
		pictureContextMenu.Size = new Size(152, 48);

		openCoverMenuItem.Name = "openCoverToolStripMenuItem";
		openCoverMenuItem.Size = new Size(151, 22);
		openCoverMenuItem.Text = "Open Cover";
		openCoverMenuItem.Click += OpenCover_Click;

		extractCoverMenuItem.Name = "extractCoverToolStripMenuItem";
		extractCoverMenuItem.Size = new Size(151, 22);
		extractCoverMenuItem.Text = "Extract cover";
		extractCoverMenuItem.Click += ExtractCover_Click;

		AutoScaleDimensions = new SizeF(96f, 96f);
		AutoScaleMode = AutoScaleMode.Dpi;
		ClientSize = new Size(534, 661);
		Controls.Add(mainLayoutPanel);
		Font = new Font("Tahoma", 9f, FontStyle.Regular, GraphicsUnit.Point, 0);
		MinimumSize = new Size(300, 300);
		Name = "FormPictureFromTags";
		ShowIcon = false;
		StartPosition = FormStartPosition.CenterParent;
		Text = "FormPictureFromTags";

		mainLayoutPanel.ResumeLayout(performLayout: false);
		footerLayoutPanel.ResumeLayout(performLayout: false);
		buttonLayoutPanel.ResumeLayout(performLayout: false);
		((ISupportInitialize)progressPictureBox).EndInit();
		pictureContextMenu.ResumeLayout(performLayout: false);
		ResumeLayout(performLayout: false);
	}

	private void SelectPicture(ConfigDescriptorState.PictureData pictureData)
	{
		SetSelectedPicture(pictureData);
	}

	private void ExtractCover(ConfigDescriptorState.PictureData pictureData)
	{
		string fileFilter = DatabaseMapper.GetImageFileDialogFilterForMimeType(pictureData.MimeType);
		if (!string.IsNullOrWhiteSpace(fileFilter))
		{
			saveCoverDialog.Filter = fileFilter;
		}

		if (saveCoverDialog.ShowDialog() != DialogResult.OK)
		{
			return;
		}

		File.WriteAllBytes(saveCoverDialog.FileName, pictureData.ImageBytes);
	}
}

