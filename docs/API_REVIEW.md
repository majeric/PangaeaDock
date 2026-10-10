# Pangaea Dock API review (0.1.0-preview.1)

This review covers the public API and code readability, based on `src/` at `6f5cb5d`. The evidence
comes from the source, the demo, the package README, and the one real integration,
`Laurentia/Dock/PangaeaDockHost.cs`. That integration is about 500 lines, and much of it works around
gaps described below. Line numbers refer to the files at that commit.
[USAGE.md](USAGE.md) documents the API as it stands today.

Correctness defects already listed in PROGRESS.md are not repeated here: the `LoadWithBackup`
exception filter, the `ClearHistory` race, missing capability checks, and collapse/pin of the primary
group.

**Ranks:**

- **P0**: fix before 0.1 ships. These are breaking changes that only get more expensive later.
- **P1**: should fix by 0.2.
- **P2**: nice to have.

**Overall:** The core is good. The immutable layout, pure engine, revision-checked plans and
retained sessions are the right foundations. A first docked window takes about 8 concepts and 7 steps,
which is reasonable. The friction is at the edges: two ways to run a command, results reported only as
`bool` plus a string event, behaviour that depends on matching strings, IDs that are all plain strings,
and theming that can only be overridden in one place.

---

## P0: must fix before 0.1

### 1. There are two ways to run a command, and one of them skips the consumer's rules

**Problem:**

- `DockWorkspaceControl.RunAsync` (`DockWorkspaceControl.cs:137`) applies two checks: `CloseRequested`
  and the on-screen size check (`Fits`).
- `DockWorkspace.ExecuteAsync` (`DockWorkspace.cs:59`) applies neither.
- The library itself mixes the two. The Duplicate menu item (`DockCommands.cs:19`), choosing a singleton
  in the picker (`DockCommands.cs:137`), keyboard tab selection (`DockGroupView.cs:64-68`) and
  `DockWorkspace.AddAsync` and `DuplicateAsync` all go straight to the workspace.
- So a consumer's `CloseRequested` holds only as long as everyone remembers to call `RunAsync`.
  Laurentia did not trust that. It also blocks `CloseTabs` in `OperationPolicy` and sets
  `CanReleaseAsync = (_, _, _) => Task.FromResult(_syncing)` (`PangaeaDockHost.cs:102, 162`), so the
  same rule is enforced three times.
- `CloseRequested` gets no help with actually closing. The handler has to know that it must call
  `ExecuteAsync` and not `RunAsync`, because `RunAsync` would call the handler again.

**Proposal:** Move both checks into the workspace, so that every route is checked.

```csharp
// Runtime
public Func<IReadOnlyList<string> /*slotIds*/, CancellationToken, Task<bool>>? CloseRequested { get; set; }
public Func<OperationPlan, string?>? CommitValidator { get; set; }   // the control installs Fits here
// ExecuteAsync(CloseTabs) calls CloseRequested unless invoked with ExecuteOptions.BypassCloseHandler.
```

Then either delete `RunAsync` or keep it as a thin alias that calls `ExecuteAsync`. **Breaking.**

### 2. Results are reported as a `bool` plus a string event, and the library's own logic matches strings

**Problem:**

- `ExecuteAsync`, `CommitAsync`, `UndoAsync` and the other commands return `Task<bool>`. The reason for a
  failure is sent separately through `Diagnostic` (`DockWorkspace.cs:62, 69-72, 99-100`).
