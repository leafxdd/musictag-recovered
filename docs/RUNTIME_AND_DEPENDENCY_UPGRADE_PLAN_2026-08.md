# MusicTag 运行时与依赖升级方案

- 日期：2026-08-30
- 分支：`develop-net8`
- 状态：待实施
- 范围：收尾 x64 方案的残留项；把目标框架从 `net8.0-windows` 升到 `net10.0-windows`；处理有 CVE 和落后补丁的依赖
- 前置：`docs/X64_MINIMAL_MIGRATION_PLAN_2026-08.md`、`docs/X64_MIGRATION_IMPLEMENTATION_REPORT_2026-08.md`

本文 §2 的全部数字来自 2026-08-30 的实测（NuGet / OSV 查询、net10 隔离探针、本地全量套件），不是估计值。
凡未实测的判断，正文中显式标注为“待验证”。

## 1. 目标与决策

驱动因素只有一个是硬的：**.NET 8 的生命周期在 2026-11-10 结束，距今 72 天**。其余都是可以自选时机的维护项。

采用以下决策：

1. 目标框架 `net8.0-windows` → `net10.0-windows`。不走 net9——net9 与 net8 **同日**（2026-11-10）结束支持，升过去等于没升；net10 是 LTS，支持到 2028-11-14。
2. 沿用 x64 方案的全部边界：`PlatformTarget=x64` 硬约束、解决方案配置仍名 `Any CPU`、framework-dependent 部署、`bin/{Configuration}/{TFM}/` 输出结构、不引入 `RuntimeIdentifier` 或 self-contained。本次只换框架版本，不换部署模型。
3. **BinaryFormatter 必须先脱钩，再换框架**，且脱钩方式选“换负载格式”，不引入 `System.Runtime.Serialization.Formatters` 过渡包（理由见 §3 批次 3）。
4. Newtonsoft.Json 12.0.2 → 13.0.4，并从本地 `Reference` 改为 `PackageReference`。这与 x64 方案 §1 拒绝对 SQLite 做同样改动的决定**不冲突**，差异理由见 §3 批次 1。
5. SixLabors.ImageSharp 2.1.12 → 2.1.13，留在 2.x 线，不跳 3.x/4.x。
6. 不动 System.Data.SQLite、TagLibSharp、UtfUnknown、Fkosoft.FontAwesome4，理由见 §5。

分支名 `develop-net8` 保持不变。它已推到 origin 且被 README / AGENTS / CLAUDE 引用，为一次框架小版本改名的收益不抵成本；改名另议。

## 2. 实测基线（2026-08-30）

### 2.1 x64 方案的残留项

x64 迁移的**代码面没有欠账**。逐项复核结论：两个项目 `PlatformTarget=x64` 已生效；`SQLite.Interop.dll` 实测 SHA-256 `1D534617B38323027A64579A581258A55C3986F5B4B15297126C8A4CEF5AA105`、`Machine=0x8664`、文件版本 `1.0.113.0`，与方案 §2.2 逐位吻合；`NativeNotificationHeader.ControlId` 已是 `UIntPtr`；`ShellFileInfo` 已是 Unicode Sequential 且 `IconIndex` 为 `int`；`SHGetFileInfoW` 入口固定；回调 `lParam` 已是 `IntPtr`；四个结构的布局断言确实按 `IntPtr.Size` 参数化（`NativeInteropLayoutCharacterization.cs:16-39`）。方案 §4.5 判定“不可达因而不处理”的 `ListViewColumnInfo` / `SendListViewColumnMessage` / `ThumbBar*` 复核后仍是零调用点；§12.2 的排除项（零 `IntPtr.ToInt32()`、零注册表、零 `System32` 硬编码、零 `unsafe`）今天仍然成立。

欠的四项全在验收和流程面：

| # | 残留项 | 依据 |
|---|---|---|
| R1 | §8.2 的 9 项重点手工验收**一项都没有记录完成** | 方案 §5 状态行与实施报告 §6 均写“待目标机人工回归”，此后 27 天无任何提交或文档记录结果 |
| R2 | 发布说明未更新运行时前提 | 方案 §7 用了“**必须**写进发布说明”；`artifacts/release/RELEASE_NOTES.md:5-10` 至今写着“x86 进程”“.NET Framework 4.8/4.8.1”“`MusicTag-1.0.9-net481-x86.zip`” |
| R3 | CI 从未跑过 x64 新增的回归网 | `.github/workflows/build.yml:20` 未传 `-RunSmokeTests`，980 个用例、四个结构的 ABI 断言、真实 SQLite x64 加载门禁全部只在本地跑 |
| R4 | 本地领先 origin 5 个提交（`4d45b2a..c94052b`）未过 CI | `git branch -vv` 报 `ahead 5`；最后一次绿色 CI 是 `fb50789`（2026-08-03） |

