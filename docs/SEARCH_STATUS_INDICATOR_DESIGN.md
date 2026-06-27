# 联网搜索状态标识 —— 设计文档（待 Codex 复核）

> 状态：设计阶段，**尚未实现**。本文档记录需求、当前架构事实、已定决策与待确认项,供复核。
> 关联界面:`CombinedTagSearchDialog`(合并标签搜索候选弹窗)。
> 日期:2026-06-27。

---

## 1. 需求

在合并标签搜索候选弹窗(`CombinedTagSearchDialog`)底部、**"确定"/"取消"按钮左侧**(见 `docs/说明示意.jpg` 红框)新增一个**搜索状态标识**,解决当前"只有一个转圈、无文字、搜索失败直接返回空白易误解"的问题。

标识为**单行 / 双行灵活切换**:

1. **搜索中、无出错**:单行 `正在搜索: 酷我/酷狗/QQ/网易云`(只列**已勾选且尚未完成**的源;某源正常返回后从列表移除)。
2. **有源出错、但仍有源在等待返回**:双行
   - 第 1 行:`正在搜索: <仍在搜的源>`(出错的源不出现在此行)
   - 第 2 行:`xx API错误(错误码)`;若是 QQ 这类带重试的源,显示 `QQ API错误(错误码), x 秒后重试 (x/x)`(括号内为 第几次/总次数)。
3. **所有源已完成、但有出错**:单行,显示 `xx API错误(错误码)`;若只剩 QQ 还在重试,则单行显示 `QQ API错误(错误码), x 秒后重试 (x/x)`。
4. **所有源完成、无出错、有结果**:标识**消失**。
5. **所有源完成、无出错、但 0 条结果**:显示空态提示(如 `未找到匹配结果`),**不直接消失**——避免与"未开始 / 搜索中"的空白混淆(Codex 意见 5)。

---

## 2. 当前架构事实(已读源码核实)

### 2.1 合并搜索是**单线程串行**的
`CombinedTagSearchDialog.SearchCombinedTagsAsync()`(`CombinedTagSearchDialog.cs:809`)在**一个** `Task.Run` 中调用 `TrackSearchCoordinator.SearchAllSources()`(`:214-279`)。该方法在**同一线程内顺序**遍历:网易 linked → 各 primary 源 → 各 secondary 源,逐个同步调用 `SearchCurrentContextTracks` → `SearchTracksFromSource`(`:736-807`)。
**任意时刻只有一个源在真正请求**,不存在多源并发。

### 2.2 结果上报是**分阶段批量**的
进度通过 `IProgress<List<TrackSearchResult>>`(`Progress<T>`,在 UI 线程同步上下文回调 `OnSearchResultsReported` → `AddSearchResultsToList`,`:201/205/814`)。每个 pass 结束后由 `RankLimitAndReportCurrentBatch`(`:299-315`)**一次性** report 一批,不是每源完成即报。排序/限额依赖串行累积状态:`AccumulatedResults`、`RemainingResultsBySource`、`RemainingGlobalResults`。

### 2.3 错误码**当前根本拿不到**(最大障碍)
传输层 `RemoteTagProviderBase`:
- `PostString`(`:79-112`):捕获所有异常 → `Console.WriteLine` → 返回 `null`;**非 200 状态码也返回 `null`**。
- `GetResponseString`/`GetResponseBytes`(`:114-151`):同样吞错,返回 `""`/`null`。

→ 上层只知道"无响应",**既无法区分"搜到 0 条"与"网络失败",也拿不到任何状态码/错误码**。
注意区分两类"码":
- **业务码**:HTTP 200 时响应 JSON body 里的字段(如 QQ `req_0.code == 2001` 表示限流,`QqMusicTagProvider.cs:125-129`)。
- **HTTP 状态码 / 异常**:超时、DNS、5xx、连接失败等——这些目前在传输层就被吞掉,无业务码。

### 2.4 QQ 重试对 UI 不可见
QQ 重试循环在 `QqMusicTagProvider.SearchSongs`(`:77-106`)内部:
- 仅 `SearchSongs`(搜歌)自身有重试循环;`SearchLyrics` 无自有重试,但它**调用** `SearchSongs`,故 `SearchLyrics` / `SearchTracks` 的调用路径上仍会发生重试与 `Retrying` 上报(见 §12.4 更正)。
- 仅针对限流码 2001(`IsRateLimited`);其它失败(网络/空/解析失败)不重试。
- `maxAttempts = 3`(原始 1 次 + 重试 2 次)。
- 退避**线性**:`cancellationSource.Token.WaitHandle.WaitOne(800 * (attempt + 1))` → 800ms、1600ms(可被取消打断)。
- 仅 `Console.WriteLine` 打日志,**UI 完全看不到重试状态**。

