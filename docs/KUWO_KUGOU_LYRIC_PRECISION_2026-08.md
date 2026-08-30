# 酷我 / 酷狗歌词时间轴精度实测与开源交叉对比

- 日期:2026-08-02
- 分支:`develop-net8`
- 性质:只读调查,未修改任何代码
- 方法:本会话真实联网抓取(命令输出在案)+ 仓库现有实现代码核对 + GitHub 开源实现源码交叉对比
- 背景:QQ(QRC)与网易云(YRC)已完成"逐字歌词 → 三位毫秒逐行 LRC"升级,见
  [`QRC_PRECISION_REVIEW_2026-07.md`](QRC_PRECISION_REVIEW_2026-07.md)、
  [`NETEASE_YRC_LYRIC_DESIGN_2026-07.md`](NETEASE_YRC_LYRIC_DESIGN_2026-07.md)、
  [`LYRIC_FETCH_CROSS_REVIEW_2026-07.md`](LYRIC_FETCH_CROSS_REVIEW_2026-07.md)。
  本文回答同一问题在酷我 / 酷狗上的答案。

## 结论

| | 逐字歌词 | 应用当前拿到的时间轴 | 源数据真实精度 | 可升级 |
|---|---|---|---|---|
| **酷狗** | **有(KRC)** | `[00:22.14]` 两位厘秒(截断) | **真 1 ms** | **是** |
| **酷我** | **有(LRCX)** | `[00:07.430]` 三位但末位恒 0 | **真 1 ms** | **是** |

**两个源都在丢失精度**,机制相同:服务端存在毫秒级逐字格式,而应用取的是被服务端
**截断**(非四舍五入)到厘秒的普通版本。两者都可以按 QQ/网易的同一套思路升级。

> **重要更正**:本文初稿曾结论"酷我未发现逐字歌词、厘秒即源上限"。该结论**错误**,
> 系仅凭端点探测(明文参数请求 `newlyric.kuwo.cn` 得到 `TP=ERROR REQUEST`)得出。
> GitHub 开源实现交叉对比推翻了它:该端点**要求参数加密**,加密后可取到逐字 LRCX。
> 已按真实抓取数据重写第二节。这正是"缺乏证据 ≠ 证据缺乏"的实例。

## 一、酷狗

### 1.1 当前实现现状

`KugouTagProvider.cs:47` 使用:

```
https://m3ws.kugou.com/api/v1/krc/get_krc?keyword={0}&hash={1}&timelength={2}
```

端点名含 `krc`,但返回的 `data.lrc` 是**服务端已解码并降精度的纯文本 LRC**,另有 `data.landata`
翻译数组(`ParseLyricResponse:414-432` 读 `lrc`,并按 `type==1` 取翻译)。

实测该端点无法取得 KRC 载荷:追加 `fmt=krc`、`ver=1&client=pc`、`charset=utf8&fmt=krc&ver=1`
三种参数变体,`data` 字段恒为 `[landata, lrc]`,首行恒为 `[00:22.14]`。

`ParseLyricResponse` 把 `data.lrc` 原样赋给 `lyric.Lyric`,而 `GetFormattedLyricText`
(`LyricSearchResult.cs:136`)在默认开关全关时原样透传。**酷狗因此是四个源里唯一输出两位
厘秒时间戳的源**。仅当同时存在翻译且启用翻译下载时,`MergeDownloadedLyrics`
(`LyricTextProcessor.cs:631`)才会重排时间戳。

### 1.2 官方 KRC 路径(实测可用,无需 cookie/签名)

```
1) https://lyrics.kugou.com/search?ver=1&man=yes&client=pc&hash={hash}&duration={ms}
   → candidates[0].id + candidates[0].accesskey
2) https://lyrics.kugou.com/download?ver=1&client=pc&id={id}&accesskey={ak}&fmt=krc&charset=utf8
   → data.content(base64)
```

解密:**base64 解码 → 跳过 4 字节魔数 `krc1`(hex `6B 72 63 31`)→ 与 16 字节密钥循环 XOR
→ zlib inflate → UTF-8**。密钥:

```
40 47 61 77 5E 32 74 47 51 36 31 2D CE D2 6E 69     ("@Gaw^2tGQ61-" + CE D2 + "ni")
```

无 DES、无 3DES,比 QQ QRC 简单得多。同一 `id`/`accesskey` 换 `fmt=lrc` 可取官方普通 LRC 用于对照。

