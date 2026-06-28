using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Resources;
using System.Windows.Forms;
using MusicTag.Readers;
using MusicTagWinApp.Instances;
using MusicTagWinApp.Properties;
using Newtonsoft.Json;

namespace MusicTagWinApp.Common;

internal class CustomColumnsDialog : Form
{
	[Serializable]
	public class ColumnHeaderInfo
	{
		public int width;

		public HorizontalAlignment textAlign;

		public int displayIndex;

		public bool isShow;

		public int beforeHideDisplayIndex;

		public int tempWidth;

		public string Name { get; }

		public ColumnHeaderInfo(string name, int displayIndex, int width, HorizontalAlignment textAlign = HorizontalAlignment.Left, bool isShow = true)
		{
			Name = name;
			this.width = width;
			this.textAlign = textAlign;
			this.displayIndex = displayIndex;
			this.isShow = isShow;
		}
	}

	private static readonly Dictionary<string, int> defaultColumnWidths = CreateDefaultColumnWidths();

	private static readonly List<ColumnHeaderInfo> columnHeaderSettings = CreateColumnHeaderSettings();

	private IContainer components;

	private ListView columnListView;

	private ColumnHeader columnNameHeader;

	private Panel buttonPanel;

	private Button resetButton;

	private Button cancelButton;

	private Button okButton;

	private Button moveDownButton;

	private Button moveUpButton;

	private static Dictionary<string, int> GetDefaultColumnWidths()
	{
		return defaultColumnWidths;
	}

	public static List<ColumnHeaderInfo> GetColumnHeaderSettings()
	{
		return columnHeaderSettings;
	}

	public ListView.ListViewItemCollection GetColumnListItems()
	{
		return columnListView.Items;
	}

	public CustomColumnsDialog()
	{
		InitializeComponent();
		ComponentResourceManager resources = new ComponentResourceManager(typeof(StateFieldInstance));
		Text = Resources.customcolumns;
		moveUpButton.Text = GetDialogText(resources, "btnMoveUp", moveUpButton.Text);
		moveDownButton.Text = GetDialogText(resources, "btnMoveDown", moveDownButton.Text);
		resetButton.Text = GetDialogText(resources, "btnReset", resetButton.Text);
		okButton.Text = Resources.OK;
		cancelButton.Text = Resources.Cancel;
		columnListView.SmallImageList = new ImageList(components)
		{
			ImageSize = new Size(1, DatabaseMapper.ScaleByDpi(32f))
		};

		List<ColumnHeaderInfo> columns = new List<ColumnHeaderInfo>(GetColumnHeaderSettings());
		columns.Sort(CompareColumnDisplayOrder);
		foreach (ColumnHeaderInfo column in columns)
		{
			ListViewItem listViewItem = new ListViewItem
			{
				Text = Resources.ResourceManager.GetString(column.Name),
				Checked = column.isShow,
				Tag = column
			};
			column.tempWidth = column.width;
			columnListView.Items.Add(listViewItem);
		}
	}

	protected override void OnShown(EventArgs e)
	{
		base.OnShown(e);
		UpdateLayout(null, null);
	}

	private static string GetDialogText(ComponentResourceManager resources, string resourceName, string fallbackText)
	{
		string resourceText = resources.GetString(resourceName);
		return string.IsNullOrEmpty(resourceText) ? fallbackText : resourceText;
	}

	private static int CompareColumnDisplayOrder(ColumnHeaderInfo left, ColumnHeaderInfo right)
	{
		int leftDisplayOrder = left.isShow ? left.displayIndex : left.beforeHideDisplayIndex;
		int rightDisplayOrder = right.isShow ? right.displayIndex : right.beforeHideDisplayIndex;
		int orderComparison = leftDisplayOrder.CompareTo(rightDisplayOrder);
		if (orderComparison != 0)
		{
			return orderComparison;
		}

		if (left.isShow && !right.isShow)
		{
			return 1;
		}
		if (!left.isShow && right.isShow)
		{
			return -1;
		}
		return 0;
	}

