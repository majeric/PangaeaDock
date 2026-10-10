# Pangaea Dock progress

## 2026-10-10 — review findings (not yet fixed)

Put under local git (`main`, no remote). A line-by-line review found these defects; none are fixed:

- `WorkspaceStore.LoadWithBackup` only falls back to `.bak` for IO/JSON/argument/invalid-operation
  errors. `InvalidDataException` (validation, size, schema version) and `NotSupportedException`
  escape, so a parseable-but-invalid main file throws instead of loading the backup.
- `DockWorkspace` commit assigns `Layout` before popping the undo/redo entry; `ClearHistory()` during
  a pending undo makes the pop throw after the layout already changed (no `Changed`, no `Collect`).
- `MoveTabs`, `SwapPanels`, `AddPanel` and `ApplyLayout` skip capability checks: a `CanFloat=false`
  tab can be dragged into a floating host, `CanCollapse=false` into a rail, and a template can drop a
  `CanClose=false` panel.
- Collapse strips `RegionKey` and leaves an empty placeholder; pin never replaces it, so
  collapse-then-pin of the primary group leaves an empty primary group beside the panel.
- `Evaluate` throws for a stale `AddPanel.ReplaceSlotId`; `UpdatePlacement` throws below 100px and
  aborts `CaptureFloatingPositions` for the remaining windows; splitting beside an empty group is
  rejected as "beside itself".
- Test strength: 480 of the 528 checks are the random loop, which ignores operation success and only
  compares slot-ID sets. No test sets a capability flag false.

## 2026-10-10 — disappearing panels after tab drag

