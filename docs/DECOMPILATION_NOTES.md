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

## 暂不删除的空壳或生成类

- `src/MusicTag/MusicTagWinApp.Exporters/PolicyTokenExporter.cs`：当前是空静态类。名称同时出现在资源键 `MusicTagWinApp.Exporters.PolicyTokenExporter` 中，并被错误/提示窗口标题间接使用。是否删除或改名需要先确认资源兼容性。
- `src/MusicTag/PrivateImplementationDetails.cs`：编译器生成的静态数据容器。虽然名称不可读，但通常承载反编译出的数组或常量数据，不应手工改名或删除。

## 已重写的 async 状态机

项目中原有的全部 `_003C..._003Ed__*` async 状态机结构体已全部重写为手写 `async`/`async void` 方法（详见 `MAINTENANCE.md` 变更日志）：

- `src/MusicTag/MusicTagWinApp.Instances/StateFieldInstance.cs`（原 12 个：刷新列表、下载歌词、查询年份、批量重命名/保存标签/清除标签/删除文件/保存 LRC/导出封面、撤销保存标签/撤销重命名、保存设置）
- `src/MusicTag/MusicTag.Mocks/CombinedTagSearchDialog.cs`（原 3 个：延迟下载歌词、延迟下载封面、联合搜索）

对全代码库 grep `: IAsyncStateMachine` / `[AsyncStateMachine` 现已无任何匹配。每次重写都是行为等价的“编译器逆操作”，并以 Debug+Release 构建 0/0 + 冒烟测试验证；但这些文件写入/搜索路径本身没有自动化测试覆盖（项目无此类测试），等价性依据是各条变更日志中记录的逆向分析，而非实际运行。

## 暂不重写的大型反编译控制流

- `src/MusicTag/MusicTagWinApp.Common/Tokenizer.cs` 仍包含大块编码检测表初始化控制流。该代码影响文件编码识别，建议只在有明确测试样本时局部修改。

## 后续处理原则

- 优先重命名有单一职责、少量调用点、含义能从代码直接证明的类、方法或变量。
- 不确定含义时使用中性名称，或继续保留并在本文档记录。
- 文件改名应单独执行，并确认 `.csproj` 包含项、资源引用和构建输出都没有变化。
- 修改后至少运行 `.\scripts\Verify-Build.ps1 -RunSmokeTests`、`git diff --check` 和 `codegraph sync`。
