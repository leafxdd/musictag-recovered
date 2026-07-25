# 联网歌词获取交叉审查(网易云 + QQ 源)

- 日期:2026-07-23
- 分支:`develop-net8`(HEAD `f4c050f`)
- 范围:仅网易云与 QQ 两个歌词源的联网获取、解密、解析、转换、翻译对齐全链路
- 方法:本仓库代码全链路梳理 + 联网深读四个开源参照项目源码交叉对比
  - chenmozhijin/**LDDC**(Python,GPL-3.0,commit `84631e8`)
  - jitwxs/**163MusicLyrics / MusicLyricApp**(C#,本项目 QQ DES 解码同源,master `26d8a73`)
  - WXRIW/**Lyricify-Lyrics-Helper**(C#,commit `983709b`)
  - Binaryify/**NeteaseCloudMusicApi**(接口参数语义)
  - 另参考 lx-music-api-server 与既有内部文档 [`QRC_PRECISION_REVIEW_2026-07.md`](QRC_PRECISION_REVIEW_2026-07.md)、[`NETEASE_YRC_LYRIC_DESIGN_2026-07.md`](NETEASE_YRC_LYRIC_DESIGN_2026-07.md)
- 置信度标注:【核验】= 本会话亲自读本仓库代码确认;【参照】= 深读外部项目源码所得;【存疑】= 无法离线证实,须以真实响应为准

## 状态更新(2026-07-25)

- F1/F3/F4/F5/F6/F7/F9 已由七个提交修复(`839496c`、`0ee3707`、`bc27c21`、`3806a13`、`dce650b`、`24cfc7a`、`0717576`),修复内容与验证记录见 [`LYRIC_FETCH_REPAIR_REPORT_2026-07.md`](LYRIC_FETCH_REPAIR_REPORT_2026-07.md);本报告审查方复核了全部七个 diff 并复跑 characterization(918 passed, 0 failed)。
- 2026-07-25 真实数据抽查 4/4 通过:孤勇者 1901371647(yrc 含两条 `t=0` JSON 制作行,正确丢弃、三位毫秒)、打上花火 496869422(无 yrc,lrc+tlyric 逐字节透传)、Windy Hill 493735862(真实无词响应形态为 `uncollected:true` + `needDesc`/`briefDesc`,无 `nolyric` 字段,正确判空)、QQ Lemon 213086592(当日密文解密+翻译对齐正常)。
- **F2 首个实证信号**:起风了 1330348068 的 `yrc` 在 2026-07-22 实测存在、07-25 从明文 v1 端点消失(补 `X-Real-IP`、参数改 `-1` 均无效),而孤勇者/打上花火的 yrc/翻译字段正常——明文通道逐字覆盖按歌波动,`lrc` 回退兜底,无用户可见故障。持续观察,触发 §四 F2 所列信号再启动 eapi 迁移。
- 真实 yrc 中版权/制作信息存在第二形态:普通时间轴行(如孤勇者 `[1,4890]词版权管理方:…`),结构上与歌词行无异,按现行规则保留,与 LDDC/Lyricify 行为一致,不视为缺陷。

## 一、总体结论

两源现均为"逐字歌词优先(网易 YRC / QQ QRC)、普通 LRC 回退"的结构,端点选择、解密算法、格式解析与四个参照项目**核心一致或更优**(QQ 用了比 163MusicLyrics 更新的 `musicu.fcg` 通道;网易翻译对齐比 Lyricify 的"逐字模式不支持翻译"更完整)。**未发现会导致歌词整体错误或获取失败的缺陷**。

发现 1 个应当修复的行为问题(P1:YRC 制作信息行被时间戳化,与所有参照项目做法相悖且会在翻译对齐时错乱合并)、5 个建议加固项(P2)、6 个低风险记录项(P3),详见第四节。

## 二、当前实现速览(经代码核验)

| 维度 | 网易云 | QQ |
|---|---|---|
| 歌词端点 | 明文 GET `music.163.com/api/song/lyric/v1?id={0}&cp=false&lv=0&kv=0&tv=0&rv=0&yv=0&ytv=0&yrv=0`(`NetEaseMusicTagProvider.cs:30`) | 主:POST `u.y.qq.com/cgi-bin/musicu.fcg` module `music.musichallSong.PlayLyricInfo` / `GetPlayLyricInfo`,`crypt:1,qrc:1,trans:1,roma:0`(`QqMusicTagProvider.cs:51,303-346`);回退:`fcg_query_lyric_new.fcg` jsonp Base64(`:49`) |
| 解密 | 无(明文 JSON) | hex → QQ 私改 3DES(key `!@#)(*$%123ZXC!@!@#)(NHL`)→ zlib(`QqQrcDecoder.cs:28`) |
| 逐字词标记 | 三元组 `(start,dur,0)`(`NetEaseYrcDecoder.cs:17`) | 二元组 `(start,dur)`(`QqQrcDecoder.cs:19`) |
| 行时间策略 | 行起始 `[start,dur]` 的 start | 同左;行起始解析失败即丢行,无首词兜底(`QqQrcDecoder.cs:107-110`) |
| 翻译 | ytlrc(逐字翻译)优先 → tlyric,`AlignAndSplitTranslatedLyric` 对齐后**严格校验**:译文每个时间戳必须存在于原文集合,否则整体回退(`NetEaseMusicTagProvider.cs:687-713,728-758`) | trans 各自转换后邻近吸附(<1000ms)对齐,无严格校验(`QqMusicTagProvider.cs:389-394`) |
| 精度 | 默认 3 位毫秒;`LyricDownload_ReformatTimetag` 开 → 10ms 四舍五入(`LyricTextProcessor.cs:254-270`) | 同左 |
| 回退链 | yrc 转换失败 → lrc/tlyric 原路径 | QRC 失败/为空 → legacy Base64 端点(`QqMusicTagProvider.cs:288-300`) |

## 三、与参照项目一致、无需改动的方面

1. **QQ QRC 解密全流程逐步一致**:hex 解码 → 24 字节 key 切三段 D-E-D 顺序 ECB 逐 8 字节块 → zlib inflate → UTF-8,与 LDDC(`core/decryptor/tripledes.py`)、163MusicLyrics(`QQMusicearchUtils.cs`)、Lyricify(`Decrypter/Qrc/Decrypter.cs`)三方逐字节同源。`LooksEncryptedLyrics` 的长度/hex 守卫(`QqQrcDecoder.cs:58`)还优于部分参照(防块不对齐越界)。
2. **QQ 端点选择处于第一梯队**:LDDC 同用 `musicu.fcg` GetPlayLyricInfo(param 细节略有差异:它带 `ct:19,cv:2111,interval,roma:1`);163MusicLyrics 还停在旧 `lyric_download.fcg`。legacy jsonp 回退端点与各家一致。响应 hex(非 base64)的处理正确。
3. **网易 v1 端点与参数拼全**:`lv,kv,tv,rv,yv,ytv,yrv` 七参数齐(漏 yv 拿不到逐字,Lyricify/NeteaseCloudMusicApi 印证参数↔响应字段一一对应:lv→lrc、kv→klyric、tv→tlyric、rv→romalrc、yv→yrc、ytv→ytlrc、yrv→yromalrc)。老端点加 `yv` 无效这一点内部实测与外部文档双重印证。
4. **YRC/QRC 词标记元数区分正确**:网易三元组、QQ 二元组,两个解码器各用各的正则,未犯"拿 QQ 正则解网易"的典型错。
5. **行时间取行起始是安全的**:LDDC/Lyricify 取首词起始,163MusicLyrics 与本项目取行起始;内部实测(两平台合计 241/241 行"行起始 == 首字起始")证明两派在真实数据上等价。
6. **翻译按时间戳对齐**(而非行号硬配)与全部参照一致;QQ 译文 trans 实为 2 位普通 LRC(非 QRC XML)已被 fixture 锁定(`ProviderLyricCharacterization.cs:278`,含 `//` 占位与 `[kana:]` 头)。
7. **3 位毫秒默认 + `ParseMillisecondPart` 补位往返无损**(`LyricTextProcessor.cs:244-252`),优于 QQ 官方 LRC 端点的 2 位截断。

## 四、发现(按严重度)

### P1 — 建议修复

**F1|网易 YRC 的 JSON 制作信息行被转成带时间戳的歌词行,与全部参照相悖,且在翻译对齐路径会错乱合并。【核验】**

- 现状:`NetEaseYrcDecoder.cs:39-42` 把 `{"t":ms,"c":[{"tx":..}]}` 行输出为 `[mm:ss.fff]作词: X` 正文行。
- 参照做法:LDDC 直接丢弃(`core/parser/yrc.py` 非 `[数字,数字]` 行 continue);Lyricify 解析为独立"信息行"类型不混入正文(`Parsers/YrcParser.cs` CreditsInfo);无一家把制作信息作为带时间戳歌词输出。本项目 QQ 侧对应物(头部 `[ti:]` 等)也输出为**无时间戳**元数据标签(`QqQrcDecoder.cs:98-102`)——两源行为自相矛盾。
- 具体危害:多条制作行常共享 `t=0`;含翻译的歌曲走 `TryAlignTranslatedLyric` 时,`LyricTextProcessor.ParseLine` 对重复时间戳的合并逻辑(`LyricTextProcessor.cs:186-202`)会把后一条制作行当作前一条的"译文"挂载、再来一条则把旧"译文"折进原文,产生"作词: X 作曲: Y"式的错乱首行;播放器侧也会在 0:00 堆叠显示。
- 修复方向:与 QQ 侧对齐——转成无时间戳行(如丢弃或输出 `[by:]` 风格标签)。注意 `ProviderDecodersCharacterization.cs:109` 已锁定现行为,改动属显式行为修正,需同步改 fixture 并按仓库惯例标注。

### P2 — 建议加固/观察

**F2|网易明文 `/api` 通道是参照项目中的孤例,存在单点风控风险。【参照+核验】** 四个参照全部走加密通道(163MusicLyrics=weapi、Lyricify/LDDC/lx-music=eapi `interface3`),仅本项目用明文 GET 主域名 + 伪造 `X-Real-IP`(`NetEaseMusicTagProvider.cs:49-67,809`)。2026-07-22 实测可用且实现最简,但若网易收紧明文接口将整体失效。建议:保留现状,但在文档中记录 eapi(AES-128-ECB,key `e82ckenh8dichen8`,LDDC `core/decryptor/eapi.py`)为备用迁移路径,出现批量 4xx/风控时再切。

**F3|网易严格对齐"一票否决"可能整份弃用合法 ytlrc。【核验】** `NetEaseMusicTagProvider.cs:741-746`:译文任一时间戳不在原文非空行集合即整体判失败退到 2 位 tlyric。ytlrc 若比 yrc 多一条尾注/信息行即触发。保守方向正确(避免错贴),但可考虑放宽为"未匹配行丢弃、其余保留"或至少统计告警。参照系:Lyricify 逐字模式干脆不支持翻译,LDDC 允许部分匹配——本项目居中,属设计选择,但一票否决的粒度偏粗。

**F4|DES 正确性无独立已知答案向量。【核验】** 测试 fixture 用同一 `QqDesHelper` 自加密再解密(`ProviderLyricCharacterization.cs:111-155`),QQ 私改 S-box(sbox2 第 2 行、sbox4 第 3 行)写错也能 round-trip 通过。目前正确性仅靠 2026-07-19 一次联网实测背书。建议:硬编码一小段真实 QQ 密文→已知明文向量作回归锚。另注:LDDC 上游同源实现的 sbox4 也带一处历史笔误仍能解——说明该路径对个别表项不敏感,更需要真实向量兜底。

**F5|QQ 歌词端点无限流重试。【核验】** 搜索有 code 2001 六次退避重试(`QqMusicTagProvider.cs:99-146`),歌词 POST 无(`:280`);被限流时静默回退 legacy 或返回无歌词,用户无感知差异。建议至少复用一次短退避,或把限流态透传到 UI。

**F6|QRC 行起始损坏无首词兜底(内部审查已建议、未实施)。【核验】** `QqQrcDecoder.cs:107-110` 行起始解析失败即整行丢弃。`QRC_PRECISION_REVIEW_2026-07.md` §四建议"行起始为主、首词起始为兜底";LDDC/Lyricify 的首词起始策略天然免疫此问题。对现有真实数据零行为变化,属低风险防御性加固。

### P3 — 低风险,记录在案

- **F7**|QRC 两个行时间戳之间的中段元数据标签(如变调歌曲混入的 `[kana:]`)不会被丢弃,而是作为字面文本**粘进上一行歌词**(`QqQrcDecoder.cs:112-115` 切片包含区间内全部内容)。真实 QRC 元数据几乎都在头部,实害有限。【核验;修正初稿"被丢弃"的说法】
- **F8**|头部元数据白名单硬编码 `ti/ar/al/by/offset`(`QqQrcDecoder.cs:21`),`[kana:]/[ly:]/[mu:]` 等即使在头部也不保留;而 163MusicLyrics 反其道把 `[offset:0]`/`[kana:` 之前的头部全部清空。各家分歧大,无公认正解。【参照】
- **F9**|网易 `nolyric`/`uncollected` 字段未显式判断,纯音乐靠空 lrc 兜底;"纯音乐,请欣赏。"文本会作为歌词写入。163MusicLyrics 同样不判(模型有字段但消费端不读),LDDC 亦无特判——与主流一致,但若想过滤占位文本需显式判 `nolyric`。【参照】
- **F10**|`romalrc/yromalrc/klyric`(网易)与 `roma`(QQ,请求即置 0)全链路忽略。klyric 为过时字段各家共识不用,罗马音是功能取舍非缺陷。【核验+参照】
- **F11**|2 位模式四舍五入 vs QQ 官方/163MusicLyrics 默认截断:同一 15ms 输出 `.02` vs `.01`。已知设计选择(`LyricTextProcessor.cs:263-269` 注释明示),与外部工具对比会有 ±10ms 差异。【核验】
- **F12**|`QqQrcDecoder` 与 `NetEaseYrcDecoder` 是两份平行实现,`NETEASE_YRC_LYRIC_DESIGN_2026-07.md` §六.4 建议的共享 helper 未做,文档预警的"漂移"已发生(元数据处理策略已不一致,即 F1)。若修 F1,顺手评估抽共享是收敛点。【核验】

## 五、测试缺口

1. F1 相关:多条同 `t` JSON 制作行 + 含翻译对齐的组合 fixture(现有 `ProviderDecodersCharacterization.cs:109` 只锁单条转换)。
2. F4 相关:真实 QQ 密文→明文的 DES 已知答案向量(替代自加密自解密)。
3. F6 相关:行起始损坏 + 字标记有效的合成 QRC(`QRC_PRECISION_REVIEW` §四清单第 2 条,至今未补)。
4. 纯音乐:网易 `nolyric:true` 响应与 QQ 占位符哨兵均无专门测试。
5. QRC XML `LyricContent` 属性含内嵌换行的 fixture(XML 属性规范化会折行为空格,现实现按正则切片不受影响,但无测试锁定)。

## 六、建议行动排序

1. 修 F1(显式行为修正 + fixture 更新)——唯一会产生用户可见错乱输出的问题。
2. 补 F4 真实 DES 向量——一次性成本极低,消除最大的"测不出"盲区。
3. 顺手做 F6 最小兜底(内部文档早有方案,零行为变化)。
4. F3 放宽策略与 F5 歌词限流重试按需求优先级排队;F2 仅记录监控信号,不动代码。
