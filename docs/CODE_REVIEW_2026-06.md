# 代码库缺陷 Review（2026-06-26）

## 元信息

- 日期：2026-06-26（Claude 原始 Review 基于 commit `7f0d8e4`；Codex 复核时工作区已有 3 处未提交改动，本轮已开始源码修复）
- 范围：`src/MusicTag` 全量（119 文件，约 32k NLOC），.NET Framework 4.8.1 / WinForms
- 性质：**缺陷与可靠性 Review**（bug、资源泄漏、并发、异常处理、编码、解析健壮性）
- Claude 原始 Review **未修改源码**；本修订版记录 Codex 复核纠偏、补漏和本轮修复计划/进展。

### 与既有文档的关系

- `HANDOFF_REVIEW.md`（2026-06-24）是一次**维护性 Review**，聚焦死代码 / 反编译残留 / 命名（iTunes/Xiami 残留、`NoWarn` 等）。本报告与之**互补、不重叠**：本报告聚焦运行期缺陷。
- 本报告记录的问题均为**当前源码的真实缺陷**，与反编译残留无关。

### 方法与验证程度（诚实标注）

数据来源三类，可信度递减：

1. **亲自读源码核实**（最高）：第 1 节崩溃链中的两个 critical/high
   （`AutoMatchTagsDialog.cs:173/725/1767`，已逐行确认裸 `Thread` + `try/finally` 无 catch、`async void` await 无包裹）。
2. **对抗性验证**（独立"怀疑者" agent 复核）：覆盖工作流标为高危的发现。该机制**驳回了 1 条误报**
   （`statefield-1`，见 §9）、**下调 1 条**（`data-history-1` high→medium），故保留项的"机制描述"可信度较高。
3. **单一审查者的详细证据**（行号 + 代码片段 + 推理）：其余工作流发现与 3 个补审 agent 的发现。
   Low 级问题**未逐条二次验证**。

> ⚠️ 本项目**无任何自动化测试**，等价性/正确性历来靠 `scripts\Verify-Build.ps1 -RunSmokeTests` 冒烟验证。
> 因此**任何按本报告所做的修复，都应至少跑一次 `Verify-Build.ps1 -RunSmokeTests` 并人工回归相关流程**。

发现规模：原始约 98 条，去重后约 **62 个独立问题**（Codex 复核后口径：Critical 1 / High 6 / Medium 26 / Low ~30）。

### Codex 复核修订（2026-06-26）

- `dialogs-search-1` 从 High 下调到 Medium：外层 `async void` 无 catch 是真问题，但 QQ/Kugou/网络层已有不少内部异常吞吐，不能概括成“一次畸形响应必崩整个应用”。
- `RunSearch` “全局处理器都拦不到”的表述过满：`Program.cs` 注册了 `Application.ThreadException` / `AppDomain.UnhandledException`，真实风险是进入全局退出链并最终 `Environment.Exit(0)`，日志/提示还可能与进程终止竞争。
- `xcut-exceptions-1` 的触发条件收窄：读取配置异常会创建空文档，只有后续保存成功时才会覆盖用户配置；若写入也被占用/拒绝，则表现为保存异常而非静默覆盖。
- 补漏：`CombinedTagSearchDialog.DownloadCoverAsync` 的 `BeginUpdate()`/`EndUpdate()` 不成对，以及 `PictureFromTagsDialog.StartPictureSearchAsync` 只捕获取消异常。

---

## 执行摘要

**一句话**：代码库的算法与互操作底子扎实，真正的风险高度集中在**一条系统性主线——核心写入路径（自动匹配 / 文件名批处理 / 标签保存）缺乏异常兜底**，常见的"文件被占用 / 只读 / 损坏 / 网络异常"会被逐层放大成整批中断乃至进程崩溃。

**阴性结论（经核实是干净的，未来无需重复怀疑）**：

