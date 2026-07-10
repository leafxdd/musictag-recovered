using System;
using System.Collections.Generic;
using MusicTag.Services;
using MusicTagWinApp.Instances;

namespace MusicTag.Tests;

// services/utilities 纯映射与解码 characterization(均 already-testable public static,零产品改动):
//   TextEncodingService.DecodeBasicHtmlEntities:HttpUtility.HtmlDecode + 手动补 &apos;(HTML4 实体表不含,
//     属 XML/HTML5);null 归一为 ""。
//   ImageUtilities.GetImageExtensionForMimeType / GetImageFileDialogFilterForMimeType:4 项 MIME 映射
//     (jpeg/png/bmp/gif),未知 MIME 分别回退 fallbackExtension / ""。
internal static class UtilityMappingCharacterization
{
	public static IEnumerable<(string, Action)> All()
	{
		// ===== GetResourceScaleSuffix:工具栏资源按当前屏 DPI 在 150% 切换 2X =====

		yield return ("GetResourceScaleSuffix: below 150 percent -> base resource", delegate
		{
			Check.Equal("", ImageUtilities.GetResourceScaleSuffix(1.49f), "below threshold");
		});

		yield return ("GetResourceScaleSuffix: 150 percent and above -> 2X resource", delegate
		{
			Check.Equal("2X", ImageUtilities.GetResourceScaleSuffix(1.5f), "at threshold");
			Check.Equal("2X", ImageUtilities.GetResourceScaleSuffix(2f), "above threshold");
		});

		// ===== DecodeBasicHtmlEntities =====

		yield return ("DecodeBasicHtmlEntities: &amp; -> &", delegate
		{
			Check.Equal("&", TextEncodingService.DecodeBasicHtmlEntities("&amp;"), "amp");
		});

		yield return ("DecodeBasicHtmlEntities: &apos; -> ' (manual pre-replace)", delegate
		{
			Check.Equal("'", TextEncodingService.DecodeBasicHtmlEntities("&apos;"), "apos");
		});

		yield return ("DecodeBasicHtmlEntities: &lt;a&gt; -> <a>", delegate
		{
			Check.Equal("<a>", TextEncodingService.DecodeBasicHtmlEntities("&lt;a&gt;"), "lt gt");
		});

		yield return ("DecodeBasicHtmlEntities: &quot; -> double quote", delegate
		{
			Check.Equal("\"", TextEncodingService.DecodeBasicHtmlEntities("&quot;"), "quot");
		});

		yield return ("DecodeBasicHtmlEntities: null -> \"\"", delegate
		{
			Check.Equal("", TextEncodingService.DecodeBasicHtmlEntities(null), "null coalesced");
		});

		yield return ("DecodeBasicHtmlEntities: plain text unchanged", delegate
		{
			Check.Equal("no entities here", TextEncodingService.DecodeBasicHtmlEntities("no entities here"), "plain");
		});

		yield return ("DecodeBasicHtmlEntities: mixed &amp; and &apos;", delegate
		{
			Check.Equal("A & B's", TextEncodingService.DecodeBasicHtmlEntities("A &amp; B&apos;s"), "mixed");
		});

		// ===== GetImageExtensionForMimeType =====

		yield return ("GetImageExtensionForMimeType: image/jpeg -> .jpg", delegate
		{
			Check.Equal(".jpg", ImageUtilities.GetImageExtensionForMimeType("image/jpeg", ".fallback"), "jpeg");
		});

		yield return ("GetImageExtensionForMimeType: image/png -> .png", delegate
		{
			Check.Equal(".png", ImageUtilities.GetImageExtensionForMimeType("image/png", ".fallback"), "png");
		});

		yield return ("GetImageExtensionForMimeType: image/bmp -> .bmp", delegate
		{
			Check.Equal(".bmp", ImageUtilities.GetImageExtensionForMimeType("image/bmp", ".fallback"), "bmp");
		});

		yield return ("GetImageExtensionForMimeType: image/gif -> .gif", delegate
		{
			Check.Equal(".gif", ImageUtilities.GetImageExtensionForMimeType("image/gif", ".fallback"), "gif");
		});

		yield return ("GetImageExtensionForMimeType: unknown -> fallback (mapping miss)", delegate
		{
			Check.Equal(".fallback", ImageUtilities.GetImageExtensionForMimeType("image/webp", ".fallback"), "unknown");
		});

		// ===== GetImageFileDialogFilterForMimeType =====

		yield return ("GetImageFileDialogFilterForMimeType: image/jpeg -> jpg|*.jpg", delegate
		{
			Check.Equal("jpg|*.jpg", ImageUtilities.GetImageFileDialogFilterForMimeType("image/jpeg"), "jpeg filter");
		});

		yield return ("GetImageFileDialogFilterForMimeType: image/png -> png|*.png", delegate
		{
			Check.Equal("png|*.png", ImageUtilities.GetImageFileDialogFilterForMimeType("image/png"), "png filter");
		});

		yield return ("GetImageFileDialogFilterForMimeType: image/gif -> gif|*.gif", delegate
		{
			Check.Equal("gif|*.gif", ImageUtilities.GetImageFileDialogFilterForMimeType("image/gif"), "gif filter");
		});

		yield return ("GetImageFileDialogFilterForMimeType: unknown -> empty string", delegate
		{
			Check.Equal("", ImageUtilities.GetImageFileDialogFilterForMimeType("image/webp"), "unknown filter");
		});

		// ===== JavaScriptStringEncode(HttpUtility 委托,转义由 Unicode 码点驱动,locale 无关)=====

		yield return ("JavaScriptStringEncode: plain text identity", delegate
		{
			Check.Equal("abc", TextEncodingService.JavaScriptStringEncode("abc"), "plain unchanged");
		});

		yield return ("JavaScriptStringEncode: double-quote -> backslash-escaped", delegate
		{
			Check.Equal("a\\\"b", TextEncodingService.JavaScriptStringEncode("a\"b"), "double quote escaped");
		});

		yield return ("JavaScriptStringEncode: angle brackets -> unicode escape", delegate
		{
			Check.Equal("\\u003cx\\u003e", TextEncodingService.JavaScriptStringEncode("<x>"), "< > unicode-escaped (XSS-safe)");
		});

		yield return ("JavaScriptStringEncode: newline -> backslash-n", delegate
		{
			Check.Equal("a\\nb", TextEncodingService.JavaScriptStringEncode("a\nb"), "newline escaped");
		});
	}
}
