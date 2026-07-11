# Cover Encoding and Track Link Enhancement Plan

## Scope

This plan covers two independent feature batches:

1. Unify embedded-cover processing for JPEG and PNG, expose meaningful unlimited limits and configurable encoding settings, and preserve original downloaded bytes for sidecar files.
2. Automatically classify supported track links by provider and safely resolve QQ Music short links before exact-ID lookup.

Hiding Kugou is explicitly out of scope.

## Confirmed Current Behavior

- `PictureSizeLimitsKB == 0` is already interpreted as unlimited by the compression core, but the options UI does not expose a zero entry.
- `PictureResolutionLimits == 0` means there is no explicit resolution cap. The UI currently labels it as `Auto`. A separate active byte-size limit may still force the compressor to reduce dimensions.
- `PictureFormatLimits == AUTO` preserves original JPEG/PNG/GIF bytes only while the image already satisfies all limits. Once resizing or recompression is required, the current implementation encodes the result as JPEG.
- JPEG encoding currently starts at quality 85. Windows GDI+ ignores attempted PNG compression-level parameters, so it cannot implement a real configurable PNG compression level.
- Manual search caches keep the provider's original downloaded bytes. Embedded-tag save later runs the compression core.
- Auto-match currently compresses one downloaded cover instance and reuses it for both tag embedding and sidecar-file output.
- Track-ID normalization already accepts direct provider links, but the selected provider is always taken from the UI dropdown.
- The supplied `c6.y.qq.com/base/fcgi-bin/u` samples resolve through ordinary HTTP redirects to QQ Music song-detail URLs containing numeric `songid` values.

## Design Decisions

### Unified image engine

- Add `SixLabors.ImageSharp 2.1.12` (Apache 2.0) as the single decoding, resize, format-conversion and encoding engine for images that require processing. ImageSharp 4.x is intentionally not used because its build requires a separate Six Labors license file/key.
- Use the same processing pipeline for JPEG and PNG. Do not keep GDI+ JPEG encoding alongside a separate PNG encoder.
- Keep WinForms `Bitmap` conversion at display boundaries only. UI rendering must not decide persisted image bytes.
- Preserve original bytes without decoding or re-encoding when `AUTO` is selected and all active limits are already satisfied.
- Record the direct dependency and applicable license in maintenance/release documentation.

### Format semantics

- `AUTO`
  - Preserve original bytes when no processing is required.
  - When processing is required, keep the original supported format.
  - JPEG remains JPEG and uses the configured JPEG quality.
  - PNG remains PNG and uses the configured PNG compression level.
  - Static GIF handling remains unchanged unless processing is required; animated-GIF conversion is not expanded in this batch.
- `JPG`
  - Always produce `image/jpeg` when processing/conversion is required.
  - Use configurable quality from 1 through 100, default 85.
- `PNG`
  - Always produce `image/png` when processing/conversion is required.
  - Use configurable lossless compression level from 0 through 9, default 6.

Byte-size and resolution limits remain hard constraints. JPEG quality and PNG compression level are preferred starting parameters; the processor may reduce JPEG quality or image dimensions further when necessary to satisfy a hard byte limit. PNG remains lossless at a fixed resolution, but dimensions may be reduced if lossless compression cannot meet the byte limit.

### Options UI

- Prepend `0` to the embedded-picture size options and display it as `Unlimited` / `无限制` / `無限制`.
- Relabel resolution value `0` from `Auto` to `Unlimited` / `无限制` / `無限制`.
- Explain that an active size limit may still reduce dimensions even when the resolution limit is unlimited.
- Extend the format list from `AUTO|JPG` to `AUTO|JPG|PNG` without changing the existing persisted values.
- Add one contextual numeric control beside the format selector:
  - `AUTO`: JPEG quality used when an oversized JPEG must be recompressed.
  - `JPG`: JPEG quality, range 1-100.
  - `PNG`: PNG compression level, range 0-9.
- Persist JPEG quality and PNG compression level independently so switching formats does not discard either value.
- Keep existing defaults for size, resolution and format to avoid changing behavior merely by upgrading.
- Apply window-local DPI metrics to the new controls and include 96/144/96 round-trip characterization.

### Original sidecar bytes

- Keep downloaded original bytes/path separate from the processed embedded-picture result.
- `SaveToFile` writes the original provider bytes.
- `SaveToTag` processes a copy according to embedded-picture settings.
- `SaveToTagAndFile` writes the original sidecar and the separately processed embedded version.

