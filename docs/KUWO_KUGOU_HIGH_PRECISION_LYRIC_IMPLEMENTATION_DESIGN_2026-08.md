# 酷狗 KRC / 酷我 LRCX 高精度逐行歌词实现设计

- 日期：2026-08-02
- 分支：`develop-net8`
- 状态：待 Claude 交叉审阅
- 范围：设计稿；本文件不实施产品代码修改
- 输入：[`KUWO_KUGOU_LYRIC_PRECISION_2026-08.md`](KUWO_KUGOU_LYRIC_PRECISION_2026-08.md)、当前 provider/测试代码、既有歌词修复决策与本轮协议复核

## 1. 目标与结论

本设计计划把酷狗和酷我的逐词歌词通道接入现有逐行 LRC 下载流程：

- 酷狗优先获取 KRC，以显式行起始毫秒转换为逐行 LRC；失败时回退当前 `m3ws` 普通 LRC。
- 酷我优先获取 LRCX，保留其真实三位毫秒行起始；失败时回退当前 `songinfoandlrc` 的 `lrclist`。
- 默认关闭“格式化时间轴”时输出真实 `[mm:ss.fff]`；开启时继续调用现有 `LyricTextProcessor.FormatTimestamp`，维持既有两位、四舍五入到 10 ms 的策略。
- 新增 provider-local 的纯 decoder/helper，不在本轮抽取 QQ、网易、酷狗、酷我四源统一 decoder。
- 不增加第三方运行时依赖，不修改 UI、设置项、provider 能力接口、`SearchSource` 枚举序号或公共调用签名。

这是可见的精度修正，不是等价重构：酷狗时间戳将由两位厘秒变为真实三位毫秒；酷我第三位将由恒为 `0` 变为真实值。旧端点回退成功时仍保留当前输出。

## 2. 交叉审查结论

### 2.1 采纳 Claude 报告的结论

以下结论与当前代码、真实响应和多个开源实现一致，可作为实现依据：

1. 酷狗 KRC 是真 1 ms 行/词时间格式；当前 `data.lrc` 是服务端降精度后的普通 LRC。
2. 酷我 LRCX 的行标签包含真实三位毫秒；当前 `lrclist.time` 主要只有两位小数，第三位由本地格式化补零。
3. KRC 解码链为 Base64 → 校验并跳过 `krc1` → 循环 XOR → zlib → UTF-8。
4. 酷我 LRCX 请求为明文参数循环 XOR `yeelion` 后 Base64，并把 Base64 直接放在 `?` 后；响应为头部 → zlib → Base64 → XOR → GB18030。
5. 酷狗 `contenttype == 2` 时载荷是 Base64 纯文本，不应进入 KRC 解密链。
6. 两个高精度通道都可以保留当前 provider 端点作为回退，不需要引入第三方二进制库。
7. 酷狗免签名 `/search` + `/download` 路径和酷我 `https://newlyric.kuwo.cn/` 路径在 2026-08-02 的实时复核中可用；稳定性仍须由回退链兜底。

### 2.2 对 Claude 报告的纠正与收紧

#### KRC 首词偏移不能作为协议保证

报告中的“186/186 行首词相对偏移为 0”是有价值的样本事实，但不足以证明协议保证。逐行转换本来就有显式 `[lineStart,lineDuration]`，主路径必须始终使用 `lineStart`。只有行头损坏且至少一个词标记可解析时，才允许用 `lineStart + firstWordRelativeStart` 作畸形行兜底；不得把首词偏移为 0 写成正常路径前提。

#### 酷我 Base64 必须严格验证，不能过滤损坏字节

四个本轮实测 LRCX 响应在 zlib 解压后均是合法 ASCII Base64：`nonAscii=0`、`invalidBase64Bytes=0`。实现应允许 Base64 规范空白，但必须拒绝非 ASCII 字节和非法字符。不得先“过滤非 ASCII”再解码，否则会把截断、代理注入或载荷损坏静默变成另一份看似可解析的数据。

#### 酷我重复时间戳的译文归属在报告中写反

真实边界序列的语义是：

```text
[00:01.547]上一行原文
[00:02.880]上一行译文
[00:02.880]当前行原文
```

