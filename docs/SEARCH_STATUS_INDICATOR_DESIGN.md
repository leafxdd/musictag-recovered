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
- 仅 `SearchSongs`(搜歌)有重试,`SearchLyrics` 没有。
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