### 1.3 KRC 格式与三处关键差异

```
[行起始ms,行时长ms]<词相对起始ms,词时长ms,0>字<词相对起始,词时长,0>字...
```

实测样本(孤勇者):

```
[22144,4242]<0,2902,0>都<2902,370,0>是<3272,210,0>勇<3482,220,0>敢<3702,540,0>的
```

与现有两个 decoder 相比,三处**必须单独处理**:

| 维度 | QQ QRC | 网易 YRC | **酷狗 KRC** |
|---|---|---|---|
| 词标记语法 | `(start,dur)` 圆括号二元组 | `(start,dur,0)` 圆括号三元组 | **`<start,dur,0>` 尖括号三元组** |
| 词时间基准 | 绝对毫秒 | 绝对毫秒 | **相对行首偏移** |
| 翻译位置 | 独立 `trans` 字段 | 独立 `tlyric`/`ytlrc` 字段 | **内嵌 `[language:base64]` 块** |
| 解密 | 私改 3DES + zlib | 无(明文) | XOR + zlib |

**"词时间是相对偏移"是最大的坑**:照搬 QRC/YRC 的绝对毫秒假设会得到全错的词时间。
实测首词相对偏移恒为 0(五首合计 **186/186 行**),即"行起始 == 首字起始"在 KRC 里
是格式构造保证的,不需要像 QQ 那样靠统计验证。

KRC 头部元数据齐全,且**首行带 UTF-8 BOM**:

```
[id:$00000000] [ar:] [ti:] [by:] [hash:] [al:] [sign:] [qq:] [total:] [offset:0]
```

### 1.4 精度实测

统计行起始不在 10 ms 网格上的比例:

| 歌曲 | 时间轴行数 | 非 10 ms 整倍数 | 占比 |
|---|---|---|---|
| 孤勇者 - 陈奕迅 | 54 | 22 | 41% |
| Lemon (Live) - 米津玄師 | 35 | 31 | 89% |
| 晴天 (Live) - 周杰伦 | 25 | 25 | **100%** |
| 打上花火 - Daoko、米津玄師 | 38 | 34 | 89% |
| 海阔天空 - BEYOND | 48 | 42 | 88% |

Lemon (Live) 行起始末位分布 `{0:4, 1:3, 2:3, 3:4, 4:7, 5:4, 6:1, 7:5, 8:2, 9:2}`,
0–9 均匀,是**真 1 ms 精度**而非网格假象。

**官方 LRC 是截断而非四舍五入**:孤勇者 54 行,`KRC 行起始 − LRC 时间戳` 差值分布
`{0:32, 2:1, 4:1, 6:18, 8:2}` —— 全部非负,最大 8 ms。代表行:

| KRC 行起始 | 官方 LRC / 应用当前输出 | KRC 若转三位 |
|---|---|---|
| 22144 | `[00:22.14]` | `[00:22.144]` |
| 28386 | `[00:28.38]` | `[00:28.386]` |
| 43152 | `[00:43.15]` | `[00:43.152]` |
| 51630 | `[00:51.63]` | `[00:51.630]` |

即当前实现**每行最多丢失 9 ms,且偏差单向为负**(总是偏早)。

### 1.5 `[language:]` 内嵌翻译块

KRC 正文可能包含一行 `[language:<base64 编码的 JSON>]`,解码后(Lemon Live 实测):

```json
{"content": [
  {"lyricContent": [["yo ne ","tsu ","ge n ","shi "], ...], "type": 0, "language": 0},
  {"lyricContent": [["我在黑暗之中描摹着你的背影"], ...],   "type": 1, "language": 0}
], "version": ...}
```

- `type: 0` = 罗马音,**逐词数组**(每行一个词数组,与该行词数一一对应)
- `type: 1` = 翻译,**逐行**(每行数组只有一个字符串)
- 行数与正文时间轴行数一致(实测均为 35),按**行索引**对齐,不需要时间戳吸附。

`type` 语义与应用现有 `landata` 判 `type==1` 的约定一致,迁移时可直接沿用。

## 二、酷我

### 2.1 当前实现与其精度天花板