同一时间点的第一条是上一行译文，第二条才是当前原文。`lx-music` 的整理逻辑先取出重复组的前项作为前一行译文，再保留后项作为当前原文。当前 `KuwoTagProvider.PopulateSongDetails` 的 `PrimaryText` / `AlternateText` 和“借用前一行时间戳”逻辑也依赖这一输入顺序。因此 LRCX 解析必须保留原始行顺序和重复项，不能先按时间戳去重，也不能把第二条解释成上一行译文。

#### 本轮不抽取四源统一 decoder

[`LYRIC_FETCH_REPAIR_REPORT_2026-07.md`](LYRIC_FETCH_REPAIR_REPORT_2026-07.md) F12 曾把“新增第三种逐词格式”列为重新评估共享层的条件。现在确实应重新评估，但评估结果仍是不统一：

| 维度 | QQ QRC | 网易 YRC | 酷狗 KRC | 酷我 LRCX |
|---|---|---|---|---|
| 传输/解密 | 3DES + zlib | 明文 JSON | XOR + zlib | XOR 请求；zlib + Base64 + XOR 响应 |
| 字符编码 | UTF-8 | UTF-8 | UTF-8 | GB18030 |
| 行时间 | `[start,duration]` | `[start,duration]` | `[start,duration]` | `[mm:ss.fff]` |
| 词标记 | `(start,duration)` | `(start,duration,0)` | `<relative,duration,0>` | `<start,duration[,x]>`，可为负 |
| 翻译载体 | 独立字段 | 独立字段 | `[language:]` JSON | 同流重复时间戳 |

目前真正稳定共享的只有 `LyricTextProcessor.FormatTimestamp`。强行统一会把协议差异转化为大量模式参数和分支，扩大已属高风险的 provider 改动。此次只允许 provider-local decoder 和酷我 provider-local 行整理 helper；以后若出现两个以上 decoder 的同类缺陷，再基于已稳定的接口抽象。

#### 不修改 `RemoteTagProviderBase`

基类已经提供：

```csharp
protected virtual HttpResult GetResponseBytesResult(string url)
protected virtual byte[] GetResponseBytes(string url)
```

它们已有取消、请求超时、HTTP 状态、最大响应体和错误分类。酷我必须直接复用原始字节路径，不能先经 `GetResponseString` 以 UTF-8 解码；本轮无需扩展基类公共或 protected 合同。

#### 酷我歌词加载必须与封面详情编排分离

`KuwoTagProvider.LoadSongDetails` 同时服务歌词和延迟封面，共有 4 个直接调用者。直接把 LRCX 请求塞入该方法会让封面下载依赖新歌词端点，并可能让后续旧详情解析覆盖已经取得的高精度歌词。设计必须增加独立歌词编排入口，同时保留 `LoadSongDetails` 作为旧详情/封面路径。

## 3. 风险和兼容边界

### 3.1 当前影响面

本轮 GitNexus/CodeGraph 预分析结果：

- `KugouTagProvider` 和 `KuwoTagProvider` 类级影响为 CRITICAL。
- `KugouTagProvider.LoadLyrics` 有 3 条 provider 内直接入口。
- `KuwoTagProvider.LoadSongDetails` 为 HIGH，有 4 个直接调用者，覆盖歌词搜索、按 ID 加载、延迟歌词和延迟封面。
- `KuwoTagProvider.LoadLyricForTrack` 位于组合标签源等上层消费流程，属于 CRITICAL 路径。

正式实施每个产品符号前仍须重新运行 GitNexus upstream impact；若结果为 HIGH/CRITICAL，应先报告实际调用者和计划防护，再编辑。

### 3.2 必须保持不变的合同

- `ISearchTrackProvider`、`ISearchLyricProvider` 等 provider 能力接口不变。
- `SearchSource` 枚举序号不变。
- 公开方法、可选参数和组合搜索调用签名不变。
- 酷狗和酷我现有 `LyricSearchResult.LyricUrl` 可观察值不变，继续指向当前 legacy URL；高精度端点是内部加载细节。
- 酷狗/酷我单次搜索结果上限仍为 5。
- `LyricDownload_ReformatTimetag` 语义和全局 `FormatTimestamp` 实现不变。
- 单源失败只记录并回退/跳过，不得中止其它源或完整候选列表。
- 取消优先于回退：用户取消后不得继续发起 legacy 请求。
- 当前 legacy 解析和输出必须由 characterization 锁定，回退结果不可顺带重写。

