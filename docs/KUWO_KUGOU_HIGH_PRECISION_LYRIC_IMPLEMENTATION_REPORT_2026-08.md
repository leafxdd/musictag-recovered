# 酷狗 KRC / 酷我 LRCX 高精度歌词实施报告

- 日期：2026-08-02
- 分支：`develop-net8`
- 设计基线：`134c233 docs: incorporate lyric precision review`
- 产品代码终点：`8fd127c feat: add Kuwo LRCX lyric precision`
- 测试补强终点：`9862443 test: cover invalid Kugou lyric encoding`
- 范围：酷狗 KRC、酷我 LRCX 的逐词格式解码、逐行 LRC 转换、翻译、回退、取消、缓存和错误隔离

## 1. 结论

酷狗和酷我现已优先使用各自的高精度歌词通道，并在关闭“格式化时间轴”时输出真实三位毫秒：

- 酷狗 KRC 的 `22144 ms` 输出为 `[00:22.144]`，不再使用被服务端截断成两位的普通 LRC 时间；
- 酷我 LRCX 的 `7.433 s` 输出为 `[00:07.433]`，第三位不再由旧 `lrclist` 补成恒定的 `0`；
- 开启“格式化时间轴”时，两源继续调用既有 `LyricTextProcessor.FormatTimestamp`，保持两位小数和四舍五入到 10 ms 的兼容行为；
- 高精度通道无候选、不可用或载荷损坏时，仍尝试原有 legacy 端点；用户取消时不继续请求或回退；
- 酷我封面详情加载与歌词加载已分离，高精度歌词不会被后续封面详情中的低精度歌词覆盖。

本轮完整自动验证为 `952 passed, 0 failed`。Debug、Release 构建通过，Release 应用启动后保持存活，未发现致命异常日志。

本次仍只把逐词格式转换为逐行 LRC，没有保存逐词时间轴，也没有增加 UI 或设置项。外部端点均为非官方开放接口，legacy 回退是尽力而为，不构成可用性保证。

## 2. 实施提交

| 提交 | 内容 | 行为性质 |
|---|---|---|
| `9eb9591` | 提取 `KuwoLegacyLyricAssembler`，并先补齐 alternate、尾部特判和日文双态 characterization | 等价重构，锁定旧酷我歌词行为 |
| `17cfad9` | 新增酷狗 KRC 解码、三位毫秒逐行转换、翻译、回退、取消和搜索内熔断 | 用户可见精度修正 |
| `8fd127c` | 新增酷我 LRCX 请求/解码、专用翻译配对、回退、缓存质量和封面隔离 | 用户可见精度修正 |
| `9862443` | 增加非法 UTF-8 字节定向用例，锁定纯文本 KRC 响应的严格解码 | 测试补强，不改产品行为 |

设计与 Claude 交叉审阅的处置记录保存在
[`KUWO_KUGOU_HIGH_PRECISION_LYRIC_IMPLEMENTATION_DESIGN_2026-08.md`](KUWO_KUGOU_HIGH_PRECISION_LYRIC_IMPLEMENTATION_DESIGN_2026-08.md)。

## 3. 酷狗 KRC 实施结果

### 3.1 获取和解码

新的 primary 路径使用：

```text
GET https://lyrics.kugou.com/search?...&hash={hash}&duration={ms}
GET https://lyrics.kugou.com/download?...&id={id}&accesskey={key}&fmt=krc
```

只有歌曲具备 32 位十六进制 hash 和正时长时才尝试 KRC。下载载荷按 `contenttype` 严格分流：

- `0`：Base64 → 校验四字节 `krc1` → 固定 16 字节 key 循环 XOR → zlib → 严格 UTF-8；
- `1`、`2`：Base64 → 严格 UTF-8 纯文本；
- 其它值：按当前歌曲载荷损坏处理并回退，不猜测格式。

解压后大小限制为 2 MiB，Base64、魔数、压缩体和 UTF-8 均采用严格校验。首部 UTF-8 BOM 会被移除。