- 在线标签提供者网络层、`NetEaseCrypto`（AES-CBC/ECB+PKCS7、教科书 RSA）解析与加密正确性 ✅
- Trie / 中文转换 / 相似度算法的下标与边界 ✅
- Win32 / COM 互操作（`IFileDialog`/`IShellItem` 的 QI、HRESULT、CharSet，实测验证）✅
- `ConfigDescriptorState` 的 **`TagLib.File` 句柄释放正确**，无"锁住用户音频文件"的泄漏路径 ✅
- `LimitedConcurrencyTaskScheduler` **并发逻辑正确**，无死锁 / 丢任务 / 计数竞态 ✅

---

## 1. 头号问题：核心写入路径的系统性崩溃链 🔴

**批量标签操作在遇到"文件被播放器占用 / 只读 / 损坏 / 网络异常"这类常见情况时，会崩溃整个应用或中断整批，用户进行中的工作全部丢失。** 根因是三层叠加：

| 层 | 缺陷 | 位置 | 验证 |
|---|---|---|---|
| **写回层（根因）** | `SaveTagFields()`/`SaveCurrentTagFile()` **恒 `return true`**，真正失败由 `tagFile.Save()` 抛异常表达。调用方仍按"返回 false=失败"写代码 → 所有 `if(Save())` 的 `else` 是**死代码**，异常向上逃逸 | `ConfigDescriptorState.cs:428-485`；死分支 `FilenameRelatedBatchDialog.cs:491`、`AutoMatchTagsDialog.cs:862` | 详细证据 |
| **搜索层** | `RunSearch` 跑在**裸 `new Thread`** 上，只有 `try/finally`、**无 catch**。.NET Framework 后台线程未处理异常 → 进入全局异常/退出链并终止进程，`async void` 调用方拦不到 | `AutoMatchTagsDialog.cs:173`，线程启动 `:725` | ✅亲自核实 |
| **调度层** | 各 `StartXxx` 是 **`async void` 且 await 周围无 try/catch**。后台异常 → 全局 `ThreadException` / `UnhandledException` → `Environment.Exit(0)` 崩溃退出，进度框/事务收尾被跳过（事务泄漏） | `AutoMatchTagsDialog.cs:1767`、`FilenameRelatedBatchDialog.cs:1155`、`StateFieldInstance.cs:5785` | ✅部分核实 |

**统一修复方向（P0）**：

1. `SaveTagFields`/`SaveCurrentTagFile` 内部 `try { tagFile.Save(); } catch (Exception ex) { loadError = ex.Message; return false; }`，恢复"失败返回 false"契约；
2. `RunSearch` 整体包 catch，异常转 `worker.loadErrorMessage` + 计入 `failedCount`，保证 `FinishSearch()` 仍执行且异常绝不逃出线程；
3. 每个 `StartXxx` 的 await 包 `try/catch/finally`，finally 中关进度框、`Dispose` 事务、恢复 UI；
4. 两处批处理 worker 的每文件循环补 `catch` + `ReportFailure` + 继续，并把 `historyTransaction.Dispose()` 放进 `finally`。

这一组改动能整体消除"批量操作遇坏文件就崩"的整类问题，收益最大。

**本轮 Codex 已按 P0 方向开始修复**：`ConfigDescriptorState` 保存契约、`AutoMatchTagsDialog.RunSearch/StartAutoMatchTags`、`FilenameRelatedBatchDialog.StartChangeTags/ChangeTags/ConfirmRegexSettings`、`StateFieldInstance.SaveTags/ClearTags`，以及两个补漏的搜索 UI 收尾点。后续仍需人工回归批量改标签 / 自动匹配 / 文件名批处理。

---

## 2. Critical（1）

| id | 问题 | 位置 |
|---|---|---|
| `automatch-critical-1` | 自动匹配并行搜索 `RunSearch` 跑在裸 `new Thread` 上、无 catch；加载损坏音频或联网解析抛异常 → 进入未处理异常/全局退出链并终止进程，`async void` 调用方拦不到。仅在并行 worker（多线程搜索，自动匹配默认配置）路径触发（`:719` 单线程走同步调用）| `AutoMatchTagsDialog.cs:173`（启动 `:725`）|

