# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this repo is

`MusicTag` is a **decompiled-and-recovered** .NET Framework WinForms desktop app (a local
music-tag/lyrics/cover editor with online metadata search). The goal is **not** a rewrite: keep the
original runtime behavior intact while incrementally turning de4dot/decompiler output into readable
code. Treat every change as behavior-preserving unless a build warning or runtime error proves the
decompiler emitted invalid logic.

Read `docs/MAINTENANCE.md` (refactoring policy + full cleanup changelog) and
`docs/DECOMPILATION_NOTES.md` (things deliberately left alone) before non-trivial work.

## Build, verify, run

There is **no unit-test suite**. Verification = build both configs + two smoke tests. Use the script,
not raw `msbuild` (it auto-resolves MSBuild via PATH → `vswhere` → known Build Tools paths):

```powershell
.\scripts\Verify-Build.ps1                 # build Debug + Release
.\scripts\Verify-Build.ps1 -RunSmokeTests  # + smoke tests (do this after changes)
.\scripts\Verify-Build.ps1 -Configurations Release   # Release only (what CI runs)
```

- Smoke tests: (1) reflectively construct `MusicTag.Schemes.FilenameRelatedBatchDialog`, (2) launch
  `MusicTag.exe` and confirm it stays alive ~5s. Adding a new top-level dialog or breaking startup
  fails these.
- Output: `src/MusicTag/bin/Release/net481/MusicTag.exe`. MSBuild logs → `artifacts/`.
- Standard post-change pass: `.\scripts\Verify-Build.ps1 -RunSmokeTests`, then `git diff --check`,
  then `codegraph sync`.

## Architecture (big picture)

Single WinExe, `net481` (.NET Framework 4.8.1, migrated from 4.6.1 — see migration log below), x86
(`Prefer32Bit`). Entry point `MusicTag.Schemes/Program.cs`
(`Program.Main`): single-instance guard (enumerates windows, forwards args via `WM_COPYDATA`) →
`Application.Run(new StateFieldInstance(args))`.

- **`StateFieldInstance`** (`MusicTagWinApp.Instances/StateFieldInstance.cs`, ~4500 lines) — the main
  form and de-facto god object: file list, tag editor, toolbar/menus, and dispatch of all batch
  operations (auto-match, rename, save/undo tags, extract covers, save LRC).
- **Tag I/O** — `MusicTag.States/ConfigDescriptorState.cs` reads/writes tags and embedded pictures.
  As of **Stage A** of the native-dependency removal (see `docs/NATIVE_DEPENDENCY_REMOVAL_PLAN.md`)
  this runs on managed **TagLibSharp** (referenced via HintPath `musictag/TagLibSharp.dll`); the class
  keeps its original public API / field vocabulary so all ~30 `StateFieldInstance` call sites are
  unchanged. The old P/Invoke into native `MusicTag.dll` for tags is gone — which is why the native
  DLL's anti-tamper gate no longer matters and its 3-byte patch was **reverted**. Stage B then removed
  the DLL entirely (see the next bullet and `docs/DECOMPILATION_NOTES.md`).
- **Native `MusicTag.dll` — fully removed (Stage A + B complete).** The app no longer P/Invokes any
  `MusicTag.dll` export: tag I/O runs on TagLibSharp (Stage A), and the online subsystem's former native
  pieces are managed too (Stage B) — endpoint URL/header constants hardcoded into the providers, NetEase
  weapi/163-key crypto re-implemented in `NetEaseCrypto` (`MusicTag.Serialization/`), and encoding
  detection on the managed **UtfUnknown** charset library. `MusicTag.dll` and its internal dependency
  `MediaInfo.dll` were deleted from `musictag/` (commit `ba3844e`; original preserved in git history +
  `artifacts/MusicTag.dll.orig`). The only `DllImport`s left are standard Win32
  (`user32`/`shell32`/`uxtheme`). See `docs/NATIVE_DEPENDENCY_REMOVAL_PLAN.md` §13.
- **`DatabaseMapper`** (`MusicTagWinApp.Instances/DatabaseMapper.cs`) — shared utility hub: DPI scaling,
  resource-bitmap loading, image resize/save, AES decrypt, URL encoding, temp/log cleanup, error
  dialogs, exception logging.
