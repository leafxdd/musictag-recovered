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

## Handoff Notes

For UI/DPI, provider, tag-write, resource, or interop changes, include the exact
verification command and a short manual-risk note. Do not package an already-run
`bin` directory; release packaging requires a clean rebuild and a scan for logs,
caches, settings, backups, and debug symbols.
