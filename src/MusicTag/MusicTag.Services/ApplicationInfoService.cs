using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using MusicTagWinApp.Instances;
using MusicTagWinApp.Properties;

namespace MusicTag.Services;

internal static class ApplicationInfoService
{
	private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

	public static readonly string UpdatePageUrl = "https://www.cnblogs.com/vinlxc/p/11347744.html";

	private static void ShowAlreadyLatestVersionMessage()
	{
		DatabaseMapper.ShowInformationMessage(Resources.Msg_UsingLastestVersion);
	}

	private static void ShowVersionCheckFailedMessage()
	{
		DatabaseMapper.ShowErrorMessage(Resources.Msg_FailedToGetNewVersion);
	}

	private static void ShowUpdateCheckRequestFailedMessage(Exception exception)
	{
		DatabaseMapper.ShowErrorMessage(string.Format(Resources.Msg_CannotConnectToUpdateSite, UpdatePageUrl) + "(" + exception.GetMessageChain() + ")");
	}

	private static void ShowNewVersionPrompt(string latestVersion)
	{
		switch (DatabaseMapper.ConfirmYesNoCancel(string.Format(Resources.Msg_FoundNewVersion, latestVersion, UpdatePageUrl)))
		{
		case DialogResult.No:
			Settings.Default.IgnoreCheckSpecAppVersion = latestVersion;
			break;
		case DialogResult.Yes:
			Process.Start(UpdatePageUrl);
			Settings.Default.IgnoreCheckSpecAppVersion = latestVersion;
			break;
		}
	}

	private static int[] ParseVersionParts(string version)
	{
		string[] parts = version.Split('.');
		if (parts.Length != 4)
		{
			return null;
		}
		int[] versionParts = new int[4];
		for (int index = 0; index < versionParts.Length; index++)
		{
			versionParts[index] = int.Parse(parts[index]);
		}
		return versionParts;
	}

	private static int CompareVersionParts(int[] currentVersionParts, int[] latestVersionParts)
	{
		for (int index = 0; index < currentVersionParts.Length; index++)
		{
			if (currentVersionParts[index] < latestVersionParts[index])
			{
				return -1;
			}
			if (currentVersionParts[index] > latestVersionParts[index])
			{
				return 1;
			}
		}
		return 0;
	}

	public static string GetFileVersion()
	{
		object[] customAttributes = Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyFileVersionAttribute), false);
		if (customAttributes.Length == 0)
		{
			return "";
		}
		return ((AssemblyFileVersionAttribute)customAttributes[0]).Version;
	}

	public static async void CheckForUpdates(bool isStartup, Action callback)
	{
		if (isStartup && !Settings.Default.CheckForUpdatesOnStartup)
		{
			callback();
			return;
		}

		bool handled = false;
		try
		{
			using HttpClient httpClient = new HttpClient
			{
				Timeout = TimeSpan.FromSeconds(60.0)
			};
			httpClient.DefaultRequestHeaders.Add("user-agent", UserAgent);
			string response = await httpClient.GetStringAsync(UpdatePageUrl);
			Match match = Regex.Match(response, "<p>当前版本：(.+)</p>");
			if (match.Success)
			{
				try
				{
					string latestVersion = match.Groups[1].Value.Trim();
					int[] latestVersionParts = ParseVersionParts(latestVersion);
					int[] currentVersionParts = ParseVersionParts(GetFileVersion());
					if (latestVersionParts != null && currentVersionParts != null)
					{
						int comparison = CompareVersionParts(currentVersionParts, latestVersionParts);
						if (comparison < 0)
						{
							if (latestVersion != Settings.Default.IgnoreCheckSpecAppVersion)
							{
								ShowNewVersionPrompt(latestVersion);
							}
						}
						else if (!isStartup)
						{
							ShowAlreadyLatestVersionMessage();
						}
						handled = true;
					}
				}
				catch (Exception exception)
				{
					Console.WriteLine("checknewversion error:" + exception.GetMessageChain());
				}
			}
		}
		catch (Exception exception)
		{
			handled = true;
			ShowUpdateCheckRequestFailedMessage(exception);
		}

		if (!handled)
		{
			ShowVersionCheckFailedMessage();
		}
		callback();
	}
}