---

## 3. High（6 —— 确定触发 + 数据损坏/丢失/崩溃）

| id | 问题 | 位置 | 修复 |
|---|---|---|---|
| `config-high-1` | **崩溃链根因**：`SaveTagFields`/`SaveCurrentTagFile` 恒 `return true`、失败抛异常 → 调用方错误分支死代码、异常逃逸、整批中断 + 事务泄漏 | `ConfigDescriptorState.cs:428-485` | 失败时 `return false` |
| `automatch-critical-2` | **崩溃链调度层**：`StartAutoMatchTags` 是 `async void`，`await Task.Run(worker.Run)` 周围无 try/catch；`Run()` 抛 DB/IO 异常 → 崩溃退出 + `CloseAfterCompletion`/`finallyCallback` 跳过 | `AutoMatchTagsDialog.cs:1767` | await 包 try/catch/finally |
| `dialogs-search-6` | 用户非法正则（如 `(`）未预校验直接进后台批处理；`Regex.Match` 抛出后逃出 worker，再穿过 `StartChangeTags async void` 的裸 await → 全局退出 + 历史事务/进度框收尾跳过 | `FilenameRelatedBatchDialog.cs:1023`、`:375`、`:1155` | `ConfirmRegexSettings` 里 `new Regex()` 预校验，worker/await 双层兜底 |
| `text-lyric-2` | 编码探测用 `FileMode.Open`（默认 ReadWrite）→ **只读/被占用的 LRC 回退默认编码 → 中文歌词整篇乱码** | `Tokenizer.cs:31` | 改 `FileAccess.Read, FileShare.ReadWrite` |
| `text-lyric-3` | 负 `[offset:]` 不钳制 → 输出非法时间标签，且 offset 不回写 → **再次解析时该行静默退化为无时间戳，永久损坏歌词** | `LyricTextProcessor.cs:182` | 应用 offset 后 `if(t<0)t=0`（与 `ShiftTimestamps` 一致）|
| `xcut-exceptions-1` | `XmlSettingsProvider` 过宽 catch + 非原子写：读取 `.config` 遇瞬时异常时会创建空配置文档，若后续保存成功 → **静默覆盖用户配置**；若写入同样被拒绝/占用，则表现为保存异常 | `XmlSettingsProvider.cs:64,89-100` | 收窄 catch（仅 `XmlException` 重建）、临时文件 + `File.Replace`、写前备份 `.bak` |

> `config-high-1` 与 `automatch-critical-2` 同属第 1 节崩溃链；`dialogs-search-6` 是同一 `async void` 模式在文件名批处理对话框的实例。

---

## 4. Medium（26 —— 可靠性 / 资源 / 并发，已去重）

