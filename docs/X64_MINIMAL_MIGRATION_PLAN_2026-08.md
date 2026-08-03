# MusicTag x64 最小影响迁移方案

- 日期：2026-08-03
- 分支：`develop-net8`
- 状态：方案已落盘，待实施授权
- 范围：只完成 x64 迁移；不在本阶段精简、替换或移除 SQLite

## 1. 目标与结论

本阶段把 MusicTag 主程序和 characterization 测试宿主从 x86 切换为真正的 x64 进程，同时保持现有功能、数据库格式、构建输出路径和部署模型不变。

采用以下最小影响决策：

1. 保留当前 `System.Data.SQLite.dll 1.0.113` 的 `net46` 托管程序集，不升级 SQLite Provider，不迁移到 `Microsoft.Data.Sqlite`。
2. 只把当前 x86 `SQLite.Interop.dll` 替换为官方 `System.Data.SQLite.Core 1.0.113` 包中的 `build/net46/x64/SQLite.Interop.dll`。
3. 主项目和测试项目的 `PlatformTarget` 从 `x86` 改为 `x64`。
4. 暂时保留 `MusicTag.sln` 的 `Any CPU` 配置名称和现有 `bin/{Configuration}/net8.0-windows/` 输出路径；项目文件的硬性 `PlatformTarget=x64` 和新增架构测试共同保证实际产物为 AMD64。
5. 只修正已经可达、且会在 x64 下产生错误布局的 Win32 interop 声明；不顺带清理未使用的 P/Invoke/COM 类型。
6. 保持当前 framework-dependent 部署，不增加 `RuntimeIdentifier=win-x64`，不改成 self-contained 发布。

这条路径不会改变 SQLite 表结构、SQL、事务语义或历史记录行为。数据库精简另立议题，不能混入 x64 提交。

本阶段也不把现有本地 `Reference` 改为 `PackageReference`。`net8.0-windows` 从该 NuGet 包解析时会优先选择 `netstandard2.1` 托管资产，而不是仓库当前逐字节匹配的 `net46` 程序集；这样会同时改变托管 Provider，超出“只替换架构配对”的最小边界。

## 2. 已确认的当前状态

### 2.1 架构约束

- `src/MusicTag/MusicTag.csproj` 和 `src/MusicTag.Tests/MusicTag.Tests.csproj` 当前均为 `PlatformTarget=x86`。
- 仓库内本地 DLL 中，`TagLibSharp`、`UtfUnknown`、`Newtonsoft.Json`、`Fkosoft.FontAwesome4`、`System.Data.SQLite` 和三个 satellite resource DLL 都是 `ILOnly` 托管程序集。
- 唯一直接把进程锁定在 x86 的运行时文件是原生 `SQLite.Interop.dll`。
- 标准 Windows `user32`、`shell32`、`uxtheme` P/Invoke 和系统 COM 组件同时支持 x64，但其托管结构体字段宽度必须正确。

### 2.2 SQLite 二进制来源

仓库现有文件已经与官方 NuGet 包逐字节核对：

| 仓库文件 | 官方 `System.Data.SQLite.Core 1.0.113` 条目 | 结果 |
|---|---|---|
| `System.Data.SQLite.dll` | `lib/net46/System.Data.SQLite.dll` | SHA-256 完全一致 |
| 当前 `SQLite.Interop.dll` | `build/net46/x86/SQLite.Interop.dll` | SHA-256 完全一致 |

因此无需猜测托管/原生配对关系。x64 版本必须取自同一官方包的 `build/net46/x64/SQLite.Interop.dll`：

- PE 架构：`AMD64 / PE32+`
- 文件版本：`1.0.113.0`
- SHA-256：`1D534617B38323027A64579A581258A55C3986F5B4B15297126C8A4CEF5AA105`
- 来源：<https://www.nuget.org/packages/System.Data.SQLite.Core/1.0.113>

### 2.3 隔离 x64 探针

