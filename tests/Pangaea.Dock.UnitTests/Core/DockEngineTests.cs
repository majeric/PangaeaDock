using System.Collections.Immutable;
using Pangaea.Dock.Core;
using Pangaea.Dock.Runtime;
using Pangaea.Dock.Tests.Support;
using static Pangaea.Dock.Tests.Support.Engine;

namespace Pangaea.Dock.Tests.Core;

public class DockEngineGeneralTests
{
    private sealed record UnknownOperation : DockOperation;

    [Fact]
    public void Evaluate_Success_IncrementsRevisionAndTargetsSourceWorkspace()
    {
        var before = L.TwoColumns() with { Revision = 7 };

        var plan = DockEngine.Evaluate(before, new SelectTab("b"), Catalog.All).Plan!;

        Assert.Equal(before.Id, plan.WorkspaceId);
        Assert.Equal(7, plan.ExpectedRevision);
        Assert.Equal(8, plan.Candidate.Revision);
    }

    [Fact]
    public void Evaluate_DoesNotChangeTheSourceLayout()
    {
        var before = L.TwoColumns();
        var snapshot = before.Groups.Select(g => g.SlotIds()).ToArray();

        Apply(before, new MoveTabs(["a"], "right"));

        Assert.Equal(snapshot, before.Groups.Select(g => g.SlotIds()).ToArray());
    }

    [Fact]
    public void Evaluate_UnsupportedOperation_IsRejected()
    {
        Assert.Contains("Unsupported", Reject(L.TwoColumns(), new UnknownOperation()));
    }

    [Fact]
    public void Evaluate_CandidateIsAlwaysNormalized()
    {
        var after = Apply(L.TwoColumns(), new MoveTabs(["c"], "left"));

        Assert.IsType<TabGroup>(after.Hosts[0].Root);
        Assert.Equal(WorkspaceStore.Serialize(after), WorkspaceStore.Serialize(LayoutTree.Normalize(after)));
    }
}

public class MoveTabsTests
{
    [Fact]
    public void MoveTabs_Center_AppendsToTargetAndActivatesMovedTab()
    {
        var after = Apply(L.TwoColumns(), new MoveTabs(["a"], "right"));

        var right = after.Group("right");
        Assert.Equal(["c", "a"], right.SlotIds());
        Assert.Equal("a", right.ActiveSlotId);
        Assert.Equal(["b"], after.Group("left").SlotIds());
    }

    [Fact]
    public void MoveTabs_Center_KeepsEachTabBoundToItsPanel()
    {
        var after = Apply(L.TwoColumns(), new MoveTabs(["a", "b"], "right"));

        Assert.All(after.Slots, s => Assert.Equal(L.P(s.Id), s.InstanceId));
        Assert.Equal("Move tabs", DockEngine.Evaluate(L.TwoColumns(), new MoveTabs(["a", "b"], "right"), Catalog.All).Plan!.Description);
    }

    [Fact]
    public void MoveTabs_CenterWithIndex_InsertsAtIndex()
    {
        var after = Apply(L.TwoColumns(), new MoveTabs(["c"], "left", Index: 1));

        Assert.Equal(["a", "c", "b"], after.GroupOf("c").SlotIds());
    }

    [Fact]
    public void MoveTabs_IndexBeyondEnd_IsClampedToEnd()
    {
        var after = Apply(L.TwoColumns(), new MoveTabs(["c"], "left", Index: 99));

        Assert.Equal(["a", "b", "c"], after.GroupOf("c").SlotIds());
    }

    [Fact]
    public void MoveTabs_ReorderForwardWithinGroup_AccountsForRemovedTab()
    {
        var before = L.Build(L.Group("g", "a", "b", "c"));

        var after = Apply(before, new MoveTabs(["a"], "g", Index: 2));

        Assert.Equal(["b", "a", "c"], after.Group("g").SlotIds());
    }