| id | 问题 | 位置 |
|---|---|---|
| `statefield-3` / `xcut-resources-1` | 封面预览每次换图不释放旧 `Bitmap`，浏览多文件热路径泄漏 → OOM | `StateFieldInstance.cs:4689` |
| `xcut-resources-2/3` / `config` | `LoadPictureImage` 双重职责：返回值在 3+ 处循环被丢弃（泄漏）、且返回基于**已释放 `MemoryStream`** 的 Image（GDI+ 隐患）| `ConfigDescriptorState.cs:367`；`StateFieldInstance.cs:1342/1355/2231`；`TagHistoryRepository.cs:305` |
| `config-medium` | `LoadCurrentTagFile` 返回 `using` 已 Dispose 的 `ConfigDescriptorState`（**use-after-dispose**，目前靠"Dispose 只置空 tagFile、缓存还在"巧合不崩）| `AutoMatchTagsDialog.cs:826` |
| `listview-controls-1` / `services-misc-3` | 静态初始化器里无保护 `JsonConvert.Deserialize`，损坏配置 → `TypeInitializationException` → 核心列设置/合并搜索路径**永久崩溃无法自愈** | `CustomColumnsDialog.cs:45,290`；`CombinedTagOverwriteOptionsDialog.cs:45` |
| `data-history-1` | `TagHistoryRepository` 事务无回滚 + `ExecuteReader` 失败把 static 共享连接置 null → 级联失败 + 原始异常被掩盖（验证后 high→medium）| `TagHistoryRepository.cs:76-171` |
| `data-history-2` | static 共享连接/序列号/`UndoTags` 无任何同步，仅靠"UI 串行 await"这一**未强制**的不变量 | `TagHistoryRepository.cs:55,173,345` |
| `config-medium` | `AppSettingData` 用 `BinaryFormatter` + `FileMode.Create` 非原子保存，`SaveSettings` 整体无 try/catch | `StateFieldInstance.cs:2338` |
| `options-1` | 持久化整数直接赋给有界 `TrackBar.Value`，越界（0/负/>100）→ **选项对话框打不开** | `OptionsDialog.cs:459,649` |
| `options-2` | 受限扩展名含重复项 → `Dictionary.Add` 抛 `ArgumentException` → 保存崩溃（输入 `.mp3;.mp3` 即触发）| `OptionsDialog.cs:719` |
| `automatch-medium-1` | 封面临时文件租约引用计数泄漏（最佳封面下载失败、备选成功时，最佳租约永不释放）| `AutoMatchTagsDialog.cs:480,1086` |
| `automatch-medium-2` | `UpdateProgress` 对 `activeFilePaths` 的 TOCTOU → `.First()` 抛 `InvalidOperationException` | `AutoMatchTagsDialog.cs:1436` |
| `statefield-4` | 14 个 `async void StartXxx` 在 await 后收尾无 finally，异常时进度框残留、列表不刷新 | `StateFieldInstance.cs:4796,5360,5785…` |
| `statefield-2` | 后台线程读 `filterTextBox.Text`（跨线程控件访问）| `StateFieldInstance.cs:2348` |
| `dialogs-search-1` | `CombinedTagSearchDialog.SearchCombinedTagsAsync` 外层 `async void` 无 catch/finally；普通网络/解析失败多由 provider 内部吞吐，但仍存在未覆盖异常导致全局退出、进度图标/任务栏状态不复位的风险 | `CombinedTagSearchDialog.cs:801` |
| `dialogs-search-7` | `CombinedTagSearchDialog.DownloadCoverAsync` 在 `searchResultsListView.BeginUpdate()` 后才进入复杂 UI 更新，异常路径不调用 `EndUpdate()` → ListView 刷新状态可能被永久挂住 | `CombinedTagSearchDialog.cs:634` |
| `dialogs-search-8` | `PictureFromTagsDialog.StartPictureSearchAsync` 只捕获 `OperationCanceledException`；嵌入封面读取/解码的非取消异常会逃出 `async void` 进入全局退出路径 | `PictureFromTagsDialog.cs:164` |
| `dialogs-search-4` | `PictureFromTagsDialog` 列表索引与重新取图计数口径不一致 → **选/导出到错误封面** | `PictureFromTagsDialog.cs:142`（已修：列表项保留原始封面索引） |
| `dialogs-search-2` | `LyricSearchDialog.StartLyricSearch` 同为 async void 无 catch + 缺 `IsDisposed` 判断 | `LyricSearchDialog.cs:454`（已修：捕获取消/异常并在 `finally` 收尾 UI） |
| `win32-shell-2` | `WM_COPYDATA` 的 `cbData` 少 1 字节（`len*2+1` 应为 `+2`）→ 跨进程传参尾部可能读到垃圾 | `Program.cs:127`（已修：按 UTF-16 字节数包含终止符） |
| `win32-shell-3` | `ITaskbarList` 从未 `HrInit()` → 部分系统任务栏进度静默不显示 | `TaskbarProgressController.cs:14`（已修：构造时调用 `HrInit()`） |
| `win32-shell-1` / `xcut-concurrency-4` | `AppDomain.UnhandledException` 处理器 `async void` + `await Task.Yield` 与进程终止竞争 → 崩溃日志/提示可能丢失 | `Program.cs:147` |
| `services-misc-1` / `xcut-concurrency-5` | `ProgressDialog` 计时器后台线程 check-then-Invoke 竞态 + 构造期即 Start（句柄未建）| `ProgressDialog.cs:45,193` |

