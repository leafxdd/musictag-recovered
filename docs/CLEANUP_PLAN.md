# 死代码/残留清理计划与进度

> 本文档是本轮清理的**单一事实来源**,用于跨上下文压缩续作。每完成一批,勾选 checkbox 并在「进度日志」追加一行（只记真实已发生的事,含真实 commit 哈希）。

日期起：2026-06-24 · 分支：`master`（本地仓库,无 remote）· 验证：`.\scripts\Verify-Build.ps1 -RunSmokeTests`

## 背景

两轮独立 review 的合并结果:
- **Claude 这轮**：6 路并行 finder + CodeGraph + grep 复核,挖代码级死代码/逻辑/命名。
- **Codex 这轮**（`HANDOFF_REVIEW.md`）：功能退役完整性 + 构建告警 + 文档时效。
- 两者互补,无冲突。关键澄清：Codex 用 `/p:NoWarn=` 得 0 warning,只能证明**编译器可见**的窄类别(CS0162/0414/0649)无残留;public 字段/extern/运行期不可达分支全是其盲区 —— 故 “0 warning ≠ 无死代码”。

## 行为保留铁律（每批都遵守）

- 反编译恢复项目,**行为等价**优先,以 Debug+Release 0/0 + 3 个 smoke test 验证。
- 不动：native `EntryPoint` 字符串、`SearchSource` 显式序数与注释、`StateFieldInstance`/`ConfigDescriptorState`/命名空间/文件夹名、持久化 JSON 键(`SourceItem` 的 Src/Seq/IsOther/WebSearchItemsLimit)、`FilenameRelatedBatchDialog` 的 `"MusicTag.Schemes.EventRulesSchema"` 资源名。
- 不动：用户配置与出厂 `MusicTag.config` 里已删源的 `Src:5/6/7/8` 条目(被 `ApplySavedSourceSettings` 忽略,保守政策)。
- 不动：`lyricTextComboBox=null`(变更日志记录为有意保留)、catch 里有意的 resilience 吞异常。

## 批次清单

- [x] **批次 1 — 孤儿死成员（纯减法,零行为风险）** ✅ 完成
  - [x] `NativeMethods.cs` 删 `WindowPlacement` 三件套:`struct WindowPlacement`(~78)+ `SetWindowPlacement`(~112)+ `GetWindowPlacement`(~116)。全库 grep 仅此处互引,外部零引用。
  - [x] 删两个死枚举文件:`MusicTagWinApp.Win32.FileDialog/FileDialogEventShareViolationResponse.cs`、`FileDialogEventOverwriteResponse.cs`（`IFileDialogEvents` 删除遗漏；SDK 隐式 glob,删文件即可）。
  - [x] `TextEncodingService.cs` 删 `HtmlEncode`(~16)、`HtmlDecode`(~21)（零调用）。
  - [x] `TrieNode.cs` 删 `AddWord(string,T)`(~113)、`GetChild(string)`(~153)（零调用）。
  - [x] `DatabaseMapper.cs` 删 `SaveJpeg`/`EncodeJpeg` 的死参数 `interpolationMode`(~160/168)+ 改调用点（调用点本就未传该参数,零改动）。
  - [x] provider DTO 只写不读死字段 + `KuwoTagProvider.FillExtendedSongMetadata` 整方法:
    - Kuwo：`AlbumArtist/Alias/Duration/FormattedArtist/Format/FormattedTitle/KMark/MusicInfo/MusicVideoFlag/MusicVideoPicture/MusicVideoQuality/Subtitle/Tags` + `AlbumId`
    - Kugou：`AlbumId/AlbumAudioId/OriginalTitle/FileName/OtherName/SourceType`
    - QQ：`QqSongInfo.DurationSeconds/LyricPreview`、`QqAlbumInfo.Title/Subtitle`、`QqArtistInfo.Id/Mid/Title`
    - NetEase：`NetEaseAlbumInfo.Type`
- [x] **批次 2 — iTunes 功能退役完整清理（P2）** ✅ 完成
  - [x] `OptionsDialog.cs`：删 `itunesCountryCodes`、5 个控件字段、本地化、CountryList 加载+读取、Hide、`case "TagSrcITunes"`、响应式宽度、Settings 保存、`InitializeComponent` 构造与布局。
  - [x] `Settings.cs`：删 `ItunesSearchParams_Country`。
  - [x] `musictag/MusicTag.config`：删 `<ItunesSearchParams_Country>`。
  - [x] `Resources`：删 `CountryList`(Resources.cs + resx)。注：iTunes dialog-text 资源键(`panelTagSrcItunesParams`/`lblItunesCountry`)不在 resx 中,无需删。
  - 验证：现有 OptionsDialog 反射构造 smoke test 兜底。注：`InitializeComponent` 已去扁平化为直线代码,当初保留理由已不成立。
