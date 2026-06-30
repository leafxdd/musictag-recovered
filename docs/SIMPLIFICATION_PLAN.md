# 简化与复用计划（第 2 轮）

> 接续 `docs/CLEANUP_PLAN.md`（第 1 轮：死代码 / 命名 / 反编译控制流，批次 1–7 已完成）。
> 本轮聚焦 **reuse / simplification / efficiency / altitude**，**全部行为保留**。
> 来源：19 分区并行扫描工作流（整库 114 文件 / ~37k LOC），67 条发现 + 19 份结构笔记，已对抗性自检。
> 本文档是本轮的**单一事实来源**，用于跨上下文压缩续作。每完成一批，勾选 checkbox 并在「进度日志」追加一行（只记真实已发生的事，含真实 commit 哈希）。

日期起：2026-06-28 · 分支：`master`（本地仓库，无 remote）· 验证：`.\scripts\Verify-Build.ps1 -RunSmokeTests`

## 背景

第 1 轮（`CLEANUP_PLAN.md`）已把死代码、退役源残留、误导命名、写后不读的 Stopwatch、反编译控制流（goto / async 状态机 / 生成闭包 / 假包装）在 provider、搜索对话框、结果模型等子系统大量清除。本轮在此之上找**仍可安全收敛的重复与可简化结构**：

- **跨文件 reuse**（最大价值）：4 个 provider 的候选装配骨架、3 个搜索对话框的页脚/状态/按源跟踪、NetEase 加密 POST 体与 JSON 读取器。
- **god-class altitude**：`StateFieldInstance` 批处理脚手架、`DatabaseMapper` 误名神类——多数高 churn，列入 Tier C 推迟。
- **死代码（纯减法）**：整库无引用的 `LimitedConcurrencyTaskScheduler` / `ImageComboBox`、`Resources.Enum_*` 残留。

## 行为保留铁律（每批都遵守）

- 反编译恢复项目，**行为等价**优先，以 Debug+Release 0/0 + 3 smoke test 验证。**一批一提交**。
- ⚠️ 标记项需**额外逐字节 / 逐分支核对**：用户可见文案、`Substring` 截断怪癖、命名约定依赖、联网写回路径。
- **联网 provider 写回路径无 smoke 覆盖**，等价性靠数据流分析 + 逐 provider 构建，非实跑。
- 每个抽取保持**公开签名不变**，调用点零改动（除非该批显式改调用点）。

## 禁区（不动）

- native `DllImport` EntryPoint 字符串、标准 Win32 P/Invoke 签名与 COM 接口声明。
- `SearchSource` 显式序数（Music163=0 / QQ=1 / Kugou=3 / Kuwo=9）与注释。
- 持久化 JSON 键 `[JsonProperty]`（`SourceItem` 的 Src/Seq/IsOther/WebSearchItemsLimit 等）。
- 传给 `ResourceManager`/`ComponentResourceManager` 的资源基名（`MusicTag.Schemes.EventRulesSchema`、`MusicTag.Importers.WorkerComparatorImporter`、`MusicTagWinApp.Instances.BaseFieldInstance`）及全部 `.resx` 键。
- 命名空间 / 文件夹名；类名 `StateFieldInstance` / `ConfigDescriptorState`。
- 已删源配置项 `Src:5/6/7/8`（加载时忽略，保守保留）。
- 有意的 resilience 吞异常（单源失败必须 log-and-skip，**不得**收紧成 throw）。

## 已评估并否决（不动，勿在后续轮次再提）

- `SearchStatusIndicator.GetSourceDisplayName` 硬编码中文源名（网易云/酷狗/酷我）—— **有意**区别于 `Enum_*` 资源，替换会改可见状态文案。
- `OptionsDialog` 加载 vs 保存镜像、单选按钮 int↔index 映射 —— 固有反序列化/序列化对偶。
- `Settings.cs` 全部 45 个属性 —— 即持久化配置名（禁区），`ApplicationSettingsBase` 反射其构建 schema，删任一即改持久化。
- `Resources.cs` 的 `*_Image`/`*_Image2X` 位图访问器 —— 可能由 designer `ComponentResourceManager` 按名加载，批量删高 churn 低价值，**不动**。
- NetEase 专辑年份 `List<(long,info)>` FIFO 缓存 —— 换 `Dictionary` 会丢有界淘汰语义，非等价。
- `FilenameRelatedBatchDialog` 的 `@N`↔标签字段三处映射（combo 顺序 / regex-capture switch / pattern-token switch）—— 三处**语义不同**（直接 set vs changed-check；`SetNumberedTag` vs `TryParse`），合并即改行为；`ValidateFilenamePattern` 的 `@5@4` `StartsWith`（1162）非冗余；`configDescriptorState.Dispose()`（206）有意释放文件锁。
- `ProgressDialog` vs `SimpleProgressDialog` 合并 —— 进度模型根本不同（确定条+计时器+回调 vs 静态 spinner），净负收益。
- `AutoMatch.GetTextTagMatchKeys()` 缓存 —— 两次调用间 `RemoveUnchangedOrBlockedField` 会 mutate，缓存即改行为；`SaveTagsToFile` 的重载 `ConfigDescriptorState`（915）是有意的锁下重读。
- `ChineseTextConverter.ConvertCharactersOnly` vs `AppendConvertedCharacters` —— 空白/null 处理不同，两者皆 live，合并改行为。
- `AsciiTokenClassifier` 过度分解 —— 但可读且上批有意抽取，收敛是 wash。
- 各类 no-op 转发器 / 恒等 getter / 装饰性习语 —— **第 1 轮批次 5/7 已评估并刻意推迟**，churn 大收益≈零，本轮不重提（唯一例外见 A9 `GetSourceFromItem`：单点、object 参数主动误导）。

---

## 批次清单

### Tier A — 安全速赢（低风险，建议先做）

- [x] **A1 死代码删除（纯减法）** —— ✅ 完成 `ea1e618`
  - [x] 删 `src/MusicTag/MusicTag.States/LimitedConcurrencyTaskScheduler.cs`（123 行，全库无 `new`）—— **删文件后 build 即证明无引用**（真·编译器证明）
  - [x] 删 `src/MusicTag/MusicTag.Bridges/ImageComboBox.cs`（165 行）+ 同步删 `MusicTagWinApp.Roles/EditableListView.cs` 不可达消费分支（`typeof(ImageComboBox)` @354-365 及仅其调用的 `ApplySingleImageSelection`/`ApplyImageListSelection`）—— ⚠️（Codex 点 4）**非纯编译器证明**：删文件会令 `typeof(ImageComboBox)` 编译失败逼出该分支，但「删分支行为等价」依据是**可达性分析**（无静态 `new`、无子类、唯一 `EditorControl` 赋值点不可能是它），执行前核对无 designer/动态创建入口、执行后跑**完整 smoke**
  - [x] 删 `MusicTagWinApp.Properties/Resources.cs:164-170` 死访问器 `Enum_Kugou`/`Enum_Kuwo`/`Enum_Music163`/`Enum_Xiami`（含退役源 `Enum_Xiami` 残留；`.resx` 数据键不动，动态键 `GetString("Enum_"+name)` 不受影响）

- [x] **A2 SQLite 数据层 in-file 去重** —— ✅ 完成 `de1af6b`
  - [x] `GetLogSubdirectory(sub)` 收敛 7 个 `Get*LogDirectory`（`MusicTagWinApp.Instances/DatabaseMapper.cs:284-317`）
  - [x] `ExecuteNonQueryLogged(...)` 收敛 3 个 CRUD 包装（`MusicTagWinApp.Listeners/TagHistoryRepository.cs:336-373`）
  - [x] `ComputeMd5HashString(string)` 委托 byte[] 重载（`DatabaseMapper.cs:103-107`）
  - [x] 两处 catch 改调既有 `MarkTransactionFailed()`（`TagHistoryRepository.cs:247,261`）

- [x] **A3 StateFieldInstance 纯 in-file 去重（无写 / 重命名副作用，仅 UI / 显示 / 错误路由）** —— 7 项完成 `cc1bc3c`（`MusicTagWinApp.Instances/StateFieldInstance.cs`）
  - [x] 复用既有 `AddSelectedFilterValues`，删 2 个 filter-tally 闭包类 `FilterValueCollector`/`SelectedItemFilterValueCounter`（两调用点改 `AddSelectedFilterValues(fileRow)`；等价证明：`GetSelectedFilterValue` 已把空白归一化为 ""，故 `AddSelectedFilterValue` 的 `IsNullOrWhiteSpace` 守卫在此为 no-op，且 `activeFilterContext.owner == this`）
  - [x] `Subscribe/UnsubscribeTagFieldTextHandlers` 收敛 6 处订阅循环（4 个 `+=` / 2 个 `-=`）
  - [x] `FormatCountDurationSize(count,ms,bytes)` ×4 状态标签插值
  - [x] `BuildBasicFileDisplayValues` 合并双分支字典（updatetime 三元，`!Exists` 时短路不取 LastWriteTime）
  - [x] `GetLoadedFilePaths` 改一行 LINQ（`new HashSet<string>(...Select(...))`）
  - [x] `ConvertAllTagFields(converter)` 把 `ChineseTextConverter` 工厂提出循环（工厂返回静态 readonly 单例 + `ConvertText` 纯函数 → 复用等价；仅改编辑框内存文本，需用户另存才落盘）
  - [x] `ReportAsyncOperationErrorIfNotCancellation(ex,cts,name)` 收敛 13 处取消感知 catch（全类各 runner 的 catch，happy-path 不变）

- [x] **A3b（Codex 点 2）写标签 / 重命名 UI 入口 + 结果文案去重（从 A3 拆出，单独成批、重验证）** —— 3 项完成 `6b61728`（`StateFieldInstance.cs`）
  - 这些处理器**确实进入写 / 重命名路径**（已核实 `StartCommonSaveTags`、`StartRenameFiles`），故不与纯 in-file 去重混批。
  - [x] `ConfirmAndSaveTagsWithOperation(labelKey,comp)` 泛化 `StartBatchLyricsOperation` + 2 个 CHS/CHT 标签处理器（均调 `StartCommonSaveTags`；SimplifiedToTraditional 的早退式写法折叠进同一 `count!=0 && Confirm` 守卫，comp 改由调用方构造——无副作用常量字典，提前 vs 延迟分配不可观测）
  - [x] `ConfirmAndConvertSelectedFilenames(menuKey,isChsToCht)` 合并文件名 CHS/CHT 两处理器（均调 `StartRenameFiles`）
  - [x] ⚠️ `BuildBatchResultMessage(total,completedMsg,primary,failed,skipped,processed,log,includeSkippedBranch)` 收敛 4 处 save/rename/undo 结果消息装配；`includeSkippedBranch` 区分 save/rename 4 分支（含 `Msg_Skipped`）与 undo 3 分支；`StartClearTags` 变体（`Msg_CleartagsCompleted`+`Msg_OK_Fail_Count`、primary 分支裸消息）保留 inline——**用户可见文案逐字节核对**
  - 验证：diff 读 + Debug+Release 0 warn + smoke；确认文案 / 选中文件预览 / 只读处理 / 取消分支 / 进度弹窗 / 每分支结果文案逐字节不变（实际 save+rename 运行路径无 smoke 覆盖，等价靠逐字节分析）