### 2.5 酷我的"重试"实为**封面详情熔断**,不是搜索重试
`KuwoTagProvider`:
- 搜索分两步:① `SearchTracks` 搜歌曲列表;② 对每首歌 `LoadSongDetails`(`:286-309`)调**详情接口**补封面 URL。
- 详情接口返回非 JSON 时,置**进程级静态标志** `detailApiUnavailable = true` + 记 `lastDetailApiCheckTick`(`:300-304`)。
- 之后 **5 分钟内**(`DetailApiRetryIntervalMs = 300000`,`:24`)所有酷我详情请求被 `IsDetailApiBackoffActive`(`:311-314`)**直接跳过**;5 分钟后自动恢复。
- **没有"第几次/共几次"概念**,且**不影响搜索成败**——搜索照常返回结果,只是部分歌曲无封面。
- `detailApiUnavailable`/`lastDetailApiCheckTick` 是 `static`(进程级共享、无同步,与历史 review 的"无同步 static 可变状态"同类)。

### 2.6 网易云、酷狗
搜索**无任何重试 / 退避**,单次请求,失败即空结果。

---

## 3. 已定决策(用户确认)

| 决策点 | 选择 |
|---|---|
| 并发模型 | **改成并行搜索**:每个已勾选源各开一个 Task 并发跑,以实现"多源同时搜、各自完成/出错"。 |
| 错误码语义 | **业务码优先,网络错误兜底**:能拿到业务码(如 QQ 2001)就显示业务码,否则回退到 HTTP 状态码 / 异常类型归类。 |
| 酷我封面熔断 | **不进错误行**:搜索成功照常处理;若本次触发详情熔断,单独提示"酷我封面暂不可用",不显示为"酷我 API 错误"。 |

---

## 4. 待确认项(本文档给出推荐默认,请复核确认/调整)

| # | 问题 | 推荐默认 |
|---|---|---|
| D1 | **并行后的排序与限额语义** | **(按 Codex 意见 1 收紧)各源并发搜索时先把结果缓冲在内存,等所有源完成后统一排序 + 全局/每源限额裁剪,再一次性输出到列表。** 不再"边报边入列"。原因:现有 `OnSearchResultsReported → AddSearchResultsToList` 入列即触发封面/歌词后台下载,若"先显示后裁剪",被裁项也会启动下载且 UI 闪烁。代价:结果不再逐源冒出,而是搜完一次性出现——但有状态标识在,用户全程知道在搜,体验可接受。 |
| D2 | **多个源同时出错 / 同时重试的展示** | 第二行(或单行错误)**最多显示一条**,优先级:重试中(QQ) > 普通错误;多个普通错误时合并为"网易云/酷狗 API错误"或轮换。若需多行,窗口需相应留高。 |
| D3 | **错误信息显示时长** | "全部完成但有错"的错误行**常驻到关窗或下次搜索开始**(便于用户看清结果为何偏少),不自动消失。 |
| D4 | **QQ 重试倒计时取整** | 退避现为 800ms/1600ms。倒计时"x 秒"**向上取整**(800ms→1 秒);或可将退避改为整秒(1s/2s)使显示更自然——倾向哪个待定。 |

---

## 5. 设计方案

### 5.1 每源搜索状态模型
为每个已勾选源维护一个状态:
```
enum SourceSearchPhase { Pending, Searching, Completed, Error, Retrying }
SourceSearchStatus {
    SearchSource Source;
    SourceSearchPhase Phase;
    string ErrorCode;        // 业务码或 HTTP 状态/异常归类(Phase=Error/Retrying 时)
    int RetryAttempt;        // 当前第几次(Phase=Retrying)
    int RetryTotal;          // 总次数
    int RetrySecondsLeft;    // 倒计时剩余秒
}
```

### 5.2 状态上报通道(独立于结果上报)
新增一条与现有 `IProgress<List<TrackSearchResult>>` **并列、独立**的状态通道(`IProgress<SourceSearchStatus>` 或 `Control.BeginInvoke`),搜索线程推送状态变更,UI 线程聚合渲染。**不复用结果上报通道**,避免把"状态"和"结果"耦合。

### 5.3 并行化改造(`TrackSearchCoordinator`)
- 把 `SearchAllSources` 由"顺序遍历各源"改为"每个已勾选源一个 Task 并发执行 `SearchTracksFromSource`"。
- 共享状态(`AccumulatedResults`、`RemainingResultsBySource`、`RemainingGlobalResults`、结果列表)**加锁**保护。
- 排序/限额按 D1 语义重写(各源独立限额收集 → 按源优先级合并 → 全局裁剪)。
- 取消:各源共享现有 `cancellationSource`,取消同时中断所有在途请求(传输层已用 `cancellationSource.Token`)。
- 每个源 Task 在 开始/成功/失败/重试 时推送 5.1 的状态。

