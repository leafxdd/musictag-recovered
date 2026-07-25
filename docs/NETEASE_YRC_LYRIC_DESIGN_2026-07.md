# 网易云 YRC 逐字歌词接入设计

- 日期:2026-07-22
- 分支:`develop-net8`
- 性质:调查与实施方案(仅评估,未修改任何代码);为 [`QRC_PRECISION_REVIEW_2026-07.md`](QRC_PRECISION_REVIEW_2026-07.md) 中 QQ QRC 精度审查的后续——将同一"逐字歌词 → 三位毫秒逐行 LRC"思路推广到网易云
- 依据:本会话联网实测 + NeteaseCloudMusicApi 官方文档 + lx-music-api-server 实现,三方交叉印证
- 状态更新(2026-07-25):第六节方案已随 `f4c050f`(feat: add NetEase YRC lyric precision)落地;后续行为修复与验收见 [`LYRIC_FETCH_REPAIR_REPORT_2026-07.md`](LYRIC_FETCH_REPAIR_REPORT_2026-07.md) 与 [`LYRIC_FETCH_CROSS_REVIEW_2026-07.md`](LYRIC_FETCH_CROSS_REVIEW_2026-07.md)

## 结论

网易云存在与 QQ QRC 完全对称的逐字歌词格式 **YRC**,行/字时间都是毫秒精度。当前代码只取普通逐行 `lrc`(2 位厘秒),换到 YRC 取行起始转三位毫秒,**能真实提升精度,不是空操作**。核心转换逻辑可与 `QqQrcDecoder` 共享,但字标记正则、元数据处理、是否解密三处必须为网易云单独处理。

## 一、当前代码现状

网易云歌词端点(`NetEaseMusicTagProvider.cs:29`):

```
https://music.163.com/api/song/lyric?os=pc&id={0}&lv=-1&kv=-1&tv=-1
```

`ExtractLyricTexts`(`NetEaseMusicTagProvider.cs:666`)只读 `lrc.lyric`(普通 2 位厘秒逐行)和 `tlyric.lyric`(翻译)。

**实测:这个老端点即使加 `yv=1` 参数也不返回逐字歌词**(top keys 里没有 `yrc`)。逐字歌词只在新版 v1 端点上返回。

## 二、逐字接口与格式(联网实测)

新版 v1 端点:

```
https://music.163.com/api/song/lyric/v1?id={id}&cp=false&lv=0&kv=0&tv=0&rv=0&yv=0&ytv=0&yrv=0
```

它的 top keys 比老端点多出 `yrc`(逐字原文)、`ytlrc`(逐字翻译)、`yromalrc`(逐字罗马音),同时仍带 `lrc`/`tlyric`,可无缝回退。主域名 `music.163.com` 即可拿到明文,不必依赖偶发 DNS 失败的 `interface3` 子域,也不必走 eapi 加密。

YRC 行格式:

```
[行起始ms,行时长ms](字起始ms,字时长,0)雨(字起始ms,字时长,0)一...
```

外加可能出现的 JSON 元数据行:

```json
{"t":显示时间ms,"c":[{"tx":"作曲: "},{"tx":"柳重言","li":"头像url","or":"orpheus://app内路径"}]}
```

其中 `tx`=文字、`li`=歌手头像、`or`=app 内跳转路径。

### 精度实测(与 QQ 审查同法统计)

| 歌曲 | yrc 行数 | 字标记 | 行起始 ms 末位分布 | 首字起始 − 行起始 |
|---|---|---|---|---|
| 起风了 (1330348068) | 54 | 323 | 0–9 均匀(真 1ms 精度) | 54/54 全为 0 |
| 孤勇者 (1901371647) | 68(含 10 条 JSON 制作行,时间轴行 57) | 558 | 56/57 末位为 0(10ms 网格) | 57/57 全为 0 |

> 勘误(2026-07-25 复测):1901371647 是**孤勇者(陈奕迅)**,原稿误标为 Lemon;该行原有"字标记 —、68/68 全为 0"两处不实——字标记实为 558 个,"68"把 JSON 制作行也计入了,可比对的时间轴行为 57。另:2026-07-25 复测发现起风了(1330348068)的 `yrc` 已不在明文 v1 端点返回(带 `X-Real-IP`、参数换 `-1` 均无效),明文通道的逐字覆盖按歌波动,回退 `lrc` 路径兜底。

