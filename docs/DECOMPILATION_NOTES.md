# 反编译待确认清单

本文档记录当前仍不适合直接重命名、删除或重构的反编译残留。处理这些项目时应单独开小批次，先确认引用关系和行为，再运行标准验证。

## 已完成的文件改名

以下三个反编译时期的文件已通过 `git mv` 改名以匹配其类型（类型名此前已可读，本次仅改文件路径）：

- `BaseFieldInstance.cs` → `LyricEditorDialog.cs`
- `EventRulesSchema.cs` → `FilenameRelatedBatchDialog.cs`
- `Template.cs` → `CustomToolStripRenderer.cs`（此条仅记改名历史；该类未被使用，后于 commit `5964fa9` 作为死代码删除，见 `MAINTENANCE.md`）

注意：`FilenameRelatedBatchDialog`（原 `EventRulesSchema`）仍通过显式字符串
`new ResourceManager("MusicTag.Schemes.EventRulesSchema", ...)` 加载本地化资源；该字符串对应预编译
（附属）程序集中的资源名，**保持原样，不要随类型/文件名改动**，否则本地化文案会丢失。

同理：`OptionsDialog`（原 `MusicTag.Importers.WorkerComparatorImporter`）的本地化文案存放在附属程序集的
`MusicTag.Importers.WorkerComparatorImporter.<culture>.resources` 资源集中，故其通过
`new ResourceManager("MusicTag.Importers.WorkerComparatorImporter", ...)` 读取（**此字符串保持原样**）。
此前的恢复代码误将其指向 `typeof(StateFieldInstance)` 的资源集——该资源集不含这些键，于是 `GetDialogText`
全部回退到代码内的 fallback 文案（旧控件 fallback 是英文，故"标签源/歌词下载/杂项1/杂项2/图片源…"等显示为英文），
本地化看似"丢失"。已于 commit 修正指向。配套新增**有意为空**的中性 resx
`src/MusicTag/MusicTag.Importers.WorkerComparatorImporter.resx`：附属程序集只覆盖各语言,主程序集需有同名中性资源集
作为 fallback 终点,否则对附属程序集中**缺失**的键（恢复期新增的 `Network`/`gbNetworkOptions`/QQ-Cookie/UA 等控件）
`ResourceManager.GetString` 会抛 `MissingManifestResourceException`;有了它,缺失键返回 null,代码内中文 fallback 生效。

同理:`LyricEditorDialog`（原 `BaseFieldInstance`）歌词编辑器右键菜单 6 个菜单项的本地化文案,
存放在附属程序集的 `MusicTagWinApp.Instances.BaseFieldInstance.<culture>.resources` 资源集中,故
`InitializeLocalizedText` 通过 `new ResourceManager("MusicTagWinApp.Instances.BaseFieldInstance", ...)`
读取(**此字符串保持原样,不要随类型/文件名改写成 LyricEditorDialog**)。此前恢复代码误用
`new ComponentResourceManager(typeof(LyricEditorDialog))` 推导基名,主程序集与各附属程序集都无该资源集
→ 一打开歌词编辑器就 `MissingManifestResourceException` 闪退,已修正基名。配套新增中性 resx
`src/MusicTag/MusicTagWinApp.Instances.BaseFieldInstance.resx`——与上面 OptionsDialog 那个**有意为空**的
不同,这里**填入 6 个键的英文中性值**:因为这 6 句是 `menuItem.Text = GetString(key)` 直接赋值、代码内
无 fallback,中性集若为空,在缺少对应附属程序集的区域性(invariant 等)下 `GetString` 返回 null 会使菜单
文字变空白;填入英文后 zh-CHS/zh-CHT/en 走附属翻译、其余区域性回退英文,均不再崩溃。这 6 句原始文案系从
随附附属资源 DLL 中读回。

## 已删除的空壳/生成类

以下两个反编译空壳/生成类经确认无活动引用后已删除（详见 `MAINTENANCE.md`，commit `5964fa9`）：

- `PolicyTokenExporter`：原为空静态类。其在资源系统中只是字符串键 `MusicTagWinApp.Exporters.PolicyTokenExporter`（经 `ResourceManager.GetString` 查找，用作错误/提示窗口标题），与这个空 C# 类彼此独立——删除类不影响资源查找，已由构建 + 冒烟验证。
- `PrivateImplementationDetails`：反编译器留下的生成数据容器空壳，全代码库无引用（删除后 Debug+Release 仍 0/0，证明没有 `InitializeArray` 之类的 IL 依赖它）。

