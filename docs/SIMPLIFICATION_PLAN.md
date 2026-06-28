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

- [ ] **A3 StateFieldInstance 纯 in-file 去重（无写 / 重命名副作用，仅 UI / 显示 / 错误路由）** —— `MusicTagWinApp.Instances/StateFieldInstance.cs`
  - [ ] 复用既有 `AddSelectedFilterValues`，删 2 个 filter-tally 闭包类 `FilterValueCollector`/`SelectedItemFilterValueCounter`（681-882，调用点 4682/4700-4711/5044-5049）
  - [ ] `Subscribe/UnsubscribeTagFieldTextHandlers` 收敛 6 处订阅循环（4660-4663 等，至 5082-5085）
  - [ ] `FormatCountDurationSize(count,ms,bytes)` ×4（4163,4165,4208,4211）
  - [ ] `BuildBasicFileDisplayValues` 合并双分支字典（4297-4317）
  - [ ] `GetLoadedFilePaths` 改一行 LINQ（3991-3999）
  - [ ] `ConvertAllTagFields(converter)` 把 `ChineseTextConverter` 工厂提出循环（6423-6437；仅改编辑框内存文本，需用户另存才落盘）
  - [ ] `ReportAsyncOperationErrorIfNotCancellation(ex,cts,name)` 收敛 13 处取消感知 catch（6021 等，全类；仅统一各 runner 的 catch，happy-path 不变）

- [ ] **A3b（Codex 点 2）写标签 / 重命名 UI 入口 + 结果文案去重（从 A3 拆出，单独成批、重验证）** —— `StateFieldInstance.cs`
  - 这些处理器**确实进入写 / 重命名路径**（已核实 `StartCommonSaveTags` @6283/6650/6673、`StartRenameFiles` @6685/6697），故不与纯 in-file 去重混批。
  - [ ] `ConfirmAndSaveTagsWithOperation(labelKey,comp)` 泛化 `StartBatchLyricsOperation` + 2 个 CHS/CHT 标签处理器（6271,6638,6656 → 均调 `StartCommonSaveTags`）
  - [ ] `ConfirmAndConvertSelectedFilenames(menuKey,isChsToCht)` 合并文件名 CHS/CHT 两处理器（6678-6700 → 均调 `StartRenameFiles`）
  - [ ] ⚠️ `BuildBatchResultMessage(...)` 收敛 5 处批结果消息装配（5986-6014 等，至 6243-6253，位于 save/rename/undo/clear runner）—— **用户可见文案，逐分支逐字节核对**；`StartClearTags` 变体可留 inline
  - 验证：确认文案 / 选中文件预览 / 只读文件处理 / 取消分支 / 进度弹窗 / 实际 save+rename 路径与每分支结果文案逐字节不变 + 手动验证说明

- [x] **A4 结果模型 / 相似度** —— 仅执行 ⭐ Item 1；其余 3 项评估后**否决**（`MusicTagWinApp.Roles/TrackSearchResult.cs`）
  - [x] ⭐ `ResolveNonInstrumentalCandidate` 收敛 `PromoteBestMatch` 7 处 instrumental 解析三元式（5 处自兜底 `?? 候选` + 2 处 `!= null` 守卫）—— **✅ 完成 `e7afa05`**（净 −14 行）。命名取 `ResolveNonInstrumentalCandidate`（返回可空三元结果，自兜底点内联 `?? 候选`）；**513 行有意不动**：判定原 `currentBest.Title` 却以当前 `results[0]` 为回溯种子（中途 `MoveTrackToFront` 已重排），判定实体≠回溯种子，不符助手契约
  - [x] ❌ **否决** `CompareScoresDescending` 上提：两评分循环**语义不等价**——`CompareByScoreOrder` 用 `CompareDescending`（`r.CompareTo(l)`，对 NaN/−0.0 有序）、`CompareLyricResults` 用 `>`/`<`（NaN 视作相等）；tie-break 亦不同（Track 含 `SearchPass` + `GetScoreIndex` 置换，Lyric 无）。真正共享内核须重构 Track 置换路径，风险 > 去重收益（零 smoke 覆盖的双排序路径），等价仅靠"分数域非 NaN"论证而非构造
  - [x] ❌ **否决** `ReplaceFullWidthPunctuation`：`NormalizeForMatch`（`.Replace` 单字符链）与 `NormalizeSimilarityTextCandidates`（`Regex` on `string[]`）是两套并行实现、跨上下文调用，Regex↔`.Replace` 等价性 + `string[]` 适配风险高收益低
  - [x] ❌ **否决**（效率）memoize `NormalizeForMatch`：属**性能优化非可读性简化**，引入缓存状态，超出本轮"行为保留简化"范围

