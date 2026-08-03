# MusicTag x64 最小迁移实施报告

- 日期：2026-08-03
- 分支：`develop-net8`
- 方案：`docs/X64_MINIMAL_MIGRATION_PLAN_2026-08.md`
- 结论：最小影响 x64 迁移已完成，自动门禁通过

## 1. 实施提交

| 提交 | 内容 |
|---|---|
| `1cecade` | 修正可达 Win32 interop 布局，并增加跨位数 ABI 与文件图标 characterization |
| `a654b10` | 把主程序、测试宿主和 SQLite 原生依赖切换为 x64，并增加架构/SQLite 门禁 |

本次没有修改 SQLite schema、`TagHistoryRepository`、Provider、标签写入策略、DPI 算法、设置键、解决方案配置名、输出目录或部署模型。

## 2. 实际改动

### 2.1 Win32 ABI

- `NativeNotificationHeader.ControlId` 从 32 位 `int` 改为指针宽 `UIntPtr`。
- `ShellFileInfo` 固定为 Unicode Sequential 布局，`IconIndex` 改为原生 32 位 `int`。
- `SHGetFileInfo` 固定调用 `SHGetFileInfoW`。
- `EnumThreadWindowsCallback` 和消费回调的 `lParam` 改为 `IntPtr`。
- 新增 `NMHDR`、`SHFILEINFOW`、`COPYDATASTRUCT`、`HDHITTESTINFO` 的尺寸和偏移断言；同一套断言按 `IntPtr.Size` 同时覆盖 x86 基线与 x64 结果。

### 2.2 x64 与 SQLite

- `MusicTag.csproj` 和 `MusicTag.Tests.csproj` 的 `PlatformTarget` 均改为 `x64`。
- 保留原 `System.Data.SQLite.dll 1.0.113.0`，只把原生 interop 替换为官方同版本 x64 文件。
- 测试宿主新增真实 SQLite 内存连接，执行 `SELECT 1, sqlite_version()`，并核对托管/原生文件版本。
- `Verify-Build.ps1` 解析 PE 头，逐配置拒绝非 AMD64 的主程序、测试宿主或 SQLite 原生 DLL。

## 3. 二进制身份

| 文件 | 结果 |
|---|---|
| `System.Data.SQLite.dll` | 保持 `1.0.113.0`，未替换 |
| `SQLite.Interop.dll` | `AMD64 / PE32+`，文件版本 `1.0.113.0` |
| x64 interop SHA-256 | `1D534617B38323027A64579A581258A55C3986F5B4B15297126C8A4CEF5AA105` |
| SQLite engine | `3.32.1` |

## 4. 验证证据

### 4.1 改动前后 characterization

- 未修改产品代码的原始基线：`956 passed, 0 failed`。
- 加入布局用例但保留旧声明：`960 passed, 2 failed`；失败项准确指向 `SHFILEINFOW` 的 352/692 尺寸差异和非 Unicode 导入。文件图标真实调用同时通过，记录了修复前行为基线。
- Win32 ABI 修复后、仍为 x86：`962 passed, 0 failed`。
- 切换 x64 并加入架构/SQLite 用例后：`964 passed, 0 failed`。

### 4.2 完整门禁

执行：

```powershell
.\scripts\Verify-Build.ps1 -RunSmokeTests
```

结果：

- Debug 与 Release 均构建成功，0 error。
- 两个配置中的 `MusicTag.exe`、`MusicTag.Tests.exe`、`SQLite.Interop.dll` 均通过 AMD64 检查。
- 测试进程确认 `Environment.Is64BitProcess == true`。
- SQLite 托管/原生文件版本均为 `1.0.113.0`，内存数据库成功打开，`SELECT 1` 返回 1，SQLite 版本返回 `3.32.1`。
- `964 passed, 0 failed`。
- Release 进程启动后保持存活，`StartedAndStayedAlive=True`。
- 启动日志未发现 `UnhandledException` 或 `ThreadException`，`NoFatalExceptionLogs=True`。
- `git diff --check` 和 `codegraph sync` 通过；两个实现提交的 GitNexus staged 变更检测均为 LOW。

## 5. 兼容性边界

- SQLite 数据库格式、表结构和路径不变，x64 版本没有 schema migration。
- 设置文件、资源名、JSON、枚举序号和在线 Provider 合同不变。
- 仍为 framework-dependent 的 `net8.0-windows` 构建，没有增加 RID 或 self-contained 发布。
- 输出目录仍为 `bin/{Configuration}/net8.0-windows/`，解决方案配置仍名为 `Any CPU`，实际 app host 由项目和门禁固定为 AMD64。
- 运行环境从 x86 变为 x64；只安装 x86 Desktop Runtime 的机器需要安装 .NET 8 Windows Desktop Runtime x64。
- 不承诺不同目录中旧 x86 与新 x64 程序之间的单实例转发；升级前应退出旧进程。

## 6. 尚未自动验证

以下项目需要在目标机器上人工回归，当前不能写成已验证：

- 使用真实历史数据库执行查看、保存、恢复、清空和 `VACUUM`；启动冒烟只证明应用可启动和 SQLite 原生库可加载，不等价于完整历史 UI 工作流。
- 第二实例通过 `WM_COPYDATA` 转发文件参数。
- 文件/目录对话框、任务栏进度和列表头右键命中。
- 100%/150% 双屏的副屏首启、跨屏拖动和往返，尤其是组合标签源、歌词源与动态封面。
- 代表性 MP3/FLAC 的实体标签写入和回读。
- 从干净输出制作发布包并扫描 `.pdb`、`temp/`、日志、缓存、备份设置和用户数据。

若上述任一流程出现位数相关异常，应停止发布并同时检查 Win32 ABI、x64 Desktop Runtime 和 SQLite 托管/原生配对。

## 7. 回滚

回滚必须成对恢复：

1. 两个项目的 `PlatformTarget` 恢复为 `x86`。
2. `SQLite.Interop.dll` 恢复为同版本官方 x86 文件。

数据库格式没有变化，无需转换或删除用户数据库。不能只回滚项目位数或只回滚原生 DLL，否则会稳定触发 `BadImageFormatException`。
