using System.Globalization;

namespace MusicTagWinApp.Properties;

// 后恢复期新增的 UI 文案无法写回随原程序附带的预编译卫星 DLL。
// 这里为应用实际支持的三种界面语言提供源码内 fallback，避免英文/繁中界面混入简中。
internal static class UiText
{
	public static string Get(string english, string simplifiedChinese, string traditionalChinese)
	{
		return Get(english, simplifiedChinese, traditionalChinese, CultureInfo.CurrentUICulture);
	}

	internal static string Get(string english, string simplifiedChinese, string traditionalChinese, CultureInfo culture)
	{
		string cultureName = culture?.Name ?? string.Empty;
		if (cultureName.StartsWith("zh", System.StringComparison.OrdinalIgnoreCase))
		{
			if (cultureName.IndexOf("Hant", System.StringComparison.OrdinalIgnoreCase) >= 0
				|| cultureName.EndsWith("-CHT", System.StringComparison.OrdinalIgnoreCase)
				|| cultureName.EndsWith("-TW", System.StringComparison.OrdinalIgnoreCase)
				|| cultureName.EndsWith("-HK", System.StringComparison.OrdinalIgnoreCase)
				|| cultureName.EndsWith("-MO", System.StringComparison.OrdinalIgnoreCase))
			{
				return traditionalChinese;
			}
			return simplifiedChinese;
		}
		return english;
	}
}
