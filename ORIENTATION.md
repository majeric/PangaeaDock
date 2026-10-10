# Pangaea Dock orientation

Standalone .NET 10 / Avalonia 11.3.12 package family. No Laurentia, Dock.Avalonia, Material or
ReactiveUI dependency in shipping code. The sample chooses Fluent as its own application theme.

Start with `./Verify.ps1`; run `./Pack-And-Smoke.ps1 -Offline` for package changes. The latter
requires cached Avalonia dependencies; omit `-Offline` on a fresh connected machine.
`Run-Demo.cmd` is the user-facing interactive launcher; don't run it to automate visual checks.

## Change docking behavior

- Model/invariants: `src/Pangaea.Dock.Core/Layout.cs`.
- Pure commands: `src/Pangaea.Dock.Core/DockEngine.cs`; operation plans are immutable and revision-checked.
- Single geometry implementation: `src/Pangaea.Dock.Core/Geometry.cs`.
- Tests: `tests/Pangaea.Dock.Tests/Program.cs`; includes seeded operation sequences, cancellation,
  normalization idempotence, identity retention and corruption/failure cases.
- Normalization must not repeatedly divide weights already summing to one: floating-point drift
  caused non-idempotent serialized results. Preserve sufficiently normalized values.

## Change panel lifetimes or persistence

- `src/Pangaea.Dock.Runtime/DockWorkspace.cs` owns sessions and history leases. Old sessions remain
  live while undo refers to them. A singleton reopened from history must reuse its retained session.
- `PanelRegistry.cs` supplies nonvisual consumer factories; `WorkspaceStore.cs` stores data-only JSON.
- Applying a saved arrangement remaps slots to live panel instances; unmatched live panels are retained.
  New template slots need fresh IDs to avoid collisions after a user has replaced an original tab.
- Never store document business state or implement document save policy in the generic docking model.

## Change rendering or interaction

- `src/Pangaea.Dock.Avalonia/DockWorkspaceControl.cs`: host coordination, content reparenting, rails.
- `DockDragging.cs`: captured-pointer transport and exact geometry preview; native qualification remains open.
- `DockGroupView.cs`, `DockCommands.cs`, `DockTheme.cs`: tabs, menus/pickers, resource keys.
- Release all ContentControl parents before attaching a retained view in another group or host.
- Build group resize menus from the current host's layout tree, never its previous rendered geometry:
  moving the last tab can remove a split before the surface has been arranged again.
- Focus the captured surface when starting a drag or Escape can go to an unrelated previously focused control.
- `tests/Pangaea.Dock.Harness/Program.cs` derives from the actual demo application, drives headless input
  and captures real Skia frames in `artifacts/ui/`. ReactiveUI setup is unnecessary: this project does not use it.
- Demo toolbar wraps; do not let a single-row toolbar force the entire shell wider than a small window.

## Change the demo / package setup

- `samples/Pangaea.Dock.Demo/DemoContent.cs`: six sample panel registrations/content and default layouts.
- `DemoWindow.cs`: workspace commands, local demo storage and evaluation guide.
- `tests/Pangaea.Dock.PackageSmoke/`: package-only consumer, intentionally outside the solution.
- `Pack-And-Smoke.ps1`: creates an isolated consumer and isolated package cache beneath artifacts.
- Builds disable Avalonia's optional stats target using `-p:UsedAvaloniaProducts=`; its log directory
  is outside the workspace sandbox. `-p:UseSharedCompilation=false` avoids slow shared-compiler waits here.

The architecture/implementation plan describes the full intended product. README and PROGRESS are
the current evaluation status; do not mark native accessibility, platform or migration gates passed
from headless evidence alone.