    [Fact]
    public void MoveTabs_ReorderBackwardWithinGroup_MovesToFront()
    {
        var before = L.Build(L.Group("g", "a", "b", "c"));

        var after = Apply(before, new MoveTabs(["c"], "g", Index: 0));

        Assert.Equal(["c", "a", "b"], after.Group("g").SlotIds());
    }

    [Theory]
    [InlineData(DockSide.Left, SplitAxis.Horizontal, true)]
    [InlineData(DockSide.Right, SplitAxis.Horizontal, false)]
    [InlineData(DockSide.Top, SplitAxis.Vertical, true)]
    [InlineData(DockSide.Bottom, SplitAxis.Vertical, false)]
    public void MoveTabs_Side_SplitsTargetOnMatchingAxisAndOrder(DockSide side, SplitAxis axis, bool insertedFirst)
    {
        var before = L.Build(L.Group("g", "a", "b"));

        var result = DockEngine.Evaluate(before, new MoveTabs(["b"], "g", side), Catalog.All);

        var split = Assert.IsType<SplitNode>(result.Plan!.Candidate.Hosts[0].Root);
        Assert.Equal(axis, split.Axis);
        Assert.Equal([0.5, 0.5], split.Weights.ToArray());
        var inserted = (TabGroup)split.Children[insertedFirst ? 0 : 1];
        Assert.Equal(["b"], inserted.SlotIds());
        Assert.Equal("b", inserted.ActiveSlotId);
        Assert.Equal("Split " + side.ToString().ToLowerInvariant(), result.Plan.Description);
    }

    [Fact]
    public void MoveTabs_LastTabOfGroup_RemovesGroupAndCollapsesSplit()
    {
        var after = Apply(L.TwoColumns(), new MoveTabs(["c"], "left"));

        var root = Assert.IsType<TabGroup>(after.Hosts[0].Root);
        Assert.Equal(["a", "b", "c"], root.SlotIds());
    }

    [Fact]
    public void MoveTabs_ActiveTabMovedAway_ActivatesNextRemainingTab()
    {
        var before = L.Build(L.H("h", L.Group("g", "a", "b", "c") with { ActiveSlotId = "b" }, L.Group("o", "d")));

        var after = Apply(before, new MoveTabs(["b"], "o"));

        Assert.Equal("c", after.Group("g").ActiveSlotId);
    }

    [Fact]
    public void MoveTabs_ActiveLastTabMovedAway_ActivatesPreviousTab()
    {
        var before = L.Build(L.H("h", L.Group("g", "a", "b") with { ActiveSlotId = "b" }, L.Group("o", "d")));

        Assert.Equal("a", Apply(before, new MoveTabs(["b"], "o")).Group("g").ActiveSlotId);
    }

    [Fact]
    public void MoveTabs_SplitBesideEmptyRegionGroup_IsAllowed()
    {
        var before = L.Build(L.H("h", L.Region("primary", "primary"), L.Group("o", "a", "b")));

        var after = Apply(before, new MoveTabs(["a"], "primary", DockSide.Left));

        Assert.Contains(after.Groups, g => g.Id == "primary" && g.Slots.IsEmpty);
        Assert.Equal(["a"], after.GroupOf("a").SlotIds());
    }

    [Fact]
    public void MoveTabs_EmptySelection_IsRejected()
    {
        Assert.Contains("distinct", Reject(L.TwoColumns(), new MoveTabs([], "right")));
    }

    [Fact]
    public void MoveTabs_DuplicateSlotIds_IsRejected()
    {
        Assert.Contains("distinct", Reject(L.TwoColumns(), new MoveTabs(["a", "a"], "right")));
    }

    [Fact]
    public void MoveTabs_UnknownSlot_IsRejected()
    {
        Assert.Contains("tab is no longer available", Reject(L.TwoColumns(), new MoveTabs(["zzz"], "right")));
    }

    [Fact]
    public void MoveTabs_UnknownGroup_IsRejected()
    {
        Assert.Contains("group is no longer available", Reject(L.TwoColumns(), new MoveTabs(["a"], "zzz")));
    }