- [ ] **A5 歌词处理** —— `MusicTag.Composer/LyricTextProcessor.cs`
  - [ ] `AbsorbTranslatedLines(translated)` 收敛 `MergeTranslatedLyric`/`AlignAndSplitTranslatedLyric` 两处译文吸收循环（449-469,504-523）
  - [ ] `AppendTimestamp(t)` 局部函数收敛 `FormatLyricLine` 6 处守卫 append（354-445）
  - [ ] `CreateMergedProcessor(lyric,translated)` ×2 静态下载包装（674-678,702-706）
  - [ ] ⚠️（altitude）LRC 元数据描述符表统一 parse / emit / merge 三处（93-152,287-337,470-481）—— **保留 `Substring` 截断怪癖，勿换正则**；`offset` 仍特例（仅 parse+apply）

- [ ] **A6 ListView 控件 / 列**
  - [ ] `SortableTextComparer`（`Func<ListViewItem,string>` 选择器 + `SortOrder`）把 3 个 IComparer 嵌套类收敛为 1，连带删 3 个死构造器（`MusicTagWinApp.Roles/EditableListView.cs:30-113`；保留 559-573 比较器选择分支不动）
  - [ ] `DrawCenteredImage(g,image,bounds,x)` 上提到基类 `MusicTagWinApp.Stubs/DrawableListViewSubItem.cs`，统一 4 处居中绘图（ImageSubItem / ImageListSubItem / CheckBoxSubItem / EditableListView 封面分支）
  - [ ] `MoveSelectedColumn(delta)` 合并上移 / 下移镜像对（`MusicTagWinApp.Common/CustomColumnsDialog.cs:357-399`）

- [ ] **A7 杂项对话框**
  - [ ] `FillAndCenterButtons(list,mainPanel,buttonPanel)` 共享布局 helper ×3（`MusicTagWinApp.Common/DirectoryManagerDialog.cs:88-91`、`MusicTag.Importers/CombinedTagOverwriteOptionsDialog.cs:83-86`、`MusicTag.Consumers/CharacterSetSelectionDialog.cs:125-128`）
  - [ ] `GetEmbeddedPictureData(state)` ×2（`MusicTag.Importers/PictureFromTagsDialog.cs:226-237,319-329`）
  - [ ] `SyncControllerInputs()` ×4 按钮处理器（`MusicTag.Consumers/FindReplaceDialog.cs:139-165`）

- [ ] **A8 其余 in-file 小项**
  - [ ] （效率）`TextBoxFindReplaceController.ReplaceAll` 提取 `textBox.Text` 本地量，消除 per-match 重读（`MusicTag.Serialization/TextBoxFindReplaceController.cs:73-107`）
  - [ ] （效率）`FilenameRelatedBatchDialog.ChangeTags` 把批常量 regex/pattern 提出 per-file 循环（`MusicTag.Schemes/FilenameRelatedBatchDialog.cs:407,430-486`）
  - [ ] `SourceOrderControl` 移动处理器复用既有 `CanMoveUp`/`CanMoveDown`（`MusicTagWinApp.Stubs/SourceOrderControl.cs:122-182`）
  - [ ] `ListViewFileSetting.AddForDir(fileInfo)` 委托给路径重载（`MusicTagWinApp/ListViewFileSetting.cs:19-57`）
  - [ ] `Program.Main` inline `RunApplication`（`MusicTag.Schemes/Program.cs:27-51`）
  - [ ] ⚠️ `TagTextEncoding` 用 "=>" 拆分替换两个 switch 阶梯（`MusicTag.Serialization/TagTextEncoding.cs:201-244`）—— **对全 10 个注册名 + "GB"→"GB18030" 特例核对**