R1 中风险最高的是**文件图标**那项：`cbFileInfo` 从 352 改成 692/696 是对原生调用的可观察变更，而自动门禁只断言 `GetSmallFileIcon` 返回非空且尺寸为正（`NativeInteropLayoutCharacterization.cs:54-59`），**没有比对图标内容**。方案 §4.2 要求的“与批次 1 基线逐项比对”没有执行。

R2 有缓解：`artifacts/` 在 `.gitignore` 内，那是 net481 时代的本地遗留文件，不是受版本控制的发布说明；用户实际能看到的 `README.md:21-23` 已正确写明“64 位 Windows”与“.NET 8 Windows Desktop Runtime x64”。所以是“计划要求的动作没做，但没有对外误导”。

### 2.2 依赖现状与漏洞实测

关键方法论：`dotnet list package --vulnerable` 对两个项目都报“没有易受攻击的包”，但**它只看得见 2 个 `PackageReference`**；5 个本地 `<Reference>` HintPath DLL 完全在它视野之外。下表的漏洞列通过 OSV（`api.osv.dev/v1/query`）逐版本查询得到，覆盖两类依赖。

| 依赖 | 引用方式 | 当前 | 最新 | OSV 结果 | 本方案处置 |
|---|---|---|---|---|---|
| TFM | — | net8.0-windows | net10 LTS | .NET 8 于 2026-11-10 EOL | **批次 4 升级** |
| Newtonsoft.Json | 本地 DLL | 12.0.2 | 13.0.4 | **GHSA-5crp-9r3c-p9vr** | **批次 1 升级** |
| SixLabors.ImageSharp | PackageReference | 2.1.12 | 2.1.13（2.x 线） | 无 | 批次 2 打补丁 |
| System.Runtime.Caching | PackageReference | 8.0.1 | 8.0.1 是 8.x 线终点 | 无 | 随 TFM 到 10.0.x |
| System.Data.SQLite | 本地 DLL | 1.0.113.0 | 1.0.119 | 无 | 不动（§5） |
| UtfUnknown | 本地 DLL | 2.5.1 | 2.7.0 | 无 | 不动（§5） |
| TagLibSharp | 本地 DLL | 2.3.0 | 2.3.0 | 无 | 已是最新 |
| Fkosoft.FontAwesome4 | 本地 DLL | 1.0.0.0（2019） | 非 NuGet | 无 | 不动（§5） |

`GHSA-5crp-9r3c-p9vr`（CVE-2024-21907）CVSS 3.1 向量 `AV:N/AC:L/PR:N/UI:N/S:U/C:N/I:N/A:H`，7.5 High，纯可用性影响；12.x 全线受影响，13.0.1 修复。

**本仓库的暴露面是真实的**：31 处 `JsonConvert.DeserializeObject` / `JObject.Parse` 分布在 13 个文件，网易云、QQ、酷狗、酷我四个 provider 都在直接解析远端响应；全仓**零 `MaxDepth`、零 `JsonSerializerSettings`**，即 12.0.2 的无界递归就是当前实际行为。构造超深嵌套 JSON 可打出 `StackOverflowException`，在 .NET 上这是不可捕获的进程级崩溃。触发需要控制平台的 HTTP 响应（中间人或平台侧返回恶意载荷），不是 RCE，后果是崩溃并丢失未保存的标签编辑。

### 2.3 net10 隔离探针结果

按 x64 方案 §2.3 的做法，在**不修改任何项目文件**的前提下用命令行覆盖目标框架探测：

```powershell
dotnet restore .\src\MusicTag\MusicTag.csproj -p:TargetFramework=net10.0-windows --force
dotnet build   .\src\MusicTag\MusicTag.csproj -c Release -p:TargetFramework=net10.0-windows --no-restore
```

注意：`TargetFramework` 全局属性**不会**沿 `ProjectReference` 传递（SDK 为支持多目标而有意剥离），所以测试宿主无法用同样方式整体探测；探针只覆盖主项目。这一限制本身就是批次 4 必须真改 csproj 的原因。