- **HiDPI**: the process is **System DPI Aware**, declared in the embedded `src/MusicTag/app.manifest`
  (`<dpiAware>true</dpiAware>` + `<dpiAwareness>system</dpiAwareness>` + `<supportedOS>` for Win7–11,
  embedded via `<ApplicationManifest>` in the csproj). The UI scales once at startup through
  `AutoScaleMode.Dpi` + `DatabaseMapper.GetDpiScale`/`ScaleByDpi` (the scale is cached → scale-once,
  not per-monitor). Don't re-add a runtime `SetProcessDpiAwareness` call — the manifest is the single
  source of truth. (A missing DPI manifest is what made the recovered app run DPI-unaware, so Windows
  bitmap-stretched the window and text/icons looked blurry above 100% scaling.)
- **Tag history / undo** — SQLite (`System.Data.SQLite`) via `TagHistoryRepository`
  (`MusicTagWinApp.Listeners/`); `MusicTag.db` ships alongside the exe.

### Online metadata search subsystem (the part most worth understanding)

This is where "联网搜索" bugs live. The flow: search dialog → per-source provider → result models →
ranked candidate list shown to user → write-back to file tags.

- **`RemoteTagProviderBase`** (`MusicTag.Serialization/`) — abstract base for every online provider.
  Supplies shared HTTP (`GetResponseString`/`GetResponseBytes`/`PostString`/`DownloadToStream`, all
  **synchronous over `.Result`**), a shared `CancellationTokenSource`, and a cover-downloader factory.
  Subclasses implement `CreateHttpClient()` and `GetSource()`.
- **`SearchSource`** enum (`MusicTagWinApp.Web/`) — the 4 sources: `Music163, QQ, Kugou, Kuwo`.
  `[Description]` gives the display name. Members keep **explicit ordinals** (`Music163=0, QQ=1,
  Kugou=3, Kuwo=9`) so existing persisted `SourceItem` JSON still matches after the six retired
  sources (Xiami/MiniLyrics/iTunes/Last.fm/MusicBrainz/VGMdb) were removed.
- **Providers** (one class each): `NetEaseMusicTagProvider`, `QqMusicTagProvider`, `KuwoTagProvider`
  (each does tracks + lyrics + covers), and `KugouTagProvider` (lyrics + lyric-candidate track
  search only). They expose `SearchTracks`, `SearchLyrics`/`LoadLyricForTrack`, and/or `SearchCovers`.
- **Result models** carry `SearchSource` + ordering (`ResultOrder`/`SearchPass`/`SourceOrder`) +
  similarity scores, and use **deferred loaders** (delegates) for lazy cover download / lazy lyric
  fetch: `TrackSearchResult` (`MusicTagWinApp.Roles/`), `LyricSearchResult`
  (`MusicTagWinApp.Adapter/`), `CoverSearchResult`.
- **Source dispatch** is a `switch (SearchSource) { case …: new XProvider(…) }` repeated in the search
  dialogs: `CombinedTagSearchDialog` (`MusicTag.Mocks/`), `LyricSearchDialog`
  (`MusicTagWinApp.Exporters/`), `CoverSearchDialog` (`MusicTagWinApp.Listeners/`), and
  `AutoMatchTagsDialog` (`MusicTagWinApp.Adapter/`, batch). Adding a source means editing each switch.
- **Per-source enable/order/limit** = `SourceItem` (`MusicTagWinApp.Web/`), persisted as JSON via
  `GetTagSourceSettings`/`GetLyricSourceSettings`/`GetCoverSourceSettings`.
- **Resilience invariant** (recurring fix in the changelog): a single failing source or malformed JSON
  must be **logged and skipped**, never abort the whole candidate list. Tolerate missing/non-array/
  non-object fields when parsing provider responses; route numeric reads through tolerant helpers
  rather than `JToken.Value<T>()`.

## Conventions specific to this (decompiled) codebase

- **Folder name ≠ logical grouping.** Directories (`MusicTagWinApp.Adapter`, `MusicTag.Mocks`,
  `MusicTag.Candidates`, `MusicTagWinApp.Writers`, …) are leftovers from the original obfuscated module
  split and group classes almost arbitrarily — providers, dialogs, and models are scattered across
  them. Namespace matches the folder, but neither tells you what's inside. **Locate code by type name
  via CodeGraph (`.codegraph/` exists — use it before grep/Read), not by folder.**
- **A file name may not signal the type inside.** The three known decompiler-era misnamed files have
  since been renamed to match their types (`BaseFieldInstance.cs`→`LyricEditorDialog.cs`,
  `EventRulesSchema.cs`→`FilenameRelatedBatchDialog.cs`, `Template.cs`→`CustomToolStripRenderer.cs` — the
  last later deleted as dead code; see `docs/DECOMPILATION_NOTES.md`). Still locate code by type name, not
  by file or folder, and do any further file renames as separate, isolated passes.