- [ ] **A9 AutoMatchTagsDialog（未测试热点，逐项验证）** —— `MusicTagWinApp.Adapter/AutoMatchTagsDialog.cs`
  - [ ] `RunSourceSearchPass(...)` 收敛主 / 次源搜索两 pass（1069-1092；用 `IsSecondarySource == secondary`，ref 线程化 `sourceOrderIndex`）
  - [ ] `SaveSidecarFiles(coverPicture)` ×2 封面+歌词侧车保存块（782-794,817-826）
  - [ ] `IsSameTrackMetadata(a,b)` ×2 best/alternate 严格等值检查（1105-1106,1121-1122）
  - [ ] inline `GetSourceFromItem`（584-595，单点 object 参数误导，唯一允许的 forwarder 例外）
  - [ ] （altitude）`ExtractResultsFromRankedTracks(...)` move-method 到 `MetadataSearchState`（1093-1176）

### Tier B — 跨文件复用（中风险，需仔细验证）

- [ ] **B1 ConfigDescriptorState 写标签核心（逐项 build+smoke）** —— `MusicTag.States/ConfigDescriptorState.cs`
  - [ ] `SaveWithId3v2Version(Action writeBody)` 收敛 `SaveTagFields`/`SaveCurrentTagFile` 版本锁 / try / catch / finally（427-522）
  - [ ] `AppendUtf8Blocks(...)` 统一 `FillRawFromXiph`/`FillRawFromApe` 的 UTF8 编码尾（799-848）
  - [ ] `AddIfAbsent(key,factory)` 收敛 `LoadAudioProperties` 6 处惰性缓存（250-280）

- [ ] **B2 搜索对话框收敛到 `SearchStatusIndicator`（在线子系统）**
  - [ ] enum→provider 类型映射工厂 `CreateProvider(source,cts)` —— ⚠️（Codex 点 1）**置于 `MusicTagWinApp.Web` 独立小工厂（与 `SearchSource` 同处），不放进 `RemoteTagProviderBase`**：基类已在 `RemoteTagProviderBase.cs:234` 有 `!(this is QqMusicTagProvider)` 一处反向依赖，不再把它对全部 4 个具体 provider 的认知加宽。用它收敛 `CoverSearchDialog` 两处 `SearchCovers` + `LyricSearchDialog.DownloadLyricBySource` 三臂的 uniform-call switch（异构签名臂——NetEase 带 musicId、Kuwo `LoadLyricForTrack`——保留 typed local）
  - [ ] `SearchStatusIndicator.LayoutFooterStatus(footer,buttons,label)`（页脚按钮+状态标签布局 ×3：`CoverSearchDialog:559-566`、`LyricSearchDialog:306-313`、`CombinedTagSearchDialog:607-614`）
  - [ ] `SearchStatusIndicator.BeginReporting()`（`Progress<SourceSearchStatus>` 通道接线 ×3：`LyricSearchDialog:577-579`、`CoverSearchDialog:647-649`、`CombinedTagSearchDialog:935-937`）
  - [ ] `SourceOutcomeTracker`（`MusicTagWinApp.Web`，Record + ReportFinal）收敛 Cover/Lyric 两份按源跟踪（`LyricSearchDialog:423-433,596-612` ≡ `CoverSearchDialog:188-211`）
  - [ ] `CoverSearchDialog` 合并 `SearchByAlbumAndArtist`/`SearchByTitleAndArtist`→`SearchCoversBySource(source,query)`（574-640）