结果：**14 个错误、20 个警告**。

**14 个错误全部是 `WFO1000`**，即 .NET 9 起随 WinForms SDK 引入的分析器规则“设计器可见属性未配置代码序列化”。分布：`TagSearchCandidatePanel.cs` 8 处、`CheckBoxColumnHeader.cs` 3 处、`SourceOrderControl.cs`、`EditableListView.cs`、`FindReplaceDialog.cs` 各 1 处。这是分析器诊断，不是真实断裂。

20 个警告分布：`WFDEV004`（`Form.OnClosing`/`OnClosed` 过时）8 处、`CS0672`（重写过时成员）8 处、`SYSLIB0023`（`RNGCryptoServiceProvider`，`NetEaseCrypto.cs:78`）2 处、`SYSLIB0011`（BinaryFormatter 过时，`ChineseTextConverter.cs:30`）1 处、`SYSLIB0021`（`MD5CryptoServiceProvider`，`TextUtilities.cs:66`）1 处。

三个必须写下来的观察：

1. **BinaryFormatter 在 net10 上能编译过，只出 `SYSLIB0011` 警告——这正是它危险的地方。** .NET 9 起 API 保留但实现移除，`Deserialize` 恒抛 `PlatformNotSupportedException`，且 `EnableUnsafeBinaryFormatterSerialization` 开关**不再有任何作用**（探针中该开关仍设为 true，未产生任何 SDK 错误，说明它被静默忽略）。所以这是**编译期沉默、运行期爆炸**的一类问题，构建绿不代表安全。
2. **67 条 resx 二进制资源（`Resources.resx` 66 条 + `StateFieldInstance.resx` 1 条）在 net10 SDK 下正常生成，未出现 MSB3825 或资源生成失败。** 这与 .NET 9 起 `System.Resources.Extensions` / WinForms 为已知类型内建二进制格式支持的设计一致。运行期能否正确反序列化仍**待验证**（批次 4 的对话框构造冒烟覆盖）。
3. 探针未发现任何 API 缺失、interop 断裂或 SQLite 相关错误。

探针后已删除 `bin/Release/net10.0-windows` 与 `obj/Release/net10.0-windows`，重新执行 `dotnet build .\MusicTag.sln -c Release` 恢复（0 警告 0 错误），套件复跑 **`980 passed, 0 failed`**。工作树与探针前一致。

（当前套件为 980 个用例，x64 实施报告记录的 964 之后由 QQ 相关提交增加了 16 个。）

## 3. 实施批次

原则与 x64 方案一致：**一次提交只动一个变量**，每个提交可独立回滚。

### 批次 0：先修回归网和发布说明（零行为风险，必须最先做）

对应残留项 R2、R3。放在最前面，是因为后面每一个批次都依赖这张网。

1. `.github/workflows/build.yml`：`Verify-Build.ps1` 调用加 `-RunSmokeTests`，让 980 个用例、四个结构的 ABI 断言和真实 SQLite x64 加载门禁进入 CI。
2. 同文件：增加 `actions/setup-dotnet` 并钉住 SDK 主版本，消除“SDK 随 runner 镜像漂”。
3. 同文件：删除 `microsoft/setup-msbuild@v2`——脚本早已改用 `dotnet build`，这步现在是空转。
4. 重写 `artifacts/release/RELEASE_NOTES.md` 的“运行要求”与包名，写明 **64 位 Windows + .NET 8 Windows Desktop Runtime x64**（批次 4 落地后再改成 net10）。虽然该文件在 `.gitignore` 内，但它是发包时的实际文案来源，必须与产物一致。

停止条件：CI 加 `-RunSmokeTests` 后若在 runner 上失败（启动冒烟需要能拉起 WinForms 进程），先定位是环境还是产品问题，**不得**用回退开关的方式绕过。

建议提交：`ci: run the full gate and drop vestigial msbuild setup`

### 批次 1：Newtonsoft.Json 12.0.2 → 13.0.4

对应 §2.2 的唯一安全发现。在 net8 上完成，与框架升级隔离。

