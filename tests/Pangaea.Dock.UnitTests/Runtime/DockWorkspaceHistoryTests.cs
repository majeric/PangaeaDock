using Pangaea.Dock.Core;
using Pangaea.Dock.Runtime;
using Pangaea.Dock.Tests.Support;

namespace Pangaea.Dock.Tests.Runtime;

public class DockWorkspaceHistoryTests
{
    [Fact]
    public async Task Undo_RestoresPreviousArrangementAndRedoReappliesIt()
    {
        using var f = new WorkspaceFixture(L.TwoColumns());
        var original = WorkspaceStore.Serialize(f.Layout with { Revision = 0 });
        Assert.True(await f.Workspace.ExecuteAsync(new MoveTabs(["a"], "right", DockSide.Bottom)));
        var moved = WorkspaceStore.Serialize(f.Layout with { Revision = 0 });

        Assert.True(await f.Workspace.UndoAsync());
        Assert.Equal(original, WorkspaceStore.Serialize(f.Layout with { Revision = 0 }));
        Assert.True(await f.Workspace.RedoAsync());
        Assert.Equal(moved, WorkspaceStore.Serialize(f.Layout with { Revision = 0 }));
    }

    [Fact]
    public async Task Undo_AlwaysAdvancesRevision()
    {
        using var f = new WorkspaceFixture(L.TwoColumns());
        await f.Workspace.ExecuteAsync(new MoveTabs(["a"], "right"));

        await f.Workspace.UndoAsync();

        Assert.Equal(2, f.Layout.Revision);
    }

    [Fact]
    public async Task UndoAndRedo_ExposeDescriptionsAndAvailability()
    {
        using var f = new WorkspaceFixture(L.TwoColumns());
        await f.Workspace.ExecuteAsync(new MoveTabs(["a"], "right"));

        Assert.True(f.Workspace.CanUndo);
        Assert.Equal("Move tabs", f.Workspace.UndoDescription);
        Assert.Null(f.Workspace.RedoDescription);
        await f.Workspace.UndoAsync();
        Assert.False(f.Workspace.CanUndo);
        Assert.True(f.Workspace.CanRedo);
        Assert.Equal("Move tabs", f.Workspace.RedoDescription);
        Assert.Equal("Undo Move tabs", f.Changes.Last().Description);
    }

    [Fact]
    public async Task UndoAndRedo_WithEmptyHistory_ReturnFalse()
    {
        using var f = new WorkspaceFixture(L.TwoColumns());

        Assert.False(await f.Workspace.UndoAsync());
        Assert.False(await f.Workspace.RedoAsync());
        Assert.Empty(f.Changes);
    }

    [Fact]
    public async Task NewOperation_ClearsRedo()
    {
        using var f = new WorkspaceFixture(L.TwoColumns());
        await f.Workspace.ExecuteAsync(new MoveTabs(["a"], "right"));
        await f.Workspace.UndoAsync();

        await f.Workspace.ExecuteAsync(new MoveTabs(["b"], "right"));

        Assert.False(f.Workspace.CanRedo);
    }

    [Fact]
    public async Task Undo_WhileLayoutLocked_IsRejected()
    {
        using var f = new WorkspaceFixture(L.TwoColumns());
        await f.Workspace.ExecuteAsync(new MoveTabs(["a"], "right"));
        f.Workspace.IsLayoutLocked = true;

        Assert.False(await f.Workspace.UndoAsync());

        Assert.True(f.Workspace.CanUndo);
    }

    [Fact]
    public async Task HistoryLimit_EvictsOldestEntries()
    {
        using var f = new WorkspaceFixture(L.TwoColumns());
        f.Workspace.HistoryLimit = 2;
        await f.Workspace.ExecuteAsync(new MoveTabs(["a"], "right"));
        await f.Workspace.ExecuteAsync(new MoveTabs(["b"], "right"));
        await f.Workspace.ExecuteAsync(new MoveTabs(["c"], "right", DockSide.Top));

        int undone = 0;
        while (await f.Workspace.UndoAsync()) undone++;

        Assert.Equal(2, undone);
        Assert.Contains(f.Layout.Group("right").Slots, s => s.Id == "a");
    }