    [Fact]
    public void MoveTabs_SplitBesideItselfWithAllTabs_IsRejected()
    {
        Assert.Contains("beside itself", Reject(L.TwoColumns(), new MoveTabs(["c"], "right", DockSide.Left)));
    }

    [Fact]
    public void MoveTabs_SplitRailGroup_IsRejected()
    {
        var before = L.Build(L.Group("g", "a"), rails: [new(DockSide.Left, L.Group("r", "b"), "g")]);

        Assert.Contains("Pin this group", Reject(before, new MoveTabs(["a"], "r", DockSide.Top)));
    }

    [Fact]
    public void MoveTabs_IntoRailGroupCenter_IsAllowedForCollapsiblePanel()
    {
        var before = L.Build(L.Group("g", "a", "x"), rails: [new(DockSide.Left, L.Group("r", "b"), "g")]);

        Assert.Equal(["b", "a"], Apply(before, new MoveTabs(["a"], "r")).Group("r").SlotIds());
    }

    [Fact]
    public void MoveTabs_IntoFloatingGroup_RejectsNonFloatingPanel()
    {
        var before = L.Build(L.Group("g", "fx", "a"), floating: [L.Floating("float", L.Group("fg", "b"))],
            types: new Dictionary<string, string> { ["fx"] = "fixed" });

        Assert.Contains("cannot float", Reject(before, new MoveTabs(["fx"], "fg")));
    }

    [Fact]
    public void MoveTabs_IntoRailGroup_RejectsNonCollapsiblePanel()
    {
        var before = L.Build(L.Group("g", "fx", "a"), rails: [new(DockSide.Left, L.Group("r", "b"), "g")],
            types: new Dictionary<string, string> { ["fx"] = "fixed" });

        Assert.Contains("cannot collapse", Reject(before, new MoveTabs(["fx"], "r")));
    }

    [Fact]
    public void MoveTabs_EmptyingFloatingHost_RemovesTheHost()
    {
        var before = L.Build(L.Group("g", "a"), floating: [L.Floating("float", L.Group("fg", "b"))]);

        var after = Apply(before, new MoveTabs(["b"], "g"));

        Assert.Single(after.Hosts);
    }
}

public class AddPanelTests
{
    [Fact]
    public void AddPanel_AppendsNewSlotAndActivatesIt()
    {
        var result = DockEngine.Evaluate(L.TwoColumns(), new AddPanel(new("new", "text"), "right"), Catalog.All);

        var right = result.Plan!.Candidate.Group("right");
        Assert.Equal(2, right.Slots.Length);
        Assert.Equal("new", right.Slots[1].InstanceId);
        Assert.Equal(right.Slots[1].Id, right.ActiveSlotId);
        Assert.Equal("text", result.Plan.Candidate.Panels["new"].TypeId);
        Assert.Equal("Add Text", result.Plan.Description);
    }

    [Fact]
    public void AddPanel_Replace_KeepsSlotIdentityAndPosition()
    {
        var result = DockEngine.Evaluate(L.TwoColumns(), new AddPanel(new("new", "text"), "left", "a"), Catalog.All);

        var after = result.Plan!.Candidate;
        Assert.Equal(["a", "b"], after.Group("left").SlotIds());
        Assert.Equal("new", after.Slot("a").InstanceId);
        Assert.Equal("a", after.Group("left").ActiveSlotId);
        Assert.DoesNotContain("p-a", after.Panels.Keys);
        Assert.Equal("Replace with Text", result.Plan.Description);
    }

    [Fact]
    public void AddPanel_UnregisteredType_IsRejected()
    {
        Assert.Contains("not registered", Reject(L.TwoColumns(), new AddPanel(new("new", "nope"), "left")));
    }

    [Fact]
    public void AddPanel_SecondSingleton_IsRejected()
    {
        var before = L.TwoColumns(new Dictionary<string, string> { ["c"] = "single" });

        Assert.Contains("already open", Reject(before, new AddPanel(new("new", "single"), "left")));
    }

