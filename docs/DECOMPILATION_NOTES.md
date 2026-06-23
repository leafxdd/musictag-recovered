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

## 暂不展开的 async 状态机

以下文件仍包含 `_003C..._003Ed__*` 形式的 async 状态机结构体。它们与 `[AsyncStateMachine]` 特性和手写 async shell 互相引用，直接改名或重写风险较高：

- `src/MusicTag/MusicTagWinApp.Instances/StateFieldInstance.cs`
- `src/MusicTag/MusicTag.Mocks/CombinedTagSearchDialog.cs`

建议只在需要修复对应功能时逐个处理，例如下载歌词、下载封面、自动匹配标签、保存/撤销标签、批量重命名、删除文件、导出封面等流程。

## 暂不重写的大型反编译控制流

- `src/MusicTag/MusicTagWinApp.Instances/StateFieldInstance.cs` 的 `InitializeComponent`（约 7583 行起）仍包含大量 `switch`/`goto` 形式的反编译控制流。该函数负责主窗体控件创建和事件绑定，改动风险高。
- `src/MusicTag/MusicTag.Importers/OptionsDialog.cs` 的 `InitializeComponent`（约 986 行起）仍是 `goto IL_*` 标签式扁平化控制流。变更日志已说明它被刻意保留（其中含一个不再显示的 iTunes 参数面板），且新增的“联网请求”选项控件是加在手写方法里、未触碰 `InitializeComponent`。改动风险高。
- `src/MusicTag/MusicTag.Schemes/FilenameRelatedBatchDialog.cs`（原 `EventRulesSchema`）的 `InitializeComponent`（约 1202–1880 行，679 行）仍是控制流扁平化残留：`int num = 35; while (true) { switch (num) { ... } }`，把设计器初始化打散到 64 个编号 `case`，通过 47 处 `goto case`、7 处 `num = X; break;`（`break` 后由 `while` 重新分发）以及若干 `case` 直落（fall-through）串接，含 2 个 `return` 出口。早期变更日志（“Inlined the remaining `EventRulesSchema.InitializeComponent` WinForms wrapper batch”）只内联了一层 wrapper 方法，并未拆掉这个分发器。它属于纯静态控制流（`num` 只被赋编译期常量、无数据相关分支），理论上可线性化为顺序代码；但这是 UI 布局代码，冒烟测试只能捕获构造期崩溃、无法验证细微的尺寸/位置回归，应作为独立的高风险任务谨慎处理（注意：冒烟测试会反射构造该对话框，任何破坏其 `InitializeComponent` 的改动都会令冒烟测试失败）。
- `src/MusicTag/MusicTagWinApp.Common/Tokenizer.cs` 仍包含大块编码检测表初始化控制流。该代码影响文件编码识别，建议只在有明确测试样本时局部修改。

## 后续处理原则

- 优先重命名有单一职责、少量调用点、含义能从代码直接证明的类、方法或变量。
- 不确定含义时使用中性名称，或继续保留并在本文档记录。
- 文件改名应单独执行，并确认 `.csproj` 包含项、资源引用和构建输出都没有变化。
- 修改后至少运行 `.\scripts\Verify-Build.ps1 -RunSmokeTests`、`git diff --check` 和 `codegraph sync`。
