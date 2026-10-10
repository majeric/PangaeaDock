# Pangaea Dock usage guide

This guide covers the API as it is in `0.1.0-preview.1`. Every snippet is written against the current
signatures in `src/`. If a snippet looks awkward, the awkwardness is tracked in
[API_REVIEW.md](API_REVIEW.md).

Packages and namespaces: `Pangaea.Dock.Core` (immutable model, pure engine), `Pangaea.Dock.Runtime`
(sessions, transactions, history, storage) and `Pangaea.Dock.Avalonia` (controls). You need .NET 10 and
Avalonia 11.3.12, and your app must load a normal control theme such as `FluentTheme`. Create and use
everything on the UI thread.

## 1. Concepts

A **layout** (`DockLayout`) is an immutable record. It contains one or more **hosts**: the main window
plus any floating windows. Each host has a tree of **splits** and **tab groups**, and may also have
**rails**, which are groups collapsed to an edge. A tab group holds **slots**. A slot is a tab position,
and each slot points to one **panel instance** (`PanelState`). A panel instance has a **type**, which
you register once with a **descriptor**. At runtime each instance owns a nonvisual **session**
(`PanelSession`), which you subclass. Each instance also gets one Avalonia **view** (`Control`). The
**workspace** (`DockWorkspace`) holds the current layout, the sessions and the undo history. The
**control** (`DockWorkspaceControl`) draws the workspace and handles input.

```text
DockWorkspaceControl ──renders──▶ DockWorkspace ──owns──▶ PanelSession (one per instance)
                                       │
                                       ▼ Layout (DockLayout, immutable, revisioned)
                 Hosts[] ── DockHost(Root, Rails[], Placement?)
                               Root = SplitNode(Axis, Children[], Weights[]) | TabGroup
                               TabGroup(Slots[], ActiveSlotId, RegionKey)
                                        └─ DockSlot(Id, InstanceId) ──▶ Panels[InstanceId] = PanelState(Id, TypeId, State, StateVersion, DocumentId)
```

| Term | Type | ID you pass around | Survives |
|---|---|---|---|
| Panel type | `PanelDescriptor` | `PanelDescriptor.Id` = `PanelState.TypeId` (e.g. `"notes"`) | app releases |
| Panel instance | `PanelState` | `PanelState.Id` = `DockSlot.InstanceId` | moving, swapping, floating |
| Slot (tab) | `DockSlot` | `DockSlot.Id` | Replace With (new instance, same slot) |
| Tab group | `TabGroup` | `TabGroup.Id` | until it empties (unless it has a `RegionKey`) |
| Split | `SplitNode` | `SplitNode.Id` | until fewer than two children remain |
| Host | `DockHost` | `DockHost.Id`; the main host is `Layout.MainHostId` | until its last tab leaves |
| Session | `PanelSession` | looked up by instance ID | while live or referenced by undo |
| Region | `TabGroup.RegionKey` | `"primary"` = the default target group | kept even when empty |

Operations (`MoveTabs`, `CloseTabs`, `SelectTab` and so on) take **slot IDs**. `Session(...)`,
`PanelActivated` and `PanelTitle` work with **instance IDs**. Keep track of which kind you hold.

```csharp
// Lookups you will need often (no built-in helpers yet).
static string PrimaryGroupId(DockLayout l) => (l.Groups.FirstOrDefault(g => g.RegionKey == "primary") ?? l.Groups.First()).Id;
static DockSlot SlotOf(DockLayout l, string instanceId) => l.Slots.First(s => s.InstanceId == instanceId);
static TabGroup GroupOf(DockLayout l, string slotId) => l.Groups.First(g => g.Slots.Any(s => s.Id == slotId));
```

## 2. Quick start