在不修改项目文件的情况下，用命令行覆盖 `PlatformTarget=x64` 并输出到 `artifacts/x64-probe/`，得到以下结果：

- `MusicTag.exe`、`MusicTag.dll`、`MusicTag.Tests.exe` 和 `MusicTag.Tests.dll` 均为 AMD64。
- 主项目和测试项目均为 0 编译错误。
- 现有 characterization 套件在 x64 宿主中仍报告 `956 passed, 0 failed`。
- 输出目录仍携带 x86 `SQLite.Interop.dll` 时，64 位进程构造 `SQLiteConnection` 立即抛出 `BadImageFormatException (0x8007000B)`。
- 仅在隔离输出中替换为官方同版本 x64 interop 后，64 位进程成功打开内存数据库并执行查询，SQLite 内核版本为 `3.32.1`。

这证明代码主体可以编译并运行在 x64，同时也证明现有测试没有覆盖 SQLite 原生加载。新增数据库加载门禁是迁移的必要条件，不是可选增强。

## 3. SQLite 在当前产品中的边界

SQLite 只持久化标签历史，不保存音乐库、在线 Provider 结果、歌词、封面、应用设置或音乐文件本身。

`TagHistoryRepository` 使用两个表：

- `tagshistory`：保存文件路径、记录时间、标题、歌手、专辑、年份、音轨和碟号等标签快照。
- `config`：保存 `thserial_prefix`，用于生成跨会话的历史记录序号。

当前保存标签、重命名、删除、自动匹配、历史查看/恢复和清空历史等流程都会触达 `TagHistoryRepository`。GitNexus 对该类的上游分析为 `CRITICAL`：13 个直接依赖、10 条受影响执行流程、4 个模块。

因此本阶段只替换同版本原生二进制，不修改 `TagHistoryRepository` 的 Provider API、SQL、事务、数据库路径或 schema。

## 4. 必须修正的 x64 interop

### 4.1 `NativeNotificationHeader`

该结构映射 Windows `NMHDR`：

```text
HWND     hwndFrom
UINT_PTR idFrom
UINT     code
```

当前 `ControlId` 使用 32 位 `int`。在 x64 中 `UINT_PTR` 必须为 8 字节，当前托管结构只有 16 字节，而正确的 x64 `NMHDR` 应为 24 字节。该结构位于列表头右键通知的可达路径，必须改为指针宽类型并增加偏移/尺寸测试。

### 4.2 `ShellFileInfo`

该结构映射 Windows `SHFILEINFO`。当前 `IconIndex` 被声明为 `IntPtr`，但原生 `iIcon` 实际为 32 位 `int`；结构也没有显式指定与 `SHGetFileInfoW` 一致的 Unicode 字符集。

最小修复为：

- 给结构增加 `StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)`。
- 把 `IconIndex` 改为 `int`。
- 把导入固定为 Unicode 入口/字符集。
- 增加 `IconIndex`、`Attributes`、字符串数组的字段偏移和总尺寸测试。

该路径只使用返回的图标句柄，但错误布局不能带入 x64 版本。

### 4.3 本阶段不处理的声明

以下项目不属于已确认的 x64 阻塞，不在最小迁移中顺带重构：

- 未使用的 `ListViewColumnInfo` / `SendListViewColumnMessage`。
- 未调用缩略图按钮方法的 `ThumbButton` 声明。
- 参数实际恒为零或 32 位消息值、当前没有失败证据的其它 `SendMessage`/回调签名。
- File Dialog 与 Taskbar COM 接口的整理、重命名或抽象。

这些声明可以在后续 interop 精简议题中单独审计，不能扩大本阶段行为面。

## 5. 实施批次

### 批次 1：锁定并修正可达 Win32 布局

1. 对即将修改的结构和消费方法重新执行 GitNexus upstream impact。
2. 增加 `NativeNotificationHeader`、`ShellFileInfo` 和已确认正确的 `CopyDataStruct` 布局测试。
3. 修正两处可达结构及对应 P/Invoke。
4. 在仍为 x86 的基线下运行完整门禁，确认没有改变现有列表头右键和文件图标行为。

