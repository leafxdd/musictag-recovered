# QQ QRC → 普通 LRC 毫秒精度独立审查

- 日期:2026-07-19
- 分支:`develop-net8`(审查时 QRC 功能改动尚未提交:`QqQrcDecoder.cs` 新增、`QqMusicTagProvider.cs` 与 `ProviderLyricCharacterization.cs` 修改)
- 性质:只读分析,未修改任何代码;本文为 CLAUDE.md 交叉审查提示(已随 `ee39b80` 移除)要求的独立审查结论
- 目标歌曲:QQ songID **678268984**(songmid `004VNDLG1kbGwO`,DJ 版,时长 165 秒)

## 一、事实证据

### 1.1 调用链(CodeGraph 追踪)

`QqMusicTagProvider.LoadLyrics`(`src/MusicTag/MusicTagWinApp.Writers/QqMusicTagProvider.cs:280`,QRC 优先)
→ `CreateQrcLyricResult`(`:348`)
→ `ParseQrcLyricResponse`(`:368`,在 `:377` 读取 `LyricDownload_ReformatTimetag` 决定 3 位/2 位毫秒)
→ `QqQrcDecoder.ConvertToLineLyric`(`src/MusicTag/MusicTagWinApp.Writers/QqQrcDecoder.cs:82`,行切片 + 去字标记)
→ `LyricTextProcessor.FormatTimestamp`(`src/MusicTag/MusicTag.Composer/LyricTextProcessor.cs:254`)。

QRC 解析失败或载荷为空时回退传统 Base64 LRC 端点(`QqMusicTagProvider.cs:294-300`)。`LoadLyrics` 的调用者共三处,均在 `QqMusicTagProvider.cs` 内:词搜索 `SearchLyrics:173`、曲目结果的延迟加载闭包 `:271-275`、`LoadLyricsForTrack:431`。

### 1.2 联网实测数据

请求接口与 `BuildQrcLyricRequestBody`(`QqMusicTagProvider.cs:303-346`)完全一致:
`POST https://u.y.qq.com/cgi-bin/musicu.fcg`,module `music.musichallSong.PlayLyricInfo`,method `GetPlayLyricInfo`,songID 678268984。

实测返回(2026-07-19):`req_0.code:0, qrc:1, qrc_t:1780627296`(≈2026-06-04 词库更新时间戳),`lyric` 为 6624 个 hex 字符,`trans` 为空(该曲无翻译)。带与不带歌名/歌手/专辑元数据请求,返回逐字节相同。

用仓库 `QqDesHelper` 的移植版解密(QQ 私改了 DES S-box——`sbox2` 第 2 行、`sbox4` 第 3 行与标准 DES 不同,标准 3DES 库解不开),得到 7434 字节 QRC XML。其头部为 `[by:…AI歌词v1.0]`:**该曲现行 QRC 是 AI 生成的时间轴**。

### 1.3 毫秒末位分布(62 行、461 个字标记)

| 统计项 | 结果 |
|---|---|
| 行起始 `[start,…]` 末位 | `{0: 59, 5: 3}` —— 全部为 5ms 整倍数,59/62 为 10ms 整倍数 |
| 字起始 `(start,…)` 末位 | `{0: 429, 1–9: 共 32}` |
| 首个字起始 − 行起始 | **62/62 行全部为 0** |

末位非 0 的 32 个字时间全部位于片头 7 行制作人员行(行起始 0/15/30/…/90ms,字时间是 0,1,2,… 的假索引);**416 个真实演唱字时间全部是 10ms 整倍数**。即该曲源数据本身只有 10ms 粒度,不存在可回收的 1ms 精度。

### 1.4 对照样本(米津玄師 Lemon,songid 213086592,真人打轴)

行起始末位在 0–9 均匀分布(`{0:7, 1:9, 2:6, 3:7, 4:4, 5:4, 6:3, 7:7, 8:3, 9:7}`,真 1ms 精度),且 57/57 行同样"行起始 == 首字起始"。两首合计 **119/119 行 diff=0**:QQ 打轴工具在格式上就把行起始写成首字起始。

### 1.5 翻译载荷格式(Lemon 实测)

`trans` 字段解密后是**普通 2 位厘秒 LRC**(含 `[kana:…]` 头与 `//` 占位行),不是 QRC XML。它进入 `ConvertToLineLyric` 后因无 `[n,n]` 匹配而原样返回(`QqQrcDecoder.cs:91-94`)。

### 1.6 传统 LRC 端点对照(678268984)

`fcg_query_lyric_new.fcg` 返回 62 个时间戳,全为 2 位厘秒,且是**向下截断**(15ms→`[00:00.01]`);本应用 2 位模式是四舍五入(15ms→`[00:00.02]`,`LyricTextProcessor.cs:263-269` 注释明示)。QRC 路径精度严格优于传统端点。

代表行转换对照(行起始 ms → 3 位 / 应用 2 位四舍五入 / QQ 官方 LRC 截断):