```csharp
using System.Text.Json;
using Avalonia.Controls;
using Pangaea.Dock.Avalonia;
using Pangaea.Dock.Core;
using Pangaea.Dock.Runtime;

public sealed class NotesSession : PanelSession
{
    public string Text { get; set; } = "";
    public override string CaptureState() => JsonSerializer.Serialize(new { Text });
    public override void RestoreState(string json, int version)   // also called with "{}" for new panels
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("Text", out var text)) Text = text.GetString() ?? "";
    }
}

public sealed class MainWindow : Window
{
    private readonly DockWorkspace _workspace;
    private readonly DockWorkspaceControl _docking;

    public MainWindow()
    {
        var panels = new PanelRegistry();                     // nonvisual: descriptor + session factory
        panels.Register(new PanelDescriptor("notes", "Notes", Icon: "≡", MinWidth: 220), _ => new NotesSession());

        var views = new PanelViewRegistry();                  // visual: same type ID, one new Control per instance
        views.Register("notes", session =>
        {
            var notes = (NotesSession)session;
            var box = new TextBox { Text = notes.Text, AcceptsReturn = true };
            box.TextChanged += (_, _) => notes.Text = box.Text ?? "";
            return box;
        });

        _workspace = new DockWorkspace(panels);               // starts from DockLayout.Empty(): one empty "primary" group
        _docking = new DockWorkspaceControl(_workspace, views);
        Content = _docking;

        _ = _workspace.AddAsync("notes", PrimaryGroupId(_workspace.Layout));
        Closed += (_, _) => { _docking.Dispose(); _workspace.Dispose(); };   // control first, then workspace
    }
}
```

That is the whole setup. Tabs can be dragged, split, floated, replaced and collapsed immediately.
`Ctrl+Alt+Z` and `Ctrl+Alt+Y` undo and redo layout changes.

Notes:

- Register each type ID in **both** registries. A type with no view factory shows a "Panel provider
  unavailable" placeholder. A type with no session factory gets a `MissingPanelSession`, which keeps its
  saved state.
- A view factory must return a **new, unattached** control on every call. Views are created when a tab
  first becomes active. They are kept while the session lives, and disposed if they implement
  `IDisposable`.
- Use one workspace per control.

### Starting from a default layout

```csharp
using System.Collections.Immutable;

static DockLayout DefaultLayout() => new("my-app", "main",
    [new DockHost("main",
        new SplitNode("columns", SplitAxis.Horizontal,
            [new TabGroup("left", [new DockSlot("s-tree", "p-tree")]),
             new TabGroup("center", [new DockSlot("s-notes", "p-notes")], "s-notes", RegionKey: "primary")],
            [0.25, 0.75]),
        [])],
    ImmutableDictionary<string, PanelState>.Empty
        .Add("p-tree", new PanelState("p-tree", "tree"))
        .Add("p-notes", new PanelState("p-notes", "notes")));

var workspace = new DockWorkspace(panels, DefaultLayout());   // throws InvalidDataException on an invalid layout
```

Validation rules include the following:

- Host, node and slot IDs share one namespace and must be unique.
- Every slot must have exactly one `Panels` entry, and every entry must have exactly one slot.
- A split needs at least two children, and its weights must be positive and sum to 1.
- A singleton type may appear only once.

## 3. Changing the layout from code

You can send commands through two entry points:

| Call | Use when |
|---|---|
| `docking.RunAsync(op)` | The command comes from the UI. It applies `CloseRequested` and the "does it fit on screen" check. |
| `workspace.ExecuteAsync(op)` | There is no control, or you are inside your own `CloseRequested` handler. This path skips both. |

Both return `Task<bool>`. When a call returns `false`, the reason goes to `workspace.Diagnostic`, and
also to `docking.StatusChanged` and `docking.LastMessage` if a control is attached.