- [x] **A4 结果模型 / 相似度** —— 仅执行 ⭐ Item 1；其余 3 项评估后**否决**（`MusicTagWinApp.Roles/TrackSearchResult.cs`）
  - [x] ⭐ `ResolveNonInstrumentalCandidate` 收敛 `PromoteBestMatch` 7 处 instrumental 解析三元式（5 处自兜底 `?? 候选` + 2 处 `!= null` 守卫）—— **✅ 完成 `e7afa05`**（净 −14 行）。命名取 `ResolveNonInstrumentalCandidate`（返回可空三元结果，自兜底点内联 `?? 候选`）；**513 行有意不动**：判定原 `currentBest.Title` 却以当前 `results[0]` 为回溯种子（中途 `MoveTrackToFront` 已重排），判定实体≠回溯种子，不符助手契约
  - [x] ❌ **否决** `CompareScoresDescending` 上提：两评分循环**语义不等价**——`CompareByScoreOrder` 用 `CompareDescending`（`r.CompareTo(l)`，对 NaN/−0.0 有序）、`CompareLyricResults` 用 `>`/`<`（NaN 视作相等）；tie-break 亦不同（Track 含 `SearchPass` + `GetScoreIndex` 置换，Lyric 无）。真正共享内核须重构 Track 置换路径，风险 > 去重收益（零 smoke 覆盖的双排序路径），等价仅靠"分数域非 NaN"论证而非构造
  - [x] ❌ **否决** `ReplaceFullWidthPunctuation`：`NormalizeForMatch`（`.Replace` 单字符链）与 `NormalizeSimilarityTextCandidates`（`Regex` on `string[]`）是两套并行实现、跨上下文调用，Regex↔`.Replace` 等价性 + `string[]` 适配风险高收益低
  - [x] ❌ **否决**（效率）memoize `NormalizeForMatch`：属**性能优化非可读性简化**，引入缓存状态，超出本轮"行为保留简化"范围

- [x] **A5 歌词处理** —— 3 项完成 `e8bd2b1`；LRC 表（altitude）推迟
  - [x] `AbsorbTranslatedLines(translated)` 收敛 `MergeTranslatedLyric`/`AlignAndSplitTranslatedLyric` 两处译文吸收循环（449-469,504-523）
  - [x] `AppendTimestamp(t)` 局部函数收敛 `FormatLyricLine` 6 处守卫 append（354-445）
  - [x] `CreateMergedProcessor(lyric,translated)` ×2 静态下载包装（674-678,702-706）
  - [ ] ⏸ **推迟**（altitude，高风险，留待）⚠️（altitude）LRC 元数据描述符表统一 parse / emit / merge 三处（93-152,287-337,470-481）—— **保留 `Substring` 截断怪癖，勿换正则**；`offset` 仍特例（仅 parse+apply）

- [x] **A6 ListView 控件 / 列** —— 完成：items 1+3 `48aa162`，item 2 `67f3132`
  - [x] `SortableTextComparer`（`Func<ListViewItem,string>` 选择器 + `SortOrder`）把 3 个 IComparer 嵌套类收敛为 1，连带删 3 个死构造器（`MusicTagWinApp.Roles/EditableListView.cs:30-113`；保留 559-573 比较器选择分支不动）
  - [x] ✅ `67f3132`（4 处中心数学逐字节核验相同，可证像素等价） `DrawCenteredImage(g,image,bounds,x)` 上提到基类 `MusicTagWinApp.Stubs/DrawableListViewSubItem.cs`，统一 4 处居中绘图（ImageSubItem / ImageListSubItem / CheckBoxSubItem / EditableListView 封面分支）
  - [x] `MoveSelectedColumn(delta)` 合并上移 / 下移镜像对（`MusicTagWinApp.Common/CustomColumnsDialog.cs:357-399`）

- [x] **A7 杂项对话框** —— 3 项完成 `caa9123`
  - [x] `FillAndCenterButtons(list,mainPanel,buttonPanel)` 共享布局 helper ×3（`MusicTagWinApp.Common/DirectoryManagerDialog.cs`、`MusicTag.Importers/CombinedTagOverwriteOptionsDialog.cs`、`MusicTag.Consumers/CharacterSetSelectionDialog.cs`）—— 落点 `DatabaseMapper`（紧邻 `ScaleByDpi`，三 dialog 既有共享 UI 工具集中地；各保留自身末尾列宽行）
  - [x] `GetEmbeddedPictureData(state)` ×2（`MusicTag.Importers/PictureFromTagsDialog.cs`）—— 两调用点保留各自早退（loop `continue` vs 方法 `return`）与 `using` 生命周期
  - [x] `SyncControllerInputs()` ×4 按钮处理器（`MusicTag.Consumers/FindReplaceDialog.cs`）

- [x] **A8 其余 in-file 小项** —— 4 项完成 `0eaefa6`；ChangeTags 推迟、TagTextEncoding 否决
  - [x] （效率）`TextBoxFindReplaceController.ReplaceAll` 提取 `textBox.Text` 本地量，消除 per-match 重读（`MusicTag.Serialization/TextBoxFindReplaceController.cs:73-107`）
  - [ ] ⏸ **推迟**（写路径，需拆常量/逐文件用法） （效率）`FilenameRelatedBatchDialog.ChangeTags` 把批常量 regex/pattern 提出 per-file 循环（`MusicTag.Schemes/FilenameRelatedBatchDialog.cs:407,430-486`）
  - [x] `SourceOrderControl` 移动处理器复用既有 `CanMoveUp`/`CanMoveDown`（`MusicTagWinApp.Stubs/SourceOrderControl.cs:122-182`）
  - [x] `ListViewFileSetting.AddForDir(fileInfo)` 委托给路径重载（`MusicTagWinApp/ListViewFileSetting.cs:19-57`）
  - [x] `Program.Main` inline `RunApplication`（`MusicTag.Schemes/Program.cs:27-51`）
  - [x] ❌ **否决**（switch `default` 分支用不同输入：源用 `NormalizeEncodingName(stringType)`、目标返回整名；未注册含 `=>` 名不等价） ~~`TagTextEncoding` 用 "=>" 拆分替换两个 switch 阶梯~~（`MusicTag.Serialization/TagTextEncoding.cs:201-244`）—— **对全 10 个注册名 + "GB"→"GB18030" 特例核对**

- [x] **A9 AutoMatchTagsDialog（未测试热点，逐项验证）** —— 4 项完成 `4bb28c6`；ExtractResultsFromRankedTracks（altitude）推迟（`MusicTagWinApp.Adapter/AutoMatchTagsDialog.cs`）
  - [x] `RunSourceSearchPass(...)` 收敛主 / 次源搜索两 pass（1069-1092；用 `IsSecondarySource == secondary`，ref 线程化 `sourceOrderIndex`，`useProviderRanking:!secondary`）
  - [x] `SaveSidecarFiles(coverPicture)` ×2 封面+歌词侧车保存块（file-only 分支保留 allSucceeded→success/failed 计数；tag-save 后分支忽略返回值，与原同）
  - [x] `IsSameTrackMetadata(a,b)` ×2 best/alternate 严格等值检查
  - [x] inline `GetSourceFromItem`（单点 object 参数误导，唯一允许的 forwarder 例外；已并入 `IsNetEaseSourceAvailable` 并删除）
  - [ ] ⏸ **推迟**（altitude）`ExtractResultsFromRankedTracks(...)` move-method 到 `MetadataSearchState`（1093-1176）

### Tier B — 跨文件复用（中风险，需仔细验证）

- [x] **B1 ConfigDescriptorState 写标签核心（逐项 build+smoke）** —— 3 项完成 `f04426e`（`MusicTag.States/ConfigDescriptorState.cs`）
  - [x] `SaveWithId3v2Version(Action writeBody)` 收敛 `SaveTagFields`/`SaveCurrentTagFile` 版本锁 / try / catch / finally（body 作 Action 传入，公共 loadError 前导 + SetId3v2Version/Save/return 尾部留 helper）
  - [x] `AppendUtf8Blocks(values,blocks,tagTypeName,ref tagType,ref stringType)` 统一 `FillRawFromXiph`/`FillRawFromApe` 的 UTF8 编码尾（仅 tagType 名不同）
  - [x] `AddIfAbsent(key,factory)` 收敛 `LoadAudioProperties` 6 处惰性缓存（factory 仅 key 缺失时求值，装箱不变）

- [x] **B2 搜索对话框收敛到 `SearchStatusIndicator`（在线子系统）** —— 4 项完成 `a906e4a`（B2-1 否决）；build+smoke 通过 + 5/5 对抗性验证 verdict 行为保留
  - [ ] ~~enum→provider 类型映射工厂 `CreateProvider(source,cts)`~~ **否决（not-equivalent）**：`RemoteTagProviderBase` 不声明任何搜索方法（`SearchCovers`/`SearchLyrics`/`SearchTracks`/`LoadLyric(s)ForTrack` 全 concrete-only 且签名异构——NetEase 带 `long musicId` 5/7 参、Kuwo `LoadLyricForTrack` 单数名、Kugou 无 `SearchCovers`，且无共享接口）。基类型工厂返回值无法 uniform 调用任何搜索方法（CS1061）；强行用 `dynamic` 改绑定/异常语义、逐源 cast 等于原 switch 零简化；另封面 switch 仅 3 源而工厂 4 源（Kugou 域不匹配）。
  - [x] `SearchStatusIndicator.LayoutFooterStatus(footer,buttons,label)` 收三 dialog 逐字节相同的页脚按钮+状态标签布局块（`public static`，加 `using System.Drawing;`）
  - [x] `SearchStatusIndicator.BeginReporting()` 收三 dialog 相同的 `Progress<SourceSearchStatus>` 通道接线（实例方法，返回 reporter；SynchronizationContext 捕获等价已证）
  - [x] `SourceOutcomeTracker`（`MusicTagWinApp.Web`，Record/Clear/BuildFinalStatus/ReportFinal）收敛 Cover（worker 字段，无 Clear）+ Lyric（dialog 字段，留 Clear）；**ReportFinal 接收调用方 enabled-source 序列**保证发射集不变；Combined 单发模型留原处
  - [x] `CoverSearchDialog` 合并 `SearchByAlbumAndArtist`/`SearchByTitleAndArtist`→`SearchCoversBySource(source,query,existingCandidates)`（query 留 lambda 内延迟求值，3 参保留 worker `accumulatedCandidates`）

- [x] **B3 Provider 候选装配上提到 `RemoteTagProviderBase`（结构价值最大，拆 2–3 子提交）**
  - [x] `BuildOrderedTracks<TSong>` / `BuildOrderedLyrics<TSong>` / `BuildDedupedCovers<TSong>`（含 SearchSource 过滤去重集、有序 Dictionary 物化、ResultOrder/SearchPass/SourceOrder 标注）—— 先 QQ/Kuwo/Kugou，NetEase 因额外 `(knownSongId==0||Cover!=null)` 谓词与 `EncodeMusicComment` 后步后续并入。⚠️（Codex 点 6）helper **只接收 songs + provider 构造/加载委托，不统一联网调用顺序与取消语义**：Tracks 骨架三家结构一致，Lyrics 因 Kuwo loader 不同经委托吸收，**Kugou 的反转取消控制流（`if-not-cancelled…continue; else break`）须先证语义等价再并入**
    - QQ `SearchTracks` 217-305 / `SearchLyrics` 153-174 / `SearchCovers` 176-215；Kugou 123-182 / 81-102；Kuwo 72-113 / 115-140 / 156-179
  - [x] 共享 JToken 安全读取器上提（`GetStringField`/`GetNullableIntField`/`GetLongField`/`GetNullableLongField`/`GetFirstField`/`GetStringOrEmpty`，现 4 provider 各有副本，NetEase 版含多名 fallback）
  - [x] NetEase 局部（`MusicTagWinApp.Exporters/NetEaseMusicTagProvider.cs`）：`BuildEncryptedPostBody`（100-171）、`PostSongQuery`（92-145）、`SearchTracks` 单 `HashSet` 有序去重删并行 List+重建（306-403）、`FormatPublishYear`（286-298,333-344）、`SearchCovers` 单一去重（206-241，已随 B3-1 折叠到 `BuildDedupedCovers`；其余 3 项由 B3-3 `11c32eb` 完成（`BuildEncryptedPostBody` 抽取、`PostSongQuery` 合并 `SearchSongs`+`LoadSongDetails`、`FormatPublishYear`），**`SearchTracks` 单 `HashSet` dedup 坍缩亦完成 `d0e53c1`**——cover-gate +`EncodeMusicComment` 后步原样保留（二者仅阻碍折叠到基类 `BuildOrderedTracks`、不阻碍本地坍缩：删 `Dictionary tracksById`+并行 `List trackIdsInOrder`+重建循环 → 单 `HashSet seenTrackIds`+直接 `tracks.Add`；3 skeptic+critic 对抗验证全等价、0 分歧）
  - [x] Kugou 删 `SearchResultDetailLoader` 闭包类 + 手写枚举器，改 foreach + lambda（`MusicTag.Candidates/KugouTagProvider.cs:21-30,144-169`）
  - [ ] ⚠️ **联网写回路径无 smoke 覆盖**；逐 provider build+smoke，等价性靠数据流分析