    [Fact]
    public async Task HistoryLimit_BelowOne_StillKeepsOneEntry()
    {
        using var f = new WorkspaceFixture(L.TwoColumns());
        f.Workspace.HistoryLimit = 0;
        await f.Workspace.ExecuteAsync(new MoveTabs(["a"], "right"));
        await f.Workspace.ExecuteAsync(new MoveTabs(["b"], "right"));

        Assert.True(await f.Workspace.UndoAsync());
        Assert.False(f.Workspace.CanUndo);
    }

    [Fact]
    public async Task Undo_DoesNotRevertPanelContent()
    {
        using var f = new WorkspaceFixture(L.TwoColumns());
        var session = f.SessionOf("a");
        await f.Workspace.ExecuteAsync(new AddPanel(new("new", "text"), "left", "a"));
        session.Text = "later edits";

        Assert.True(await f.Workspace.UndoAsync());

        Assert.Same(session, f.SessionOf("a"));
        Assert.Equal("later edits", session.Text);
    }

    [Fact]
    public async Task ClosedPanel_IsRetainedWhileHistoryRefersToIt()
    {
        using var f = new WorkspaceFixture(L.TwoColumns());
        var session = f.SessionOf("a");

        await f.Workspace.ExecuteAsync(new CloseTabs(["a"]));

        Assert.False(session.Disposed);
        Assert.True(f.Workspace.IsRetained("p-a"));
        Assert.True(await f.Workspace.UndoAsync());
        Assert.Same(session, f.SessionOf("a"));
    }

    [Fact]
    public async Task HistoryEviction_DisposesClosedPanelExactlyOnce()
    {
        using var f = new WorkspaceFixture(L.TwoColumns());
        f.Workspace.HistoryLimit = 1;
        var session = f.SessionOf("a");
        await f.Workspace.ExecuteAsync(new CloseTabs(["a"]));

        await f.Workspace.ExecuteAsync(new MoveTabs(["b"], "right"));

        Assert.Equal(1, session.DisposeCount);
        Assert.False(f.Workspace.IsRetained("p-a"));
    }

    [Fact]
    public async Task ClearingRedo_DisposesPanelOnlyReferencedByRedo()
    {
        using var f = new WorkspaceFixture(L.TwoColumns());
        await f.Workspace.AddAsync("text", "left");
        var added = (TestSession)f.Workspace.Session(f.Layout.Group("left").Slots.Last().InstanceId);
        await f.Workspace.UndoAsync();
        Assert.False(added.Disposed);

        await f.Workspace.ExecuteAsync(new MoveTabs(["a"], "right"));

        Assert.Equal(1, added.DisposeCount);
    }

    [Fact]
    public async Task ClearHistory_DisposesSessionsOnlyHistoryRetained()
    {
        using var f = new WorkspaceFixture(L.TwoColumns());
        var closed = f.SessionOf("a"); var live = f.SessionOf("b");
        await f.Workspace.ExecuteAsync(new CloseTabs(["a"]));

        f.Workspace.ClearHistory();

        Assert.Equal(1, closed.DisposeCount);
        Assert.False(live.Disposed);
        Assert.False(f.Workspace.CanUndo);
    }

    [Fact]
    public async Task ClearHistory_WhileUndoAwaitsGuard_AbandonsTheUndo()
    {
        using var f = new WorkspaceFixture(L.TwoColumns());
        await f.Workspace.AddAsync("text", "left");
        var gate = new TaskCompletionSource<bool>();
        f.Workspace.CanReleaseAsync = async (_, _, _) => await gate.Task;
        var revision = f.Layout.Revision;

        var undo = f.Workspace.UndoAsync();
        f.Workspace.ClearHistory();
        gate.SetResult(true);

        Assert.False(await undo);
        Assert.Equal(revision, f.Layout.Revision);
        Assert.Equal(4, f.Layout.Slots.Count());
        Assert.Contains("Layout history changed while waiting.", f.Diagnostics);
    }
}

public class DockWorkspacePanelTests
{
    [Fact]
    public async Task AddAsync_CreatesSessionForNewPanel()
    {
        using var f = new WorkspaceFixture();

        Assert.True(await f.Workspace.AddAsync("text", f.Primary));

        var slot = Assert.Single(f.Layout.Slots);
        Assert.IsType<TestSession>(f.Workspace.Session(slot.InstanceId));
    }

