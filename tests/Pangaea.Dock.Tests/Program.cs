using System.Collections.Immutable;
using Pangaea.Dock.Core;
using Pangaea.Dock.Runtime;

int passed = 0;
void Check(bool condition, string label) { if (!condition) throw new Exception("FAIL: " + label); passed++; }
PanelRegistry Registry()
{
    var registry = new PanelRegistry();
    registry.Register(new("text", "Text"), _ => new TestSession());
    registry.Register(new("single", "Singleton", Singleton: true, CanDuplicate: false), _ => new TestSession());
    registry.Register(new("broken", "Broken"), _ => throw new InvalidOperationException("Factory failed on purpose"));
    return registry;
}
string Primary(DockWorkspace w) => w.Layout.Groups.First().Id;
using (var w = new DockWorkspace(Registry()))
{
    Check(await w.AddAsync("text", Primary(w)), "add");
    string a = w.Layout.Slots.Single().Id, originalGroup = Primary(w), originalInstance = w.Layout.Slot(a).InstanceId;
    var session = (TestSession)w.Session(originalInstance); session.Text = "live edits";
    Check(await w.AddAsync("text", originalGroup), "second panel"); string b = w.Layout.Slots.Last().Id;
    var before = WorkspaceStore.Serialize(w.Layout);
    var preview = w.Evaluate(new MoveTabs([b], originalGroup, DockSide.Right));
    Check(preview.Success && WorkspaceStore.Serialize(w.Layout) == before, "evaluate has no side effects");
    Check(await w.CommitAsync(preview.Plan!), "commit preview");
    Check(w.Layout.Groups.Count() == 2 && ReferenceEquals(session, w.Session(originalInstance)), "split preserves instance");
    var splitLayout = w.Layout;
    Check(!await w.CommitAsync(preview.Plan!), "stale commit rejected");
    Check(await w.UndoAsync() && w.Layout.Groups.Count() == 1, "undo split");
    Check(await w.RedoAsync() && w.Layout.Groups.Count() == 2, "redo split");
    Check(await w.AddAsync("single", originalGroup, a), "replace");
    Check(w.Layout.Slot(a).Id == a && w.Layout.Group(originalGroup).Slots[0].Id == a, "replacement keeps slot/group/order");
    session.Text = "later edits";
    Check(await w.UndoAsync() && ReferenceEquals(w.Session(originalInstance), session) && session.Text == "later edits", "layout undo does not undo content");
    var oldRevision = w.Layout.Revision;
    Check(!await w.AddAsync("broken", originalGroup, a) && w.Layout.Revision == oldRevision, "factory failure preserves layout/revision");
    Check(ReferenceEquals(session, w.Session(originalInstance)), "factory failure preserves original session");
    w.CanReleaseAsync = (_, _, _) => Task.FromResult(false);
    Check(!await w.ExecuteAsync(new CloseTabs([a])) && w.Layout.Slots.Any(s => s.Id == a), "cancel close preserves all state");
    w.CanReleaseAsync = null;
    Check(await w.ExecuteAsync(new SwapPanels(a, b)), "swap");
    Check(w.Layout.Slot(b).InstanceId == originalInstance, "swap bindings not slots");
    Check(await w.UndoAsync(), "undo swap");
    Check(await w.ExecuteAsync(new FloatTabs([a])), "float tab");
    Check(w.Layout.Hosts.Length == 2, "real floating host in model");
    var floatGroup = w.Layout.Groups.First(g => g.Slots.Any(s => s.Id == a));
    Check(await w.ExecuteAsync(new MoveTabs([a], w.Layout.Groups.First(g => g.Slots.Any(s => s.Id == b)).Id)), "dock back");
    Check(w.Layout.Hosts.Length == 1, "empty float removed");
    var group = w.Layout.Groups.First(g => g.Slots.Any(s => s.Id == a));
    Check(await w.ExecuteAsync(new CollapseGroup(group.Id, DockSide.Right)), "collapse group");
    Check(w.Layout.Hosts[0].Rails.Length == 1, "rail owns group");
    Check(await w.ExecuteAsync(new PinGroup(group.Id)), "pin group");
    Check(w.Layout.Hosts[0].Rails.Length == 0 && w.Layout.Slots.Count() == 2, "pin conserves panels");
    var json = WorkspaceStore.Serialize(w.Capture()); var roundtrip = WorkspaceStore.Deserialize(json);
    Check(WorkspaceStore.Serialize(roundtrip) == json, "data-only round trip");
    Check(!json.Contains("$type"), "no CLR type metadata");
    Check(await w.DuplicateAsync(a) && w.Layout.Slots.Count() == 3, "duplicate creates another instance");
    Check(await w.AddAsync("single", Primary(w)), "add singleton");
    Check(!await w.AddAsync("single", Primary(w)), "singleton enforced across hosts");
    w.IsLayoutLocked = true;
    Check(!await w.ExecuteAsync(new FloatTabs([a])), "layout lock checked centrally");
    Check(await w.ExecuteAsync(new SelectTab(a)), "selection allowed while locked");
}

// Pending asynchronous guards block reentrancy; cancellation must not partially close multiple tabs.
using (var w = new DockWorkspace(Registry()))
{
    await w.AddAsync("text", Primary(w)); await w.AddAsync("text", Primary(w));
    var gate = new TaskCompletionSource<bool>();
    w.CanReleaseAsync = async (_, _, _) => await gate.Task;
    var closing = w.ExecuteAsync(new CloseTabs(w.Layout.Slots.Select(s => s.Id).ToImmutableArray()));
    Check(!await w.AddAsync("text", Primary(w)), "overlapping transactions rejected");
    gate.SetResult(false); Check(!await closing && w.Layout.Slots.Count() == 2, "batch close cancel is atomic");
}
using (var w = new DockWorkspace(Registry()) { HistoryLimit = 1 })
{
    await w.AddAsync("text", Primary(w)); var id = w.Layout.Slots.Single().InstanceId; var s = (TestSession)w.Session(id);
    await w.ExecuteAsync(new CloseTabs([w.Layout.Slots.Single().Id])); Check(!s.Disposed, "history retains closed content");
    await w.AddAsync("text", Primary(w)); Check(s.Disposed && s.DisposeCount == 1, "eviction disposes exactly once");
}

