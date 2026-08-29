# Repository Guidelines

## Repository and Branch Awareness

This repository restores and maintains the MusicTag Windows WinForms desktop app from recovered/decompiled sources. Preserve observable behavior by default and make changes in small, reviewable batches.

Always inspect `git branch --show-current`, the project files, and `scripts/Verify-Build.ps1` before assuming a framework or workflow. As of the 2026-08-03 x64 migration, the active checkout is `develop-net8`:

- `develop-net8` targets `net8.0-windows` x64 and contains the Per-Monitor V2 DPI work plus the ID3 write-policy options.
- `develop` remains the separate .NET Framework 4.8.1 development line. Do not casually merge framework-specific fixes between the two branches.
- `main` is a curated orphan release snapshot, not the normal development branch. Never push ordinary development history to it.

The root `AGENTS.md` and `CLAUDE.md` are the committed repository guidance. `.claude/`, `.mcp.json`, `.repowise/`, `cctortest/`, and `.serena/` remain local tooling or guidance and must not be staged. Preserve unrelated untracked or modified user files.

## Project Structure and Source of Truth

Open the repository through `MusicTag.sln`. It currently contains:

- `src/MusicTag/MusicTag.csproj`: the WinForms application.
- `src/MusicTag.Tests/MusicTag.Tests.csproj`: an x64 console characterization-test harness with hand-written assertions and no third-party test framework.

Recovered namespaces under `src/MusicTag/` do not reliably describe logical ownership. WinForms UI, online providers, serialization, and interop types are scattered across legacy namespace/folder boundaries. Locate code by type or symbol, not by guessing a folder.

Runtime assets in `src/MusicTag/musictag/` are required and copied by the project, including managed dependencies, SQLite interop/data files, satellite resources, and fonts. The managed `System.Data.SQLite.dll 1.0.113.0` is paired with the official x64 `SQLite.Interop.dll` of the same version; a bitness or version mismatch fails at runtime. Do not remove or replace these assets without verifying startup, resource loading, tag I/O, and packaging.

Maintenance records live in `docs/`; build and investigation output belongs in `artifacts/`. Some documentation describes the `develop`/net481 line and can be stale for `develop-net8`. When facts conflict, the active `.csproj`, source, verification script, and current Git history win.

## Code Discovery

Use CodeGraph before raw text search when locating symbols, callers, or blast radius:

```powershell
codegraph explore "<question>"
codegraph node <symbol-or-file>
```

Verify any index-derived conclusion against current source when the tool reports stale or approximate data. Use `rg` after CodeGraph for exact strings, resource keys, event subscriptions, generated names, or cases CodeGraph cannot answer. Run `codegraph sync` after code changes.

## Build and Validation

The standard local regression gate is:

```powershell
.\scripts\Verify-Build.ps1 -RunSmokeTests
```

On `develop-net8`, this builds Debug and Release, verifies the main/test/SQLite native PEs are AMD64, runs the characterization executable (including a real SQLite open/query), constructs key dialogs through the test suite, launches the Release app, and scans startup logs for fatal exceptions. The expected application output is:

```text
src/MusicTag/bin/Release/net8.0-windows/MusicTag.exe
```

For the release-only CI-shaped pass:

```powershell
.\scripts\Verify-Build.ps1 -Configurations Release
```

For a focused characterization probe:

```powershell
dotnet build .\src\MusicTag.Tests\MusicTag.Tests.csproj -c Release
.\src\MusicTag.Tests\bin\Release\net8.0-windows\MusicTag.Tests.exe
```

For behavior-preserving work, run the relevant characterization case before the edit when practical, capture the current output, then run it again afterward. Add a golden case first when changing an uncovered parser, provider, tag path, or pure extraction. Provider tests should inject recorded responses by overriding the existing protected virtual HTTP methods; tests must not depend on live network services.

Before finishing code changes, run:

```powershell
git diff --check
codegraph sync
```

UI, DPI, online search, tag writing, resource loading, and interop changes also need focused manual-risk notes because the automated suite cannot cover every live workflow.

## Coding Style and Stable Contracts

Follow `.editorconfig`: UTF-8, CRLF, final newline, no trailing whitespace, tabs of width 4 for C#, and two-space indentation for project, solution, and Markdown files.

Prefer small behavior-preserving edits. Avoid broad renames or mechanical rewrites unless every caller and persisted contract can be verified. Keep these stable unless the task explicitly changes the contract:

- JSON keys and settings names, including attributes such as `[JsonProperty]`.
- Explicit enum ordinals such as `SearchSource`, because persisted configuration depends on them.
- Legacy `ResourceManager` base names, `.resx` keys, and satellite resource names. Several renamed forms intentionally load resources under their original decompiled type names.
- Win32 `DllImport` entry points and ABI signatures.
- Existing public method signatures and optional trailing parameters used by batch or dialog callers.

Use readable PascalCase for types/methods/properties and follow the surrounding file's established field/local style. Do not enable nullable or add warning suppressions as a shortcut around recovered-code issues.

## Recovered-Code Safety Rules

- Preserve short-circuiting, exception behavior, side effects, and operand evaluation counts. Extracting `A && B[index]` into eager method arguments can introduce exceptions even when the returned Boolean is unchanged.
- Avoid global identical-block replacement during base-class extraction. It can delete the newly added base member or damage indentation. Prefer scoped edits, inspect every occurrence, and add the base implementation after removing derived duplicates when appropriate.
- When converging duplicate methods, retain a tested original entry point as a thin forwarding shell if that lets existing characterization tests verify argument mapping and the shared implementation.
- Collapsing multiple conditional WinForms property writes into one write is safe only after proving ordered data flow, same-value setter behavior, and the absence of observers for intermediate values.
- If a refactor exposes a latent defect, fix it only as an explicit behavior change. Document the exact behavior delta and update/add tests; never describe it as byte-for-byte equivalent.
- Do not transfer BCL assumptions between net481 and net8. Probe target-framework behavior for `Path`, WinForms, serialization, encoding, and other runtime-sensitive APIs.
- Generated fixture or agent analysis is not evidence by itself. Trace the actual fixture through the code and run it. Only claim validation that has corresponding command output from the current work session.

## Current High-Risk Areas

`StateFieldInstance` is the main large form and coordinates file lists, tag editing, batch work, history/undo, and much of the DPI layout. Treat it as a hotspot: use CodeGraph for callers and keep diffs narrowly scoped.

Online search flows through provider capability interfaces/factories and shared transport/status models. Preserve these invariants:

- A failed source or malformed response is logged and skipped; it must not abort the complete candidate list.
- Kugou has no cover-search capability; do not widen factory casts or switch coverage blindly.
- Result limits, `SearchSource` ordinals, optional status/reporting parameters, and batch-dialog call signatures are persisted or shared contracts.
- Characterization fixtures use recorded data and must exercise the intended parsing/search path, not merely produce a passing result.

All four lyric sources now decode a word-level format, and these are current high-risk parsing paths. Decoders are deliberately provider-local; `LyricTextProcessor.FormatTimestamp` is the only shared point. Do not unify them without a separate design decision.

- `QqMusicTagProvider` requests `music.musichallSong.PlayLyricInfo` QRC lyrics first (one short retry on throttling code 2001) and falls back to the legacy Base64 LRC endpoint when QRC is unavailable or malformed.
- `QqQrcDecoder` uses the QQ-compatible 3DES/zlib decoder adapted from `jitwxs/163MusicLyrics`; the Apache-2.0 attribution is recorded in `docs/THIRD_PARTY_NOTICES.md` and `docs/licenses/Apache-2.0-jitwxs-163MusicLyrics.txt`. Decryption is locked by a real-ciphertext golden vector; do not regenerate that vector with the self-encrypting fixture.
- QRC line timestamps use `[start,duration]`; word timestamps use `(start,duration)`. The ordinary-LRC conversion keeps the line start and removes word markers; a malformed line start falls back to the first parseable word start, and LRC-style metadata tags between timed lines are stripped. Full per-word timing is still not retained.
- `NetEaseMusicTagProvider` reads the plaintext `api/song/lyric/v1` endpoint. `NetEaseYrcDecoder` converts YRC to line LRC and drops JSON credit records; the provider honors boolean `nolyric`/`uncollected` flags. YTLRC translations align per line with unmatched lines dropped; plain tlyric alignment stays strict.
- `KugouTagProvider` prefers `lyrics.kugou.com/search` + `download?fmt=krc` over the legacy `m3ws` endpoint. `KugouKrcDecoder` strips the four-byte `krc1` magic, XORs with a fixed 16-byte key and inflates. KRC word timestamps are **relative to the line start**, unlike the absolute times in QRC/YRC. `contenttype` is 0 for KRC and 1 for plain LRC; the value 2 that public implementations describe has never been observed live.
- KRC frequently carries no `[language:]` translation while the legacy `landata` does — 64 of 93 translated songs measured. A KRC success with an empty translation therefore still fetches `landata` and aligns it by line order. That is sound because the two channels describe the same lyric: line counts matched in all 93 songs, and the text sequence was verified identical on spot-checked songs. An optional translation failure must never downgrade the high-precision lyric or surface a transport error; clear it explicitly, because `GetResponseStringResult` records every result.
- `KuwoTagProvider` prefers `newlyric.kuwo.cn`, whose **entire query string** is XOR-`yeelion`'d then Base64'd. Plaintext probes of that endpoint fail and prove nothing about capability. `KuwoLrcxDecoder` inflates, un-Base64s, XORs again and decodes **GB18030**, so the code-page provider registered at the top of `Program.Main` is a hard dependency. LRCX encodes translations as duplicate timestamps: at one timestamp the **first** entry is the previous line's translation and the second is the current original. `KuwoLegacyLyricAssembler` keeps the old `songinfoandlrc` heuristics for the fallback only; LRCX must not reuse them.
- Kuwo's legacy `songinfoandlrc` serves roughly 32% of songs against 97% for LRCX and adds no coverage over it, so treat that fallback as cheap defence rather than a safety net, and do not expect to reproduce it by hand.
- `KugouKrcDecoder` and `KuwoLrcxDecoder` both keep `ti/ar/al/by/offset` and drop provider-private tags. `[kuwo:xxx]` is the word-timing obfuscation parameter and must never reach the LRC. Metadata is written to the main lyric only, never repeated in the translation.
- With `LyricDownload_ReformatTimetag` disabled, three-digit milliseconds are emitted without rounding. With it enabled, the existing two-digit output intentionally rounds to 10 ms. Do not change global `FormatTimestamp` behavior without a separate compatibility decision.