`KuwoTagProvider.cs:46` 使用 `https://m.kuwo.cn/newh5/singles/songinfoandlrc?musicId={0}`,
读 `data.lrclist[]`,每项形如 `{"lineLyric": "词：唐恬", "time": "7.43"}`。
`time` 是**以秒为单位、两位小数的字符串**;`:464` 做 `Convert.ToInt64(double.Parse(time) * 1000.0)`,
`:558` 经 `FormatTimestamp` 输出三位毫秒 —— **第三位恒为 0**。

两首统计:孤勇者 69 行(2 位小数 ×59)、孤勇者 Live 61 行(2 位小数 ×50),厘秒末位 0–9 均匀。
少数 5–6 位小数值是**浮点表示噪声**而非额外精度(`88.229996`→88.23、`110.240005`→110.24),
现有 `Convert.ToInt64(x * 1000.0)` 对其取整正确。

其它同源端点精度相同:`www.kuwo.cn/openapi/v1/www/lyric/getlyric` 返回同一份 `lrclist`;
`songinfoandlrc` 追加 `type=lrcx` / `lrcx=1` / `httpsStatus=1&reqId=` 返回体一字不变。

### 2.2 逐字 LRCX 通道(参数加密,实测可用)

`newlyric.kuwo.cn/newlyric.lrc` **要求 query 整体加密**,明文请求返回 `TP=ERROR REQUEST`。
完整流程(四个开源实现一致,本会话独立复现成功):

**请求**

1. 明文参数:`user=12345,web,web,web&requester=localhost&req=1&rid=MUSIC_{id}`,
   逐字再追加 `&lrcx=1`
2. 与 ASCII 密钥 **`yeelion`**(7 字节)循环 XOR
3. base64 编码,整体作为 query:`http://newlyric.kuwo.cn/newlyric.lrc?{base64}`

**响应**

1. 响应体以 `tp=content\r\npath=...` 开头,定位首个 `\r\n\r\n`,取其后
2. zlib inflate
3. **非 lrcx**:直接按 **GB18030** 解码 → 普通 LRC
4. **lrcx=1**:滤掉非 ASCII 字节 → base64 解码 → **再次与 `yeelion` XOR** →
   按 **GB18030** 解码 → LRCX

⚠️ **编码是 GB18030 不是 UTF-8**,这是与其它三源全然不同的一点,按 UTF-8 解会得到乱码。

### 2.3 LRCX 格式与精度实测

```
[kuwo:034]
[ver:v1.0] [ti:孤勇者] [ar:陈奕迅] [al:孤勇者] [by:p_pttzhang] [offset:0]
[00:07.433]<2104,-2104>词：<2758,-1706>唐<3060,-892>恬
[00:08.222]<1880,-1880>曲：<1958,-1018>钱<2418,-734>雷
```

- **行时间戳本身就是标准 `[mm:ss.fff]` 三位毫秒**,无需任何换算——比 KRC/QRC/YRC 都简单。
- 词标记是 `<start,duration>` 尖括号**二元组**(也可能三元组),且**可能为负**。
  负值源于行首 `[kuwo:034]` 标签(八进制)所定义的混淆:各实现取
  `value = int(content, 8)`、`offset = value // 10`、`offset2 = value % 10` 参与还原。
  **逐行方案不需要处理**——只需用 `<-?\d+,-?\d+(?:,-?\d+)?>` 正则整体剥离词标记。
- **翻译用"重复时间戳"约定表达**。**更正(2026-08-02 复核)**:本文初稿称"同一时间戳第二次出现
  即视为上一行的译文",**归属写反了**。真实样本(Lemon `musicId=40602735`)为:

  ```
  [00:01.547]夢ならば              ← 原文
  [00:02.880]如果只是一场梦         ← 上一行(01.547)的译文
  [00:02.880]どれほどよかったでしょう ← 当前行原文
  ```

  即**同一时间点的第一条是上一行的译文,第二条才是当前行原文**;lx-music `sortLrcArr`
  取出重复组前项、重定时到再前一行的时间戳后归入译文。另外无译文时该"译文槽"是
  **纯空白行**而非缺失。应用现有 Kuwo 解析走的是另一套语义(按同时间戳配对 +
  `ContainsChinese` 启发式,`KuwoTagProvider.cs:466-480/509-512`),**并非** lx-music 算法,
  对接时需明确以哪一套为准。详见
  [`KUWO_KUGOU_HIGH_PRECISION_LYRIC_IMPLEMENTATION_DESIGN_2026-08.md`](KUWO_KUGOU_HIGH_PRECISION_LYRIC_IMPLEMENTATION_DESIGN_2026-08.md) §13.2。