建议提交：

```text
fix: make active Win32 interop layouts x64-safe
```

### 批次 2：切换主程序、测试宿主和 SQLite interop

1. 将两个项目的 `PlatformTarget` 改为 `x64`。
2. 用官方 `build/net46/x64/SQLite.Interop.dll` 替换仓库现有原生文件。
3. 更新项目和测试中的 x86 注释；保留解决方案配置名和输出目录结构。
4. 新增 x64 架构门禁：测试进程必须满足 `Environment.Is64BitProcess == true`。
5. 新增 SQLite 原生加载测试：打开内存数据库、执行 `SELECT 1`，并验证 Provider/interop 文件版本一致。
6. 在验证脚本中增加产物架构检查：主程序、测试宿主和原生 interop 必须为 AMD64；`ILOnly` 托管程序集不能因 PE32/I386 头被误报。

建议提交：

```text
build: switch MusicTag and tests to x64
```

### 批次 3：记录实际实施结果

把最终文件哈希、测试数量、手工验证结果、残余风险和回滚演练追加到独立实施报告或本文件末尾。

建议提交：

```text
docs: record x64 migration results
```

## 6. 预计修改范围

产品和构建文件：

- `src/MusicTag/MusicTag.csproj`
- `src/MusicTag.Tests/MusicTag.Tests.csproj`
- `src/MusicTag/musictag/SQLite.Interop.dll`
- `src/MusicTag/MusicTagWinApp.Containers/NativeMethods.cs`
- `scripts/Verify-Build.ps1`

测试与文档：

- 新增或扩展 architecture/interop/SQLite characterization 文件
- `src/MusicTag.Tests/Program.cs`
- `README.md`
- `AGENTS.md`
- `CLAUDE.md`
- x64 实施报告

本最小方案不要求修改：

- `MusicTag.sln`
- `TagHistoryRepository.cs`
- SQLite schema 或仓库自带 `MusicTag.db`
- Provider、标签写入、DPI、设置持久化或资源文件

## 7. 兼容性与回滚

- SQLite 数据库文件格式与进程位数无关，现有 `MusicTag.db` 可由同版本 x64 Provider 直接打开。
- 本阶段没有 schema migration；x64 版本写入后的数据库仍可由原 x86 版本读取。
- 程序设置键、JSON、资源名称、`SearchSource` 序号、TagLib 写入策略和在线 Provider 合同均不变。
- framework-dependent 部署模式保持不变，但运行时架构前提会从 x86 Desktop Runtime 切换为 .NET 8 Windows Desktop Runtime x64。
- 回滚必须同时还原两个项目的 `PlatformTarget` 和 x86 `SQLite.Interop.dll`，不能只回滚其中一项。
- 因为数据库格式不变，回滚不需要转换或删除用户数据库。

## 8. 验证门禁

### 8.1 自动验证

每个实现提交执行：

```powershell
.\scripts\Verify-Build.ps1 -RunSmokeTests
git diff --check
codegraph sync
```

提交前运行 GitNexus `detect_changes(scope: staged)`，确认只影响预期的 interop、架构、SQLite 加载和验证流程。

新增的 x64 专项断言至少覆盖：

- 主测试进程为 64 位。
- `MusicTag.exe`、`MusicTag.Tests.exe` 和原生 `SQLite.Interop.dll` 为 AMD64。
- `System.Data.SQLite.dll` 与 `SQLite.Interop.dll` 文件版本均为 `1.0.113.0`。
- x64 进程能够打开 SQLite、执行查询并正常释放连接。
- `NMHDR` 和 `SHFILEINFO` 关键字段偏移与 Windows ABI 一致。
- 现有 characterization 全部继续通过。
- Release 启动后保持存活且没有 `UnhandledException` / `ThreadException` 日志。

### 8.2 重点手工验证

