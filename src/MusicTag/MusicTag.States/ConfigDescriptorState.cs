using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using MusicTag.Serialization;
using MusicTagWinApp.Instances;
using MusicTagWinApp.Properties;

namespace MusicTag.States;

// Tag I/O is backed by TagLibSharp (managed). No native MusicTag.dll bindings
// remain anywhere in the app — every field/picture/audio-property read and write
// goes through TagLib.File. The public API, the TagValues cache keys and their
// types are unchanged so every caller (StateFieldInstance, AutoMatchTagsDialog, …)
// is untouched.
internal class ConfigDescriptorState : IDisposable
{
	public class PictureData
	{
		private byte[] imageBytes;

		private string pictureType;

		private string mimeType;

		private int width;

		private int height;

		private bool processingFailed;

		public byte[] ImageBytes
		{
			get => imageBytes;
			set => imageBytes = value;
		}

		public string PictureType
		{
			get => pictureType;
			set => pictureType = value;
		}

		public string MimeType
		{
			get => mimeType;
			set => mimeType = value;
		}

		public int Width
		{
			get => width;
			set => width = value;
		}

		public int Height
		{
			get => height;
			set => height = value;
		}

		public bool ProcessingFailed
		{
			get => processingFailed;
			set => processingFailed = value;
		}
	}

	private TagLib.File tagFile;

	private static readonly string[] supportedPictureMimeTypes;

	private static readonly List<string> pictureTypeNames;

	private static readonly Encoding Latin1Encoding = Encoding.GetEncoding("ISO-8859-1");

	private static readonly object id3v2VersionLock = new object();

	private readonly Dictionary<string, object> tagValues;

	private string filePath;

	private string loadError;

	public object this[string fieldName]
	{
		get
		{
			return TagValues[fieldName];
		}
		set
		{
			TagValues[fieldName] = value;
		}
	}

	public static string[] SupportedPictureMimeTypes()
	{
		return supportedPictureMimeTypes;
	}

	private Dictionary<string, object> TagValues
	{
		get => tagValues;
	}

	public static List<string> PictureTypeNames()
	{
		return pictureTypeNames;
	}

	public string GetFilePath()
	{
		return filePath;
	}

	private void SetFilePath(string value)
	{
		filePath = value;
	}

	public string GetLoadError()
	{
		return loadError;
	}

	public bool IsLoadedSuccessfully()
	{
		return GetLoadError() == null;
	}

	public ConfigDescriptorState()
	{
		tagValues = new Dictionary<string, object>();
	}

	public ConfigDescriptorState(string filePath)
	{
		tagValues = new Dictionary<string, object>();
		SetFilePath(filePath);
		try
		{
			if (!System.IO.File.Exists(filePath))
			{
				loadError = Resources.Msg_FileNotFound;
				return;
			}
			tagFile = TagLib.File.Create(filePath);
		}
		catch (TagLib.UnsupportedFormatException)
		{
			loadError = Resources.Msg_InvalidFile;
		}
		catch (TagLib.CorruptFileException)
		{
			loadError = Resources.Msg_InvalidFile;
		}
		catch (System.IO.FileNotFoundException)
		{
			loadError = Resources.Msg_FileNotFound;
		}
		catch (Exception)
		{
			loadError = Resources.Msg_OpenFileFail + "(-1)";
		}
	}

	public void Dispose()
	{
		if (tagFile != null)
		{
			tagFile.Dispose();
			tagFile = null;
		}
	}

	public void LoadBasicTagFields()
	{
		if (!TagValues.ContainsKey("tagtypes"))
		{
			TagValues.Add("tagtypes", BuildTagTypesSummary());
		}
		if (!TagValues.ContainsKey("fileext"))
		{
			TagValues.Add("fileext", BuildFileExtension(filePath));
		}
		string[] tagFields = new string[13]
		{
			"title", "artist", "album", "year", "track", "disc", "trackstr", "discstr", "genre", "albumartist",
			"composer", "lyricist", "comment"
		};
		foreach (string tagField in tagFields)
		{
			if (TagValues.ContainsKey(tagField))
			{
				continue;
			}
			string tagValue = ReadFieldText(tagField);
			switch (tagField)
			{
				case "trackstr":
					TagValues.Add(tagField, ((int)TagValues["track"] > 0) ? tagValue : "");
					break;
				case "discstr":
					TagValues.Add(tagField, ((int)TagValues["disc"] > 0) ? tagValue : "");
					break;
				case "track":
				case "disc":
					TagValues.Add(tagField, int.TryParse(tagValue, out var numericValue) ? numericValue : 0);
					break;
				default:
					TagValues.Add(tagField, tagValue);
					break;
			}
		}
	}

