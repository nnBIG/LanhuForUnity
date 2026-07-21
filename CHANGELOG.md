# Changelog

All notable changes to Lanhu Runtime Sync are documented here.

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