	private static Dictionary<string, int> CreateDefaultColumnWidths()
	{
		return new Dictionary<string, int>
		{
			{
				"filename",
				DatabaseMapper.ScaleByDpi(150f)
			},
			{
				"filedir",
				DatabaseMapper.ScaleByDpi(100f)
			},
			{
				"tagtypes",
				DatabaseMapper.ScaleByDpi(100f)
			},
			{
				"title",
				DatabaseMapper.ScaleByDpi(100f)
			},
			{
				"artist",
				DatabaseMapper.ScaleByDpi(100f)
			},
			{
				"album",
				DatabaseMapper.ScaleByDpi(100f)
			},
			{
				"albumartist",
				DatabaseMapper.ScaleByDpi(100f)
			},
			{
				"year",
				DatabaseMapper.ScaleByDpi(100f)
			},
			{
				"trackstr",
				DatabaseMapper.ScaleByDpi(100f)
			},
			{
				"discstr",
				DatabaseMapper.ScaleByDpi(100f)
			},
			{
				"genre",
				DatabaseMapper.ScaleByDpi(100f)
			},
			{
				"composer",
				DatabaseMapper.ScaleByDpi(100f)
			},
			{
				"lyricist",
				DatabaseMapper.ScaleByDpi(100f)
			},
			{
				"comment",
				DatabaseMapper.ScaleByDpi(100f)
			},
			{
				"haspicture",
				DatabaseMapper.ScaleByDpi(70f)
			},
			{
				"lyrics",
				DatabaseMapper.ScaleByDpi(100f)
			},
			{
				"bitpersample",
				DatabaseMapper.ScaleByDpi(80f)
			},
			{
				"channels",
				DatabaseMapper.ScaleByDpi(80f)
			},
			{
				"samplerate",
				DatabaseMapper.ScaleByDpi(90f)
			},
			{
				"bitrate",
				DatabaseMapper.ScaleByDpi(80f)
			},
			{
				"durationinms",
				DatabaseMapper.ScaleByDpi(80f)
			},
			{
				"updatetime",
				DatabaseMapper.ScaleByDpi(135f)
			}
		};
	}

	private static List<ColumnHeaderInfo> CreateColumnHeaderSettings()
	{
		List<ColumnHeaderInfo> columnHeaders = new List<ColumnHeaderInfo>();

		void AddColumn(string name, HorizontalAlignment textAlign = HorizontalAlignment.Left)
		{
			columnHeaders.Add(new ColumnHeaderInfo(name, 0, GetDefaultColumnWidths()[name], textAlign));
		}

		AddColumn("filename");
		AddColumn("filedir");
		AddColumn("tagtypes");
		AddColumn("title");
		AddColumn("artist");
		AddColumn("album");
		AddColumn("albumartist");
		AddColumn("year");
		AddColumn("trackstr");
		AddColumn("discstr");
		AddColumn("genre");
		AddColumn("composer");
		AddColumn("lyricist");
		AddColumn("comment");
		AddColumn("haspicture", HorizontalAlignment.Center);
		AddColumn("lyrics");
		AddColumn("bitpersample", HorizontalAlignment.Right);
		AddColumn("channels", HorizontalAlignment.Right);
		AddColumn("samplerate", HorizontalAlignment.Right);
		AddColumn("bitrate", HorizontalAlignment.Right);
		AddColumn("durationinms", HorizontalAlignment.Right);
		AddColumn("updatetime", HorizontalAlignment.Right);

		int beforeHideDisplayIndex = 0;
		foreach (ColumnHeaderInfo columnHeader in columnHeaders)
		{
			if (columnHeader.isShow)
			{
				columnHeader.displayIndex = beforeHideDisplayIndex++;
			}
			else
			{
				columnHeader.beforeHideDisplayIndex = beforeHideDisplayIndex;
			}
		}
		foreach (ColumnHeaderInfo columnHeader in columnHeaders)
		{
			if (!columnHeader.isShow)
			{
				columnHeader.displayIndex = beforeHideDisplayIndex++;
			}
		}

		List<ColumnHeaderInfo> savedColumnHeaders = TryDeserializeColumnHeaderSettings();
		if (savedColumnHeaders != null)
		{
			Dictionary<string, ColumnHeaderInfo> columnHeaderByName = new Dictionary<string, ColumnHeaderInfo>();
			foreach (ColumnHeaderInfo columnHeader in columnHeaders)
			{
				columnHeaderByName.Add(columnHeader.Name, columnHeader);
			}
			foreach (ColumnHeaderInfo savedColumnHeader in savedColumnHeaders)
			{
				if (savedColumnHeader == null || string.IsNullOrWhiteSpace(savedColumnHeader.Name))
				{
					continue;
				}
				if (columnHeaderByName.TryGetValue(savedColumnHeader.Name, out var columnHeader))
				{
					columnHeader.width = savedColumnHeader.width;
					columnHeader.displayIndex = savedColumnHeader.displayIndex;
					columnHeader.beforeHideDisplayIndex = savedColumnHeader.beforeHideDisplayIndex;
					columnHeader.isShow = savedColumnHeader.isShow;
				}
			}
			if (!savedColumnHeaders.Exists(columnHeader => columnHeader != null && columnHeader.Name == "lyricist"))
			{
				int lyricistDisplayIndex = columnHeaderByName["composer"].displayIndex + 1;
				foreach (KeyValuePair<string, ColumnHeaderInfo> columnHeaderPair in columnHeaderByName)
				{
					ColumnHeaderInfo columnHeader = columnHeaderPair.Value;
					if (columnHeader.displayIndex >= lyricistDisplayIndex)
					{
						columnHeader.displayIndex++;
					}
					if (columnHeader.beforeHideDisplayIndex >= lyricistDisplayIndex)
					{
						columnHeader.beforeHideDisplayIndex++;
					}
				}
				columnHeaderByName["lyricist"].displayIndex = lyricistDisplayIndex;
			}
		}
		return columnHeaders;
	}

