# 手动歌曲 ID 直取(自定义 ID 搜索)可行性与设计

> 状态:**设计/讨论稿,未实现**。后端可行性已按源逐一核实(端点 + 方法签名,见下)。
> **UI 部分为占位草案,标注「待后续修改(TBD)」——需重新设计后再定稿。**
> 起因:QQ 关键词搜索恒被限流(`req_0.code == 2001`),用户希望「直接输入歌曲 ID → 拉元数据/歌词」以绕过搜索这一步。

## 1. 现状:四源都是「搜索 → 拿 id → 按 id 取歌词/封面/(部分)详情 → 写回」

用户的理解正确。通用流程:

1. 关键词搜索 → 候选列表(每条带各源自己的标识符);
2. 选中候选后,按标识符取歌词 / 封面 /(部分源)完整详情;
3. 写回标签。

关键差异在于**「按 id 直取完整详情」的支持度**——这决定了「手动 ID 直取」模块每个源要写多少新代码。

## 2. 各源按 ID 直取能力矩阵(全部据现码核实)

| 源(ordinal) | 标识符 | 按 id 取**完整元数据** | 按 id 取**歌词** | 封面 | 受搜索限流? |
|---|---|---|---|---|---|
| **网易云(0)** | 数字 `songId` | ✅ **已内建**:`LoadSongDetails(long)` → `music.163.com/weapi/v3/song/detail` | ✅ 按 songId | ✅ | 详情/歌词端点**不吃**搜索限流 |
| **QQ(1)** | `songmid`(字符串) | ❌ **缺**独立 detail 调用(现只能靠限流搜索) | ✅ 按 songmid → `c.y.qq.com/.../fcg_query_lyric_new.fcg?songmid=`(**独立端点,不吃 2001**) | ✅ 按 albummid 模板 | 搜索(`u.y.qq.com/.../musicu.fcg`)恒 2001 |
| **酷狗(3)** | `hash`(MD5 式) | ❌ 只有搜索返回 | ⚠️ 按 `hash` + 时长 → `m3ws.kugou.com/api/v1/krc/get_krc?hash=&timelength=` | ❌ 无封面 | 搜索 |
| **酷我(9)** | 数字 `musicId` | ✅ 有 `m.kuwo.cn/newh5/singles/songinfoandlrc?musicId=`(**详情 + 歌词合一**,现仅用于按 track 取词) | ✅ 同一端点 | ✅ | 详情端点**不吃**搜索限流 |

### 关键代码锚点(核实来源)
- 网易云 `NetEaseMusicTagProvider`(`MusicTagWinApp.Exporters`):`songDetailsEndpoint` 常量;`LoadSongDetails(long songId)`;`SearchTracks`/`SearchLyrics` 已有 `(knownSongId > 0L) ? LoadSongDetails(knownSongId) : SearchSongs(...)` 分支——**按 id 直取的通路已经存在**,由 `knownSongId` 门控驱动。
- QQ `QqMusicTagProvider`(`MusicTagWinApp.Writers`):`SearchTracks`/`SearchLyrics`/`SearchCovers` 全经 `SearchSongs`(限流);`LoadLyricsForTrack(track)` 用已知 track 的 `QqMusicMid`(songmid)重建**最小** `QqSongInfo` 只为取歌词,**不取元数据**;`BuildTrackResult` 存 `SourceTrackId`=数字 songid、`QqMusicMid`=songmid、封面按 `Album.Mid`。
- 酷我 `KuwoTagProvider`(`MusicTagWinApp.Adapter`):`LoadLyricForTrack` 走 `songinfoandlrc?musicId=`(详情+歌词合一)。
- 酷狗 `KugouTagProvider`(`MusicTag.Candidates`):歌词 `get_krc?keyword=&hash=&timelength=`;无封面(与 CLAUDE.md「Kugou = 歌词+曲目,无封面」一致)。

## 3. 可行性结论(按落地成本分级)

