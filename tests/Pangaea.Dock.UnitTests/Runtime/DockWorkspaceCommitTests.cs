using Pangaea.Dock.Core;
using Pangaea.Dock.Runtime;
using Pangaea.Dock.Tests.Support;

namespace Pangaea.Dock.Tests.Runtime;

public class DockWorkspaceConstructionTests
{
    [Fact]
    public void Constructor_WithoutLayout_StartsFromEmptyWorkspace()
    {
        using var f = new WorkspaceFixture();

        Assert.Single(f.Layout.Hosts);
        Assert.Empty(f.Layout.Panels);
        Assert.False(f.Workspace.CanUndo);
        Assert.False(f.Workspace.CanRedo);
    }

    [Fact]
    public void Constructor_CreatesAndRestoresASessionPerPanel()
    {
        var initial = L.TwoColumns();
        initial = initial with { Panels = initial.Panels.SetItem("p-a", initial.Panels["p-a"] with { State = "\"hello\"" }) };

        using var f = new WorkspaceFixture(initial);

        Assert.Equal(3, f.Sessions.Count);
        Assert.Equal("hello", f.SessionOf("a").Text);
        Assert.True(f.Workspace.IsRetained("p-c"));
    }

    [Fact]
    public void Constructor_InvalidLayout_Throws()
    {
        Assert.Throws<InvalidDataException>(() => new DockWorkspace(Registries.Create(), L.TwoColumns() with { MainHostId = "x" }));
    }

    [Fact]
    public void Constructor_TwoSingletonInstances_Throws()
    {
        var layout = L.TwoColumns(new Dictionary<string, string> { ["a"] = "single", ["c"] = "single" });

        Assert.Throws<InvalidDataException>(() => new DockWorkspace(Registries.Create(), layout));
    }

    [Fact]
    public void Constructor_SessionRestoreFailure_DisposesSessionsCreatedSoFarAndThrows()
    {
        var sessions = new List<TestSession>();
        var layout = L.TwoColumns();
        layout = layout with { Panels = layout.Panels.SetItem("p-c", layout.Panels["p-c"] with { State = "\"explode\"" }) };

        Assert.Throws<InvalidDataException>(() => new DockWorkspace(Registries.Create(sessions), layout));

        Assert.NotEmpty(sessions);
        Assert.All(sessions, s => Assert.Equal(1, s.DisposeCount));
    }

    [Fact]
    public void Session_UnknownInstance_Throws()
    {
        using var f = new WorkspaceFixture();

        Assert.Throws<KeyNotFoundException>(() => f.Workspace.Session("nope"));
        Assert.False(f.Workspace.IsRetained("nope"));
    }
}

public class DockWorkspaceCommitTests
{
    [Fact]
    public void Evaluate_HasNoSideEffects()
    {
        using var f = new WorkspaceFixture(L.TwoColumns());
        var before = WorkspaceStore.Serialize(f.Layout);

        var result = f.Workspace.Evaluate(new MoveTabs(["a"], "right", DockSide.Right));

        Assert.True(result.Success);
        Assert.Equal(before, WorkspaceStore.Serialize(f.Layout));
        Assert.Empty(f.Changes);
    }

    [Fact]
    public async Task Commit_PublishesCandidateIncrementsRevisionAndRaisesChanged()
    {
        using var f = new WorkspaceFixture(L.TwoColumns());
        var plan = f.Workspace.Evaluate(new MoveTabs(["a"], "right")).Plan!;

        Assert.True(await f.Workspace.CommitAsync(plan));

        Assert.Equal(["c", "a"], f.Layout.Group("right").SlotIds());
        Assert.Equal(1, f.Layout.Revision);
        Assert.Equal([new WorkspaceChange("Move tabs", true)], f.Changes);
    }