    [Fact]
    public async Task AddAsync_SecondSingleton_IsRejected()
    {
        using var f = new WorkspaceFixture();
        Assert.True(await f.Workspace.AddAsync("single", f.Primary));

        Assert.False(await f.Workspace.AddAsync("single", f.Primary));

        Assert.Contains(f.Diagnostics, d => d.Contains("already open"));
    }

    [Fact]
    public async Task AddAsync_ReopeningHistoryRetainedSingleton_ReusesItsSession()
    {
        using var f = new WorkspaceFixture();
        await f.Workspace.AddAsync("single", f.Primary);
        var instance = f.Layout.Slots.Single().InstanceId; var session = f.Workspace.Session(instance);
        await f.Workspace.ExecuteAsync(new CloseTabs([f.Layout.Slots.Single().Id]));

        Assert.True(await f.Workspace.AddAsync("single", f.Primary));

        Assert.Equal(instance, f.Layout.Slots.Single().InstanceId);
        Assert.Same(session, f.Workspace.Session(instance));
    }

    [Fact]
    public void Evaluate_ReplacingSingletonWithSameType_OnlySelectsIt()
    {
        using var f = new WorkspaceFixture(L.TwoColumns(new Dictionary<string, string> { ["c"] = "single" }));

        var result = f.Workspace.Evaluate(new AddPanel(new("new", "single"), "right", "c"));

        Assert.False(result.Plan!.RecordHistory);
        Assert.Equal("p-c", result.Plan.Candidate.Slot("c").InstanceId);
    }

    [Fact]
    public async Task DuplicateAsync_CopiesLiveStateIntoNewInstanceInSameGroup()
    {
        using var f = new WorkspaceFixture(L.TwoColumns());
        f.SessionOf("c").Text = "copy me";

        Assert.True(await f.Workspace.DuplicateAsync("c"));

        var right = f.Layout.Group("right");
        Assert.Equal(2, right.Slots.Length);
        var copy = (TestSession)f.Workspace.Session(right.Slots[1].InstanceId);
        Assert.NotSame(f.SessionOf("c"), copy);
        Assert.Equal("copy me", copy.Text);
    }

    [Fact]
    public async Task DuplicateAsync_Singleton_IsRejected()
    {
        using var f = new WorkspaceFixture(L.TwoColumns(new Dictionary<string, string> { ["c"] = "single" }));

        Assert.False(await f.Workspace.DuplicateAsync("c"));

        Assert.Contains("This panel does not support duplication.", f.Diagnostics);
    }

    [Fact]
    public async Task DuplicateAsync_UnregisteredType_IsRejected()
    {
        using var f = new WorkspaceFixture(L.TwoColumns(new Dictionary<string, string> { ["c"] = "missing" }));

        Assert.False(await f.Workspace.DuplicateAsync("c"));
    }

    [Fact]
    public void Capture_ReplacesPanelStateWithLiveSessionState()
    {
        using var f = new WorkspaceFixture(L.TwoColumns());
        f.SessionOf("a").Text = "live";

        var captured = f.Workspace.Capture();

        Assert.Equal("\"live\"", captured.Panels["p-a"].State);
        Assert.Equal("{}", f.Layout.Panels["p-a"].State);
    }

    [Fact]
    public void MissingProvider_KeepsOriginalStateAsPlaceholderSession()
    {
        var layout = L.TwoColumns(new Dictionary<string, string> { ["c"] = "uninstalled" });
        layout = layout with { Panels = layout.Panels.SetItem("p-c", layout.Panels["p-c"] with { State = "{\"keep\":1}" }) };
        using var f = new WorkspaceFixture(layout);

        var session = Assert.IsType<MissingPanelSession>(f.Workspace.Session("p-c"));

        Assert.Equal("uninstalled", session.TypeId);
        Assert.Equal("{\"keep\":1}", f.Workspace.Capture().Panels["p-c"].State);
    }
}

