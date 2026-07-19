# Claude Code Instructions

## Repository State

`MusicTag` is a recovered Windows WinForms application. The active checkout is
`develop-net8`, targeting `net8.0-windows` x86 through `MusicTag.sln`:

- `src/MusicTag/MusicTag.csproj` is the application.
- `src/MusicTag.Tests/MusicTag.Tests.csproj` is the x86 characterization harness.
- `scripts/Verify-Build.ps1` is the normal build and smoke-test entry point.
- Runtime files under `src/MusicTag/musictag/` are required application assets.

Preserve observable behavior by default. Names in JSON, settings, resources, Win32
interop signatures, enum ordinals, and public method signatures are stable contracts.
Recovered folders and namespaces are not reliable ownership boundaries; locate code by
type or symbol.

The root `AGENTS.md` is the detailed repository policy. This file keeps the Claude
specific operating context and cross-review prompt in the repository. `.claude/` is
local tooling and is not a source of committed project guidance.

## Verification

Run the focused characterization executable for narrow parser/provider changes:

```powershell
dotnet build .\src\MusicTag.Tests\MusicTag.Tests.csproj -c Release
.\src\MusicTag.Tests\bin\Release\net8.0-windows\MusicTag.Tests.exe
```

Run the full local gate for behavior, online-search, tag, resource, or UI changes:

```powershell
.\scripts\Verify-Build.ps1 -RunSmokeTests
```

Before handoff or commit, run `git diff --check` and `codegraph sync`. Do not claim
validation without current command output. Provider tests must inject recorded HTTP
responses; they must not depend on live services.

## Code Intelligence and Git

Use CodeGraph before raw search when locating symbols or callers. Use GitNexus
`query`/`context` for execution flows and run `impact({target, direction: "upstream"})`
before editing a symbol. Warn before proceeding when the impact risk is HIGH or
CRITICAL. Run GitNexus `detect_changes()` before committing and confirm that the
reported scope matches the intended files.

Keep commits small and use Conventional Commit prefixes such as `fix:`, `feat:`,
`refactor:`, and `docs:`. Stage explicit paths only. Do not stage `.claude/`, `.mcp.json`,
`.repowise/`, `cctortest/`, `.serena/`, screenshots, or unrelated generated/user files.

## QQ QRC Lyrics

QQ lyric loading requests QRC first and falls back to the legacy Base64 LRC endpoint.
`QqQrcDecoder` decrypts the QQ payload and converts QRC to ordinary LRC. QRC contains
line timestamps (`[start,duration]`) and word timestamps (`(start,duration)`); the
current conversion keeps the line start and removes word markers. Therefore ordinary
LRC cannot retain complete per-word timing. With `LyricDownload_ReformatTimetag` off,
three-digit millisecond formatting is exact; with it on, the existing two-digit
formatting intentionally rounds to 10 ms.

The current precision investigation recommends a QQ-only policy of using the first
valid word start as the line timestamp, with the line timestamp as a malformed/missing
data fallback. Keep translation lines aligned and do not change global timestamp
formatting without a separate compatibility decision. Full word timing would require
an enhanced LRC/QRC representation and broader consumer support.

## Claude Cross-Review Prompt

Copy the following prompt into Claude when requesting an independent analysis. It is
intentionally analysis-only so the two reviews can be compared before implementation:

```text
你现在是 MusicTag 项目的独立代码审查员。仓库是 D:\\vibe\\tag，当前分支是
develop-net8。请只做分析，不修改文件、不提交代码。

请重点调查 QQ 歌词 QRC 转普通 LRC 的毫秒精度问题，目标歌曲 ID 是 678268984。
不要直接接受任何现成结论，先用 CodeGraph/GitNexus 追踪：

1. QqMusicTagProvider.LoadLyrics -> ParseQrcLyricResponse -> QqQrcDecoder.ConvertToLineLyric
   -> LyricTextProcessor.FormatTimestamp 的真实调用链和所有调用者。
2. 解密后的 QRC 中行级 `[start,duration]` 与逐字 `(start,duration)` 的语义和精度，
   尤其统计该歌曲两类时间的毫秒末位分布。
3. `LyricDownload_ReformatTimetag` 开关在 QQ、普通歌词解析、翻译合并和最终保存路径中
   的实际行为，确认是否存在二次重排或精度丢失。
4. 比较三种方案：保持行级时间、使用每行首个有效字时间、保留 QRC/增强 LRC 逐字格式。
   评估精度、普通播放器兼容性、翻译对齐、异常数据回退、设置兼容性和测试成本。
5. 给出推荐方案、明确的时间选择/回退算法、需要新增的 characterization fixtures，以及
   可能受影响的文件和风险等级。不得把“看起来能通过”的测试当成真实验证；若联网获取
   QQ 数据，说明请求接口和实际返回数据依据。

请按“事实证据 -> 当前根因 -> 方案对比 -> 推荐设计 -> 风险与验证缺口”的结构输出，
并附上具体文件和行号。最后列出你与另一位审查员最可能产生分歧的判断点，供交叉对比。
```

## Handoff Notes

For UI/DPI, provider, tag-write, resource, or interop changes, include the exact
verification command and a short manual-risk note. Do not package an already-run
`bin` directory; release packaging requires a clean rebuild and a scan for logs,
caches, settings, backups, and debug symbols.
