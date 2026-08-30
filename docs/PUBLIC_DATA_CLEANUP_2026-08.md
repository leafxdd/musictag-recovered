# Public Data Cleanup 2026-08

## Scope

The public Git history previously contained installation-specific `MusicTag.db`
and `MusicTag.dat` files. They included tag-history records, local file paths,
and file-list state. The public cleanup removes both paths from every public
branch and version tag history.

The old release ZIP assets for `v1.0.9-net481`, `v1.0.10-net481`, and
`v1.0.11-net481` also contain these files and are scheduled for asset deletion.
Release pages and their notes remain available.

## Runtime behavior

Both files are per-installation state, not application binaries:

- `TagHistoryRepository` creates the SQLite database and schema when the file
  is absent.
- `AppSettingData` is loaded only when its `.dat` file exists; first launch
  follows the normal default path and writes a new file on exit.

The project file no longer copies either state file, and `.gitignore` contains
exact paths to prevent generated state from being committed again.

## Validation

The cleaned `develop-net8` tree was validated in a fresh checkout with:

- `dotnet build MusicTag.sln -c Release`
- `MusicTag.Tests.exe`: 980 passed, 0 failed
- `scripts/Verify-Build.ps1 -Configurations Release -RunSmokeTests`
- AMD64 PE checks for the application, test host, and SQLite interop
- Release startup with no pre-existing database or settings file: process stayed
  alive and no fatal exception log was produced

## Limitations

History rewriting updates the public refs, but copies already held by forks,
clones, caches, or downloads may persist outside the repository. The cleanup
therefore also removes the affected release ZIP assets; it cannot recall
third-party copies.