1. 使用仓库现有数据库启动，打开标签历史窗口并读取历史。
2. 保存标签后查看/恢复历史；验证事务提交和回滚。
3. 重命名、删除、自动匹配和清空历史，确认数据库路径同步与 `VACUUM` 正常。
4. 加载带文件图标的列表，验证图标获取、释放和重复刷新。
5. 右键列表头，验证列菜单命中位置正常。
6. 启动第二实例并传入文件参数，验证 `WM_COPYDATA` 转发。
7. 验证文件/目录对话框和任务栏进度。
8. 在 100% 与 150% DPI 双屏环境执行副屏首启、跨屏拖动和往返；组合标签源与歌词源窗口不得出现新的缩放问题。
9. 对代表性 MP3/FLAC 执行实体标签写入和回读。

### 8.3 打包检查

- 从干净构建目录生成包，不复用已运行的 `bin`。
- 确认包内只有一个根级 `SQLite.Interop.dll`，且为 AMD64、版本 `1.0.113.0`、哈希与方案一致。
- 区分原生 PE 与 `ILOnly` 托管程序集，不能把 AnyCPU 托管 DLL 的 I386/PE32 头误判为 x86 运行时依赖。
- 扫描并排除 `.pdb`、`temp/`、日志、缓存、备份设置和用户数据。

## 9. 风险与停止条件

| 风险 | 防护 | 停止条件 |
|---|---|---|
| 托管/原生 SQLite 版本或位数不匹配 | 固定官方 1.0.113 来源、版本/PE/打开测试 | 任何 `BadImageFormatException`、`DllNotFoundException` 或版本不一致 |
| Win32 结构在 x64 下错位 | 字段偏移测试和可达 UI 手测 | 列表头通知、图标或 shell UI 异常 |
| 现有测试继续漏掉数据库 | 增加真实 SQLite 打开/查询门禁 | 只能编译、不能在 x64 进程内查询 |
| 旧数据库兼容性回归 | 对现有数据库副本做读写和回滚验证 | schema、时间或历史内容发生非预期变化 |
| `Any CPU` 名称造成误解 | 项目硬锁 x64，加产物架构断言 | 任一标准构建生成非 AMD64 主程序 |
| x64 引入 DPI/UI 回归 | 100%/150% 双屏手测 | 首显、跨屏往返或固定资产缩放回归 |

任一停止条件出现时，不迁移 SQLite API 来“顺便解决”；先回到对应批次定位，保持每个提交可独立回滚。

## 10. 后续精简议题

以下问题待 x64 迁移稳定后另行分析和决策：

1. 继续保留 `System.Data.SQLite`，还是升级到更新版本。
2. 是否迁移到 `Microsoft.Data.Sqlite` 或其它 RID 管理更清晰的 Provider。
3. 标签历史是否需要 SQLite，能否使用更简单的持久化格式。
4. 是否允许关闭或移除标签历史功能，从而完全删除 SQLite 依赖。
5. 是否需要历史条数/容量上限、自动清理或数据库位置迁移。
6. 是否把解决方案配置正式改名为 `x64`，并引入 `win-x64` RID 或 self-contained 发布。
7. 是否清理当前未使用或 ABI 不完整的 P/Invoke/COM 声明。

这些讨论必须先收集实际历史功能使用需求、数据保留要求、包体积和部署目标，再单独形成设计文档。不得反向扩大本最小迁移方案。

## 11. 验收标准

- 标准 Debug/Release 构建产生真正的 AMD64 主程序和测试宿主。
- 官方同版本 x64 `SQLite.Interop.dll` 是包内唯一 SQLite 原生库。
- 现有 `MusicTag.db` 无 schema migration 即可完整读写，并可回退到 x86 版本。
- 保存、重命名、删除、自动匹配、历史查看/恢复和清空历史行为保持不变。
- 已确认的 Win32 结构布局在 x64 下符合 ABI。
- 完整自动门禁和重点手工验证通过。
- 没有混入 SQLite 精简、Provider 替换、版本升级、输出目录调整或部署模型变化。