行起始是真正的逐毫秒(不像 QQ 678268984 那首 AI 词的 10ms 网格),而普通 `lrc` 仍是 2 位厘秒。所以换到 yrc 取行起始转三位毫秒能真实提升精度。且"行起始 == 首字起始"这条规律与 QQ 完全一致(网易 + QQ 合计 122/122 行 diff 恒为 0),说明两家打轴工具在格式上都把行起始写成首字起始。

## 三、三方交叉印证

| 事实点 | 本会话实测 | NeteaseCloudMusicApi 文档 | lx-music-api-server | 一致性 |
|---|---|---|---|---|
| 逐字字段名 `yrc` | ✓ | ✓ `yrc.lyric` | ✓ `body["yrc"]["lyric"]` | 完全一致 |
| 逐字翻译 `ytlrc` / 罗马音 `yromalrc` | ✓ 见字段 | ✓ | ✓ 都取 | 一致 |
| 端点 `api/song/lyric/v1` | ✓ 明文可用 | ✓ 底层同一个 | 用 `eapi/song/lyric/v1`(加密) | 一致(加密非必需) |
| 老 `lyric` 端点加 `yv=1` 不返回逐字 | ✓ | 文档另立 `/lyric/new` | — | 印证 |
| 行格式 `[start,dur](wstart,wdur,0)字` | ✓ | ✓ 逐字段解释 | ✓ | 一致 |
| 行起始 == 首字起始 | ✓ 122/122 行 | — | — | 本会话独有证据 |

## 四、官方文档补出的关键细节(实测遗漏)

1. **JSON 元数据行的完整结构**。yrc 里 `{...}` 开头的行是制作信息,结构为
   `{"t":显示时间ms,"c":[{"tx":"作曲: "},{"tx":"柳重言","li":"头像url","or":"orpheus://app内路径"}]}`。
   转普通 LRC 时这些行**必须处理**(丢弃,或把 `c[].tx` 拼成一行文本),否则会把整段 JSON 泄进歌词。这是网易云独有、QQ QRC 没有的坑,也是最容易漏的点。

2. **字时长字段疑似"厘秒"**。官方文档社区注解把 `(字起始ms, 字时长, 0)` 的第 2 个数标为"厘秒/0.01s"——即字**起始**是毫秒、字**时长**可能是厘秒,单位不统一。该说法存疑(更可能是注解者笔误),但**对逐行 LRC 方案完全无影响**,因为逐行方案只用行起始/字起始(都是毫秒),不用字时长。仅当未来要做真逐字导出时,须先用"下一字起始 − 本字起始 vs 本字时长"实测校准这个单位。

3. **`interface3` 子域走 eapi 加密**。lx-music 用的是加密路径;而实测证明主域名 `music.163.com/api/song/lyric/v1` 明文 GET 就能拿到 yrc,**不必引入 eapi 加密**,比 QQ 那套 DES 简单得多。上一轮 `interface3` 偶发 DNS 失败也印证了应优先用主域名。

## 五、YRC vs QQ QRC 差异(决定能否复用 QqQrcDecoder)

| 维度 | QQ QRC | 网易云 YRC |
|---|---|---|
| 获取 | POST + DES 解密 + zlib 解压 | **明文 GET**,无解密 |
| 行时间轴 | `[start,dur]` 毫秒 | `[start,dur]` 毫秒(相同) |
| 字标记 | `(start,dur)` **两元组** | `(start,dur,0)` **三元组** |
| 元数据 | XML `LyricContent` 属性内嵌 `[ti:]…` | 独立 `{"t":,"c":[]}` JSON 行 + 也有 `[ti:]` 标签 |
| 翻译 | `trans`(普通 2 位 LRC) | `tlyric`(2 位)或 `ytlrc`(逐字) |

**结论:转换逻辑(取行起始 → `FormatTimestamp` → 去字标记 → 逐行输出)可共享,但 `QqQrcDecoder.ConvertToLineLyric` 不能直接调用**——字标记正则、元数据处理、是否解密三处都不同:

1. **字标记正则不同**:yrc 是三元组 `(0,290,0)`,QQ 的 `QrcWordTimestampRegex = \(\d+,\d+\)` 匹配不了,需泛化为 `\(\d+,\d+,\d+\)`。
2. **无解密**:yrc 是明文 JSON 字段,不像 QQ 要走 DES + zlib(`DecryptLyrics` 那套完全不需要)。
3. **JSON 元数据行**:yrc 行首可能是 `{"t":…,"c":[…]}`,需跳过或转成 `[by:]`/制作信息;QQ 那边是 XML `LyricContent` 属性,处理方式不同。

## 六、推荐方案

**1. 端点切换**(`NetEaseMusicTagProvider.cs:29`)

```
https://music.163.com/api/song/lyric/v1?id={0}&cp=false&lv=0&kv=0&tv=0&rv=0&yv=0&ytv=0&yrv=0
```

用主域名明文,不引入 eapi。v1 同时返回 `yrc` 和 `lrc`,无逐字歌词的老歌自动只有 `lrc`,**回退零成本**。

**2. 解析优先级**:`yrc` 有 → 转三位毫秒 LRC;否则回退现有 `lrc`/`tlyric` 路径(即当前 `ExtractLyricTexts`)。

**3. 新增 YRC → LRC 转换**,要点:

- 逐行扫描:`{` 开头 → JSON 元数据行,跳过(或把 `c[].tx` 拼成无时间戳的一行);`[ti:]`/`[ar:]` 等 → 原样保留;`[数字,数字]` 开头 → 时间轴行。
- 时间轴行:取行起始(字段 1)→ `LyricTextProcessor.FormatTimestamp(ms, !ReformatTimetag)` → 用正则 `\(\d+,\d+,\d+\)`(**三元组**)去掉字标记 → 输出 `[mm:ss.fff]文字`。
- 沿用 `LyricDownload_ReformatTimetag` 开关(3 位默认 / 2 位四舍五入),与 QQ 行为一致。

**4. 代码复用**:把 `QqQrcDecoder.ConvertToLineLyric` 的"行切片 + 格式化 + 去字标记"核心抽成共享 helper,参数化(字标记正则、元数据行预处理委托)。QQ 传两元组正则 + XML 预处理,网易传三元组正则 + JSON 行预处理。避免两份平行实现漂移。

**5. 翻译**:最省事是继续用普通 `tlyric`(2 位),与现状一致、零对齐风险;进阶可用逐字 `ytlrc` 转换后走 `AlignAndSplitTranslatedLyric`,取舍与 QQ 报告里"翻译键 2 位/原文 3 位靠邻近吸附"完全平行。**建议一期先只升级原文精度,翻译维持现状。**

## 七、风险与验证缺口

- **JSON 元数据行不处理会污染输出** —— 网易独有,必须在解析里显式跳过/转换。这是最容易漏的点。
- **字时长单位存疑** —— 仅影响未来逐字导出,逐行方案无关;若做逐字须先实测校准。
- **端点变更影响面** —— `LoadLyrics` 有 3 处调用者(均在 `NetEaseMusicTagProvider.cs` 内),`impact` 风险低;但 `lyricEndpointFormat` 也用于 `lyric.LyricUrl`(`NetEaseMusicTagProvider.cs:399`)展示,需一并确认。
- **characterization** —— 明文 fixture,比 QQ 的 DES 加密夹具简单;应覆盖:含 JSON 元数据行、三元组字标记、有 yrc / 无 yrc 回退 lrc、`ReformatTimetag` 开关两态。当前 `NetEaseProviderCharacterization.cs` 对逐字完全无覆盖。
- 本轮 GitHub 对 `163MusicLyrics`/`LDDC` 的代码搜索返回空(索引限制),未取到它们的 C#/Python 转换源码逐行对照;但 NeteaseCloudMusicApi(主流权威)+ lx-music + 本会话实测已三方印证,结论可靠。若需与 `163MusicLyrics`(与本项目 QQ 解码同源)逐行核对,可继续抓取。
- 本文全部数据为本会话真实抓取(命令输出在案),未运行仓库测试、未声称任何构建验证。