    [Fact]
    public async Task Commit_PreservesSessionIdentityAcrossMoves()
    {
        using var f = new WorkspaceFixture(L.TwoColumns());
        var session = f.SessionOf("a");

        Assert.True(await f.Workspace.ExecuteAsync(new MoveTabs(["a"], "right", DockSide.Bottom)));

        Assert.Same(session, f.SessionOf("a"));
        Assert.Equal(0, session.DisposeCount);
    }

    [Fact]
    public async Task Commit_StalePlan_IsRejected()
    {
        using var f = new WorkspaceFixture(L.TwoColumns());
        var plan = f.Workspace.Evaluate(new MoveTabs(["a"], "right")).Plan!;
        Assert.True(await f.Workspace.CommitAsync(plan));

        Assert.False(await f.Workspace.CommitAsync(plan));

        Assert.Contains(f.Diagnostics, d => d.Contains("layout changed"));
    }

    [Fact]
    public async Task Commit_PlanFromAnotherWorkspace_IsRejected()
    {
        using var f = new WorkspaceFixture(L.TwoColumns());
        var plan = f.Workspace.Evaluate(new SelectTab("b")).Plan! with { WorkspaceId = "other" };

        Assert.False(await f.Workspace.CommitAsync(plan));
    }

    [Fact]
    public async Task Commit_InvalidCandidate_IsRejectedAndReported()
    {
        using var f = new WorkspaceFixture(L.TwoColumns());
        var plan = f.Workspace.Evaluate(new SelectTab("b")).Plan!;
        plan = plan with { Candidate = plan.Candidate with { MainHostId = "gone" } };

        Assert.False(await f.Workspace.CommitAsync(plan));

        Assert.Equal(0, f.Layout.Revision);
        Assert.Contains(f.Diagnostics, d => d.StartsWith("Could not change the layout"));
    }

    [Fact]
    public async Task Execute_RejectedOperation_ReportsErrorAndReturnsFalse()
    {
        using var f = new WorkspaceFixture(L.TwoColumns());

        Assert.False(await f.Workspace.ExecuteAsync(new SwapPanels("a", "a")));

        Assert.Contains("Choose another tab to swap with.", f.Diagnostics);
    }

    [Fact]
    public async Task Execute_SelectTab_DoesNotRecordHistoryAndRaisesNonStructuralChange()
    {
        using var f = new WorkspaceFixture(L.TwoColumns());

        Assert.True(await f.Workspace.ExecuteAsync(new SelectTab("b")));

        Assert.False(f.Workspace.CanUndo);
        Assert.Equal([new WorkspaceChange("Select tab", false)], f.Changes);
    }

    [Fact]
    public async Task Execute_PreCanceledToken_LeavesLayoutUnchanged()
    {
        using var f = new WorkspaceFixture(L.TwoColumns());

        Assert.False(await f.Workspace.ExecuteAsync(new CloseTabs(["a"]), new CancellationToken(true)));

        Assert.Equal(3, f.Layout.Slots.Count());
        Assert.Contains("Layout operation canceled.", f.Diagnostics);
    }

    [Fact]
    public async Task Execute_TokenCanceledDuringReleaseGuard_LeavesLayoutUnchanged()
    {
        using var f = new WorkspaceFixture(L.TwoColumns());
        using var cts = new CancellationTokenSource();
        f.Workspace.CanReleaseAsync = (_, _, _) => { cts.Cancel(); return Task.FromResult(true); };

        Assert.False(await f.Workspace.ExecuteAsync(new CloseTabs(["a"]), cts.Token));

        Assert.Equal(3, f.Layout.Slots.Count());
    }

    [Fact]
    public void OperationPolicy_RejectionReasonIsReturnedBeforeEvaluation()
    {
        using var f = new WorkspaceFixture(L.TwoColumns());
        f.Workspace.OperationPolicy = op => op is FloatTabs ? "No floating here." : null;

        var result = f.Workspace.Evaluate(new FloatTabs(["a"]));

        Assert.Equal("No floating here.", result.Error);
        Assert.True(f.Workspace.Evaluate(new SelectTab("b")).Success);
    }