> 标 `/` 的条目为工作流横切扫描与模块审查**独立两次命中**的同一问题，可信度更高。

---

## 5. Low（约 30 条，按类别汇总）

- **GDI / 句柄泄漏（确定但量小/低频）**：`GetSmallFileIcon` 的 HICON 永不 `DestroyIcon`（`DatabaseMapper.cs:456`，三处命中）、`LoadResourceBitmap` 的 `Graphics` 未释放（`DatabaseMapper.cs:664`）、多处对话框 `ImageList`（`CustomColumnsDialog.cs:90`、`CharacterSetSelectionDialog.cs:46`、`DirectoryManagerDialog.cs:46`）、`Icon`（`AboutDialog.cs:191`）、按钮位图（`SourceOrderControl.cs:75`、`LyricEditorDialog.cs:142/145`）、歌词搜索对话框（`LyricEditorDialog.cs:259`）、Shell COM 从不 `ReleaseComObject`（`FolderSelectionDialog.cs`、`LyricSaveFileDialog.cs`）、`PictureFromTagsDialog` 重复哈希图未释放（`:145`）。
- **文化相关（非中文区）**：年份 `ToString("yyyy")` → 佛历/回历错年份（`NetEaseMusicTagProvider.cs:337`、`QqMusicTagProvider.cs:232`，已修为 invariant culture）、`ToUpper()` → 土耳其语非法编码名（`TagTextEncoding.cs:109`，已修为 `ToUpperInvariant()`）、查找替换用 `CurrentCulture` 比较（`TextBoxFindReplaceController.cs:30`，已修为 ordinal 比较）。
- **解析强转**：`long.Parse(SourceTrackId)`（`QqMusicTagProvider.cs:300`、`NetEaseMusicTagProvider.cs:430`，已修为 `TryParse`）、`tagState["durationinms"]` 缺键 `KeyNotFoundException`（`TrackSearchContext.cs:39`，已修为 `GetDisplayValue`）、单位数小数秒偏小 10 倍（`LyricTextProcessor.cs:228`，已修为按位数补齐到毫秒）、HTML 实体只解码 5 个、数字引用 `&#39;` 残留（`TextEncodingService.cs:7`，已改用 `HttpUtility.HtmlDecode`）、酷狗 `?? ""` 死防御兼潜在 NRE（`KugouTagProvider.cs:322`，已修空值防御）。
- **异常静默（WinForms 无控制台，`Console.WriteLine` 日志全不可见）**：`DecodeBase64String` 失败返回原文污染歌词（`DatabaseMapper.cs:82`，已修为返回空字符串并跳过无效歌词字段）、`ImportLrcText` 显式导入失败静默 null（`LyricEditorDialog.cs:413`，已修为 UI 层捕获并弹出错误框）、`ReadSettingValue` 用 NRE 当控制流（`XmlSettingsProvider.cs:115`，已修为显式判空后回默认值）。
- **并发 / 性能小问题**：`SourceOrderControl` 500ms 空轮询（`:289`）、每单元格 `new SolidBrush`（`EditableListView.cs:502`）、UI 线程 `task.Wait()`（`StateFieldInstance.cs:4181`）、批次结束 `GC.Collect()` 卡顿（`AutoMatchTagsDialog.cs:1790`）、相似度/歌词热路径 `new Regex`（`TrackSearchResult.cs:503`、`LyricTextProcessor.cs:216`、`FilenameRelatedBatchDialog.cs:444`）、`ApplicationInfoService` 后台线程弹 `MessageBox` 并写 `Settings`（`:20`）、`GetScheduledTasks` 返回活引用（`LimitedConcurrencyTaskScheduler.cs:77`）。
- **正确性小问题**：仅大小写改名被误判为冲突（`FilenameRelatedBatchDialog.cs:243`）、`PictureFromTagsDialog` 未选中点 OK 返回 null（`:241`）、内联重命名把用户输入直拼路径可越目录（`StateFieldInstance.cs:6681`）、`SetId3v2Version` 全局静态不复位（`ConfigDescriptorState.cs:1045`）、撤销字节计数在卸载后仍累加（`TagHistoryRepository.cs:351`）。

