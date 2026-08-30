# MusicTag

这是从原版 `musictag` 本地音乐标签工具恢复出来的源码项目。原程序是一个基于 `.NET Framework 4.6.1` 的 Windows WinForms 桌面软件，用于编辑本地音乐文件标签、歌词、封面，并支持从多个网络来源搜索音乐标签信息。

当前仓库的目标不是重写软件，而是在尽量保持原行为不变的前提下，把反编译源码整理成更接近普通 GitHub 项目的形态，方便后续维护和排查联网搜索问题。目前已在保持功能等效的前提下，将目标框架从 `.NET Framework 4.6.1` 迁移到 `.NET Framework 4.8.1`。

## 项目结构

- `src/MusicTag/`：当前可编译的源码目录。
- `src/MusicTag/MusicTag.csproj`：SDK 风格的 WinForms 项目文件。
- `MusicTag.sln`：可用 Visual Studio 或 MSBuild 打开的解决方案。
- `src/MusicTag/musictag/`：运行时依赖文件，例如托管库（`TagLibSharp`、`UtfUnknown` 等）、SQLite 互操作库、数据库、多语言资源和字体文件。原版自带的原生 `MusicTag.dll`/`MediaInfo.dll` 已被托管实现取代并移除。

## 环境要求

- Windows。
- Visual Studio 2022 或 Build Tools 2022（或更新版本），安装 MSBuild 和 .NET Framework 构建工具。
- .NET Framework 4.8.1 Developer Pack 或兼容的 targeting pack。

## 构建方式

用 Visual Studio 打开 `MusicTag.sln` 直接生成，或使用 MSBuild 命令行：

```powershell
msbuild MusicTag.sln /restore /p:Configuration=Release
```

构建输出位于：

```text
src/MusicTag/bin/Release/net481/MusicTag.exe
```

构建时会自动复制必要运行文件，例如 `TagLibSharp.dll`、`UtfUnknown.dll`、`SQLite.Interop.dll`、`System.Data.SQLite.dll`、`Newtonsoft.Json.dll`、多语言资源 DLL 和 FontAwesome 字体。`MusicTag.db` 与 `MusicTag.dat` 是每个安装实例在首次运行时生成的本地历史和文件列表状态，不随源码或发布包提供。

## 运行方式

构建成功后，可以直接运行：

```powershell
.\src\MusicTag\bin\Release\net481\MusicTag.exe
```

也可以直接从 [Releases](../../releases) 下载打包好的可执行文件，解压后运行其中的 `MusicTag.exe`（已内置全部运行时依赖，无需额外安装）。

## 当前已知问题

- 这是反编译恢复源码，仍保留部分反编译和混淆痕迹。
- 部分大型 WinForms 窗体和 async 状态机仍需小批量整理，不建议一次性大规模重写。
- 联网标签搜索依赖第三方服务接口，接口返回异常、字段缺失或格式变化时仍可能影响候选结果。

## 免责声明

- 本项目是对第三方音乐标签工具 `musictag` 的**反编译恢复与整理**，仅用于个人学习、研究和技术交流，请勿用于任何商业用途。
- 原软件的著作权归其原作者所有。本仓库不附带、不分发原版的原生二进制（`MusicTag.dll`/`MediaInfo.dll` 已被托管实现取代并移除）。若原作者认为本项目侵犯其权益，请联系处理，我们会及时配合下架。
- 联网标签、歌词、封面搜索调用网易云音乐、QQ 音乐、酷狗、酷我等第三方公开网络接口，相关数据的版权归各平台及内容方所有；请在遵守对应服务条款和当地法律法规的前提下使用，搜索到的内容仅供个人试听与学习。
- 使用本项目（含源码、构建产物与发布的可执行文件）所产生的一切风险与后果，由使用者自行承担。
