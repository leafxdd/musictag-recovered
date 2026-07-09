using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

// characterization 测试项目（src/MusicTag.Tests）经此访问本程序集的 internal 类型（provider / 工具类等）。
// 纯可见性，零运行时行为影响。详见 docs/SIMPLIFICATION_PLAN.md Phase 2 characterization 基础设施。
[assembly: InternalsVisibleTo("MusicTag.Tests")]
// net8 迁移:GenerateAssemblyInfo=False 时 SDK 不注入平台标注,缺了它 CA1416 分析器会把
// 程序集当跨平台代码审(对 WinForms API 刷数千条警告)。本应用是纯 Windows 桌面程序。
[assembly: SupportedOSPlatform("windows7.0")]
[assembly: ComVisible(false)]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCopyright("Copyright ©  2019-2022")]
[assembly: Guid("7a0f3e73-499a-48de-9cea-71491b4814df")]
[assembly: AssemblyFileVersion("1.0.11.0")]
[assembly: AssemblyTitle("Music Tag")]
[assembly: AssemblyCompany("")]
[assembly: AssemblyProduct("Music Tag")]
[assembly: AssemblyDescription("")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyVersion("1.0.0.0")]