- [x] **B4（可选）ProgressDialog 事件现代化** —— 完成 `4289394`；build+smoke 通过 + 6 怀疑者对抗验证（行为保留，唯一 delta 不可达）
  - [x] 手写委托字段 + Add/Remove + `Interlocked` CAS → C# field-like `event`（双事件 `CancelRequested`/`ProgressUpdate`，删 4 Add/Remove 方法 + 2 静态 CAS 助手 + 孤儿 `using System.Threading`，留 `ProgressDialogCallback` 委托类型）；30 个调用点（24 `StateFieldInstance`：22 Add→`+=` + 2 Remove→`-=`；4 `FilenameRelatedBatchDialog`；2 `AutoMatchTagsDialog`）改 `+=`/`-=`，git numstat 增删配平、零方向翻转。行为等价（Roslyn field-like event 的 add/remove 访问器即 `Delegate.Combine/Remove`+`Interlocked.CompareExchange` CAS，与手写助手同构）。**唯一 delta 不可达**：手写循环判 `CompareExchange(...) != previous` 因两端委托类型绑 `Delegate.op_Inequality`（值比较调用列表），Roslyn 用引用比较（`bne.un`）——仅并发改订阅插入"值等引用异"多播时分歧（手写或把失败 CAS 误判成功丢更新，事件形重试），而本类 `+=`/`-=`/invoke 全在 UI 线程串行（订阅在 ShowDialog 前；唯一 Remove 对在 async-void `finally` 经捕获 UI SyncContext 恢复；后台 `Task.Run` 体不碰订阅），CAS 从不竞争 → 两形逐次等价，可达处事件形严格更正确。

### Tier C — 结构 / altitude（高 churn / 高风险，**默认推迟，仅记录方向**）

- [ ] **StateFieldInstance**（8868 行）：`BatchFileTaskContext` 基类（或 `BatchFileProcessor<TItem>` 驱动）统一 ~11 个批处理 context + 8 个 `Start*` runner 脚手架（Cancel/UpdateProgress/循环/取消检查/`TagHistoryRepository` try-finally）；7 个 failure-reporter 类 + `AppendFileError(page,name,msg)`；`PictureCompressionWorker` 的 14 个 `ResizeToNNNQualityMM` 改 `(resolution,quality)[]` 表；`CoverPreviewController`（~700 行，4815-5650）/ VirtualMode 选择模型（3711-3816）抽取；`InitializeComponent` 移入 `StateFieldInstance.Designer.cs` partial。
- [x] **DatabaseMapper**（实测 ~72 方法、零 DB 访问）：拆 image/DPI、file-logging、path/dir、message-box → 实为 **5 簇**（+ text/hash/encoding）。**2026-06-29 完成**，原文件删除，详见下方「Phase 2 首批完成」。
- [ ] **TagHistoryRepository**：抽出静态 `UndoStore`（进程级内存撤销栈 + spill-to-disk，与实例和 SQLite 连接无关）。
- [ ] **ConfigDescriptorState**：13 字段词汇表统一 4 个并行 switch（`ReadFieldText`/`Id3v2FrameId`/`XiphFieldId`/`ApeFieldId`）—— 有真实不对称（id3v2 略 comment/lyrics；read 派生 trackstr/discstr），中风险。
- [ ] **CombinedTagSearchDialog**：封面缩略图子系统（`CoverDownloadRequestContext`+`CoverImageLoadTask`+`CoverDownloadFile` + `DownloadCoverAsync`，~250 行，63-195/717-812，疑与 CoverSearchDialog 封面下载重叠）、track-search 三元组（`TrackSearchCoordinator`/`TrackSearchLimitState`/`TrackResultLimitCollector`，197-412）抽取。
- [ ] **OptionsDialog / AutoMatchWorker 大拆分**：已评估为**高风险低收益，不建议**（designer 不洁继承 + 共享可变字段 + smoke 反射构造），仅记录。

---

## 验证协议

每批后（对齐 `AGENTS.md` §Build/Testing + `DECOMPILATION_NOTES.md` 后续处理原则）：`.\scripts\Verify-Build.ps1 -RunSmokeTests` → `git diff --check` → **`codegraph sync`**（Codex 点 3，此前漏列）→ 触及 **UI / 写标签 / 在线搜索 / 资源加载 / interop** 时补 **focused 手动验证说明**（smoke 不覆盖这些工作流；本轮几乎每批都命中至少一类）→ 通过则提交到 `master`（一批一提交）→ 回本文勾选 + 记日志（含真实 commit 哈希）。⚠️ 标记项需在提交前额外逐字节 / 逐分支核对。Tier B / B3 联网 provider 路径无 smoke 覆盖，依赖数据流分析。

> **提交卫生（Codex 仓库状态提醒）**：工作区现存 `D CLAUDE.md` 与未跟踪的 `.claude/`、`.mcp.json`、`.repowise/`、`AGENTS.md`、本计划文档。每批**只 `git add` 本批的具体代码文件，禁用 `git commit -a`**，勿把这些环境 / 交接文件混进行为提交。

## 进度日志