1. `MusicTag.csproj`：删除本地 `<Reference Include="Newtonsoft.Json">`，改为 `<PackageReference Include="Newtonsoft.Json" Version="13.0.4" />`。
2. `MusicTag.Tests.csproj`：删除本地 `<Reference Include="Newtonsoft.Json">`（编译资产经 `ProjectReference` 传递）。若实际构建证明不传递，则在测试项目也加同版本 `PackageReference`——以构建结果为准，不预设。
3. 删除 `src/MusicTag/musictag/Newtonsoft.Json.dll`。该文件不在 csproj 的 `<None ... CopyToOutputDirectory>` 清单内，是经 `<Reference>` 机制复制的，改成 `PackageReference` 后即成为死文件。
4. 更新 `AGENTS.md` / `CLAUDE.md` 中“运行时资产”的描述。

**为什么这里可以改 `PackageReference`，而 x64 方案 §1 拒绝对 SQLite 这样做**：SQLite 的托管件与原生 `SQLite.Interop.dll` 是必须成对匹配的已知量，换资产会同时换掉托管 Provider，超出“只替换架构配对”的边界；Newtonsoft.Json 没有原生配对，且当前引用的是 **netfx 构建的 12.0.2 跑在 .NET 8 上**——这是反编译恢复的历史产物，不是深思熟虑的 pin。取 13.0.4 的 `net6.0` 资产严格更正确。

**必须盯住的行为变更**：13.0.1 起默认 `MaxDepth=64`——这正是该 CVE 的修复手段本身。音乐 API 响应的嵌套深度远不到 64 层，四个 provider 的 characterization（注入录制的 HTTP 响应）正好是这一点的门禁。若任一 provider 用例报深度相关失败，说明真实响应比预期深，此时**不得**简单地把 MaxDepth 调大回无界，应记录实际深度后单独决策。

建议提交：`fix: upgrade Newtonsoft.Json to 13.0.4 for CVE-2024-21907`

### 批次 2：ImageSharp 2.1.12 → 2.1.13

单行版本号改动。OSV 对 2.1.12 和 2.1.13 均无漏洞记录，属纯 bugfix 跟进。封面下载/缩放/编码相关 characterization（`CoverDownloadCoreCharacterization`、`CoverImageProcessorCharacterization`）是门禁。

建议提交：`chore: pick up ImageSharp 2.1.13`

### 批次 3：BinaryFormatter 脱钩（net10 的硬前提）

**这是整个方案技术风险最高、也最容易被低估的一步。** 必须在 net8 上完成并验证通过，再进入批次 4。

唯一的代码调用点是 `ChineseTextConverter.cs:30`：

```csharp
MappingChars mappingChars = new BinaryFormatter().Deserialize(mappingStream) as MappingChars;
```

反序列化的是内嵌资源 `Resources.tsmap` / `Resources.tcmap`（`byte[]`），目标类型 `MappingChars` 只有两个字段（`MappingChars.cs:8-10`）：

```csharp
public string chars;
public string[] lexemics;
```

两条路线：

**方案 A（采纳）——换负载格式。** 用一次性转换工具在 net8 上读出现有两个 blob（此时 BinaryFormatter 仍可用），以 `BinaryWriter` 的长度前缀 UTF-8 格式重写（先写 `chars`，再写 `lexemics.Length` 与逐条字符串），作为新的内嵌资源提交；`ChineseTextConverter` 改用对应的 `BinaryReader` 读取；删除旧 blob 与 `EnableUnsafeBinaryFormatterSerialization`（两个 csproj 各一处）。

**方案 B（备选）——引入 `System.Runtime.Serialization.Formatters` 10.0.x 包。** 一行改动、零代码变更，但保留一个 Microsoft 明确定位为“迁移过渡、非长期方案”的兼容层，并且为整个进程重新打开完整的 BinaryFormatter 反序列化面——这是众所周知的 gadget/RCE 形状 API，而本应用的用途只是读两个构建期内嵌的常量表，完全不需要它。

采纳 A 的理由：负载结构平凡（一个字符串 + 一个字符串数组）、转换是一次性的、`ChineseTextConverterCharacterization` 已锁定简繁/繁简的实际转换结果，等价性有现成门禁。若转换过程中发现 blob 里存在超出上述两字段的内容（例如自定义类型嵌套），立即停下改走方案 B，并把发现记进实施报告。

必须做的记录：转换前后两个 blob 的 SHA-256、转换工具的完整源码（放 `artifacts/`，不入库）、以及转换前后 `ChineseTextConverterCharacterization` 的用例结果。

建议提交：`refactor: drop BinaryFormatter from the Chinese conversion tables`

### 批次 4：net8.0-windows → net10.0-windows

