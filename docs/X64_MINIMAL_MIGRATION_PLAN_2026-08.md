# MusicTag x64 最小影响迁移方案

- 日期：2026-08-03
- 分支：`develop-net8`
- 状态：方案已落盘并经交叉审阅修订，待实施授权
- 范围：只完成 x64 迁移；不在本阶段精简、替换或移除 SQLite

本文已并入 Claude 的交叉审阅结论：§4.2、§4.3、§4.4、§5 批次 1、§6、§8.1、§8.2 有实质修订，
§12 记录独立复核的核验项与排除项。修订处均在正文标注，未标注的章节保持初稿判断。

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
- 仓库内本地 DLL 中，`TagLibSharp`、`UtfUnknown`、`Newtonsoft.Json`、`Fkosoft.FontAwesome4`、`System.Data.SQLite` 和三个 satellite resource DLL 都是 `ILOnly` 托管程序集（已按 COR20 标志位复核，无一个带 `32BITREQUIRED`，见 §12.1）。
- 唯一直接把进程锁定在 x86 的运行时文件是原生 `SQLite.Interop.dll`（已复核，见 §12.1）。
- 标准 Windows `user32`、`shell32`、`uxtheme` P/Invoke 和系统 COM 组件同时支持 x64，但其托管结构体字段宽度必须正确。

### 2.2 SQLite 二进制来源

仓库现有文件已经与官方 NuGet 包逐字节核对（交叉审阅时独立复算过一次，见 §12.1）：

| 仓库文件 | 官方 `System.Data.SQLite.Core 1.0.113` 条目 | 结果 |
|---|---|---|
| `System.Data.SQLite.dll` | `lib/net46/System.Data.SQLite.dll` | SHA-256 完全一致 |
| 当前 `SQLite.Interop.dll` | `build/net46/x86/SQLite.Interop.dll` | SHA-256 完全一致 |

因此无需猜测托管/原生配对关系。x64 版本必须取自同一官方包的 `build/net46/x64/SQLite.Interop.dll`：

- PE 架构：`AMD64 / PE32+`
- 文件版本：`1.0.113.0`
- SHA-256：`1D534617B38323027A64579A581258A55C3986F5B4B15297126C8A4CEF5AA105`
- 来源：<https://www.nuget.org/packages/System.Data.SQLite.Core/1.0.113>
  （NuGet V3 索引中的归一化包版本是 `1.0.113`；包内程序集与原生文件版本为
  `1.0.113.0`。NuGet 下载端点可接受带尾随 `.0` 的等价版本写法，但文档统一使用索引版本。）

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

## 4. x64 interop 审计结果

§4.1、§4.2 必须修正；§4.3 可达但已论证安全；§4.5 不可达、本阶段不处理。

### 4.1 `NativeNotificationHeader`

该结构映射 Windows `NMHDR`：

```text
HWND     hwndFrom
UINT_PTR idFrom
UINT     code
```

当前 `ControlId` 使用 32 位 `int`。在 x64 中 `UINT_PTR` 必须为 8 字节，当前托管结构只有 16 字节，而正确的 x64 `NMHDR` 应为 24 字节。该结构位于列表头右键通知的可达路径（`HeaderAwareListView.cs:55` 的 `WM_NOTIFY` 处理），必须改为指针宽类型并增加偏移/尺寸测试（期望值见 §4.4）。

改为 `UIntPtr` 在两个位数下都正确（x86 = 12 字节、x64 = 24 字节，均与原生一致），
因此批次 1 在 x86 基线上落地该修改是安全的。`ControlId` 本身无消费方，仅 `NotificationCode` 被读取。

### 4.2 `ShellFileInfo`

**修订（交叉审阅）**：该结构不只是"x64 隐患"，它**现在就是错的**，本次是修复既有缺陷，
提交信息与验收口径都应按行为修正对待。

该结构映射 Windows `SHFILEINFO`。三处问题：

1. 结构**没有任何 `StructLayout` 特性**，C# 默认 `CharSet.Ansi`，因此两个 `ByValTStr` 按 ANSI 计算宽度；
   而 `EntryPoint = "SHGetFileInfo"` + `CharSet.Auto` 在 Windows 上解析到的是 **`SHGetFileInfoW`**。
   ANSI/Unicode 当场不匹配。
2. 因此 `Marshal.SizeOf` 当前为 **352**（x86），而原生 `SHFILEINFOW` 为 **692**。
   `ImageUtilities.cs:178` 正是用 `Marshal.SizeOf(fileInfo)` 传 `cbFileInfo`，即**一直在传错值**。