Lyric conversion changes must add recorded characterization cases covering word-marker handling, malformed timestamps, formatting settings, translated-line alignment, and no-lyric flags. Assert on exact output where the shape matters: the two new decoders were able to drift into opposite metadata behavior precisely because every existing assertion used `Contains`. Negative conclusions drawn from probing a live endpoint are weak evidence — reproduce the provider's exact request headers and check open-source implementations before recording one. Background and open items live in `docs/QRC_PRECISION_REVIEW_2026-07.md`, `docs/LYRIC_FETCH_REPAIR_REPORT_2026-07.md`, and `docs/KUWO_KUGOU_HIGH_PRECISION_LYRIC_IMPLEMENTATION_REPORT_2026-08.md`.

Tag writes converge through `ConfigDescriptorState.SaveWithId3v2Version`, which calls the write body, `ApplyTagWritePolicy`, version selection, and `tagFile.Save()` under a shared lock. Keep new save paths inside this funnel. The current settings are:

- `RemoveMisplacedId3OnSave`: default true; removes ID3v1/v2 from FLAC before save.
- `RemoveId3v1OnSave`: optional removal of ID3v1.
- `KeepExistingId3v2Version`: preserves an existing ID3v2 version while new tags still use the selected default.

When changing tag behavior, verify physical round trips for representative MP3 and FLAC fixtures, not only in-memory tag objects.

## Per-Monitor DPI Rules for `develop-net8`

The active application calls `Application.SetHighDpiMode(HighDpiMode.PerMonitorV2)` before creating handles. The accepted direction is clarity-first Per-Monitor V2; do not downgrade to `SystemAware` unless the user explicitly chooses that fallback.

Preserve the established DPI model:

- Relative assets such as column widths and bitmaps use a DPI ledger and ratio scaling to prevent compounding.
- Absolute corrections such as explicit font sizes and geometry are reapplied even when the recorded DPI equals `DeviceDpi`; nested or deferred framework scaling can leave stale state after a net-zero transition.
- Do not rely on assigning a property to trigger `SizeChanged` or another event; if correctness needs recalculation, call the recalculation method explicitly.
- Child controls may retain WinForms `ScaledControlFont` state even after the form font is corrected. Verify child fonts, ToolStrip-hosted controls, row heights, column widths, split-panel minimums, and status-strip height.
- Test both startup-on-secondary-monitor and cross-monitor drag/round-trip paths. They are distinct WinForms paths and have produced different bugs.
- Existing user settings may contain column widths written by older experimental builds. Inspect the stored DPI stamp and compare against a reset configuration before diagnosing a new regression.

Set `MUSICTAG_DPI_TRACE=1` to write `dpi-trace.log` beside the executable. The local ignored `cctortest/` directory may contain monitor-driving and screenshot helpers; treat them as diagnostic aids, not committed product assets.

## Commits, Releases, and Packaging