```csharp
var layout = workspace.Layout;
var slot = SlotOf(layout, instanceId);

await docking.RunAsync(new SelectTab(slot.Id));                                   // not recorded in undo
await docking.RunAsync(new MoveTabs([slot.Id], targetGroupId));                   // join as last tab
await docking.RunAsync(new MoveTabs([slot.Id], targetGroupId, DockSide.Center, Index: 0));
await docking.RunAsync(new MoveTabs([slot.Id], targetGroupId, DockSide.Right));   // split beside target
await docking.RunAsync(new SwapPanels(slot.Id, otherSlotId));                     // exchange contents, keep positions
await docking.RunAsync(new CloseTabs([slot.Id]));
await docking.RunAsync(new ResizeSplit(splitId, [0.3, 0.7]));                     // weights are renormalized

await workspace.AddAsync("notes", groupId);                                       // new instance, default state
await workspace.AddAsync("inspector", groupId, replaceSlot: slot.Id);             // Replace With: same slot, new panel
await workspace.DuplicateAsync(slot.Id);                                          // copies CaptureState(); throws if slot is gone

// Open a panel bound to one of your documents (DocumentId is opaque to the library):
await docking.RunAsync(new AddPanel(new PanelState(Ids.New(), "editor", DocumentId: doc.Id), groupId));
// ...and read it in the session factory:
panels.Register(new PanelDescriptor("editor", "Editor"), state => new EditorSession(documents.Open(state.DocumentId!)));
```

- If you want a plan without committing it, call `workspace.Evaluate(op)`. It returns an
  `OperationResult` with `Plan` and `Error`. `workspace.CommitAsync(plan)` then commits it, and refuses if
  the layout revision has changed since.
- While one command is still awaiting a guard, any other command is **rejected**, not queued.
- `AddPanel` for a singleton type that is already open is rejected. The built-in picker activates the
  existing tab instead. If undo is still holding a closed singleton, reopening it reuses that session.

## 4. Sessions and panel state

```csharp
var notes = (NotesSession)workspace.Session(instanceId);   // KeyNotFoundException if not retained
bool alive = workspace.IsRetained(instanceId);              // live, or kept alive by undo/redo
```

- `RestoreState(json, version)` runs once, when the session is created. It gets the saved `State` and
  `StateVersion`. A new panel gets `"{}"` and version `1`.
- `CaptureState()` only runs when you call `workspace.Capture()` or `DuplicateAsync`. That means
  `workspace.Layout.Panels[id].State` still holds the state the panel was created with.
- `StateVersion` is copied unchanged when state is captured. Your session cannot raise it. If you
  version your state format, put a version field inside your JSON.
- `Dispose()` runs when no live slot and no undo/redo entry refers to the instance any more. It also
  runs when the workspace is disposed.

## 5. Close guards and consumer-owned close

There are two hooks, and they run at different points.

**`workspace.CanReleaseAsync`** is called for every instance a commit would remove. That includes close,
Replace With, applying a layout or arrangement, and undo or redo of an add. Return `false` to cancel the
whole operation.

```csharp
workspace.CanReleaseAsync = async (state, session, ct) =>
    session is not EditorSession { IsDirty: true } editor || await AskSaveOrDiscardAsync(editor, ct);
```

**`docking.CloseRequested`** takes over every close the UI starts: the tab's × button, the Close and
Close group menu items, and closing a floating window. It receives **slot IDs**. Nothing closes unless
your handler closes it, using `workspace.ExecuteAsync`, because `RunAsync` would call your handler again.

```csharp
docking.CloseRequested = async slotIds =>
{
    if (!await myShell.ConfirmCloseAsync(slotIds)) return false;
    return await workspace.ExecuteAsync(new CloseTabs(slotIds));
};
```

Undo can put a closed panel back. If closing a panel also closes the document behind it, call
`workspace.ClearHistory()` afterwards, so that undo cannot bring the panel back with no document.

## 6. Floating windows

```csharp
await docking.RunAsync(new FloatTabs([slot.Id]));                                          // default 640x480 at (120,120)
await docking.RunAsync(new FloatTabs([slot.Id], new WindowPlacement(X: 200, Y: 200, Width: 800, Height: 600)));
docking.FloatingWindowFactory = () => new Window { Icon = myIcon };   // optional base window
docking.UpdateFloatingThemes();                                     // after changing the theme variant at runtime
```