3. `IconIndex` 被声明为 `IntPtr`，但原生 `iIcon` 实际为 32 位 `int`。

当前没有暴露为故障，是因为调用方 flags 为 `0x101 = SHGFI_ICON | SHGFI_SMALLICON`，
**没有请求 `SHGFI_DISPLAYNAME` / `SHGFI_TYPENAME`**，那两个字符串缓冲区从未被写入；
唯一被读取的 `IconHandle` 又恰好位于偏移 0。若以后请求字符串字段，错误的 `cbFileInfo`
可能导致调用失败、返回不完整，或在不遵守长度的实现路径上产生越界写入风险；不能继续依赖当前 flags 掩盖布局错误。

最小修复为：

- 给结构增加 `StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)`。
- 把 `IconIndex` 改为 `int`。
- 把导入固定为 Unicode 入口/字符集。
- 增加 `IconIndex`、`Attributes`、字符串数组的字段偏移和总尺寸测试（期望值见 §4.4）。

**实施约束**：修复后 `Marshal.SizeOf` 变为 692（x86）/ 696（x64），即 `cbFileInfo`
从 352 改为 692——这是对原生调用的**可观察变更**，且在批次 1 仍为 x86 时就会生效。
因此批次 1 必须**先记录图标当前是否正常**（截图或明确记录），否则事后无法区分
"修好了"与"弄坏了"。

### 4.3 可达但已论证安全的声明

**修订（交叉审阅）**：初稿把"可达但安全"与"完全不可达"混在同一份延后清单里，
风险等级不同，此处拆开。以下声明**在可达路径上**，经分析在 x64 下安全，本阶段可不改，
但必须记录为"已审计"，不得被后续读者误认为漏审：

- **`NativeMethods.EnumThreadWindowsCallback(IntPtr, int lParam)`**：`LPARAM` 应为指针宽。
  调用点 `StateFieldInstance.cs:2244`（枚举 `#32770` 对话框）真实可达。x64 下无害——
  两个参数均走寄存器（RCX/RDX），不涉及栈平衡；`CollectIfMatchingDialog` 不读 `lParam`，
  调用方恒传 `IntPtr.Zero`。改为 `IntPtr` 只是一个词，建议顺手修正。
- **`NativeMethods.SendTextBufferMessage(..., int wParam, ...)`**：`WPARAM` 应为指针宽。
  用于 `WM_GETTEXT`，实参是 `StringBuilder.Capacity`（恒为正），x64 上 32 位值零扩展入寄存器，
  取值正确。
- **`EditableListView.cs:90` 的私有 `SendMessage(IntPtr, uint, int wParam, int lParam)`**：
  该文件是仓库内**第二处 `DllImport`**，初稿未提及。调用点仅 `ScrollListView` 发
  `LVM_SCROLL (4116)`。dx/dy 可为负，x64 下 32 位实参只做零扩展，但 Windows 侧按
  `(int)wParam` 取低 32 位，负值仍正确还原；返回值被忽略。**判定安全，不改动。**

### 4.4 布局期望值（跨位数）

测试断言**必须写成 `IntPtr.Size` 的函数**，不得写死单一位数的常量——否则同一份断言在批次 1
（x86）与批次 2（x64）不可能同时成立，为过门禁而改断言会使"锁定 ABI"失去意义。

| 结构 | x86 尺寸 | x64 尺寸 | 关键字段偏移 x86 → x64 |
|---|---|---|---|
| `NativeNotificationHeader`（修复后） | 12 | 24 | `NotificationCode` 8 → 16 |
| `ShellFileInfo`（修复后） | 692 | 696 | `IconIndex` 4 → 8；`Attributes` 8 → 12；`DisplayName` 12 → 16 |
| `CopyDataStruct`（现状即正确） | 12 | 24 | `Data` 8 → 16 |
| `HeaderHitTestInfo`（与位数无关） | 16 | 16 | `ItemIndex` 12 → 12 |

`HeaderHitTestInfo` 位于列表头右键可达路径，本身无指针字段、天然安全；一并加断言是零成本，
且能固化"该结构与位数无关"这一事实，避免后续误加指针字段。

### 4.5 本阶段不处理的声明

以下项目**不可达**，不属于已确认的 x64 阻塞，不在最小迁移中顺带重构：

- 未使用的 `ListViewColumnInfo` / `SendListViewColumnMessage`（全仓零调用点，已复核）。
- 未调用缩略图按钮方法的 `ThumbButton` 声明（`ThumbBar*` 全仓零调用点，已复核；
  其 `ByValTStr` 同样是 ANSI/W 不匹配，但仅出现在未调用方法的签名中，
  COM vtable 槽位顺序不受结构内部布局影响）。
