# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this repo is

`MusicTag` is a **decompiled-and-recovered** .NET Framework 4.6.1 WinForms desktop app (a local
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
- Output: `src/MusicTag/bin/Release/net461/MusicTag.exe`. MSBuild logs → `artifacts/`.
- Standard post-change pass: `.\scripts\Verify-Build.ps1 -RunSmokeTests`, then `git diff --check`,
  then `codegraph sync`.

## Architecture (big picture)

Single WinExe, `net461`, x86 (`Prefer32Bit`). Entry point `MusicTag.Schemes/Program.cs`
(`Program.Main`): single-instance guard (enumerates windows, forwards args via `WM_COPYDATA`) →
`Application.Run(new StateFieldInstance(args))`.

- **`StateFieldInstance`** (`MusicTagWinApp.Instances/StateFieldInstance.cs`, ~4500 lines) — the main
  form and de-facto god object: file list, tag editor, toolbar/menus, and dispatch of all batch
  operations (auto-match, rename, save/undo tags, extract covers, save LRC).
- **Native tag I/O** — `MusicTag.States/ConfigDescriptorState.cs` reads/writes tags and embedded
  pictures from audio files via P/Invoke into the bundled native `MusicTag.dll` (plus `MediaInfo.dll`).
  The DLL exports are obfuscated single/double-letter `EntryPoint` strings (`"bb"`, `"zzz"`, `"d"`…) —
  **never rename those `EntryPoint` values.**
- **`DatabaseMapper`** (`MusicTagWinApp.Instances/DatabaseMapper.cs`) — shared utility hub: DPI scaling,
  resource-bitmap loading, image resize/save, AES decrypt, URL encoding, temp/log cleanup, error
  dialogs, exception logging.
- **Tag history / undo** — SQLite (`System.Data.SQLite`) via `TagHistoryRepository`
  (`MusicTagWinApp.Listeners/`); `MusicTag.db` ships alongside the exe.

### Online metadata search subsystem (the part most worth understanding)

This is where "联网搜索" bugs live. The flow: search dialog → per-source provider → result models →
ranked candidate list shown to user → write-back to file tags.

- **`RemoteTagProviderBase`** (`MusicTag.Serialization/`) — abstract base for every online provider.
  Supplies shared HTTP (`GetResponseString`/`GetResponseBytes`/`PostString`/`DownloadToStream`, all
  **synchronous over `.Result`**), a shared `CancellationTokenSource`, and a cover-downloader factory.
  Subclasses implement `CreateHttpClient()` and `GetSource()`.
- **`SearchSource`** enum (`MusicTagWinApp.Web/`) — the 10 sources: `Music163, QQ, Xiami, Kugou,
  MiniLyrics, ITunes, Lastfm, Brainz, Vgmdb, Kuwo`. `[Description]` gives the display name.
- **Providers** (one class each): `NetEaseMusicTagProvider`, `QqMusicTagProvider`, `XiamiTagProvider`,
  `KugouTagProvider`, `KuwoTagProvider`, `ItunesTagProvider`, `MusicBrainzTagProvider`,
  `VgmdbTagProvider`, `LastfmCoverProvider`, `MiniLyricsProvider`. They expose `SearchTracks`,
  `SearchLyrics`/`LoadLyricForTrack`, and/or `SearchCovers`.
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
- **Some file names don't match the type inside** (see `docs/DECOMPILATION_NOTES.md`): e.g.
  `BaseFieldInstance.cs` → `LyricEditorDialog`, `EventRulesSchema.cs` → `FilenameRelatedBatchDialog`,
  `Template.cs` → `CustomToolStripRenderer`. File renames are done as separate, isolated passes.
- **Do not force-rename or rewrite** (high risk, evidence required first): `PrivateImplementationDetails.cs`
  (compiler-generated data), `PolicyTokenExporter.cs` (empty shell referenced by a resource key), the
  remaining `_003C…_003Ed__*` async state machines in `StateFieldInstance.cs` /
  `CombinedTagSearchDialog.cs`, the large `goto`/`switch` `InitializeComponent` in `StateFieldInstance.cs`,
  the encoding-detection table init in `Tokenizer.cs`, and the obfuscated native `EntryPoint` strings.
- When a decompiled name's meaning is unclear, **keep it** (or use a neutral name) and record it in
  `docs/DECOMPILATION_NOTES.md` — don't guess a rename.
- **Persisted names must stay stable**: keep old JSON keys via `[JsonProperty]` (e.g. `SourceItem`'s
  `Src`/`Seq`/`IsOther`/`WebSearchItemsLimit`) and be careful renaming types tied to `.resx` resource
  keys, so existing user config and resource lookups keep working.
- **Two `musictag` locations, opposite meaning**: `src/MusicTag/musictag/` holds **required** runtime
  assets copied to output (native `MusicTag.dll`, `MediaInfo.dll`, `SQLite.Interop.dll`, `MusicTag.db`,
  `MusicTag.dat`, `en`/`zh-CHS`/`zh-CHT` resource DLLs, FontAwesome ttf) — keep it. Root `/musictag/` is
  ignored loose extraction. `tools/` (de4dot, dnSpy, ILSpy, die) is an ignored local RE toolbox.
- Style (`.editorconfig`): **tabs** in `.cs` (size 4), 2-space in `csproj`/`sln`/`md`; CRLF; UTF-8;
  final newline. Build suppresses decompiler-noise warnings `CS0162;CS0414;CS0649` via `NoWarn`.

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

### Migration TODO
- Phase 2: capture the `net461` baseline build (errors/warnings) via `scripts\Verify-Build.ps1`.
- Phase 3: write the concrete migration plan (file changes, dependency strategy, validation).
- Phases 4–8: retarget → fix dependencies → clean/compile → verify → finalize docs.
