using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace MusicTag.Tests;

// 原 Verify-Build.ps1 的两个反射构造冒烟(net481 时代经外部 Windows PowerShell LoadFrom 反射),
// net8 迁移后外部宿主(netfx CLR 的 powershell.exe / x64 的 pwsh)无法加载 x86 net8 程序集,
// 并入特征套件:本进程即 x86 net8 WinForms + [STAThread],构造走完整 designer/资源路径。
internal static class DialogConstructionSmoke
{
	public static IEnumerable<(string, Action)> All()
	{
		yield return ("Smoke: FilenameRelatedBatchDialog reflective construction + dispose (zh-CHS ui culture)", delegate
		{
			RunWithAppUiCulture(delegate
			{
				Form dialog = (Form)ConstructNonPublic(typeof(MusicTag.Schemes.FilenameRelatedBatchDialog));
				try
				{
					Check.True(dialog != null, "constructed");
					TabControl tabs = (TabControl)GetField(dialog, "tabControl");
					Button patternOk = (Button)GetField(dialog, "patternOkButton");
					Button patternCancel = (Button)GetField(dialog, "patternCancelButton");
					Button regexOk = (Button)GetField(dialog, "regexOkButton");
					Button regexCancel = (Button)GetField(dialog, "regexCancelButton");
					dialog.Show();
					Application.DoEvents();
					tabs.SelectedIndex = 0;
					Application.DoEvents();
					Check.True(ReferenceEquals(dialog.AcceptButton, patternOk), "pattern tab Enter button");
					Check.True(ReferenceEquals(dialog.CancelButton, patternCancel), "pattern tab Escape button");
					tabs.SelectedIndex = 1;
					Application.DoEvents();
					Check.True(ReferenceEquals(dialog.AcceptButton, regexOk), "regex tab Enter button");
					Check.True(ReferenceEquals(dialog.CancelButton, regexCancel), "regex tab Escape button");
				}
				finally
				{
					dialog.Dispose();
				}
			});
		});

		yield return ("Smoke: OptionsDialog reflective construction + dispose (zh-CHS ui culture)", delegate
		{
			RunWithAppUiCulture(delegate
			{
				Form dialog = (Form)ConstructNonPublic(typeof(MusicTag.Importers.OptionsDialog));
				try
				{
					Check.True(dialog != null, "constructed");
					Check.True(ReferenceEquals(dialog.AcceptButton, GetField(dialog, "okButton")), "Enter invokes OK");
					CheckTranslatedLyricConnectorState(dialog);
				}
				finally
				{
					dialog.Dispose();
				}
			});
		});

		yield return ("Smoke: CombinedTagSearchDialog track ID toolbar layout + dispose (zh-CHS ui culture)", delegate
		{
			RunWithAppUiCulture(delegate
			{
				Form dialog = (Form)ConstructNonPublic(typeof(MusicTag.Mocks.CombinedTagSearchDialog));
				try
				{
					TableLayoutPanel lookupPanel = (TableLayoutPanel)GetField(dialog, "trackIdLookupPanel");
					ComboBox source = (ComboBox)GetField(dialog, "trackIdSourceComboBox");
					TextBox idInput = (TextBox)GetField(dialog, "trackIdTextBox");
					Button lookup = (Button)GetField(dialog, "trackIdLookupButton");
					Check.Equal(4, source.Items.Count, "four ID lookup sources");
					Check.True(lookupPanel.Height > 0, "lookup panel has stable height");
					Check.True(idInput.Width > 100, "ID input remains usable");
					Check.True(lookup.Width > 0 && lookup.Right <= lookupPanel.ClientSize.Width, "lookup button fits panel");
					ListView results = (ListView)GetField(dialog, "searchResultsListView");
					ImageList covers = (ImageList)GetField(dialog, "coverImageList");
					int[] widths96 = new int[results.Columns.Count];
					for (int index = 0; index < results.Columns.Count; index++)
					{
						widths96[index] = results.Columns[index].Width;
					}
					InvokeMethod(dialog, "ApplyDpiMetrics", 144);
					Check.Equal(192, covers.ImageSize.Width, "150 percent combined-search cover width");
					Check.Equal(27, lookup.Image.Width, "150 percent lookup icon width");
					Check.Equal(27, lookup.Image.Height, "150 percent lookup icon height");
					Check.Equal(54, lookup.MinimumSize.Width, "150 percent lookup button minimum width");
					Check.Equal(42, lookup.MinimumSize.Height, "150 percent lookup button minimum height");
					Check.Equal((int)Math.Round(widths96[0] * 1.5d), results.Columns[0].Width, "150 percent cover column width");
					InvokeMethod(dialog, "ApplyDpiMetrics", 96);
					Check.Equal(128, covers.ImageSize.Width, "100 percent combined-search cover width after downscale");
					Check.Equal(18, lookup.Image.Width, "100 percent lookup icon width after downscale");
					Check.Equal(18, lookup.Image.Height, "100 percent lookup icon height after downscale");
					Check.Equal(36, lookup.MinimumSize.Width, "100 percent lookup button minimum width after downscale");
					Check.Equal(widths96[0], results.Columns[0].Width, "cover column width round trips without compounding");
					Check.True(ReferenceEquals(dialog.AcceptButton, GetField(dialog, "okSplitButton")), "dialog Enter remains OK outside ID input");
				}
				finally
				{
					dialog.Dispose();
				}
			});
		});

		yield return ("TagSearchCandidatePanel DPI metrics are absolute and round-trip safely", delegate
		{
			Control panel = (Control)ConstructNonPublic(typeof(MusicTag.Consumers.TagSearchCandidatePanel));
			try
			{
				Label sourceLabel = (Label)GetField(panel, "sourceLabel");
				PictureBox lyricPictureBox = (PictureBox)GetField(panel, "lyricPictureBox");
				InvokeMethod(panel, "ApplyDpiMetrics", 144);
				Check.Equal(30, sourceLabel.Height, "150 percent source label height");
				Check.Equal(24, lyricPictureBox.Height, "150 percent lyric icon height");
				Check.Equal(3, lyricPictureBox.Margin.Top, "150 percent lyric margin");
				InvokeMethod(panel, "ApplyDpiMetrics", 96);
				Check.Equal(20, sourceLabel.Height, "100 percent source label height");
				Check.Equal(16, lyricPictureBox.Height, "100 percent lyric icon height");
				Check.Equal(2, lyricPictureBox.Margin.Top, "100 percent lyric margin");
				InvokeMethod(panel, "ApplyDpiMetrics", 144);
				Check.Equal(30, sourceLabel.Height, "round-trip source label does not compound");
			}
			finally
			{
				panel.Dispose();
			}
		});

		yield return ("CustomColumnsDialog row metrics use window-local DPI", delegate
		{
			using Form dialog = (Form)ConstructNonPublic(typeof(MusicTagWinApp.Common.CustomColumnsDialog));
			ListView columnList = (ListView)GetField(dialog, "columnListView");
			InvokeMethod(dialog, "ApplyDpiMetrics", 144);
			Check.Equal(48, columnList.SmallImageList.ImageSize.Height, "150 percent custom-column row height");
			InvokeMethod(dialog, "ApplyDpiMetrics", 96);
			Check.Equal(32, columnList.SmallImageList.ImageSize.Height, "custom-column row height round-trip");
		});

		yield return ("SourceOrderControl DPI assets use local absolute metrics", delegate
		{
			Control control = (Control)ConstructNonPublic(typeof(MusicTagWinApp.Stubs.SourceOrderControl));
			try
			{
				Button moveUpButton = (Button)GetField(control, "moveUpButton");
				FlowLayoutPanel buttonPanel = (FlowLayoutPanel)GetField(control, "buttonPanel");
				InvokeMethod(control, "ApplyDpiMetrics", 144);
				Check.Equal(45, buttonPanel.Width, "150 percent source button panel width");
				Check.Equal(45, moveUpButton.Width, "150 percent source button width");
				Check.Equal(34, moveUpButton.Height, "150 percent source button height");
				Check.Equal(30, moveUpButton.Image.Width, "150 percent source icon width");
				InvokeMethod(control, "ApplyDpiMetrics", 96);
				Check.Equal(30, buttonPanel.Width, "100 percent source button panel width");
				Check.Equal(30, moveUpButton.Width, "100 percent source button width");
				Check.Equal(23, moveUpButton.Height, "100 percent source button height");
				Check.Equal(20, moveUpButton.Image.Width, "100 percent source icon width");
			}
			finally
			{
				control.Dispose();
			}
		});

		yield return ("FindReplaceDialog DPI layout and icons round-trip absolutely", delegate
		{
			Form dialog = (Form)ConstructNonPublic(typeof(MusicTag.Consumers.FindReplaceDialog));
			try
			{
				Button previousButton = (Button)GetField(dialog, "findPreviousButton");
				TextBox findTextBox = (TextBox)GetField(dialog, "findWhatTextBox");
				InvokeMethod(dialog, "ApplyDpiMetrics", 144);
				Check.Equal(375, findTextBox.Width, "150 percent find text width");
				Check.Equal(46, previousButton.Width, "150 percent find button width");
				Check.Equal(30, previousButton.Image.Width, "150 percent find icon width");
				InvokeMethod(dialog, "ApplyDpiMetrics", 96);
				Check.Equal(250, findTextBox.Width, "100 percent find text width");
				Check.Equal(31, previousButton.Width, "100 percent find button width");
				Check.Equal(20, previousButton.Image.Width, "100 percent find icon width");
			}
			finally
			{
				dialog.Dispose();
			}
		});

		yield return ("LyricEditorDialog DPI buttons and icons round-trip absolutely", delegate
		{
			Form dialog = (Form)ConstructNonPublic(typeof(MusicTagWinApp.Instances.LyricEditorDialog));
			try
			{
				Button searchButton = (Button)GetField(dialog, "searchButton");
				Button saveButton = (Button)GetField(dialog, "saveAsLrcButton");
				InvokeMethod(dialog, "ApplyDpiMetrics", 144);
				Check.Equal(150, searchButton.Width, "150 percent lyric search width");
				Check.Equal(180, saveButton.Width, "150 percent lyric save width");
				Check.Equal(36, searchButton.Image.Width, "150 percent lyric search icon width");
				InvokeMethod(dialog, "ApplyDpiMetrics", 96);
				Check.Equal(100, searchButton.Width, "100 percent lyric search width");
				Check.Equal(120, saveButton.Width, "100 percent lyric save width");
				Check.Equal(24, searchButton.Image.Width, "100 percent lyric search icon width");
			}
			finally
			{
				dialog.Dispose();
			}
		});

		yield return ("OptionsDialog runtime-created network controls use window-local DPI", delegate
		{
			Form dialog = (Form)ConstructNonPublic(typeof(MusicTag.Importers.OptionsDialog));
			try
			{
				SplitContainer split = (SplitContainer)GetField(dialog, "mainSplitContainer");
				TextBox cookie = (TextBox)GetField(dialog, "qqCookieTextBox");
				GroupBox networkGroup = (GroupBox)GetField(dialog, "networkOptionsGroupBox");
				InvokeMethod(dialog, "ApplyDpiMetrics", 144);
				Check.Equal(180, split.SplitterDistance, "150 percent options navigation width");
				Check.Equal(585, cookie.Width, "150 percent cookie editor width");
				Check.Equal(81, cookie.Height, "150 percent cookie editor height");
				Check.Equal(7, networkGroup.Padding.Left, "150 percent network group padding");
				InvokeMethod(dialog, "ApplyDpiMetrics", 96);
				Check.Equal(120, split.SplitterDistance, "100 percent options navigation width");
				Check.Equal(390, cookie.Width, "100 percent cookie editor width");
				Check.Equal(54, cookie.Height, "100 percent cookie editor height");
				Check.Equal(5, networkGroup.Padding.Left, "100 percent network group padding");
			}
			finally
			{
				dialog.Dispose();
			}
		});

		yield return ("Fixed-list dialogs keep row metrics and columns stable across DPI round-trips", delegate
		{
			Form historyDialog = (Form)ConstructNonPublic(typeof(MusicTag.Composer.TagHistorySelectionDialog));
			Form encodingDialog = (Form)ConstructNonPublic(typeof(MusicTag.Consumers.CharacterSetSelectionDialog));
			Form overwriteDialog = (Form)ConstructNonPublic(typeof(MusicTag.Importers.CombinedTagOverwriteOptionsDialog));
			Form directoryDialog = (Form)ConstructNonPublic(typeof(MusicTagWinApp.Common.DirectoryManagerDialog));
			Form lyricDialog = (Form)ConstructNonPublic(typeof(MusicTagWinApp.Exporters.LyricSearchDialog));
			try
			{
				ImageList historyRows = (ImageList)GetField(historyDialog, "rowHeightImageList");
				ColumnHeader historyTitle = (ColumnHeader)GetField(historyDialog, "titleColumn");
				InvokeMethod(historyDialog, "ApplyDpiMetrics", 144);
				Check.Equal(60, historyRows.ImageSize.Height, "150 percent history row height");
				Check.Equal(195, historyTitle.Width, "150 percent history title width");
				InvokeMethod(historyDialog, "ApplyDpiMetrics", 96);
				Check.Equal(40, historyRows.ImageSize.Height, "100 percent history row height");
				Check.Equal(130, historyTitle.Width, "history title width round-trip");

				ListView encodingList = (ListView)GetField(encodingDialog, "encodingListView");
				ColumnHeader encodingColumn = (ColumnHeader)GetField(encodingDialog, "encodingColumn");
				InvokeMethod(encodingDialog, "ApplyDpiMetrics", 144);
				Check.Equal(48, encodingList.SmallImageList.ImageSize.Height, "150 percent encoding row height");
				Check.Equal(225, encodingColumn.Width, "150 percent encoding column width");
				InvokeMethod(encodingDialog, "ApplyDpiMetrics", 96);
				Check.Equal(150, encodingColumn.Width, "encoding column width round-trip");

				ListView overwriteList = (ListView)GetField(overwriteDialog, "optionListView");
				ListView directoryList = (ListView)GetField(directoryDialog, "directoryListView");
				InvokeMethod(overwriteDialog, "ApplyDpiMetrics", 144);
				InvokeMethod(directoryDialog, "ApplyDpiMetrics", 144);
				Check.Equal(48, overwriteList.SmallImageList.ImageSize.Height, "150 percent overwrite row height");
				Check.Equal(48, directoryList.SmallImageList.ImageSize.Height, "150 percent directory row height");

				ImageList lyricIcons = (ImageList)GetField(lyricDialog, "lyricIconImages");
				ListView lyricList = (ListView)GetField(lyricDialog, "lyricListView");
				_ = lyricList.Handle;
				Check.Equal(2, lyricIcons.Images.Count, "lyric ImageList survives deferred native handle creation");
				ColumnHeader lyricTitle = (ColumnHeader)GetField(lyricDialog, "titleColumn");
				InvokeMethod(lyricDialog, "ApplyDpiMetrics", 144);
				Check.Equal(48, lyricIcons.ImageSize.Height, "150 percent lyric result icon height");
				Check.Equal(270, lyricTitle.Width, "150 percent lyric title width");
				Check.Equal(2, lyricIcons.Images.Count, "lyric icons remain populated after upscale");
				InvokeMethod(lyricDialog, "ApplyDpiMetrics", 96);
				Check.Equal(32, lyricIcons.ImageSize.Height, "100 percent lyric result icon height");
				Check.Equal(180, lyricTitle.Width, "lyric title width round-trip");
				Check.Equal(2, lyricIcons.Images.Count, "lyric icons remain populated after round-trip");
			}
			finally
			{
				historyDialog.Dispose();
				encodingDialog.Dispose();
				overwriteDialog.Dispose();
				directoryDialog.Dispose();
				lyricDialog.Dispose();
			}
		});

		yield return ("Dynamic cover thumbnails rebuild from original sources across DPI round-trips", delegate
		{
			string cachedCoverPath = Path.Combine(Path.GetTempPath(), "mtcover_" + Guid.NewGuid().ToString("N") + ".png");
			string taggedAudioPath = WriteEmbeddedPictureFixture();
			Form coverDialog = null;
			Form pictureFromTagsDialog = null;
			string stage = "create cached cover";
			try
			{
				using (Bitmap cover = new Bitmap(320, 180))
				using (Graphics graphics = Graphics.FromImage(cover))
				{
					graphics.Clear(Color.CornflowerBlue);
					cover.Save(cachedCoverPath, ImageFormat.Png);
				}

				stage = "construct network cover dialog";
				coverDialog = (Form)ConstructNonPublic(typeof(MusicTagWinApp.Listeners.CoverSearchDialog));
				ListView coverList = (ListView)GetField(coverDialog, "candidateListView");
				ImageList coverImages = (ImageList)GetField(coverDialog, "candidateImageList");
				coverList.Items.Add(new ListViewItem("cached")
				{
					ImageKey = cachedCoverPath
				});
				stage = "upscale network cover thumbnails";
				InvokeMethod(coverDialog, "ApplyDpiMetrics", 144);
				Check.Equal(192, coverImages.ImageSize.Width, "150 percent network cover thumbnail size");
				Check.True(coverImages.Images.ContainsKey(cachedCoverPath), "network cover reloaded from cached original");
				using (Image thumbnail = coverImages.Images[cachedCoverPath])
				{
					Check.Equal(192, thumbnail.Width, "network cover thumbnail rebuilt at 150 percent");
				}
				File.Delete(cachedCoverPath);
				InvokeMethod(coverDialog, "ApplyDpiMetrics", 240);
				Check.Equal(256, coverImages.ImageSize.Width, "network cover thumbnail respects ImageList maximum at 250 percent");
				Check.True(coverImages.Images.ContainsKey(cachedCoverPath), "network cover round-trip uses retained master thumbnail");
				InvokeMethod(coverDialog, "ApplyDpiMetrics", 96);
				Check.Equal(128, coverImages.ImageSize.Width, "network cover thumbnail returns to 100 percent");

				stage = "construct embedded-picture dialog";
				pictureFromTagsDialog = (Form)ConstructNonPublic(typeof(MusicTag.Importers.PictureFromTagsDialog));
				ListView pictureList = (ListView)GetField(pictureFromTagsDialog, "pictureListView");
				ImageList pictureImages = (ImageList)GetField(pictureFromTagsDialog, "pictureImageList");
				string embeddedImageKey = taggedAudioPath + "_0";
				pictureList.Items.Add(new ListViewItem("1x1")
				{
					ImageKey = embeddedImageKey,
					Tag = (taggedAudioPath, 0)
				});
				stage = "upscale embedded-picture thumbnails";
				InvokeMethod(pictureFromTagsDialog, "ApplyDpiMetrics", 144);
				Check.Equal(192, pictureImages.ImageSize.Width, "150 percent embedded-picture thumbnail size");
				Check.True(pictureImages.Images.ContainsKey(embeddedImageKey), "embedded picture reloaded from audio tag");
				using (Image thumbnail = pictureImages.Images[embeddedImageKey])
				{
					Check.Equal(192, thumbnail.Width, "embedded picture rebuilt at 150 percent");
				}
				File.Delete(taggedAudioPath);
				InvokeMethod(pictureFromTagsDialog, "ApplyDpiMetrics", 240);
				Check.Equal(256, pictureImages.ImageSize.Width, "embedded-picture thumbnail respects ImageList maximum at 250 percent");
				Check.True(pictureImages.Images.ContainsKey(embeddedImageKey), "embedded-picture round-trip uses retained master thumbnail");
				InvokeMethod(pictureFromTagsDialog, "ApplyDpiMetrics", 96);
				Check.Equal(128, pictureImages.ImageSize.Width, "embedded-picture thumbnail returns to 100 percent");
			}
			catch (Exception ex)
			{
				throw new Exception(stage + ": " + ex, ex);
			}
			finally
			{
				coverDialog?.Dispose();
				pictureFromTagsDialog?.Dispose();
				File.Delete(cachedCoverPath);
				File.Delete(taggedAudioPath);
			}
		});
	}