- 2026-06-28 建文档，锁定本轮批次计划（整库 19 分区并行扫描综合：67 条发现 + 19 份结构笔记，已对抗性自检并剔除已完成 / 已推迟 / 禁区项）。**尚未开始执行**——用户选择先落盘计划、暂不改代码。
- 2026-06-28 Codex 审阅 + Claude 复核：5 项事实声明逐一对源码核实**全部属实**（详见末尾「Claude 复核 Codex 审阅」）。据此修订计划：验证协议补 `codegraph sync` + 手动验证说明 + 提交卫生；A3 拆出写 / 重命名 UI 入口为 A3b；A1 改「非纯编译器证明」+ 可达性核对；A4 明确只抽 score 内核；B2 工厂移出基类至 `MusicTagWinApp.Web`；B3 加「不统一取消语义」约束。仍未改任何代码。
- 2026-06-28 **A1 完成 `ea1e618`**：删 `LimitedConcurrencyTaskScheduler.cs`（123 行）+ `ImageComboBox.cs`（165 行）+ `EditableListView` 不可达 ImageComboBox 分支与 2 个仅其调用的 helper + 2 个 orphan using；删 `Resources.cs` 4 个死 `Enum_*` 访问器（`.resx` 键留）。Debug+Release 0 warn、smoke 通过；仅暂存 4 个代码文件。
- 2026-06-28 **A2 完成 `de1af6b`**：`DatabaseMapper.GetLogSubdirectory` 收敛 7 个 `Get*LogDirectory`、`ComputeMd5HashString(string)` 委托 byte[] 重载；`TagHistoryRepository.ExecuteNonQueryLogged` 收敛 3 个 CRUD 包装、2 处 catch 改 `MarkTransactionFailed()`。仅暂存 2 个数据层文件。Debug+Release 0 warn、smoke 通过。
- 2026-06-28 **A4 部分完成 `e7afa05`**：⭐ `ResolveNonInstrumentalCandidate` 收敛 `PromoteBestMatch` 7 处 instrumental 三元式（净 −14 行）；其余 3 子项（CompareScoresDescending / ReplaceFullWidthPunctuation / memoize）评估后**否决**（语义不等价 / 双实现风险 / 越界优化，详见 A4 批次）。Debug+Release 0 warn、smoke 通过；仅暂存 `TrackSearchResult.cs`。
- 2026-06-28 **A6 部分完成 `48aa162`**：`EditableListView` 3 个 `IComparer` 嵌套类（+3 死构造器）收敛为 `SortableTextComparer`（`Func<object,string>` 选择器，调用点传 lambda；列号支用不可变 `e.Column`）；`CustomColumnsDialog` 上/下移镜像对合并为 `MoveSelectedColumn(delta)`（瘦事件处理器保留供 designer 按名接线）。item 2（`DrawCenteredImage` 跨 5 文件居中绘图上提）**推迟**（需逐点像素等价 + 手动 UI 说明）。净 −72 行，Debug+Release 0 warn、smoke 通过。
- 2026-06-28 **A6 item 2 完成 `67f3132`**：`DrawCenteredImage` 上提到基类 `DrawableListViewSubItem`，统一 4 处(ImageSubItem/ImageListSubItem/CheckBoxSubItem/EditableListView 封面)居中绘图——4 处中心数学逐字节相同,可证像素等价。A6 全批完成。Debug+Release 0 warn、smoke 通过。
- 2026-06-28 **A8 部分完成 `0eaefa6`**：`ListViewFileSetting.AddForDir(fileInfo)` 委托路径重载、`Program.Main` 内联 `RunApplication`、`ReplaceAll` 提取 `textBox.Text` 本地量、`SourceOrderControl` 移动处理器复用 `CanMove*`。`ChangeTags` 批常量提循环**推迟**（写路径）、`TagTextEncoding` "=>" 拆分**否决**（switch default 用不同输入，未注册含 => 名不等价）。Debug+Release 0 warn、smoke 通过。
- 2026-06-28 **A5 部分完成 `e8bd2b1`**：`AbsorbTranslatedLines` 收两译文吸收循环、`FormatLyricLine` 6 处时间戳守卫→局部函数 `AppendTimestamp`、`CreateMergedProcessor` 收两下载包装。LRC 描述符表统一（altitude）**推迟**。Debug+Release 0 warn、smoke 通过。
- 2026-06-28 **A9 4 项完成 `4bb28c6`**：`IsSameTrackMetadata` 收 lyric/cover 两处 Title/Artist/Album 三元等值；`SaveSidecarFiles` 统一封面+歌词侧车保存两块（file-only 分支保留 allSucceeded→success/failed 计数，tag-save 后分支忽略返回值）；`RunSourceSearchPass` 合并主/次源搜索两 pass（`IsSecondarySource==secondary` + `useProviderRanking:!secondary`，`sourceOrderIndex` 经 `ref` 线程化保序）；inline 并删 `GetSourceFromItem` forwarder。`ExtractResultsFromRankedTracks` move-method（altitude）推迟。Debug+Release 0 warn、smoke 通过。
- 2026-06-28 **A7 完成 `caa9123`**：`DatabaseMapper.FillAndCenterButtons` 收三 dialog 相同的 fill-list+center-buttons 布局块（各保留末尾列宽行）；`PictureFromTagsDialog.GetEmbeddedPictureData` 收两处 load+取列表+null 检查（调用点各保留 continue/return 早退与 `using` 生命周期）；`FindReplaceDialog.SyncControllerInputs` 收四按钮处理器的 SearchText/MatchCase 推送。Debug+Release 0 warn、smoke 通过。
- 2026-06-28 **A3 完成 `cc1bc3c`**（7 项，纯 in-file 显示/UI/错误路由去重）：删 `FilterValueCollector`/`SelectedItemFilterValueCounter` 闭包类、两调用点复用 `AddSelectedFilterValues`；`Subscribe/UnsubscribeTagFieldTextHandlers` 收 6 订阅循环；`FormatCountDurationSize` 收 4 状态标签插值；`BuildBasicFileDisplayValues` 合并双分支；`GetLoadedFilePaths` LINQ 一行；`ConvertAllTagFields` 提工厂出循环（静态单例+纯函数等价）；`ReportAsyncOperationErrorIfNotCancellation` 收 13 取消感知 catch。Debug+Release 0 warn、smoke 通过。
- 2026-06-28 **A3b 完成 `6b61728`**（3 项，写/重命名 UI 入口，Codex 拆出重验证）：`ConfirmAndSaveTagsWithOperation` 泛化 lyrics + 2 tag CHS/CHT 处理器、`ConfirmAndConvertSelectedFilenames` 合并 2 文件名处理器、`BuildBatchResultMessage` 收 4 处 save/rename/undo 结果消息（clear 变体保留 inline）。逐字节核对 + diff 读，Debug+Release 0 warn、smoke 通过；实跑 save/rename 无 smoke 覆盖。**Tier A 全部完成。**
- 2026-06-28 **B1 完成 `f04426e`**（3 项，写标签核心）：`SaveWithId3v2Version(Action)` 收两 save 方法的版本锁/try/catch/finally；`AppendUtf8Blocks` 统一 Xiph/Ape 的 UTF8 尾；`AddIfAbsent` 收 LoadAudioProperties 6 惰性缓存。Debug+Release 0 warn、smoke 通过；写路径无 smoke 覆盖,等价靠脚手架同构/body 迁移分析。
- 2026-06-28 **B2 完成 `a906e4a`**（4 项,在线搜索弹窗收敛；B2-1 否决）：`SearchStatusIndicator.LayoutFooterStatus`（三 dialog 逐字节相同页脚块，+`using System.Drawing;`）、`BeginReporting()`（三处 `Progress<SourceSearchStatus>` 通道接线，SynchronizationContext 捕获等价）、`SourceOutcomeTracker`（新类收敛 Cover/Lyric 按源成败统计，`ReportFinal` 接 enabled-source 序列保发射集不变，Combined 单发模型留原处）、`SearchCoversBySource`（合并两 `SearchBy*` 封面方法，query 留 lambda 内延迟求值，3 参保留 `accumulatedCandidates`）。**B2-1 provider 工厂否决**：基类无搜索方法、签名异构，基类型无法 uniform 调用（CS1061），强行 `dynamic`/逐源 cast 改语义或零简化。两个 workflow：设计-等价分析（5 子项 + critic）→ 实现 → 对抗性验证（4 怀疑者 + holistic，**5/5 verdict 行为保留、0 真实差异**）。Debug+Release 0 warn、smoke 通过；联网写回路径无 smoke 覆盖,等价靠数据流 + 对抗验证。
- 2026-06-28 **B3 完成 `3257045`→`0794388`→`95b8066`**（provider 候选装配上提,3 子提交；NetEase 局部余项另立 B3-3）：**B3-4** 删 Kugou `SearchResultDetailLoader` 闭包类 + 手写枚举器→`foreach`+lambda（捕获循环变量,与 QQ/NetEase 同构）；**B3-2** 6 个 JToken 安全读取器（`GetStringOrEmpty`/`GetFirstField`/`GetStringField`/`GetNullableIntField`/`GetLongField`/`GetNullableLongField`）上提基类 `protected static`,删 NetEase(6)/QQ(4)/Kugou(1) 副本（NetEase canon 逐字节；QQ/Kugou 内联版在 JSON-null 边界归约为 canon,实证等价）；**B3-1** 新增 `BuildOrderedTracks`/`BuildOrderedLyrics`/`BuildDedupedCovers` 泛型 helper（+`using System.Collections.Generic`+三 result-model 命名空间）,折叠 QQ tracks/lyrics/covers + Kugou tracks/lyrics + Kuwo tracks/lyrics/covers + NetEase covers 共 9 法（各 provider 经构造委托保留本源字段/URL 模板/DeferredLyricLoader；NetEase tracks/lyrics 因 cover-gate + `EncodeMusicComment` 留 B3-3）。Phase-A 去重集 / Phase-B-C 物化坍缩 / Kugou 反转取消 / 封面 build-every / SearchSongs 单调用 均证等价。对抗验证 **13 怀疑者 + critic**（含实编译 v13 JSON 对照 + 强制 Release 重编）→ **全等价、0 真实差异**；唯二代码级 delta（Dictionary→HashSet null-key、Kuwo `Concat`/`Any` ANE→NRE）经核均不可达。Debug+Release 0 warn、smoke 通过；联网路径无 smoke 覆盖,靠数据流 + 对抗验证。
- 2026-06-29 **B3-3 完成 `11c32eb`**（NetEase 局部 3 项）：抽 `BuildEncryptedPostBody`（`SearchSongs`/`LoadSongDetails`/`LoadAlbumDetails` 三处 params/encSecKey 格式化逐字节相同）、`PostSongQuery`（合并同构的 `SearchSongs`+`LoadSongDetails`,日志标签参数化保 "SearchMusic error:" / "SearchSongDetail error:" 串；payload 移到调用点 try 外,对字面 JObject 无异常）、`FormatPublishYear`（`GetAlbumReleaseYear`+`SearchTracks` 共用;失败/溢出返 null == 原 `track.Year` 不赋值的 null 默认,经查 `TrackSearchResult.Year` 为纯 auto-property）。**`SearchTracks` 单 `HashSet` dedup 坍缩仍推迟**（cover-gate +`EncodeMusicComment`,NetEase 最高 churn）。对抗验证 3 怀疑者 + critic（含独立 Release 构建）→ 全等价、0 真实差异。Debug+Release 0 warn、smoke 通过。
- 2026-06-29 **B4 完成 `4289394`**（ProgressDialog 事件现代化，Tier B 末项）：手写双委托字段 + 4 个 Add/Remove 方法 + 2 个静态 `Interlocked` CAS 助手（`AddCallback`/`RemoveCallback`）→ 两个 C# field-like `event`（`CancelRequested`/`ProgressUpdate`），删孤儿 `using System.Threading`，留 `ProgressDialogCallback` 委托类型；30 个调用点（24 `StateFieldInstance` = 22 Add→`+=` + 2 Remove→`-=`、4 `FilenameRelatedBatchDialog`、2 `AutoMatchTagsDialog`）改 `+=`/`-=`，git numstat 增删逐文件配平、零方向翻转、零错配接收者。等价依据：Roslyn 把 field-like event 的 add/remove 访问器 lower 成 `Delegate.Combine/Remove` + `Interlocked.CompareExchange` CAS 循环，与手写助手同构。对抗验证 **6 怀疑者**（各以仓库实 `csc`/`ildasm`/运行时探针攻一条等价主张）+ 仓库实据综合：5 条（类内读 backing 字段、调用点映射、public 表面无重名、删 using 安全、字段用法完备）确证等价；A-lowering 高置信编译器实证**一条不可达 delta**——手写循环 `CompareExchange(...) != previous` 两端委托类型使 `!=` 绑 `Delegate.op_Inequality`（**值**比较调用列表，IL `call op_Inequality`），Roslyn 访问器用**引用**比较（`bne.un`）；仅当并发 writer 在「读」与「CAS」之间换入值等引用异的多播实例才分歧（手写或把失败 CAS 误判成功而丢 subscribe/unsubscribe，事件形重试），而 ProgressDialog 经 `StartAddAnyFiles` 等核实：全部 `+=`/`-=`/invoke 在 WinForms UI 线程串行（订阅在 `ShowDialog` 前同步执行；唯一 Remove 对在 `async void` 的 `finally`、经 await 捕获的 UI `SynchronizationContext` 恢复；后台 `Task.Run` 体只调方法、不碰订阅），CAS 从不竞争 → 两形逐次等价，可达处事件形严格更正确（修掉一处潜在丢更新竞态）。性质同 B3-1 已记录的不可达 delta（Dictionary→HashSet null-key、Kuwo `Concat`/`Any` ANE→NRE）。Debug+Release 0 warn、smoke 通过。**Tier B 四批（B1/B2/B3/B4）主体完成。**
- 2026-06-29 **B3 收尾 `d0e53c1`**（NetEase `SearchTracks` 单 `HashSet` dedup 坍缩，兑现 B3-3 末项推迟）：删 `Dictionary<string,TrackSearchResult> tracksById` + 并行 `List<string> trackIdsInOrder` + 末尾重建循环，改单 `HashSet<string> seenTrackIds` + 直接 `tracks.Add(track)`，与其它 provider 经 `BuildOrderedTracks` 的 HashSet+直接 add 同惯用法。原推迟理由（cover-gate +`EncodeMusicComment` 后步）仅阻碍折叠到基类 `BuildOrderedTracks`（需给共享 helper 加可选谓词+后步），**不阻碍本地坍缩**——cover-gate 留 dedup 条件内、`EncodeMusicComment` 留 post-loop，均逐字未动。等价归纳证明：两版 seen 集（`tracksById.Keys` / `seenTrackIds`）皆仅在同一三连词 gate 内填充 → 逐轮相等 → 同序接受同对象；`tracksById[id]` 恒返回首次接受对象（`Dictionary.Add` 仅首次触发、`!ContainsKey` 禁覆盖），重建即等于直接 add。唯一结构差异（`Dictionary` 对 null 键 `ContainsKey`/`Add` 抛 ANE、`HashSet` 容忍）**不可达**：`SourceTrackId = song.Id.ToString()` 而 `song.Id` 为 `long`，永非 null。对抗验证 **3 怀疑者 + critic** → 全等价、0 真实差异。Debug+Release 0 warn、smoke 通过。**注：NetEase tracks 折叠到 `BuildOrderedTracks` 基类仍按 B3-1 推迟（altitude / 需泛化共享 helper）。**

---

## Codex 审阅记录（2026-06-28）

结论：计划总体有价值，但不建议按当前版本直接执行。部分批次低估了耦合、依赖方向和验证成本；应先修订计划再开批次提交。

### 需修改的关键点

- **B2：不要把 provider 工厂放进 `RemoteTagProviderBase`。**
  - 当前计划写 `RemoteTagProviderBase.CreateProvider(source,cts)`，但 `RemoteTagProviderBase` 现在主要承载 HTTP、下载、状态上报基础设施，且已有 `this is QqMusicTagProvider` 下载头特例。
  - 继续让基类认识全部具体 provider 会固定反向依赖，增加基类职责。
  - 建议改为放到 `MusicTagWinApp.Web` 的独立小工厂，或放在搜索对话框私有 helper 中。B2 可保留“收敛 switch”的目标，但不要把类型映射塞进基类。

- **A3：`ConfirmAndSaveTagsWithOperation` 不是普通安全去重。**
  - A3 标题称“不触在线搜索 / 写标签核心”，但 `StartBatchLyricsOperation`、`ConvertSelectedTagsTraditionalToSimplified_Click`、`ConvertSelectedTagsSimplifiedToTraditional_Click` 都会进入 `StartCommonSaveTags` 写标签入口。
  - 该子项应从 A3 拆出，按“写标签 UI 入口”单独成批。
  - 验证必须覆盖确认文案、选中文件预览、只读文件处理、取消分支、进度弹窗是否仍按原路径显示。

- **验证协议缺 `codegraph sync` 和手动验证说明。**
  - `AGENTS.md` 明确要求代码改动结束前运行 `git diff --check` 和 `codegraph sync`。
  - `AGENTS.md` 还要求触及 UI、tag writing、online search、resource loading、interop 时补充 focused manual verification notes。
  - 建议把每批验证协议改为：`.\scripts\Verify-Build.ps1 -RunSmokeTests` → `git diff --check` → `codegraph sync` → 必要手动验证说明 → 提交。