| ms | 3 位 | 2 位(应用) | QQ 官方 LRC |
|---|---|---|---|
| 15 | `[00:00.015]` | `[00:00.02]` | `[00:00.01]` |
| 45 | `[00:00.045]` | `[00:00.05]` | `[00:00.04]` |
| 3810 | `[00:03.810]` | `[00:03.81]` | `[00:03.81]` |
| 160140 | `[02:40.140]` | `[02:40.14]` | `[02:40.14]` |

## 二、当前根因判定

**对该曲而言,当前代码没有精度丢失问题。**

- 默认配置(`src/MusicTag/musictag/MusicTag.config` 中 `LyricDownload_ReformatTimetag=False` → 3 位毫秒)下,`ConvertToLineLyric` 输出与 QRC 行起始逐毫秒一致。
- `TimestampRegex`(`LyricTextProcessor.cs:28`)支持 1–3 位小数,`ParseMillisecondPart`(`:244-252`)补齐不足 3 位,3 位时间戳往返解析无损。
- 开关打开时的 10ms 四舍五入是明示的特性行为(`:263-269`)。
- 二次重排检查:QRC 单语路径只格式化一次;含翻译时 `AlignAndSplitTranslatedLyric`(`:502-578`)重排一次,保存路径 `GetFormattedLyricText`(`src/MusicTag/MusicTagWinApp.Adapter/LyricSearchResult.cs:118/:131`)在相关开关全关时原样透传,开关开时的再舍入对已是 10ms 倍数的值幂等——**无叠加损失**。

## 三、方案对比

1. **保持行级时间(现状)**:对两首实测歌曲输出与源精度完全一致;零改动、零风险。
2. **每行首个有效字时间**(此前调查的推荐):实测 119/119 行与方案 1 输出**逐字节相同**,在真实数据上是空操作。其唯一价值是行时间戳损坏时的兜底;但现行代码对无法解析的行起始是整行丢弃(`QqQrcDecoder.cs:107-110`),兜底方向应反过来——**行时间为主、首字时间为兜底**,而非"首字为主、行时间为兜底"。
3. **保留 QRC/增强 LRC 逐字格式**:唯一能保留逐字时序的方案,但 `TimestampRegex` 不识别 `(n,n)`,重排/翻译合并/保存漏斗全部会破坏字标记,需要新表示层与下游播放器支持,成本高,超出普通 LRC 契约。

## 四、推荐设计

**维持方案 1,不改转换策略。**若要加固,做最小防御:行起始 `long.TryParse` 失败时改用该行第一个可解析的 `(start,…)` 作为行时间,仍失败才丢行——对现有数据零行为变化,风险低。

翻译对齐保持现状:实测 trans 是 2 位 LRC,与 3 位原文的时间键相差 1–9ms,靠 `AlignAndSplitTranslatedLyric` 的 <1000ms 邻近吸附(`LyricTextProcessor.cs:521-522`)合并并保留原文 3 位时间戳,无需改全局时间格式。

若实施任何改动,建议新增 characterization fixtures:

1. 行起始 ≠ 首字起始的合成 QRC(锁定策略选择);
2. 行起始损坏 + 字标记有效(锁定兜底路径);
3. 3 位原文 + 2 位普通 LRC 翻译(锁定邻近吸附——现有两个 QRC 测试 `src/MusicTag.Tests/Tests/ProviderLyricCharacterization.cs:228-264` 均为单语,`ParseQrcLyricResponse:389-394` 翻译分支完全无覆盖);
4. 含 `//` 占位与 `[kana:]` 头的翻译载荷。

## 五、风险与验证缺口

- 受影响文件/风险:维持现状 = 无风险;字时间兜底 = `QqQrcDecoder.cs` 低风险;逐字格式 = `QqQrcDecoder.cs`、`LyricTextProcessor.cs`、`LyricSearchResult.cs` 及全部歌词消费端,高风险。
- 审查时 CodeGraph 汇报 `ConvertToLineLyric`"无覆盖测试"是索引陈旧(工作区未提交的测试已覆盖);提交 QRC 功能前需 `codegraph sync`。
- QRC 翻译分支无任何测试;本次实测证明其真实输入形态(2 位普通 LRC)与现有 QRC-XML 假设不同。
- 附注:QRC XML 的 `LyricContent` 属性内含字面换行,XML 属性规范化会把换行折成空格;`ConvertToLineLyric` 按 `[n,n]` 正则切片、不依赖换行,故不受影响。
- 本审查全部数据为 2026-07-19 真实抓取;未运行仓库测试,不构成构建验证。

## 六、交叉对比:最可能的分歧点

1. **"首字时间"方案是否有实际收益**——本审查数据(2 曲 119 行 diff 恒为 0)说明没有;若另一审查基于 2026-06-04 之前的旧 QRC(该曲 `qrc_t` 显示词库近期被 AI 版本替换),可能得出相反结论。
2. **678268984 是否仍是合适的调查标的**——其现行 QRC 是 10ms 网格的 AI 占位时间轴,已不构成"行粗字精"的证据。
3. 翻译 2 位/原文 3 位的**时间键错位**是靠邻近吸附容忍即可,还是应视为需归一化的缺陷。
4. 2 位模式**四舍五入 vs QQ 官方截断**(15ms→`.02` vs `.01`)是否需要向官方对齐。
5. 是否值得投入增强逐字格式。