	private static string WriteEmbeddedPictureFixture()
	{
		byte[] header = { 0xFF, 0xFB, 0x90, 0x64 };
		const int frameSize = 417;
		const int frameCount = 4;
		byte[] mp3 = new byte[frameSize * frameCount];
		for (int i = 0; i < frameCount; i++)
		{
			Array.Copy(header, 0, mp3, i * frameSize, header.Length);
		}
		string path = Path.Combine(Path.GetTempPath(), "mttagcover_" + Guid.NewGuid().ToString("N") + ".mp3");
		File.WriteAllBytes(path, mp3);
		try
		{
			using MusicTag.States.ConfigDescriptorState state = new MusicTag.States.ConfigDescriptorState(path);
			state.LoadBasicTagFields();
			state.LoadLyrics();
			state["allpicturedata"] = new List<MusicTag.States.ConfigDescriptorState.PictureData>
			{
				new MusicTag.States.ConfigDescriptorState.PictureData
				{
					ImageBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="),
					PictureType = "Front Cover"
				}
			};
			if (!state.SaveTagFields())
			{
				throw new Exception("failed to seed embedded-picture fixture");
			}
			return path;
		}
		catch
		{
			File.Delete(path);
			throw;
		}
	}

	private static void CheckTranslatedLyricConnectorState(Form dialog)
	{
		CheckBox downloadTranslatedLyrics = (CheckBox)GetField(dialog, "downloadTranslatedLyricsCheckBox");
		RadioButton sameLineFormat = (RadioButton)GetField(dialog, "translatedLyricFormat1RadioButton");
		RadioButton separateLineFormat = (RadioButton)GetField(dialog, "translatedLyricFormat2RadioButton");
		ComboBox separator = (ComboBox)GetField(dialog, "lyricTranslationSeparatorComboBox");

		downloadTranslatedLyrics.Checked = true;
		separateLineFormat.Checked = true;
		Check.True(!separator.Enabled, "separator disabled for separate-line format");

		// 用户复现路径：取消“下载翻译”后重新勾选。通用控件启用逻辑不得覆盖格式限制。
		downloadTranslatedLyrics.Checked = false;
		downloadTranslatedLyrics.Checked = true;
		Check.True(!separator.Enabled, "separator stays disabled after translation toggle in separate-line format");

		sameLineFormat.Checked = true;
		Check.True(separator.Enabled, "separator enabled for same-line format while translation is enabled");

		downloadTranslatedLyrics.Checked = false;
		Check.True(!separator.Enabled, "separator disabled when translated lyric download is disabled");
	}