精度实测(三首):

| 歌曲 | LRCX 行数 | 非 10 ms 整倍数 | 占比 | 第三位毫秒分布 |
|---|---|---|---|---|
| 孤勇者 - 陈奕迅 (198554068) | 69 | 60 | 87% | 0–9 均匀 |
| 孤勇者 (Live) - 陈奕迅 (237787110) | 61 | 53 | 87% | 0–9 均匀 |
| 孤勇者 (Live) - 张韶涵&信 (212958388) | 71 | 65 | 92% | 0–9 均匀 |

**与现有路径逐行对照**(孤勇者,两边均 69 行,一一对齐):
`LRCX − songinfoandlrc` 差值分布 `{0:9, 1:5, 2:16, 3:6, 4:6, 5:4, 6:4, 7:8, 8:6, 9:5}`
—— 全部非负、覆盖 0–9,**证实 `songinfoandlrc` 是截断**。

| LRCX | songinfoandlrc | 应用当前输出 |
|---|---|---|
| `[00:07.433]` | `7.43` | `[00:07.430]` |
| `[00:08.222]` | `8.22` | `[00:08.220]` |
| `[00:09.557]` | `9.55` | `[00:09.550]` |
| `[00:10.357]` | `10.35` | `[00:10.350]` |

同一端点**不带 `lrcx=1`** 时返回 `[00:07.43]` 两位——**第三位毫秒只存在于 lrcx 通道**。

## 三、开源实现交叉对比

| 事实点 | 本会话实测 | LDDC | lx-music-desktop | lx-music-api-server | voicefox(Rust) | 一致性 |
|---|---|---|---|---|---|---|
| 酷狗 KRC 密钥 `@Gaw^2tGQ61-\xce\xd2ni` | ✓ | ✓ `KRC_KEY` | — | — | — | **逐字节一致** |
| 酷狗跳 4 字节头 + XOR + zlib | ✓ | ✓ `encrypted[4:]` | — | — | — | 一致 |
| 酷狗词时间为**相对行首** | ✓ 186/186 行 | ✓ `line_start + word.start` | — | — | — | **一致** |
| 酷狗 `language` type 0=罗马音/1=翻译 | ✓ | ✓ 同判定 | — | — | — | **一致** |
| 酷我加密密钥 `yeelion` | ✓ 复现成功 | — | ✓ `buf_key` | ✓ `buf_key` | ✓ `kw_lyric_crypto` | **四方一致** |
| 酷我 `tp=content` + `\r\n\r\n` + zlib | ✓ | — | ✓ | ✓ | ✓ | 一致 |
| 酷我 lrcx 二次 base64+XOR | ✓ | — | ✓ | ✓ | ✓ | 一致 |
| 酷我编码 GB18030 | ✓ | — | — | ✓ | ✓ `GB18030.decode` | 一致 |
| 酷我翻译 = 重复时间戳 | ✓ | — | ✓ `sortLrcArr` | ✓ | — | 一致 |

**开源实现补出的、实测未覆盖的细节:**

1. **酷狗 `contenttype == 2` 分支**(LDDC `kg.py`):`download` 响应的 `contenttype` 为 2 时,
   `content` 是**base64 纯文本歌词而非加密 KRC**,直接 base64 解码即可,不能送去 XOR+zlib。
   本会话抽样未遇到该形态,但实现时必须处理,否则这类歌曲会解密失败。
2. **酷狗另有带签名的 `lyrics.kugou.com/v1/search`**(LDDC 使用,需 `appid=3116`、
   `clientver=11070` 与 salt `LnT6xpN3khm36zse0QzvmgTZ3waWdRSA` 的 MD5 签名)。
   本会话实测**免签名的 `lyrics.kugou.com/search` 仍可用**,应优先用简单路径,
   签名路径留作失效后的备选。
3. **酷狗罗马音按行索引对齐需跳过空行**(LDDC `krc2mdata` 的 `offset` 变量):
   正文中全空的行不出现在罗马音数组里,直接按下标取会整体错位。仅影响罗马音,不影响翻译。
4. **酷我 `[kuwo:xxx]` 八进制标签**参与词时间还原(见 §2.3),逐行方案可忽略。

## 四、四源精度对照(当前状态)