---

## 6. 系统性主题（比单条更重要）

1. **`async void` / 裸 `Thread` 无异常兜底**（最普遍）：贯穿搜索、批处理、退出保存。后台异常 → 崩溃退出或进度框残留。是第 1 节崩溃链与多条 High/Medium 的共同根。
2. **GDI+ / 句柄热路径泄漏**：封面 `Bitmap`、`LoadPictureImage` 返回值、HICON、`Graphics`、`ImageList`、`Icon` 等未确定性释放；热路径（封面预览、文件图标）会累积到 OOM / GDI 上限。
3. **配置/歌词的静默损坏与丢失**：`XmlSettingsProvider` 覆盖配置、负 offset 损坏歌词、`DecodeBase64String` 污染歌词、`AppSettingData` 非原子保存。
4. **静态初始化器中无保护的反序列化**：损坏配置 → `TypeInitializationException` → 核心路径永久崩溃无法自愈。
5. **文化相关解析/格式化（CurrentCulture vs Invariant）**：`double.Parse`、`ToString("yyyy")`、`ToUpper`、查找替换比较——影响非中文区域用户。
6. **无同步的 static 可变状态**：`TagHistoryRepository`、`KuwoTagProvider` 退避——当前靠"UI 串行"这一未强制的不变量。
7. **`Console.WriteLine` 当日志**：本应用是 WinForms GUI、无控制台，所有此类"日志"生产中不可见，使每个仅靠 Console 记录的 catch 在排障层面近似静默。建议整体改为可见的日志文件。

---

## 7. 项目 / 构建层面

| 观察 | 影响 |
|---|---|
| **零自动化测试**，等价性仅靠 `Verify-Build.ps1` 冒烟 | 最大结构性风险；每次反编译重写的"行为等价"无回归网 |
| **未启用可空引用类型**（`LangVersion latest` 却无 `<Nullable>`）| 大量网络 JSON 解析 + 控件访问，NRE 全靠人工防御——解释了 NRE 类高频 |
| `CheckForOverflowUnderflow=False` | 数值路径（LRC 偏移）溢出静默回绕 |
| `NoWarn` 已从 `CS0162;CS0414;CS0649` 收敛到只剩 `CS0649` | `HANDOFF_REVIEW.md` 的建议已部分采纳 |

---

## 8. 与未提交 diff 的关系（commit `7f0d8e4` 之上）

当前 3 处改动（Kugou 正则缓存、QQ 合并 `Parse`、`RemoteTagProviderBase` ASCII→UTF8）**验证无回归**。两点提示：

- ASCII→UTF8 这处**当前并未修复真实乱码**（非 JSON 分支唯一调用方 NetEase 的 body 已是纯 ASCII，UTF8 与 ASCII 产生相同字节），是合理的防御性改动；
- 同一"内联 `new Regex` 外提"主题下**漏了 3 处更高频的点**：`LyricTextProcessor.cs:216`、`TrackSearchResult.cs:503`、`FilenameRelatedBatchDialog.cs:444`，建议顺手处理。

---

## 9. 已排除 / 阴性结论（避免未来重复怀疑）