- A caller cannot tell these outcomes apart: rejected by policy, layout locked, stale plan, a guard
  cancelled, or an exception was thrown. `Diagnostic` carries user-facing refusals ("Unlock the layout
  first.") and internal faults ("Presenter notification: ...") on the same channel. Laurentia writes all
  of them to stderr (`PangaeaDockHost.cs:221`).
- `WorkspaceChange(string Description, bool Structural)` does not say which operation ran, so code reads
  the English labels:
  - `DockWorkspaceControl.cs:55`: `change.Description == "Window placement"`
  - `DockWorkspaceControl.cs:159`: `plan.Description.StartsWith("Replace with ")`. The size check depends
    on how the engine words its labels.
  - `PangaeaDockHost.cs:214`: `change.Description == "Select tab"`

  If labels are ever localized or reworded, this behaviour breaks without any error.

**Proposal:**

```csharp
public enum DockOutcome { Committed, Rejected, Canceled, Stale, Busy, Failed }
public sealed record DockResult(DockOutcome Outcome, string? Message, OperationPlan? Plan, Exception? Exception = null)
{ public bool Success => Outcome == DockOutcome.Committed; }
Task<DockResult> ExecuteAsync(DockOperation op, CancellationToken ct = default);

public enum ChangeKind { Operation, Undo, Redo, Placement }
public sealed record WorkspaceChange(ChangeKind Kind, DockOperation? Operation, string Description, bool Structural);
```

- Keep `Diagnostic` for faults only, meaning `Failed` outcomes and exceptions thrown by handlers.
- Make `DockWorkspace.Report` internal (`DockWorkspace.cs:193`).

**Breaking.**

### 3. ID naming: one concept has two names, two concepts share one name, and every ID is a `string`

**Problem:**

- The type ID is called `PanelDescriptor.Id` in one place (`Geometry.cs:8`) and `PanelState.TypeId` in
  another (`Layout.cs:9`).
- The instance record is named `PanelState`, and it has a property also called `State`, so code reads
  `state.State`. `Layout.Panels` is keyed by instance ID, while `DockSlot.InstanceId` refers to the same
  value.
- The operations are named after tabs (`MoveTabs`, `CloseTabs`, `SelectTab`) but take `SlotIds`.
- Callbacks disagree about which ID they pass. `PanelActivated` passes an instance ID
  (`DockGroupView.cs:59`). `CloseRequested` passes slot IDs. `PanelTitle` and `ConfigureTab` pass a
  `PanelState`.
- ARCHITECTURE.md §2 already names these `SlotId`, `PanelTypeId` and `PanelInstanceId`. The code drifted
  away from that.
- Because every ID is a `string`, passing an instance ID where a slot ID is expected compiles. The
  result is a "The tab is no longer available" message at runtime.

**Proposal:** Align the code with the architecture now, while it is a rename and not a migration.

```csharp
public sealed record PanelDescriptor(string TypeId, string Title, ...);
public sealed record PanelInstance(string Id, string TypeId, string Data = "{}", int DataVersion = 1, string? DocumentId = null);
// DockLayout.Panels : ImmutableDictionary<string, PanelInstance>
```

- Choose one word for the operations, either `Tab` or `Slot`. `Tab` reads better, and the parameters
  would then be `TabIds`.
- Typed IDs (`readonly record struct SlotId(string Value)` with a JSON converter) can wait until P1, but
  rename the members before 0.1.

**Breaking**, including the JSON property names, so it should land before anyone stores files.

### 4. Saving the obvious way loses data

**Problem:** `workspace.Layout` looks like the thing to save, but its panel state is out of date.
`CaptureState()` only runs inside `Capture()` (`DockWorkspace.cs:143`).

**Proposal:** Rename `Layout` to `Arrangement`, or add `WorkspaceStore.Save(path, DockWorkspace)`,
which captures state itself. **Partly breaking** if `Layout` is renamed; the overload alone is not.

### 5. Saved state cannot carry a new version, and floating-window positions are not saved

**Problem:**

- `StateVersion` cannot be changed by a session. `Capture()` copies the old version
  (`DockWorkspace.cs:143-144`), and `CaptureState()` returns only a string. A panel that moves to a v2
  format gets `version == 1` on the next load.
- Floating-window positions are only saved if the consumer remembers to call
  `CaptureFloatingPositions()` first. Both the demo and Laurentia call it by hand.

**Proposal:**

```csharp
public virtual PanelData CaptureState() => new("{}", 1);   // record PanelData(string Json, int Version)
```

The control should update placement on `Window.PositionChanged` and `Resized`, throttled, or `Capture()`
should ask the presenter for it.

**Breaking** (`CaptureState` signature).

### 6. Theme overrides only work in one place, and the resource keys are not public

**Problem:**

- `DockWorkspaceControl` adds `new DockTheme()` to its own `Styles` (`DockWorkspaceControl.cs:45`), and
  every floating surface adds another (`:97`). Avalonia resolves an element's `Styles` resources before
  it looks at ancestors or `Application.Resources`. As a result, **application-level overrides are
  hidden**. The only override that works is setting `Resources` directly on the control and on each
  floating surface.
- That is exactly what Laurentia had to build: a resource subscription that writes eight keys into the
  control and into every `Surfaces` entry, plus `ConfigureSurface` for new floating windows, plus
  `UpdateFloatingThemes()` on every theme change (`PangaeaDockHost.cs:117-124, 457, 472-486`).
- `Pangaea.Dock.Hover` and `Pangaea.Dock.Line` are defined (`DockTheme.cs:21-23`) but never used.
- `Preview` is the same brush in both theme variants (`:16`).
- Keys are built as `"Pangaea.Dock." + token` and are not exposed as constants.

**Proposal:**

- Install `DockTheme` once, as an application-level style or in a merged dictionary the consumer opts
  into, and not on every element.
- Have floating surfaces inherit by sharing one `ResourceDictionary` that the control owns.
- Follow `ActualThemeVariantChanged` automatically.
- Publish the keys:

```csharp
public static class DockResourceKeys { public const string Background = "Pangaea.Dock.Background"; /* … */ }
public ResourceDictionary ThemeResources { get; }   // applied to the main and all floating surfaces
```

Either use `Hover` and `Line`, or delete them before people start depending on them. **Breaking** for
theming behaviour only.

### 7. Hook style is inconsistent, and one hook gets silently overwritten

**Problem:**

- `PreparePresentation` is a settable `Action<DockLayout>` property. The control attaches to it with `+=`
  (`DockWorkspaceControl.cs:49`). If a consumer assigns `PreparePresentation = ...`, the control's
  handler is removed without any warning.
- `Dispose()` sets the property to `null` (`DockWorkspace.cs:202`).
- Notifications use three different styles:
  - real events: `Changed`, `Diagnostic`, `StatusChanged`
  - settable `Action` properties: `PanelActivated`, `ConfigureSurface`, `ConfigureTab`
  - settable `Func` properties: `CanReleaseAsync`, `OperationPolicy`, `CloseRequested`

**Proposal:**

- Use events for notifications: `PanelActivated`, `SurfaceCreated`.
- Use single-assignment `Func` properties only for decisions: guard, policy, close, title, empty
  content, window factory.
- Make `PreparePresentation` internal, or turn it into an interface that the control registers through
  `DockWorkspace.AttachPresenter(IDockPresenter)`.

**Breaking.**

### 8. Some types are public only so the tests can reach them

**Problem:**

- `DockSurface`, `DockGroupView` (including `TabHeaders`), `CurrentPreviewPlan`,
  `CurrentPreviewRectangle`, `Surfaces` and `Ids` are public. The comment at
  `DockWorkspaceControl.cs:194` says this is "for geometry/interaction tests". ARCHITECTURE.md §9 says
  not to publish internal collections as an integration API.
- Laurentia already depends on them. It reads `GroupViews…IsKeyboardFocusWithin` to find the active
  group (`PangaeaDockHost.cs:216`). It casts `window.Content` to `DockSurface` to find out which host a
  floating window belongs to (`:464`).
- `OperationPlan` has a public constructor and `CommitAsync` is public. Together they let any caller
  build a candidate layout that `OperationPolicy` never sees.

**Proposal:**

- Make these types `internal`, and give the harness access with `InternalsVisibleTo`.
- First add the queries that integrators actually need:

```csharp
// DockWorkspace
string? ActiveInstanceId { get; }              // last activated panel, raised via Changed
bool TryGetSession<T>(string instanceId, out T session) where T : PanelSession;
DockSlot? FindSlot(string instanceId);  TabGroup? FindGroup(string slotOrInstanceId);
Task<DockResult> ActivateAsync(string instanceId);
// DockWorkspaceControl
bool TryGetView(string instanceId, out Control view);
Func<DockHost, Window>? FloatingWindowFactory { get; set; }   // receives its host
```

- Give `OperationPlan` an internal constructor.

**Breaking.**

---

## P1: should fix by 0.2

### 9. Registering a panel type takes two registries, a cast, and a group ID

**Problem:** The quick start takes these steps:

1. Register the panel in `PanelRegistry`.
2. Register its view in `PanelViewRegistry`, under the same string.
3. Cast `(NotesSession)session` in the view factory.
4. Create `DockWorkspace`.
5. Find a group ID (`workspace.Layout.Groups.First().Id`, as PACKAGE_README does).
6. Create `DockWorkspaceControl`.
7. Dispose the control and then the workspace, in that order.

If the two registries disagree, nothing fails; the user just sees a "provider unavailable" placeholder.

**Proposal:** Add the following without breaking anything:

```csharp
// Pangaea.Dock.Avalonia
public static void Register<TSession>(this PanelRegistry panels, PanelViewRegistry views, PanelDescriptor d,
    Func<PanelState, TSession> session, Func<TSession, Control> view) where TSession : PanelSession;
Task<bool> AddAsync(string typeId, string? groupId = null, string? replaceSlot = null);  // default: "primary" group
public const string PrimaryRegion = "primary";   // the string is repeated in Layout.cs:31, DockCommands.cs:102, DockWorkspace.cs:167, Laurentia
```

### 10. Objects that already exist do not fit the session model

**Problem:** Laurentia's panels already exist as view models. To use them it had to:

- register **a separate singleton type for every instance**,
- write an empty `BorrowedPanel` session, and
- unregister the type again when the instance closes (`PangaeaDockHost.cs:138-142, 279-292`).

That happens because view factories receive only the session, so they cannot tell which instance they
are building for.

**Proposal:** Pass the instance to the view factory:
`Func<PanelViewContext /*State, Session*/, Control>`.

Also allow `Register(descriptor)` without a factory, which would use an empty session. Then one
`"document"` type with `DocumentId` covers what Laurentia needs. This can be added alongside the
current API.

### 11. Synchronizing from outside the app needs history control and sync-over-async

**Problem:**

- Laurentia applies changes from outside with `ApplyLayout` and then calls `ClearHistory()`
  (`:133-136`). Otherwise undo can bring back a document the application has already closed.
- Undo and redo skip `OperationPolicy` entirely (`DockWorkspace.cs:122-129`), so they can re-add the
  kinds of operations the policy blocks.
- Laurentia blocks on `ExecuteAsync(...).GetAwaiter().GetResult()` twice (`:133, 151`). That only
  works because nothing in the call awaits.

**Proposal:**

- Add an `ExecuteOptions { RecordHistory = false }` argument, or a `ReplaceInventoryAsync`.
- Make history entries that add or remove instances pass through `OperationPolicy`.
- Add a synchronous `TrySelect` or `Activate` for operations that cannot be guarded.

### 12. Tabs are rebuilt on every change, and there is no way to report a title change

**Problem:**

- Every `Changed` notification, including `SelectTab`, rebuilds every surface, tab and menu
  (`DockWorkspaceControl.cs:72-94`). ARCHITECTURE.md §9 says not to do this.
- `ConfigureTab` runs again on every rebuild. Laurentia's dirty-state binding therefore subscribes on
  `AttachedToVisualTree` and unsubscribes on `DetachedFromVisualTree` to avoid leaks (`:243-267`).
- `PanelTitle` is read once per rebuild, and there is no way to say "this title changed."
- The floating window factory's `Title`, `MinWidth` and `MinHeight` are overwritten after the factory
  returns (`DockWorkspaceControl.cs:99-101`).

**Proposal:**

- Give `PanelSession` an optional `Title`, and maybe `IsDirty`, through `INotifyPropertyChanged`, and
  have the tab bind to them.
- Reconcile tabs by key instead of rebuilding them.
- Only set window properties the factory left at their defaults.

### 13. Built-in text cannot be localized

**Problem:** Menus, tooltips, empty-state copy ("A little room to explore", "Make room for an idea"),
and error messages are English literals spread through `DockCommands.cs`, `DockGroupView.cs` and
`DockEngine.cs`. Some of the copy is marketing text, which does not belong in a library default.

**Proposal:** Add a `DockStrings` class with overridable properties, passed to the control. Make the
default copy neutral ("No panels", "Add panel").

### 14. Public XML documentation is thin and is not published

**Problem:**

- `Directory.Build.props` does not set `GenerateDocumentationFile`.
- Every operation record, `PanelDescriptor` parameter, control property and `WorkspaceStore` method has
  no documentation.
- Some important facts are recorded only in ORIENTATION.md:
  - `Keywords` is matched as a single substring.
  - `Icon` is a text glyph, not an image.
  - `MinWidth` is in DIPs and also controls whether a replacement is allowed.
  - `X` and `Y` in `WindowPlacement` are pixels while `Width` and `Height` are DIPs.

**Proposal:** Turn the setting on, with CS1591 as a warning only in the three `src` projects. Add a
one-line `<summary>` to each public member. Skip summaries that only repeat the name.

---

## P2: nice to have

15. **Command descriptors.** ARCHITECTURE.md §9 promises "command descriptors so hosts can build native
    menus." At the moment menus are built privately inside the control. Expose something like
    `IReadOnlyList<DockCommand> GetTabCommands(slotId)` with `Title`, `IsEnabled`, `Gesture` and
    `ExecuteAsync`. This would also meet the suite's rule that every action must be reachable from a
    menu.
16. **Floating-window close behaviour.** Closing a floating window closes its panels
    (`DockWorkspaceControl.cs:104-112`). Most docking libraries move the panels back to the main window
    instead. Add a `FloatingCloseBehavior { ClosePanels, Redock }` option.
17. **Rail width.** `RailGroup.Extent` can only be changed by editing the layout. Add a resize handle,
    or a `ResizeRail` operation.
18. **Picker without a `Window`.** `ShowPickerAsync` does nothing when the control is not inside a
    `Window` (`DockCommands.cs:103`). Return `false`, or accept an owner as a parameter.
19. **`IsLayoutLocked` notifications.** Changing it raises no event, so toolbars that show the lock
    state have to poll. Raise `Changed` with `Kind = Lock`.

---

## Code readability

The code is consistent and short (about 1,800 lines). The problem is density. 65 source lines are
longer than 140 characters, and many lines hold several statements. That makes the code hard to scan
and hard to review.

### Specific issues

- **Misleading layout on one line.** `DockGroupView.cs:70` reads:
  `if (...) current.TabHeaders[...].Focus(); e.Handled = true;`
  The `e.Handled = true` runs whether or not the condition is true. The code is correct, but the
  layout suggests otherwise. Put one statement on each line, with braces whenever an `if` shares a line
  with anything else. Other examples: `DockWorkspace.cs:37, 73, 111`, `DockWorkspaceControl.cs:185`,
  `DockCommands.cs:30`.
- **One long method.** `DockEngine.Evaluate` (`DockEngine.cs:25-168`) is a 140-line `switch` with three
  local helper functions. Split it into one `private static DockLayout ApplyX(DockLayout, X)` per
  operation. Keep `Evaluate` as the try/validate/plan wrapper. This also makes each operation testable
  on its own.
- **Records packed onto single lines.** The operation records (`DockEngine.cs:6-16`) and
  `PanelDescriptor` (`Geometry.cs:8-10`, with 12 parameters) are hard to read. Use one parameter per
  line and add `<param>` docs.
- **Validation conditions that are too long.** Examples: `Layout.cs:108, 124-125, 158`. One message,
  "Invalid split weights.", covers an undefined axis, fewer than two children and a length mismatch.
  Split these into separate checks with exact messages. Integrators will see these messages when an
  import fails.
- **A boolean with three meanings.** `CommitInternalAsync(OperationPlan, bool? historyUndo, ...)`
  (`DockWorkspace.cs:67`) uses `null` for a normal commit, `true` for undo and `false` for redo. Use an
  enum: `CommitSource { Operation, Undo, Redo }`.
- **Short lambda names in nested queries.** Names like `d`, `g`, `n`, `s`, `p` are fine in short lambdas,
  but nested ones are hard to read: `DockWorkspaceControl.cs:172` (190 characters) and
  `DockCommands.cs:56`. Pull them out into `Descriptor(slotId)` and `Title(slotId)` helpers. The same
  `Registry.Descriptors.GetValueOrDefault(Layout.Panels[slot.InstanceId].TypeId)` chain appears 6 times.
- **Repeated literal sizes.**
  - The rail width `30` appears at `DockWorkspaceControl.cs:257, 278-279, 317-318`.
  - The tab height `34` is written out at `DockGroupView.cs:23, 38`, even though
    `DockGeometry.TabHeight` already exists.

  Name them as constants. Later, make them theme metrics.
- **Types in the wrong files.**
  - `PanelDescriptor` is in `Geometry.cs`.
  - The operation records are in `DockEngine.cs` (move them to `Operations.cs`).
  - `DockSurface` is inside `DockWorkspaceControl.cs`.
  - `Ids` is inside `Layout.cs`.

  Put one public type in each file, except for small record families.

### Comments

**Good comments, worth copying:**

- `DockWorkspace.cs:49`: why a singleton held by undo is reused.
- `DockCommands.cs:62-63`: why menus must read the current layout and not the last rendered geometry.
- `DockWorkspaceControl.cs:177`: layout undo deliberately leaves text undo alone.

**Missing, where the behaviour is not obvious:**

- `Layout.cs:89`: the `1e-12` tolerance in `Normalize`. The reason, avoiding floating-point drift and
  keeping normalization idempotent, is only written down in ORIENTATION.md. A future cleanup could
  "simplify" it away.
- `Layout.cs:69` and `DockEngine.cs:122`: a group with a `RegionKey` survives being emptied, and
  collapsing a group leaves an empty placeholder group behind. Neither fact is explained.
- `DockWorkspaceControl.cs:156-157`: the `Fits` rule allows a layout that is too big as long as it is
  no bigger than the current one. This rule is important, because it lets users work in a small window.
- `DockWorkspace.cs:146-172`: `ApplyArrangementAsync` matches panels by ID, then by type, then by
  retained singleton, and puts leftovers in `"primary"`. This needs a short summary of the algorithm.
- `DockWorkspace.cs:183`: `Collect` keeps a session alive while any undo or redo entry refers to it.

**Noise:** there is very little. Most comments are the right length. If anything, the public surface
has too few comments, not too many.

---

## Suggested order

1. Make the P0 renames: finding 3, plus the operation and hook renames from findings 2 and 7.
2. Add the result and change-kind types (finding 2).
3. Move `CloseRequested` and the size check into the workspace (finding 1).
4. Add the queries that replace public `DockSurface` access (finding 8).
5. Fix theming (finding 6).

This order lets Laurentia's adapter drop its workarounds one at a time. It can serve as the acceptance
test: after P0 and P1, `PangaeaDockHost` should not need `BorrowedPanel`, per-instance registrations,
`GetResult()`, `Description` string matches, per-surface resource copying, or the
`CanReleaseAsync`/`OperationPolicy` close workarounds.