### 5.4 传输层暴露错误(`RemoteTagProviderBase`)——统一 GET/POST 结果模型
(按 Codex 意见 3 修订:错误暴露点**不止 `PostString`**,`GetResponseString`/`GetResponseBytes` 同样吞错,且 Kuwo / Kugou / NetEase 的歌词 / 详情路径都依赖它们。)
- 引入**统一的请求结果模型**(如 `HttpResult { string Body; int? HttpStatus; ErrorKind Error; }`),`PostString` / `GetResponseString` / `GetResponseBytes` 都改为返回它(或并存一个返回结果模型的新重载,旧签名保留转调以保证向后兼容)。
- provider 拿到结果后:先看业务码(各源各自 code 字段),否则用 HTTP 状态 / 异常类型归类为兜底错误码。
- **把"解析失败"单独列为一类状态**:HTTP 200 拿到 body 但 JSON 解析失败 ≠ 网络失败 ≠ 搜到 0 条。三者要能区分,否则"空结果"和"出错"仍混淆。
- 错误归类(`ErrorKind`)建议:`None / HttpStatus(code) / Timeout / Network / ParseFailed / RateLimited(businessCode)`。
- **向后兼容**:歌词、封面下载等其它调用点必须不受影响(见 §8 待复核问题 3)。

### 5.5 QQ 重试可见(`QqMusicTagProvider.SearchSongs`)
- 重试时通过状态通道上报 `Retrying{attempt, total, secondsLeft}`。
- UI 端用一个**带 `IsHandleCreated` / `IsDisposed` 保护的计时器**每秒刷新倒计时(规避历史 review 中 `ProgressDialog` 计时器的 check-then-Invoke 竞态)。
- 退避等待仍用可取消的 `WaitHandle.WaitOne`。

### 5.6 酷我详情接口熔断提示(`KuwoTagProvider`)
(按 Codex 意见 4 修订文案。)**核实依据**:酷我详情接口是 `songinfoandlrc`(`SongDetailUrlFormat`,`:28`),**同时承载封面和歌词**——`PopulateSongDetails` 既填 `song.CoverUrl`(`:564`)又解析 `song.LoadedLyric`(`:573-576`)。`LoadSongDetails`(`:286`)受熔断标志 `IsDetailApiBackoffActive` 控制,熔断时封面与(预取的)歌词都拿不到。
- 不进错误行(搜索本体照常返回结果)。
- 文案改为 **"酷我详情接口暂不可用"**(而非仅"封面不可用"),更准确反映封面 + 歌词都受影响。
- 触发条件:本次搜索期间 `detailApiUnavailable` 被置位;结束后在标识区附该提示(具体位置 / 文案待定)。

### 5.7 UI(`CombinedTagSearchDialog` 底部)
- 在底部 `FlowLayoutPanel` 中"确定"按钮左侧放一个状态控件(单 `Label` 双行,或两行 `Label` 容器)。
- 按聚合状态渲染 §1 的单/双行规则。
- **预留两行高度**,单/双行切换时不顶动窗口/按钮布局。
- 源名映射:`Music163/QQ/Kugou/Kuwo` → `网易云/QQ/酷狗/酷我`(复用现有 `GetDisplayName`)。

### 5.8 特殊路径的状态规则(按 Codex 意见 2 补充)
状态标识必须覆盖以下绕开"普通全量搜索"的分支,否则会残留旧状态或在缓存命中时不消失:

- **缓存复用**(`OnShown` 的 `canReuseCachedResults`,`CombinedTagSearchDialog.cs:485-501`):同 context + 同 preferredSource 命中时直接复用 `cachedSearchResults`、走 `cachedResultsTimer` 延迟入列,**不联网搜索**。此路径下状态标识应**不显示**(或瞬时"已完成"后立即隐藏);若缓存结果非空则不显示空态。
- **首选源快路径**(`SearchAllSources` 的 `preferredSource.HasValue` 分支,`:227-242`):只搜首选那一个源。状态标识只显示**该单一源**的搜索 / 出错 / 重试,不列其它源。
- **进入弹窗即重置**:每次 `OnShown` 开始一轮新搜索前,状态标识必须**清空上一轮的残留**(出错行 / 重试倒计时 / 空态),避免跨会话残留。
- **空结果态**(配合 §1 规则 5):所有源成功但合并后 0 条 → 显示"未找到匹配结果",而非直接消失成空白。

---

## 6. 分阶段实施计划(每步过 `Verify-Build.ps1 -RunSmokeTests`)

1. **阶段 1**:状态模型 + 状态上报通道 + 传输层暴露错误(不改并发,先让"出错可观测")。
2. **阶段 2**:`TrackSearchCoordinator` 并行化 + 排序/限额按 D1 重写 + 共享状态加锁。
3. **阶段 3**:QQ 重试上报 + UI 倒计时计时器。
4. **阶段 4**:UI 状态标识(单/双行渲染、布局预留)+ 酷我封面提示。

---

## 7. 风险 / 注意

- 本功能触及**搜索协调器、传输层、四个 provider、UI**四大块,且与最近刚修过的搜索路径(异步收尾、async void 取消、资源释放)重叠,需注意不要回退已修问题。
- 并行化后需重新验证:结果排序稳定性、全局/每源限额正确性、取消能否干净中断所有源、`Progress`/`BeginInvoke` 回 UI 线程的线程安全。
- 倒计时计时器、状态控件的 `IsDisposed`/`IsHandleCreated` 保护,避免关窗竞态。
- 酷我详情熔断的 `static` 状态在并行下被多 Task 读写,需评估是否需要同步(当前为进程级共享、无锁)。