## 已重写的 async 状态机

项目中原有的全部 `_003C..._003Ed__*` async 状态机结构体已全部重写为手写 `async`/`async void` 方法（详见 `MAINTENANCE.md` 变更日志）：

- `src/MusicTag/MusicTagWinApp.Instances/StateFieldInstance.cs`（原 12 个：刷新列表、下载歌词、查询年份、批量重命名/保存标签/清除标签/删除文件/保存 LRC/导出封面、撤销保存标签/撤销重命名、保存设置）
- `src/MusicTag/MusicTag.Mocks/CombinedTagSearchDialog.cs`（原 3 个：延迟下载歌词、延迟下载封面、联合搜索）

对全代码库 grep `: IAsyncStateMachine` / `[AsyncStateMachine` 现已无任何匹配。每次重写都是行为等价的“编译器逆操作”，并以 Debug+Release 构建 0/0 + 冒烟测试验证；但这些文件写入/搜索路径本身没有自动化测试覆盖（项目无此类测试），等价性依据是各条变更日志中记录的逆向分析，而非实际运行。

## 已重写的大型反编译控制流

- `src/MusicTag/MusicTagWinApp.Common/Tokenizer.cs` 的编码检测频率表初始化(`EncodingDetector.InitializeFrequencyTables`)原为约 4480 行的 `goto`/`switch` 混淆状态机,已重写为数据驱动的小型加载器(7 个 `static readonly int[]` 三元组表 + 一个 `LoadFrequencyTable` 回放循环),并删除了仅服务于它的 `PostRole`/`InvokeRole`/`DestroyRole` 桩(它们分别恒为 `true`/`false`/单元赋值,使原控制流完全静态)。等价性以**逐字节方式验证**:用反射 dump 原始构建中 7 张表的全部非零单元(共 3301 个),重写后重新 dump 并对 SHA256,结果完全一致;未改动的 `Score*Encoding` 只读这些表,故检测结果不变。验证脚本 `artifacts/Dump-TokenizerTables.ps1`、`artifacts/Generate-TokenizerRegion.ps1` 位于 gitignored 的 `artifacts/`。
- 附带发现并已处理：`EncodingDetector` 在全代码库中**从未被实例化**——`Tokenizer.DetectFileEncoding` 走的是 native `ResolveToken`(`MusicTag.dll`)P/Invoke,这套托管打分器是死代码。该未用类(连同 `EncodingNameTables`)已在后续清理批次删除(commit `5964fa9`,见 `MAINTENANCE.md`);`Tokenizer.cs` 当时仅余 `DetectFileEncoding`/`ReadFileSampleBytes` 与 `ResolveToken`(`de`)P/Invoke 导入,上一条记录的频率表初始化重写因此已成历史(类不再存在)。**阶段 B(PB3)后** `ResolveToken`(`de`)P/Invoke 亦被移除,`DetectFileEncoding` 改走托管 charset 库 **UtfUnknown**(见 `NATIVE_DEPENDENCY_REMOVAL_PLAN.md` §13);现 `Tokenizer.cs` 不含任何 native 导入。

## 原生 MusicTag.dll 的反篡改自校验门(已不再相关 — 原生 DLL 已整体删除)

> **现状(阶段 A + B 均完成)**:标签读写已整体迁移到托管 **TagLibSharp**(见
> `MusicTag.States/ConfigDescriptorState.cs` 与 `NATIVE_DEPENDENCY_REMOVAL_PLAN.md` §12),
> 在线加解密/编码检测亦已托管化(§13),**整个项目不再 P/Invoke 任何 `MusicTag.dll` 导出**。
> 阶段 A 收尾时此前的 3 字节补丁已**移除**、DLL 还原为原版;阶段 B(PB4)进一步把
> `musictag/MusicTag.dll` 与 `MediaInfo.dll` 一并**从仓库删除**。因此**这道门对本项目已彻底无影响**。
> 下文保留对该门的二进制分析作为知识底稿——原始 DLL 仅存于 git 历史与 `artifacts/MusicTag.dll.orig`,
> 仅当将来**回退阶段 A/B、重新引入并经 native 读写标签**时才再相关。

