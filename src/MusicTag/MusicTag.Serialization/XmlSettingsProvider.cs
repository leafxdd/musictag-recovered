using System;
using System.Collections;
using System.Collections.Specialized;
using System.Configuration;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using System.Xml;

namespace MusicTag.Serialization;

internal class XmlSettingsProvider : SettingsProvider
{
	private const string SettingsRootElementName = "Settings";

	private XmlDocument settingsDocument;

	public override string ApplicationName
	{
		get
		{
			return Assembly.GetExecutingAssembly().GetName().Name;
		}
		set
		{
		}
	}

	private string SettingsDirectory
	{
		get
		{
			return Path.GetDirectoryName(Application.ExecutablePath);
		}
	}

	private string SettingsFileName
	{
		get
		{
			return ApplicationName + ".config";
		}
	}

	private string SettingsFilePath
	{
		get
		{
			return Path.Combine(SettingsDirectory, SettingsFileName);
		}
	}

	public override void Initialize(string name, NameValueCollection config)
	{
		base.Initialize(ApplicationName, config);
	}

	public override void SetPropertyValues(SettingsContext context, SettingsPropertyValueCollection values)
	{
		foreach (SettingsPropertyValue propertyValue in values)
		{
			SaveSettingValue(propertyValue);
		}
		LoadSettingsDocument().Save(SettingsFilePath);
	}

	public override SettingsPropertyValueCollection GetPropertyValues(SettingsContext context, SettingsPropertyCollection properties)
	{
		SettingsPropertyValueCollection propertyValues = new SettingsPropertyValueCollection();
		foreach (SettingsProperty property in properties)
		{
			SettingsPropertyValue propertyValue = new SettingsPropertyValue(property)
			{
				IsDirty = false,
				SerializedValue = ReadSettingValue(property)
			};
			propertyValues.Add(propertyValue);
		}
		return propertyValues;
	}

	private XmlDocument LoadSettingsDocument()
	{
		if (settingsDocument != null)
		{
			return settingsDocument;
		}
		settingsDocument = new XmlDocument();
		try
		{
			settingsDocument.Load(SettingsFilePath);
			if (settingsDocument.SelectSingleNode(SettingsRootElementName) == null)
			{
				CreateEmptySettingsDocument();
			}
		}
		catch (Exception)
		{
			CreateEmptySettingsDocument();
		}
		return settingsDocument;
	}

	private void CreateEmptySettingsDocument()
	{
		settingsDocument.RemoveAll();
		XmlDeclaration declaration = settingsDocument.CreateXmlDeclaration("1.0", "utf-8", string.Empty);
		settingsDocument.AppendChild(declaration);
		XmlNode root = settingsDocument.CreateNode(XmlNodeType.Element, SettingsRootElementName, "");
		settingsDocument.AppendChild(root);
	}

	private string ReadSettingValue(SettingsProperty property)
	{
		try
		{
			return LoadSettingsDocument().SelectSingleNode(GetSettingXPath(property)).InnerText;
		}
		catch (Exception)
		{
			return property.DefaultValue == null ? "" : property.DefaultValue.ToString();
		}
	}

	private void SaveSettingValue(SettingsPropertyValue propertyValue)
	{
		XmlDocument document = LoadSettingsDocument();
		XmlElement settingElement = document.SelectSingleNode(GetSettingXPath(propertyValue.Property)) as XmlElement;
		if (settingElement == null)
		{
			settingElement = document.CreateElement(propertyValue.Name);
			if (IsMachineIndependentSetting(propertyValue.Property))
			{
				document.SelectSingleNode(SettingsRootElementName).AppendChild(settingElement);
			}
			else
			{
				GetOrCreateMachineElement(document).AppendChild(settingElement);
			}
		}
		settingElement.InnerText = propertyValue.SerializedValue == null ? "" : propertyValue.SerializedValue.ToString();
	}

	private string GetSettingXPath(SettingsProperty property)
	{
		if (IsMachineIndependentSetting(property))
		{
			return SettingsRootElementName + "/" + property.Name;
		}
		return SettingsRootElementName + "/" + Environment.MachineName + "/" + property.Name;
	}

	private XmlElement GetOrCreateMachineElement(XmlDocument document)
	{
		XmlElement machineElement = document.SelectSingleNode(SettingsRootElementName + "/" + Environment.MachineName) as XmlElement;
		if (machineElement != null)
		{
			return machineElement;
		}
		machineElement = document.CreateElement(Environment.MachineName);
		document.SelectSingleNode(SettingsRootElementName).AppendChild(machineElement);
		return machineElement;
	}

	private static bool IsMachineIndependentSetting(SettingsProperty property)
	{
		foreach (DictionaryEntry attribute in property.Attributes)
		{
			if (attribute.Value is SettingsManageabilityAttribute)
			{
				return true;
			}
		}
		return false;
	}
}
