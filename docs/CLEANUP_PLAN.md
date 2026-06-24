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
- [ ] **批次 4 — 死分支 + 命名修正（小心）**
  - [ ] `EditableListView.cs`：删死类 `TextSubItemComparer`(30-59)+ 折叠 598 行恒假分支。**不修正**抽象类型判断本身（会改排序行为,另立项）。
  - [ ] `StateFieldInstance.cs`：删 Undo 的 “Skipped” 死分支(5533 UndoSaveTags、5596 UndoRename)。
  - [ ] 改名 `ConfigDescriptorState.LoadPictureSummary` 参数 `includePictureBytes`→`flagOnly`（纯重命名）。
  - [ ] 改名 `AutoMatchTagsDialog.IsSelectedCoverData`→`HasProcessingFailed`。
- [ ] **批次 5 — 可读性残留 + 构建卫生（可选,纯整洁）**
  - [ ] no-op 转发/恒等 getter 簇内联（AutoMatchWorker、SFI、PhraseTrie 冗余 override 等）。
  - [ ] 习语残留:while+无条件 break、双重否定、重复计数类、多余别名、Stopwatch 写后不读、提前 Dispose、KugouTagProvider 关键词参数顺序。
  - [ ] `TrackSearchContext:76` `JToken.Value<long>()` → 容错读取。
  - [ ] 移除 `NoWarn` 三项压制(csproj:15)——Debug+Release 都验证 0 warning。
- [ ] **批次 6 — 文档同步**
  - [ ] `CLAUDE.md`(96-107) 过期反编译约定（文件已改名、async 状态机已重写、Tokenizer 表初始化已删）。
  - [ ] `docs/MAINTENANCE.md` 追加本轮变更日志；`DECOMPILATION_NOTES.md` 同步。

## 验证协议

每批后:`.\scripts\Verify-Build.ps1 -RunSmokeTests` → `git diff --check` → 通过则提交到 `master`（一批一提交）→ 回本文勾选 + 记日志（含真实 commit 哈希）。

## 进度日志

- 2026-06-24 建文档,锁定 6 批计划。开始批次 1。
- 2026-06-24 **批次 1 完成**。删 `WindowPlacement` 三件套、两个死枚举文件、`HtmlEncode/HtmlDecode`、`TrieNode.AddWord/GetChild(string)`、`DatabaseMapper` 死参 `interpolationMode`、provider DTO 只写不读字段(Kuwo 14 / Kugou 6 / QQ 7 / NetEase 1)+ `KuwoTagProvider.FillExtendedSongMetadata`。16 文件改(含 2 文件删),纯删除 −174 行。Debug+Release 0/0 + 3 smoke 全过。构建即验证了所有删除字段确为只写不读(否则编译失败)。commit `74f81a0`。
- 2026-06-24 **批次 2 完成**。删退役 iTunes 源的全部残留:`OptionsDialog.cs` 的 itunes* 控件字段/构造初始化/本地化/CountryList 加载/Show-Hide/`case "TagSrcITunes"`/响应式宽度/Settings 保存/InitializeComponent 块(−82)、`Settings.cs` 的 `ItunesSearchParams_Country`、`Resources.cs`+`.resx` 的 `CountryList`、`MusicTag.config` 的持久值。5 文件改,纯删除 −103 行。Debug+Release 0/0 + 3 smoke 全过(OptionsDialog 反射构造守住 designer 手术)。commit `82c1969`。
- 2026-06-24 **批次 3 完成**。删退役源恒空搜索 pass:`CoverSearchDialog.SearchByArtist` 桩 + 两处调用;`CombinedTagSearchDialog` 的 `SearchAlbumFallbackTracks`/`SearchCurrentContextAlbumFallback` 两方法 + `SearchAllSources` 两处 album-fallback 块(preferred + 多源);`AutoMatchTagsDialog` album-fallback 块。3 文件改,纯删除 −62 行。已逐一追踪 rank/report 链确认空 pass 无副作用(空列表→排序/限流/上报皆 no-op,不改剩余计数,候选与排序不变)。Debug+Release 0/0 + 3 smoke 全过。commit `c15d874`。