	public void LoadRawTextFieldData()
	{
		string[] textFields = new string[10] { "title", "artist", "album", "year", "genre", "albumartist", "composer", "lyricist", "comment", "lyrics" };
		foreach (string textField in textFields)
		{
			string dataKey = textField + "data";
			string tagTypeKey = textField + "data_tagtype";
			string stringTypeKey = textField + "data_stringtype";
			if (TagValues.ContainsKey(dataKey))
			{
				continue;
			}
			List<byte[]> dataBlocks = new List<byte[]>();
			string tagType = "";
			string stringType = "";
			FillRawFieldData(textField, dataBlocks, ref tagType, ref stringType);
			TagValues.Add(dataKey, dataBlocks);
			TagValues.Add(tagTypeKey, tagType);
			TagValues.Add(stringTypeKey, stringType);
		}
	}

	public void LoadLyrics()
	{
		if (!TagValues.ContainsKey("lyrics"))
		{
			TagValues.Add("lyrics", tagFile.Tag.Lyrics ?? "");
		}
	}

	public void LoadAudioProperties()
	{
		TagLib.Properties properties = tagFile.Properties;
		AddIfAbsent("bitpersample", delegate
		{
			int bitsPerSample = (properties != null) ? properties.BitsPerSample : 0;
			// Native reports 16 for lossy formats (where bit depth is meaningless and
			// TagLibSharp returns 0); lossless formats carry a real value. Preserve that.
			return (bitsPerSample > 0) ? bitsPerSample : 16;
		});
		AddIfAbsent("channels", () => (properties != null) ? properties.AudioChannels : 0);
		AddIfAbsent("samplerate", () => (properties != null) ? properties.AudioSampleRate : 0);
		AddIfAbsent("bitrate", () => (properties != null) ? properties.AudioBitrate : 0);
		AddIfAbsent("durationinms", () => (properties != null) ? (int)properties.Duration.TotalMilliseconds : 0);
		AddIfAbsent("hasvideotrack", () => properties != null && (properties.MediaTypes & TagLib.MediaTypes.Video) != 0);
	}

	private void AddIfAbsent(string key, Func<object> valueFactory)
	{
		if (!TagValues.ContainsKey(key))
		{
			TagValues.Add(key, valueFactory());
		}
	}

	public void LoadPictureSummary(bool flagOnly)
	{
		if (flagOnly)
		{
			if (!TagValues.ContainsKey("haspicture"))
			{
				TagLib.IPicture picture = GetFirstValidPicture();
				TagValues.Add("haspicture", picture != null && picture.Data != null && picture.Data.Count > 0);
			}
			return;
		}
		if (TagValues.ContainsKey("haspicture"))
		{
			return;
		}
		TagValues.Add("haspicture", false);
		if (!TagValues.ContainsKey("picturedata"))
		{
			TagLib.IPicture picture = GetFirstValidPicture();
			if (picture != null && picture.Data != null && picture.Data.Count > 0)
			{
				TagValues.Add("picturedata", picture.Data.Data);
				TagValues["haspicture"] = true;
			}
		}
	}