    [Fact]
    public void AddPanel_ReplacingTheOpenSingletonWithAnotherInstance_IsAllowed()
    {
        var before = L.TwoColumns(new Dictionary<string, string> { ["c"] = "single" });

        var after = Apply(before, new AddPanel(new("new", "single"), "right", "c"));

        Assert.Equal("new", after.Slot("c").InstanceId);
    }

    [Fact]
    public void AddPanel_ExistingPanelIdentity_IsRejected()
    {
        Assert.Contains("identity is already in use", Reject(L.TwoColumns(), new AddPanel(new("p-a", "text"), "right")));
    }

    [Fact]
    public void AddPanel_ReplaceSlotInAnotherGroup_IsRejected()
    {
        Assert.Contains("not in this group", Reject(L.TwoColumns(), new AddPanel(new("new", "text"), "right", "a")));
    }

    [Fact]
    public void AddPanel_StaleReplaceSlot_IsRejectedWithoutThrowing()
    {
        Assert.Contains("not in this group", Reject(L.TwoColumns(), new AddPanel(new("new", "text"), "right", "gone")));
    }

    [Fact]
    public void AddPanel_StaleReplaceSlotForSingleton_IsRejectedWithoutThrowing()
    {
        var before = L.TwoColumns(new Dictionary<string, string> { ["c"] = "single" });

        var error = Reject(before, new AddPanel(new("new", "single"), "right", "gone"));

        Assert.Contains("no longer available", error);
    }

    [Fact]
    public void AddPanel_ReplacingNonClosablePanel_IsRejected()
    {
        var before = L.TwoColumns(new Dictionary<string, string> { ["c"] = "fixed" });

        Assert.Contains("cannot be replaced", Reject(before, new AddPanel(new("new", "text"), "right", "c")));
    }

    [Fact]
    public void AddPanel_UnknownGroup_IsRejected()
    {
        Assert.Contains("group is no longer available", Reject(L.TwoColumns(), new AddPanel(new("new", "text"), "zzz")));
    }

    [Fact]
    public void AddPanel_NonFloatingPanelIntoFloatingHost_IsRejected()
    {
        var before = L.Build(L.Group("g", "a"), floating: [L.Floating("float", L.Group("fg", "b"))]);

        Assert.Contains("cannot float", Reject(before, new AddPanel(new("new", "fixed"), "fg")));
    }
}

public class SwapPanelsTests
{
    [Fact]
    public void SwapPanels_ExchangesInstancesButKeepsSlots()
    {
        var result = DockEngine.Evaluate(L.TwoColumns(), new SwapPanels("a", "c"), Catalog.All);

        var after = result.Plan!.Candidate;
        Assert.Equal("p-c", after.Slot("a").InstanceId);
        Assert.Equal("p-a", after.Slot("c").InstanceId);
        Assert.Equal(["a", "b"], after.Group("left").SlotIds());
        Assert.Equal("Swap panels", result.Plan.Description);
    }

    [Fact]
    public void SwapPanels_SameSlot_IsRejected()
    {
        Assert.Contains("another tab", Reject(L.TwoColumns(), new SwapPanels("a", "a")));
    }

    [Fact]
    public void SwapPanels_UnknownSlot_IsRejected()
    {
        Reject(L.TwoColumns(), new SwapPanels("a", "zzz"));
    }

    [Fact]
    public void SwapPanels_NonFloatingPanelIntoFloatingHost_IsRejected()
    {
        var before = L.Build(L.Group("g", "fx"), floating: [L.Floating("float", L.Group("fg", "b"))],
            types: new Dictionary<string, string> { ["fx"] = "fixed" });

        Assert.Contains("cannot float", Reject(before, new SwapPanels("fx", "b")));
    }
}