- File Dialog 与 Taskbar COM 接口的整理、重命名或抽象。

这些声明可以在后续 interop 精简议题中单独审计，不能扩大本阶段行为面。

## 5. 实施批次

### 批次 1：锁定并修正可达 Win32 布局

1. 对即将修改的结构和消费方法重新执行 GitNexus upstream impact。
2. **在改动前记录文件图标当前行为基线**（§4.2 会改变 `cbFileInfo` 传值，事后无基线则无法归因）。
3. 增加 `NativeNotificationHeader`、`ShellFileInfo`、`CopyDataStruct` 和 `HeaderHitTestInfo`
   布局测试。断言按 §4.4 **参数化为 `IntPtr.Size` 的函数**，使同一份断言在批次 1 与批次 2 均成立。
4. 修正两处可达结构及对应 P/Invoke；可顺带把 `EnumThreadWindowsCallback` 的 `int lParam`
   改为 `IntPtr`（§4.3，一词改动、无行为变化）。
5. 在仍为 x86 的基线下运行完整门禁，确认没有改变现有列表头右键和文件图标行为。
   注意：x86 门禁只能证明**没有回归**，无法验证 x64 修复是否正确——后者由 §4.4 的
   参数化断言与批次 2 的手工验证承担。

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
- `scripts/Verify-Build.ps1`（新增产物架构检查；顺带更新第 14 行已失效的
  "外部 netfx/x64 宿主无法加载 x86 net8 程序集"注释）

已审计、判定不需改动（记录在案，避免后续误认为漏审）：

- `src/MusicTag/MusicTagWinApp.Roles/EditableListView.cs`——仓库内第二处 `DllImport`，
  分析见 §4.3。
- `src/MusicTag/app.manifest`——无 `processorArchitecture` 依赖项声明。
- `src/MusicTag/musictag/MusicTag.exe.config`——仅含 net8 忽略的 netfx 遗留节。
- 全部 COM 接口（`IFileDialog` / `IFileOpenDialog` / `IShellItem` / `IShellItemArray` /
  `ITaskbarList4` / `FileDialogFilterSpec`）——指针宽参数均已是 `IntPtr` 或接口类型。

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
  该前提变化**必须写进发布说明**：只装了 x86 Desktop Runtime 的用户升级后会直接启动失败。
- 用户设置不受影响：`XmlSettingsProvider` 把设置写在 exe 同目录的 `MusicTag.config`，
  不是带 evidence 哈希的 `%LOCALAPPDATA%` `user.config`，位数切换不改变该路径（已复核）。
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

| 断言 | 自哪个批次起生效 |
|---|---|
| `NMHDR` / `SHFILEINFO` / `COPYDATASTRUCT` / `HDHITTESTINFO` 布局与 §4.4 一致 | 批次 1（参数化，两个批次都要过） |
| 现有 characterization 全部继续通过 | 批次 1 |
| 主测试进程为 64 位（`Environment.Is64BitProcess`） | **批次 2**（批次 1 仍为 x86，此断言必然失败） |
| `MusicTag.exe`、`MusicTag.Tests.exe` 和原生 `SQLite.Interop.dll` 为 AMD64 | **批次 2** |
| `System.Data.SQLite.dll` 与 `SQLite.Interop.dll` 文件版本均为 `1.0.113.0` | **批次 2** |
| x64 进程能够打开 SQLite、执行查询并正常释放连接 | **批次 2** |
| Release 启动后保持存活且没有 `UnhandledException` / `ThreadException` 日志 | 批次 1 |

批次归属必须照此执行：把"进程为 64 位"一类断言写进批次 1 会直接卡死门禁。

### 8.2 重点手工验证

1. 使用仓库现有数据库启动，打开标签历史窗口并读取历史。
2. 保存标签后查看/恢复历史；验证事务提交和回滚。
3. 重命名、删除、自动匹配和清空历史，确认数据库路径同步与 `VACUUM` 正常。
4. 加载带文件图标的列表，验证图标获取、释放和重复刷新，并**与批次 1 记录的基线逐项比对**
   （§4.2 改变了 `cbFileInfo` 传值）。
5. 右键列表头，验证列菜单命中位置正常。
6. 启动第二实例并传入文件参数，验证 `WM_COPYDATA` 转发。
7. 验证文件/目录对话框和任务栏进度。
8. 在 100% 与 150% DPI 双屏环境执行副屏首启、跨屏拖动和往返；组合标签源与歌词源窗口不得出现新的缩放问题。
9. 对代表性 MP3/FLAC 执行实体标签写入和回读。