---

## 8. 待复核问题清单(给 Codex)

1. D1 并行排序/限额语义是否合理?是否有更稳妥的方案保证结果顺序稳定且限额精确?
2. D2 多源同时出错/重试的展示规则是否够用?是否需要多行?
3. 传输层 `PostString` 改签名的**向后兼容**风险:还有哪些调用点会受影响(歌词、封面、各 provider 其它请求)?
4. 并行化后共享状态(限额、结果列表、酷我 static 熔断标志)的**线程安全**是否覆盖完整?
5. QQ 重试退避是否改为整秒?倒计时取整方式?
6. 是否存在比"改签名"更小侵入的传输层错误暴露方式(如 provider 内部直接捕获并上报,不动基类签名)?

---

## 9. Codex 修改意见

1. **D1 需要再收紧**:当前文档写的是“并行收集 + 最终统一裁剪”,但现有 UI 是边报结果边入列,还会同步触发封面/歌词下载。这个语义如果不先定死,实现时会出现“先显示、后裁剪”的短暂超额和额外后台工作。建议明确成“先缓冲、统一合并后再一次性输出”,或者把“短暂超额”写成可接受的行为边界。
2. **补上两个现有分支**:`preferredSource` 快路径和缓存结果复用都能绕开普通全量搜索。状态标识必须定义这两条路径下的显示/重置规则,否则很容易残留旧状态或在缓存命中时不消失。
3. **错误来源不要只写 `PostString`**:`GetResponseString` / `GetResponseBytes` 也在吞错,而且 Kuwo、Kugou、NetEase 的歌词/详情路径都依赖它们。建议把错误暴露点写成统一的 GET/POST 结果模型,并把解析失败也单独列成一类状态,不然“空结果”和“网络失败”还是分不清。
4. **Kuwo 文案要改**:这里不是单纯“封面暂不可用”,而是详情接口熔断,歌词也会受影响。更准确的文案应是“酷我详情接口暂不可用”或“酷我部分详情暂不可用”。
5. **补一个空结果态**:如果所有源都成功但最终 0 条结果,当前规则下状态会直接消失,界面还是空白。建议再加一个明确的“无结果”空态,或者在结果区保留最小提示,避免和“正在搜索中”混淆。

---

## 10. 对 Codex 意见的处理(2026-06-27)

经源码核实,**5 条意见全部成立,已全部纳入设计**:

| Codex 意见 | 核实结论 | 文档落点 |
|---|---|---|
| 1. D1 需收紧(边报边入列 + 触发下载,先显示后裁剪会超额/闪烁) | ✅ 成立:`OnSearchResultsReported → AddSearchResultsToList`(`:205/210`)入列即触发封面/歌词后台下载 | D1 改为**先缓冲、全源完成后统一排序+限额+一次性输出** |
| 2. 补 `preferredSource` 快路径 + 缓存复用两条分支 | ✅ 成立:缓存复用 `canReuseCachedResults`(`:485-501`)命中不搜索;`preferredSource` 快路径(`:227-242`)只搜单源 | 新增 **§5.8 特殊路径的状态规则**(缓存命中不显示、快路径只显示单源、OnShown 重置残留) |
| 3. 错误暴露不止 `PostString`,需统一 GET/POST 结果模型 + 解析失败单列 | ✅ 成立:`GetResponseString`/`GetResponseBytes`(`:114-151`)同样吞错,被歌词/详情依赖 | §5.4 改为**统一 `HttpResult` 模型**,`ErrorKind` 含 `ParseFailed`,区分 空结果/网络失败/解析失败 |
| 4. 酷我文案应为"详情接口"而非仅"封面" | ✅ 成立且有据:详情接口 `songinfoandlrc`(`:28`)同时承载封面(`:564`)与歌词(`:573-576`) | §5.6 文案改为 **"酷我详情接口暂不可用"** |
| 5. 补空结果态 | ✅ 成立:规则 4"无错即消失"会让 0 结果与未搜索同样空白 | §1 新增**规则 5 空结果态**("未找到匹配结果"),§5.8 呼应 |

**仍开放、留待实现期定的点**(不阻塞复核):
- D4 QQ 重试退避是否改整秒 / 倒计时取整方式;
- §5.4 统一结果模型采用"改签名"还是"新增重载并存"(§8 问题 6);
- §5.6 酷我提示的具体文案与摆放位置。

---

## 11. 实现纪要(2026-06-27,已落地,供 Codex 复核)

四个阶段已全部实现并提交(各阶段过 `Verify-Build.ps1 -RunSmokeTests`):

| 提交 | 阶段 | 内容 |
|---|---|---|
| `b07864c` | 阶段1 | 状态模型 `SourceSearchStatus`/`SourceSearchPhase`;传输层 `HttpResult`/`RemoteErrorKind` + `*Result` 重载(旧签名委托) |
| `3e089fa` | 阶段2 | `TrackSearchCoordinator` pass B/C 并行化(语义等价) |
| `6d445bb` | 阶段3+4 | 状态通道端到端 + QQ 重试上报/倒计时 + UI 状态标识 |