### 3.3 非目标

- 不保留逐词时间轴到最终文件；一期仍转换为逐行 LRC。
- 不实现酷狗 `type=0` 罗马音，也不增加 UI 开关。
- 不还原酷我 `[kuwo:xxx]` 参与混淆的逐词时间；逐行方案只使用行标签。
- 不实现酷狗带 MD5 签名的 `/v1/search` 作为第二主路径；免签名路径失效且有真实样本后再评估。
- 不迁移或重构 QQ QRC、网易 YRC。
- 不加入实时网络测试，不把 `.claude/tmp/lyric-spotcheck/` 直接纳入产品包。

### 3.4 协议来源与许可证边界

实现应把真实响应和多方一致的协议事实作为输入，重新编写本项目代码，不直接复制 GPL 项目的函数体、常量组织或注释。若最终确实适配了某个可兼容许可证项目的具体代码，应像现有 QQ QRC 一样补充 `THIRD_PARTY_NOTICES`、许可证全文和固定版本来源；仅“没有第三方运行时依赖”不等于无需审查代码来源。

## 4. 总体加载状态机

两个 provider 都采用“高精度优先、legacy 回退、取消终止”的结构：

```text
开始加载歌词
  │
  ├─ 已有确认的高精度结果 ───────────────→ 返回
  │
  ├─ 尝试高精度端点
  │    ├─ 成功且主歌词非空 ─────────────→ 保存并返回
  │    ├─ 用户取消 ─────────────────────→ 返回 null/现有缓存，不回退
  │    └─ 无候选、传输失败或解析失败
  │
  └─ 调用现有 legacy 路径
       ├─ 成功 ─────────────────────────→ 返回旧格式结果
       └─ 失败 ─────────────────────────→ 保留现有错误分类并返回空
```

“高精度端点不可用”和“这首歌没有高精度候选”必须区分：前者可触发本次 provider 搜索内的端点熔断，后者只影响当前歌曲。

## 5. 酷狗实现设计

### 5.1 新增文件和职责

建议新增：

```text
src/MusicTag/MusicTag.Candidates/KugouKrcDecoder.cs
```

`KugouKrcDecoder` 是无网络、无全局状态的纯 helper，负责：

1. 解码下载响应中的 `content`。
2. 将 KRC 主歌词转换为逐行 LRC。
3. 从 `[language:]` 提取 `type=1` 逐行翻译。
4. 返回主歌词、译文和明确的失败结果；不得在内部吞掉所有异常后伪装为空歌词。

建议的内部结果形态：

```csharp
internal sealed class KugouKrcDecodeResult
{
  public string Lyric { get; init; }
  public string TranslatedLyric { get; init; }
}
```

具体签名可按现有 C# 风格调整，但 decoder 不应依赖 `KugouTagProvider`、HTTP 或设置对象；是否输出两位/三位通过显式参数传入。

### 5.2 Provider 请求流程

`KugouTagProvider.LoadLyrics` 调整为：

```text
TryLoadKrc(song)
  ├─ GET /search?hash={Hash}&duration={DurationMs}
  ├─ 选择首个字段完整的 candidate(id + accesskey)
  ├─ GET /download?id={id}&accesskey={accesskey}&fmt=krc
  ├─ 按 contenttype 解码
  └─ 转换并补齐 LyricSearchResult 元数据

若失败且未取消
  └─ 原有 m3ws get_krc + ParseLyricResponse
```

要求：

- 继续使用现有 `song.Hash`、`song.DurationMs`；无有效 32 位 hash 或正时长时直接走 legacy。
- `/search` 和 `/download` 使用 HTTPS、现有 `GetResponseStringResult`/`GetResponseBytesResult` 错误分类与取消令牌。
- 搜索响应成功但候选为空属于当前歌曲“无 KRC”，不把端点标记为不可用。
- HTTP/网络/超时，或搜索/下载端点的顶层响应合同整体不可解析，属于端点级失败。单个候选缺歌词、未知 `contenttype`、KRC 魔数或压缩体损坏只按当前歌曲失败处理，不据此禁用其它候选。provider 实例在确认端点级失败后记录 `krcUnavailableThisSearch`，之后候选直接走 legacy，避免最多 5 首连续重复撞击故障端点。
- 取消和单曲无候选不得触发 `krcUnavailableThisSearch`。
- 不增加自动重试；两次高精度请求本身已增加请求量，失败立即回退更符合当前 provider 的错误隔离策略。

