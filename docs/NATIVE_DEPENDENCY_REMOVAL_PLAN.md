# 去除原生 `MusicTag.dll` 依赖 — 实施规划

> 目标:把项目对**原版二进制 `MusicTag.dll`** 的全部依赖替换为可维护的托管代码,最终**删除该 DLL**,
> 从而根除当前为标签读写打的反篡改补丁(见 `DECOMPILATION_NOTES.md` 的“反篡改自校验门”一节)。
> 本文是路线图与事实底稿,不是已完成的改动。

## 1. 为什么做这件事

`MusicTag.dll` 是反编译恢复项目里**唯一无法从源码重建**、且带反篡改自校验的原生制品。它对宿主 EXE 做
CRC-16 校验,只有原版 EXE 放行标签读写;重编译的 EXE 必须给 DLL 打 3 字节补丁才能读写标签。补丁本身可用、
有文档,但属于长期维护负担(每次重新提取/替换 DLL 都要重打)。彻底替换掉这个 DLL 即可不再需要补丁。

## 2. 现状:对原版二进制的全部依赖

| 制品 | 用途 | 是否需替换 |
|---|---|---|
| **`MusicTag.dll`(原生)** | ① 标签读写(被反篡改门 gate,补丁对象)② 在线搜索的 URL/头常量 + 网易云加密 + 编码检测 | **是**(本规划核心) |
| `MediaInfo.dll` | `MusicTag.dll` 内部依赖(C# 不直接调) | 随 ① 一起去留 |
| 卫星资源 DLL(en/zh-CHS/zh-CHT) | 预编译本地化(源码无分语言 resx) | 独立低优先,本规划不含 |
| `SQLite.Interop.dll` / System.Data.SQLite | 标签历史库 | 否(标准开源件) |
| `MusicTag.db` / `MusicTag.dat` | 数据/设置文件 | 否(数据,非代码依赖) |
| user32/shell32/uxtheme P/Invoke | 标准 Win32 | 否(不是原版依赖) |

## 3. 关键事实(已查证)

### 3.1 反篡改门只拦标签 I/O(实测)
在**未打补丁**的原始 DLL 上实测:在线导出全部正常返回字符串,只有标签读取(`ee`)返回空。
→ **只要标签 I/O 不再走 native,就能移除补丁**;在线导出本就不受门限制。

### 3.2 标签 I/O 的封装层(`MusicTag.States/ConfigDescriptorState.cs`)
`internal IDisposable`,包住一个 native tag handle;所有字段值缓存在 `Dictionary<string,object>`,经 `this[string]` 索引器读写。
对外能力面(替换时必须等价实现):

| 方法 | 作用 | TagLibSharp 对应 |
|---|---|---|
| `ctor(string path)` | 打开文件,解析 open 协议为 `loadError` | `TagLib.File.Create(path)` + 异常映射 |
| `LoadBasicTagFields` | 读 13 个文本/数字字段 + `tagtypes`(f)+`fileext`(p) | `File.Tag.*` + `File.TagTypes` + `File.MimeType` |
| `LoadRawTextFieldData` | 读 10 个字段的**原始帧字节**(d)+ 声明编码 | ID3v2 `TextInformationFrame` 原始字节 / APE / Xiph(**硬骨头**,见 5) |
| `LoadLyrics` | 读 `lyrics`(USLT) | `Tag.Lyrics` |
| `LoadAudioProperties` | h/i/j/k/l/o → 位深/声道/采样率/码率/时长/有无视频轨 | `File.Properties.*` |
| `LoadPictureSummary(flagOnly)` | 主封面(g):有无 / 字节 | `Tag.Pictures[0]` |
| `LoadAllPictures` | 全部封面(gg)→ `List<PictureData>` | `Tag.Pictures` |
| `SaveTagFields` | 写 12 字段(m0)+ 清/加封面(m1/m2)+ 保存(m3) | 改 `Tag.*` + `Tag.Pictures` 后 `File.Save()` |
| `SaveCurrentTagFile` | 不改字段、按配置版本重存(q) | `File.Save()`(撤销流程用) |
| `GetGenreNameByIndex`(cc) | ID3v1 数字流派→名 | `TagLib.Genres.Audio[]` |
| `PictureTypeNames`(ggg) / `SupportedPictureMimeTypes` | 封面类型名列表 / 支持的 MIME | `TagLib.PictureType` ↔ 名;`{jpeg,png,gif}` |
| `ReadAndFreeNativeString` / `bb/zzz/zz1/dd` + handle | 非托管内存/句柄管理 | **全部消失**(托管 GC) |
| `NativeFileExists`(663-667,无 EntryPoint) | 路径是否存在 | `System.IO.File.Exists` |

**字段词表**(传给 ee/d/m0 的 key):`title artist album year track disc trackstr discstr genre albumartist composer lyricist comment` + `lyrics`。
**写入顺序**(m0 的 `string[12]`,固定契约,与读顺序不同):
`[0]title [1]artist [2]album [3]year [4]trackstr [5]discstr [6]genre [7]albumartist [8]composer [9]comment [10]lyricist [11]lyrics`。
**ID3v2 版本**:`Settings.Default.ID3v2Version`,`3`=ID3v2.3 / `4`=ID3v2.4(OptionsDialog 单选)→ TagLib `Id3v2.Tag.DefaultVersion = 3|4` + `ForceDefaultVersion = true`。

### 3.3 在线 native = 字符串常量 + 4 个函数(DLL 不发 HTTP,HTTP 已全在托管层 `RemoteTagProviderBase`)
- **QQ / 酷我 / 酷狗**:纯常量,拿到字符串即**零算法**。
- **网易云**:仅 `rc2`(weapi 加密)、`rc4`/`rc3`(“163 key”评论编/解码,互逆对)、`de`(编码检测)。

### 3.4 在线常量全集(已 dump,明文)

| 源 | EP | 值 / 模板 |
|---|---|---|
| 网易 | na | `http://music.163.com/weapi/cloudsearch/pc`(POST 搜索) |
| 网易 | nb | `params={0}&encSecKey={1}`(POST body 格式;`{0}`=rc2 的 `a`,`{1}`=`b`) |
| 网易 | nc | `http://music.163.com/api/song/lyric?os=pc&id={0}&lv=-1&kv=-1&tv=-1`(GET 歌词,**不加密**) |
| 网易 | naa | `http://music.163.com/weapi/v3/song/detail`(POST 详情) |
| 网易 | nab | `http://music.163.com/weapi/v1/album/{0}`(POST 专辑) |
| 网易 | rc1 / rc | 头名 `X-Real-IP` / 头值 = **运行时随机** `112.88.x.x`(非常量,需重写随机逻辑) |
| QQ | nd | `https://u.y.qq.com/cgi-bin/musicu.fcg`(POST) |
| QQ | ndd | `{{"{0}":{{"method":"DoSearchForQQMusicDesktop","module":"music.search.SearchCgiService","param":{{"search_type":0,"query":"{1}","page_num":1,"num_per_page":{2}}}}}}}` |
| QQ | ne | `https://c.y.qq.com/lyric/fcgi-bin/fcg_query_lyric_new.fcg?songmid={0}&g_tk=5381&jsonpCallback={1}&format=jsonp` |
| QQ | nf | `https://y.qq.com/music/photo_new/T002R800x800M000{0}.jpg` |
| 酷我 | pa | `https://search.kuwo.cn/r.s?all={0}&client=kt&pn=0&rn={1}&ver=kwplayer_ar_9.2.3.2&vipver=1&show_copyright_off=1&newver=1&correct=1&ft=music&cluster=0&strategy=2012&encoding=utf8&rformat=json&vermerge=1&mobi=1&issubtitle=1` |
| 酷我 | pb | `https://m.kuwo.cn/newh5/singles/songinfoandlrc?musicId={0}` |
| 酷狗 | nh | `http://mobilecdn.kugou.com/api/v3/search/song?format=json&keyword={0}&page=1&pagesize={1}&showtype=1` |
| 酷狗 | ni | `https://m3ws.kugou.com/api/v1/krc/get_krc?keyword={0}&hash={1}&timelength={2}` |

### 3.5 网易云三算法(替换时唯一的真算法,均为公开方案)
- **rc2 = weapi**:输入 JSON,输出 `{a,b}`(字面字段名 `a`/`b` = params/encSecKey)。AES-128-CBC 双层(固定 key1 `0CoJUm6Qyw8W8jud`、IV `0102030405060708` + 随机 16 字节二层 key)+ RSA 无填充(公开公钥模数)生成 encSecKey。**仅** 搜索/歌曲详情/专辑详情三个 POST 用;三处输入 JObject 已记录在案。*实施时以社区参考实现核对常量。*
- **rc4 / rc3 = “163 key” 编/解码**:AES-ECB + base64 + `163 key(Don't modify):` 前缀,互为逆。rc4 把 `music:{...}` 明文编码写入 comment(受 `CommentTagWrite163Key` 开关控制);rc3 从文件 comment 解码回明文用于预填搜索。
- **de = 编码检测**:输入文件前 ≤2000 字节,输出 .NET 编码名;唯一消费者是导入 `.lrc`(`LyricEditorDialog.ImportLrcText`)。结果 `ISO8859-1`/空 视为“未检测”→ 回退 `Encoding.Default`。可用托管 charset 检测库(UTF.Unknown / Ude.NetStandard)替代,保留该回退。

### 3.6 格式覆盖约束(阶段 A 的功能边界)— 已实测(TagLibSharp 2.3.0)
应用支持的扩展名:`.aac .aiff/.aif/.aifc .ape .dff .dsf .flac .mpc .mp3 .mp4/.m4a .ogg .opus .tak .wav .wma .wv`。
**阶段 0 实测**(反射枚举 `FileTypes.AvailableTypes` + `SupportedMimeType` 特性,`artifacts/tlspoc/`):
TagLibSharp 2.3.0(NuGet,引用 `lib/net462/TagLibSharp.dll`)支持其中 **15 个**——含 `.dsf`(DSD)与 `.aac`(此前误判为不支持);
**仅 3 个边缘容器不支持**:`.dff`(DSDIFF,主流 DSD 容器 `.dsf` 已支持)、`.tak`、`.aifc`(压缩 AIFF,`.aif`/`.aiff` 已支持)。缺口比原预判小得多(见 §9 决策点)。

## 4. 总体策略与阶段划分

```
阶段 0  前置 PoC + 黄金对拍基线        (不改业务代码)
   │
阶段 A  标签 I/O → TagLibSharp         → 完成后【移除补丁】,native DLL 仅剩在线用途
   │
阶段 B  在线去 native(常量+3算法+检测) → 完成后【删除 MusicTag.dll / MediaInfo.dll】
```
两阶段彼此独立、可分别回退。**阶段 A 即可解决补丁痛点**;阶段 B 才彻底去掉 DLL。

## 5. 阶段 0 — 前置验证与基线(无业务改动)
1. **TagLibSharp 格式 PoC**:NuGet 引入 `TagLibSharp`,对每个支持扩展名各取样本,读关键字段+音频属性+封面,确认覆盖;**重点确认 DSD/TAK/裸 aac** 行为(支持/抛异常/读空)。
2. **黄金对拍基线**:用当前(已补丁)native DLL 把 `docs/测试歌曲/` 等样本的全部字段/音频属性/封面/tagtypes dump 成 JSON 基线 —— 阶段 A 用它逐文件比对(无单测项目,这是主要回归手段)。
3. 选定 charset 检测库(de 替代候选),小样本验证对 `.lrc` 的判定。

## 6. 阶段 A — 标签 I/O 迁移到 TagLibSharp(完成即移除补丁)
**做法**:保持 `ConfigDescriptorState` 的**公共 API 与字段词表/缓存键不变**(StateFieldInstance 有 ~30 处调用点,改动面要锁在这一个类内),把内部实现从 P/Invoke 换成 `TagLib.File`。这样调用方零改动、可对拍。

要点:
- 打开/异常:`TagLib.File.Create` + 把 `UnsupportedFormatException`/`CorruptFileException`/`FileNotFoundException` 映射回原 `loadError` 文案(`Msg_InvalidFile*`/`Msg_FileNotFound`/`Msg_OpenFileFail`)。
- 读字段/音频属性/封面:按 §3.2 映射表实现;保持 `track/disc` 解析、`trackstr/discstr` 字符串形态、numeric genre 解码(`TagLib.Genres`)。
- 写:按固定 `string[12]` 顺序映射回 `Tag.*`;封面 `Clear` + 逐张 `Picture`(类型名 ↔ `PictureType` round-trip,MIME/宽高由调用方提供的逻辑保留);`Save()` 前设 ID3v2 版本。
- 句柄/free(`bb/zzz/zz1/dd`)、`NativeFileExists` → 删除,用托管等价。
- **保留** comment 槽原样写(其中可能是 rc4 生成的“163 key”,阶段 A 不碰加密)。

**完成判据**:对拍基线全绿 + `Verify-Build.ps1 -RunSmokeTests` + 人工逐格式回归(读/改/存/撤销)。达标后**把 `musictag/MusicTag.dll` 还原为未补丁原版**(标签不再走 native,门不再相关),并更新 `DECOMPILATION_NOTES.md`。

**最难的子项**(§3.2 中标“硬骨头”):`LoadRawTextFieldData` + `DecodeFieldWithEncoding` 的“用指定代码页重解错误编码 CJK”功能,需要拿到 ID3v2 帧原始字节 + text-encoding byte 自行重渲染(TagLibSharp 给的是已解码字符串)。若成本过高,可作为阶段 A 的**可选子特性**单列(多数用户不用),先保证主路径。

## 7. 阶段 B — 在线去 native(完成即删除 DLL)
1. **常量硬编**:把 §3.4 全部模板搬进各 provider(替掉 na/nb/nc/naa/nab/rc1、nd/ndd/ne/nf、pa/pb、nh/ni 的 P/Invoke 与静态初始化)。`rc`(X-Real-IP)改为托管随机 `112.88.x.x` 生成器。
2. **网易云算法**:托管实现 `rc2`(weapi,返回 `{a,b}` 或直接改三处调用点)、`rc4`/`rc3`(163 key 编/解码)。对照公开参考实现核对密钥常量,用当前 native 输出做对拍(同输入比对 `a/b`、163key 串)。
3. **编码检测**:`Tokenizer.DetectFileEncoding` 改走 charset 检测库,保留 `ISO8859-1/空→Encoding.Default` 回退。
4. 删除 `ConfigDescriptorState` 里残余的 native import、`MusicTag.dll`/`MediaInfo.dll`(csproj `None` 项 + musictag/ 文件),回归联网搜索四源 + 导入 .lrc。

## 8. 验证策略(无单测下的对拍)
- **黄金对拍**:native(阶段 A 基线)vs 托管,逐文件逐字段 JSON diff —— 标签迁移的主要保障。
- **算法对拍**:同输入下 native `rc2/rc3/rc4` 输出 vs 托管输出逐字节比对(用 §保留的探针,见 `artifacts/`)。
- 每阶段:`Verify-Build.ps1 -RunSmokeTests` + `git diff --check`。
- 联网四源 + 导入 .lrc 的人工冒烟(自动化不覆盖)。

## 9. 风险、决策点与回退
- **决策点(冷门格式)**:实测仅 `.dff` / `.tak` / `.aifc` 三个边缘容器 TagLibSharp 不支持(`.dsf`/`.aac` 已支持)。三选一:(a) 仅这三个格式保留 native 标签 I/O —— 但那部分仍被门 gate,**补丁无法移除**,与初衷冲突;(b) 自实现最小读写(`.dff` 的 DIIN/ID3 chunk、`.tak` 的 APEv2 等);(c) 明确降级(只读/不支持)。鉴于缺口已缩到三个冷门容器,(c) 的代价很低,推荐(c)。需你拍板。
- **raw 编码重解码**(§6 硬骨头):TagLibSharp 抽象层可能够不到帧级字节,最坏要 fork/反射;可先降级为可选特性。
- **网易云算法对齐**:weapi/163key 常量记错即全废,靠 native 对拍兜底。
- **回退**:阶段 A/B 各自一组提交,任一阶段出问题可单独 `git revert`;补丁原版 DLL 始终是 `artifacts/MusicTag.dll.orig` + git 历史。

## 10. 里程碑与提交粒度
- P0 `chore: TagLibSharp PoC + 黄金对拍基线`(可丢弃的 PoC 留 artifacts)
- PA1 `refactor: ConfigDescriptorState 改用 TagLibSharp(行为等价)`
- PA2 `build: 还原未补丁原版 MusicTag.dll;移除反篡改补丁记录`
- PB1 `refactor: 在线 URL/头常量去 native(QQ/酷我/酷狗/网易)`
- PB2 `feat: 托管实现网易云 weapi 与 163key 编解码`
- PB3 `refactor: 编码检测改用托管 charset 库`
- PB4 `build: 删除 MusicTag.dll / MediaInfo.dll 及其引用`
- PX `docs: 收尾同步 README/MAINTENANCE/DECOMPILATION_NOTES`

## 11. 阶段 0 实测结果(2026-06-25,已完成)

**环境**:TagLibSharp 2.3.0(NuGet;net481 集成时引用 `lib/net462/TagLibSharp.dll`)。探针留存于 `artifacts/`(gitignored):`tlspoc/`(格式枚举)、`GoldenCompare.cs`+`gc.exe`(x86 对拍)、`gc_result.txt`(对拍输出)。

**① 格式覆盖**(反射枚举 `FileTypes.AvailableTypes` + `SupportedMimeType`,确定性):18 个应用扩展名支持 **15** 个;**仅 `.dff` / `.tak` / `.aifc` 不支持**(`.dsf`/`.aac` 实测支持,修正原预判)。

**② 黄金对拍**(9 个真实样本 mp3×3 / flac×3 / ogg×3,native 打补丁版当 oracle):
- **文本字段主路径等价**:title/album/year/track/disc/genre/albumartist/composer/comment/lyrics 在三格式上逐字段一致;`comment` 的 "163 key" 原样读出(印证阶段 A 不碰 comment),`lyrics` 完全一致。
- **已定位、可控的映射细节**(阶段 A 落实):
  1. **多值连接符**:native 用**配置的** `ConnectorsArtists`(默认 `/`)join,非 "; "。TagLibSharp 已正确分出多值(`Performers=["赵乃吉","DJ细霖"]`),用同一配置连接符 join 即等价。
  2. **`lyricist`**:9 样本均空(两边都空);需容器级映射(ID3v2 `TEXT` / Xiph `LYRICIST` / APE `Lyricist`)并用带值样本验证。
  3. **`trackstr` 无值**:native 给 `"0"`(而 `track` 给空)——量级 quirk,决定是否复刻。
  4. **有损格式 `BitsPerSample`**:native 填 `16`,TagLibSharp 给 `0`(仅无损有意义)。
  5. **`_ext`**:native 返回大写。
  6. **mp3/ogg 时长、码率**:估算口径差极小(时长 <40ms;ogg 码率 native 实测平均 vs taglib 标称);flac 时长完全一致。
- ✅ **封面语义差异已查清**:`半壶纱.mp3` 的“6 张图”实为 Serato DJ 写入的 6 个 `PictureType.NotAPicture` / `application/json` 元数据对象(CuePoints、Serato Markers、Key、Energy、BeatGrid),**非真实封面**;native 正确过滤(返回 0),TagLibSharp 把它们计入 `Tag.Pictures`。**阶段 A 规则**:封面读写一律过滤 `Type == PictureType.NotAPicture`(或只取 `image/*` mime),即与 native 等价。其余样本的真实封面两边均 =1。

**③ 编码检测库候选**(阶段 B `de` 替代):**UTF.Unknown**(MIT,活跃,Mozilla 通用字符集检测移植)或 Ude.NetStandard;保留 `ISO8859-1`/空 → `Encoding.Default` 回退,阶段 B 对 `.lrc` 小样本对拍。

**结论**:标签 I/O 可整体迁移到 TagLibSharp、**补丁可移除**;封面语义差异已查清(过滤 `NotAPicture` 即与 native 等价),**阶段 A 无已知阻塞**。冷门格式缺口收窄到三个边缘容器,推荐对其明确降级(§9 c)。

## 12. 阶段 A 实测结果(2026-06-25,已完成)

**做了什么**:`MusicTag.States/ConfigDescriptorState.cs` 的内部实现从 P/Invoke(native `MusicTag.dll`)整体改为托管 **TagLibSharp**,**公共 API / 字段词表 / 缓存键全部不变**(StateFieldInstance 的 ~30 处调用点零改动)。`MusicTag.csproj` 新增 `<Reference Include="TagLibSharp">`(HintPath `musictag/TagLibSharp.dll`,TagLibSharp 2.3.0 的 `lib/net462` 构建),运行时 DLL 落 `musictag/`。完成后**把 `musictag/MusicTag.dll` 还原为未打补丁原版**(覆盖自 `artifacts/MusicTag.dll.orig`),即 git 层面回退 commit `a9907e2` 的 3 字节反篡改补丁——标签不再走 native,门不再相关。

**对拍回归(主要保障,无单测)**——探针留存于 gitignored `artifacts/`(`StageACompare.cs` 四模式:native oracle / 新实现读对拍 / 写自洽 / native 回读;`NativeWriteProbe.cs`;`OnlineProbe.cs`):
- **读对拍(9 样本 mp3×3/flac×3/ogg×3,native 打补丁版当 oracle)**:全部 DIF 已逐条解释,**无一为封装 bug**——
  - `trackstr`×4:native `ee` 原始返回 `"0"`,新实现按原软件逻辑 `((int)track > 0) ? str : ""` 给空串;查 git HEAD 证实原软件本就是此逻辑,**新实现与原软件一致**(oracle 取的是更底层的 `ee` 原值,非原软件展示值)。
  - `_durms`×2 / `_bitrate`×3:MediaInfo(native)vs TagLibSharp 的测量口径差(时长 ±26–39ms;flac 码率 ±1 取整;ogg native 实测均值 vs taglib 标称 320),非逻辑差异。
- **写圆环(写后自读)**:`WRITE DIFF=0`(mp3/flac/ogg 全字段,含多值 artist、year/track/disc、lyricist、lyrics、封面保留)。
- **写回 native 交叉校验**(用**原版 oracle** 读回新实现写出的文件):`VERIFYWRITE DIFF=0`——证明新实现写出的标签,原生读取器逐字段读回一致。

**两处行为保真决策(经 native 实测后对齐,非臆测)**:
1. **多值字段按整串写一个标签字段**(`ToSingleValue`):原 native `m0` 契约对每个多值字段(如 artist=`"甲/乙"`)收到的是**一个字符串**并存入**单个**标签字段。新实现若按分隔符拆成多个 Xiph/ID3 字段会与原行为不符(verifywrite 表现为 `甲; 乙` vs `甲/乙`)。改为整串写单字段后 DIFF 归零。读路径的 `JoinMulti`(用 `Settings.Default.ConnectorsArtists`,默认 `/`)保持不变(读对拍已证等价)。
2. **改写 comment 时清空所有 COMM 帧**:`NativeWriteProbe` 实测原 native `m0`+`m3` 写 comment 时**会清掉**网易云“163 key”COMM 帧(`163key RETAINED by native write: NO`)。为保真,新实现写 comment 前 `id3v2.RemoveFrames("COMM")`。这意味着新实现同样在编辑 comment 时丢弃 163key——**刻意复刻原行为**(反编译恢复项目的首要约束是行为等价,即便原行为可能不理想)。

**在线导出未受门限制(实测复核)**:`OnlineProbe` 对**打补丁 vs 原版**两个 DLL 跑全部无参在线端点(na/nb/nc/naa/nab/rc/rc1/nd/ndd/ne/nf/pa/pb)+ `bb` free——14 个里 13 个**逐字节一致**,仅 `rc`(X-Real-IP 头值)不同,因其本就是 native 端运行时随机 `112.88.x.x`(非门、非常量)。证明还原原版 DLL 后,重编译的 EXE 仍能从原 DLL 取到全部在线端点字符串,**阶段 B 不受影响**。

**残留的 native 依赖(本文件内,属阶段 B)**:仅保留 `[DllImport("MusicTag.dll", EntryPoint="bb")] FreeNativeString` 与 `ReadAndFreeNativeString`——它们服务在线路径的非托管字符串释放,不被门 gate,留待阶段 B 一并清理。

**测试脚手架的一处坑(仅影响反射对拍,不影响真实 EXE)**:用 `Assembly.LoadFrom` 反射加载 `MusicTag.exe` 跑新实现时,CLR 的 LoadFrom 绑定上下文与默认上下文隔离;当被加载代码按名字解析 “MusicTag” 程序集,fusion 按 appbase 文件系统探测(`.dll` 先于 `.exe`),会先撞上 native `MusicTag.dll` 并以托管方式加载失败抛 `BadImageFormatException`。解法:新实现对拍在**移除了 native `MusicTag.dll` 的 bin/ 副本**里跑,native oracle 在原 bin/ 跑,经 `oracle.json` 交换数据。真实 `MusicTag.exe` 是默认上下文的入口程序集,**永不触发此问题**;仅反射探针需规避。

**冷门格式**:`.dff` / `.tak` / `.aifc` 三个边缘容器 TagLibSharp 不支持,`TagLib.File.Create` 抛 `UnsupportedFormatException` → 映射回 `loadError`(等同 §9 c 的“明确降级”)。`.dsf`/`.aac` 等其余 15 个应用扩展名均支持。

**验证**:`Verify-Build.ps1 -RunSmokeTests` 绿(Debug+Release 构建 + 3 冒烟:`FilenameRelatedBatchDialog`/`OptionsDialog` 反射构造 + EXE 存活);`git diff --check` 干净;`codegraph sync` 通过。**未自动化覆盖**:四源联网搜索与导入 .lrc 的端到端人工冒烟(无自动化测试,沿用既定可接受风险)。

**提交**:PA1(迁移)+ PA2(还原原版 DLL)合并为一次提交落地(连同本文档与 `DECOMPILATION_NOTES.md` 同步)。

**阶段 A 状态:完成,补丁已移除。** 阶段 B(在线去 native + 删除 `MusicTag.dll`/`MediaInfo.dll`)尚未授权开始。
