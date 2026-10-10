# Pangaea Dock

An independent Avalonia docking library. **0.1 is an evaluation preview, not a production release.**

`Pangaea.Dock.Core` contains the immutable layout and pure engine. `Pangaea.Dock.Runtime` owns
sessions, transactions, layout history and JSON storage. `Pangaea.Dock.Avalonia` provides the
controls, pointer interaction, floating hosts, menus, panel picker and neutral light/dark chrome.
No Laurentia, Dock.Avalonia, Material.Avalonia or ReactiveUI dependency is required.

Use .NET 10 and Avalonia 11.3.12. Keep your application's standard Avalonia control theme; the
library does not install an application theme. Create everything below on the UI thread:

```csharp
var panels = new PanelRegistry();
panels.Register(new PanelDescriptor("notes", "Notes"), _ => new NotesSession());
var views = new PanelViewRegistry();
views.Register("notes", session => new NotesView((NotesSession)session));
var workspace = new DockWorkspace(panels);
await workspace.AddAsync("notes", workspace.Layout.Groups.First().Id);
var docking = new DockWorkspaceControl(workspace, views);
window.Content = docking;
```

Namespaces: `Pangaea.Dock.Core`, `.Runtime`, `.Avalonia`. `NotesSession` is your nonvisual
`PanelSession` subclass; `NotesView` is your Avalonia `Control`. Each factory must return its
own session/control. Override `CaptureState` and `RestoreState` for panel view state. Use opaque
`PanelState.DocumentId` and the consumer-owned `CanReleaseAsync` guard for document-backed views.
Document editing, document undo, saving and recovery belong to the application.

Call commands through `DockWorkspaceControl.RunAsync` when host size constraints apply.
`DockWorkspace.Evaluate` returns an immutable plan; `CommitAsync` rejects stale revisions.
`UndoAsync`/`RedoAsync` restore arrangements and retained sessions, not old document edits.
Dispose the docking control first, then the workspace. Do not share one workspace across multiple
independent `DockWorkspaceControl` instances; the control owns its floating surfaces.

Built-in tabs support drag/split/group, Replace, Duplicate, Swap, Close and Float. The dotted
grip moves a group. Group menus expose collapse/pin and keyboard movement/resize routes.
Ctrl+Alt+Z/Y handle layout history. Escape cancels a drag. Override `Pangaea.Dock.*` resources
to integrate with an application's visual style.

`WorkspaceStore` serializes data-only version-1 layouts and uses temporary-sibling file replacement
with a last-good backup. `ApplyArrangementAsync` preserves live panels while applying a saved
arrangement. A raw `Capture` includes each panel's application-supplied state; sanitize private
data before sharing exports. Import size/depth/identity validation is included.

Remaining release work includes native macOS/mixed-DPI qualification, custom accessibility peers,
memory-budgeted retention, per-document singleton policy, portable export sanitization, schema
migrations and a multi-process storage coordinator. Automatic floating from a drop outside the
workspace is not implemented; use Move to New Window. Generic application document guards need
integration tests in the consuming application. No packages have been published.