	// app 启动即显式设 CurrentUICulture ∈ {zh-CHS, zh-CHT, en}(StateFieldInstance 语言初始化),
	// 部分对话框资源(如 MusicTag.Schemes.EventRulesSchema)只存在于三个卫星程序集,neutral
	// 程序集没有兜底副本。netfx 下测试进程的 zh-CN 经 legacy 父链 zh-CN→zh-CHS 恰好命中卫星;
	// net8/ICU 的父链是 zh-CN→zh-Hans→zh→neutral,不再路过 zh-CHS → MissingManifestResourceException。
	// 冒烟按 app 真实运行环境显式设 zh-CHS(用完复位),不依赖宿主系统 culture。
	private static void RunWithAppUiCulture(Action action)
	{
		CultureInfo saved = Thread.CurrentThread.CurrentUICulture;
		Thread.CurrentThread.CurrentUICulture = CultureInfo.CreateSpecificCulture("zh-CHS");
		try
		{
			action();
		}
		finally
		{
			Thread.CurrentThread.CurrentUICulture = saved;
		}
	}

	private static object ConstructNonPublic(Type type)
	{
		try
		{
			return Activator.CreateInstance(type, nonPublic: true);
		}
		catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException != null)
		{
			// 冒烟诊断:剥掉反射包装,直接暴露构造器内的真实异常(类型+消息+栈)。
			throw new Exception(type.Name + " ctor threw: " + ex.InnerException.GetType().Name + ": " + ex.InnerException.Message + Environment.NewLine + ex.InnerException.StackTrace, ex.InnerException);
		}
	}

	private static object GetField(object instance, string fieldName)
	{
		FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
		if (field == null)
		{
			throw new Exception("field not found: " + fieldName);
		}
		return field.GetValue(instance);
	}

	private static object InvokeMethod(object instance, string methodName, params object[] arguments)
	{
		MethodInfo method = instance.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
		if (method == null)
		{
			throw new Exception("method not found: " + methodName);
		}
		return method.Invoke(instance, arguments);
	}
}