- [x] **批次 3 — 退役源恒空搜索 pass（P2/P3）** ✅ 完成
  - [x] `CoverSearchDialog.cs`：删 `SearchByArtist`(恒空桩)及 “artist” pass 调用(多源 + preferred-source 两处)。
  - [x] `CombinedTagSearchDialog.cs`：删 `SearchAlbumFallbackTracks`(恒空,注释已说明 fallback 仅由已删 VGMdb/MusicBrainz 提供)+ 实例包装 `SearchCurrentContextAlbumFallback`,及 `SearchAllSources` 内**两处** album-fallback 块（preferred-source 路径 + 多源循环 —— 计划仅列了多源循环,删方法需连 preferred 一并删,两块都是各自 return 前最后一块、`CurrentBatch` 恒空使 rank/report 只上报空列表,行为等价）。
  - [x] `AutoMatchTagsDialog.cs`：删 album-fallback 块(1074-1085);`AddRankedCandidates` 空输入纯 no-op(无进度上报)。
  - 已确认:三处空 pass 均不产候选、不上报、不改剩余计数,候选列表与排序不变。
- [x] **批次 4 — 死分支 + 命名修正（小心）** ✅ 完成
  - [x] `EditableListView.cs`：删死类 `TextSubItemComparer` + 折叠恒假分支(`subItem.GetType() == typeof(DrawableListViewSubItem)`,而该类是 `abstract` 故精确类型判断恒假 → 唯一实例化点不可达)。**未**改抽象类型判断语义,只把恒假 `else if` 折叠进恒取的末尾 `else`。
  - [x] `StateFieldInstance.cs`：删 Undo 的 “Skipped” 死分支。已证 `UndoSaveTagsTaskContext`(1502-1652)/`UndoRenameTaskContext`(1691-1778)内 `skippedCount` 仅声明+读取、从不自增 → 恒 0;UndoSaveTags 删恒假 `else if (skippedCount>0)`,UndoRename 把恒真 `else if (skippedCount<=0)` 折叠、删死的末尾 `else{Msg_Skipped}`。多文件汇总行仍报该计数(恒 0),不动。
  - [x] 改名 `ConfigDescriptorState.LoadPictureSummary` 参数 `includePictureBytes`→`flagOnly`(名实相反:true=只标志、false=载字节);纯 token 替换 6 处,布尔值全不变。
  - [x] 改名 `AutoMatchTagsDialog.IsSelectedCoverData`→`HasProcessingFailed`(方法体仅 `return coverData.ProcessingFailed`,与选择无关）。
- [x] **批次 5 — 可读性残留 + 构建卫生（可选,纯整洁）** ✅ 完成（部分按判断推迟）
  - [x] `TrackSearchContext:76` `JToken.Value<long>()` → `long.TryParse(token.ToString())` 容错读取(对齐既有 `GetNullableLongField` 与 resilience 不变量;非数值 musicId 不再抛异常丢掉整个 LinkedMusicMetadata）。
  - [x] `NoWarn` 收窄:移除 `CS0162`/`CS0414`(全量 Rebuild 实测两者均 0),**保留 `CS0649`**——移除后暴露 15 条全是误报(interop `Marshal.PtrToStructure`:`NativeNotificationHeader`/`NativePictureEntry`/`ThumbButton`;`BinaryFormatter` 反序列化:`MappingChars`;WinForms designer:`components`),字段不可删,已在 csproj 加注释说明。
  - [~] no-op 转发/恒等 getter 簇内联、习语残留(while+无条件 break、双重否定、重复计数、多余别名、Stopwatch 写后不读、提前 Dispose、Kugou 关键词参数顺序)：**按保守政策推迟**——纯装饰性、对反编译码 churn 大而行为/可读性收益近零,且每处都带非零风险,违背「最小 diff、不做大范围重写」。非用户所求(死代码/残留/命名)的核心,留作独立专项按需处理。
- [x] **批次 6 — 文档同步** ✅ 完成
  - [x] `CLAUDE.md` 过期反编译约定:三处文件改名已全做(CustomToolStripRenderer 还被删)、三类高风险块(InitializeComponent 设计器状态机 / `_003C` async 状态机 / Tokenizer 表初始化)已全部重建、`PrivateImplementationDetails`/`PolicyTokenExporter` 已不存在 —— 改为过去时/标准政策表述;NoWarn 说明改为仅 CS0649。
  - [x] `docs/MAINTENANCE.md` 追加本轮批次 1-5 变更日志(5 条,含真实哈希)。
  - [x] `docs/DECOMPILATION_NOTES.md`:本就已被 `7c2584c` 修正到位(改名/async/Tokenizer 均准确);仅补注 CustomToolStripRenderer 改名后已删,保持「已完成改名」清单自洽。commit `a9eb55a`。

## 验证协议

每批后:`.\scripts\Verify-Build.ps1 -RunSmokeTests` → `git diff --check` → 通过则提交到 `master`（一批一提交）→ 回本文勾选 + 记日志（含真实 commit 哈希）。

## 进度日志

- 2026-06-24 建文档,锁定 6 批计划。开始批次 1。
- 2026-06-24 **批次 1 完成**。删 `WindowPlacement` 三件套、两个死枚举文件、`HtmlEncode/HtmlDecode`、`TrieNode.AddWord/GetChild(string)`、`DatabaseMapper` 死参 `interpolationMode`、provider DTO 只写不读字段(Kuwo 14 / Kugou 6 / QQ 7 / NetEase 1)+ `KuwoTagProvider.FillExtendedSongMetadata`。16 文件改(含 2 文件删),纯删除 −174 行。Debug+Release 0/0 + 3 smoke 全过。构建即验证了所有删除字段确为只写不读(否则编译失败)。commit `74f81a0`。
- 2026-06-24 **批次 2 完成**。删退役 iTunes 源的全部残留:`OptionsDialog.cs` 的 itunes* 控件字段/构造初始化/本地化/CountryList 加载/Show-Hide/`case "TagSrcITunes"`/响应式宽度/Settings 保存/InitializeComponent 块(−82)、`Settings.cs` 的 `ItunesSearchParams_Country`、`Resources.cs`+`.resx` 的 `CountryList`、`MusicTag.config` 的持久值。5 文件改,纯删除 −103 行。Debug+Release 0/0 + 3 smoke 全过(OptionsDialog 反射构造守住 designer 手术)。commit `82c1969`。
- 2026-06-24 **批次 3 完成**。删退役源恒空搜索 pass:`CoverSearchDialog.SearchByArtist` 桩 + 两处调用;`CombinedTagSearchDialog` 的 `SearchAlbumFallbackTracks`/`SearchCurrentContextAlbumFallback` 两方法 + `SearchAllSources` 两处 album-fallback 块(preferred + 多源);`AutoMatchTagsDialog` album-fallback 块。3 文件改,纯删除 −62 行。已逐一追踪 rank/report 链确认空 pass 无副作用(空列表→排序/限流/上报皆 no-op,不改剩余计数,候选与排序不变)。Debug+Release 0/0 + 3 smoke 全过。commit `c15d874`。
- 2026-06-24 **批次 4 完成**。死分支:`EditableListView.TextSubItemComparer` 死类 + 恒假抽象类型分支折叠;`StateFieldInstance` 两个 Undo 的 “Skipped” 死分支(已证 skippedCount 在两 undo context 内恒 0)。命名:`LoadPictureSummary(includePictureBytes→flagOnly)`(名实相反,纯值不变 token 替换 6 处)、`IsSelectedCoverData→HasProcessingFailed`。4 文件改,−52/+9。Debug+Release 0/0 + 3 smoke 全过(构建验证改名各调用点完整)。commit `3710e9c`。
- 2026-06-24 **批次 5 完成（部分推迟）**。`TrackSearchContext` musicId 改容错 `long.TryParse`;`NoWarn` 收窄为仅 `CS0649`(移除已 0 的 CS0162/CS0414,CS0649 是 interop/反序列化/designer 误报必留,加注释)。2 文件改,+7/−2。关键发现:全量 Rebuild 暴露 15 条 CS0649 全为运行期赋值字段误报,非死代码。no-op 转发内联/装饰性习语清扫按保守政策**主动推迟**(churn 大、收益近零、非用户核心诉求)。Debug+Release 全量 Rebuild **0 warning/0 error** + 3 smoke 全过。commit `f88ffee`。
- 2026-06-24 **批次 6 完成**。文档同步:`CLAUDE.md` 反编译约定整段去过期(改名/三类高风险块/已删空壳),NoWarn 注释改 CS0649-only;`MAINTENANCE.md` 追加批次 1-5 日志;`DECOMPILATION_NOTES.md` 补 CustomToolStripRenderer 删除注。纯文档,无构建影响。commit `a9eb55a`。

## 收尾总结

6 批全部完成,**14 个代码/文档提交**(7 代码 + 7 文档进度),全程行为保留:每个代码批次 Debug+Release 构建 0/0 + 3 个 smoke test 全过。净删除约 **−390 行代码**(批次 1−174 / 批次 2−103 / 批次 3−62 / 批次 4−52+9 / 批次 5+7−2),零功能回归(构建即验证字段/调用点完整性,死分支均经数据流证明恒不可达)。

**做了什么**:孤儿死成员(struct/enum/方法/只写字段)、退役源(iTunes UI/config/resource、恒空搜索 pass)、恒不可达分支(抽象类型判断、恒 0 的 skippedCount)、误导命名(名实相反的 `flagOnly`、`HasProcessingFailed`)、一处容错读取对齐、构建告警收窄(CS0162/0414 移除、CS0649 留并说明)、文档去过期。

**主动未做(诚实记录)**:批次 5 的 no-op 转发器/恒等 getter 批量内联与装饰性习语清扫 —— 对反编译码 churn 大、行为/可读性收益近零,违背「最小 diff、不做大范围重写」,留作独立专项。`musictag/System.ValueTuple.dll` 等孤儿运行期资产已在更早批次处理。

**未覆盖验证(项目固有)**:无单元测试;联网 provider 的实际写回路径不被 smoke test 触及,等价性依据数据流分析与构建,非实跑。