- [ ] **B3 Provider 候选装配上提到 `RemoteTagProviderBase`（结构价值最大，拆 2–3 子提交）**
  - [ ] `BuildOrderedTracks<TSong>` / `BuildOrderedLyrics<TSong>` / `BuildDedupedCovers<TSong>`（含 SearchSource 过滤去重集、有序 Dictionary 物化、ResultOrder/SearchPass/SourceOrder 标注）—— 先 QQ/Kuwo/Kugou，NetEase 因额外 `(knownSongId==0||Cover!=null)` 谓词与 `EncodeMusicComment` 后步后续并入。⚠️（Codex 点 6）helper **只接收 songs + provider 构造/加载委托，不统一联网调用顺序与取消语义**：Tracks 骨架三家结构一致，Lyrics 因 Kuwo loader 不同经委托吸收，**Kugou 的反转取消控制流（`if-not-cancelled…continue; else break`）须先证语义等价再并入**
    - QQ `SearchTracks` 217-305 / `SearchLyrics` 153-174 / `SearchCovers` 176-215；Kugou 123-182 / 81-102；Kuwo 72-113 / 115-140 / 156-179
  - [ ] 共享 JToken 安全读取器上提（`GetStringField`/`GetNullableIntField`/`GetLongField`/`GetNullableLongField`/`GetFirstField`/`GetStringOrEmpty`，现 4 provider 各有副本，NetEase 版含多名 fallback）
  - [ ] NetEase 局部（`MusicTagWinApp.Exporters/NetEaseMusicTagProvider.cs`）：`BuildEncryptedPostBody`（100-171）、`PostSongQuery`（92-145）、`SearchTracks` 单 `HashSet` 有序去重删并行 List+重建（306-403）、`FormatPublishYear`（286-298,333-344）、`SearchCovers` 单一去重（206-241）
  - [ ] Kugou 删 `SearchResultDetailLoader` 闭包类 + 手写枚举器，改 foreach + lambda（`MusicTag.Candidates/KugouTagProvider.cs:21-30,144-169`）
  - [ ] ⚠️ **联网写回路径无 smoke 覆盖**；逐 provider build+smoke，等价性靠数据流分析

- [ ] **B4（可选）ProgressDialog 事件现代化**
  - [ ] 手写委托字段 + Add/Remove + `Interlocked` CAS → C# field-like `event`（`MusicTagWinApp.Containers/ProgressDialog.cs:17-19,47-65,153-175`）；~30 个 `Add*Handler`/`Remove*Handler` 调用点（多在 `StateFieldInstance`）改 `+=`/`-=`。行为等价（Roslyn field-like event 用同样 CAS add/remove），**churn 大**，单列。

### Tier C — 结构 / altitude（高 churn / 高风险，**默认推迟，仅记录方向**）

- [ ] **StateFieldInstance**（8868 行）：`BatchFileTaskContext` 基类（或 `BatchFileProcessor<TItem>` 驱动）统一 ~11 个批处理 context + 8 个 `Start*` runner 脚手架（Cancel/UpdateProgress/循环/取消检查/`TagHistoryRepository` try-finally）；7 个 failure-reporter 类 + `AppendFileError(page,name,msg)`；`PictureCompressionWorker` 的 14 个 `ResizeToNNNQualityMM` 改 `(resolution,quality)[]` 表；`CoverPreviewController`（~700 行，4815-5650）/ VirtualMode 选择模型（3711-3816）抽取；`InitializeComponent` 移入 `StateFieldInstance.Designer.cs` partial。
- [ ] **DatabaseMapper**：误名神类（~50 静态方法，无一 DB 相关），拆 image/DPI、file-logging（7×Get*LogDirectory ↔ 7×Write*Log 对偶）、path/dir、message-box 四簇静态类。
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
