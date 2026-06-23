# MusicTag

这是从原版 `musictag` 本地音乐标签工具恢复出来的源码项目。原程序是一个基于 `.NET Framework 4.6.1` 的 Windows WinForms 桌面软件，用于编辑本地音乐文件标签、歌词、封面，并支持从多个网络来源搜索音乐标签信息。

当前仓库的目标不是重写软件，而是在尽量保持原行为不变的前提下，把反编译源码整理成更接近普通 GitHub 项目的形态，方便后续维护和排查联网搜索问题。目前已在保持功能等效的前提下，将目标框架从 `.NET Framework 4.6.1` 迁移到 `.NET Framework 4.8.1`。

## 项目结构

- `src/MusicTag/`：当前可编译的源码目录。
- `src/MusicTag/MusicTag.csproj`：SDK 风格的 WinForms 项目文件。
- `MusicTag.sln`：可用 Visual Studio 或 MSBuild 打开的解决方案。
- `src/MusicTag/musictag/`：从原程序保留的运行时依赖，例如原生 DLL、数据库、资源和字体文件。
- `scripts/Verify-Build.ps1`：本地和 CI 共用的构建验证脚本。
- `docs/MAINTENANCE.md`：维护策略、清理记录和常用命令。
- `docs/DECOMPILATION_NOTES.md`：暂不强行重命名或重构的反编译残留清单。

## 环境要求

- Windows。
- Visual Studio Build Tools 2022 或更新版本，安装 MSBuild 和 .NET Framework 构建工具。
- .NET Framework 4.8.1 Developer Pack 或兼容的 targeting pack。
- PowerShell，用于运行验证脚本。

如果 `msbuild` 没有加入环境变量，验证脚本会优先尝试通过 PATH、`vswhere` 和常见 Build Tools 安装路径自动查找。

## 构建方式

推荐直接运行仓库内的验证脚本：

```powershell
.\scripts\Verify-Build.ps1
```

如果还想做一次基础启动检查：

```powershell
.\scripts\Verify-Build.ps1 -RunSmokeTests
```

构建输出位于：

```text
src/MusicTag/bin/Release/net481/MusicTag.exe
```

构建时会自动复制必要运行文件，例如 `MusicTag.dll`、`MediaInfo.dll`、`SQLite.Interop.dll`、`MusicTag.db`、`MusicTag.dat`、多语言资源 DLL 和 FontAwesome 字体。构建日志会写入 `artifacts/` 目录。

## 运行方式

构建成功后，可以直接运行：

```powershell
.\src\MusicTag\bin\Release\net481\MusicTag.exe
```

首次维护或调试时，建议先运行 `.\scripts\Verify-Build.ps1 -RunSmokeTests`，确认程序能够构造关键窗口并正常启动数秒。

## 当前已知问题

- 这是反编译恢复源码，仍保留部分反编译和混淆痕迹。
- 部分大型 WinForms 窗体和 async 状态机仍需小批量整理，不建议一次性大规模重写。
- 联网标签搜索依赖第三方服务接口，接口返回异常、字段缺失或格式变化时仍可能影响候选结果。
- `tools/` 是本地反编译/分析工具目录，已被忽略，不应提交到仓库。

## 维护建议

- 每次只做小范围、可验证的行为等价修改。
- 优先修复会影响构建、候选搜索、标签读写和数据完整性的问题。
- 修改后运行：

```powershell
.\scripts\Verify-Build.ps1 -RunSmokeTests
git diff --check
codegraph sync
```

- 不确定含义的类名、函数名或字段名不要强行改名，先保留并在 `docs/DECOMPILATION_NOTES.md` 或 `docs/MAINTENANCE.md` 中记录。