### 与原设计的偏差及理由(请重点复核)

1. **D1「先缓冲、统一输出」被判定为不必要,未做缓冲重写。**
   核实发现现有 `RankLimitAndReportCurrentBatch` 是**先限额、后上报**(`AddIfWithinLimit`→`LimitedResults`→`Report`),从不 show-then-trim,故 Codex 意见 1 担心的"超额显示 + 多余下载"在现有结构里本就不存在。并行化只让网络 I/O 重叠,排序/限额/上报逻辑**逐字未改**,输出结果集与排序与原串行版**等价**。pass B(rank)与 pass C(sort)分别排序,**未合并**以保序。

2. **并行化采用 map→barrier→reduce,无锁,而非 §5.3 的「共享状态 + 锁」。**
   每源独立 `Task` + 独立 provider 实例,只读访问 `existingResults` 做**同源**去重(已核实四个 provider 均只比 `SearchSource == GetSource()`,跨源为 no-op),并行期间不写任何共享状态,屏障后才在协调器线程串行 reduce。比加锁更安全。`searchPass` 按源次序确定性分配,与原逐源自增一致。
   **附带健壮性改善**:原先单源抛异常会经 `Task.Run` 冒泡**中止整轮搜索**,改后仅丢弃该源(`Status != RanToCompletion` 跳过,不阻塞)。

3. **§5.4 采用「新增重载并存」(非改签名)。** `PostString`/`GetResponseBytes`/`GetResponseString` 保留,新增 `*Result` 版本返回 `HttpResult`;封面/歌词等所有旧调用点零影响。`SearchTracksFromSource` 仅在**末尾追加可选参数** `statusReporter = null`,`AutoMatchTagsDialog` 三处旧调用不受影响。

4. **§5.6 酷我详情接口提示暂缓(本期未实现)。**
   核实发现酷我 `LoadSongDetails`(详情/封面)**不在搜索流程调用**——它是结果显示后封面下载的**延迟路径**(`SearchTracks` 仅解析搜索响应)。因此"搜索结束即读熔断标志"恒为 false。要正确提示需另挂到延迟下载路径。`KuwoTagProvider` 已保留实例标志 `DetailApiUnavailableThisSearch` 备用,提示本身延后为独立增强。

5. **D4 倒计时取整:向上取整**(800ms→1 秒,`(waitMs+999)/1000`),退避仍为 800/1600ms。UI 用带 `IsDisposed` 保护的 1s `Timer` 逐秒递减;`OnClosed` 停表。

6. **错误判定:有结果即视为完成**(即便末次子请求出错);仅当 **0 结果且末次传输出错**才标记 Error。业务码优先(QQ 2001 由 provider 回填),否则回退 `timeout`/`network`/HTTP 状态码。

7. **收尾兜底:** 搜索整体结束时把所有**非 Error** 源统一置 Completed,杜绝边角路径(如首选网易 linkedId 占满限额跳过常规搜索)导致"正在搜索"残留;Error 行保留(D3)。

8. **源名映射**:`GetDisplayName()` 走 `[Description]` 返回 163/QQ/Kugou/Kuwo;为贴合需求文案,状态行单独用中文映射 **网易云/QQ/酷狗/酷我**。

### 待人工验证(无法由编译/冒烟覆盖)
- **UI 视觉**:状态标签宽度、两行高度、与按钮/转圈的相对位置、长文案(重试行)是否被 `AutoEllipsis` 合理截断或换行。`UpdateSearchDialogLayout` 保证 `buttonPanel.Location.X` 不变(label 宽 + buttonPanel 左边距 == 原居中起点)。
- **真实联网行为**:各源完成/出错/QQ 限流重试倒计时、并行结果排序与限额、取消能否干净中断所有源。

---

## 12. 扩展到封面源 / 歌词源弹窗(2026-06-27)

合并标签弹窗落地后,功能同样需覆盖封面搜索(`CoverSearchDialog`)与歌词搜索(`LyricSearchDialog`)。

### 12.1 共享渲染器 `SearchStatusIndicator`
为避免**封面 / 歌词两个弹窗**各自复制状态聚合 + 渲染 + 倒计时逻辑,抽出共享渲染器 `MusicTagWinApp.Web/SearchStatusIndicator.cs`:
> **注**:合并标签弹窗 `CombinedTagSearchDialog` **未迁移到本渲染器**,仍保留其自有内联实现(`BeginSearchStatusTracking` / `RefreshSearchStatusDisplay` / 自带倒计时 `retryCountdownTimer`)。共享渲染器目前**只被封面 / 歌词复用**;把合并标签也收敛过来是一项已知 DRY 待办(见 §13.3 / §14)。
- 入参 `(Label, Func<bool> hasResults, IContainer)`;封装 §1 的单/双行规则、空结果态、QQ 重试逐秒倒计时(带 `Label.IsDisposed` 保护)。
- 生命周期方法:`Begin()`(开搜清残留)、`End()`(收尾把非 Error 统一置 Completed)、`Reset()`(缓存命中等不联网路径)、`Report(status)`、`StopCountdown()`。
- 源名中文映射(网易云/QQ/酷狗/酷我)收敛为静态方法。

