# 反编译待确认清单

本文档记录当前仍不适合直接重命名、删除或重构的反编译残留。处理这些项目时应单独开小批次，先确认引用关系和行为，再运行标准验证。

## 已完成的文件改名

以下三个反编译时期的文件已通过 `git mv` 改名以匹配其类型（类型名此前已可读，本次仅改文件路径）：

- `BaseFieldInstance.cs` → `LyricEditorDialog.cs`
- `EventRulesSchema.cs` → `FilenameRelatedBatchDialog.cs`
- `Template.cs` → `CustomToolStripRenderer.cs`

注意：`FilenameRelatedBatchDialog`（原 `EventRulesSchema`）仍通过显式字符串
`new ResourceManager("MusicTag.Schemes.EventRulesSchema", ...)` 加载本地化资源；该字符串对应预编译
（附属）程序集中的资源名，**保持原样，不要随类型/文件名改动**，否则本地化文案会丢失。

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
- 附带发现并已处理：`EncodingDetector` 在全代码库中**从未被实例化**——`Tokenizer.DetectFileEncoding` 走的是 native `ResolveToken`(`MusicTag.dll`)P/Invoke,这套托管打分器是死代码。该未用类(连同 `EncodingNameTables`)已在后续清理批次删除(commit `5964fa9`,见 `MAINTENANCE.md`);`Tokenizer.cs` 现仅保留 `DetectFileEncoding`/`ReadFileSampleBytes` 与 `ResolveToken` P/Invoke 导入,上一条记录的频率表初始化重写因此已成历史(类不再存在)。

## 后续处理原则

- 优先重命名有单一职责、少量调用点、含义能从代码直接证明的类、方法或变量。
- 不确定含义时使用中性名称，或继续保留并在本文档记录。
- 文件改名应单独执行，并确认 `.csproj` 包含项、资源引用和构建输出都没有变化。
- 修改后至少运行 `.\scripts\Verify-Build.ps1 -RunSmokeTests`、`git diff --check` 和 `codegraph sync`。