1. 两个 csproj 的 `TargetFramework` 改为 `net10.0-windows`。
2. `System.Runtime.Caching` 8.0.1 → 10.0.x。
3. `WFO1000`：在 `MusicTag.csproj` 的 `NoWarn` 中加入并写明原因注释。仓库已有完全对应的先例——同一个 `PropertyGroup` 里的 `NoWarn;CS0649` 就是为反编译代码的分析器噪声而设（`MusicTag.csproj:25-30`）。这些控件属性从不在设计器中打开，逐个补 `[DesignerSerializationVisibility]` / `[DefaultValue]` 会引入 14 处无回归价值的元数据改动。补属性作为可选清理另议，不进本批次。
4. `scripts/Verify-Build.ps1`：3 处硬编码路径（`:19`、`:29`、`:100`）与 5 行说明注释里的 `net8.0-windows` / `net8` 更新。
5. `.github/workflows/build.yml`：`setup-dotnet` 的 SDK 版本跟到 10。
6. `README.md`（4 处）、`AGENTS.md`（4 处）、`CLAUDE.md`（2 处）中的框架与路径描述更新；`RELEASE_NOTES.md` 的运行要求改为 .NET 10 Windows Desktop Runtime x64。
7. 不动：`app.manifest`（DPI 已在 `Program.Main` 里设置，manifest 无框架相关内容）、`MusicTag.exe.config`、`MusicTag.sln`、分支名。

`SYSLIB0011` / `SYSLIB0021` / `SYSLIB0023` / `WFDEV004` / `CS0672` 这些过时警告**本批次不处理**。它们是真实的技术债，但每一条都涉及行为面（加密 API 替换、窗体关闭事件语义），必须单独立项并各自配 characterization，混进框架升级会让回归归因失效。

**一个需要盯住的具体风险**：`Fkosoft.FontAwesome4` 是 netfx 程序集，构造时绑定 `System.Runtime.Caching 4.0.0.0`（`MusicTag.csproj:44-47` 有注释说明，缺它则启动即崩 `FileNotFoundException`）。10.0.x 包是否仍暴露该程序集版本、以及 .NET 的版本前滚是否覆盖，**待验证**。好消息是现成门禁正好覆盖这一点：`Verify-Build.ps1` 的启动冒烟加异常日志扫描，就是当年为这个 FontAwesome 缺包的假阳性专门加的（`Verify-Build.ps1:15-16`）。

建议提交：`build: retarget MusicTag and tests to net10.0-windows`

### 批次 5：合并手工验收（x64 R1 + net10 回归）

对应残留项 R1。**x64 欠的 9 项手工验收与 net10 需要的回归高度重叠，合并成一次执行**——net10 的 WinForms 又调整了 DPI 与字体缩放行为，而 DPI 双屏验证正是 x64 欠的那一项，分两次做纯属重复劳动。

按 x64 方案 §8.2 的清单逐项执行并**逐项书面记录结果**（不是“跑了一遍没问题”）：

1. 用真实历史数据库执行查看、保存、恢复、清空与 `VACUUM`。
2. 第二实例经 `WM_COPYDATA` 转发文件参数。
3. 文件/目录对话框、任务栏进度、列表头右键命中。
4. **文件图标**：加载带图标的列表，验证获取、释放与重复刷新。这一项要额外截图留档——x64 批次 1 应留而未留的基线现在补不回来了，只能以当前 net8 x64 的表现作为 net10 的对照基线。
5. 100% / 150% 双屏：副屏首启、跨屏拖动与往返；组合标签源、歌词源与动态封面窗口。
6. 代表性 MP3 / FLAC 的实体标签写入与回读。
7. 简繁 / 繁简转换（批次 3 换了负载格式，且这是 BinaryFormatter 移除后唯一可能沉默失败的路径）。
8. 从干净输出制作发布包，扫描 `.pdb`、`temp/`、日志、缓存、备份设置与用户数据。

### 批次 6：记录实施结果

新建 `docs/RUNTIME_AND_DEPENDENCY_UPGRADE_REPORT_2026-XX.md`，记录最终版本号、blob 哈希、各批次用例数变化、手工验收逐项结果、残余风险与回滚演练。

建议提交：`docs: record the net10 and dependency upgrade results`

## 4. 预计修改范围

产品与构建：