    [Fact]
    public async Task LayoutLock_RejectsStructuralOperationsButAllowsSelection()
    {
        using var f = new WorkspaceFixture(L.TwoColumns());
        f.Workspace.IsLayoutLocked = true;

        Assert.False(await f.Workspace.ExecuteAsync(new FloatTabs(["a"])));
        Assert.True(await f.Workspace.ExecuteAsync(new SelectTab("b")));

        Assert.Contains("Unlock the layout to rearrange panels.", f.Diagnostics);
    }

    [Fact]
    public async Task LayoutLock_RejectsCommitOfPlanEvaluatedBeforeLocking()
    {
        using var f = new WorkspaceFixture(L.TwoColumns());
        var plan = f.Workspace.Evaluate(new FloatTabs(["a"])).Plan!;
        f.Workspace.IsLayoutLocked = true;

        Assert.False(await f.Workspace.CommitAsync(plan));

        Assert.Contains("Unlock the layout first.", f.Diagnostics);
    }

    [Fact]
    public async Task ReleaseGuard_IsCalledOncePerPanelLosingItsLastSlot()
    {
        using var f = new WorkspaceFixture(L.TwoColumns());
        var released = new List<string>();
        f.Workspace.CanReleaseAsync = (state, session, _) => { released.Add(state.Id); Assert.IsType<TestSession>(session); return Task.FromResult(true); };

        Assert.True(await f.Workspace.ExecuteAsync(new CloseTabs(["a", "b"])));

        Assert.Equal(["p-a", "p-b"], released.Order());
    }

    [Fact]
    public async Task ReleaseGuard_Cancellation_KeepsEveryTab()
    {
        using var f = new WorkspaceFixture(L.TwoColumns());
        f.Workspace.CanReleaseAsync = (_, _, _) => Task.FromResult(false);

        Assert.False(await f.Workspace.ExecuteAsync(new CloseTabs(["a", "b"])));

        Assert.Equal(3, f.Layout.Slots.Count());
        Assert.Empty(f.Changes);
    }

    [Fact]
    public async Task ReleaseGuard_Throwing_RejectsAndReports()
    {
        using var f = new WorkspaceFixture(L.TwoColumns());
        f.Workspace.CanReleaseAsync = (_, _, _) => throw new InvalidOperationException("guard exploded");

        Assert.False(await f.Workspace.ExecuteAsync(new CloseTabs(["a"])));

        Assert.Contains(f.Diagnostics, d => d.Contains("guard exploded"));
        Assert.Equal(3, f.Layout.Slots.Count());
    }

    [Fact]
    public async Task PendingGuard_RejectsOverlappingTransaction()
    {
        using var f = new WorkspaceFixture(L.TwoColumns());
        var gate = new TaskCompletionSource<bool>();
        f.Workspace.CanReleaseAsync = async (_, _, _) => await gate.Task;

        var closing = f.Workspace.ExecuteAsync(new CloseTabs(["a", "b"]));
        Assert.False(await f.Workspace.ExecuteAsync(new SelectTab("c")));
        gate.SetResult(false);

        Assert.False(await closing);
        Assert.Contains(f.Diagnostics, d => d.Contains("already pending"));
        Assert.Equal(3, f.Layout.Slots.Count());
    }

    [Fact]
    public async Task UpdatePlacement_WhileTransactionPending_IsIgnored()
    {
        using var f = new WorkspaceFixture(L.TwoColumns());
        f.Build(out var floating);
        var gate = new TaskCompletionSource<bool>();
        f.Workspace.CanReleaseAsync = async (_, _, _) => await gate.Task;

        var closing = f.Workspace.ExecuteAsync(new CloseTabs(["a"]));
        f.Workspace.UpdatePlacement(floating, new WindowPlacement(1, 1, 300, 300)); // ignored while busy
        gate.SetResult(true);

        Assert.True(await closing);
        Assert.Equal(new WindowPlacement(), f.Layout.Hosts.Single(h => h.Id == floating).Placement);
    }