### 5.3 KRC 载荷解码

按 `contenttype` 分流：

- `0` 或明确的 KRC 类型：严格 Base64 → 至少 4 字节 → 校验前四字节为 `krc1` → 对剩余字节循环 XOR 固定 16 字节 key → `ZLibStream` 解压 → 严格 UTF-8。
- `2`：严格 Base64 → 严格 UTF-8 纯文本；不得检查 `krc1` 或 XOR。
- 未知类型：失败并回退，不猜测格式。

KRC XOR key 必须按字节写入并用已知密文向量锁定：

```text
40 47 61 77 5E 32 74 47 51 36 31 2D CE D2 6E 69
```

解压后的文本处理：

- 移除开头 UTF-8 BOM。
- 设置解压后大小上限，防止压缩载荷异常膨胀；上限使用内部常量并至少覆盖正常歌词，不能提供用户设置。
- 不记录完整歌词、密钥或 Base64 内容；日志只记录阶段和异常类别。

### 5.4 KRC 到逐行 LRC

每个正常行格式为：

```text
[lineStartMs,lineDurationMs]<relativeStartMs,durationMs,0>text...
```

转换规则：

1. 使用显式 `lineStartMs` 作为正常行时间，不依赖首词相对偏移。
2. 去除全部合法 `<relative,duration,0>` 词标记，保留相邻文本和原始字符顺序。
3. 空文本行默认跳过；如果它参与翻译行号对齐，则只在内部保留占位，不输出空 LRC 行。
4. 行头损坏时，仅在存在可解析首词且能确定行基准时使用兜底；无法安全恢复则跳过该行，不让一行损坏中止整首歌。
5. 时间输出统一调用 `LyricTextProcessor.FormatTimestamp(lineStartMs, useThreeDigitMilliseconds)`。
6. 保留旧酷狗路径已可能暴露的标准 LRC 头部 `ti/ar/al/by/offset`；丢弃 `id/hash/sign/qq/total/language` 等 KRC 私有标签。
7. 主歌词最终为空视为高精度失败，走 legacy。

### 5.5 KRC 翻译

- 找到 `[language:<base64-json>]` 后严格 Base64/UTF-8/JSON 解码。
- 只读取 `content[].type == 1`；`type == 0` 罗马音一期忽略。
- `type=1` 按正文时间行的原始行序号对齐，每项取该行数组中的文本并拼接。
- 翻译 JSON 缺失、损坏或行数不匹配时，保留高精度主歌词并放弃译文；不得因为可选译文损坏而回退低精度主歌词。
- 译文输出使用对应主歌词行起始，并通过现有 `LyricTextProcessor` 对齐/拆分能力做最终规范化；不得按译文自己的估算时间创建新时间轴。

## 6. 酷我实现设计

### 6.1 先提取并锁定现有行整理算法

`KuwoTagProvider.PopulateSongDetails` 当前把 JSON `lrclist` 解析、重复时间戳归类、原文/译文推断、尾部特判、时间戳格式化和封面解析混在一个方法中。LRCX 若复制这段逻辑，会立即产生两份难以验证的翻译启发式。

第一阶段先新增 provider-local 纯 helper：

```text
src/MusicTag/MusicTagWinApp.Adapter/KuwoLyricAssembler.cs
```

建议输入、输出：

```csharp
internal readonly record struct KuwoTimedLyricLine(long TimestampMs, string Text);

internal sealed class KuwoAssembledLyrics
{
  public string Lyric { get; init; }
  public string TranslatedLyric { get; init; }
}
```

`KuwoLyricAssembler.Build` 必须接收保留原始顺序和重复时间戳的序列。先补齐 characterization，再把 `PopulateSongDetails` 中从 `SortedDictionary` 到两个 `StringBuilder` 的既有行为逐步移入 helper；提取提交不得改变任何现有 fixture 输出。

最低 characterization 包括：

- 单语多行和小数秒。
- 第一个时间点重复时的拼接特例。
- 中间重复时间点的原文/译文交换。
- 三行边界“上一行原文 → 上一行译文 → 当前原文”。
- 尾部多个 alternate 拆到 `+5000 ms` 的当前兼容行为。
- 中英、中日以及全非中文内容，避免只靠 `ContainsChinese` 的样本产生假安全感。

