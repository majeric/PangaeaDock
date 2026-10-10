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
    var instances = w.Layout.Panels.Keys.ToHashSet(); int succeeded = 0;
    for (int i = 0; i < 240; i++)
    {
        var groups = w.Layout.Groups.ToArray(); var slots = w.Layout.Slots.ToArray();
        DockOperation operation = (i % 5) switch
        {
            0 => new FloatTabs([slots[random.Next(slots.Length)].Id]),
            1 => new CollapseGroup(groups[random.Next(groups.Length)].Id, DockSide.Left),
            _ => new MoveTabs([slots[random.Next(slots.Length)].Id], groups[random.Next(groups.Length)].Id, (DockSide)random.Next(5))
        };
        var bindings = w.Layout.Slots.ToDictionary(s => s.Id, s => s.InstanceId);
        if (await w.ExecuteAsync(operation)) succeeded++;
        LayoutTree.Validate(w.Layout);
        Check(w.Layout.Slots.Select(s => s.Id).ToHashSet().SetEquals(ids), "random operation preserves slots " + i);
        Check(w.Layout.Slots.All(s => bindings[s.Id] == s.InstanceId) && w.Layout.Panels.Keys.ToHashSet().SetEquals(instances), "random operation keeps each tab's panel " + i);
        var normalized = LayoutTree.Normalize(w.Layout);
        Check(WorkspaceStore.Serialize(normalized) == WorkspaceStore.Serialize(LayoutTree.Normalize(normalized)), "normalization idempotent " + i);
    }
    // Rejected operations still count as passes above, so require that most of the sequence really ran.
    Check(succeeded > 120, $"random sequence mostly succeeds ({succeeded}/240)");
    string Shape(DockLayout l) => WorkspaceStore.Serialize(l with { Revision = 0 });
    var final = Shape(w.Layout); int undone = 0;
    while (w.CanUndo) { Check(await w.UndoAsync(), "random history undo " + undone); undone++; }
    for (int i = 0; i < undone; i++) Check(await w.RedoAsync(), "random history redo " + i);
    Check(undone > 0 && Shape(w.Layout) == final, "undoing and redoing the whole random history restores the layout");
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

// Capability flags hold for every route, not only the matching command.
PanelRegistry Fixed()
{
    var registry = Registry();
    registry.Register(new("fixed", "Fixed", CanClose: false, CanFloat: false, CanCollapse: false), _ => new TestSession());
    return registry;
}
using (var w = new DockWorkspace(Fixed()))
{
    await w.AddAsync("fixed", Primary(w)); await w.AddAsync("text", Primary(w));
    string fix = w.Layout.Slots.First().Id, text = w.Layout.Slots.Last().Id;
    Check(await w.ExecuteAsync(new FloatTabs([text])), "ordinary panel floats");
    var floatGroup = w.Layout.Groups.First(g => g.Slots.Any(s => s.Id == text)).Id;
    Check(!await w.ExecuteAsync(new MoveTabs([fix], floatGroup)), "non-floating panel cannot be dragged into a floating window");
    Check(!await w.ExecuteAsync(new SwapPanels(fix, text)), "non-floating panel cannot be swapped into a floating window");
    Check(await w.ExecuteAsync(new MoveTabs([text], Primary(w))), "ordinary panel docks back");
    Check(await w.ExecuteAsync(new MoveTabs([text], Primary(w), DockSide.Right)), "split for rail test");
    var textGroup = w.Layout.Groups.First(g => g.Slots.Any(s => s.Id == text)).Id;
    Check(await w.ExecuteAsync(new CollapseGroup(textGroup, DockSide.Right)), "ordinary group collapses");
    Check(!await w.ExecuteAsync(new MoveTabs([fix], textGroup)), "non-collapsing panel cannot be dragged into a rail");
    Check(!await w.ExecuteAsync(new CloseTabs([fix])) && w.Layout.Slots.Any(s => s.Id == fix), "non-closing panel stays");
    var dropped = LayoutTree.Normalize(LayoutTree.Map(w.Layout, n => n is TabGroup g ? g with { Slots = g.Slots.Where(s => s.Id != fix).ToImmutableArray() } : n));
    Check(await w.ExecuteAsync(new ApplyLayout(dropped)), "consumer ApplyLayout may remove a panel it owns");
    var textInstance = w.Layout.Slot(text).InstanceId;
    var clash = w.Layout with { Panels = w.Layout.Panels.SetItem(textInstance, w.Layout.Panels[textInstance] with { TypeId = "single" }) };
    Check(!await w.ExecuteAsync(new ApplyLayout(clash)), "a layout reusing a live panel identity for another type is rejected");
}