	public void LoadAllPictures()
	{
		List<PictureData> pictures = default(List<PictureData>);
		if (!TagValues.ContainsKey("allpicturedata"))
		{
			pictures = new List<PictureData>();
			try
			{
				TagLib.IPicture[] nativePictures = tagFile.Tag.Pictures;
				if (nativePictures != null)
				{
					foreach (TagLib.IPicture nativePicture in nativePictures)
					{
						// Filter non-image attachments (e.g. Serato DJ application/json blobs
						// stored as APIC frames of type NotAPicture) exactly like native does.
						if (nativePicture == null || nativePicture.Type == TagLib.PictureType.NotAPicture)
						{
							continue;
						}
						if (nativePicture.Data == null || nativePicture.Data.Count == 0)
						{
							continue;
						}
						pictures.Add(new PictureData
						{
							ImageBytes = nativePicture.Data.Data,
							PictureType = PictureTypeToName(nativePicture.Type)
						});
					}
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine("LoadAllPictures error:" + ex.GetMessageChain());
				try
				{
					LoadPictureSummary(flagOnly: false);
					if (TagValues.TryGetValue("picturedata", out object value) && value is byte[] pictureBytes)
					{
						pictures.Add(new PictureData
						{
							ImageBytes = pictureBytes,
							PictureType = "Other"
						});
					}
				}
				catch (Exception fallbackError)
				{
					Console.WriteLine("LoadFallbackPicture error:" + fallbackError.GetMessageChain());
				}
			}
			TagValues.Add("allpicturedata", pictures);
		}
		else
		{
			pictures = TagValues["allpicturedata"] as List<PictureData>;
		}
		TagValues["haspicture"] = pictures.Count > 0;
	}

	public static Image LoadPictureImage(PictureData pictureData)
	{
		try
		{
			using MemoryStream memoryStream = new MemoryStream(pictureData.ImageBytes);
			using Image image = Image.FromStream(memoryStream);
			ImageCodecInfo imageCodecInfo = ImageUtilities.GetImageDecoderByFormatId(image.RawFormat.Guid);
			if (imageCodecInfo != null && imageCodecInfo.MimeType != null)
			{
				pictureData.MimeType = imageCodecInfo.MimeType;
			}
			else
			{
				pictureData.MimeType = "";
			}
			pictureData.Width = image.Width;
			pictureData.Height = image.Height;
			return new Bitmap(image);
		}
		catch (Exception ex)
		{
			Console.WriteLine("LoadPictureImage error:{0}", ex.Message);
		}
		return null;
	}

	public static string GetGenreNameByIndex(int genreIndex)
	{
		string[] audioGenres = TagLib.Genres.Audio;
		if (genreIndex >= 0 && genreIndex < audioGenres.Length)
		{
			return audioGenres[genreIndex];
		}
		return "";
	}

	public string DecodeFieldWithEncoding(string fieldName, string encodingName, string currentText)
	{
		return TagTextEncoding.DecodeTagValue(fieldName, TagValues[fieldName + "data_stringtype"] as string, TagValues[fieldName + "data"] as List<byte[]>, TagValues[fieldName] as string, currentText, encodingName);
	}

	public bool IsExcludedByFileFilter()
	{
		if (Settings.Default.FileFilterByDuration > 0)
		{
			int minimumDurationMilliseconds = Settings.Default.FileFilterByDuration * 1000;
			if ((int)TagValues["durationinms"] < minimumDurationMilliseconds)
			{
				return true;
			}
		}
		if (Settings.Default.FileFilterIgnoreVideoFile)
		{
			return (bool)TagValues["hasvideotrack"];
		}
		return false;
	}

	private bool SaveWithId3v2Version(Action writeBody)
	{
		lock (id3v2VersionLock)
		{
			byte previousDefaultVersion = TagLib.Id3v2.Tag.DefaultVersion;
			bool previousForceDefaultVersion = TagLib.Id3v2.Tag.ForceDefaultVersion;
			try
			{
				loadError = null;
				writeBody();
				SetId3v2Version();
				tagFile.Save();
				return true;
			}
			catch (Exception ex)
			{
				loadError = StateFieldInstance.ResolveFailureMessage(ex.Message);
				return false;
			}
			finally
			{
				TagLib.Id3v2.Tag.DefaultVersion = previousDefaultVersion;
				TagLib.Id3v2.Tag.ForceDefaultVersion = previousForceDefaultVersion;
			}
		}
	}

	public bool SaveTagFields()
	{
		return SaveWithId3v2Version(delegate
		{
			TagLib.Tag tag = tagFile.Tag;
			// Same fixed field order as the former native m0 string[12] contract.
			tag.Title = TagValues["title"] as string;
			tag.Performers = ToSingleValue(TagValues["artist"] as string);
			tag.Album = TagValues["album"] as string;
			SetYear(tag, TagValues["year"] as string);
			SetTrack(tag, TagValues["trackstr"] as string);
			SetDisc(tag, TagValues["discstr"] as string);
			tag.Genres = ToSingleValue(TagValues["genre"] as string);
			tag.AlbumArtists = ToSingleValue(TagValues["albumartist"] as string);
			tag.Composers = ToSingleValue(TagValues["composer"] as string);
			// Match the original native m0 behavior: it cleared ALL comment frames before
			// writing the new value, so a netease "163 key" COMM (which carries a non-empty
			// description) does NOT survive a comment edit. TagLib's Tag.Comment setter only
			// replaces the default (empty-description) COMM, so clear the rest explicitly to
			// stay behavior-equivalent (verified: original native write drops the 163 key).
			if (tagFile.GetTag(TagLib.TagTypes.Id3v2, create: false) is TagLib.Id3v2.Tag id3v2ForComment)
			{
				id3v2ForComment.RemoveFrames("COMM");
			}
			tag.Comment = TagValues["comment"] as string;
			WriteLyricist(TagValues["lyricist"] as string);
			tag.Lyrics = TagValues["lyrics"] as string;
			if (TagValues.TryGetValue("allpicturedata", out var value))
			{
				List<PictureData> pictures = value as List<PictureData>;
				List<TagLib.IPicture> tagLibPictures = new List<TagLib.IPicture>();
				foreach (PictureData picture in pictures)
				{
					if (picture.MimeType == null || picture.Width == 0 || picture.Height == 0)
					{
						using (LoadPictureImage(picture))
						{
						}
					}
					TagLib.Picture tagLibPicture = new TagLib.Picture(new TagLib.ByteVector(picture.ImageBytes))
					{
						Type = NameToPictureType(picture.PictureType),
						MimeType = picture.MimeType,
						Description = ""
					};
					tagLibPictures.Add(tagLibPicture);
				}
				tag.Pictures = tagLibPictures.ToArray();
			}
		});
	}

	public bool SaveCurrentTagFile()
	{
		return SaveWithId3v2Version(delegate
		{
		});
	}

	public bool TryGetRawValue(string key, out object value)
	{
		bool found = TagValues.TryGetValue(key, out object rawValue);
		value = found ? rawValue : null;
		return found;
	}

	public bool RemoveRawValue(string fieldName)
	{
		return TagValues.Remove(fieldName);
	}

	public string GetDisplayValue(string fieldName)
	{
		// 缺键或空值时返回空串而非抛 KeyNotFoundException / 强转空值崩溃
		//(例如音频属性尚未加载时读取 durationinms)。
		if (!TagValues.TryGetValue(fieldName, out object value) || value == null)
		{
			return "";
		}
		switch (fieldName)
		{
			case "bitrate":
				return string.Concat(value, "kbps");
			case "samplerate":
				return string.Concat(value, "Hz");
			case "haspicture":
			case "hasvideotrack":
				return (bool)value ? "√" : "";
			case "durationinms":
				return FormatDurationWithMilliseconds((int)value);
			default:
				return value.ToString();
		}
	}

	public static string FormatDurationWithMilliseconds(long milliseconds)
	{
		long remainingMilliseconds = milliseconds % 1000L;
		long totalSeconds = milliseconds / 1000L;
		long seconds = totalSeconds % 60L;
		long minutes = totalSeconds / 60L;
		return $"{minutes:00}:{seconds:00}.{remainingMilliseconds:000}";
	}

	public static string FormatDurationHms(long milliseconds)
	{
		long totalSeconds = milliseconds / 1000L;
		long seconds = totalSeconds % 60L;
		long totalMinutes = totalSeconds / 60L;
		long minutes = totalMinutes % 60L;
		long hours = totalMinutes / 60L;
		return $"{hours:00}:{minutes:00}:{seconds:00}";
	}

	// --- field mapping helpers (native field vocabulary -> TagLib) ---

	private string ReadFieldText(string field)
	{
		TagLib.Tag tag = tagFile.Tag;
		switch (field)
		{
			case "title":
				return tag.Title ?? "";
			case "artist":
				return JoinMulti(tag.Performers);
			case "album":
				return tag.Album ?? "";
			case "year":
				return (tag.Year == 0) ? "" : tag.Year.ToString();
			case "track":
				return (tag.Track == 0) ? "" : tag.Track.ToString();
			case "disc":
				return (tag.Disc == 0) ? "" : tag.Disc.ToString();
			case "trackstr":
				return tag.Track.ToString() + ((tag.TrackCount > 0) ? ("/" + tag.TrackCount) : "");
			case "discstr":
				return tag.Disc.ToString() + ((tag.DiscCount > 0) ? ("/" + tag.DiscCount) : "");
			case "genre":
				return JoinMulti(tag.Genres);
			case "albumartist":
				return JoinMulti(tag.AlbumArtists);
			case "composer":
				return JoinMulti(tag.Composers);
			case "lyricist":
				return ReadLyricist();
			case "comment":
				return tag.Comment ?? "";
			case "lyrics":
				return tag.Lyrics ?? "";
			default:
				return "";
		}
	}

	private string ReadLyricist()
	{
		if (tagFile.GetTag(TagLib.TagTypes.Id3v2, false) is TagLib.Id3v2.Tag id3v2Tag)
		{
			foreach (TagLib.Id3v2.TextInformationFrame frame in id3v2Tag.GetFrames<TagLib.Id3v2.TextInformationFrame>("TEXT"))
			{
				string joined = JoinMulti(frame.Text);
				if (!string.IsNullOrEmpty(joined))
				{
					return joined;
				}
			}
		}
		if (tagFile.GetTag(TagLib.TagTypes.Xiph, create: false) is TagLib.Ogg.XiphComment xiphComment)
		{
			string[] values = xiphComment.GetField("LYRICIST");
			if (values != null && values.Length > 0)
			{
				return JoinMulti(values);
			}
		}
		if (tagFile.GetTag(TagLib.TagTypes.Ape, create: false) is TagLib.Ape.Tag apeTag)
		{
			TagLib.Ape.Item item = apeTag.GetItem("Lyricist");
			if (item != null)
			{
				return JoinMulti(item.ToStringArray());
			}
		}
		return "";
	}

	// Native f(tagtypes) summary format, e.g. "ID3v2.3,ID3v1" / "FLAC" / "Vorbis Comment".
	private string BuildTagTypesSummary()
	{
		List<string> parts = new List<string>();
		TagLib.TagTypes types = tagFile.TagTypesOnDisk;
		if (tagFile.GetTag(TagLib.TagTypes.Id3v2, create: false) is TagLib.Id3v2.Tag id3v2Tag)
		{
			parts.Add("ID3v2." + id3v2Tag.Version);
		}
		if ((types & TagLib.TagTypes.Id3v1) != 0)
		{
			parts.Add("ID3v1");
		}
		if ((types & TagLib.TagTypes.FlacMetadata) != 0)
		{
			parts.Add("FLAC");
		}
		else if ((types & TagLib.TagTypes.Xiph) != 0)
		{
			parts.Add("Vorbis Comment");
		}
		if ((types & TagLib.TagTypes.Apple) != 0)
		{
			parts.Add("MPEG-4");
		}
		if ((types & TagLib.TagTypes.Ape) != 0)
		{
			parts.Add("APE");
		}
		if ((types & TagLib.TagTypes.Asf) != 0)
		{
			parts.Add("ASF");
		}
		if ((types & TagLib.TagTypes.RiffInfo) != 0)
		{
			parts.Add("RIFF");
		}
		return string.Join(",", parts);
	}

	// Native p(fileext): uppercase, no leading dot (e.g. "MP3")。从实例方法提取 static 纯核(filePath 传参),
	// 便于表征;Path.GetExtension 对 null/无扩展名返回 null/"" -> "",否则去点大写(ToUpperInvariant culture 无关)。
	internal static string BuildFileExtension(string filePath)
	{
		string extension = Path.GetExtension(filePath);
		if (string.IsNullOrEmpty(extension))
		{
			return "";
		}
		return extension.TrimStart('.').ToUpperInvariant();
	}

	private static string JoinMulti(string[] values)
	{
		if (values == null || values.Length == 0)
		{
			return "";
		}
		string separator = Settings.Default.ConnectorsArtists;
		if (string.IsNullOrEmpty(separator))
		{
			separator = "/";
		}
		return string.Join(separator, values);
	}

	// The original native m0 contract received each multi-value field as ONE string
	// (e.g. "甲/乙") and stored it in a SINGLE tag field. Preserve that on write:
	// emit the whole string as one field rather than splitting on ConnectorsArtists,
	// so the on-disk frame structure matches the original (otherwise Xiph would get
	// multiple ARTIST fields — confirmed divergent via native read-back). The read
	// path still re-joins through JoinMulti, so the round-trip is unchanged.
	internal static string[] ToSingleValue(string value)
	{
		return string.IsNullOrEmpty(value) ? new string[0] : new string[1] { value };
	}

	// --- raw text field bytes for DecodeFieldWithEncoding (re-decode wrong CJK encoding) ---

	private void FillRawFieldData(string field, List<byte[]> blocks, ref string tagType, ref string stringType)
	{
		if (tagFile.GetTag(TagLib.TagTypes.Id3v2, create: false) is TagLib.Id3v2.Tag id3v2Tag
			&& FillRawFromId3v2(id3v2Tag, field, blocks, ref tagType, ref stringType))
		{
			return;
		}
		if (tagFile.GetTag(TagLib.TagTypes.Xiph, create: false) is TagLib.Ogg.XiphComment xiphComment)
		{
			FillRawFromXiph(xiphComment, field, blocks, ref tagType, ref stringType);
			return;
		}
		if (tagFile.GetTag(TagLib.TagTypes.Ape, create: false) is TagLib.Ape.Tag apeTag)
		{
			FillRawFromApe(apeTag, field, blocks, ref tagType, ref stringType);
		}
	}

	private static bool FillRawFromId3v2(TagLib.Id3v2.Tag tag, string field, List<byte[]> blocks, ref string tagType, ref string stringType)
	{
		if (field == "comment")
		{
			foreach (TagLib.Id3v2.CommentsFrame frame in tag.GetFrames<TagLib.Id3v2.CommentsFrame>())
			{
				tagType = "ID3v2";
				stringType = StringTypeName(frame.TextEncoding);
				if (!string.IsNullOrEmpty(frame.Text))
				{
					blocks.Add(EncodeByStringType(frame.Text, frame.TextEncoding));
				}
				return true;
			}
			return false;
		}
		if (field == "lyrics")
		{
			foreach (TagLib.Id3v2.UnsynchronisedLyricsFrame frame in tag.GetFrames<TagLib.Id3v2.UnsynchronisedLyricsFrame>())
			{
				tagType = "ID3v2";
				stringType = StringTypeName(frame.TextEncoding);
				if (!string.IsNullOrEmpty(frame.Text))
				{
					blocks.Add(EncodeByStringType(frame.Text, frame.TextEncoding));
				}
				return true;
			}
			return false;
		}
		string frameId = Id3v2FrameId(field);
		if (frameId == null)
		{
			return false;
		}
		bool found = false;
		foreach (TagLib.Id3v2.TextInformationFrame frame in tag.GetFrames<TagLib.Id3v2.TextInformationFrame>(frameId))
		{
			found = true;
			tagType = "ID3v2";
			stringType = StringTypeName(frame.TextEncoding);
			foreach (string value in frame.Text)
			{
				if (!string.IsNullOrEmpty(value))
				{
					blocks.Add(EncodeByStringType(value, frame.TextEncoding));
				}
			}
		}
		return found;
	}

	private static void AppendUtf8Blocks(string[] values, List<byte[]> blocks, string tagTypeName, ref string tagType, ref string stringType)
	{
		tagType = tagTypeName;
		stringType = "UTF8";
		foreach (string value in values)
		{
			if (!string.IsNullOrEmpty(value))
			{
				blocks.Add(Encoding.UTF8.GetBytes(value));
			}
		}
	}

	private static void FillRawFromXiph(TagLib.Ogg.XiphComment tag, string field, List<byte[]> blocks, ref string tagType, ref string stringType)
	{
		string fieldId = XiphFieldId(field);
		if (fieldId == null)
		{
			return;
		}
		string[] values = tag.GetField(fieldId);
		if (values == null || values.Length == 0)
		{
			return;
		}
		AppendUtf8Blocks(values, blocks, "Vorbis Comment", ref tagType, ref stringType);
	}

	private static void FillRawFromApe(TagLib.Ape.Tag tag, string field, List<byte[]> blocks, ref string tagType, ref string stringType)
	{
		string fieldId = ApeFieldId(field);
		if (fieldId == null)
		{
			return;
		}
		TagLib.Ape.Item item = tag.GetItem(fieldId);
		if (item == null)
		{
			return;
		}
		string[] values = item.ToStringArray();
		if (values == null || values.Length == 0)
		{
			return;
		}
		AppendUtf8Blocks(values, blocks, "APE", ref tagType, ref stringType);
	}

	// field -> (Id3v2 frame, Xiph field, APE item) 标识符词汇表。合并原三个并行 switch
	// (Id3v2FrameId/XiphFieldId/ApeFieldId),逐字节等价。不对称:comment/lyrics 的 Id3v2 列为 null
	// ——二者在 Id3v2 走 CommentsFrame/UnsynchronisedLyricsFrame 特殊 frame、由 FillRawFromId3v2 直接
	// 处理、不经此表(等价于原 Id3v2FrameId 对二者落 default 返 null);track/disc/trackstr/discstr 纯读
	// 派生、不做 raw 提取,故不在表中(TryGetValue 落空 -> null,同原 switch default)。
	private static readonly Dictionary<string, (string Id3v2, string Xiph, string Ape)> rawFieldVocabulary =
		new Dictionary<string, (string Id3v2, string Xiph, string Ape)>
		{
			{ "title",       ("TIT2", "TITLE",       "Title") },
			{ "artist",      ("TPE1", "ARTIST",      "Artist") },
			{ "album",       ("TALB", "ALBUM",       "Album") },
			{ "year",        ("TDRC", "DATE",        "Year") },
			{ "genre",       ("TCON", "GENRE",       "Genre") },
			{ "albumartist", ("TPE2", "ALBUMARTIST", "Album Artist") },
			{ "composer",    ("TCOM", "COMPOSER",    "Composer") },
			{ "lyricist",    ("TEXT", "LYRICIST",    "Lyricist") },
			{ "comment",     (null,   "COMMENT",     "Comment") },
			{ "lyrics",      (null,   "LYRICS",      "Lyrics") },
		};

	// field==null 时短路返回 null(不查字典),严格保持原 switch(null)->default->null 语义:
	// Dictionary.TryGetValue(null) 会抛 ArgumentNullException,守卫不可省。
	internal static string Id3v2FrameId(string field)
	{
		return (field != null && rawFieldVocabulary.TryGetValue(field, out (string Id3v2, string Xiph, string Ape) ids)) ? ids.Id3v2 : null;
	}

	internal static string XiphFieldId(string field)
	{
		return (field != null && rawFieldVocabulary.TryGetValue(field, out (string Id3v2, string Xiph, string Ape) ids)) ? ids.Xiph : null;
	}

	internal static string ApeFieldId(string field)
	{
		return (field != null && rawFieldVocabulary.TryGetValue(field, out (string Id3v2, string Xiph, string Ape) ids)) ? ids.Ape : null;
	}

	internal static string StringTypeName(TagLib.StringType stringType)
	{
		switch (stringType)
		{
			case TagLib.StringType.Latin1:
				return "Latin1";
			case TagLib.StringType.UTF16:
				return "UTF16";
			case TagLib.StringType.UTF16BE:
				return "UTF16BE";
			case TagLib.StringType.UTF16LE:
				return "UTF16LE";
			default:
				return "UTF8";
		}
	}

	internal static byte[] EncodeByStringType(string value, TagLib.StringType stringType)
	{
		if (value == null)
		{
			value = "";
		}
		switch (stringType)
		{
			case TagLib.StringType.Latin1:
				return Latin1Encoding.GetBytes(value);
			case TagLib.StringType.UTF16BE:
				return Encoding.BigEndianUnicode.GetBytes(value);
			case TagLib.StringType.UTF8:
				return Encoding.UTF8.GetBytes(value);
			default:
				return Encoding.Unicode.GetBytes(value);
		}
	}

	// --- picture type name <-> code (native 20-entry list; index = ID3v2 APIC type code) ---

	internal static string PictureTypeToName(TagLib.PictureType pictureType)
	{
		int code = (int)pictureType;
		if (code >= 0 && code < pictureTypeNames.Count)
		{
			return pictureTypeNames[code];
		}
		return pictureTypeNames[0];
	}

	internal static TagLib.PictureType NameToPictureType(string name)
	{
		if (name != null)
		{
			int index = pictureTypeNames.IndexOf(name);
			if (index >= 0)
			{
				return (TagLib.PictureType)index;
			}
		}
		return TagLib.PictureType.Other;
	}

	private TagLib.IPicture GetFirstValidPicture()
	{
		TagLib.IPicture[] pictures = tagFile.Tag.Pictures;
		if (pictures == null)
		{
			return null;
		}
		foreach (TagLib.IPicture picture in pictures)
		{
			if (picture != null && picture.Type != TagLib.PictureType.NotAPicture)
			{
				return picture;
			}
		}
		return null;
	}

	// --- write helpers ---

	private void WriteLyricist(string value)
	{
		if (tagFile.GetTag(TagLib.TagTypes.Id3v2, create: false) is TagLib.Id3v2.Tag id3v2Tag)
		{
			id3v2Tag.RemoveFrames("TEXT");
			if (!string.IsNullOrEmpty(value))
			{
				TagLib.Id3v2.TextInformationFrame frame = new TagLib.Id3v2.TextInformationFrame("TEXT")
				{
					Text = ToSingleValue(value)
				};
				id3v2Tag.AddFrame(frame);
			}
		}
		if (tagFile.GetTag(TagLib.TagTypes.Xiph, create: false) is TagLib.Ogg.XiphComment xiphComment)
		{
			if (string.IsNullOrEmpty(value))
			{
				xiphComment.RemoveField("LYRICIST");
			}
			else
			{
				xiphComment.SetField("LYRICIST", ToSingleValue(value));
			}
		}
		if (tagFile.GetTag(TagLib.TagTypes.Ape, create: false) is TagLib.Ape.Tag apeTag)
		{
			if (string.IsNullOrEmpty(value))
			{
				apeTag.RemoveItem("Lyricist");
			}
			else
			{
				apeTag.SetValue("Lyricist", ToSingleValue(value));
			}
		}
	}

	private static void SetYear(TagLib.Tag tag, string yearValue)
	{
		uint.TryParse(yearValue, out uint year);
		tag.Year = year;
	}

	private static void SetTrack(TagLib.Tag tag, string trackValue)
	{
		ParseNumberAndCount(trackValue, out uint number, out uint count);
		tag.Track = number;
		tag.TrackCount = count;
	}

	private static void SetDisc(TagLib.Tag tag, string discValue)
	{
		ParseNumberAndCount(discValue, out uint number, out uint count);
		tag.Disc = number;
		tag.DiscCount = count;
	}

	internal static void ParseNumberAndCount(string value, out uint number, out uint count)
	{
		number = 0u;
		count = 0u;
		if (string.IsNullOrEmpty(value))
		{
			return;
		}
		string[] parts = value.Split('/');
		uint.TryParse(parts[0].Trim(), out number);
		if (parts.Length > 1)
		{
			uint.TryParse(parts[1].Trim(), out count);
		}
	}

	private static void SetId3v2Version()
	{
		int version = Settings.Default.ID3v2Version;
		if (version == 3 || version == 4)
		{
			TagLib.Id3v2.Tag.DefaultVersion = (byte)version;
			TagLib.Id3v2.Tag.ForceDefaultVersion = true;
		}
	}

	static ConfigDescriptorState()
	{
		supportedPictureMimeTypes = new string[3] { "image/jpeg", "image/png", "image/gif" };
		// Verbatim native ggg picture-type list (20 entries, index = ID3v2 APIC type
		// code). British spelling "Coloured Fish"; native has no "Publisher Logo" (code 20).
		pictureTypeNames = new List<string>
		{
			"Other", "File Icon", "Other File Icon", "Front Cover", "Back Cover",
			"Leaflet Page", "Media", "Lead Artist", "Artist", "Conductor",
			"Band", "Composer", "Lyricist", "Recording Location", "During Recording",
			"During Performance", "Movie Screen Capture", "Coloured Fish", "Illustration", "Band Logo"
		};
	}
}