- `src/MusicTag/MusicTag.csproj`（TFM、`NoWarn`、`PackageReference` 三处、删 `EnableUnsafeBinaryFormatterSerialization`）
- `src/MusicTag.Tests/MusicTag.Tests.csproj`（同上）
- `src/MusicTag/MusicTagWinApp.Structs/ChineseTextConverter.cs`
- `src/MusicTag/MusicTagWinApp.Properties/Resources.resx` 及 `Resources.cs`（tsmap / tcmap 负载替换）
- 删除 `src/MusicTag/musictag/Newtonsoft.Json.dll`
- `scripts/Verify-Build.ps1`
- `.github/workflows/build.yml`

文档：`README.md`、`AGENTS.md`、`CLAUDE.md`、`artifacts/release/RELEASE_NOTES.md`、本文与实施报告。

**不修改**：`MusicTag.sln`、`app.manifest`、`MusicTag.exe.config`、`TagHistoryRepository.cs`、SQLite schema、任何 Win32 interop 声明、Provider 协议、标签写入策略、DPI 算法、设置键、`SearchSource` 序号。`MusicTag.db`/`MusicTag.dat` 属于安装实例状态，不再作为源码或发布包输入。

## 5. 明确不做的

- **System.Data.SQLite 1.0.113.0 → 1.0.119**。OSV 对 1.0.113 无漏洞记录。SQLite 引擎（3.32.1，2020 年）自身此后修过若干 CVE，但本项目的用法是应用自有的本地标签历史库配参数化命令，不接触不可信 SQL 或外来数据库文件，实际暴露面接近零。而升级代价高：必须托管件与原生 interop 成对更换（x64 方案 §7 的回滚约束反向同样成立），且现有两个文件是与官方包逐字节核对过的已知量，换版本要重做身份核对加真实历史库回归。属 x64 方案 §10 议题 1，不该顺手做。
- **UtfUnknown 2.5.1 → 2.7.0**。无漏洞。编码探测结果的任何变化会直接改变歌词与标签的乱码判定，高行为敏感、低收益。
- **Fkosoft.FontAwesome4**。它是 `System.Runtime.Caching` 存在的唯一理由；替换它才能去掉那个引用。纯技术债，不是风险。
- **ImageSharp 3.x / 4.x**。大版本跳，有 API 破坏。
- **`SYSLIB0011` / `SYSLIB0021` / `SYSLIB0023` / `WFDEV004` 的过时 API 清理**、**WFO1000 的逐属性标注**、**解决方案配置改名为 `x64`**、**`win-x64` RID 或 self-contained**、**未使用 P/Invoke 清理**。全部另立议题。

## 6. 兼容性与回滚

- 数据库格式、schema、路径不变；没有 migration。
- 设置键、JSON、资源名、枚举序号、TagLib 写入策略与在线 Provider 合同不变。
- 运行时前提从 .NET 8 Windows Desktop Runtime x64 变为 **.NET 10**。这是对用户可见的破坏性前提变化，**必须写进发布说明**——x64 方案在同一条上已经漏过一次（R2），不要重复。
- 每个批次独立可回滚。批次 3 与批次 4 存在**单向依赖**：可以只回滚批次 4 保留批次 3（脱钩后的 net8 完全正常）；**不能**只回滚批次 3 保留批次 4，那会得到一个编译通过但简繁转换运行时必崩的产物。
- 批次 1 的回滚需同时还原 csproj 引用方式与 `musictag/Newtonsoft.Json.dll`。

## 7. 验证门禁

每个实现提交执行：

```powershell
.\scripts\Verify-Build.ps1 -RunSmokeTests
git diff --check
codegraph sync
```

提交前跑 GitNexus `detect_changes(scope: staged)`，确认影响面与批次意图一致。

批次归属（照此执行，错位会卡死门禁）：

| 断言 | 自哪个批次起生效 |
|---|---|
| 现有全部 characterization 继续通过 | 批次 0 起，每批次都要过 |
| CI 实际执行完整门禁而非仅构建 | **批次 0** |
| 四个 provider 的 characterization 在 13.0.4 下通过（含 MaxDepth=64 生效后） | **批次 1** |
| `ChineseTextConverterCharacterization` 在新负载格式下通过 | **批次 3** |
| 产物 TFM 为 `net10.0-windows` 且仍为 AMD64 | **批次 4** |
| Release 启动存活且无 `UnhandledException` / `ThreadException` 日志（覆盖 FontAwesome / System.Runtime.Caching 绑定） | 批次 0 起，批次 4 是重点 |
| 对话框构造冒烟通过（覆盖 67 条 resx 二进制资源的运行期反序列化） | **批次 4** |
| §3 批次 5 的 8 项手工结果逐项书面记录 | **批次 5** |