	public static void SaveColumnHeaderSettings()
	{
		string listviewColumnHeader = JsonConvert.SerializeObject(GetColumnHeaderSettings());
		Settings.Default.ListviewColumnHeader = listviewColumnHeader;
	}

	private static List<ColumnHeaderInfo> TryDeserializeColumnHeaderSettings()
	{
		try
		{
			return JsonConvert.DeserializeObject<List<ColumnHeaderInfo>>(Settings.Default.ListviewColumnHeader);
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private void UpdateLayout(object sender, EventArgs e)
	{
		columnListView.Size = new Size(DatabaseMapper.ScaleByDpi(180f), ClientSize.Height);
		columnListView.Columns[0].Width = columnListView.ClientSize.Width;
	}

	private void MoveSelectedColumnUp(object sender, EventArgs e)
	{
		MoveSelectedColumn(-1);
	}

	private void MoveSelectedColumnDown(object sender, EventArgs e)
	{
		MoveSelectedColumn(1);
	}

	private void MoveSelectedColumn(int delta)
	{
		if (columnListView.SelectedItems.Count <= 0)
		{
			return;
		}

		ListViewItem selectedItem = columnListView.SelectedItems[0];
		int newIndex = selectedItem.Index + delta;
		if (newIndex < 0 || newIndex >= columnListView.Items.Count)
		{
			return;
		}

		columnListView.Items.Remove(selectedItem);
		columnListView.Items.Insert(newIndex, selectedItem);
		Rectangle movedItemRect = columnListView.GetItemRect(newIndex);
		bool reachedViewportEdge = (delta < 0) ? (movedItemRect.Top <= columnListView.ClientRectangle.Top) : (movedItemRect.Bottom >= columnListView.ClientRectangle.Bottom);
		if (reachedViewportEdge)
		{
			columnListView.EnsureVisible(newIndex);
		}
	}

	private void OkButtonClick(object sender, EventArgs e)
	{
		DialogResult = DialogResult.OK;
		Close();
	}

	private void CancelButtonClick(object sender, EventArgs e)
	{
		DialogResult = DialogResult.Cancel;
		Close();
	}

	private void ResetColumnsClick(object sender, EventArgs e)
	{
		columnListView.Items.Clear();
		foreach (ColumnHeaderInfo column in GetColumnHeaderSettings())
		{
			ListViewItem listViewItem = new ListViewItem
			{
				Text = Resources.ResourceManager.GetString(column.Name),
				Checked = true,
				Tag = column
			};
			column.tempWidth = GetDefaultColumnWidths()[column.Name];
			columnListView.Items.Add(listViewItem);
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
		components = new Container();
		columnListView = new ListView();
		columnNameHeader = new ColumnHeader();
		buttonPanel = new Panel();
		resetButton = new Button();
		cancelButton = new Button();
		okButton = new Button();
		moveDownButton = new Button();
		moveUpButton = new Button();

		buttonPanel.SuspendLayout();
		SuspendLayout();

		columnListView.CheckBoxes = true;
		columnListView.Columns.AddRange(new ColumnHeader[1] { columnNameHeader });
		columnListView.FullRowSelect = true;
		columnListView.HeaderStyle = ColumnHeaderStyle.None;
		columnListView.HideSelection = false;
		columnListView.Location = new Point(0, 0);
		columnListView.Margin = new Padding(0);
		columnListView.MultiSelect = false;
		columnListView.Name = "listView1";
		columnListView.Size = new Size(180, 563);
		columnListView.TabIndex = 0;
		columnListView.UseCompatibleStateImageBehavior = false;
		columnListView.View = View.Details;
		columnNameHeader.Width = 200;

		buttonPanel.Controls.Add(resetButton);
		buttonPanel.Controls.Add(cancelButton);
		buttonPanel.Controls.Add(okButton);
		buttonPanel.Controls.Add(moveDownButton);
		buttonPanel.Controls.Add(moveUpButton);
		buttonPanel.Location = new Point(183, 82);
		buttonPanel.Name = "panel1";
		buttonPanel.Size = new Size(119, 481);
		buttonPanel.TabIndex = 6;

		moveUpButton.Location = new Point(17, 62);
		moveUpButton.Margin = new Padding(0);
		moveUpButton.Name = "btnMoveUp";
		moveUpButton.Size = new Size(85, 23);
		moveUpButton.TabIndex = 6;
		moveUpButton.Text = "Move up";
		moveUpButton.UseVisualStyleBackColor = true;
		moveUpButton.Click += MoveSelectedColumnUp;

		moveDownButton.Location = new Point(17, 112);
		moveDownButton.Margin = new Padding(0);
		moveDownButton.Name = "btnMoveDown";
		moveDownButton.Size = new Size(85, 23);
		moveDownButton.TabIndex = 7;
		moveDownButton.Text = "Move down";
		moveDownButton.UseVisualStyleBackColor = true;
		moveDownButton.Click += MoveSelectedColumnDown;

		resetButton.Location = new Point(17, 162);
		resetButton.Margin = new Padding(0);
		resetButton.Name = "btnReset";
		resetButton.Size = new Size(85, 23);
		resetButton.TabIndex = 10;
		resetButton.Text = "Reset";
		resetButton.UseVisualStyleBackColor = true;
		resetButton.Click += ResetColumnsClick;

		okButton.Location = new Point(17, 342);
		okButton.Margin = new Padding(0);
		okButton.Name = "btnOK";
		okButton.Size = new Size(85, 23);
		okButton.TabIndex = 8;
		okButton.Text = "OK";
		okButton.UseVisualStyleBackColor = true;
		okButton.Click += OkButtonClick;

		cancelButton.Location = new Point(17, 382);
		cancelButton.Margin = new Padding(0);
		cancelButton.Name = "btnCancel";
		cancelButton.Size = new Size(85, 23);
		cancelButton.TabIndex = 9;
		cancelButton.Text = "Cancel";
		cancelButton.UseVisualStyleBackColor = true;
		cancelButton.Click += CancelButtonClick;

		AutoScaleDimensions = new SizeF(96f, 96f);
		AutoScaleMode = AutoScaleMode.Dpi;
		ClientSize = new Size(304, 561);
		Controls.Add(buttonPanel);
		Controls.Add(columnListView);
		Font = new Font("Tahoma", 9f, FontStyle.Regular, GraphicsUnit.Point, 0);
		FormBorderStyle = FormBorderStyle.FixedDialog;
		MaximizeBox = false;
		MaximumSize = new Size(320, 600);
		MinimizeBox = false;
		MinimumSize = new Size(320, 600);
		Name = "FormCustColumns";
		ShowIcon = false;
		SizeGripStyle = SizeGripStyle.Hide;
		StartPosition = FormStartPosition.CenterParent;
		Text = "Customize columns";
		SizeChanged += UpdateLayout;

		buttonPanel.ResumeLayout(performLayout: false);
		ResumeLayout(performLayout: false);
	}


}

