# Changelog

All notable changes to Lanhu Runtime Sync are documented here.

## 1.0.6 - 2026-07-21

- Calibrated Photoshop text stroke, shadow spread, and shadow blur thickness for TMP SDF rendering while preserving the source shadow offset.
- Removed percentage-based horizontal scaling and no longer writes TMP `<scale>` tags into imported text content.

## 1.0.5 - 2026-07-21

- Preserved Photoshop outside and inside stroke alignment with TMP Face Dilate instead of allowing centered SDF outlines to cover the glyph face.
- Added reusable high-padding dynamic TMP font assets for outlined and shadowed text, preventing Underlay sampling artifacts from tightly packed source atlases.
- Corrected Photoshop shadow angle direction and kept Distance, Spread, Size, and color-space conversion in source pixel units.
- Applied Photoshop percentage-based horizontal glyph scaling through TMP rich text while keeping Character Spacing at zero.
- Forced imported Canvas roots back to unit scale when creating or updating a page.

## 1.0.4 - 2026-07-21

- Moved imported pages under a dedicated Canvas parent and fixed CanvasScaler reference resolution to use the Lanhu artboard size.
- Added automatic migration for existing prefabs that previously stored Canvas and sync metadata on the same object.
- Disabled TMP word wrapping and auto sizing for fixed Photoshop text frames, preserving source font sizes and text box dimensions.
- Prevented intrinsic Black/Bold font assets from receiving an additional synthetic TMP bold weight.
- Converted TMP outline and underlay colors for Unity Linear color space and applied Photoshop shadow distance using its source angle.

## 1.0.3 - 2026-07-21

- Corrected Photoshop/Lanhu shadow fields so distance and spread percentage map to TMP Underlay correctly.
- Clamped TMP Outline and Underlay values to their shader-supported ranges.
- Set imported TMP character spacing to zero.

## 1.0.2 - 2026-07-21

- Replaced legacy UGUI `Shadow`/`Outline` components on TMP text with TMP SDF material effects.
- Added deterministic reusable TMP materials keyed by font, outline, and shadow parameters.
- Added Lanhu text-shadow blur and spread parsing and automatic cleanup of legacy text effects.

## 1.0.1 - 2026-07-21

- Added Cookie extraction for cURL `-b`, `--cookie`, and header variants.
- Added local Cookie format validation before loading Lanhu pages.
- Improved HTTP 401/403/418 diagnostics for expired sessions and account permission failures.

## 1.0.0 - 2026-07-21

- Added Lanhu page discovery and authenticated design loading.
- Added UGUI prefab creation and stable node-ID incremental updates.
- Added selectable hidden-node handling and per-node synchronization controls.
- Added editable TMP text, rich text spans, font matching, line height, spacing, outline, and shadow synchronization.
- Added project-wide duplicate-page protection and optimized sprite import defaults.
- Added whole-page preview fallback and editable-layer rebuild mode.
- Added daily update checks and one-click Git package updates.