### 6.2 新增 LRCX decoder

建议新增：

```text
src/MusicTag/MusicTagWinApp.Adapter/KuwoLrcxDecoder.cs
```

职责分为三个纯步骤：

1. `BuildRequestUrl(trackId)`：构造 XOR + Base64 query。
2. `DecodeResponse(byte[])`：验证响应头、解压、严格 Base64、XOR、GB18030。
3. `ParseTimedLines(string)`：解析行时间戳、去词标记并保留重复行顺序。

decoder 不直接创建 `LyricSearchResult`，也不读取全局设置；provider 把解析结果交给 `KuwoLyricAssembler`。

### 6.3 请求构造

明文严格按当前实测协议：

```text
user=12345,web,web,web&requester=localhost&req=1&rid=MUSIC_{id}&lrcx=1
```

然后：

1. 以 ASCII `yeelion` 循环 XOR。
2. 标准 Base64。
3. 直接拼接为 `https://newlyric.kuwo.cn/newlyric.lrc?{base64}`。

Base64 query 不经普通 `UrlEncode`；`+`、`/`、`=` 是该协议的一部分。`trackId` 必须先通过现有正整数校验，不能把任意输入带入协议明文。

### 6.4 响应解码

使用 `GetResponseBytesResult` 获取原始字节，顺序如下：

1. 在合理头部长度内查找首个 ASCII `\r\n\r\n`。
2. 头部必须包含独立的 `tp=content` 行；`TP=ERROR REQUEST` 或缺少分隔符均失败。
3. 对分隔符后的字节执行 zlib 解压，并施加解压后大小上限。
4. 解压结果只允许 ASCII Base64 字符和规范空白。任何非 ASCII 或非法 Base64 字节立即失败，不做过滤修复。
5. Base64 解码后循环 XOR `yeelion`。
6. 使用带异常 fallback 的 GB18030 严格解码；不得先尝试 UTF-8，也不得用替换字符吞掉损坏。

主程序和测试入口已经注册 `CodePagesEncodingProvider`，decoder 不应重复修改进程级注册状态。

### 6.5 LRCX 行解析

解析规则：

- 识别 `[mm:ss.fff]` 时间行，并兼容一至三位小数；通过整数补位换算为毫秒，避免 `double`。
- 用能覆盖负数和二/三元组的模式去除词标记：`<-?\d+,-?\d+(?:,-?\d+)?>`。
- 保留时间行在载荷中的原始顺序和全部重复项，再交给 `KuwoLyricAssembler`。
- `[kuwo:]`、`[ver:]`、`[ti:]` 等无时间标签不进入最终歌词；酷我旧 JSON 路径本来不输出这些头部，避免借精度升级扩大可见行为。
- 一行词标记损坏但行时间和可见文本仍可安全读取时，保留文本并去除能识别的标记；无法辨认的协议残片不得写入最终 LRC。
- 解析后没有任何有效时间行则视为失败并回退。

### 6.6 独立歌词编排和缓存状态

在 `KuwoTagProvider` 新增私有 `LoadLyrics(KuwoSongInfo song)`，并让以下歌词入口改调它：

- `LoadSongLyric`
- `LoadLyricForTrack`
- `LoadDeferredLyric`

`DownloadDeferredCover` 继续只调用 `LoadSongDetails`，不直接依赖 LRCX。

`KuwoSongInfo` 需要最小内部状态，区分：

- 是否已尝试 LRCX。
- 当前 `LoadedLyric` 是高精度还是 legacy。
- 旧详情是否已加载。

建议用内部 enum/字段而不是从时间戳末位推断质量。状态规则：

```text
LoadLyrics(song)
  ├─ HighPrecision 已存在 → 返回
  ├─ 尚未尝试 LRCX → TryLoadLrcx
  │    ├─ 成功 → 写 HighPrecision，返回
  │    └─ 失败/无结果 → 标记 attempted
  ├─ 已取消 → 返回现有缓存或 null
  ├─ Legacy 已存在 → 返回
  └─ LoadSongDetails → 返回 legacy 或 null
```

`PopulateSongDetails` 解析旧 `lrclist` 时：

- 高精度 `LoadedLyric` 已存在则绝不覆盖。
- 只有没有高精度结果时才写 legacy，并标记质量。
- 封面字段始终按当前规则更新，不因歌词质量守卫而跳过。