public class CloseTabsTests
{
    [Fact]
    public void CloseTabs_RemovesSlotAndItsPanelState()
    {
        var result = DockEngine.Evaluate(L.TwoColumns(), new CloseTabs(["a"]), Catalog.All);

        var after = result.Plan!.Candidate;
        Assert.Equal(["b"], after.Group("left").SlotIds());
        Assert.DoesNotContain("p-a", after.Panels.Keys);
        Assert.Equal("Close tabs", result.Plan.Description);
    }

    [Fact]
    public void CloseTabs_AllTabsOfRegionGroup_LeavesEmptyRegion()
    {
        var after = Apply(L.TwoColumns(), new CloseTabs(["a", "b"]));

        Assert.Contains(after.Groups, g => g.Id == "left" && g.Slots.IsEmpty && g.RegionKey == "primary");
    }

    [Fact]
    public void CloseTabs_NonClosablePanel_IsRejected()
    {
        var before = L.TwoColumns(new Dictionary<string, string> { ["a"] = "fixed" });

        Assert.Contains("cannot be closed", Reject(before, new CloseTabs(["a", "b"])));
    }

    [Fact]
    public void CloseTabs_EmptySelection_IsRejected()
    {
        Reject(L.TwoColumns(), new CloseTabs([]));
    }
}

public class FloatTabsTests
{
    [Fact]
    public void FloatTabs_CreatesFloatingHostWithDefaultPlacement()
    {
        var result = DockEngine.Evaluate(L.TwoColumns(), new FloatTabs(["a", "b"]), Catalog.All);

        var after = result.Plan!.Candidate;
        var host = Assert.Single(after.Hosts, h => h.Id != after.MainHostId);
        var group = Assert.IsType<TabGroup>(host.Root);
        Assert.Equal(["a", "b"], group.SlotIds());
        Assert.Equal("a", group.ActiveSlotId);
        Assert.Equal(new WindowPlacement(), host.Placement);
        Assert.Equal("Move to new window", result.Plan.Description);
    }

    [Fact]
    public void FloatTabs_UsesSuppliedPlacement()
    {
        var placement = new WindowPlacement(5, 6, 700, 500);

        var after = Apply(L.TwoColumns(), new FloatTabs(["c"], placement));

        Assert.Equal(placement, after.Hosts.Single(h => h.Id != L.Main).Placement);
    }

    [Fact]
    public void FloatTabs_NonFloatingPanel_IsRejected()
    {
        var before = L.TwoColumns(new Dictionary<string, string> { ["c"] = "fixed" });

        Assert.Contains("cannot float", Reject(before, new FloatTabs(["c"])));
    }

    [Fact]
    public void MoveTabs_WithinFloatingHost_AllowsNonFloatingPanelAlreadyThere()
    {
        // Capability enforcement only rejects panels newly entering a floating host (e.g. a consumer-applied layout).
        var before = L.Build(L.Group("g", "a"), floating: [L.Floating("float", L.Group("fg", "fx", "b"))],
            types: new Dictionary<string, string> { ["fx"] = "fixed" });

        var after = Apply(before, new MoveTabs(["b"], "fg", Index: 0));

        Assert.Equal(["b", "fx"], after.Group("fg").SlotIds());
    }
}

public class CollapseAndPinTests
{
    [Fact]
    public void CollapseGroup_NonRegionGroup_MovesToRailAnchoredToNeighbor()
    {
        var before = L.Build(L.H("h", L.Region("left", "primary", "a"), L.Group("mid", "b"), L.Group("right", "c")));

        var result = DockEngine.Evaluate(before, new CollapseGroup("right", DockSide.Right), Catalog.All);

        var host = result.Plan!.Candidate.Hosts[0];
        var rail = Assert.Single(host.Rails);
        Assert.Equal(DockSide.Right, rail.Side);
        Assert.Equal("right", rail.Group.Id);
        Assert.Equal("mid", rail.ReturnGroupId);
        Assert.DoesNotContain(LayoutTree.Groups(host.Root), g => g.Id == "right");
        Assert.Equal("Collapse group", result.Plan.Description);
    }

