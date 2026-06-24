using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using MusicTag.Serialization;
using MusicTagWinApp.Instances;
using MusicTagWinApp.Properties;

namespace MusicTag.States;

internal class ConfigDescriptorState : IDisposable
{
	private struct NativePictureEntry
	{
		public IntPtr DataPointer;

		public int DataLength;

		public IntPtr PictureTypePointer;
	}

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

	private IntPtr nativeTagHandle;

	private static readonly string[] openErrorTemplates;

	private static readonly string[] supportedPictureMimeTypes;

	private readonly Dictionary<string, object> tagValues;

	private static readonly List<string> pictureTypeNames;

	private string filePath;

	private readonly string loadError;

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

	private static string[] GetOpenErrorTemplates()
	{
		return openErrorTemplates;
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
			nativeTagHandle = OpenTagFile(nativeTagHandle, filePath, verifyFileOnly: false, out var openResultPointer);
			string openResult = ReadAndFreeNativeString(openResultPointer);
			if (!openResult.StartsWith("false"))
			{
				return;
			}
			string[] resultParts = openResult.Split(',');
			int errorIndex = -1;
			if (resultParts.Length > 1)
			{
				errorIndex = int.Parse(resultParts[1]);
			}
			string errorArgument = null;
			if (resultParts.Length > 2)
			{
				errorArgument = resultParts[2];
			}
			if (errorIndex >= 0 && errorIndex < GetOpenErrorTemplates().Length)
			{
				if (errorArgument != null)
				{
					loadError = string.Format(GetOpenErrorTemplates()[errorIndex], errorArgument);
				}
				else
				{
					loadError = GetOpenErrorTemplates()[errorIndex];
				}
			}
			else if (!NativeFileExists(nativeTagHandle, filePath))
			{
				loadError = Resources.Msg_FileNotFound;
			}
			else
			{
				loadError = Resources.Msg_InvalidFile;
			}
		}
		catch (Exception)
		{
			loadError = Resources.Msg_OpenFileFail + "(-1)";
		}
	}

	public void Dispose()
	{
		if (nativeTagHandle == IntPtr.Zero)
		{
			return;
		}

		CloseTagFile(nativeTagHandle);
		nativeTagHandle = IntPtr.Zero;
	}

	public void LoadBasicTagFields()
	{
		if (!TagValues.ContainsKey("tagtypes"))
		{
			TagValues.Add("tagtypes", ReadAndFreeNativeString(ReadTagTypeSummary(nativeTagHandle, "", out var _, out var _)));
		}
		if (!TagValues.ContainsKey("fileext"))
		{
			TagValues.Add("fileext", ReadAndFreeNativeString(GetFileExtension(nativeTagHandle)));
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
			IntPtr tagType;
			IntPtr stringType;
			string tagValue = ReadAndFreeNativeString(ReadTagField(nativeTagHandle, tagField, out tagType, out stringType));
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
			IntPtr rawBlockListPointer = IntPtr.Zero;
			IntPtr tagTypePointer = IntPtr.Zero;
			IntPtr stringTypePointer = IntPtr.Zero;
			int blockCount = ReadRawTextFieldData(nativeTagHandle, textField, out tagTypePointer, out stringTypePointer, out rawBlockListPointer);
			if (rawBlockListPointer != IntPtr.Zero && blockCount > 0)
			{
				for (int blockIndex = 0; blockIndex < blockCount; blockIndex++)
				{
					IntPtr blockPointerAddress = IntPtr.Add(rawBlockListPointer, Marshal.SizeOf(typeof(IntPtr)) * blockIndex);
					IntPtr blockPointer = (IntPtr)Marshal.PtrToStructure(blockPointerAddress, typeof(IntPtr));
					if (blockPointer == IntPtr.Zero)
					{
						continue;
					}
					int blockLength = (int)Marshal.PtrToStructure(blockPointer, typeof(int));
					IntPtr blockDataPointer = IntPtr.Add(blockPointer, Marshal.SizeOf(typeof(int)));
					byte[] dataBlock = new byte[blockLength];
					Marshal.Copy(blockDataPointer, dataBlock, 0, blockLength);
					dataBlocks.Add(dataBlock);
				}
			}
			FreeRawTextFieldData(rawBlockListPointer, blockCount);
			TagValues.Add(dataKey, dataBlocks);
			TagValues.Add(tagTypeKey, ReadAndFreeNativeString(tagTypePointer));
			TagValues.Add(stringTypeKey, ReadAndFreeNativeString(stringTypePointer));
		}
	}

	public void LoadLyrics()
	{
		if (!TagValues.ContainsKey("lyrics"))
		{
			TagValues.Add("lyrics", ReadAndFreeNativeString(ReadTagField(nativeTagHandle, "lyrics", out var _, out var _)));
		}
	}

	public void LoadAudioProperties()
	{
		if (!TagValues.ContainsKey("bitpersample"))
		{
			TagValues.Add("bitpersample", GetBitsPerSample(nativeTagHandle));
		}
		if (!TagValues.ContainsKey("channels"))
		{
			TagValues.Add("channels", GetChannelCount(nativeTagHandle));
		}
		if (!TagValues.ContainsKey("samplerate"))
		{
			TagValues.Add("samplerate", GetSampleRate(nativeTagHandle));
		}
		if (!TagValues.ContainsKey("bitrate"))
		{
			TagValues.Add("bitrate", GetBitrate(nativeTagHandle));
		}
		if (!TagValues.ContainsKey("durationinms"))
		{
			TagValues.Add("durationinms", GetDurationMilliseconds(nativeTagHandle));
		}
		if (TagValues.ContainsKey("hasvideotrack"))
		{
			return;
		}
		TagValues.Add("hasvideotrack", HasVideoTrack(nativeTagHandle));
	}

	public void LoadPictureSummary(bool flagOnly)
	{
		if (flagOnly)
		{
			if (!TagValues.ContainsKey("haspicture"))
			{
				IntPtr picturePointer = IntPtr.Zero;
				int pictureLength = ReadPrimaryPicture(nativeTagHandle, out picturePointer);
				TagValues.Add("haspicture", pictureLength > 0 && picturePointer != IntPtr.Zero);
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
			IntPtr pictureDataPointer = IntPtr.Zero;
			int pictureDataLength = ReadPrimaryPicture(nativeTagHandle, out pictureDataPointer);
			if (pictureDataLength > 0 && pictureDataPointer != IntPtr.Zero)
			{
				byte[] pictureData = new byte[pictureDataLength];
				Marshal.Copy(pictureDataPointer, pictureData, 0, pictureDataLength);
				TagValues.Add("picturedata", pictureData);
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
				IntPtr nativePictureListPointer = IntPtr.Zero;
				int pictureCount = ReadAllPictures(nativeTagHandle, out nativePictureListPointer);
				if (nativePictureListPointer != IntPtr.Zero)
				{
					for (int pictureIndex = 0; pictureIndex < pictureCount; pictureIndex++)
					{
						NativePictureEntry nativePicture = (NativePictureEntry)Marshal.PtrToStructure(IntPtr.Add(nativePictureListPointer, Marshal.SizeOf(typeof(NativePictureEntry)) * pictureIndex), typeof(NativePictureEntry));
						if (nativePicture.DataLength > 0 && nativePicture.DataPointer != IntPtr.Zero)
						{
							PictureData picture = new PictureData
							{
								ImageBytes = new byte[nativePicture.DataLength]
							};
							Marshal.Copy(nativePicture.DataPointer, picture.ImageBytes, 0, nativePicture.DataLength);
							picture.PictureType = Marshal.PtrToStringUni(nativePicture.PictureTypePointer);
							pictures.Add(picture);
						}
					}
					FreePictureList(nativePictureListPointer, pictureCount);
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

	private static List<string> LoadPictureTypeNames()
	{
		List<string> pictureTypes = new List<string>();
		IntPtr nativePictureTypeListPointer = IntPtr.Zero;
		int pictureTypeCount = ReadPictureTypeNames(out nativePictureTypeListPointer);
		if (nativePictureTypeListPointer != IntPtr.Zero)
		{
			for (int pictureTypeIndex = 0; pictureTypeIndex < pictureTypeCount; pictureTypeIndex++)
			{
				IntPtr pointerAddress = IntPtr.Add(nativePictureTypeListPointer, Marshal.SizeOf(typeof(IntPtr)) * pictureTypeIndex);
				string pictureType = Marshal.PtrToStringUni((IntPtr)Marshal.PtrToStructure(pointerAddress, typeof(IntPtr)));
				pictureTypes.Add(pictureType);
			}
		}
		return pictureTypes;
	}

	public static Image LoadPictureImage(PictureData pictureData)
	{
		try
		{
			using MemoryStream memoryStream = new MemoryStream(pictureData.ImageBytes);
			Image image = Image.FromStream(memoryStream);
			if (image != null)
			{
				ImageCodecInfo imageCodecInfo = DatabaseMapper.GetImageDecoderByFormatId(image.RawFormat.Guid);
				if (imageCodecInfo != null && imageCodecInfo.MimeType != null)
				{
					pictureData.MimeType = imageCodecInfo.MimeType;
				}
				else
				{
					pictureData.MimeType = "";
				}
			}
			pictureData.Width = image.Width;
			pictureData.Height = image.Height;
			return image;
		}
		catch (Exception ex)
		{
			Console.WriteLine("LoadPictureImage error:{0}", ex.Message);
		}
		return null;
	}

	public static string GetGenreNameByIndex(int genreIndex)
	{
		return ReadAndFreeNativeString(ReadGenreName(genreIndex));
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

	public bool SaveTagFields()
	{
		string[] tagValues = new string[12]
		{
			TagValues["title"] as string,
			TagValues["artist"] as string,
			TagValues["album"] as string,
			TagValues["year"] as string,
			TagValues["trackstr"] as string,
			TagValues["discstr"] as string,
			TagValues["genre"] as string,
			TagValues["albumartist"] as string,
			TagValues["composer"] as string,
			TagValues["comment"] as string,
			TagValues["lyricist"] as string,
			TagValues["lyrics"] as string
		};
		WriteTagFields(nativeTagHandle, tagValues, tagValues.Length);
		if (TagValues.TryGetValue("allpicturedata", out var value))
		{
			List<PictureData> pictures = value as List<PictureData>;
			ClearPictures(nativeTagHandle);
			foreach (PictureData picture in pictures)
			{
				if (picture.MimeType == null || picture.Width == 0 || picture.Height == 0)
				{
					using (LoadPictureImage(picture))
					{
					}
				}
				AddPicture(nativeTagHandle, picture.ImageBytes, picture.ImageBytes.Length, picture.PictureType, picture.MimeType, picture.Width, picture.Height);
			}
		}
		return SaveTagFieldsNative(nativeTagHandle, Settings.Default.ID3v2Version);
	}

	public bool SaveCurrentTagFile()
	{
		return SaveTagFileNative(nativeTagHandle, Settings.Default.ID3v2Version);
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
		object value = this[fieldName];
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

	public static string ReadAndFreeNativeString(IntPtr nativeStringPointer)
	{
		try
		{
			return Marshal.PtrToStringUni(nativeStringPointer);
		}
		finally
		{
			FreeNativeString(nativeStringPointer);
		}
	}

	[DllImport("MusicTag.dll", EntryPoint = "bb")]
	private static extern void FreeNativeString(IntPtr nativeStringPointer);

	[DllImport("MusicTag.dll", EntryPoint = "zzz")]
	private static extern void FreePictureList(IntPtr pictureListPointer, int pictureCount);

	[DllImport("MusicTag.dll", EntryPoint = "zz1")]
	private static extern void FreeRawTextFieldData(IntPtr rawFieldDataPointer, int fieldValueCount);

	[DllImport("MusicTag.dll", EntryPoint = "dd")]
	private static extern void CloseTagFile(IntPtr tagHandle);

	[DllImport("MusicTag.dll", EntryPoint = "cc")]
	private static extern IntPtr ReadGenreName(int genreId);

	[DllImport("MusicTag.dll", CharSet = CharSet.Unicode, EntryPoint = "d")]
	private static extern int ReadRawTextFieldData(IntPtr tagHandle, string fieldName, out IntPtr tagTypePointer, out IntPtr stringTypePointer, out IntPtr rawBlockListPointer);

	[DllImport("MusicTag.dll", CharSet = CharSet.Unicode, EntryPoint = "ee")]
	private static extern IntPtr ReadTagField(IntPtr tagHandle, string fieldName, out IntPtr tagTypePointer, out IntPtr stringTypePointer);

	[DllImport("MusicTag.dll", EntryPoint = "f")]
	private static extern IntPtr ReadTagTypeSummary(IntPtr tagHandle, string fieldName, out IntPtr tagTypePointer, out IntPtr stringTypePointer);

	[DllImport("MusicTag.dll", EntryPoint = "g")]
	private static extern int ReadPrimaryPicture(IntPtr tagHandle, out IntPtr picturePointer);

	[DllImport("MusicTag.dll", EntryPoint = "gg")]
	private static extern int ReadAllPictures(IntPtr tagHandle, out IntPtr pictureListPointer);

	[DllImport("MusicTag.dll", CharSet = CharSet.Unicode, EntryPoint = "e")]
	private static extern IntPtr OpenTagFile(IntPtr existingTagHandle, string filePath, bool verifyFileOnly, out IntPtr openResultPointer);

	[DllImport("MusicTag.dll", EntryPoint = "ggg")]
	private static extern int ReadPictureTypeNames(out IntPtr pictureTypeListPointer);

	[DllImport("MusicTag.dll", EntryPoint = "p")]
	private static extern IntPtr GetFileExtension(IntPtr tagHandle);

	[DllImport("MusicTag.dll", EntryPoint = "h")]
	private static extern int GetBitsPerSample(IntPtr tagHandle);

	[DllImport("MusicTag.dll", EntryPoint = "i")]
	private static extern int GetChannelCount(IntPtr tagHandle);

	[DllImport("MusicTag.dll", EntryPoint = "j")]
	private static extern int GetSampleRate(IntPtr tagHandle);

	[DllImport("MusicTag.dll", EntryPoint = "k")]
	private static extern int GetBitrate(IntPtr tagHandle);

	[DllImport("MusicTag.dll", EntryPoint = "l")]
	private static extern int GetDurationMilliseconds(IntPtr tagHandle);

	[DllImport("MusicTag.dll", EntryPoint = "o")]
	[return: MarshalAs(UnmanagedType.I1)]
	private static extern bool HasVideoTrack(IntPtr tagHandle);

	[DllImport("MusicTag.dll", CharSet = CharSet.Unicode, EntryPoint = "m0")]
	private static extern void WriteTagFields(IntPtr tagHandle, string[] fieldValues, int fieldValueCount);

	[DllImport("MusicTag.dll", CharSet = CharSet.Unicode, EntryPoint = "m1")]
	private static extern void ClearPictures(IntPtr tagHandle);

	[DllImport("MusicTag.dll", CharSet = CharSet.Unicode, EntryPoint = "m2")]
	private static extern void AddPicture(IntPtr tagHandle, byte[] pictureBytes, int pictureByteCount, string pictureType, string mimeType, int width, int height);

	[DllImport("MusicTag.dll", CharSet = CharSet.Unicode, EntryPoint = "m3")]
	[return: MarshalAs(UnmanagedType.I1)]
	private static extern bool SaveTagFieldsNative(IntPtr tagHandle, int id3v2Version);

	[DllImport("MusicTag.dll", EntryPoint = "q")]
	private static extern bool SaveTagFileNative(IntPtr tagHandle, int id3v2Version);

	[DllImport("MusicTag.dll", CharSet = CharSet.Unicode)]
	[return: MarshalAs(UnmanagedType.I1)]
	private static extern bool NativeFileExists(IntPtr tagHandle, string filePath);

	static ConfigDescriptorState()
	{
		openErrorTemplates = new string[2]
		{
			Resources.Msg_InvalidFileWithPossiableExt,
			Resources.Msg_InvalidFileWithNoSupportMultiTrackAudioFile
		};
		supportedPictureMimeTypes = new string[3] { "image/jpeg", "image/png", "image/gif" };
		pictureTypeNames = LoadPictureTypeNames();
	}
}