### Track-link classification

- Add a pure host-classification component using `Uri.Host`, not substring matching.
- Direct URL input overrides the dropdown provider when the host is recognized.
- Raw non-URL ID input continues to use the dropdown provider.
- After automatic classification, update the dropdown to show the provider actually used.
- Reject unsupported URL hosts instead of passing them to the selected provider.
- Preserve existing provider-specific ID normalization and enum ordinals.

Initial direct-link host families:

- NetEase: `music.163.com` and explicitly characterized official variants.
- QQ Music: `y.qq.com`, `i.y.qq.com`, `c6.y.qq.com` and explicitly characterized official variants.
- Kuwo: `kuwo.cn` and explicitly characterized official variants.

### QQ short-link resolution

- Only resolve URLs matching the QQ short-link host/path allowlist.
- Use an isolated `HttpClientHandler` with automatic redirects enabled and a bounded maximum redirect count.
- Use `ResponseHeadersRead`, a short timeout and the dialog cancellation token.
- Do not read the final HTML body when the final request URI is sufficient.
- Require every redirect destination/final host to remain within the QQ Music allowlist.
- Normalize the final `songid`/`songmid` through the existing QQ ID rules.
- Expose the transport operation behind an injectable seam; tests use recorded redirect responses and never require live services.

## Implementation Batches

### Batch 1: Characterization and unified image core

1. Add fixtures for JPEG, PNG with alpha, oversized images and exact original-byte preservation.
2. Add ImageSharp and implement a standalone cover-processing service with explicit input/output contracts.
3. Cover `AUTO`, forced JPEG, forced PNG, resolution caps, byte caps, JPEG quality and PNG compression levels.
4. Preserve existing `PictureData` fields and the existing tag-write funnel.

### Batch 2: Settings and options UI

1. Add persisted JPEG-quality and PNG-compression-level settings.
2. Add unlimited size UI entry and relabel unlimited resolution.
3. Add PNG format and contextual encoding control.
4. Add localization fallback strings and DPI-safe layout.
5. Wire the options into the unified processor.

### Batch 3: Manual and auto-match save paths

1. Route manual embedded-cover processing through the unified processor.
2. Split auto-match original sidecar bytes from embedded processed bytes.
3. Verify MP3 and FLAC physical round trips and sidecar byte identity.

### Batch 4: Link classification and QQ short links

1. Add pure provider classification and direct-link normalization tests.
2. Add asynchronous QQ short-link expansion with redirect allowlisting.
3. Update the combined-search dialog to resolve/classify before provider lookup.
4. Keep raw IDs bound to the selected dropdown source.

## Risk and Compatibility

- Image processing has a low static caller count but affects tag saving, undo restore and auto-match output. Treat it as medium-to-high behavioral risk and verify physical files.
- `TrackIdInput.TryNormalize` has high blast radius: 3 direct callers, 15 upstream symbols and 3 affected lookup processes. Keep network resolution outside the pure normalizer and change the dialog orchestration narrowly.
- Do not change `SearchSource` enum ordinals, existing `PictureFormatLimits` values, public signatures, JSON keys, resource base names or the `SaveWithId3v2Version` tag-write funnel.
- Existing settings must load without migration. Unknown/invalid new values fall back to JPEG quality 85 and PNG compression level 6.
- Do not add live-network tests.

## Validation Gate

Each batch must run:

```powershell
.\scripts\Verify-Build.ps1 -RunSmokeTests
git diff --check
codegraph sync
```

Before each commit, run GitNexus `detect_changes()` and review affected flows.

Focused validation includes:

- Exact original-byte preservation in `AUTO` when all limits are satisfied.
- JPEG and PNG decoded dimensions, MIME and pixel/alpha round trips.
- PNG compression levels produce valid lossless files and meaningful size differences on a compressible fixture.
- Hard size/resolution limits remain enforced.
- MP3 and FLAC embedded-cover physical round trips.
- Auto-match sidecar output is byte-identical to the recorded provider payload.
- Direct-link provider classification and raw-ID dropdown behavior.
- Recorded QQ redirect chains, unsupported hosts, cross-host redirects, redirect loops, timeout and cancellation.
- Options-dialog layout at 100% and 150% DPI, including startup and cross-monitor round trips.

## Commit Strategy

- `feat: unify embedded cover encoding settings`
- `feat: resolve track links by provider`

Keep the two feature areas in separate validated commits.