- **A1：方向成立，但“编译器即证明无引用”说法过强。**
  - CodeGraph 抽查显示 `LimitedConcurrencyTaskScheduler` 和 `ImageComboBox` 没有静态调用方。
  - 但 `EditableListView.CommitComboBoxEditorSelection` 存在 `editor.GetType() == typeof(ImageComboBox)` 运行时类型分支，删除 `ImageComboBox` 时同步删分支是行为面收窄，不只是普通类删除。
  - 执行前应记录已核对无 designer / 动态创建入口，执行后跑完整 smoke。

- **A4：`CompareScoresDescending` 只能抽 score 比较，不能抽完整 comparer。**
  - `TrackSearchResult` 的排序 tie-break 是 `SourceOrder`、`SearchPass`、`ResultOrder`。
  - `LyricSearchResult` 的排序 tie-break 是 `SourceOrder`、`ResultOrder`，没有 `SearchPass`。
  - 可抽前三个 float score 的降序比较，但不要把两个完整 comparer 合并。

- **B3：候选装配 helper 要保持 provider 私有调用顺序。**
  - QQ / Kugou / Kuwo 的 `SearchTracks` 有共同的按源去重、保序、标注 `ResultOrder` / `SearchPass` / `SourceOrder` 骨架。
  - 但 `SearchSongs`、`LoadLyrics`、详情加载、取消检查、DeferredLyricLoader 捕获方式仍有 provider 差异。
  - Helper 应只接收“已经构造好的候选”或 provider 提供的构造委托，不应把联网调用顺序和取消语义统一掉。

### 建议执行顺序调整

- 先做 A1、A2、A4 中非联网、非写标签的小项。
- A3 中保存标签相关子项单独成批，不与普通 in-file 去重混在一起。
- B2 先改成“factory 不进基类”的版本，再执行布局和 `Progress<SourceSearchStatus>` 接线复用。
- B3 拆成 provider-by-provider 小提交；每个 provider 保留原始调用顺序，并附数据流等价说明。

### 开始执行前的仓库状态提醒

- 审阅时工作区已有 `D CLAUDE.md`，以及 `.claude/`、`.mcp.json`、`.repowise/`、`AGENTS.md`、`docs/SIMPLIFICATION_PLAN.md` 等未跟踪或变更项。
- 开始“一批一提交”前应先明确哪些文件属于本轮计划，避免把环境文件或交接文件混进行为提交。

---

## Claude 复核 Codex 审阅（2026-06-28）

逐条对照真实源码核验 Codex 的 5 项事实声明，**全部属实**；建议多数采纳，2 处对计划现状的轻微误读已注明。

| Codex 点 | 事实核验（源码） | 结论 | 处理 |
|---|---|---|---|
| 1 B2 工厂不入基类 | ✅ `RemoteTagProviderBase.cs:234` 已有 `if (!(this is QqMusicTagProvider))` | **采纳**（更清洁）。轻微过陈述：基类已对 QQ 有 1 处反向依赖，故是“加宽”而非全新方向 | 工厂置 `MusicTagWinApp.Web`（与 `SearchSource` 同处），不放基类 |
| 2 A3 拆写标签子项 | ✅ 三处理器均调 `StartCommonSaveTags`（@6283/6650/6673） | **采纳** | 拆出 A3b，并按同理把重命名入口、结果文案一并归入、重验证 |
| 3 协议缺 codegraph sync + 手动验证 | ✅ `AGENTS.md:23-28`（codegraph sync）、`:38`（手动验证说明） | **采纳**（我的协议确实漏列） | 验证协议已补 + 提交卫生条款 |
| 4 A1“编译器证明”过强 | ✅ `EditableListView.cs:354` 有运行时 `typeof(ImageComboBox)` 分支 | **采纳** | A1 改“非纯编译器证明”+ 可达性核对 + 完整 smoke |
| 5 CompareScoresDescending 只抽内核 | ✅ Track tie-break 含 `SearchPass`（@671），Lyric 无（@186-192） | **计划本就如此**（只抽 score 内核，未提合并整 comparer），Codex 略误读 | A4 措辞已显式化，补 Track artist-first 置换约束 |
| 6 B3 保 provider 调用顺序 | ✅ 与原 finding 一致（helper 收委托） | **采纳** | B3 加“不统一联网顺序 / 取消语义”，Kugou 反转取消流须先证等价 |

执行顺序建议与仓库状态提醒均合理，已纳入验证协议的提交卫生条款。**Codex 审阅无误报、无需推翻任何结论**；唯点 1 / 点 5 对计划现状有轻微误读（已分别标注），不影响其建议的有效性。

---

## Codex 完成项复审（2026-06-29）

范围：复审当前已勾选完成的 A1-A9 已执行子项、B1-B4 已执行子项，以及 B3 follow-up `d0e53c1`。未勾选项按用户说明视作 deferred/rejected，不按“漏做”处理。

### 结论

- 未发现需要立即回滚或阻断继续工作的实质性 bug。
- 已重新运行 `.\scripts\Verify-Build.ps1 -RunSmokeTests`，Debug/Release 构建和 3 个 smoke 通过。
- 仍需承认原计划中的覆盖缺口：写标签、重命名、在线搜索、provider 真实联网结果排序/下载路径仍没有 smoke 覆盖，本次复审主要靠 CodeGraph + 提交前后对照 + 数据流阅读，不能替代端到端手动验证。

### 已复审的高风险点

- **B1 写标签核心**：`SaveWithId3v2Version(Action)` 保留 `loadError = null`、body、`SetId3v2Version()`、`tagFile.Save()`、catch/finally 恢复全局 ID3v2 设置的顺序。`SaveCurrentTagFile` 的空 body 仍走同一保存尾部；未看到顺序回归。
- **A3b 写/重命名 UI 入口**：`ConfirmAndSaveTagsWithOperation` 与旧三处理器的确认文案、只读处理、`StartCommonSaveTags` 调用形状一致；`StartRenameFiles` 使用 `Resources.Msg_SaveCompleted` 是旧代码已有行为，不是本次合并新引入的文案错误。
- **B2 搜索状态收敛**：`BeginReporting()`、`LayoutFooterStatus()`、`SourceOutcomeTracker.ReportFinal(...)` 的调用方仍由各 dialog 提供 enabled-source 序列；Cover preferred-source 和 normal-source 两条路径均只 report 对应源集合，未发现额外源被错误标 Completed/Error。
- **B3 provider helper**：`BuildOrderedTracks` / `BuildOrderedLyrics` / `BuildDedupedCovers` 没有发起网络请求，只接收已拉取 songs 和构造/加载委托，符合“不统一联网顺序”的约束。`BuildDedupedCovers` 增加 `string.IsNullOrWhiteSpace(coverUrl)` 过滤：NetEase 原本已有该过滤；QQ/Kuwo 原循环无显式过滤，但 QQ parse 要求 album mid 非空，Kuwo fallback cover URL 由 detail URL 模板生成，当前看不到可达差异。建议保留这条为手动联网验证关注点。
- **B4 ProgressDialog 事件现代化**：调用点已变为 `+=` / `-=`，事件触发仍在 Cancel 按钮和 timer marshaled UI 回调；当前代码没有后台线程订阅/退订事件的直接路径，Claude 关于可达处等价的论证可以接受。
- **A5/A6/A8 小项抽查**：`LyricTextProcessor.AppendTimestamp` 只包住原 `omitTimestamps` 守卫；`AbsorbTranslatedLines` 保留目标行缺失时创建、已有译文回填到原文的顺序。`EditableListView.SortableTextComparer` 保留 `decimal.TryParse` / `DateTime.TryParse` 当前区域性比较。`TextBoxFindReplaceController.ReplaceAll` 使用初始 `sourceText` 做计数和批量替换，单匹配分支仍走 `Paste`，未看到行为面扩大。

### 已完成项剩余风险

- Provider 相关提交虽然通过 build/smoke，但真实网络响应、空字段、限流、解析失败、下载 404 等路径仍需人工或录制响应 fixture 验证。
- A3b/B1 写标签路径没有 smoke 真实写文件覆盖；建议至少手动覆盖“保存标签成功、只读文件取消/允许、撤销保存、批量重命名成功/跳过/失败”各一例。
- B3 的 `BuildDedupedCovers` 空白 URL 过滤对 QQ/Kuwo 目前看不可达，但如果上游 API 返回特殊空 album mid 或空 track id，应确认 UI 是否期望显示失败占位候选。

## Codex 对 deferred/rejected 项的后续建议（2026-06-29）

在“允许大范围重构”的条件下，Claude 的推迟/否决多数仍是合理的；它们不是不能做，而是不适合继续以“纯等价小简化”方式做。建议下一轮改成“先建 characterization，再做结构迁移”的模式。

- **A5 LRC 元数据描述符表**：可以做，但要先为 parse / emit / merge 建金样本。样本必须覆盖 `ar/ti/al/by/offset/re/ve/total`、未知 tag、重复 tag、空值、`offset` 只 parse+apply 不 emit 的特例，以及当前 `Substring` 截断怪癖。实现上可用 descriptor table，但 descriptor 必须允许 per-field parse/emit/merge 策略，而不是一张简单 key->property 表。
- **A8 `FilenameRelatedBatchDialog.ChangeTags` 常量提升**：不要先碰保存循环。先把 pattern-to-regex 编译步骤抽成不可变 `FilenameTagPatternPlan`，用一批文件名/模式样本验证 captures 与 tag changes 完全一致；尤其不要把当前手写 escape 链直接替换成 `Regex.Escape`，因为字符集和替换顺序可能改变语义。验证后再把 plan 移出 per-file 循环。
- **A9 move-method 到 `MetadataSearchState`**：可以推进，但不应只移动一个大方法。先把 `MetadataSearchState` 的职责边界固定为“候选累计、limit 消耗、ranked extraction”，再用小步骤迁移纯数据操作；保留 owner/dialog 依赖在外层。方法名已陈旧时，先按当前代码重新命名目标行为，避免按旧计划机械移动。
- **B2 provider 工厂**：不建议做“返回 `RemoteTagProviderBase` 的统一工厂”。若要大改，先定义显式能力接口，例如 `ITrackSearchProvider` / `ILyricSearchProvider` / `ICoverSearchProvider` / `ITrackLyricLoader`，并让每个 provider 只实现真实支持的能力；调用方按能力分派。这样可以消除 switch，但会是架构改造，不是 helper 提取。
- **B3 caveat**：这不是任务项。后续真正值得做的是 provider fixture 测试：把 QQ/NetEase/Kugou/Kuwo 的典型响应、空响应、解析失败、限流响应落成样本，验证 `SearchTracks/SearchLyrics/SearchCovers` 的候选数、顺序、SourceOrder/SearchPass/ResultOrder、URL、错误状态。
- **Tier C / StateFieldInstance**：可以拆，但必须分阶段。优先抽可独立测试的 batch runner skeleton：`BatchOperationContext` 只管 cancel/progress/error-log/close-dialog；每个具体 task 仍保留自己的业务 body。`CoverPreviewController`、VirtualMode 选择模型、Designer partial 应分三批，不要和 batch runner 混在一起。
- **Tier C / DatabaseMapper**：建议做命名空间级拆分，但保持 public facade 一轮不动。先新建 `ImageUtilities`、`LogPathService`、`DialogService`、`PathUtilities`，让 `DatabaseMapper` 委托过去；下一轮再逐步改调用点。这样可以控制 blast radius。
- **Tier C / TagHistoryRepository UndoStore**：可抽，但需要先记录事务边界和进程级静态状态。`UndoStore` 应只管理内存 undo 栈和 spill-to-disk，SQLite/history transaction 仍留在 repository，避免把持久化和撤销状态同时迁移。
- **Tier C / ConfigDescriptorState 字段词汇表**：可以用 descriptor table，但 descriptor 需要支持真实不对称：`ReadFieldText` 的 `trackstr/discstr` 派生、ID3v2 不覆盖 comment/lyrics 普通 text frame、Xiph/Ape field id 差异、读写类型不同。建议先加内部 snapshot 测试，再迁移一个字段族。
- **Tier C / CombinedTagSearchDialog**：建议先抽封面下载/缩略图子系统，因为它和 `CoverSearchDialog` 的重叠更可验证；track-search 三元组可后置。抽出后用 UI 手动验证“列表增量显示、封面加载失败占位、取消关闭、缓存复用”。
- **OptionsDialog / AutoMatchWorker 大拆分**：如果真的要做，先禁止行为重写，只做 presenter/service seam：OptionsDialog 保留 designer 和控件事件，抽纯 load/save mapping service；AutoMatchWorker 先抽 immutable input/result DTO，再迁移 worker body。没有 UI 手动验证清单前不建议动。

