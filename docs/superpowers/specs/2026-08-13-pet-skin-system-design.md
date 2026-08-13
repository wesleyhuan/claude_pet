# Claude Pet — Custom Skin System

**Goal:** Let a user replace the pet's built-in procedural pixel art with their
own — either hand-drawn in an external pixel-art tool, or generated for them
(by Claude, or any script) — without touching code.

## Background

Today `src/ClaudePet/Rendering/PixelArtGenerator.cs` procedurally builds every
frame the pet ever shows: a 16x16 `uint[]` (BGRA) pixel array per `Mood`
(`NoSession`/`Happy`/`Eating`/`Full`/`Stressed`), 2 frames each (idle +
"squish"), plus two overlay accents (`Working`, `Worried`) drawn on top
regardless of mood. `PetWindow` cycles the 2 frames on a 500ms
`DispatcherTimer` and upscales the 16x16 sprite into its fixed 128x128 window
via `NearestNeighbor` scaling.

This spec adds a **skin system**: an alternate, file-based source for those
same frames, selectable from the tray menu, sitting alongside the existing
built-in art (kept as the permanent "Default" skin).

## Architecture

- **`SkinLoader`** scans `%LOCALAPPDATA%\ClaudePet\skins\` once at startup.
  Each immediate subfolder is a candidate skin. `SkinLoader` validates each
  one fully (see Validation below) and returns the set of valid `Skin`
  models; invalid folders are skipped entirely (never shown as selectable)
  with the specific failure reason logged to `debug.log`.
- **`AppSettings`** gains `string? ActiveSkinName` — `null` means the
  built-in Default skin. Persisted the same way `ShowSubscriptionUsage`
  already is, via `SettingsStore`.
- **`TrayIconManager`** gains a "Skin" submenu: "Default" plus one entry per
  valid discovered skin (labeled with that skin's `displayName`), behaved as
  a single-select radio group — selecting one unchecks the others, persists
  `ActiveSkinName`, and swaps the pet's active frame source immediately (no
  restart needed to *switch* between already-discovered skins; a restart is
  only needed to discover a *newly added* skin folder — see Non-Goals).
- **Rendering forks on the active skin.** `PetWindow` no longer calls
  `PixelArtGenerator` directly; it asks a frame source (Default's
  `PixelArtGenerator` or the active custom `Skin`) for frames given
  `(mood, isWorking, isWorried)`. Both sources return the same
  `IReadOnlyList<PixelFrame>` shape, so `PetWindow`'s animation/render loop
  is unchanged either way.

## Skin folder & manifest schema

A skin is a folder `%LOCALAPPDATA%\ClaudePet\skins\<folder-name>\` containing
a `skin.json` manifest and its frame files, referenced by path relative to
the skin folder:

```json
{
  "displayName": "My Cool Pet",
  "moods": {
    "NoSession": { "frame0": "nosession_0.png", "frame1": "nosession_1.png" },
    "Happy":     { "frame0": "happy_0.png",     "frame1": "happy_1.png" },
    "Eating":    { "frame0": "eating_0.json",   "frame1": "eating_1.json" },
    "Full":      { "frame0": "full_0.png",      "frame1": "full_1.png" },
    "Stressed":  { "frame0": "stressed_0.png",  "frame1": "stressed_1.png" }
  },
  "overlays": {
    "working": { "frame0": "working_0.png", "frame1": "working_1.png" },
    "worried": { "frame0": "worried_0.png", "frame1": "worried_1.png" }
  }
}
```

- The **folder name** is the identifier stored in `ActiveSkinName`.
  `displayName` is only the tray-menu label.
- All 5 moods and both overlays are required, each with both `frame0` and
  `frame1` (14 frame files total). Any omission invalidates the whole skin —
  no partial-override fallback to Default art (a deliberate choice: mixing
  hand-drawn and procedural art in one skin was rejected as visually
  inconsistent).
- File extension selects the per-frame loader — `.png` or `.json` — and the
  two can be freely mixed within a single skin.

### PNG frames

Must decode to exactly 16x16 pixels; any other dimensions invalidate the
skin. The alpha channel supplies transparency, same as the app's existing
internal pixel format.

### JSON grid frames

An alternative to PNG for hand-typing or generating without an image codec:

```json
{
  "palette": { "R": "FF4CAF50", "K": "FF212121", ".": "00000000" },
  "pixels": [
    "................",
    "..RRRRRRRRRRRR..",
    "  ... 14 rows total ..."
  ]
}
```

- `pixels` must be exactly 16 rows of exactly 16 characters each.
- Every character used in `pixels` must have a matching `palette` entry —
  there is no implicit "unrecognized character = transparent"; transparency
  is always an explicit palette entry (conventionally `.`, but any key
  works).
- Palette values are 8 hex digits, `AARRGGBB` — the same byte layout as the
  app's internal pixel format, so partial alpha is supported, not just
  fully-opaque-or-fully-transparent.

## Compositing

For a custom skin's frame at animation index *i* (0 or 1):

1. Start from the mood's `frame{i}` pixels.
2. If `isWorking`, alpha-composite the `working` overlay's `frame{i}` on top.
3. If `isWorried`, alpha-composite the `worried` overlay's `frame{i}` on top
   of that.

This is the same body → working → worried layering order the built-in
generator already uses — only the pixel source changes. A new
`PixelCompositor` does real alpha-over blending (not a "replace if
non-transparent" shortcut), since custom overlay art may use partial alpha
for soft edges.

## Validation

Run once per candidate skin folder at startup:

1. `skin.json` exists and parses as valid JSON matching the schema above.
2. All 5 moods and both overlays are present, each with `frame0` and
   `frame1`.
3. Every referenced frame file exists on disk.
4. Every PNG frame decodes to exactly 16x16.
5. Every JSON grid frame has exactly 16 rows of exactly 16 characters, and
   every character used resolves in that frame's `palette`.

Any single failure invalidates the whole skin: it is skipped from the tray
submenu entirely, and the specific failure (which file, which check) is
logged to `debug.log`. A broken or partially-written skin folder can never
crash the app or partially render.

**Startup fallback:** if the persisted `ActiveSkinName` no longer resolves
to a valid skin (folder deleted, now fails validation), the app silently
falls back to Default, logs why, and corrects the persisted setting back to
`null` — so the tray checkmark never lies about what's actually showing.

## Starter example skin

To give users something concrete to copy, the app ensures
`skins\example\` exists on startup, generating it if missing. Its art is
derived directly from the current `PixelArtGenerator` output (so it's
guaranteed valid and looks identical to Default), split across both frame
formats to demonstrate mixing: a couple of moods written as PNG (via the
same PNG encoder used elsewhere in the app), the rest plus both overlays
written as JSON grids (palette auto-derived from each frame's actual
colors). It appears in the tray submenu as "Example (copy me!)" — an
ordinary, fully valid, selectable skin, not a special-cased template. A user
copies `skins\example\` to `skins\<their-name>\` and edits from there.

## Non-Goals (explicit)

- **No hot-reload of newly added skin folders.** Skins are discovered once
  at startup; adding a new skin folder while the app is running requires a
  restart to appear in the tray submenu. (Switching *between* already-
  discovered skins does not require a restart.)
- **No configurable sprite resolution.** Every skin is locked to 16x16,
  matching the built-in art and the app's fixed 128x128 window with
  `NearestNeighbor` upscaling.
- **No variable frame counts per mood.** Every mood and overlay is exactly 2
  frames (idle + squish), matching the existing 500ms alternation — a skin
  re-skins the existing bounce animation, it doesn't define a new one.
- **No partial-override skins.** A skin either supplies complete art for
  everything or is rejected outright; there is no mixing of one skin's art
  with Default's or another skin's art within a single mood set.

## Testing

Pure/deterministic logic is extracted into small, directly testable static
classes, matching the codebase's existing pattern
(`TooltipFormatter`/`PetBadgeFormatter`/`WorriedEvaluator`/
`MoodStateMachine`):

- **`SkinManifestParser`** — parses and validates `skin.json` structure
  (checks 2–5 above, minus PNG/JSON-specific dimension checks).
- **`PixelGridParser`** — parses and validates a JSON grid frame (row/column
  counts, palette resolution, hex parsing) into a `PixelFrame`.
- **`PixelCompositor`** — the alpha-over blending used for overlay
  compositing, tested independently of any file I/O.

PNG decoding itself leans on WPF's built-in `BitmapDecoder`/`PngBitmapEncoder`
(used for both reading skin PNGs and generating the example skin's PNG
frames), exercised with a couple of small fixture PNGs rather than
reimplemented or deeply unit-tested.

**Skin discovery** (`SkinLoader`'s folder scanning) is not directly unit
tested, consistent with how `UsageReader`/`SubscriptionUsageReader` are
verified live via the `run` skill rather than unit-tested — it's I/O
plumbing around already-tested pure logic (`SkinManifestParser`,
`PixelGridParser`).