### 12.2 统一的底部布局(三弹窗一致)
`footerPanel` 由 `FlowLayoutPanel` 改为普通 `Panel`,子控件**绝对定位**:按钮恒定居中(与状态标签显隐无关,修复"结果出来后按钮跳到右侧"),状态标签置于按钮右侧(原转圈位置)、垂直中线与按钮对齐。删除原转圈 `PictureBox`(封面 `progressPictureBox` / 歌词 `progressImage`)。

### 12.3 封面源(`CoverSearchDialog`)
- 串行搜索(未并行化,低风险);`CandidateSearchWorker` 开搜先对各启用源上报 `Searching`,各源跨多 pass 搜索,结束时按"有结果即 Completed / 始终 0 结果且末次传输出错即 Error"上报最终状态。
- provider 实例方法(`SearchByAlbumAndArtist`/`SearchByTitleAndArtist`)内设 `provider.StatusReporter` 并捕获 `provider.LastTransportResult` 到 `lastSourceTransportResult`(串行单线程安全)。

### 12.4 歌词源(`LyricSearchDialog`)与封面的差异
- **无结果 `IProgress` 通道**:`StartLyricSearch` 是 `async void`,两段 `await Task.Run`(known-id 阶段 + candidate 阶段)。故 `Begin()`+各源 `Searching` 上报、最终结果上报均放在 `StartLyricSearch` 的 **UI 线程段**执行(await 后回到 UI 线程,后台写入的统计已可见),无需额外 marshaling;`End()` 在 finally 兜底。
- **静态 provider 方法被 `AutoMatchTagsDialog` 复用**:`SearchLyricsBySource`/`SearchTracksBySource` 只**追加可选参数** `Action<SourceSearchStatus> statusReporter = null, Action<HttpResult> transportSink = null`(设 `StatusReporter` + 回填传输结果),`AutoMatchTagsDialog` 三处旧调用零影响。
- 每源结果统计在实例转发器(`SearchLyricsFromSource`/`SearchTrackCandidates`)里经 `RecordLyricSourceOutcome` 记录;候选-track 模式以"搜到 track 即完成"为口径(下载歌词步骤不再单独判源成败)。
- **QQ 歌词路径其实会重试**:`QqMusicTagProvider.SearchLyrics` / `SearchTracks` 自身无重试循环,但二者都经由 `SearchSongs`,后者对限流码 2001 有重试并经 `StatusReporter` 上报 `Retrying`。因此在候选-track 搜索路径(`SearchLyricsByCandidateTracks` → `SearchTracksBySource` / `SearchLyricsBySource`)中,QQ 被限流时歌词弹窗**会**显示 Retrying 行(`StatusReporter` 已端到端接好:两处 `qqProvider.StatusReporter = statusReporter`,经 `Progress<SourceSearchStatus>` 编组到 `searchStatusIndicator.Report`)。仅 Music163-only 的 known-id 快路径不触发 QQ。(原此处"歌词路径本无重试,Retrying 不出现"的说法不准确,已更正——对应 §13.4。)

### 12.5 提交
| 提交 | 内容 |
|---|---|
| `94eef7a` | 封面源状态标识 + 抽出共享渲染器 `SearchStatusIndicator` |
| (本次) | 歌词源状态标识(复用共享渲染器,UI 线程上报最终结果) |

### 12.6 仍待人工验证
- 歌词/封面弹窗的状态标签视觉(同 §11 待验证项)。
- 歌词两阶段中 known-id 占满限额导致 candidate 阶段跳过部分源时,这些源由 `End()` 兜底显示 Completed(非残留"正在搜索")。

---

## 13. Codex 二次复核意见(2026-06-27)

1. **`ParseFailed` 尚未真正落地。** 当前只加了 `RemoteErrorKind.ParseFailed` 枚举,但没有任何 provider 实际设置它。QQ 的 `TryParseJsonObject` 解析失败会返回 `null`,Kuwo 的 `ParseSearchResponse` 会吞掉解析异常并返回空列表。结果是 HTTP 200 + 非法 JSON 仍可能被当成"0 条结果 / Completed",没有兑现 §5.4 / §10 中"解析失败单独列状态"的设计要求。

2. **合并标签并行搜索吞掉 faulted task 后会误报 Completed。** `SearchSourcesInParallel` catch `AggregateException` 后只跳过非 `RanToCompletion` task,没有记录、也没有上报 Error。对应源此前已是 `Searching`,最终 `EndSearchStatusTracking` 会把非 Error 统一改成 `Completed`,导致某个源发生未预期异常时用户看到"完成 / 无结果",而不是 API 错误。