- **Rewriting obfuscated / decompiler-emitted artifacts into readable code is allowed — but only when it
  stays strictly behavior-equivalent**, proven by a clean build + smoke tests and a careful read of the
  diff. The large historical blocks this covered have all been reconstructed: every `goto`/`switch`
  `InitializeComponent` designer state machine (`StateFieldInstance.cs`, `OptionsDialog.cs`,
  `FilenameRelatedBatchDialog.cs`), every `_003C…_003Ed__*` async state machine (the 12 in
  `StateFieldInstance.cs` + 3 in `CombinedTagSearchDialog.cs`), and the `Tokenizer.cs` encoding-detection
  table init (whose unused `EncodingDetector` scorer was then removed entirely). Treat the rule as a
  standing policy for any artifact that resurfaces: prefer the smallest equivalent form, and when a change
  can live in hand-written code, add it there rather than reshaping a generated block.
- **P/Invoke `EntryPoint` strings are real export names, not obfuscation to undo** — renaming one breaks
  the binding. The obfuscated single/double-letter exports of native `MusicTag.dll` (`"bb"`, `"zzz"`,
  `"d"`…) this once warned about are **gone** (Stage B deleted the DLL); the only `DllImport`s left are
  standard Win32 (`user32`/`shell32`/`uxtheme`), whose `EntryPoint`s are OS API names — likewise never
  change them (same spirit as the persisted JSON keys and `.resx` keys below).
- When a decompiled name's meaning is unclear, **keep it** (or use a neutral name) and record it in
  `docs/DECOMPILATION_NOTES.md` — don't guess a rename.
- **Persisted names must stay stable**: keep old JSON keys via `[JsonProperty]` (e.g. `SourceItem`'s
  `Src`/`Seq`/`IsOther`/`WebSearchItemsLimit`) and be careful renaming types tied to `.resx` resource
  keys, so existing user config and resource lookups keep working.
- **Two `musictag` locations, opposite meaning**: `src/MusicTag/musictag/` holds **required** runtime
  assets copied to output (`TagLibSharp.dll`, `UtfUnknown.dll`, `SQLite.Interop.dll`, `MusicTag.db`,
  `MusicTag.dat`, `en`/`zh-CHS`/`zh-CHT` resource DLLs, FontAwesome ttf) — keep it. (The original native
  `MusicTag.dll`/`MediaInfo.dll` were removed in Stage B — see Architecture.) Root
  `/musictag/` is ignored loose extraction. `tools/` (de4dot, dnSpy, ILSpy, die) is an ignored local RE
  toolbox.