public class DockWorkspaceArrangementTests
{
    [Fact]
    public async Task ApplyArrangement_MatchesPanelsByIdentityThenType()
    {
        using var f = new WorkspaceFixture(L.TwoColumns());
        var sessions = f.Layout.Slots.ToDictionary(s => s.Id, s => f.Workspace.Session(s.InstanceId));
        var template = L.Build(L.V("v", L.Region("top", "primary", "c"), L.Group("bottom", "x")));

        Assert.True(await f.Workspace.ApplyArrangementAsync(template, "Stacked"));

        Assert.Equal(3, f.Layout.Panels.Count);
        Assert.Same(sessions["c"], f.Workspace.Session("p-c"));
        Assert.Equal("Switch to Stacked", f.Changes.Last().Description);
        Assert.All(f.Layout.Panels.Keys, id => Assert.Contains(id, new[] { "p-a", "p-b", "p-c" }));
    }

    [Fact]
    public async Task ApplyArrangement_UnmatchedLivePanelsJoinThePrimaryGroup()
    {
        using var f = new WorkspaceFixture(L.TwoColumns());
        var template = L.Build(L.H("h", L.Region("main-group", "primary", "c"), L.Group("side", "z")),
            types: new Dictionary<string, string> { ["z"] = "single" });

        Assert.True(await f.Workspace.ApplyArrangementAsync(template, "Template"));

        var primary = f.Layout.Group("main-group");
        Assert.Equal(3, primary.Slots.Length);
        Assert.Contains(primary.Slots, s => s.InstanceId == "p-a");
        Assert.Contains(primary.Slots, s => s.InstanceId == "p-b");
        Assert.Equal(4, f.Layout.Panels.Count);
    }

    [Fact]
    public async Task ApplyArrangement_TemplateOnlyTypeGetsFreshInstance()
    {
        using var f = new WorkspaceFixture(L.TwoColumns());
        var template = L.Build(L.Group("g", "s"), types: new Dictionary<string, string> { ["s"] = "single" });

        Assert.True(await f.Workspace.ApplyArrangementAsync(template, "T"));

        var single = Assert.Single(f.Layout.Panels.Values, p => p.TypeId == "single");
        Assert.NotEqual("p-s", single.Id);
    }

    [Fact]
    public async Task ApplyArrangement_ReusesDormantSingletonAndRemapsCollidingSlots()
    {
        using var f = new WorkspaceFixture();
        await f.Workspace.AddAsync("single", f.Primary);
        var singletonId = f.Layout.Slots.Single().InstanceId; var singleton = f.Workspace.Session(singletonId);
        var template = f.Layout;
        await f.Workspace.ExecuteAsync(new CloseTabs([f.Layout.Slots.Single().Id]));
        await f.Workspace.AddAsync("single", f.Primary);
        var slotId = f.Layout.Slots.Single().Id;
        await f.Workspace.AddAsync("text", f.Primary, slotId);
        var notes = f.Layout.Slot(slotId).InstanceId;

        Assert.True(await f.Workspace.ApplyArrangementAsync(template, "original"));

        Assert.Contains(notes, f.Layout.Panels.Keys);
        Assert.Contains(singletonId, f.Layout.Panels.Keys);
        Assert.Equal(2, f.Layout.Slots.Select(s => s.Id).Distinct().Count());
        Assert.Same(singleton, f.Workspace.Session(singletonId));
    }

    [Fact]
    public async Task ApplyArrangement_WithoutPrimaryRegion_AddsLeftoversToFirstMainGroup()
    {
        using var f = new WorkspaceFixture(L.TwoColumns());
        var template = L.Build(L.H("h", L.Group("one", "c"), L.Group("two", "y")));

        Assert.True(await f.Workspace.ApplyArrangementAsync(template, "No region"));

        // Which text panel the template's "y" claims depends on dictionary order; the other is the leftover.
        var one = f.Layout.Group("one");
        Assert.Equal(2, one.Slots.Length);
        Assert.Equal("c", one.Slots[0].Id);
        Assert.Contains(one.Slots[1].Id, new[] { "a", "b" });
        Assert.Equal(3, f.Layout.Panels.Count);
    }

    [Fact]
    public async Task ApplyArrangement_InvalidTemplate_ReturnsFalseAndReports()
    {
        using var f = new WorkspaceFixture(L.TwoColumns());

        Assert.False(await f.Workspace.ApplyArrangementAsync(L.TwoColumns() with { MainHostId = "x" }, "Broken"));

        Assert.Contains(f.Diagnostics, d => d.StartsWith("Could not apply Broken"));
        Assert.Equal(0, f.Layout.Revision);
    }
}