native `musictag/MusicTag.dll`(TagLib 封装,`MainT` 命名空间)内置一道反篡改自校验,
曾是“重编译后软件读不到 MP3 标签”的根因(历史 commit `a9907e2`):

- DLL 在首次标签操作时,用 `GetModuleFileNameW(NULL, …)` 取**宿主 EXE**(即 `MusicTag.exe`)的磁盘路径,
  `CreateFileW` + `ReadFile` 读其**全部文件内容**,经一张半字节 CRC 表(rdata `0x100D9DF0`)算出 **CRC-16**,
  与常量 **`0x69EB`** 比较:相等则把全局放行标志 `ds:[0x100F5D68]` 置 **2(启用)**,否则置 **1(禁用)**。
  `0x69EB` 是**原版发行的 `MusicTag.exe`** 的内容校验和。
- 所有**标签字段读/写**与**内嵌封面**导出/写入函数(导出名 `ee`/`d`/`g`/`gg`/`n`/`v`/`m1`/`m2`)开头都检查
  `[0x100F5D68] == 2`,否则直接返回空/不写。音频属性(`h`/`i`/`j`/`k`/`l`)、标签类型摘要(`f`)、
  MediaInfo 路径**不设门**。
- 后果(历史):本仓库**从源码重编译**出的 `MusicTag.exe` 内容必然不同 → CRC ≠ 0x69EB → 标志置 1 →
  标签/封面读写被**静默禁用**。表现为文件仍能列出格式/时长(走 MediaInfo),但 title/artist/album/year/封面全空、
  保存标签无效。managed 层无法修复(C# 改不了自身宿主 EXE 的 CRC)——这正是当初打补丁的原因。

**历史补丁(已移除,commit `a9907e2` 引入、阶段 A 收尾时回退)**:曾在校验门的赋值处中和判定使标志恒为 2。
`musictag/MusicTag.dll` 内**唯一**匹配 9 字节模式 `0F 94 C0 40 A3 68 5D 0F 10`
(= `sete al; inc eax; mov ds:[0x100F5D68],eax`;其前 4 字节为比较常量 `EB 69 00 00`),位于**文件偏移 208497**;
补丁把前 3 字节 `0F 94 C0`(`sete al`)改为 `B0 01 90`(`mov al,1; nop`),使 `inc eax` 后恒为 2、无条件放行。
**阶段 A 把标签 I/O 迁到 TagLibSharp 后,本项目不再调用任何被门 gate 的导出,补丁失去意义**,故已将
`musictag/MusicTag.dll` 还原为未打补丁原版(原始字节亦备份于 gitignored `artifacts/MusicTag.dll.orig`);
**阶段 B(PB4)随后把该 DLL 整体从仓库删除**,原版仅存于 git 历史与该备份。

**⚠️ 仅在回退阶段 A/B 时才需重打**:只要标签读写继续走托管 TagLibSharp,就**不需要**这个补丁。
由于阶段 B 已把 `MusicTag.dll` 从仓库删除,万一将来回退、改回经 native 读写标签,需先从 git 历史或
`artifacts/MusicTag.dll.orig` 取回原版 DLL,再按需重打:搜唯一模式
`0F 94 C0 40 A3 68 5D 0F 10`,将起始 3 字节改为 `B0 01 90`(偏移随 DLL 版本可能变,以模式搜索为准),
不触碰任何 `EntryPoint`/ABI。

验证(还原原版 DLL 后):`Verify-Build.ps1 -RunSmokeTests` 绿;阶段 A 黄金对拍(TagLibSharp 读写 vs native
oracle 回读)逐字段一致(见 `NATIVE_DEPENDENCY_REMOVAL_PLAN.md` §12)。

## 后续处理原则

- 优先重命名有单一职责、少量调用点、含义能从代码直接证明的类、方法或变量。
- 不确定含义时使用中性名称，或继续保留并在本文档记录。
- 文件改名应单独执行，并确认 `.csproj` 包含项、资源引用和构建输出都没有变化。
- 修改后至少运行 `.\scripts\Verify-Build.ps1 -RunSmokeTests`、`git diff --check` 和 `codegraph sync`。
