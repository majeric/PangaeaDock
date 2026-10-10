# Changelog

All notable changes to Pangaea Dock are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/). Before 1.0, minor versions may break the public API.

## [0.1.0] - 2026-10-10

First public release: an **evaluation preview**. The libraries work and are tested, but the API is
not yet stable and several production acceptance gates remain open (see [PROGRESS.md](https://github.com/majeric/PangaeaDock/blob/main/PROGRESS.md)).

### Added

- **Pangaea.Dock.Core** — immutable, data-only layout model (hosts, splits, tab groups, border
  trays, panels) with separate slot and panel-instance identities; validation, bounded import and
  idempotent normalization; pure, revision-checked operations for move, reorder, group, split,
  resize, replace, swap, duplicate, float, close, collapse/pin, select and apply-arrangement; a
  single minimum-size/weighted geometry implementation shared by previews and rendered surfaces.
- **Pangaea.Dock.Runtime** — panel registry with nonvisual `PanelSession` factories and
  singletons; transactional commits with asynchronous release guards, stale-plan rejection and
  failure rollback; operation-count-bounded layout undo/redo that retains and reuses live sessions;
  `WorkspaceStore` for schema-1 JSON with atomic replacement and last-good backup; saved
  arrangements that preserve live panels.
- **Pangaea.Dock.Avalonia** — `DockWorkspaceControl` with tab groups, splitters, captured-pointer
  drag with exact split/group previews and Escape cancellation, native floating windows, border
  trays on all four edges with temporary open and pin, tab and group context menus (Replace,
  Duplicate, Swap, Move to New Window, Close, collapse, keyboard move/resize), a searchable panel
  picker, Ctrl+Alt+Z/Y layout history and overridable neutral light/dark resources.
- Desktop demo (`samples/Pangaea.Dock.Demo`) with six sample panels, Explore/Focus presets, named
  arrangements, import/export and theme switching.
- Close/float/collapse capability flags are enforced for every user operation (drag, swap, add,
  float, collapse, close); `ApplyLayout` remains consumer authority.
- Border-tray buttons show the panel name (rotated along side edges), not just an icon.
- Verification: 992 core/runtime checks, 55 headless Avalonia/Skia UI interaction checks with
  rendered frames, and a package-only consumer smoke test.

### Known limitations

- Native multi-monitor, mixed-DPI and macOS interaction are not yet qualified; dragging a tab onto
  bare desktop does not create a floating window (use **Move to New Window**).
- No custom automation peers or screen-reader certification yet.
- Storage has no schema migrations, sanitized portable export or multi-process coordination.
- History is bounded by operation count, not memory.

[0.1.0]: https://github.com/majeric/PangaeaDock/releases/tag/v0.1.0
