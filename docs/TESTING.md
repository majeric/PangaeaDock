# Testing Pangaea Dock

Everything runs headless (Avalonia.Headless + Skia): no window is shown and no mouse or keyboard is used, so the suite
runs as-is in CI.

## How to run

```powershell
# Full verification: Release build, xUnit suite, legacy console checks, headless harness and frame capture
./Verify.ps1

# xUnit suite only
dotnet test tests/Pangaea.Dock.UnitTests -m:1 -p:UseSharedCompilation=false -p:UsedAvaloniaProducts=

# Coverage summary per assembly (fails if below target); -Uncovered lists uncovered lines
./Coverage.ps1 -Uncovered
```

`Coverage.ps1` runs `dotnet test --collect:"XPlat Code Coverage"` (coverlet.collector) with
`tests/Pangaea.Dock.UnitTests/coverage.runsettings`, which limits coverage to the three library assemblies. It reads
the Cobertura report from `artifacts/coverage/` (git-ignored). No extra tools are needed.

## Layout of the suite

`tests/Pangaea.Dock.UnitTests` (xUnit 2 + Avalonia.Headless.XUnit). Each test checks one behavior and follows
Arrange/Act/Assert.

| Folder / file | Covers |
|---|---|
| `Core/LayoutModelTests.cs` | `DockLayout`, `TabGroup.ActiveSlot`, `DockRect`, `Ids`, and `LayoutTree` traversal, map and normalize |
| `Core/LayoutValidationTests.cs` | `LayoutTree.Validate`: a valid layout, plus one theory case per kind of corruption and the size/depth limits |
| `Core/DockEngineTests.cs` | Each `DockOperation`, its success result, each rejection path and capability enforcement |
| `Core/DockGeometryTests.cs` | `Allocate`, `Minimum` and `Arrange` |
| `Runtime/DockWorkspaceCommitTests.cs` | Construction, commit, stale plans, lock, policy, release guards, cancellation, rollback and notification isolation |
| `Runtime/DockWorkspaceHistoryTests.cs` | Undo/redo, history limit, session retention and disposal, singletons, duplicates, capture, arrangements, placement and disposal |
| `Runtime/RandomOperationPropertyTests.cs` | Seeded random operation sequences (theory over 4 seeds), described below |
| `Runtime/PanelRegistryTests.cs`, `WorkspaceStoreTests.cs` | Registration rules and missing providers; JSON round trip, bounds, schema, atomic replace and `.bak` recovery |
| `Avalonia/DockWorkspaceControlTests.cs` | Construction/refresh, view reuse and disposal, view failures, empty groups, fit checks, tabs, keyboard and close buttons |
| `Avalonia/DockMenuTests.cs` | Tab and group context menu contents, enabled state and commands |
| `Avalonia/DockInteractionTests.cs` | Pointer tab and group drags (preview, commit, cancel, edge split, tab-strip index), splitter resize, rails and floating windows |
| `Avalonia/DockThemeAndPickerTests.cs` | Theme resource keys and variants, consumer overrides, `PanelViewRegistry` and the panel picker dialog |
| `Support/` | Layout builders (`L`), test catalog, `TestSession`, `WorkspaceFixture` and the `DockUi` headless window harness |

The random-operation property test runs 240 float, collapse, pin, move and split operations for each seed. After
every step it asserts that:

- slots and panel instances are conserved, and each tab keeps its panel;
- the layout validates;
- normalization is idempotent.

At the end it asserts that more than half of the operations succeeded, that undoing everything restores the initial
layout, and that redoing everything restores the final layout.

### Existing projects (kept)

- `tests/Pangaea.Dock.Tests`: the original console program (992 checks). It is kept because `Verify.ps1` and the
  Laurentia docs reference it. Its intent is fully ported to the xUnit suite.
- `tests/Pangaea.Dock.Harness` drives the real demo app and writes the review frames to `artifacts/ui/`. It is kept
  for those frames and for its end-to-end checks against `DemoWindow`.
- `Verify.ps1` now also runs `dotnet test` on the xUnit suite after the build, before the two programs above.

## Current numbers (2026-10-10)

409 tests: 405 pass and 4 are skipped because they expose real bugs (see below). The suite takes about 6 s and passed
on repeated runs.

| Assembly | Line | Branch | Target (line) |
|---|---|---|---|
| Pangaea.Dock.Core | 99.6% (281/282) | 98.3% | 90% |
| Pangaea.Dock.Runtime | 100% (232/232) | 94.4% | 90% |
| Pangaea.Dock.Avalonia | 100% (561/561) | 89.1% | 70% |

## Notable gaps and why

- `DockEngine.EnforceCapabilities` "cannot be closed" (DockEngine.cs, one line) has no test. It is a defensive
  check: every user operation that can drop a panel (close or replace) already rejects a `CanClose=false` panel
  earlier, and `ApplyLayout` skips this check by design.
- Branch gaps in Avalonia are mostly null-conditional fallbacks, for example a missing descriptor icon, no
  `TopLevel` or a missing screen. They also include `PointerCaptureLost`, which headless input cannot raise
  independently of release.
- Headless checks do not cover native-platform behavior: OS z-order of floating windows, DPI, real monitor topology and
  screen readers. See PROGRESS.md for these release gates.

## Known bugs (skipped tests)

| Skipped test | Bug |
|---|---|
| `ResizeSplitTests.ResizeSplit_TooFewWeights_IsRejectedWithoutThrowing` | `DockEngine.Evaluate` throws `IndexOutOfRangeException` (from `LayoutTree.Normalize`) when a `ResizeSplit` has fewer weights than children; `Evaluate` is meant to return an error. |
| `ResizeSplitTests.ResizeSplit_TooManyWeights_IsRejected` | Extra `ResizeSplit` weights are silently dropped and the plan succeeds. |
| `DockTabTests.ArrowKey_KeepsKeyboardFocusOnTheNewlySelectedTab` | Left/Right on a tab focuses the rebuilt header synchronously. The ScrollViewer template has not attached it yet, so `Focus()` fails and keyboard focus is lost after the first arrow press. |
| `DockWorkspaceArrangementTests.ApplyArrangement_ExactIdentityMatchTakesPrecedenceOverTypeMatch` | `ApplyArrangementAsync` matches template panels in hash order, so a type-only match can claim the live panel that another template panel names by identity. Panel placement is therefore nondeterministic between runs. |
