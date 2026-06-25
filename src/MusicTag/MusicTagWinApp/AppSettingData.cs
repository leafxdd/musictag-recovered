using System;
using System.Reflection;
using MusicTagWinApp.Instances;

namespace MusicTagWinApp;

	[Serializable]
	internal class AppSettingData
	{
		public static readonly string AppSettingDataPath = DatabaseMapper.GetApplicationDirectory() + Assembly.GetExecutingAssembly().GetName().Name + ".dat";

		public ListViewFileSetting ListViewFileSetting { get; set; }
	}