这样同时覆盖两种顺序：

1. 先下载歌词：LRCX 成功；之后封面详情只补封面，不覆盖歌词。
2. 先下载封面：旧详情可能顺带缓存 legacy；之后歌词入口仍尝试 LRCX，成功后升级缓存。

## 7. 错误、取消和端点健康策略

### 7.1 错误分类

高精度尝试至少区分：

- `Success`：主歌词非空。
- `NoCandidate`：请求成功且结构合法，但该歌曲没有高精度歌词。
- `Malformed`：魔数、响应头、Base64、zlib、编码或歌词结构损坏。
- `TransportFailure`：HTTP、网络、超时、响应过大。
- `Canceled`：用户取消或请求令牌取消。

这些可以是 provider-local enum，不扩展全局 `RemoteErrorKind`。最终 legacy 也失败时，保留最有诊断价值的现有传输错误；高精度解析失败但 legacy 成功不应把整源标成失败。

### 7.2 取消点

必须在以下位置检查取消：

- 每次请求前。
- KRC `/search` 返回后、`/download` 发起前。
- 原始字节返回后、进入可能较重的解压前。
- 高精度失败后、legacy 回退前。

decoder 本身保持同步纯函数；载荷很小，不引入额外线程。取消发生后不得记录端点不可用，也不得继续 fallback。

### 7.3 搜索内熔断

- 酷狗有两次请求且一次搜索最多处理 5 首，使用 provider 实例级 `krcUnavailableThisSearch`。
- 酷我 LRCX 是单次请求，可采用同样的实例级标志；只有端点级传输/整体协议失败才熔断，单曲无歌词不熔断。
- 标志不持久化、不跨 provider 实例、不写设置；下一次用户搜索重新探测。
- 现有酷我详情 API backoff 继续独立存在，不能与 LRCX 健康状态共用一个布尔值。

## 8. 测试设计

所有 provider 网络测试必须覆盖现有 protected virtual HTTP 方法并注入固定响应；禁止依赖实时服务。

### 8.1 酷狗 decoder 固定向量

- 真实 `krc1` 二进制向量，不使用“测试内先加密再解密”的自证 fixture。
- UTF-8 BOM。
- `22144 ms → [00:22.144]`。
- 开启格式化时走现有两位时间戳策略。
- 多个 `<relative,duration,0>` 词标记和标点/空格保留。
- 首词相对偏移非 0，仍使用显式行起始。
- 行头损坏、魔数错误、截断 XOR 数据、zlib 损坏。
- `contenttype == 2` Base64 纯文本。
- `[language:]` 中 `type=1` 翻译，`type=0` 罗马音忽略。
- 翻译损坏时主歌词仍成功。

### 8.2 酷我 decoder 固定向量

- `BuildRequestUrl` 已知答案，锁定参数顺序、XOR 和未 URL 编码的 Base64 query。
- 真实响应字节向量：头部、zlib、Base64、XOR、GB18030 全链路。
- `7.433 s → [00:07.433]`。
- 一至三位毫秒补位，不经过浮点。
- 二元和三元词标记、负数值。
- GB18030 中文/日文样本。
- 非 ASCII、非法 Base64、错误 `tp`、缺少分隔符、zlib 损坏严格失败。
- 重复时间戳三行边界，确认第一条重复项是上一行译文、第二条是当前原文。

### 8.3 Provider 编排

酷狗和酷我分别覆盖：

- 高精度 primary 成功，不调用 legacy。
- 合法响应但无候选/无有效时间行，调用 legacy。
- primary 传输失败，调用 legacy。
- primary 解析失败，调用 legacy。
- 两次请求之间取消，不发第二次请求。
- primary 失败后取消，不发 legacy。
- legacy 成功时不保留 primary 的失败状态为整源错误。
- 首次端点级故障后，本次多候选搜索不再重复调用高精度端点。
- `LyricUrl` 和结果元数据保持当前合同。

酷我额外覆盖：

- 先 LRCX 后详情封面，高精度歌词不被覆盖。
- 先详情封面后 LRCX，legacy 缓存可被高精度升级。
- LRCX 失败且已有 legacy，直接保留 legacy。
- `DownloadDeferredCover` 不调用 LRCX。
- `PopulateSongDetails` 提取前后全部 characterization 输出逐字一致。