| 源 | 逐字格式 | 应用是否用了逐字 | 输出时间戳 | 第三位是否有效 |
|---|---|---|---|---|
| QQ | QRC(3DES+zlib) | 是 | `[mm:ss.fff]` | **是** |
| 网易云 | YRC(明文) | 是(按歌波动) | `[mm:ss.fff]` | **是** |
| 酷狗 | **KRC(XOR+zlib)** | **否** | `[mm:ss.ff]` | —(两位) |
| 酷我 | **LRCX(XOR+zlib+GB18030)** | **否** | `[mm:ss.ff0]` | **否(恒 0)** |

## 五、若升级:可行性与风险

**共同优势**:两者都可保留现有端点作回退,与 QQ「QRC 优先 / legacy 回退」结构一致;
两者解密都只需 XOR + `ZLibStream`,无第三方依赖。

### 酷狗

- 应用已持有官方路径所需的全部输入(`song.Hash`、`song.DurationMs`,见 `KugouTagProvider.cs:106`)。
- 风险:①词时间相对偏移(逐行方案不受影响,但若做逐字必须加 `line_start`);
  ②首行 BOM 需剥离;③歌词加载从 1 次 HTTP 变 **2 次**(search + download),
  需确认限流与取消令牌传递;④`contenttype == 2` 的纯文本分支必须处理。

### 酷我

- **改动面比酷狗小**:行时间戳已是 `[mm:ss.fff]`,解析只需"剥词标记 + 现有重复时间戳翻译逻辑",
  不需要任何时间换算;且仍是**单次 HTTP**。
- 风险:①**GB18030 解码**(net8 需 `CodePagesEncodingProvider`,主程序 `Program.Main`
  首行已注册,测试入口 `MusicTag.Tests/Program.cs:15` 同样已注册,可直接用);
  ②请求参数需实现 XOR+base64 构造;③响应需按字节处理(定位 `\r\n\r\n` 前不能先转字符串);
  ④词标记可能为负数,正则须带 `-?`。

### 共同的架构决策点

KRC 与 LRCX 会是**第三、第四种逐字格式**,在词标记语法、时间基准、翻译载体、字符编码上
两两不同。这正好命中 [`LYRIC_FETCH_CROSS_REVIEW_2026-07.md`](LYRIC_FETCH_CROSS_REVIEW_2026-07.md)
F12 所述"共享重构的唯一合理重启条件"。动手前应先决定是否抽共享层,避免四份平行实现。

### 行为变更可见性

两个源的歌词时间戳都会变化(酷狗两位→三位且整体后移 0–9 ms;酷我第三位从恒 0 变为真值)。
属精度修正,须按仓库惯例显式标注,不能伪装成等价重构。

## 六、验证缺口

- 本文全部实测数据为 2026-08-02 真实抓取;**未运行仓库测试,不构成任何构建验证**。
- 抽样集中于华语与日语流行曲(酷狗 5 首、酷我 3 首),未覆盖纯音乐、无歌词、
  超长音轨、无逐字仅普通歌词等边界形态。
- 酷狗 `contenttype == 2`、酷我 `[kuwo:]` 词时间还原两条来自开源实现阅读,**未经实测复现**。
- 酷我三首抽样均为同一首歌的不同版本,曲库覆盖面偏窄。
- 两个通道的风控与稳定性未做长期观察;酷我 `songinfoandlrc` 已观察到瞬时故障
  (同一 ID 首次 `status:301 音乐查询失败`,同参数重试成功)。

## 附:本轮抓取的样本

留存于 `.claude/tmp/lyric-spotcheck/`(不入库),可直接转为 characterization fixture:

- `kugou_download_krc.json` / `kugou_decrypted.krc` —— KRC 密文与解密结果(孤勇者)
- `kugou_download_lrc.json` / `kugou_official.lrc` —— 同一 id 的官方厘秒 LRC(截断对照)
- `kugou_get_krc.json` —— 应用当前端点的返回
- `kugou_lemon.krc` / `kugou_lemon_language.json` —— 含 `[language:]` 块的样本
- `kuwo_198554068.json` / `kuwo_237787110.json` —— 酷我 `songinfoandlrc` 厘秒 `lrclist`
- `kuwo_198554068_lrcx.txt` / `kuwo_237787110_lrcx.txt` / `kuwo_212958388_lrcx.txt` —— 酷我逐字 LRCX
- `kuwo_198554068_plain.lrc` —— 酷我 newlyric 非 lrcx 通道(两位厘秒对照)