---

## Claude 复核 Codex 完成项复审 + Phase 2 启动（2026-06-29）

6 路并行验证 workflow（每路独立对照源码 + git 历史核验一条 Codex 声明）+ critic 综合 → **Codex 的「完成项复审」与「后续建议」两节全部属实（critic: fully-correct，6/6 验证点 correct，0 误报）**。

| 验证点 | Codex 声明 | 核验结论 |
|---|---|---|
| V1 封面过滤 | `BuildDedupedCovers` 给 QQ/Kuwo 新增 `IsNullOrWhiteSpace` 过滤（NetEase 原有），当前不可达 | ✅ `git show 95b8066~1` 证 QQ/Kuwo 原循环无空白跳过；两源封面 URL 均非空模板字面量（`string.Format` 不返 null）+ QQ 解析 guard 要求 `Mid.Any()` → delta 不可达，保留为联网手验关注点 |
| V2 重命名文案 | `StartRenameFiles` 用 `Msg_SaveCompleted` 是原反编译行为，非 A3b 引入 | ✅ `git log -S` 证该串 `406ce91`（初始恢复）入档、非 `6b61728`；无 `Msg_RenameCompleted` 资源 |
| V3 B1 顺序 | `SaveWithId3v2Version` 保 null/body/SetVersion/Save/catch/finally 顺序 | ✅ 空 body 仍走同尾；逐字节核对 |
| V4 B2 ReportFinal | 只发射调用方给的 enabled-source 集 | ✅ Cover preferred/normal 两路各只报自身源集 |
| V5 小项 | AppendTimestamp/AbsorbTranslatedLines/SortableTextComparer/ReplaceAll 忠实无扩大 | ✅ 4 项逐一核对 |
| V6 后续建议前提 | A9 方法名陈旧；DatabaseMapper ~50 方法无一 DB；provider 异构 | ✅（且更强）`ExtractResultsFromRankedTracks` 全库不存在（仅 `SearchAutoMatchMetadata` 内 inline 块 ~1085-1169）；DatabaseMapper 实为 ~72-75 方法、零 DB 访问；4 provider 无能力接口、Kugou 无 `SearchCovers`、NetEase 带 `knownSongId`、Kuwo loader 单数名 |

唯三处「轻微」均**强化而非削弱** Codex：V1 QQ 模板字面量本身即保非空（不必靠 guard）；V5 SortableText 逻辑在 `CompareSortableText` 助手内；V6「~50」低估为 ~72、且能力接口仍需逐方法签名调和。

**Phase 2 行动项**（critic 汇总，按 Codex「先 characterization 再结构迁移」路线）：
- 🔴 触 provider / 写标签 / 重命名结构迁移前，先落 characterization/fixture（典型/空/解析失败/限流响应 → 断言候选数 / 顺序 / URL / 错误态）+ 手动写路径覆盖。
- 🟡 A8 勿用 `Regex.Escape` 换手写转义链；A9 按当前代码重命名目标（inline 块在 `SearchAutoMatchMetadata`，计划行号 `1093-1176` 已陈旧）；B2 能力接口仍需逐方法签名调和。
- 🟢 保留 `BuildDedupedCovers` 空白过滤为联网手验关注点。

**Phase 2 首批选定：`DatabaseMapper` 命名空间级拆分**（Tier C / DatabaseMapper，行 287）——blast-radius 最小（非写、非联网、纯工具方法），新类置同命名空间 `MusicTagWinApp.Instances` 使扩展方法（`GetMessageChain`/`GetStringRespectingUtf16Bom`）移动对调用点透明，非扩展方法调用点 `DatabaseMapper.X→NewClass.X` 由编译器强校验完整性（漏一处即 CS0117）。

**Phase 2 首批完成：`DatabaseMapper` 命名空间级拆分（2026-06-29）** —— 误名神类（实测 ~72 方法、零 DB 访问）按职责拆为同命名空间 `MusicTagWinApp.Instances` 下 5 个内聚静态类，原 `DatabaseMapper.cs` 删除。**采用直接迁移**（用户授权大范围重构，故不留 Codex 建议的 facade 委托）：每簇逐方法体「字节级」搬移 + 调用点 retarget + build/smoke + 独立 byte-identity 核验（脚本从 `git show HEAD:DatabaseMapper.cs` 提取原方法体逐字节比对，EOL 归一）+ commit。

| 簇 | commit | 内容 | 调用点 retarget |
|---|---|---|---|
| `TextUtilities` | `422a924` | 文本/哈希/编码/杂项（含 2 扩展方法 `GetMessageChain`/`GetStringRespectingUtf16Bom`） | 52 |
| `PathFileUtilities` | `70c8824` | 路径/目录/文件 + 歌词存盘路径（17 法） | 45 |
| `ImageUtilities` | `dd49a77` | 图像缩放/DPI/编解码/资源位图缓存（17 法 + 3 字段 + 半个 cctor） | 203 |
| `LogService` | `e93df96` | 日志目录解析 + 操作/异常日志写入（19 法，含 7 个 expr-bodied + `startupLogFileName` + 半个 cctor） | 21 |
| `DialogService` | `f7f3811` | 消息框/确认/资源管理器/设置保存（8 法）；`DatabaseMapper` 至此清空并删除 | 88 |

- **扩展方法透明**：新类同命名空间，`ex.GetMessageChain()` 等实例式调用按命名空间解析，无需改调用点；非扩展方法由编译器强校验 retarget 完整性，最终全库 **0 残留 `DatabaseMapper` 代码引用**（仅各新类 header 注释保留 1 行历史出处）。
- **静态构造函数拆分**：原单 cctor 初始化 `imageMimeMappings`/`startupLogFileName`/`resourceImageCache`；拆后 image 两字段入 `ImageUtilities` cctor、`startupLogFileName` 入 `LogService` cctor，`DatabaseMapper` cctor 随类删除。三初始化互相独立、保留显式 cctor 以保 `beforefieldinit` 语义。

**对抗验证（5 lens perspective-diverse skeptics + 自核代码）发现 1 处真实 divergence + 显式修正 `da0a847`**：cctor 拆分使 `startupLogFileName` 的渲染时机从「culture 重置前（启动 OS 区域日历）」推迟到「重置后（应用语言）」——`ToString("yyyy-MM-dd HH_mm_ss")` 单参重载按 `CurrentCulture` 日历渲染年份；原单 cctor 由首次 image 访问（`GetDpiScale`，`StateFieldInstance.cs:3211`，在 culture 重置 `3481-3482` 之前）触发，拆后改由首次 log 访问（用户操作/异常，均在重置后）触发。非公历默认日历区域（th-TH 泰历、ar-SA 伊斯兰历、fa-IR）操作日志文件名年份因此改变（如 2569→2026）；公历区域（含开发/CI）不可见，故 build+smoke 未捕获。**用户裁定：改用 `CultureInfo.InvariantCulture`，作为「显式行为修正」记录（非纯行为保持）**——文件名年份恒公历、与时机/UI 语言均无关，既消除拆分 divergence 又修掉潜伏 i18n 缺陷（与 `NetEaseMusicTagProvider.cs:126`/`QqMusicTagProvider.cs:192` 既有 InvariantCulture 修复同源）。注：`LogService.cs:52` 逐行时间戳与 `StateFieldInstance.cs:4279` 文件时间显示同属 culture-sensitive，但拆分前后均在重置后渲染（pre-existing 行为），**未动**。

**Phase 2 characterization 基础设施落成（2026-06-29）** —— 进入剩余高风险批次（provider 解析 / 写标签 / 重命名 / god-form / ConfigDescriptorState）前，按 Codex「先 characterization 再结构迁移」路线，在此前无测试套件的项目里立起回归网，锁定「当前实际行为」golden master，使后续大范围重构可对比验证行为未变。

- **宿主形态（用户拍板）**：独立 `src/MusicTag.Tests`（`Microsoft.NET.Sdk.WindowsDesktop`，net481，x86，`OutputType=Exe` console）+ 自写极简断言（`TestRunner.cs`：`Check.Equal/True/Null/NotNull` + 收集→逐个跑→打印 PASS/FAIL→退出码 0/1），**零第三方框架**，保持主项目零-NuGet 纯净度。已注册进 `MusicTag.sln`；`scripts/Verify-Build.ps1` 加 `Invoke-CharacterizationTests`（跑 Release 测试 exe，退出码非 0 即 throw），挂 `-RunSmokeTests` 下、3 smoke 之前。
- **可见性**：`src/MusicTag/Properties/AssemblyInfo.cs` 加 `[InternalsVisibleTo("MusicTag.Tests")]`（纯可见性、零运行时行为），使测试程序集可达 internal provider / 工具类。
- **provider 注入点**：测试子类继承 internal provider、override `protected virtual PostString`/`GetResponseString` 喂录制 JSON fixture，从 public `SearchTracks`/`SearchLyrics`/`SearchCovers` 端到端驱动**真实解析链**（含 `NetEaseCrypto` 加密、去重、排序、`ParseFailed` 回填）。无 mock HttpClient、无反射、不联网（`CreateHttpClient`/`GetHttpClient` 永不触发）。4 provider 同构（均继承 `RemoteTagProviderBase`、共享 protected virtual HTTP 注入点），此模式可直接复用。

| commit | 内容 | 测试 |
|---|---|---|
| `b2f9a11` | harness 骨架 + IVT + Verify-Build 集成 + 零依赖自检 | 4 自检（`TextUtilities` 纯确定性、locale 无关不变式：UnixEpoch→1970 UTC / UrlEncode 空格→%20 / CoalesceNonBlank / MD5("") 公认常量） |
| `cd443c0` | NetEase 解析 golden master（注入点验证） | 典型 2 结果（Id/Title/Artist/Album/Year/Comment/ResultOrder/SearchSource）/ 空结果→0 / 同 id 去重→1 / HTTP-200 不可解析→`ParseFailed` |

- **副产实证**：`Settings.Default` 在 console 测试宿主按 `musictag/MusicTag.config` 默认值工作（`ConnectorsArtists`=`/` / `CommentTagWrite163Key`=False / `TrackSearchResult` static cctor 读 `CombTagsInfo_SourceItemList` 均正常）；`Newtonsoft.Json`/`System.Data.SQLite`/`MusicTag.db` 等依赖经 ProjectReference 自动传递到测试 bin。
- **provider 覆盖完成（2026-06-29）**：4 provider 解析全部 characterize（22 测试：4 自检 + NetEase 4 + QQ 5 + Kuwo 5 + Kugou 4），各一个 commit（`9f2f0a4` QQ、`6af2ce7` Kuwo、`770a27e` Kugou）。注入点二分：NetEase/QQ 走 `PostString`（POST），Kuwo/Kugou 走 `GetResponseString`（GET）；均从 public `SearchTracks` 端到端驱动真实解析链，锁定字段映射 / 过滤 / 去重 / 排序 / `ParseFailed` 回填 + 各源特有行为：
  - QQ：空 album guard（`Album.Id>0 && Mid 非空 && Name 非空`）、限流 2001 → `Retrying` 上报（测试用 StatusReporter 回调首次上报即 cancel，避开真实指数退避）、`title`/`name` 双字段。
  - Kuwo：`TrackId` 去 `MUSIC_` 前缀、album-first / fallback 选取、Title&Artist 必须非空。
  - Kugou：**NO covers**（`track.Cover==null`）、`DurationMs` 秒 ×1000、按 `audio_id` 过滤。