- **网易云 / 酷我 —— 低成本、体验好、天生绕限流。** 二者本就有「数字 id → 详情」端点(网易云 `LoadSongDetails` 已在用,酷我 `songinfoandlrc`)。做「输入数字 id 直取」基本是**复用现成方法 + 加一个入口**。id 是纯数字,用户从分享链接易得。
- **QQ —— 用户的痛点,能绕限流,但有两个前提。**
  - 只要**歌词**:今天就能绕过搜索——歌词走独立端点(不吃 2001),只要有 `songmid` 即可直接取词(`LoadLyricsForTrack` 的取词路径已具备)。
  - 要**完整元数据**(标题/艺术家/专辑/年份/音轨号):目前**没有** detail 调用,需**新增**一个(QQ `musicu.fcg` 的 song-detail 模块,或旧接口 `fcg_play_single_song.fcg?songmid=`)。工作量中等。真正的摩擦不是技术,而是 **`songmid` 是字符串**(如 `003aAYrm3GE0Ac`),用户须从分享链接 `y.qq.com/n/ryqq/songDetail/{songmid}` 抠出,不如数字 id 友好。
- **酷狗 —— 可行性最低、价值最低。** 标识是 `hash`(用户几乎不可能手输),歌词还需额外提供时长(`timelength`),且无封面。**建议不做或最后做。**

## 4. 后端设计(可实现部分)

新增一个「按 ID 取候选」的入口,产出与现有搜索**同构**的 `TrackSearchResult`/`LyricSearchResult`/`CoverSearchResult`,直接喂给**既有的候选列表 → 写回管线**(不碰写回逻辑)。按源分派:

- **网易云**:把用户输入的数字 id 当作 `knownSongId` 走现有 `LoadSongDetails` 通路(几乎零新代码)。
- **酷我**:调 `songinfoandlrc?musicId=` 取详情 + 歌词。
- **QQ**:**新增** song-detail 调用(按 songmid 取完整 `QqSongInfo`),歌词/封面复用现有 by-songmid / by-albummid 路径。
- **酷狗**(可选):按 hash + 时长取歌词;无封面;元数据缺失需容忍。

**不变量 / 禁区**(沿用现有搜索子系统约定):
- 复用 `SearchProviderFactory` + 能力接口的既有分派风格;`SearchSource` 显式序数(163=0/QQ=1/Kugou=3/Kuwo=9)不改。
- **韧性铁律**:单源失败 / JSON 畸形必须「记录并跳过」,不得中断整条候选链;HTTP-200 但不可解析回填 `ParseFailed`。
- `SourceItem` 持久化 JSON 键、`.resx` 键、`[JsonProperty]` 名保持稳定。

## 5. UI 设计(⚠️ 待后续修改 — TBD)

> 以下仅为占位草案,**需重新设计后定稿**。留待后续与用户确认交互与放置位置。

- **入口位置**:TBD(候选搜索对话框内新增分页 / 按钮?独立小对话框?主菜单项?)。
- **控件草案**:源下拉(网易云/QQ/酷我/(酷狗))+ ID/mid 粘贴框 + 「获取」按钮 → 直接构造一条候选并加入现有候选列表。
- **ID 提示**:按源提示标识符类型与获取方式(网易云/酷我数字 id;QQ songmid;酷狗 hash)——文案 TBD。
- **本地化**:新资源键在预编译卫星 DLL 中缺席 → 非中文界面会回退中文文案(与近期新增键同一已知限制);若要多语,需另行处理卫星资源。
- **错误呈现**:复用现有搜索状态指示器(`SearchStatusIndicator`)/ `HttpResult` 错误模型?TBD。

## 6. 渐进落地建议

1. **先做网易云 + 酷我的「数字 ID 直取」**(成本最低、id 友好、天然绕限流),打通「ID → 候选 → 写回」端到端 + 后端特征测试。
2. **再给 QQ 加 song-detail 调用**(解决其搜索限流的核心诉求;接受 songmid 的 UX 摩擦)。
3. **酷狗暂缓**(hash UX 不值)。
4. UI 每步按第 5 节重新设计后再接入。

## 7. 验证方式(实现时)

- 后端:沿用 `src/MusicTag.Tests` 特征测试模式——override provider 的 `protected virtual` HTTP 方法注入 fixture,断言「给定 id → 解析出的 `*SearchResult` 字段」;QQ 新 detail 调用需先加金样例锁定解析。
- 集成:`.\scripts\Verify-Build.ps1 -RunSmokeTests`(构建 + 特征 + 3 冒烟)。
- 联网真实拉取为手动验证(无联网自动化 harness)。