旧 x86 版与新 x64 版的并存转发不在本阶段范围：Windows 会锁定正在运行的 exe，不能在同一路径
原位替换；从不同路径启动时，现有 `IsSameExecutable` 又会有意把它们视为两个独立实例。
升级前应退出旧进程，不能把侧载目录之间的跨位数通信写成产品承诺。

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

## 12. 独立复核记录（2026-08-03）

本节记录交叉审阅时**实际执行的核验**，用于避免后续重复求证或重新争论已定事实。

### 12.1 已独立验证为真

| 方案声明 | 验证方式 | 结果 |
|---|---|---|
| 仓库两个 SQLite 文件与官方包逐字节一致 | 下载 `System.Data.SQLite.Core 1.0.113`，逐条目算 SHA-256 | 托管件 = `lib/net46/`，原生件 = `build/net46/x86/`，**均吻合** |
| x64 interop SHA-256 `1D5346…A105` | 同一包 `build/net46/x64/SQLite.Interop.dll` | **逐位吻合**，且确为 `AMD64 / PE32+` |
| 本地托管程序集均为 ILOnly | 解析 COR20 header 的 flags 位 | 8 个全部 `ILONLY`，**无一个带 `32BITREQUIRED`**，可载入 64 位进程 |
| `SQLite.Interop.dll` 是唯一原生库 | 按有无 COR20 数据目录区分托管/原生 | ✓ `musictag/` 下仅此一个原生 PE |
| `NativeNotificationHeader` 在可达路径 | 追调用点 | ✓ `HeaderAwareListView.cs:55` 的 `WM_NOTIFY` 实时读取 |
| `ListViewColumnInfo`、`ThumbButton` 不可达 | 全仓搜引用 | ✓ 均零调用点 |
| `CopyDataStruct` 布局已正确 | 手算 x64 对齐 | ✓ 8+4+pad4+8 = 24，与 `COPYDATASTRUCT` 一致 |

### 12.2 已排查、确认不构成风险

以下是 x64 迁移的常见故障源，本仓库均不适用，无需为其增加工作项：

- **`app.manifest` 无 `processorArchitecture` 硬编码**（老程序最常见的 x64 坑）。
- **全仓零注册表访问** → 不存在 `Wow6432Node` 重定向行为差异。
- **无 `System32` / `Program Files` 路径硬编码** → 不存在 WOW64 文件系统重定向差异。
- **全仓零 `IntPtr.ToInt32()`** → 不存在句柄截断的 `OverflowException`。
- **全仓零 `m.WParam` / `m.LParam` 直接访问**；4 个 `WndProc` 覆写只比较 `m.Msg`；
  `Marshal.PtrToStructure` / `GetLParam` 全仓仅 2 处，均已在 §4 覆盖。
- **无 `unsafe` / `stackalloc` / `fixed` 代码**。
- **设置文件路径不变**（详见 §7）。
- **§2.3 探针遗留的中间产物无害**：探针未覆盖 `BaseIntermediateOutputPath`，
  致 `obj/Release/net8.0-windows/` 留下 AMD64 中间件而 csproj 仍为 x86。
  已实测：`dotnet build -c Release` 会正确重建并输出 I386，**不需要额外的 obj 清理步骤**。
  （复现探针时建议一并覆盖 `BaseIntermediateOutputPath`，以免读者困惑。）
- **`MusicTag.csproj` 中 CS0649 注释提到的 `NativePictureEntry` 已不存在**，
  是过期注释而非漏审的第三个结构。

### 12.3 复核带来的方案修订

1. §4.2：`ShellFileInfo` 重新定性为**既有缺陷修复**（ANSI/W 不匹配 + `cbFileInfo` 传 352 而非 692），
   并要求批次 1 先取图标行为基线。
2. §4.3：拆出"可达但已论证安全"一类，点名 `EnumThreadWindowsCallback`、
   `SendTextBufferMessage` 和此前完全未提及的 `EditableListView.cs` 第二处 `DllImport`。
3. §4.4：新增跨位数布局期望值表，要求断言参数化为 `IntPtr.Size` 的函数——
   否则批次 1（x86）与批次 2（x64）的断言不可能同时成立。
4. §8.1：门禁按批次归属重排，避免把"进程为 64 位"写进仍为 x86 的批次 1。
5. §6：新增"已审计、判定不需改动"清单。
6. §7、§8.2：补运行时前置条件的发布说明要求，以及跨位数 `WM_COPYDATA` 的处置选择。