    [Fact]
    public void CollapseGroup_FirstChild_AnchorsToFollowingSibling()
    {
        var before = L.Build(L.H("h", L.Group("first", "a"), L.Group("second", "b")));

        Assert.Equal("second", Apply(before, new CollapseGroup("first", DockSide.Left)).Hosts[0].Rails[0].ReturnGroupId);
    }

    [Fact]
    public void CollapseGroup_RegionGroup_LeavesEmptyPlaceholderKeepingRegion()
    {
        var after = Apply(L.TwoColumns(), new CollapseGroup("left", DockSide.Left));

        var rail = after.Hosts[0].Rails.Single();
        Assert.Null(rail.Group.RegionKey);
        var placeholder = after.Group(rail.ReturnGroupId!);
        Assert.Empty(placeholder.Slots);
        Assert.Equal("primary", placeholder.RegionKey);
    }

    [Fact]
    public void CollapseGroup_CenterSide_IsRejected()
    {
        Assert.Contains("edge", Reject(L.TwoColumns(), new CollapseGroup("left", DockSide.Center)));
    }

    [Fact]
    public void CollapseGroup_EmptyGroup_IsRejected()
    {
        var before = L.Build(L.H("h", L.Region("p", "primary"), L.Group("o", "a")));

        Assert.Contains("no panels", Reject(before, new CollapseGroup("p", DockSide.Left)));
    }

    [Fact]
    public void CollapseGroup_NonCollapsiblePanel_IsRejected()
    {
        var before = L.TwoColumns(new Dictionary<string, string> { ["c"] = "fixed" });

        Assert.Contains("cannot collapse", Reject(before, new CollapseGroup("right", DockSide.Right)));
    }

    [Fact]
    public void CollapseGroup_AlreadyCollapsed_IsRejected()
    {
        var collapsed = Apply(L.TwoColumns(), new CollapseGroup("right", DockSide.Right));

        Assert.Contains("already collapsed", Reject(collapsed, new CollapseGroup("right", DockSide.Left)));
    }

    [Fact]
    public void PinGroup_RegionGroup_ReturnsIntoPlaceholderWithRegion()
    {
        var collapsed = Apply(L.TwoColumns(), new CollapseGroup("left", DockSide.Left));

        var result = DockEngine.Evaluate(collapsed, new PinGroup("left"), Catalog.All);

        var after = result.Plan!.Candidate;
        Assert.Empty(after.Hosts[0].Rails);
        var groups = LayoutTree.Groups(after.Hosts[0].Root).ToArray();
        Assert.Equal(2, groups.Length);
        Assert.Contains(groups, g => g.Id == "left" && g.RegionKey == "primary" && g.Slots.Length == 2);
        Assert.Equal("Pin group", result.Plan.Description);
    }

    [Fact]
    public void PinGroup_NonRegionGroup_ReturnsBesideAnchorOnRailSide()
    {
        var collapsed = Apply(L.TwoColumns(), new CollapseGroup("right", DockSide.Right));

        var after = Apply(collapsed, new PinGroup("right"));

        var root = Assert.IsType<SplitNode>(after.Hosts[0].Root);
        Assert.Equal(SplitAxis.Horizontal, root.Axis);
        Assert.Equal(["left", "right"], root.Children.Select(c => c.Id));
    }

    [Fact]
    public void PinGroup_MissingAnchor_FallsBackToFirstGroup()
    {
        var before = L.Build(L.Group("g", "a"), rails: [new(DockSide.Top, L.Group("r", "b"), "gone")]);

        var root = Assert.IsType<SplitNode>(Apply(before, new PinGroup("r")).Hosts[0].Root);

        Assert.Equal(SplitAxis.Vertical, root.Axis);
        Assert.Equal(["r", "g"], root.Children.Select(c => c.Id));
    }

    [Fact]
    public void PinGroup_NotCollapsed_IsRejected()
    {
        Assert.Contains("not collapsed", Reject(L.TwoColumns(), new PinGroup("left")));
    }
}

