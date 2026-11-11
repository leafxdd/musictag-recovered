namespace MusicTagWinApp.Common;

// 界面字体:全部窗口统一用微软雅黑 UI。它自带中文字形,中英文用同一个字体绘制。原先的 Tahoma
// 与 Microsoft Sans Serif 都没有中文字形,中文由系统按替补表(Tahoma 的第一替补是宋体)临时换字体,
// 每画一段中文都要切分文字、切换字体:文件列表实测整屏重画约 75ms,同条件换雅黑约 28ms,
// Tahoma 画纯英文约 26ms —— 慢的是替补,不是 Tahoma 本身。字号沿用各处原值。
internal static class UiFonts
{
	public const string FamilyName = "Microsoft YaHei UI";
}