- **`statefield-1`（误报，已驳回）**：曾被报为"退出保存失败 → 模态进度框永不关闭 → 应用挂死、只能强杀"。对抗验证读源码确认：`Program.cs:147-164` 注册了全局 `Application.ThreadException` + `Environment.Exit(0)`，真实行为是**弹错误框后退出进程**，不存在"挂死必须强杀"。其遗留的真实问题（`StartSaveAppSettingData` 缺 try/finally、`FileMode.Create` 半截写）已并入 `statefield-4` / `AppSettingData` 条目。
- `TagLib.File` 句柄释放、`LimitedConcurrencyTaskScheduler` 并发、网络层解析、`NetEaseCrypto`、Trie/中文/相似度算法、Win32/COM 互操作——均经审查/实测确认正确（见执行摘要"阴性结论"）。
- 大量子项/列头类（`CheckBoxSubItem`、`ImageSubItem`、`MultiValueListViewItem`、`ImageComboBox`、`EditableColumnHeader` 等）经可达性核实**从未被实例化**，其内部隐患属死代码，未计入。

---

## 10. 建议修复路线图

- **P0（核心功能稳健性，已完成）**：第 1 节崩溃链三处 —— `SaveTagFields` 契约 + `RunSearch` catch + `StartXxx` try/catch/finally。一组改动消除"批量操作遇坏文件就崩"的整类问题。
- **P1（数据安全，已完成主要项）**：歌词编码/offset、配置覆盖原子写、静态反序列化崩溃、选项边界、退出状态原子写、历史库失败回滚已修。
- **P2（资源/体验）**：封面/嵌入图片热路径泄漏、非中文区歌词时间轴、About/Shell 图标句柄释放、封面预览图释放、资源位图缩放释放、对话框列表图像句柄释放、按钮图像/歌词搜索对话框释放、清空历史错误可见性已修；仍剩更零散的 Low 级资源释放。

> 每步修复后运行：`scripts\Verify-Build.ps1 -RunSmokeTests`，并人工回归对应流程（批量改标签 / 自动匹配 / 歌词下载 / 配置保存）。

### 本轮修复计划 / 状态