- **后续扩展**：写标签 / 重命名路径 characterization（需临时音频文件 fixture），作为 write/rename 结构批次的前置回归网。

**联网 cover/lyric characterization 扩展（2026-06-30）** —— 承 provider `SearchTracks` 覆盖，补齐前文「provider fixture 测试」建议里的 `SearchCovers`/`SearchLyrics`（共 +23，105→128）：
- **封面（`993aa69f`，+10）**：NetEase/QQ/Kuwo 的 `SearchCovers`。锁定 CoverUrl 映射（NetEase=al.picUrl；QQ=`photo_new/...M000{album.mid}.jpg`；Kuwo=web_albumpic_short 拼 `500/` 高清直链，缺失则回退 `songinfoandlrc` 详情 URL）+ 基类 `BuildDedupedCovers` 去重三分支（空 CoverUrl / 重复 / existingCovers 已有）+ ParseFailed。封面下载是延迟闭包（`CoverDownloader`），不触发，无需 mock 图片字节；Kugou 无封面不列。
- **歌词（`13029ef2` NetEase/QQ + `adbfbef2` Kugou/Kuwo，+13）**：四源 `SearchLyrics`。注入按 HTTP 调用分流——NetEase/QQ 搜索走 `PostString`、歌词走 `GetResponseString`（分别 override）；Kugou/Kuwo 搜索与歌词同走 `GetResponseString`，Stub 按 URL 关键字分流（`get_krc` / `songinfoandlrc`）。锁定 Lyric/TranslatedLyric/字段映射/ResultOrder/SourceOrder：NetEase 直取 lrc.lyric+tlyric.lyric；QQ base64-in-jsonp（fixture 用 `Convert.ToBase64String(UTF8)` 动态编码）；Kugou 直取 data.lrc；Kuwo 详情 lrclist 逐行 `FormatTimestamp` 厘秒 `[mm:ss.cc]` 拼装。均用单语规避 QQ `AlignAndSplitTranslatedLyric` / Kuwo 双语重排（留边界外）。空歌词→0、NetEase existing TrackId 去重、搜索 HTTP-200 不可解析→ParseFailed。
- 全程注入点不发起网络、不触发下载/对齐——纯解析链 golden master。

## Phase 2 首个结构重构：B2 provider 能力接口 + dispatch 收敛（2026-06-29）

承 characterization 回归网，落实 B2 正解——**显式能力接口 + 按能力分派**（基类型工厂路线已否决：基类不声明搜索方法、返回值无法 uniform 调用）。4 联网 provider（NetEase/QQ/Kuwo/Kugou，命名空间分散、均继承 `RemoteTagProviderBase`）方法签名异构；3 搜索 dialog 散落 5 个 `switch(SearchSource)` dispatch，逐源 `new XxxProvider` 后调用。

**铁律**：22 characterization 经子类 override `protected virtual PostString`/`GetResponseString` 驱动各 provider 的 public **concrete** 搜索方法。故 concrete 签名与方法体**一字不动**——签名调和全用「显式接口实现转发器」，不碰 concrete，回归网每步恒绿。

| commit | 步骤 | 内容 | 风险 |
|---|---|---|---|
| `3081471` | Step 1 | 新建 4 能力接口 + `IRemoteSearchProvider` + `SearchProviderFactory` + `SearchProviderPolicy`（置 `MusicTagWinApp.Web`）；4 provider 加 `: 接口` 与显式转发器。纯加法、无调用方 → 零行为 | 极低 |
| `28ac232` | Step 2 | 收敛 Dispatch E（`CoverSearchDialog.SearchCoversBySource`） | 低 |
| `521fc9a` | Step 3 | 收敛 Dispatch D（`LyricSearchDialog.DownloadLyricBySource`） | 低 |
| `e70fb7e` | Step 4 | 收敛 Dispatch C（`LyricSearchDialog.SearchLyricsBySource`） | 中 |
| `4455648` | Step 5 | 收敛 Dispatch B（`LyricSearchDialog.SearchTracksBySource`） | 中 |
| `1b1c831` | 收尾 | 移除两 dialog 因收敛而 unused 的 provider-namespace using（仅本批可追溯项；pre-existing 非 provider unused 留置） | 极低 |

- **接口设计**：`IRemoteSearchProvider : IDisposable`（暴露 `LastTransportResult`，base 已 public 提供）；`ITrackSearchProvider`/`ILyricSearchProvider` 取 NetEase 超集签名（含 `knownSongId`）；`ICoverSearchProvider`（Kugou 不实现）；`ITrackLyricLoader`（`LoadLyricsForTrack`）。
- **provider 实现**：NetEase 四接口全**隐式**（concrete 签名即超集，零新增成员）；QQ/Kugou 加 2 个 track/lyric 显式转发器（丢 `knownSongId`/`existingLyrics`）；Kuwo 再加 `LoadLyricsForTrack` 显式转发到单数名 concrete `LoadLyricForTrack`。
- **工厂/策略**：`SearchProviderFactory` 每能力一 `Create*`，经典 `switch` 造实例、返回前注入 `StatusReporter`，未知源 / `CreateCoverSearch` 的 Kugou → `null`（镜像原 `default`，与「Kugou 不实现 ICoverSearchProvider」双重保证无封面）。`SearchProviderPolicy.ResultLimit`：网易云/QQ=15、酷狗/酷我=5（B/C/E 散落字面量的单一来源）。
- **对抗性核对（i18n/NRE）**：收敛后 Dispatch C 对所有源求值 `useKnownMusicId ? trackInfo.LinkedMusicMetadata.musicId : 0L`（非 NetEase 经转发器丢弃）。核验所有 `useKnownMusicId=true` 调用点（dialog 内 gated `== Music163` 且已解引用 `LinkedMusicMetadata.musicId`；`AutoMatchTagsDialog:1186` gated `searchContext.LinkedMusicMetadata.musicId > 0L`）均保证 `LinkedMusicMetadata` 非 null → 三元对非 NetEase 短路取 `0L`、不触 NRE，严格等价。Dispatch B 各源硬编码 `knownSongId=0L`/`searchPass=0`/两个新建空列表，Kuwo 转发器把空列表映射到 concrete `previousResults`/`currentResults`（值同、无歧义）。
- **Dispatch A 完成（2026-06-30，B2 收官）**：`CombinedTagSearchDialog.SearchTracksFromSource`（per-source 多趟查询编排）收敛到粗粒度 `ICombinedTrackSearch`（封装整个多趟编排，区别于 `ITrackSearchProvider` 单次）+ 工厂 `CreateCombinedTrackSearch`（3 源：网易云/QQ/酷我，无酷狗 = 镜像原 `default`）+ 3 个实现（`{NetEase,Qq,Kuwo}CombinedTrackSearch`，各把原 case 体逐字节搬入，concrete `SearchTracks` 调用一字未动 → 22 characterization 恒绿）。每源多趟（网易云 linked 1 趟 / 非 linked 3 趟、QQ 3 趟、酷我 1-2 趟含 `!results.Any()` gate）+ 趟间取消 + 趟2/3 条件逐字保持；`LastTransportResult` 由「读已 Dispose provider」改为「`using` 存活期内读」（值同——`Dispose()` 只释放 httpClient/CTS、不碰该 auto-property）；provider Dispose 时机延后到方法结束（其间仅 `ReportSourceOutcome` 读属性，无 HTTP、无副作用）；`SearchTracksFromSource` 签名不变 → `AutoMatchTagsDialog` 零改动。**3-lens 对抗验证（等价/回归/完备-数据流）全 pass**（逐源逐趟数据流核对，联网路径无 smoke 故为主要回归网）。至此 **5 个 dispatch 全部收敛完成（B/C/D/E + A）**。
- **不变量保持**：`SearchSource` 序数、`SourceItem` JSON 键、4 个 static dispatch 签名（含可选尾参默认值）→ `AutoMatchTagsDialog` 零改动；per-source 上限字节级复刻；StatusReporter 搜索前注入 / transportSink 搜索后 Invoke 的时序不变。
- **验证**：每步 `Verify-Build.ps1 -RunSmokeTests`（Debug+Release 编译 + 22 characterization + 3 smoke）全绿。

## 写标签/重命名 characterization 扩展（2026-06-30）

承 B2 能力接口批次末尾「后续扩展：写标签/重命名路径 characterization」，按用户「先纯逻辑后音频」决策，为写标签（`ChangeTags`）/ 重命名（`RenameFiles`）/ `ConfigDescriptorState` 读写路径建 characterization 网，锁定现状为后续结构迁移的回归基线。测试总数 **22 → 103**（+81）。

**铁律延续**：concrete 业务逻辑一字不动。可测性经两手段达成——**可见性放宽**（`private`→`internal`，纯可见性零逻辑）或**纯逻辑提取**（内联→`internal static`，逐字节搬移 + 调用点 retarget，build+smoke+diff 兜底）。每子批一 commit、各跑 `Verify-Build.ps1 -RunSmokeTests` 全绿。

### 纯逻辑批（先做，无 fixture）

| commit | 目标 | 手段 | 测试 |
|---|---|---|---|
| `d0a61ff` | `ConfigDescriptorState.ParseNumberAndCount` / `ToSingleValue` | 可见性放宽 | 13 |
| `ec2ca03` | `FilenameRegexCaptureExtractor`（文件名正则捕获 + 括号保护段 masking） | 可见性放宽（`private sealed`→`internal sealed`） | 5 |
| `7211d33a` | `RenderRenameFilename`（@1-8 模板渲染 + 非法字符清理） | 提取自 `RenameFiles` 内联 | 8 |
| `fb8ad017` | `PendingTagUpdate`（数字门控 `SetNumberedTag` + disc/track 占位符 `SetFilenamePatternTag`） | 可见性放宽 | 13 |
| `9045add9` | `BuildFilenameMatchRegex`（模板→匹配正则 + token 提取） | 提取自 `ChangeTags` 内联 | 6 |
| `78be2666` | `SplitCombinedDiscTrackCapture`（disc/track 组合 token 数字前缀拆分） | 提取自 `ChangeTags` 内联（返回赋值序列隔离副作用） | 6 |
| `13149346` | `GetDisplayValue`（显示格式化）+ `FormatDurationWithMilliseconds` / `FormatDurationHms` | 零放宽（全 public，空构造 + indexer 填 dict） | 16 |
| `e641ae11` | `ValidateFilenamePatternCore`（模板输入校验） | 提取自 `ValidateFilenamePattern`（纯判定→enum，UI 层翻译消息） | 10 |
| `b8c9ba88` | `PendingTagUpdate` 文本 case（@1/@2/@3/@6/@7/@8 经 `SetTextTagIfChanged` 变更门控 + tagName 映射）+ `ApplyChanges` 写回 TagState | 零放宽（全 public） | 11 |
| `9ea53a36` | `PendingTagUpdate.SetRegexCaptureTag`（regex 捕获组序号 1-8→标签键；文本盲写 vs disc/track `SetNumberedTag` 门控，与 @N 路径语义分叉）| 提取自 `ChangeTags` 内联 switch（byte-identical） | 12 |
| `4a6a89ea` | `ResolveDestinationAudioPath`（`RenameFiles` 的 `(N)` 冲突 dedup：base 路径构造 + 同名/纯大小写改名豁免 `OrdinalIgnoreCase` + ` (N)` 从 1 递增首个空位）| 提取自 `RenameFiles` 内联 + 注入 `Func<string,bool>` 存在谓词（生产传 `File.Exists`，byte-identical） | 12 |
| `bdd45fc0` | `IsRequiredTagMissing`（@1/@2 必填校验：模板含 @1/@2 占位符却 title/artist 空 → 跳过计失败；`else if` 短路 + `!value.Any()` 空判据）| 提取自 `RenameFiles` 内联（byte-identical，保 `else if` + 局部变量）| 7 |
| 本批 | `ResolveRelatedFileTarget`（关联文件 lrc/封面目标 + 防覆盖：source `null`→`null`、目标存在且≠source→`null`、==source 精确/大小写→目标；lrc/image 两段合并）| 提取自 `RenameFiles` 内联 + 注入 `Func<string,bool>`（image extension 三元保求值时机）| 7 |
| 本批 | `PathFileUtilities.GetSiblingPathWithExtension`（目录 + 无扩展名文件名 + extension 纯拼接，不智能加点）| 零放宽（已 `public`，纯函数加测试）| 6 |

