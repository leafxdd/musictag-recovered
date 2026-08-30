# MusicTag

这是从原版 `musictag` 本地音乐标签工具恢复出来的源码项目。原程序是一个基于 `.NET Framework 4.6.1` 的 Windows WinForms 桌面软件，用于编辑本地音乐文件标签、歌词、封面，并支持从多个网络来源搜索音乐标签信息。

当前仓库的目标不是重写软件，而是在尽量保持原行为不变的前提下，把反编译源码整理成更接近普通 GitHub 项目的形态，方便后续维护和排查联网搜索问题。当前开发分支 `develop-net8` 以 `net8.0-windows` x64 运行；`develop` 仍保留独立的 `.NET Framework 4.8.1` 开发线。

## 项目结构

- `src/MusicTag/`：当前可编译的源码目录。
- `src/MusicTag/MusicTag.csproj`：SDK 风格的 WinForms 项目文件。
- `MusicTag.sln`：可用 Visual Studio 或 MSBuild 打开的解决方案。
- `src/MusicTag/musictag/`：运行时依赖文件，例如托管库（`TagLibSharp`、`UtfUnknown` 等）、SQLite 互操作库、数据库、多语言资源和字体文件。原版自带的原生 `MusicTag.dll`/`MediaInfo.dll` 已被托管实现取代并移除（见 `docs/NATIVE_DEPENDENCY_REMOVAL_PLAN.md`）。
- `scripts/Verify-Build.ps1`：本地和 CI 共用的构建验证脚本。
- `docs/MAINTENANCE.md`：维护策略、清理记录和常用命令。
- `docs/DECOMPILATION_NOTES.md`：暂不强行重命名或重构的反编译残留清单。
- `docs/CLEANUP_PLAN.md`：死代码与反编译残留的清理计划及进度记录。
- `docs/NATIVE_DEPENDENCY_REMOVAL_PLAN.md`：去除原生 `MusicTag.dll` 依赖、迁移到托管实现的规划与实施记录。

## 环境要求

- 64 位 Windows。
- .NET 8 SDK，用于构建 `develop-net8`。
- .NET 8 Windows Desktop Runtime x64，用于运行构建产物。
- PowerShell，用于运行验证脚本。

验证脚本直接调用 `dotnet build`。解决方案配置名称仍为 `Any CPU`，但主程序和测试项目都通过 `PlatformTarget=x64` 固定生成 AMD64 进程，脚本会在每次构建后校验产物架构。

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
src/MusicTag/bin/Release/net8.0-windows/MusicTag.exe
```

构建时会自动复制必要运行文件，例如 `TagLibSharp.dll`、`UtfUnknown.dll`、`SQLite.Interop.dll`、`System.Data.SQLite.dll`、`Newtonsoft.Json.dll`、多语言资源 DLL 和 FontAwesome 字体。`MusicTag.db` 与 `MusicTag.dat` 是每个安装实例在首次运行时生成的本地历史和文件列表状态，不随源码或发布包提供。当前 SQLite 组合固定为托管 Provider `1.0.113.0` 和同版本官方 x64 interop；测试会真实打开内存数据库并执行查询。构建日志会写入 `artifacts/` 目录。

## 运行方式

构建成功后，可以直接运行：

```powershell
.\src\MusicTag\bin\Release\net8.0-windows\MusicTag.exe
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

## 免责声明

- 本项目是对第三方音乐标签工具 `musictag` 的**反编译恢复与整理**，仅用于个人学习、研究和技术交流，请勿用于任何商业用途。
- 原软件的著作权归其原作者所有。本仓库不附带、不分发原版的原生二进制（`MusicTag.dll`/`MediaInfo.dll` 已被托管实现取代并移除）。若原作者认为本项目侵犯其权益，请联系处理，我们会及时配合下架。
- 联网标签、歌词、封面搜索调用网易云音乐、QQ 音乐、酷狗、酷我等第三方公开网络接口，相关数据的版权归各平台及内容方所有；请在遵守对应服务条款和当地法律法规的前提下使用，搜索到的内容仅供个人试听与学习。
- 使用本项目（含源码、构建产物与发布的可执行文件）所产生的一切风险与后果，由使用者自行承担。
