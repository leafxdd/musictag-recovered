using System.IO;
using System.Reflection;
using MusicTagWinApp.Instances;
using Newtonsoft.Json;

namespace MusicTagWinApp;

	internal class AppSettingData
	{
		public static readonly string AppSettingDataPath = PathFileUtilities.GetApplicationDirectory() + Assembly.GetExecutingAssembly().GetName().Name + ".dat";

		public ListViewFileSetting ListViewFileSetting { get; set; }

		public static AppSettingData Load(string filePath)
		{
			string json = File.ReadAllText(filePath);
			AppSettingData appSettingData = JsonConvert.DeserializeObject<AppSettingData>(json);
			if (appSettingData == null)
			{
				throw new JsonSerializationException("Application setting data is empty.");
			}
			return appSettingData;
		}

		public void Save(string filePath)
		{
			string json = JsonConvert.SerializeObject(this);
			File.WriteAllText(filePath, json);
		}
	}