### 3.2 时间轴和翻译

正常 KRC 行使用显式 `[lineStartMs,lineDurationMs]` 的 `lineStartMs`，再移除 `<relativeStart,duration,0>` 逐词标记。不会把样本中“首词偏移通常为 0”当作协议保证。

损坏的行时长仅在行起始和首词相对偏移均可解析时兜底；单行无法恢复不会中止整首。标准 `ti/ar/al/by/offset` 元数据保留，KRC 私有元数据不写入 LRC。

`[language:]` 中只读取 `type == 1` 的逐行翻译并按原始时间行索引对齐。罗马音 `type == 0` 不在本轮范围。翻译 Base64、JSON 或行数损坏时，只放弃译文，不降低可用主歌词的精度。

### 3.3 回退和错误隔离

- 高精度成功：直接返回，不请求原 `m3ws` 端点；
- 合法响应但无候选：只对当前歌曲回退，不熔断；
- 单候选载荷损坏：只对当前歌曲回退，不连累同批其它候选；
- search/download 的传输失败或顶层合同损坏：本 provider 实例内熔断，避免同一搜索最多 5 首重复撞击故障端点；
- 用户取消：停止 download 或 legacy 请求；
- KRC 与 legacy 同时失败：保留可诊断失败，不把空结果伪装成成功。

## 4. 酷我 LRCX 实施结果

### 4.1 legacy 行为隔离

旧 `songinfoandlrc` 的重复时间戳、`ContainsChinese` 语言启发式、时间戳借用和尾部 `+5000 ms` 特判已原样提取到 `KuwoLegacyLyricAssembler`。提取前新增 characterization 锁定：

- 单语、小数秒和首时间点重复；
- 中间重复时间戳与尾部多 alternate；
- 未翻译缺口的下一时间戳借用；
- 日文纯假名与含汉字两种 `ContainsChinese` 分支。

LRCX 不复用这套 legacy 启发式，避免把新的协议语义反向改变旧回退输出。

### 4.2 请求和响应解码

请求明文为：

```text
user=12345,web,web,web&requester=localhost&req=1&rid=MUSIC_{id}&lrcx=1
```

明文以 ASCII `yeelion` 循环 XOR，再作标准 Base64，直接拼到
`https://newlyric.kuwo.cn/newlyric.lrc?` 后。只有正整数 Track ID 才会进入该路径。

响应按原始字节处理：

1. 在 16 KiB 头部上限内查找首个 `\r\n\r\n`；
2. 要求头部存在独立 `tp=content` 行；
3. 对正文 zlib 解压，并限制在 2 MiB；
4. 严格验证 ASCII Base64，只允许规范空白；
5. Base64 解码后再以 `yeelion` XOR；
6. 使用异常 fallback 的 GB18030 解码，不以替换字符吞掉损坏。

### 4.3 时间轴和翻译

LRCX 行时间支持一至三位小数，完全用整数补位换算，避免浮点误差。二元/三元、包含负数的 `<...>` 词标记均被移除；其它完整尖括号片段会被丢弃，仍残留不配对的 `<` 或 `>` 时拒绝该行，避免协议残片写入歌词正文。

翻译采用 LRCX 的重复时间戳协议，不使用语言识别：同时间戳第一条是上一行译文槽，第二条是当前原文；空白槽仅表示无译文。真实 Lemon 边界序列已锁定为：

```text
[00:01.547]夢ならば
[00:02.880]如果只是一场梦
[00:02.880]どれほどよかったでしょう
```

译文“如果只是一场梦”最终绑定到上一句的 `[00:01.547]`，当前日文原文保持 `[00:02.880]`。

### 4.4 编排、缓存和封面隔离

酷我新增独立歌词入口和三档缓存质量：`None`、`Legacy`、`HighPrecision`。