### 音频 round-trip 批（后做，自包含 fixture）

| commit | 目标 | fixture | 测试 |
|---|---|---|---|
| `09de9f22` | `LoadBasicTagFields`→改字段→`SaveTagFields`→重读 端到端 + **163-key COMM clear** | **程序化构造最小有效 MP3**（MPEG-1 Layer III 帧头 `0xFF FB 90 64` + 静音，4 帧 1668B；`TagLib.File.Create` 可识别，不依赖 gitignored 真实音频，CI 可复现） | 4 |

- **fixture 自包含**：写标签 round-trip 需真实音频文件（`TagLib.File.Create` 默认 `ReadStyle.Average` 读音频属性），而 `docs/测试歌曲/` gitignored、CI 缺失。解法：测试代码程序化构造最小有效 MP3 字节（4 个相同 MPEG 帧头 + 静音填充），写临时文件，round-trip 后删。比 base64 嵌入更自解释。
- **163-key COMM clear**：CLAUDE.md 标注的 native 行为修正（`SaveTagFields` 的 `RemoveFrames("COMM")` 使带非空 description 的网易云 163-key COMM 不随 comment 编辑存活）。测试用 TagLib 直接预置带 description 的 COMM，经 `ConfigDescriptorState` 改 comment 后验证其消失。为此 `MusicTag.Tests.csproj` 加 `TagLibSharp` dll 引用（与主项目同一 `musictag/TagLibSharp.dll`，非 NuGet）。

### 已修复的 latent bug（曾锁定 IS，现修正为 SHOULD）

- `FilenameRegexCaptureExtractor` 的**括号/书名号保护段 masking 对捕获分组未生效**（曾按 characterization 原则锁定现状）。根因**单一**：`variant.Text` 误存该轮 mask **前**的原文 → 最深 masked 版本从未进入 `maskedVariants`、构造函数 `regex.Match` 退化到跑在未屏蔽的原文上（还原循环从 `matchedVariantIndex+1` 起跳过命中变体，只是旧 Text 语义下的自洽配套，**非独立缺陷**）。故 `"A (b - c) - D"` 经 `^(.+?) - (.+)$` 切成 `["A (b","c) - D"]`。
- **已作为显式行为修正修复**（非逐字节等价；CLAUDE.md 铁律的合法例外——latent bug 经 characterization 锁定后单独决策修复），三处耦合：① `variant.Text` 改存 mask **后**文本；② 还原循环起点含命中变体自身（①② 合起来使 masking 生效）；③ 占位符 `segmentIndex` 由无填充改 `:D5` 定宽。修复后：`"A (b - c) - D"`→`["A (b - c)","D"]`、嵌套 `"((a - b))"`→`["((a - b))"]`、同层两段 `"(a - b) - (c - d)"`→`["(a - b)","(c - d)"]`、书名号 `"《b - c》 - D"`→`["《b - c》","D"]`。
- **③ 的由来（3-lens 对抗验证 Workflow 发现的回归）**：①② 让"masking 生效后的还原路径"首次真正运行，暴露既有占位符格式 `\t{depth:D5}{seg}` 中 `seg` 无填充的潜伏缺陷——同层 **≥11 段**时 seg1 占位符 `\t…001` 是 seg10 `\t…0010` 的前缀，`String.Replace` 按序还原会**静默损坏第 11 段**（reachable：同人/V 家文件名可叠 11+ 个括号组，错写标签且无报错）。`seg` 同样 `:D5` 定宽后占位符等长、互不为前缀，回归消除（实践上限 10^5 段，与既有 `depth:D5` 对称、改动最小）。占位符为构造函数内临时生成/消费、不落 JSON/resx/DllImport，改格式不触犯持久化名称稳定约束。
- characterization 由「锁定现状」翻转为「断言修复后正确行为」，并补嵌套、同层多段、**≥11 段回归守卫**、书名号 4 个用例（该类 5→9），全绿。
- **同源引号缺陷（已修复，独立 commit）**：`ProtectedSegmentRegex` 的引号 4 分支 `“[^“”]”`/`‘[^‘’]’`/`『[^『』]』`/`「[^「」]」` 原**缺 `*` 量词**（前 7 分支均有），只能匹配**单字符**引号段，`「a - b」` 这类多字符段不被保护 → 内部 `" - "` 被当分隔符错切。**已各补 `*`**（→ `“[^“”]*”` 等）使多字符引号段与括号/书名号一致地被屏蔽。`*` 是修复前 exactly-1 的**严格超集**（单字符段结果不变、空段 `“”` 也匹配但透明），`[^“”]` 排除自身引号对故贪婪 `*` **不跨对**（`"“a” - “b”"`→两段）。补 6 用例（4 引号类型多字符 + 单字符回归守卫 + 空段透明，该类 9→15）。3-lens 对抗验证确认无真实可达回归，全绿。
- **仍未覆盖的保护类型盲区（正交、预先存在，记录待决）**：`ProtectedSegmentRegex` 11 分支对若干常见 CJK/全角括号**无任何分支匹配**——最显眼是 `〈〉`（单书名号，其同伴 `《》` 已覆盖，遗漏突兀）；另有 `〔〕`/`〖〗`、全角 `［］`/`｛｝`/`＜＞`（分支只覆盖 ASCII 形式）。非回归（既有缺失，引号修复既未引入也未解决），留作后续单独决策（加分支 = 新增保护行为，须与引号/masking 修复同样审慎）。

### 覆盖边界

写标签/重命名的核心纯数据变换 + 端到端 round-trip 已覆盖；`ChangeTags` 的 regex 捕获 `case 1-8` 映射已提取为 `SetRegexCaptureTag`、`RenameFiles` 的 `(N)` 冲突 dedup → `ResolveDestinationAudioPath`、@1/@2 必填校验 → `IsRequiredTagMissing`、关联文件 lrc/封面 dedup → `ResolveRelatedFileTarget`、`GetSiblingPathWithExtension` 路径拼接，均已覆盖（RenameFiles 的纯逻辑分支已清完）。

**盘点（2026-06-30，3-reader Workflow）确认的候选**（按价值）：
- [x] ~~**HIGH** `RenameFiles` 的 `(N)` 冲突 dedup（原 `FilenameRelatedBatchDialog.cs:313-326`）~~ —— ✅ 完成（本批）：提取 `ResolveDestinationAudioPath`（internal static + 注入 `Func<string,bool>` 存在谓词替代 `File.Exists`，生产传方法组、byte-identical），12 个 characterization 锁定 base 构造 / 同名 + 纯大小写改名豁免（`OrdinalIgnoreCase`）/ ` (N)` 从 1 递增首个空位 / 两位数无零填充 / 括号后缀朴素拼接 / 空目录前导反斜杠 / 扩展名大小写保留。3-lens 对抗验证 = 等价 pass + 回归 pass + 完备 concern（concern 仅指我据此补的 3 个 golden-master 边界，非回归）。
- [x] ~~**MEDIUM** 关联文件 lrc/封面目标 + 防覆盖；@1/@2 必填 tag 校验~~ —— ✅ 完成：@1/@2 校验提取 `IsRequiredTagMissing`（`bdd45fc0`，7 case，byte-identical 保 `else if` + 局部变量）；关联文件 lrc/image 两段合并提取 `ResolveRelatedFileTarget`（本批，注入存在谓词，7 case；3-lens 对抗验证 等价/回归 pass、完备 concern）。
- [x] ~~**零成本** `PathFileUtilities.GetSiblingPathWithExtension`~~ —— ✅ 完成（本批，6 case 锁定纯拼接 + anti-`Path.Combine`/anti-`ChangeExtension` quirks）。

其余刻意未罩：`RenameFiles` 外层编排 / `Cancel` / `UpdateProgress` / `SaveTagFields` 的 TagLib 写（耦合文件系统 + SQLite + UI，属集成测试范畴）、各 UI 布局/事件。

- **集成缺口（对抗验证 major finding，待 route A）**：`RenameFiles` 的【关联文件 move-gating `if (sourceXxx != null && destinationXxx != null) Move`】+【per-related-file `try/catch` 的「歌词/封面移动失败只告警、不回退已成功的音频改名」韧性不变式】无任何测试覆盖（codegraph 确认 `RenameFiles` 无覆盖测试）。`ResolveRelatedFileTarget` / `ResolveDestinationAudioPath` 本身已 characterize，但未来重构可保持它们 byte-identical 却仍回归 move-gating（如 dest==null 仍移动、调换 lrc/image 参数、移除 `try/catch` 致单个关联文件失败中止整批），两测试 suite 仍全过。需真实文件系统 fixture 的集成测试（route A 范畴）才罩得住。
- **route A 起步（本批，集成测试层）**：`PathFileUtilities.MoveFileAllowingCaseOnlyRename`（`RenameFiles` 改名/移动的【实际核心原语】）已补真实文件系统集成测试（5 case：普通改名内容保留 / case-only rename 经同目录 temp 两步中转使磁盘 casing 真变更 + 无 temp 残留 / case-only 相对路径→`ArgumentException` / 源缺失→抛 / 目标已存在非 case-only→抛 + 源保留）。自建临时目录 + `try/finally` 删除，CI 可复现、零额外依赖。**仍未覆盖**上一条的 worker 级 move-gating 编排 + per-file `try/catch` 韧性 + history/undo 回写 —— 那需 worker 端到端 fixture（route A form 2，仍待决策）。
- **route A form 3 完成（本批，提取 + 单元测试层）**：把 `RenameFiles` 的【两段对称关联文件(lrc/封面)尽力而为移动】合并提取为 `FilenameRelatedBatchDialog.MoveRelatedFileBestEffort`（`internal static`，注入 `moveFile`=`MoveFileAllowingCaseOnlyRename` 方法组 + `reportFailure`=`ReportFailure` 委托字段，byte-identical），5 个单元测试（注入 `Recorder` fake，不碰真实 FS）锁定韧性不变式核心：移动失败 → 异常被方法内 `catch` 吞掉、**不传播** → 到不了外层 `catch` → `successCount` 不回退、`failureCount` 不触发（= **不回退已成功的音频改名**）+ `reportFailure(sourcePath, ex.Message)`；gate 4 种 null 组合 no-op。3-lens 对抗验证（等价/回归/完备）**全 pass**。这填补了上方 major-finding 的【移除 `try/catch` 致单个关联文件失败中止整批】回归场景。**仍未覆盖**：音频 move 本体 + `successCount++` 计数时机 + 4 项 history/undo 回写的 worker 级端到端 —— 那耦合 static SQLite/Form/计数，注入会引入计数时机的可观测变化风险，属 form 2 端到端 fixture（仍待决策）。
