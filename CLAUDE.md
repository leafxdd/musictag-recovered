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

## Lyrics (QQ QRC / NetEase YRC)

Both sources prefer word-level lyrics and fall back to plain LRC. QQ requests QRC
first (one short retry on rate-limit code 2001) and falls back to the legacy Base64
LRC endpoint; `QqQrcDecoder` decrypts the payload (3DES + zlib, locked by a real
ciphertext golden vector — do not regenerate it with the self-encrypting fixture).
NetEase reads the plaintext `api/song/lyric/v1` endpoint; `NetEaseYrcDecoder`
converts YRC, dropping JSON credit records, and the provider honors boolean
`nolyric`/`uncollected` flags.

Conversion keeps the line start (`[start,duration]`) and removes word markers; a
malformed line start falls back to the first parseable word start, and LRC-style
metadata tags between timed lines are stripped. Ordinary LRC therefore cannot retain
complete per-word timing; that would require an enhanced representation and broader
consumer support. YTLRC translations align per line with unmatched lines dropped;
plain tlyric alignment stays strict. With `LyricDownload_ReformatTimetag` off,
three-digit millisecond formatting is exact; with it on, the two-digit formatting
intentionally rounds to 10 ms. Do not change global timestamp formatting without a
separate compatibility decision. History and open items:
`docs/QRC_PRECISION_REVIEW_2026-07.md`, `docs/LYRIC_FETCH_REPAIR_REPORT_2026-07.md`.

## Handoff Notes

For UI/DPI, provider, tag-write, resource, or interop changes, include the exact
verification command and a short manual-risk note. Do not package an already-run
`bin` directory; release packaging requires a clean rebuild and a scan for logs,
caches, settings, backups, and debug symbols.

<!-- gitnexus:start -->
# GitNexus — Code Intelligence

This project is indexed by GitNexus as **musictag-recovered** (4102 symbols, 12617 relationships, 300 execution flows). Use the GitNexus MCP tools to understand code, assess impact, and navigate safely.

> Index stale? Run `node .gitnexus/run.cjs analyze` from the project root — it auto-selects an available runner. No `.gitnexus/run.cjs` yet? `npx gitnexus analyze` (npm 11 crash → `npm i -g gitnexus`; #1939).

## Always Do

- **MUST run impact analysis before editing any symbol.** Before modifying a function, class, or method, run `impact({target: "symbolName", direction: "upstream"})` and report the blast radius (direct callers, affected processes, risk level) to the user.
- **MUST run `detect_changes()` before committing** to verify your changes only affect expected symbols and execution flows. For regression review, compare against the default branch: `detect_changes({scope: "compare", base_ref: "main"})`.
- **MUST warn the user** if impact analysis returns HIGH or CRITICAL risk before proceeding with edits.
- When exploring unfamiliar code, use `query({search_query: "concept"})` to find execution flows instead of grepping. It returns process-grouped results ranked by relevance.
- When you need full context on a specific symbol — callers, callees, which execution flows it participates in — use `context({name: "symbolName"})`.
- For security review, `explain({target: "fileOrSymbol"})` lists taint findings (source→sink flows; needs `analyze --pdg`).

## Never Do

- NEVER edit a function, class, or method without first running `impact` on it.
- NEVER ignore HIGH or CRITICAL risk warnings from impact analysis.
- NEVER rename symbols with find-and-replace — use `rename` which understands the call graph.
- NEVER commit changes without running `detect_changes()` to check affected scope.

## Resources

| Resource | Use for |
|----------|---------|
| `gitnexus://repo/musictag-recovered/context` | Codebase overview, check index freshness |
| `gitnexus://repo/musictag-recovered/clusters` | All functional areas |
| `gitnexus://repo/musictag-recovered/processes` | All execution flows |
| `gitnexus://repo/musictag-recovered/process/{name}` | Step-by-step execution trace |

## CLI

| Task | Read this skill file |
|------|---------------------|
| Understand architecture / "How does X work?" | `.claude/skills/gitnexus/gitnexus-exploring/SKILL.md` |
| Blast radius / "What breaks if I change X?" | `.claude/skills/gitnexus/gitnexus-impact-analysis/SKILL.md` |
| Trace bugs / "Why is X failing?" | `.claude/skills/gitnexus/gitnexus-debugging/SKILL.md` |
| Rename / extract / split / refactor | `.claude/skills/gitnexus/gitnexus-refactoring/SKILL.md` |
| Tools, resources, schema reference | `.claude/skills/gitnexus/gitnexus-guide/SKILL.md` |
| Index, status, clean, wiki CLI commands | `.claude/skills/gitnexus/gitnexus-cli/SKILL.md` |

<!-- gitnexus:end -->