// Fixed seed property exercise: arbitrary sequences must never lose, duplicate or orphan moved slots.
using (var w = new DockWorkspace(Registry()))
{
    for (int i = 0; i < 8; i++) await w.AddAsync("text", Primary(w));
    var ids = w.Layout.Slots.Select(s => s.Id).ToHashSet(); var random = new Random(20261010);
    for (int i = 0; i < 240; i++)
    {
        var groups = w.Layout.Groups.ToArray(); var slots = w.Layout.Slots.ToArray();
        DockOperation operation = (i % 5) switch
        {
            0 => new FloatTabs([slots[random.Next(slots.Length)].Id]),
            1 => new CollapseGroup(groups[random.Next(groups.Length)].Id, DockSide.Left),
            _ => new MoveTabs([slots[random.Next(slots.Length)].Id], groups[random.Next(groups.Length)].Id, (DockSide)random.Next(5))
        };
        await w.ExecuteAsync(operation); LayoutTree.Validate(w.Layout);
        Check(w.Layout.Slots.Select(s => s.Id).ToHashSet().SetEquals(ids), "random operation preserves slots " + i);
        var normalized = LayoutTree.Normalize(w.Layout);
        Check(WorkspaceStore.Serialize(normalized) == WorkspaceStore.Serialize(LayoutTree.Normalize(normalized)), "normalization idempotent " + i);
    }
}
var allocation = DockGeometry.Allocate(1000, [0.1, 0.9], [200, 100]);
Check(allocation[0] == 200 && allocation[1] == 800, "independent minimum allocation fixture");

using (var w = new DockWorkspace(Registry()))
{
    await w.AddAsync("single", Primary(w)); var singletonId = w.Layout.Slots.Single().InstanceId;
    var singleton = w.Session(singletonId); var template = w.Layout;
    await w.ExecuteAsync(new CloseTabs([w.Layout.Slots.Single().Id]));
    await w.AddAsync("single", Primary(w));
    Check(w.Layout.Slots.Single().InstanceId == singletonId && ReferenceEquals(w.Session(singletonId), singleton), "reopen retained singleton reuses session");
    var slotId = w.Layout.Slots.Single().Id;
    await w.AddAsync("text", Primary(w), slotId);
    var notesInstance = w.Layout.Slot(slotId).InstanceId;
    Check(await w.ApplyArrangementAsync(template, "original"), "apply template after replacing original slot");
    Check(w.Layout.Panels.ContainsKey(notesInstance) && w.Layout.Panels.ContainsKey(singletonId), "workspace application conserves unmatched live panel");
    Check(w.Layout.Slots.Select(s => s.Id).Distinct().Count() == 2, "workspace application remaps colliding slot IDs");
    Check(ReferenceEquals(w.Session(singletonId), singleton), "template reuses dormant singleton");
    w.PreparePresentation = _ => throw new InvalidOperationException("View construction failed");
    var before = WorkspaceStore.Serialize(w.Layout);
    Check(!await w.AddAsync("text", Primary(w)) && WorkspaceStore.Serialize(w.Layout) == before, "presentation preparation failure rolls back");
}
foreach (var invalid in new[]
{
    "{\"Format\":\"pangaea-dock\",\"SchemaVersion\":1,\"Layout\":null}",
    "{\"Format\":\"pangaea-dock\",\"SchemaVersion\":1,\"Layout\":{\"Id\":\"x\",\"MainHostId\":\"m\",\"Hosts\":[null],\"Panels\":{}}}"
})
{
    bool rejected = false; try { WorkspaceStore.Deserialize(invalid); } catch (InvalidDataException) { rejected = true; }
    Check(rejected, "malformed null data rejected safely");
}

string folder = Path.GetFullPath(Path.Combine("artifacts", "tests", "Pangaea-tests-" + Guid.NewGuid().ToString("N"))); Directory.CreateDirectory(folder);
string path = Path.Combine(folder, "layout.json");
try
{
    var empty = DockLayout.Empty(); WorkspaceStore.Save(path, empty); WorkspaceStore.Save(path, empty with { Revision = 10 });
    Check(WorkspaceStore.Load(path).Revision == 10, "atomic write");
    File.WriteAllText(path, "not json");
    Check(WorkspaceStore.LoadWithBackup(path)?.Revision == 0, "last-good backup recovered");
    bool rejected = false;
    try { WorkspaceStore.Deserialize(WorkspaceStore.Serialize(empty).Replace("\"SchemaVersion\": 1", "\"SchemaVersion\": 99")); }
    catch (InvalidDataException) { rejected = true; }
    Check(rejected, "future schema rejected");
}
finally { foreach (var file in Directory.GetFiles(folder)) File.Delete(file); Directory.Delete(folder); }
Console.WriteLine($"PASS: {passed} core/runtime checks; random seed 20261010.");

sealed class TestSession : PanelSession
{
    public string Text = "";
    public bool Disposed; public int DisposeCount;
    public override string CaptureState() => System.Text.Json.JsonSerializer.Serialize(Text);
    public override void RestoreState(string json, int version) { if (json.StartsWith('"')) Text = System.Text.Json.JsonSerializer.Deserialize<string>(json)!; }
    public override void Dispose() { Disposed = true; DisposeCount++; }
}
