# Pangaea Dock — bring your panels together

Status: **working evaluation preview 0.1**, 2026-10-10. Independent libraries, a runnable desktop
demo, automated model/runtime tests, rendered interaction tests and local NuGet packages exist.
This is not yet the production release described by every acceptance gate in the design plan.

Pangaea Dock is an independent Avalonia docking library. It replaces Dock.Avalonia itself; it is not a
Laurentia docking wrapper. Its model, engine, content lifecycle and controls must work in an
ordinary Avalonia application with no Laurentia, AdaSuite, Material.Avalonia or ReactiveUI dependency.
Laurentia will be one consumer, with a separate integration layer owned by Laurentia.

## Run the example

Double-click **[Run-Demo.cmd](Run-Demo.cmd)** from Explorer. It builds the Release example using
the .NET 10 SDK, then opens the application. Avalonia's build telemetry is disabled for this
build invocation so it does not need to write outside the project during an agent build.

The already-built executable is at
`samples/Pangaea.Dock.Demo/bin/Release/net10.0/Pangaea.Dock.Demo.exe`.
Run that directly if you do not need to rebuild. No installation or administrator access is needed.

Try these interactions:

1. Drag **Notes** onto the center of **Inspector** to make a tabbed group.
2. Drag a tab near a panel edge; release to commit the highlighted split. Escape cancels.
3. Drag the dotted grip to move a whole group, or drag a separator to resize it.
4. Right-click a tab for **Replace**, **Duplicate**, **Swap**, **Move to New Window**, or **Close**.
5. Use a group's **⋯** menu to collapse it to a sidebar. Click its icon to open it; **↗** pins it.
6. Switch **Explore / Focus**, edit Notes, then switch back. Live panel state follows the panel.
7. Try **Undo layout / Redo** (Ctrl+Alt+Z/Y), including after replacing, closing or resetting.
8. Save a named arrangement, export/import it, and switch to the light theme.

The demo has six panel types: Atlas, Notes, Inspector, Expedition, Activity and Palette. Some
are singletons; the panel picker marks them already open and activates them instead of duplicating.
The demo's Inspector/Expedition are sample content, not a complete cartography application.

Session and named-workspace files live under `%LOCALAPPDATA%\PangaeaDock\Demo` when you run the
desktop demo. Tests disable that persistence. Exports contain demo scratch notes; they are layout
snapshots with application-provided panel state, not sanitized portable workspace templates yet.

## Use the library

See [package setup and API usage](PACKAGE_README.md). The package family is:

| Package | Responsibility |
|---|---|
| `Pangaea.Dock.Core` | Immutable layout, pure operations, validation, normalization and geometry |
| `Pangaea.Dock.Runtime` | Registry, panel sessions, guards, transaction commit, undo/redo and JSON storage |
| `Pangaea.Dock.Avalonia` | Controls, drag previews, panel menus, picker, rails and floating windows |

Local `.nupkg` files are in `artifacts/packages/`. Nothing has been published. Distribution license
and public package-name availability still need to be decided/checked before publishing.

## Verify

```powershell
.\Verify.ps1
.\Pack-And-Smoke.ps1 -Offline
```

`Verify.ps1` builds Release, runs the pure/runtime suite, then drives the real demo through
Avalonia's headless backend with Skia rendering. Frames are written to `artifacts/ui/`.
`Pack-And-Smoke.ps1` packs all three libraries and restores/builds/renders a separate consumer
using only NuGet references. `-Offline` uses the already-installed package cache; omit it on a
fresh machine with network access. No validation command launches a visible desktop window.

The two executable test projects are console assertion harnesses, not `dotnet test` adapters.
The package smoke project is intentionally outside the solution: its local packages must exist
before restore. The library has no Laurentia or existing Dock dependency.

## Current limits

- Captured-pointer cross-host dragging is exercised headlessly; actual Windows multi-monitor,
  mixed-DPI and macOS interaction still require native qualification. Use **Move to New Window**
  to float; dragging onto bare desktop does not yet create a floating window.
- Keyboard commands and accessibility names exist; custom automation peers, high-contrast and
  screen-reader certification, reduced-motion policy and novice usability studies remain open.
- History is bounded by operation count, not retained memory. Async close guards exist; production
  document lifetime/recovery integration belongs to consuming applications and is not demonstrated
  by scratch-note panels.
- Storage supports schema 1, bounded validation, atomic writes and last-good backup. Version
  migrations, sanitized portable exports, cross-process writes and monitor-topology reconciliation
  are not complete. Named arrangements preserve live panels, including ones absent from a template.
- The current UI rebuilds group chrome on commit while retaining panel controls. More granular
  focus/bookmark preservation, target hysteresis and large-workspace performance are still work items.
- Laurentia renders its shell through this library via its own adapter (`Laurentia/Dock/PangaeaDockHost.cs`);
  its document/palette contracts still use Dock.Avalonia types until the later migration.

## Design and continuation

Read in this order:

1. [Design brief](DESIGN_BRIEF.md) — the user's original requirements, preserved verbatim.
2. [Architecture](ARCHITECTURE.md) — boundaries, identities, contracts, interaction semantics,
   persistence, reliability and consumer integration.
3. [Implementation plan](IMPLEMENTATION_PLAN.md) — ordered work packages, acceptance tests,
   migration gates and a reusable agent assignment.

4. [Orientation](ORIENTATION.md) — current source seams and verification commands.
5. [Progress](PROGRESS.md) — implemented behavior, evidence and remaining acceptance gaps.

This directory is a standalone repository/package boundary, located in AdaSuite for convenience.
`ARCHITECTURE.md` describes the intended complete product; the current public APIs in source and
`PACKAGE_README.md` describe the evaluation build. Do not infer completion of a work package from
the existence of a similarly named control.