Reproduced the user's report with actual headless pointer input: dragging the Activity tab into
the center removes its now-redundant parent split. During presentation rebuilding, `GroupMenu`
looked up that removed split using the previous frame's geometry and threw `Sequence contains
no matching element`. The workspace caught the notification error, leaving a partially rebuilt
surface. Saved session data still contained all six panel instances; no user storage was changed.

Resize menu actions now derive from the current host's layout tree. Added selected-tab drag
regressions covering Atlas, Expedition, Activity and Inspector, including removal of a group's
last tab, rendered group bounds, content failures, and undo. The harness now also rejects any
workspace diagnostic across the full interaction sequence, catching presentation failures that
model-only checks missed. The new Activity drag test failed before the fix and passes afterward.

Release build: zero warnings/errors. Core/runtime: 528 checks pass. Headless UI: 54 checks pass.
Inspected the Activity-after-drag rendered frame; all remaining groups and their content are visible.
The interactive Windows demo was not driven for this verification. Rebuild/rerun from Rider or
use Run-Demo.cmd to load the corrected library into a new demo process.

## 2026-10-10 — independent library and working evaluation demo

Implemented a standalone package family and desktop evaluation app. The user requested a working
example to evaluate after the architecture/design phase. Laurentia integration is intentionally
not part of this evaluation build; its existing runtime and dependencies are unchanged.

### Implemented

- Immutable data-only host/split/tab/rail/panel model; identity validation, bounded import and
  deterministic normalization. Slots and panel instances have separate IDs.
- Pure evaluated operations for moving/reordering/grouping/splitting, resizing, replacement,
  swapping, duplication, floating, closing, collapse/pin, selection and workspace application.
- One minimum-size/weighted geometry implementation shared by rendered surfaces and previews.
- Runtime registry and retained panel sessions; asynchronous release guards, stale-plan rejection,
  failure rollback, operation-count-bounded layout undo/redo and redo disposal. Reopening a
  singleton retained by history reuses its session. Document contents are consumer-owned.
- Avalonia tab/group controls, context menus, searchable panel picker, retained visual instances,
  captured-pointer drag previews, splitter previews, cancellation, native floating host creation,
  simple screen-bound clamping and four sidebar edges with temporary expansion/pinning.
- Neutral light/dark docking resources; no Material/ReactiveUI/Laurentia/old Dock dependencies.
  Visible menu routes for movement and resizing; tab keyboard navigation and layout shortcuts.
- Version-1 JSON layout storage, temporary-sibling replacement and last-good backup. Demo autosave,
  saved named arrangements, Explore/Focus presets, reset, import/export and live-panel preservation.
- Six demo panels: Atlas, Notes, Inspector, Expedition, Activity and Palette. `Run-Demo.cmd`
  builds and launches the Release application; `? Guide` explains evaluation interactions.
- Three local NuGet packages and an isolated package-only consumer test. No package publication.

### Verification

- Release solution build: **0 warnings, 0 errors**.
- **528 core/runtime checks**, including 240 deterministic random operations with per-step identity
  and normalization assertions (seed 20261010), stale/failed transactions, close cancellation,
  retained-session identity/disposal, singleton reopening, template application after replacement,
  malformed null input and backup recovery.
- **29 headless UI/interaction checks**, using the actual DemoApp, Avalonia.Headless and Skia:
  real pointer merge/split/resize, unchanged source during preview, preview/commit geometry equality,
  Escape cancellation, replacement bounds, sidebar expansion/pin, float creation/closure,
  cross-top-level pointer movement/drop, workspace state conservation and responsive sizing.
- Generated frames under `artifacts/ui/`; dark/light layouts, split notes, replacement, sidebar,
  auto-hide, floating panel, focus layout and small window were rendered. Dark/light/small-window
  and auto-hide frames were visually inspected, and the small toolbar clipping was corrected.
- Local packages packed and a separate consumer restored using only package references, built
  with **0 warnings/errors**, and rendered docking controls headlessly. Dependencies restored
  from the local feed and existing package cache into an isolated cache.

Commands: `./Verify.ps1`, `./Pack-And-Smoke.ps1 -Offline`. The scripts serialize project builds,
disable the optional Avalonia stats task for workspace-safe builds, and disable shared compilation
to avoid long shared-compiler waits on this machine. No desktop mouse/keyboard or visible native
window was used for automated verification. The demo executable is ready for the user's evaluation.

### Defects found and fixed during verification

- Repeated normalization divided already-normalized split weights, causing floating-point drift.
- Escape went to prior keyboard focus outside the docking surface; starting capture now focuses it.
- Applying a template after replacing its original panel could collide with a live leftover slot ID.
- Reopening an undo-retained singleton created another session instead of reusing the original.
- Invalid null host/layout JSON could escape validation as a null-reference failure.
- A fixed horizontal toolbar forced the shell outside a smaller window; it now wraps.
- Replacement/swap size validation now rejects a panel that would force its target group to grow.

The cross-host test initially targeted a point covered by the floating window. It was corrected
to use an exposed main-host region; the transport correctly treats overlapping hosts as targets.
This is headless evidence, not proof that the current registered-host order matches OS z-order.

### Work-package status against the full plan

| Work | Evaluation status | Remaining release evidence/work |
|---|---|---|
| WD-00 | Captured-pointer implementation and decision record | Native Windows/macOS, DPI, GPU content and OS z-order |
| WD-01 | Three buildable libraries, demo, test projects, solution | Full public API versioning/documentation review and licensing |
| WD-02 | Model/validation/normalization implemented and exercised | Typed ID wrappers, broader adversarial/property coverage |
| WD-03 | Main operations and minimum geometry implemented | Full policy/constraint metadata, deterministic device-scale rounding |
| WD-04 | Transaction/session/undo behavior implemented and exercised | Retained-memory budget and advanced document lifecycle integration |
| WD-05 | Rendered static/interactive control set with both themes | Granular chrome reconciliation, focus bookmarks, complete templating surface |
| WD-06 | Pointer group/tab/resize preview and cancellation tested | Hysteresis, drag ghost, explicit insertion marker, overflow auto-scroll, desktop float |
| WD-07 | Picker, replace/duplicate/swap/close and layout history usable | Category headers, disabled capability explanations, production document sample |
| WD-08 | Headless multi-host pointer/closure exercised | Native platform qualification and monitor-topology reconciliation |
| WD-09 | Rails/temporary open/pin implemented | Return proportion fidelity, resize overlays, full owned-popup/focus matrix |
| WD-10 | Data-only storage, backup, arrangements and demo commands | Schema migrations, portable sanitization, concurrent-process storage leases |
| WD-11 | Keyboard menu routes, names, rendered visual checks | Custom automation peers, native reader, contrast/motion/RTL and performance gates |
| WD-12 | Local package-only restore/build/render passed | Public license/publication identity, broader compatibility matrix |
| WD-13 | Laurentia adapter live (`Laurentia/Dock/PangaeaDockHost.cs`, 39 integration checks) | See Laurentia/PROGRESS.md known gaps |
| WD-14–15 | Not started | Suite migration off `IDockable`, old Dock removal |

The evaluation preview does not claim complete fulfillment of the production architecture.
The actual API is described in PACKAGE_README.md and the source. In particular, the demo's export
contains its scratch-note state; it is not yet a sanitized portable workspace definition. Storage
is synchronous/atomic rather than a general asynchronous multi-process coordinator. History is
bounded by count. Singletons are per-workspace only. Native display recovery is basic clamping.

Next work should follow the user's evaluation feedback, with WD-00 native input/hosting validation
and WD-11 focus/accessibility as the highest remaining platform risks before Laurentia adoption.