### 8.4 最终验证

实施完成后执行：

```powershell
dotnet build .\src\MusicTag.Tests\MusicTag.Tests.csproj -c Release
.\src\MusicTag.Tests\bin\Release\net8.0-windows\MusicTag.Tests.exe
.\scripts\Verify-Build.ps1 -RunSmokeTests
git diff --check
codegraph sync
```

随后运行 GitNexus `detect_changes()`，确认只影响预期 provider、decoder、characterization 和歌词加载流程。自动测试不证明实时端点稳定性，仍需手工验证酷狗/酷我各一首有翻译和无翻译歌曲，并确认组合标签源的封面延迟加载不回退歌词精度。

## 9. 分阶段实施和提交

建议四个独立提交：

1. `refactor: extract Kuwo lyric line assembly`
   - 先补 characterization，再纯提取现有行为。
2. `feat: add Kugou KRC lyric precision`
   - KRC decoder、primary/fallback、熔断与固定向量。
3. `feat: add Kuwo LRCX lyric precision`
   - LRCX decoder、独立歌词编排、缓存质量状态与固定向量。
4. `docs: record high precision lyric implementation results`
   - 实施结果、真实手工验证、残余风险和第三方协议来源。

若任一 provider 的行为变更无法由固定响应稳定锁定，应停在该提交，不把两个源绑在同一提交中。

## 10. 验收标准

- 酷狗真实 KRC `22144` 输出 `[00:22.144]`，且最终歌词无 KRC 词标记。
- 酷我 LRCX `7.433` 输出 `[00:07.433]`，第三位不是本地补零。
- 开启格式化时间轴时，两源继续遵循当前两位四舍五入策略。
- primary 不可用时，用户仍能通过现有端点取得与改动前相同的歌词。
- 翻译缺失/损坏不拖垮可用的高精度主歌词。
- 取消不会触发后续请求或回退。
- 酷我封面详情不会覆盖高精度歌词，封面下载也不会依赖 LRCX。
- 同一源一个候选损坏不会中止其它源；端点整体故障不会对 5 个候选重复轰炸。
- 完整验证通过，且没有修改 UI、设置持久化或枚举合同。

## 11. 供 Claude 审阅的问题

请重点审阅以下问题，并尽量给出基于真实格式或开源源码的反例：

1. KRC `contenttype` 除 `0`、`2` 外是否还有必须支持的已知值？未知值直接回退是否安全？
2. KRC `type=1` 翻译是否存在需要跳过空正文行的真实样本，还是只有 `type=0` 罗马音需要偏移修正？
3. 酷我重复时间戳是否存在三条以上、或译文不紧邻下一句原文的变体，现有 assembler 的尾部启发式是否会误判？
4. 酷我解压后 Base64 是否有合法换行/空白变体；“允许 ASCII 空白、拒绝其它字节”是否覆盖全部已知实现？
5. provider 实例级熔断应只覆盖传输/响应整体损坏，还是下载候选 404 也应视作端点故障？
6. 解压后歌词大小上限应复用哪个现有常量，是否需要为 KRC/LRCX 设独立且更小的内部上限？
7. KRC 标准头部保留、LRCX 头部全部丢弃是否与当前两个 legacy 输出最一致？
8. 是否有证据表明免签名酷狗路径必须准备签名 `/v1/search` 同轮回退；若没有，本设计建议暂缓以控制维护面。
9. 本设计新增的酷我缓存质量状态是否覆盖“先封面、后歌词”和“先歌词、后封面”的所有并发顺序？
10. 是否存在许可证要求，导致即使只按协议重写而不复制实现，也需要新增第三方 notice？

## 12. 当前未解决项

- 免签名酷狗端点和酷我 LRCX 端点都属于非正式、可能变化的外部协议；legacy 回退只能降低、不能消除长期失效风险。
- 酷我现有翻译整理包含内容语言启发式和尾部 `+5000 ms` 兼容逻辑，本设计先复用而不重新定义产品语义；是否应长期保留需要单独讨论。
- 罗马音、多语歌词保存格式和真正逐词歌词文件输出均不在本轮范围。
- 本设计使用 2026-08-02 的实时协议证据；实施时仍须把固定样本纳入测试，不能依赖本地 `.claude/tmp` 文件或仅引用调研结论。