- 已有高精度歌词直接复用；
- 已有 legacy 歌词且该候选尚未尝试 LRCX 时仍会尝试，成功后升级；非取消失败会保留 attempted 状态，同一候选随后复用 legacy，不重复撞击 LRCX；
- LRCX 成功后，后续旧详情只补封面，不覆盖高精度歌词；
- 搜索结果没有封面直链、需要延迟封面回退时仍只调用旧详情，不依赖 LRCX；已有 `SearchAlbumCoverUrl` 时直接下载图片；
- 取消发生在 LRCX 请求期间时清除本次 attempted 状态，使下一次用户操作可以重新尝试；
- 传输或整体协议失败只在当前 provider 实例内熔断，合法无时间行只对当前歌曲回退；
- LRCX 失败而已有 legacy 缓存时保留缓存并清除临时高精度错误；两条路径都失败时保留最有诊断价值的错误。

## 5. Characterization 证据

### 5.1 酷狗

- 真实 `lyrics.kugou.com` KRC 固定向量，歌词 ID `216374858`；
- 锁定 `22144 ms → [00:22.144]`，以及开启格式化后的两位四舍五入；
- 覆盖 `contenttype` 0、1、2，严格 Base64/UTF-8、魔数和 zlib 错误；
- 覆盖 `type == 1` 翻译、译文损坏、行数不匹配和罗马音忽略；
- 覆盖 primary 成功、无候选、传输熔断、单曲损坏、双路径失败和两个取消点。

### 5.2 酷我

- 真实 `newlyric.kuwo.cn` 响应固定向量，曲目 ID `198554068`；
- 响应长度 `7612`，SHA-256
  `34243482A35FCEFDB2B59672477C4F2830E447BC0F4DC162131BD1ACECDD3CA0`；
- 锁定 `[00:07.433]词：唐恬`，以及开启格式化后的两位输出；
- 覆盖请求 URL 已知答案、完整解码链、GB18030 中日文本、负数二/三元词标记和严格错误；
- 覆盖空白译文槽、上一行译文配对、primary/legacy 回退、熔断、取消后重试、缓存升级和错误优先级；
- 旧 provider 测试已注入合法无时间行 LRCX 响应，避免 characterization 意外访问实时网络。

固定响应测试验证协议和代码路径，但不证明当天实时端点可达。

## 6. 验证记录

执行并通过：

```powershell
dotnet build .\src\MusicTag.Tests\MusicTag.Tests.csproj -c Release
.\src\MusicTag.Tests\bin\Release\net8.0-windows\MusicTag.Tests.exe
.\scripts\Verify-Build.ps1 -Configurations Release
.\scripts\Verify-Build.ps1 -RunSmokeTests
git diff --check
codegraph sync
```

最终结果：

- Debug 构建：成功；
- Release 构建：成功；
- characterization：`952 passed, 0 failed`；
- Release 启动冒烟：`StartedAndStayedAlive=True`；
- 致命异常日志扫描：`NoFatalExceptionLogs=True`；
- 构建仍有 4 条既有 obsolete API 警告，与本次歌词改动无关；
- 酷我功能提交前的 GitNexus staged 分析：7 个文件、64 个变更符号、2 条预期歌词流程，整体风险 `medium`；该数字不代表酷狗、legacy 提取和报告提交的合计文件数。

`952 passed`、启动存活和异常日志扫描来自本次 `Verify-Build.ps1 -RunSmokeTests` 控制台记录；脚本未把这三项另存为单独日志。`artifacts/build-sln-debug.log` 和 `artifacts/build-sln-release.log` 持久记录了双配置构建输出。

## 7. GitNexus 索引故障处置

增量刷新曾失败：

```text
FTS index 'file_fts' is inconsistent
```

已执行完整清理和重建：

```powershell
node .gitnexus/run.cjs clean --force
gitnexus analyze
```

第一次完整重建结果为 `4,355 nodes | 13,270 edges | 267 clusters | 300 flows`。功能提交后的一次增量刷新成功，但在测试/报告收尾时再次增量刷新又复现同一 FTS 错误；第二次清理并全量重建后为 `4,382 nodes | 13,298 edges | 267 clusters | 300 flows`，索引对齐 `9862443`。