Use concise Conventional Commit prefixes such as `fix:`, `feat:`, `refactor:`, and `chore:`. Keep each commit to one behavior or cleanup unit. Do not assume the obsolete direct-to-`master` workflow from older notes; verify the active branch and only commit or push when the current task authorizes it.

The public repository is `github.com/leafxdd/musictag-recovered`. Public `main` is maintained through a specialized clean orphan-snapshot release flow and intentionally differs from development branches. Perform that flow only when explicitly requested.

Never package a previously run `bin` directory: it can contain logs, cover caches, backup settings, or credentials/cookies. Release packaging requires a clean rebuild and an explicit scan for `.pdb`, `temp/`, logs, caches, backup files, and populated user configuration.

PR or handoff notes should state the user-visible change, exact validation commands run, and any untested manual-risk areas. Include screenshots only for visible UI changes.

<!-- gitnexus:start -->
# GitNexus — Code Intelligence

This project is indexed by GitNexus as **musictag-recovered** (4569 symbols, 13898 relationships, 393 execution flows).

> Index stale? Run `node .gitnexus/run.cjs analyze --index-only` from the project root — it auto-selects an available runner. No `.gitnexus/run.cjs` yet? Bootstrap with `npx`, `bunx`, or `pnpm dlx` — e.g. `bunx gitnexus@latest analyze` (npm 11 npx crash; #1939).

## Always Do

- **MUST run impact analysis before editing.** Use `impact({target: "symbolName", direction: "upstream"})` (MCP) or `node .gitnexus/run.cjs impact "symbolName" --direction upstream --repo .` (CLI fallback); report callers, processes, and risk. Never substitute grep for graph analysis.
- **MUST analyze graph changes before committing.** Use `detect_changes({scope: "all"})` (MCP) or `node .gitnexus/run.cjs detect-changes --scope all --repo .` (CLI fallback). `partial: true` or `truncated: true` is not a clean check — a zero means unseen, not unaffected; re-run it. For regression review: `detect_changes({scope: "compare", base_ref: "main"})` or `node .gitnexus/run.cjs detect-changes --scope compare --base-ref "main" --repo .`.
- **MUST warn the user** if impact analysis returns HIGH or CRITICAL risk before proceeding with edits.
- **MUST treat `risk: UNKNOWN` as unresolved, not as low.** An empty caller set is not evidence the symbol is unused — it can also mean the callers are not resolvable by the index (plain-object property access, dynamic dispatch, cross-language calls). `impact` pairs `UNKNOWN` with a `riskNote` saying so. Confirm with a text search before treating the symbol as safe to change or delete; do not proceed on the strength of a zero.
- When exploring unfamiliar code, use `query({search_query: "concept"})` to find execution flows instead of grepping. It returns process-grouped results ranked by relevance.
- When you need full context on a specific symbol — callers, callees, which execution flows it participates in — use `context({name: "symbolName"})`.
- For security review, `explain({target: "fileOrSymbol"})` lists taint findings (source→sink flows; needs `analyze --pdg`).

## Never Do

- NEVER edit a function, class, or method before MCP/CLI impact analysis.
- NEVER ignore HIGH or CRITICAL risk warnings from impact analysis, and never read `UNKNOWN` as an all-clear — it means the walk could not answer, which is the one verdict that requires confirming by other means.
- NEVER rename symbols with find-and-replace — use `rename` which understands the call graph.
- NEVER commit before MCP/CLI graph change analysis.

## Resources

| Resource | Use for |
| --- | --- |
| `gitnexus://repo/musictag-recovered/context` | Codebase overview, check index freshness |
| `gitnexus://repo/musictag-recovered/clusters` | All functional areas |
| `gitnexus://repo/musictag-recovered/processes` | All execution flows |
| `gitnexus://repo/musictag-recovered/process/{name}` | Step-by-step execution trace |

## CLI

| Task | Read this skill file |
| --- | --- |
| Understand architecture / "How does X work?" | `.claude/skills/gitnexus-exploring/SKILL.md` |
| Blast radius / "What breaks if I change X?" | `.claude/skills/gitnexus-impact-analysis/SKILL.md` |
| Trace bugs / "Why is X failing?" | `.claude/skills/gitnexus-debugging/SKILL.md` |
| Rename / extract / split / refactor | `.claude/skills/gitnexus-refactoring/SKILL.md` |
| Tools, resources, schema reference | `.claude/skills/gitnexus-guide/SKILL.md` |
| Index, status, clean, wiki CLI commands | `.claude/skills/gitnexus-cli/SKILL.md` |

<!-- gitnexus:end -->