3. **共享渲染器的文档描述与代码不一致。** §12.1 写 `SearchStatusIndicator` 是为了避免"三处弹窗各自复制状态聚合 + 渲染 + 倒计时逻辑",但当前实际只在封面 / 歌词弹窗实例化;合并标签弹窗仍保留自己的 `BeginSearchStatusTracking` / `RefreshSearchStatusDisplay` / 倒计时逻辑。后续若改共享渲染器,合并标签不会自动同步,需要么把合并标签也迁移到共享渲染器,要么把文档改成"封面 / 歌词复用,合并标签暂保留本地实现"。

4. **QQ 歌词路径"无重试"的说明不准确。** §12.4 写"QQ 歌词路径(`SearchLyrics`/`SearchTracks`)本无重试,故 Retrying 行在歌词弹窗自然不出现",但 `QqMusicTagProvider.SearchLyrics` 和 `SearchTracks` 都会走 `SearchSongs`,而 `SearchSongs` 对限流 2001 会上报 `Retrying`。应改文档说明,或确认是否需要让歌词弹窗显示该 Retrying 状态。

---

## 14. 对 Codex 二次复核意见的处理(2026-06-27)

经独立工作流亲读源码逐条核实,**§13 的 4 条意见全部成立**。1、2 为真实代码缺陷(已修),3、4 为文档/注释与实际不符(已更正):

| Codex 意见 | 核实结论 | 处置 |
|---|---|---|
| 1. `ParseFailed` 从未被赋值,HTTP 200 + 非法 JSON 被当成"0 条 / Completed" | ✅ 成立:全仓 `ParseFailed` 仅出现在枚举声明 + 注释;传输层只产 None/HttpStatus/Timeout/Network,两 provider 解析失败均吞错返回空 | **修代码**:QQ `SearchSongs`(解析返回 null 且 `responseBody` 非空)、Kuwo `ParseSearchResponse`(顶层 `JObject.Parse` 抛异常且 `searchJson` 非空)各回填 `SetTransportError(ParseFailed, "parse")`;传输失败(body 为空)不误判。上层 `ReportSourceOutcome` 据"0 结果 + 末次传输非成功"即标 Error,解析失败遂与"0 条结果"区分开 |
| 2. 并行搜索吞掉 faulted task → 误报 Completed | ✅ 成立:`SearchTracksFromSource` 内 provider 调用无 try/catch,抛异常则 `ReportSourceOutcome` 不执行,源停在 `Searching`;收割只取 `RanToCompletion`,`EndSearchStatusTracking` 又把非 Error 统一改 Completed | **修代码**:`SearchSourcesInParallel` 收割循环改带索引,对 `IsFaulted`(且未取消)的源调用新增的 `ReportError(source)` 上报 `Error`(`ErrorCode` 留空 → 显示"未知");`End` 保留 Error 不再覆盖 |
| 3. 共享渲染器文档/注释称"三处弹窗复用",实际仅封面/歌词 | ✅ 成立:`new SearchStatusIndicator` 仅见于 `CoverSearchDialog`/`LyricSearchDialog`;合并标签仍自带 `BeginSearchStatusTracking`/`RefreshSearchStatusDisplay`/`retryCountdownTimer` | **改文档/注释**:§12.1 与 `SearchStatusIndicator.cs` 顶部注释均改为"封面/歌词两个弹窗复用,合并标签保留自有内联实现(DRY 待办)" |
| 4. QQ 歌词路径"无重试"说明不准确 | ✅ 成立:`SearchLyrics`/`SearchTracks` 均经 `SearchSongs`,后者对 2001 上报 `Retrying`;歌词弹窗已端到端接好 `StatusReporter`,候选-track 路径下 QQ 限流**会**显示 Retrying | **改文档**(代码无需动,链路已通):§12.4 与 §2.4 措辞更正——歌词候选-track 路径会显示 Retrying,仅 Music163-only 快路径不触发 QQ |

**已知 DRY 待办(未处理,留作后续)**:把合并标签弹窗 `CombinedTagSearchDialog` 也迁移到共享渲染器 `SearchStatusIndicator`,消除其内联重复实现。

---

## 15. Codex 三次复核意见(2026-06-27)

### 15.1 结论

`0d88263` 已经修正了 §13 中的并行 faulted task 误报 Completed、共享渲染器说明不准、QQ 歌词重试说明不准这三类问题。但 `ParseFailed` 的修复仍不完整:本次只覆盖了 QQ 与酷我两个前一轮举例路径,网易云与酷狗的同类搜索解析失败仍会被吞掉并返回空列表。

### 15.2 需要继续修改