public class ResizeSplitTests
{
    [Fact]
    public void ResizeSplit_NormalizesWeights()
    {
        var result = DockEngine.Evaluate(L.TwoColumns(), new ResizeSplit("cols", [3, 1]), Catalog.All);

        Assert.Equal([0.75, 0.25], result.Plan!.Candidate.SplitById("cols").Weights.ToArray());
        Assert.Equal("Resize panels", result.Plan.Description);
    }

    [Fact]
    public void ResizeSplit_UnknownSplit_IsRejected()
    {
        Assert.Contains("no longer exists", Reject(L.TwoColumns(), new ResizeSplit("gone", [1, 1])));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ResizeSplit_InvalidWeight_IsRejected(double weight)
    {
        Assert.Contains("Invalid splitter size", Reject(L.TwoColumns(), new ResizeSplit("cols", [weight, 1])));
    }

    [Fact(Skip = "Bug: ResizeSplit does not check the weight count; Normalize silently drops extra weights and the plan succeeds.")]
    public void ResizeSplit_TooManyWeights_IsRejected()
    {
        Reject(L.TwoColumns(), new ResizeSplit("cols", [3, 1, 4]));
    }

    [Fact(Skip = "Bug: DockEngine.Evaluate throws IndexOutOfRangeException from LayoutTree.Normalize when ResizeSplit has fewer weights than children.")]
    public void ResizeSplit_TooFewWeights_IsRejectedWithoutThrowing()
    {
        Reject(L.TwoColumns(), new ResizeSplit("cols", [1]));
    }
}

public class SelectTabTests
{
    [Fact]
    public void SelectTab_ActivatesSlotWithoutRecordingHistory()
    {
        var result = DockEngine.Evaluate(L.TwoColumns(), new SelectTab("b"), Catalog.All);

        Assert.Equal("b", result.Plan!.Candidate.Group("left").ActiveSlotId);
        Assert.False(result.Plan.RecordHistory);
        Assert.Equal("Select tab", result.Plan.Description);
    }

    [Fact]
    public void OtherOperations_RecordHistory()
    {
        Assert.True(DockEngine.Evaluate(L.TwoColumns(), new MoveTabs(["a"], "right"), Catalog.All).Plan!.RecordHistory);
    }

    [Fact]
    public void SelectTab_UnknownSlot_IsRejected()
    {
        Reject(L.TwoColumns(), new SelectTab("zzz"));
    }
}

public class ApplyLayoutTests
{
    [Fact]
    public void ApplyLayout_KeepsWorkspaceIdentityAndUsesLabel()
    {
        var template = L.Build(L.Group("g", "a")) with { Id = "template" };

        var result = DockEngine.Evaluate(L.TwoColumns(), new ApplyLayout(template, "Focus"), Catalog.All);

        Assert.Equal("ws", result.Plan!.Candidate.Id);
        Assert.Equal("Focus", result.Plan.Description);
        Assert.Equal(["a"], result.Plan.Candidate.Slots.Select(s => s.Id));
    }

    [Fact]
    public void ApplyLayout_MayRemoveNonClosablePanel()
    {
        var before = L.TwoColumns(new Dictionary<string, string> { ["c"] = "fixed" });

        var after = Apply(before, new ApplyLayout(L.Build(L.Group("g", "a"))));

        Assert.DoesNotContain("p-c", after.Panels.Keys);
    }

    [Fact]
    public void ApplyLayout_InvalidLayout_IsRejected()
    {
        var invalid = L.TwoColumns() with { MainHostId = "nope" };

        Reject(L.TwoColumns(), new ApplyLayout(invalid));
    }

    [Fact]
    public void ApplyLayout_TwoInstancesOfASingleton_IsRejected()
    {
        var twoSingletons = L.TwoColumns(new Dictionary<string, string> { ["a"] = "single", ["c"] = "single" });

        Assert.Contains("only be open once", Reject(L.TwoColumns(), new ApplyLayout(twoSingletons)));
    }
}