    [Fact]
    public async Task FactoryFailure_PreservesLayoutAndExistingSession()
    {
        using var f = new WorkspaceFixture(L.TwoColumns());
        var session = f.SessionOf("a");

        Assert.False(await f.Workspace.AddAsync("broken", "left", "a"));

        Assert.Equal(0, f.Layout.Revision);
        Assert.Same(session, f.SessionOf("a"));
        Assert.Contains(f.Diagnostics, d => d.Contains("Factory failed on purpose"));
    }

    [Fact]
    public async Task PreparePresentationFailure_RollsBackAndDisposesNewSession()
    {
        using var f = new WorkspaceFixture(L.TwoColumns());
        var before = WorkspaceStore.Serialize(f.Layout);
        f.Workspace.PreparePresentation = _ => throw new InvalidOperationException("View construction failed");

        Assert.False(await f.Workspace.AddAsync("text", "left"));

        Assert.Equal(before, WorkspaceStore.Serialize(f.Layout));
        var created = Assert.Single(f.Sessions.Skip(3));
        Assert.Equal(1, created.DisposeCount);
    }

    [Fact]
    public async Task PreparePresentation_ReceivesTheCandidateBeforePublication()
    {
        using var f = new WorkspaceFixture(L.TwoColumns());
        DockLayout? seen = null; long revisionDuringPrepare = -1;
        f.Workspace.PreparePresentation = l => { seen = l; revisionDuringPrepare = f.Layout.Revision; };

        Assert.True(await f.Workspace.ExecuteAsync(new MoveTabs(["a"], "right")));

        Assert.NotNull(seen);
        Assert.Contains(seen!.Group("right").Slots, s => s.Id == "a");
        Assert.Equal(0, revisionDuringPrepare);
    }

    [Fact]
    public async Task ApplyLayout_ReusingIdentityForAnotherType_IsRejected()
    {
        using var f = new WorkspaceFixture(L.TwoColumns());
        var clash = f.Layout with { Panels = f.Layout.Panels.SetItem("p-c", f.Layout.Panels["p-c"] with { TypeId = "single" }) };

        Assert.False(await f.Workspace.ExecuteAsync(new ApplyLayout(clash)));

        Assert.Contains(f.Diagnostics, d => d.Contains("already belongs to another panel type"));
    }

    [Fact]
    public async Task ChangedHandlerFailure_IsReportedAndDoesNotFailTheCommit()
    {
        using var f = new WorkspaceFixture(L.TwoColumns());
        f.Workspace.Changed += _ => throw new InvalidOperationException("presenter broke");

        Assert.True(await f.Workspace.ExecuteAsync(new MoveTabs(["a"], "right")));

        Assert.Contains("Presenter notification: presenter broke", f.Diagnostics);
    }

    [Fact]
    public void DiagnosticHandlerFailure_DoesNotStopOtherHandlers()
    {
        using var f = new WorkspaceFixture();
        var later = new List<string>();
        f.Workspace.Diagnostic += _ => throw new InvalidOperationException();
        f.Workspace.Diagnostic += later.Add;

        f.Workspace.Report("hello");

        Assert.Equal(["hello"], later);
    }
}

internal static class WorkspaceFixtureExtensions
{
    /// <summary>Floats "c" synchronously through evaluate/commit so a floating host exists.</summary>
    public static void Build(this WorkspaceFixture f, out string floatingHostId)
    {
        Assert.True(f.Workspace.ExecuteAsync(new FloatTabs(["c"])).GetAwaiter().GetResult());
        floatingHostId = f.Layout.Hosts.Single(h => h.Id != f.Layout.MainHostId).Id;
    }
}