- Style (`.editorconfig`): **tabs** in `.cs` (size 4), 2-space in `csproj`/`sln`/`md`; CRLF; UTF-8;
  final newline. Build suppresses only `CS0649` via `NoWarn` (interop/deserialization/designer fields the
  compiler can't see assigned); `CS0162`/`CS0414` were dropped once cleanup reached zero of each.

## .NET Framework 4.8.1 migration (in progress)

**Goal:** retarget from .NET Framework 4.6.1 (`net461`) to 4.8.1 (`net481`), preserving functional
behavior (not line-for-line code). Driven phase-by-phase. `master` is the pre-migration rollback
point; all migration work happens on branch `migrate/net481`, one commit per phase.

### Phase log

**Phase 0 — baseline (2026-06-23) ✅**
- Repo is local-only (no git remote). Baseline commit `406ce91 Initial project recovery and cleanup`
  (the only commit). Working tree was clean except untracked `CLAUDE.md`, committed as this baseline.
- Solution/projects: `MusicTag.sln` + a single SDK-style project `src/MusicTag/MusicTag.csproj`
  (`net461`, `WinExe`, x86). **No test project; no `packages.config` / `PackageReference` /
  `nuget.config` / `Directory.Build.*` / `global.json`.**
- Dependencies are local `HintPath` DLLs under `src/MusicTag/musictag/` (Newtonsoft.Json,
  Fkosoft.FontAwesome4, System.Data.SQLite, System.ValueTuple) plus GAC framework references — so
  NuGet restore is effectively a no-op and there is no online package risk.
- Strategy: one commit per phase on `migrate/net481`; validate each phase's acceptance criteria
  before advancing; keep changes minimal and behavior-preserving.

**Phase 1 — build environment (2026-06-23) ✅ no blockers**
- MSBuild: `C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\MSBuild\Current\Bin\MSBuild.exe`
  (on PATH and via `vswhere`). `Verify-Build.ps1` resolves it automatically.
- **.NET Framework 4.8.1 Targeting Pack: installed** — `…\Reference Assemblies\Microsoft\Framework\
  .NETFramework\v4.8.1` has `RedistList\FrameworkList.xml` (".NET Framework 4.8.1") + 133 ref
  assemblies (incl. `mscorlib.dll`, `System.Windows.Forms.dll`). So `net481` will compile.
- .NET Framework 4.8.1 runtime: installed (NDP v4 Full `Release=533320`, `Version=4.8.09032`).
- NuGet restore: trivial (no package references); `dotnet` SDK `10.0.301` also present.
- Conclusion: environment fully supports retargeting to `net481` — safe to proceed.

**Phase 2 — net461 baseline build (2026-06-23) ✅ clean**
- Clean **Rebuild** of `MusicTag.sln` (Release, `Any CPU`) via VS Build Tools 18 MSBuild:
  **build succeeded, 0 errors, 0 warnings.** Verified it was a real compile (log shows `CoreClean`
  + `CoreCompile` invoking Roslyn `csc.exe` over all ~130 `.cs` files against the `v4.6.1` reference
  assemblies, `/out:…\net461\MusicTag.exe`); the first incremental run was a no-op, so a forced
  rebuild was used for the true baseline.
- Restore: no-op (no package references). Compiler already runs with `/nowarn:CS0162,CS0414,CS0649`.
- Classification: **no environment / dependency / code / config blockers.** The recovered code
  already compiles cleanly, so retargeting risk is low and Phase 6 should be minimal.
- (Build log written to gitignored `artifacts/`, not committed.)

**Phase 3 — migration plan (2026-06-23) ✅**

Every tracked `net461`/`4.6.1` reference was located and classified. The change set is small and
behavior-preserving:

*Required (functional):*
1. **Retarget (core)** — `src/MusicTag/MusicTag.csproj`: `<TargetFramework>net461</TargetFramework>`
   → `net481`. Single line; drives everything else.
2. **Runtime config** — `src/MusicTag/musictag/MusicTag.exe.config`:
   `sku=".NETFramework,Version=v4.6.1"` → `v4.8.1`. Keep `legacyCorruptedStateExceptionsPolicy`
   (global exception handlers rely on it) and the HighDPI appSetting. **No `bindingRedirect`s exist**,
   so none need maintaining.
3. **Build script** — `scripts/Verify-Build.ps1`: smoke-test output paths `bin\Release\net461\` →
   `net481\` (2 occurrences). Required or Phase 7 smoke tests break.

*Dependency strategy:*
- Local `HintPath` DLLs (Newtonsoft.Json, FontAwesome, SQLite) are framework-agnostic — keep as-is.
- **`System.ValueTuple` is the one watch item.** It is in-box since .NET 4.7, so under `net481` the
  explicit `<Reference Include="System.ValueTuple">` (→ `musictag/System.ValueTuple.dll`) may become
  redundant or trigger a duplicate-type conflict (CS0433 / CS1701). Plan: retarget first, rebuild,
  observe; if it warns/conflicts, **remove that Reference** (mscorlib then supplies the types) and
  confirm the bundled DLL is no longer needed at runtime.
- All framework references resolve from the v4.8.1 targeting pack automatically.

*Code-cleanup strategy:* baseline is already 0 errors / 0 warnings, so **expect no code changes.**
Touch code only if a `net481`-specific compile error appears; keep fixes isolated and
behavior-preserving; do not rewrite unclear decompiled logic (record TODOs instead).

*Validation strategy:* after each change, clean **Rebuild** (Debug + Release) and compare against the
0/0 baseline. Phase 7 runs `Verify-Build.ps1 -RunSmokeTests` and confirms `csc` now targets the
`v4.8.1` reference assemblies and the exe starts.

*Commit batches:* P4 `build: retarget projects to net481` (csproj + exe.config + Verify-Build.ps1) ·
P5 `build: fix dependencies for net481` (ValueTuple, only if needed) · P6 `fix:`/`refactor:` per
module (only if errors) · P7 `test: verify net481 migration` · P8 `docs: finalize net481 migration
notes` (sync README / MAINTENANCE / architecture prose; deferred so P4 stays focused).

**Phase 4 — retarget to net481 (2026-06-23) ✅ (build red as predicted; fixed in P5)**
- Applied: `MusicTag.csproj` `net461`→`net481`; `MusicTag.exe.config` sku `v4.6.1`→`v4.8.1`;
  `Verify-Build.ps1` smoke-test paths `net461`→`net481`.
- Clean Rebuild (`/restore /t:Rebuild`) confirms the retarget took effect: `csc` now references the
  **v4.8.1** reference assemblies and defines `NET481;NET48_OR_GREATER;NET481_OR_GREATER`, output to
  `…\net481\`.
- Surfaced exactly the predicted dependency conflict: **CS0433** — `ValueTuple` is defined in both the
  external `System.ValueTuple v4.0.3.0` and `net481` mscorlib (2 sites in `StateFieldInstance.cs`).
  Resolved in Phase 5 by removing the explicit reference.
- Note: `/t:Rebuild` alone first failed with `NETSDK1005` because it skips restore after a TFM change —
  must pass `/restore` (which `Verify-Build.ps1` already does).

**Phase 5 — dependency fix (2026-06-23) ✅ green**
- Removed the explicit `<Reference Include="System.ValueTuple">` from `MusicTag.csproj` (the type is
  in-box in net481 mscorlib). Clean Rebuild Debug + Release: **0 errors, 0 warnings** — matches the
  net461 baseline. `csc` no longer references the external `System.ValueTuple.dll`, and it is no
  longer copied to the output dir (correct — the framework supplies `ValueTuple`).
- Other bundled deps (Newtonsoft.Json, FontAwesome, SQLite) and all framework references resolve
  cleanly under net481; no `bindingRedirect`s needed.
- Leftover: `musictag/System.ValueTuple.dll` stays in the repo but is now unreferenced — a candidate
  for a later cleanup pass (left in place per the conservative policy).

**Phase 6 — decompiled-artifact cleanup / compile fixes (2026-06-23) ✅ no-op**
- The net481 build is already 0 errors / 0 warnings, so no compile fixes were required and the
  retarget needed no decompiled-code changes (behavior-preserving). General decompiler cleanup is a
  separate, ongoing effort (`docs/MAINTENANCE.md` changelog) and is intentionally out of scope here.

**Phase 7 — verify (2026-06-23) ✅ passed**
- `Verify-Build.ps1 -RunSmokeTests`: Debug + Release build green; smoke test 1 reflectively
  constructed `MusicTag.Schemes.FilenameRelatedBatchDialog` from the net481 exe; smoke test 2 launched
  `MusicTag.exe` and it stayed alive 5s (`StartedAndStayedAlive=True`) — so native `MusicTag.dll`
  P/Invoke, SQLite, FontAwesome and `MusicTag.exe.config` all load on the 4.8.1 runtime.
- Output exe carries the embedded `.NETFramework,Version=v4.8.1` TargetFramework attribute; its copied
  `MusicTag.exe.config` declares the v4.8.1 supportedRuntime; all runtime DLLs present;
  `System.ValueTuple.dll` correctly absent from output.
- Functional-equivalence basis: zero code changes beyond removing the ValueTuple reference + a clean
  compile against the v4.8.1 reference assemblies + the app constructs a key dialog and runs. **Not
  exercised:** live online-search providers (no automated tests exist — accepted risk per the plan).

**Phase 8 — finalize (2026-06-23) ✅**
- Synced prose docs to net481: `README.md` (overview note, env requirement, two build/run paths),
  `docs/MAINTENANCE.md` (target framework), and this file's architecture section.

### Migration result
- **Status: migrated to .NET Framework 4.8.1 successfully.** Clean Rebuild (Debug + Release) is green
  with 0 errors / 0 warnings (matches the net461 baseline); `Verify-Build.ps1 -RunSmokeTests` passes;
  the exe is marked `.NETFramework,Version=v4.8.1` and runs on the 4.8.1 runtime.
- Change footprint (no application/business code touched): `MusicTag.csproj` (TFM `net461`→`net481`,
  dropped the `System.ValueTuple` reference), `musictag/MusicTag.exe.config` (supportedRuntime sku),
  `scripts/Verify-Build.ps1` (output paths), plus docs.
- Key decisions: work on branch `migrate/net481`, one commit per phase, `master` = rollback point;
  removed the now-in-box `System.ValueTuple` reference to clear the CS0433 duplicate-type conflict.
- Known issues / follow-ups:
  - Live online-search providers were not exercised beyond app startup (no automated test suite).
  - `musictag/System.ValueTuple.dll` is now an unreferenced orphan — safe to delete in a later pass.
  - The broader decompiler-cleanup effort (`docs/MAINTENANCE.md`) continues independently of this
    migration.