第二次重建后，`context`、`impact`、`query` 和 `detect_changes` 均可用，`TryLoadLrcx` context 能返回精确调用关系，先前偶发的 `LadybugDB not initialized` 已消失。仍未解决的是 **FTS 在后续增量更新中可能再次不一致**；当前可靠恢复方式是 `clean --force` 后全量 `analyze`。这属于 GitNexus 索引维护问题，未发现产品代码、构建或运行时受影响；若工具升级后仍复现，应连同“生成指导文件被重写后执行增量刷新”的步骤提交最小复现。

## 8. 未实施或需要后续讨论

### 8.1 不保留逐词时间轴

KRC、LRCX 目前都只用于恢复准确的行起始，再转换为普通逐行 LRC。真正逐词文件输出会牵涉格式选择、播放器兼容和编辑器/UI 行为，应另立功能设计。

### 8.2 不下载罗马音

酷狗 KRC 的 `type == 0` 罗马音被明确忽略。若接入，需要先决定原文、翻译、罗马音的保存顺序、开关及外置/内嵌歌词格式。

### 8.3 不还原酷我逐词混淆时间

`[kuwo:xxx]` 参与逐词时间还原，但逐行方案只使用标准行标签，因此没有实现该混淆算法。这不影响本轮三位毫秒逐行目标。

### 8.4 不加入酷狗签名搜索备用端点

免签名 `/search` 在调研时可用，没有证据要求同轮再维护带 MD5 签名的 `/v1/search`。只有免签名路径持续失效并取得真实响应后，才应增加第二套协议。

### 8.5 不统一四源 decoder

QQ QRC、网易 YRC、酷狗 KRC、酷我 LRCX 在传输、编码、行时间、词标记和翻译载体上差异显著。当前稳定共享点仍只有 `LyricTextProcessor.FormatTimestamp`；本轮保持 provider-local decoder，避免模式参数和协议分支扩散。

### 8.6 保留酷我 legacy 兼容算法

旧 `ContainsChinese` 和尾部特判可能不是理想的语言归属算法，但它只服务 legacy 回退，且已成为可观察兼容行为。没有新的真实反例前不借精度升级改写它。

## 9. 剩余风险和手工验证建议

- 酷狗、酷我高精度与 legacy 端点都可能随服务端协议或风控变化；回退降低故障面，但两条路径可以同时失效。
- 酷狗 `contenttype == 2` 仍无本轮实时样本，当前行为由开源交叉证据和合成固定用例覆盖。
- 酷我真实全链路固定向量不含译文；Lemon 翻译边界来自真实明文序列，但传输包装由测试 helper 构造。
- 自动测试没有验证实时网络、地区差异或长时间限流，也没有覆盖所有纯音乐、超长歌词和冷门曲目。
- 自动启动冒烟不证明组合标签源中的实时选择、歌词下载、封面延迟加载交互全部正确。
- 取消用例使用 scripted provider，证明请求返回后的编排会停止；没有用阻塞中的真实 HTTP 集成用例验证传输中断时机。
- 封面相关用例直接验证详情解析不会覆盖高精度歌词，但没有执行完整的延迟下载、文件写入、锁和 UI 并发路径。

建议手工抽查：

1. 酷狗各选一首有翻译和无翻译歌曲，确认三位时间、译文对齐和 legacy 回退提示；
2. 酷我各选一首有翻译和无翻译歌曲，确认第三位非恒 0，且先加载封面/先加载歌词两种顺序都不覆盖高精度歌词；
3. 在组合标签源中取消一次进行中的酷狗或酷我歌词下载，再重试，确认无额外请求和缓存卡死；
4. 保留失败时的响应合同、HTTP 状态和时间，不记录完整歌词或访问凭据，再据此决定是否调整端点或容错。

## 10. 工作区边界

本报告未纳入以下用户本地材料：

- `docs/KUWO_KUGOU_LYRIC_PRECISION_2026-08.md`；
- `.claude/tmp/lyric-spotcheck/`。

它们继续留在本地，不属于产品或报告提交范围。