// Collapsing the primary region and pinning it back restores one primary group, not an empty one beside it.
using (var w = new DockWorkspace(Registry()))
{
    await w.AddAsync("text", Primary(w)); var primary = w.Layout.Groups.Single();
    Check(primary.RegionKey == "primary" && await w.ExecuteAsync(new CollapseGroup(primary.Id, DockSide.Left)), "collapse primary");
    Check(await w.ExecuteAsync(new PinGroup(primary.Id)), "pin primary");
    var groups = LayoutTree.Groups(w.Layout.Hosts[0].Root).ToArray();
    Check(groups.Length == 1 && groups[0].RegionKey == "primary" && groups[0].Slots.Length == 1, "pinned primary keeps its region and leaves no empty group");
}
// A collapsed group returns beside its own neighbour; splitting beside an empty region group is allowed.
using (var w = new DockWorkspace(Registry()))
{
    for (int i = 0; i < 3; i++) await w.AddAsync("text", Primary(w));
    var slots = w.Layout.Slots.Select(s => s.Id).ToArray();
    await w.ExecuteAsync(new MoveTabs([slots[1]], Primary(w), DockSide.Right));
    var middle = w.Layout.Groups.First(g => g.Slots.Any(s => s.Id == slots[1])).Id;
    await w.ExecuteAsync(new MoveTabs([slots[2]], middle, DockSide.Right));
    var last = w.Layout.Groups.First(g => g.Slots.Any(s => s.Id == slots[2])).Id;
    Check(await w.ExecuteAsync(new CollapseGroup(last, DockSide.Right)) && w.Layout.Hosts[0].Rails[0].ReturnGroupId == middle, "rail returns beside its neighbour, not the first group");
    await w.ExecuteAsync(new PinGroup(last));
    Check(await w.ExecuteAsync(new CloseTabs([slots[0]])) && w.Layout.Group(Primary(w)).Slots.IsEmpty, "primary emptied");
    Check(await w.ExecuteAsync(new MoveTabs([slots[1]], Primary(w), DockSide.Left)), "can split beside an empty region group");
}
// History cleared while an undo waits for its release guard: the undo must not half-apply.
using (var w = new DockWorkspace(Registry()))
{
    await w.AddAsync("text", Primary(w));
    var gate = new TaskCompletionSource<bool>(); w.CanReleaseAsync = async (_, _, _) => await gate.Task;
    var revision = w.Layout.Revision; var undo = w.UndoAsync();
    w.ClearHistory(); gate.SetResult(true);
    Check(!await undo && w.Layout.Revision == revision && w.Layout.Slots.Count() == 1, "undo abandoned when its history entry is cleared");
}
using (var w = new DockWorkspace(Registry()))
{
    await w.AddAsync("text", Primary(w));
    Check(!await w.AddAsync("single", Primary(w), "no-such-slot"), "stale replacement slot is an error, not an exception");
    await w.ExecuteAsync(new FloatTabs([w.Layout.Slots.Single().Id]));
    var floating = w.Layout.Hosts.Single(h => h.Id != w.Layout.MainHostId).Id;
    w.UpdatePlacement(floating, new WindowPlacement(10, 10, 40, 30));
    Check(w.Layout.Hosts.Single(h => h.Id == floating).Placement!.Width == 100, "tiny floating window stores the minimum size instead of throwing");
    var revision = w.Layout.Revision; w.UpdatePlacement("gone", new WindowPlacement());
    Check(w.Layout.Revision == revision, "unknown host placement is ignored");
}

string folder = Path.GetFullPath(Path.Combine("artifacts", "tests", "Pangaea-tests-" + Guid.NewGuid().ToString("N"))); Directory.CreateDirectory(folder);
string path = Path.Combine(folder, "layout.json");
try
{
    var empty = DockLayout.Empty(); WorkspaceStore.Save(path, empty); WorkspaceStore.Save(path, empty with { Revision = 10 });
    Check(WorkspaceStore.Load(path).Revision == 10, "atomic write");
    File.WriteAllText(path, "not json");
    Check(WorkspaceStore.LoadWithBackup(path)?.Revision == 0, "last-good backup recovered");
    WorkspaceStore.Save(path, empty with { Revision = 3 });
    File.WriteAllText(path, WorkspaceStore.Serialize(empty).Replace("\"SchemaVersion\": 1", "\"SchemaVersion\": 99"));
    Check(WorkspaceStore.LoadWithBackup(path)?.Revision == 0, "backup recovered from a parseable but unsupported file");
    WorkspaceStore.Save(path, empty with { Revision = 4 });
    Check(WorkspaceStore.Load(path + ".bak").Revision == 0, "saving over a corrupt file keeps the last good backup");
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