## 8. 风险与停止条件

| 风险 | 防护 | 停止条件 |
|---|---|---|
| BinaryFormatter 编译期沉默、运行期抛 `PlatformNotSupportedException` | 批次 3 先于批次 4；简繁转换 characterization + 手工验收第 7 项 | net10 产物上任何 `PlatformNotSupportedException` |
| 67 条 resx 二进制资源运行期反序列化失败 | 对话框构造冒烟 + 启动日志扫描 | 任一对话框构造抛资源相关异常 |
| `MaxDepth=64` 截断真实 provider 响应 | 四个 provider 的录制响应 characterization | 任一 provider 用例报深度相关失败——记录实际深度后单独决策，不得直接放开为无界 |
| FontAwesome 绑定的 `System.Runtime.Caching 4.0.0.0` 在 10.0.x 下解析失败 | 启动冒烟 + 异常日志扫描（本就是为此加的） | 启动即崩 `FileNotFoundException` |
| net10 WinForms 的 DPI/字体缩放行为变化 | 批次 5 的 100%/150% 双屏验收 | 副屏首显、跨屏往返或固定资产缩放回归 |
| Newtonsoft 换资产（netfx → net6.0）引入隐性差异 | 四个 provider + 序列化编码 characterization | 任何 JSON 解析结果差异 |
| 批次串味导致回归无法归因 | 一次提交一个变量 | 任一提交同时改了框架与依赖 |

任一停止条件出现时，**不得**用“顺便升级另一个依赖”来绕过；回到对应批次定位。

## 9. 验收标准

- 标准 Debug/Release 构建产生 `net10.0-windows` 的 AMD64 主程序与测试宿主。
- 全量 characterization 通过，用例数不低于当前的 980。
- CI 实际执行完整门禁（含特征化套件与 SQLite x64 加载），且为绿。
- 进程内不再存在任何 BinaryFormatter 反序列化调用，且未引入 `System.Runtime.Serialization.Formatters`。
- Newtonsoft.Json 为 13.0.4，`musictag/` 下不再有其本地副本。
- x64 方案 §8.2 的 9 项手工验收**逐项有书面结果**（残留项 R1 闭合）。
- 发布说明的运行要求与实际产物一致（残留项 R2 闭合）。
- 没有混入 SQLite 升级、Provider 替换、过时 API 清理、输出目录调整或部署模型变化。

## 10. 降级路径

若决定**不升 net10**、接受 .NET 8 在 2026-11-10 后无安全更新：

- 批次 0、1、2 照做——它们与框架版本无关，且批次 1 是唯一有 CVE 的项。
- 批次 3 可选。net8 上 BinaryFormatter 经 `EnableUnsafeBinaryFormatterSerialization` 仍能工作，但它已经是一个进程内的 gadget 面，脱钩本身有独立价值。
- 批次 4 跳过。
- 批次 5 仍要做——那是 x64 方案欠的债，与本方案是否升级框架无关。

这条路径把 EOL 风险显式转为“接受”，必须在 README 与发布说明中写明运行时不再接收安全更新，不能默认沉默。

## 11. Codex 交叉审阅意见（2026-08-30）

### 11.1 总体结论

本方案的依赖选择和 BinaryFormatter 脱钩方向基本成立，但实施顺序需要调整后再执行。当前最重要的边界是：x64 迁移的代码已经完成，真实数据库、标签写回、双屏 DPI 和干净发布包等验收仍未闭环；这些项目应先在当前 `net8.0-windows` x64 基线上完成记录，再用于对照 net10 回归。否则后续出现问题时无法区分 x64 遗留问题、TFM 变化和依赖升级问题。

### 11.2 需要修订的技术点

