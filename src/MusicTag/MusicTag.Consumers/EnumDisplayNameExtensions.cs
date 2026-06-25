using System;
using System.ComponentModel;
using System.Reflection;
using MusicTagWinApp.Properties;

namespace MusicTag.Consumers;

internal static class EnumDisplayNameExtensions
{
	public static string GetDisplayName(this Enum enumValue)
	{
		string enumName = enumValue.ToString();
		FieldInfo field = enumValue.GetType().GetField(enumName);
		string resourceText = Resources.ResourceManager.GetString("Enum_" + enumName);
		if (string.IsNullOrWhiteSpace(resourceText))
		{
			object[] customAttributes = field?.GetCustomAttributes(typeof(DescriptionAttribute), inherit: true) ?? Array.Empty<object>();
			if (customAttributes.Length != 0 && customAttributes[0] is DescriptionAttribute descriptionAttribute)
			{
				return descriptionAttribute.Description;
			}
			return enumName;
		}
		return resourceText;
	}
}