public class DockWorkspacePlacementTests
{
    [Fact]
    public void UpdatePlacement_StoresPlacementWithoutHistory()
    {
        using var f = new WorkspaceFixture(L.TwoColumns());
        f.Build(out var host); f.Changes.Clear();
        var revision = f.Layout.Revision;

        f.Workspace.UpdatePlacement(host, new WindowPlacement(10, 20, 500, 400, 1.5));

        Assert.Equal(new WindowPlacement(10, 20, 500, 400, 1.5), f.Layout.Hosts.Single(h => h.Id == host).Placement);
        Assert.Equal(revision + 1, f.Layout.Revision);
        Assert.Equal([new WorkspaceChange("Window placement", false)], f.Changes);
        Assert.Equal("Move to new window", f.Workspace.UndoDescription);
    }

    [Fact]
    public void UpdatePlacement_TinyWindow_StoresMinimumSize()
    {
        using var f = new WorkspaceFixture(L.TwoColumns());
        f.Build(out var host);

        f.Workspace.UpdatePlacement(host, new WindowPlacement(10, 10, 40, 30));

        var placement = f.Layout.Hosts.Single(h => h.Id == host).Placement!;
        Assert.Equal(100, placement.Width);
        Assert.Equal(100, placement.Height);
    }

    [Fact]
    public void UpdatePlacement_UnknownHost_IsIgnored()
    {
        using var f = new WorkspaceFixture(L.TwoColumns());

        f.Workspace.UpdatePlacement("gone", new WindowPlacement());

        Assert.Equal(0, f.Layout.Revision);
        Assert.Empty(f.Changes);
    }

    [Fact]
    public void UpdatePlacement_NonFiniteValues_AreIgnoredAndReported()
    {
        using var f = new WorkspaceFixture(L.TwoColumns());
        f.Build(out var host); var revision = f.Layout.Revision;

        f.Workspace.UpdatePlacement(host, new WindowPlacement(double.NaN, 0, 300, 300));

        Assert.Equal(revision, f.Layout.Revision);
        Assert.Contains(f.Diagnostics, d => d.StartsWith("Window placement ignored"));
    }
}

public class DockWorkspaceDisposalTests
{
    [Fact]
    public async Task Dispose_DisposesLiveAndHistoryRetainedSessionsOnce()
    {
        var f = new WorkspaceFixture(L.TwoColumns());
        await f.Workspace.ExecuteAsync(new CloseTabs(["a"]));

        f.Workspace.Dispose();
        f.Workspace.Dispose();

        Assert.Equal(3, f.Sessions.Count);
        Assert.All(f.Sessions, s => Assert.Equal(1, s.DisposeCount));
        Assert.False(f.Workspace.CanUndo);
    }

    [Fact]
    public async Task Dispose_RejectsFurtherOperations()
    {
        var f = new WorkspaceFixture(L.TwoColumns());
        var plan = f.Workspace.Evaluate(new SelectTab("b")).Plan!;
        f.Workspace.Dispose();

        Assert.Equal("The workspace is closed.", f.Workspace.Evaluate(new SelectTab("b")).Error);
        Assert.False(await f.Workspace.CommitAsync(plan));
        f.Workspace.UpdatePlacement(L.Main, new WindowPlacement());
        Assert.Equal(0, f.Layout.Revision);
    }

    [Fact]
    public void Dispose_SessionDisposalFailure_IsReportedAndOthersStillDisposed()
    {
        var f = new WorkspaceFixture(L.TwoColumns());
        f.SessionOf("a").ThrowOnDispose = new InvalidOperationException("dispose failed");

        f.Workspace.Dispose();

        Assert.Contains("Panel disposal: dispose failed", f.Diagnostics);
        Assert.All(f.Sessions, s => Assert.Equal(1, s.DisposeCount));
    }

    [Fact]
    public async Task Dispose_DuringPendingGuard_AbandonsTheTransaction()
    {
        var f = new WorkspaceFixture(L.TwoColumns());
        var gate = new TaskCompletionSource<bool>();
        f.Workspace.CanReleaseAsync = async (_, _, _) => await gate.Task;

        var closing = f.Workspace.ExecuteAsync(new CloseTabs(["a"]));
        f.Workspace.Dispose();
        gate.SetResult(true);

        Assert.False(await closing);
        Assert.Equal(3, f.Layout.Slots.Count());
    }
}