| 阶段 | 状态 | 范围 |
|---|---|---|
| P0-1 保存契约 | 已实现 | `ConfigDescriptorState.SaveTagFields` / `SaveCurrentTagFile` 保存失败返回 `false` 并写入 `loadError` |
| P0-2 自动匹配异常边界 | 已实现 | `RunSearch` 捕获单文件异常；`StartAutoMatchTags` 用 `try/catch/finally` 收尾进度框和回调 |
| P0-3 批处理异常边界 | 已实现 | `StateFieldInstance.SaveTags/ClearTags`、`FilenameRelatedBatchDialog.ChangeTags/StartChangeTags` 补 per-file catch 和事务 finally |
| P0-4 搜索 UI 补漏 | 已实现 | `CombinedTagSearchDialog.SearchCombinedTagsAsync/DownloadCoverAsync`、`PictureFromTagsDialog.StartPictureSearchAsync` 补 catch/finally |
| P1-1 歌词/配置数据安全 | 已实现 | `Tokenizer` 共享只读读取 LRC 样本；`LyricTextProcessor` offset 夹到 0；`XmlSettingsProvider` 仅 XML 损坏时重建并用临时文件替换保存 |
| P1-2 配置反序列化和选项边界 | 已实现 | 列设置/合并覆盖选项 JSON 损坏时回退；搜索限制 trackbar 夹值；受限扩展名 trim/小写/去重 |
| P1-3 退出状态数据安全 | 已实现 | `AppSettingsSaveTask` 写 `.tmp` 后替换 `.dat`，序列化/写入失败时保留旧数据 |
| P1-4 历史库事务 | 已实现 | SQLite 命令绑定事务；SQL/undo 失败时事务回滚；清空历史先事务提交再做非致命 `VACUUM` |
| P2-1 图片热路径资源释放 | 已实现 | `ImageList.Images.Add` 后释放源 `Bitmap/Image`；取消嵌入图片搜索时释放未进入 UI 的候选图 |
| P2-2 文化无关歌词时间轴 | 已实现 | LRC offset/时间戳解析与输出使用 invariant culture；Kuwo 歌词秒数按 invariant 小数解析 |
| P2-3 Low 级 GDI 释放 | 已实现 | `AboutDialog` 使用 `Icon.ToBitmap()` 后释放源 `Icon`；Shell 文件图标 clone 后释放原 HICON 和 clone；替换封面预览和提取封面时释放临时 `Image`；资源位图缩放后释放 `Graphics`；对话框 `SmallImageList` 挂入组件容器释放；源顺序/歌词编辑按钮图像随控件释放；歌词搜索对话框随用随释放 |
| P2-4 历史库错误可见性 | 已实现 | 清空历史失败时返回具体异常链并用错误框展示 |
| P2-5 嵌入封面选择正确性 | 已实现 | `PictureFromTagsDialog` 去重显示时保留原始封面索引，避免选中/导出错图 |
| P2-6 编码名文化无关规范化 | 已实现 | 默认编码名使用 `ToUpperInvariant()`，避免土耳其语等区域设置下生成非法编码名 |
| P2-7 跨进程参数转发长度 | 已实现 | `WM_COPYDATA` 的 `cbData` 使用包含 `\0` 的 UTF-16 字节数，避免尾部截断/垃圾 |
| P2-8 任务栏进度初始化 | 已实现 | `TaskbarProgressController` 创建 `ITaskbarList4` 后调用 `HrInit()` |
| P2-9 歌词搜索异常边界 | 已实现 | `LyricSearchDialog.StartLyricSearch` 捕获取消/异常并保证进度图和任务栏状态复位 |
| P2-10 提供者年份格式化 | 已实现 | 网易/QQ 提供者年份输出使用 invariant culture，避免非公历区域年份错误 |
| P2-11 查找替换文化无关比较 | 已实现 | 歌词编辑器查找/替换改为 ordinal 比较，避免区域设置影响匹配 |
| P2-12 提供者歌词 TrackId 解析 | 已实现 | 网易/QQ 按 TrackId 重新加载歌词时使用 `TryParse`，避免旧数据/异常 ID 触发崩溃 |
| P2-13 搜索上下文时长缺失防御 | 已实现 | `TrackSearchContext` 读取 `durationinms` 使用 `GetDisplayValue`，缺键时不再抛异常 |
| P2-14 歌词小数秒解析 | 已实现 | 1/2/3 位小数秒按毫秒位数补齐，避免 `[00:01.5]` 被解析成 50ms |
| P2-15 HTML 实体解码 | 已实现 | `TextEncodingService` 使用 `HttpUtility.HtmlDecode`，支持数字实体和完整命名实体 |
| P2-16 酷狗歌词关键词空值防御 | 已实现 | `KugouTagProvider` 构造歌词关键词时先处理空 artist/title，避免 `Trim()` 空引用 |
| P2-17 查找替换快捷键处理 | 已实现 | `Ctrl+Z` 只执行一次 `Undo()`，并对已处理的 `Ctrl+A`/`Ctrl+Z`/`F2`/`F3` 设置 `SuppressKeyPress` |
| P2-18 网易专辑请求资源释放 | 已实现 | `LoadAlbumDetails` 为临时 album `HttpClient` 建立 `using` 生命周期，避免每次请求泄漏 |
| P2-19 QQ 歌词 Base64 解码失败处理 | 已实现 | `DecodeBase64String` 解码失败返回空字符串，避免把无效 base64 payload 写入歌词正文 |
| P2-20 LRC 导入失败可见性 | 已实现 | 显式导入 LRC 时不再在 `ImportLrcText` 吞异常，点击处理器捕获后用错误框展示失败原因 |
| P2-21 设置读取缺失节点处理 | 已实现 | `XmlSettingsProvider.ReadSettingValue` 显式处理缺失 XML 节点，避免用空引用异常作为默认值路径 |
| P2-22 设置保存失败可见性 | 已实现 | `Settings.Default.Save()` 统一经 `DatabaseMapper.TrySaveApplicationSettings`，只读安装目录等保存失败不再走全局崩溃链；确认型对话框保存失败时停留原窗口 |
| P2 后续 | 待办 | 更零散的 Low 级资源释放问题 |