1. **[P1] `ParseFailed` 只落到 QQ / 酷我,网易云 / 酷狗仍会把 HTTP 200 + 非法 JSON 当成空结果。**

   设计要求在 §5.4 已明确:"HTTP 200 拿到 body 但 JSON 解析失败"要与"网络失败"、"搜到 0 条"区分。当前 `QqMusicTagProvider.SearchSongs` 与 `KuwoTagProvider.ParseSearchResponse` 已补 `SetTransportError(RemoteErrorKind.ParseFailed, "parse")`,但另外两个源仍未补:
   - `src/MusicTag/MusicTagWinApp.Exporters/NetEaseMusicTagProvider.cs:445-478` 的 `ParseSongSearchResponse` 在顶层 `JObject.Parse(responseBody)` 失败时只 `Console.WriteLine`,然后返回空 `songs`。
   - `src/MusicTag/MusicTag.Candidates/KugouTagProvider.cs:184-220` 的 `ParseSongSearchResponse` 同样只打日志并返回空 `songs`。

   影响:网易云 / 酷狗在"请求成功但响应体不可解析"时,上层 `ReportSourceOutcome` 看到 `results.Count == 0` 且 `LastTransportResult` 仍是成功,会把该源标记为 `Completed` / 空结果,没有显示"解析失败"。这与 §14 表格里"`ParseFailed` 问题已修"的结论不一致。

   建议:对网易云、酷狗的搜索响应顶层 parse catch 按 QQ / 酷我同样处理:仅在 `responseBody` 非空时 `SetTransportError(RemoteErrorKind.ParseFailed, "parse")`;空 body 保持由传输层的 Network / Timeout / HttpStatus 归类。item-level 单条结果解析失败可以继续只跳过并记录日志,不必把整源标错。

2. **[P3] 合并标签并行 faulted task 已能显示 Error,但异常细节被完全吞掉。**

   `CombinedTagSearchDialog.SearchSourcesInParallel` 现在会对 `sourceTask.IsFaulted` 上报 `Error`,避免被 `EndSearchStatusTracking` 误置 `Completed`,这个方向是对的。但 `catch (AggregateException) { }` 仍完全不记录异常,新增的 `ReportError(source)` 也没有 `ErrorCode`,最终 UI 只能显示"API错误(未知)",控制台也失去具体异常链。

   建议:至少在 `catch (AggregateException ex)` 或遍历 faulted task 时 `Console.WriteLine` 每个 `InnerException.GetMessageChain()`。UI 是否继续显示"未知"可以接受,但日志里应保留 provider 未预期异常的定位信息。

### 15.3 是否把合并标签弹窗也收敛到共享渲染器

建议做,但拆成后续单独重构提交,不要和上面的 `ParseFailed` 补漏混在一起。

理由:
- 现在 `CombinedTagSearchDialog` 仍内联维护 `sourceSearchStatuses`、`retryCountdownTimer`、`BeginSearchStatusTracking`、`EndSearchStatusTracking`、`RefreshSearchStatusDisplay`、`BuildErrorOrRetryLine`、源名映射等逻辑;`SearchStatusIndicator` 里已有几乎同一套实现。
- 这次已经出现过"共享渲染器文档说三处复用,实际只两处复用"的漂移。继续保留双实现,后续改空结果态、错误文案、倒计时或源排序时很容易只改到一边。
- `SearchStatusIndicator` 的构造参数已经能覆盖合并标签需求:`Label` + `Func<bool> hasResults` + `IContainer`。合并标签只需要把 `searchResultsListView.Items.Count > 0` 作为结果判断,把现有 `Begin/End/Reset/Report/StopCountdown` 调用点替换过去。

迁移边界建议:
- 先修 P1 的网易云 / 酷狗 `ParseFailed` 补漏。
- 再单独提交迁移 `CombinedTagSearchDialog` 到 `SearchStatusIndicator`,删除内联重复渲染方法和本地状态字典。
- 迁移后重点人工验证:缓存命中不显示状态、首选源快路径只显示单源、取消不残留"正在搜索"、0 结果显示"未找到匹配结果"、QQ 重试倒计时仍刷新。

---

## 16. 对 Codex 三次复核意见的处理(2026-06-27)

| Codex §15 意见 | 处置 |
|---|---|
| **P1**:`ParseFailed` 只落到 QQ/酷我,网易云/酷狗仍把 HTTP 200 + 非法 JSON 当空结果 | ✅ **已补**:`NetEaseMusicTagProvider.ParseSongSearchResponse` 与 `KugouTagProvider.ParseSongSearchResponse` 顶层 parse catch 比照 QQ/酷我 —— 仅 `responseBody` 非空时 `SetTransportError(ParseFailed, "parse")`,空 body 保持由传输层归类;item-level 单条解析失败仍只跳过 + 记日志,不标整源。四个源至此一致 |
| **P3**:并行 faulted task 已显示 Error,但异常细节被完全吞掉 | ✅ **已补日志**:`SearchSourcesInParallel` 收割 faulted 源时 `Console.WriteLine("SearchSourceInParallel error:" + sourceTask.Exception?.GetBaseException().GetMessageChain())`,保留 provider 未预期异常链;UI 仍显示"API错误(未知)"(可接受) |
| **§15.3**:把合并标签弹窗收敛到共享渲染器 | ⏳ **单独提交进行**(不与 P1/P3 补漏混提):见下方迁移提交。迁移后删除 `CombinedTagSearchDialog` 内联的 `sourceSearchStatuses`/`retryCountdownTimer`/`Begin·End·Reset·RefreshSearchStatusDisplay`/`BuildErrorOrRetryLine`/源名映射,统一走 `SearchStatusIndicator` |