1. **不要把 `System.Runtime.Caching` 升级与 TFM 升级绑定。** 当前 `Fkosoft.FontAwesome4.dll` 引用了 `System.Runtime.Caching, Version=4.0.0.0`。`System.Runtime.Caching 10.0.11` 的 `net8.0`/`net10.0` 资产程序集版本为 `10.0.0.11`，而 `netstandard2.0` 资产仍为 `4.0.0.0`，不能假定旧程序集绑定一定成功。建议先以现有 `8.0.1` 完成 net10 运行验证，再把缓存包升级拆成独立批次，并检查实际解析的资产和程序集版本。
2. **批次 4 违反“一次只改一个变量”。** TFM、缓存包、`NoWarn`、验证脚本、CI、README 和指导文档应至少拆成 TFM、缓存包、分析器配置/文档三个可独立回滚的提交。否则启动、资源或 DPI 回归难以归因。
3. **BinaryFormatter 新负载要有格式边界。** `BinaryWriter` 方案可采纳，但格式应包含 magic、版本号、字符串/数组长度上限和校验，读取时拒绝异常长度。转换工具应可复现并保留源码或格式说明；发现额外字段时应扩展新格式并审查，不应把重新引入 `System.Runtime.Serialization.Formatters` 作为默认后路。
4. **net10 隔离探针不能只编译主项目。** 必须实际构建并运行 net10 测试宿主，覆盖完整 characterization、真实文件数据库、`Fkosoft.FontAwesome4` 启动绑定、TagLib/UtfUnknown、资源和对话框构造。主项目编译通过不能证明 BinaryFormatter、SQLite 或 WinForms 运行期兼容。
5. **Newtonsoft 迁移必须做干净构建。** 从本地 `<Reference>` 改成 `PackageReference` 后应删除对应 `bin/obj` 再构建，并增加程序集身份断言，确认运行时加载的是 `13.0.4` 而不是残留 DLL。CVE 的风险结论成立，但实际影响仍受 HTTPS、响应大小上限和可达输入深度影响；建议保留显式深度限制测试，不要把条件性风险写成必然进程崩溃。
6. **发布说明不能依赖被忽略的文件。** `artifacts/release/RELEASE_NOTES.md` 当前被 `.gitignore` 忽略，且未发现仓库内打包脚本消费它。应先确认真正的发布文案来源；若该文件是发布流程输入，就应明确由发布流程生成或纳入受控交付，不能把修改一个被忽略文件视为已完成的版本记录。
7. **x64 自动门禁仍需补强。** 除 PE 架构外，建议检查官方 `SQLite.Interop.dll` SHA-256、包内重复原生 DLL、托管/原生版本配对，并增加首次运行生成的临时历史库读写和回滚测试。现有内存数据库测试不能替代历史库工作流。
8. **SDK 固定要精确到版本和前滚策略。** 当前机器同时安装 .NET SDK 8 和 10；只固定主版本仍会产生环境漂移。增加 `global.json` 前需说明它对仍使用 net481 的 `develop` 分支的影响，并在 CI 与本地验证中使用同一 SDK 策略。
9. **SQLite 风险表述应收敛。** 应用会从程序目录打开首次运行生成、可被替换的 `MusicTag.db`，因此“实际暴露面接近零”属于有条件的威胁模型，不应作为绝对结论。暂缓 `System.Data.SQLite` 升级可以接受，但应记录这是明确的风险接受项，并单独安排托管件/原生 interop 成对升级评估。
10. **生成文件不应作为手工修改目标。** 资源替换应修改 `.resx` 并让 SDK 重新生成 `Resources.cs`；不要直接编辑生成文件。67 条二进制资源还应增加资源枚举、卫星资源加载和关键对话框构造的明确断言。

### 11.3 建议采用的执行顺序

1. 在当前 net8 x64 版本完成并书面记录 x64 方案 §8.2 的手工验收、干净包扫描和文件图标基线。
2. 先补 CI 完整门禁、精确 SDK 策略、SQLite 哈希/重复文件检查和真实文件数据库 characterization。
3. 在 net8 上独立升级 Newtonsoft.Json 到 `13.0.4`，再独立升级 ImageSharp 到 `2.1.13`，每批次清理输出并运行完整门禁。
4. 在 net8 上完成带版本和长度校验的新简繁字库负载，确认转换结果后再删除 BinaryFormatter 开关。
5. 保持 `System.Runtime.Caching 8.0.1` 不变，单独把两个项目迁移到 `net10.0-windows`，运行完整测试宿主、启动、资源、SQLite 和 DPI 回归。
6. net10 基线稳定后，再单独评估 `System.Runtime.Caching 10.0.11`、`UtfUnknown 2.7.0` 和 `System.Data.SQLite 1.0.119`；其中 SQLite 必须同时更换托管件和 x64 原生库，并执行真实历史库回归与回滚演练。

### 11.4 审阅状态

以上意见是对本计划的只读交叉审阅，不代表已执行任何升级。当前文档仍应保持“待实施”状态；在第 1、2 项基线和门禁工作完成前，不建议直接进入 net10 或缓存包升级。