- In `WindowPlacement`, `X` and `Y` are **screen pixels**, while `Width` and `Height` are DIPs. Restored
  windows are kept inside the nearest screen's working area.
- The library sets each floating window's `Title`, `Width`, `Height`, `MinWidth`, `MinHeight`, `Content`
  and `RequestedThemeVariant` after calling the factory. Setting these in the factory has no effect.
- Closing a floating window **closes its panels**; they do not return to the main window. This goes
  through `CloseRequested` when it is set.
- Moving a floating window does not update the layout. Call `docking.CaptureFloatingPositions()` before
  you save.
- Dropping a tab outside the workspace does nothing. Users float tabs with **Move to new window** or
  **Float group**.

## 7. Border rails: collapse and pin

```csharp
await docking.RunAsync(new CollapseGroup(groupId, DockSide.Left));   // group becomes an icon on the left edge
await docking.RunAsync(new PinGroup(groupId));                       // put it back into the tree
```

- Clicking a rail icon opens that group as an overlay, `RailGroup.Extent` wide (300 by default). Escape,
  clicking outside or moving focus away closes it.
- Pinning puts the group back beside `RailGroup.ReturnGroupId` (or the host's first group), on the rail's side.
- A group must be pinned before you can split beside it.
- Known issue: collapsing and then pinning the `"primary"` group leaves an empty primary group behind
  (see PROGRESS.md).

## 8. Layout undo, redo and locking

```csharp
await workspace.UndoAsync();  await workspace.RedoAsync();
bool can = workspace.CanUndo;  string? label = workspace.UndoDescription;   // e.g. "Split right"
workspace.HistoryLimit = 50;                                               // entries, default 100
workspace.ClearHistory();                                                  // also releases sessions only history kept alive
workspace.IsLayoutLocked = true;    // only SelectTab is allowed; drags, undo and menus are rejected
docking.AllowLayoutHistory = false; // hides Undo/Redo menu items and disables Ctrl+Alt+Z/Y
```

- History covers the **arrangement** and the panel instances in it. It never undoes document edits.
- Selecting a tab is not recorded.
- Changing `IsLayoutLocked` does not raise `Changed`, so update your own UI when you change it.

## 9. Persistence and arrangements

```csharp
// Save (e.g. on Closing, or debounced on workspace.Changed):
docking.CaptureFloatingPositions();
WorkspaceStore.Save(path, workspace.Capture());     // Capture(), not Layout — Layout has stale panel state

// Load:
DockLayout initial;
try { initial = WorkspaceStore.LoadWithBackup(path, message => log(message)) ?? DefaultLayout(); }
catch (InvalidDataException) { initial = DefaultLayout(); }     // invalid-but-parseable files currently throw
var workspace = new DockWorkspace(panels, initial);
```

- `Save` checks the layout, writes a temporary file next to the target, then replaces the target. The
  previous file is kept as `path + ".bak"`.
- `LoadWithBackup` tries the main file, then the `.bak` file, and returns `null` if neither loads.
- Input is limited to 5 MiB, 32 hosts, 2048 items, a nesting depth of 32 and 256 KiB of state per panel.
- The JSON is wrapped in `{ Format: "pangaea-dock", SchemaVersion: 1, Layout }`.
- Use `Serialize(layout)` and `Deserialize(json)` for in-memory round trips.
- `Capture()` includes each panel's state, which may be private user content. Remove it before sharing an export.

**Arrangements** are named layouts that users switch between without losing their open panels:

```csharp
named["Focus"] = workspace.Capture();                                  // save the current arrangement
await workspace.ApplyArrangementAsync(named["Focus"], "Focus");        // undo label: "Switch to Focus"
```

`ApplyArrangementAsync` matches each template panel to a live panel, first by instance ID and then by
type. Matched panels keep their sessions and live state. Template panels with no match are created
fresh, using the template's state. Live panels the template does not mention are added to the
`"primary"` group, so none are closed. In contrast, `ExecuteAsync(new ApplyLayout(layout))` replaces
the whole layout and releases any panel that is not in it.

## 10. Theming

`DockWorkspaceControl` installs its own `DockTheme`, with light and dark brushes. Override the brushes
on the **control's `Resources`**. Avalonia checks an element's `Resources` before its `Styles`, so the
library's `DockTheme` hides overrides placed at window or application level. Floating windows are
separate visual trees, so set the same brushes on each floating surface as well:

```csharp
docking.Resources["Pangaea.Dock.Accent"] = Brushes.Orange;
docking.ConfigureSurface = surface => surface.Resources["Pangaea.Dock.Accent"] = Brushes.Orange;  // floating hosts only
// Per theme variant:
docking.Resources.ThemeDictionaries[ThemeVariant.Dark] = new ResourceDictionary { ["Pangaea.Dock.Accent"] = darkAccent };
```

| Key | Used for |
|---|---|
| `Pangaea.Dock.Background` | Host background, tab strip, inactive tabs |
| `Pangaea.Dock.Surface` | Group body, active tab, picker dialog |
| `Pangaea.Dock.Text` / `.Muted` | Active / inactive tab text, grip, hints |
| `Pangaea.Dock.Accent` | Active tab underline, drop-preview outline |
| `Pangaea.Dock.Preview` | Drop-preview fill (one brush for both variants) |
| `Pangaea.Dock.Hover`, `.Line` | Defined, but nothing uses them yet |

Sizes are fixed: tabs are 34 px high (`DockGeometry.TabHeight`), splitters 6 px (`DockGeometry.SplitterSize`)
and rails 30 px. Standard controls such as menus, buttons, the picker's TextBox and ListBox use your
application's control theme. To customise individual tabs, use `ConfigureTab`, `PanelTitle` and
`EmptyGroupContent` (see §12).

## 11. Operation policy

`OperationPolicy` lets you veto commands. Return a reason to reject the command, or `null` to allow it.
The policy is checked during `Evaluate`, so it runs whenever a menu is built and on **every pointer
move during a drag**. Keep it fast and free of side effects.

```csharp
workspace.OperationPolicy = op => op switch
{
    CloseTabs => "Close documents from the File menu.",
    MoveTabs m when m.Side != DockSide.Center && locked => "Splitting is disabled in this mode.",
    _ => null
};
```

Undo and redo bypass the policy, because they skip `Evaluate`. If the policy blocks adding or closing
panels, also disable or clear history. Descriptor flags (`CanClose`, `CanFloat`, `CanCollapse`,
`CanDuplicate`, `Singleton`) are checked by the engine for each operation. PROGRESS.md lists the ones
that are currently missed.

## 12. Control options

| Member | Purpose |
|---|---|
| `AllowPanelCreation` | Show Add panel, Replace with and Duplicate menu items, and the empty-group button (default true) |
| `AllowLayoutHistory` | Undo and redo menu items and shortcuts (default true) |
| `ShowCloseButtons` | Show × on tabs whose descriptor has `CanClose` (default false) |
| `PanelTitle` | `Func<PanelState, string>`: dynamic tab title (otherwise the descriptor's `Title`) |
| `ConfigureTab` | `Action<PanelState, Control tab, TextBlock title>`: runs each time a tab is built (on every change) |
| `EmptyGroupContent` | `Func<TabGroup, Control?>`: replaces the "A little room to explore" placeholder |
| `PanelActivated` | `Action<string instanceId>`: a tab or its content was clicked, focused or selected by keyboard |
| `StatusChanged` / `LastMessage` | Hints, results and errors for a status bar |
| `ShowPickerAsync(groupId?, replaceSlot?)` | The searchable Add/Replace dialog (needs a `Window` owner) |
| `Refresh()` | Rebuild every surface from `workspace.Layout` |

Built-in user interaction:

- Dragging a tab moves it. Dragging the ⠿ grip moves the whole group. Escape cancels a drag.
- Right-clicking a tab, or pressing Shift+F10, opens the tab menu.
- The group's ⋯ button opens the group menu, which includes keyboard routes to move, split and resize.
- In a tab strip, arrow keys select the previous or next tab, and Enter or Space selects the focused tab.

## 13. Integrating with an existing application (the Laurentia pattern)

`Laurentia/Dock/PangaeaDockHost.cs` puts Pangaea in front of an application that already has its own
list of documents and palettes. The application owns opening, closing and saving. Pangaea only owns
where things are placed. The steps are:

1. **Turn the app's model into a `DockLayout`** (`ImportModel`). Use stable IDs (`panel:`, `group:`,
   `slot:`). Mark the document area with `RegionKey: "primary"`.
2. **Register one panel type per existing object**, as a singleton with no duplicates. Use an empty
   `PanelSession` subclass, because the object already exists. The view factory closes over that object.
3. **Turn off library-owned lifetimes:**
   - Set `AllowPanelCreation = false`.
   - Set `OperationPolicy` to reject `AddPanel`, `ApplyLayout`, `SwapPanels` and `CloseTabs` from outside.
   - Set `CanReleaseAsync` to allow a release only while the adapter itself is synchronizing.
   - Route `CloseRequested` to the shell's own close command, which shows Save/Discard.
4. **Keep the layout in step with the app** (`Reconcile`). Drop panels the app has closed. Add new ones
   to their original group, or else to the `"primary"` group. Apply the result with
   `ExecuteAsync(new ApplyLayout(next, ...))`, then call `ClearHistory()` so that undo cannot bring back
   a closed document.
5. **Report activation back to the app** with `PanelActivated`, and with `Window.Activated` on floating
   windows. Show live titles and dirty state by binding inside `ConfigureTab`.
6. **Map theme brushes:** copy the app's semantic brushes into `Pangaea.Dock.*` on the control and on
   every surface, and call `UpdateFloatingThemes()` when the theme variant changes.
7. **Persist only the arrangement:** call `CaptureFloatingPositions()`, use `LayoutTree.Map` to remove
   document slots, then `LayoutTree.Normalize`.

## 14. Troubleshooting

| Symptom | Cause / fix |
|---|---|
| Saved file has stale panel contents | You saved `workspace.Layout`; save `workspace.Capture()`. |
| Floating windows reopen at old positions | Call `docking.CaptureFloatingPositions()` before `Capture()`. |
| "A workspace operation is already pending" | A guard (`CanReleaseAsync`) is still awaiting; commands are not queued. |
| "The layout changed. Please try the operation again." | A plan from `Evaluate` was committed after another change; evaluate again. |
| "There isn't enough room here" | `RunAsync` size check: the new layout's minimum size exceeds the window. Lower `MinWidth`/`MinHeight` or resize. |
| Close button bypasses my confirmation | You called `workspace.ExecuteAsync(new CloseTabs(...))` from UI code; use `docking.RunAsync` so `CloseRequested` runs. |
| Theme overrides ignored | Put them on `docking.Resources` (and floating surfaces via `ConfigureSurface`), not `Application.Resources`. |
| Floating window title is "Pangaea Dock · Floating panels" | The library overwrites the factory's title; set it later (e.g. in `Opened`). |
| "A panel factory must return an unattached control" | Your view factory returned a cached/shared control. Return a new one per call. |
| `LoadWithBackup` throws | Known bug: validation errors (`InvalidDataException`) escape instead of falling back. Catch it. |
| Text-box Ctrl+Z undoes layout | It doesn't; layout history is Ctrl+Alt+Z/Y only, by design. |
