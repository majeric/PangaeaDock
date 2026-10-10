using Pangaea.Dock.Core;
using Pangaea.Dock.Tests.Support;

namespace Pangaea.Dock.Tests.Core;

public class LayoutModelTests
{
    [Fact]
    public void Empty_HasOneMainHostWithEmptyPrimaryRegionGroup()
    {
        var layout = DockLayout.Empty();

        var host = Assert.Single(layout.Hosts);
        Assert.Equal(layout.MainHostId, host.Id);
        var group = Assert.IsType<TabGroup>(host.Root);
        Assert.Equal("primary", group.RegionKey);
        Assert.Empty(group.Slots);
        Assert.Empty(layout.Panels);
        LayoutTree.Validate(layout);
    }

    [Fact]
    public void Empty_CreatesDistinctWorkspaceIdentities()
    {
        Assert.NotEqual(DockLayout.Empty().Id, DockLayout.Empty().Id);
    }

    [Fact]
    public void IdsNew_ReturnsUnique32CharacterHex()
    {
        var a = Ids.New(); var b = Ids.New();

        Assert.NotEqual(a, b);
        Assert.Matches("^[0-9a-f]{32}$", a);
    }

    [Fact]
    public void Groups_IncludesRailGroups()
    {
        var layout = L.Build(L.Group("g", "a"), rails: [new(DockSide.Left, L.Group("r", "b"), "g")]);

        Assert.Equal(["g", "r"], layout.Groups.Select(g => g.Id));
        Assert.Equal(["a", "b"], layout.Slots.Select(s => s.Id));
    }

    [Fact]
    public void Group_UnknownId_ThrowsInvalidOperation()
    {
        var error = Assert.Throws<InvalidOperationException>(() => L.TwoColumns().Group("missing"));

        Assert.Contains("no longer available", error.Message);
    }

    [Fact]
    public void Slot_UnknownId_ThrowsInvalidOperation()
    {
        Assert.Throws<InvalidOperationException>(() => L.TwoColumns().Slot("missing"));
    }

    [Fact]
    public void ActiveSlot_UnknownActiveId_FallsBackToFirstSlot()
    {
        var group = L.Group("g", "a", "b") with { ActiveSlotId = "zzz" };

        Assert.Equal("a", group.ActiveSlot!.Id);
    }

    [Fact]
    public void ActiveSlot_EmptyGroup_IsNull()
    {
        Assert.Null(new TabGroup("g", []).ActiveSlot);
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(9.99, 19.99, true)]
    [InlineData(10, 5, false)]
    [InlineData(5, 20, false)]
    [InlineData(-0.01, 5, false)]
    public void DockRect_Contains_IsInclusiveAtOriginExclusiveAtFarEdge(double x, double y, bool expected)
    {
        Assert.Equal(expected, new DockRect(0, 0, 10, 20).Contains(x, y));
    }
}

public class LayoutTreeTests
{
    [Fact]
    public void Nodes_EnumeratesDepthFirstPreOrder()
    {
        var root = L.H("h", L.Group("a", "x"), L.V("v", L.Group("b", "y"), L.Group("c", "z")));

        Assert.Equal(["h", "a", "v", "b", "c"], LayoutTree.Nodes(root).Select(n => n.Id));
    }

    [Fact]
    public void Groups_ReturnsOnlyTabGroups()
    {
        var root = L.H("h", L.Group("a", "x"), L.V("v", L.Group("b", "y"), L.Group("c", "z")));

        Assert.Equal(["a", "b", "c"], LayoutTree.Groups(root).Select(g => g.Id));
    }

    [Fact]
    public void MapNode_MapsChildrenBeforeTheirParent()
    {
        var root = L.H("h", L.Group("a", "x"), L.Group("b", "y"));
        var visited = new List<string>();

        LayoutTree.Map(root, n => { visited.Add(n.Id); return n; });

        Assert.Equal(["a", "b", "h"], visited);
    }

    [Fact]
    public void MapNode_ReplacesMatchingNode()
    {
        var root = L.H("h", L.Group("a", "x"), L.Group("b", "y"));

        var mapped = (SplitNode)LayoutTree.Map(root, n => n.Id == "b" ? L.Group("b2", "y") : n);

        Assert.Equal(["a", "b2"], mapped.Children.Select(c => c.Id));
    }

    [Fact]
    public void MapLayout_AlsoMapsRailGroups()
    {
        var layout = L.Build(L.Group("g", "a"), rails: [new(DockSide.Left, L.Group("r", "b"), "g")]);

        var mapped = LayoutTree.Map(layout, n => n is TabGroup g ? g with { ActiveSlotId = null } : n);

        Assert.All(mapped.Groups, g => Assert.Null(g.ActiveSlotId));
    }

    [Fact]
    public void Normalize_RemovesEmptyGroupWithoutRegion()
    {
        var layout = L.Build(L.H("h", L.Group("a", "x"), new TabGroup("empty", []), L.Group("b", "y")));

        var normalized = LayoutTree.Normalize(layout);

        var split = Assert.IsType<SplitNode>(normalized.Hosts[0].Root);
        Assert.Equal(["a", "b"], split.Children.Select(c => c.Id));
        Assert.Equal(1, split.Weights.Sum(), 12);
    }

    [Fact]
    public void Normalize_KeepsEmptyRegionGroup()
    {
        var layout = L.Build(L.H("h", L.Region("primary-group", "primary"), L.Group("b", "y")));

        var normalized = LayoutTree.Normalize(layout);

        Assert.Contains(normalized.Groups, g => g.Id == "primary-group" && g.Slots.IsEmpty);
    }

    [Fact]
    public void Normalize_ReplacesSingleChildSplitWithItsChild()
    {
        var layout = L.Build(L.H("h", L.Group("a", "x"), new TabGroup("empty", [])));

        Assert.Equal("a", LayoutTree.Normalize(layout).Hosts[0].Root.Id);
    }

    [Fact]
    public void Normalize_FlattensNestedSameAxisSplitAndMultipliesWeights()
    {
        var inner = new SplitNode("inner", SplitAxis.Horizontal, [L.Group("b", "y"), L.Group("c", "z")], [0.25, 0.75]);
        var outer = new SplitNode("outer", SplitAxis.Horizontal, [L.Group("a", "x"), inner], [0.6, 0.4]);

        var root = Assert.IsType<SplitNode>(LayoutTree.Normalize(L.Build(outer)).Hosts[0].Root);

        Assert.Equal(["a", "b", "c"], root.Children.Select(c => c.Id));
        Assert.Equal(0.6, root.Weights[0], 12);
        Assert.Equal(0.1, root.Weights[1], 12);
        Assert.Equal(0.3, root.Weights[2], 12);
    }

    [Fact]
    public void Normalize_KeepsCrossAxisNesting()
    {
        var layout = L.Build(L.H("h", L.Group("a", "x"), L.V("v", L.Group("b", "y"), L.Group("c", "z"))));

        var root = Assert.IsType<SplitNode>(LayoutTree.Normalize(layout).Hosts[0].Root);

        Assert.IsType<SplitNode>(root.Children[1]);
    }

    [Fact]
    public void Normalize_RescalesWeightsWhenChildIsRemoved()
    {
        var split = new SplitNode("h", SplitAxis.Horizontal, [L.Group("a", "x"), new TabGroup("e", []), L.Group("b", "y")], [0.2, 0.5, 0.3]);

        var root = Assert.IsType<SplitNode>(LayoutTree.Normalize(L.Build(split)).Hosts[0].Root);

        Assert.Equal(0.4, root.Weights[0], 12);
        Assert.Equal(0.6, root.Weights[1], 12);
    }

    [Fact]
    public void Normalize_PreservesAlreadyNormalizedWeightsExactly()
    {
        double[] weights = [0.1, 0.2, 0.7];
        var split = new SplitNode("h", SplitAxis.Horizontal, [L.Group("a", "x"), L.Group("b", "y"), L.Group("c", "z")], [.. weights]);

        var root = (SplitNode)LayoutTree.Normalize(L.Build(split)).Hosts[0].Root;

        Assert.Equal(weights, root.Weights.ToArray());
    }

    [Fact]
    public void Normalize_RepairsActiveSlotThatIsNotInGroup()
    {
        var layout = L.Build(L.Group("g", "a", "b") with { ActiveSlotId = "gone" });

        Assert.Equal("a", LayoutTree.Normalize(layout).Groups.Single().ActiveSlotId);
    }

    [Fact]
    public void Normalize_DropsEmptyFloatingHost()
    {
        var layout = L.Build(L.Group("g", "a"), floating: [L.Floating("float", new TabGroup("fg", []))]);

        Assert.Equal([L.Main], LayoutTree.Normalize(layout).Hosts.Select(h => h.Id));
    }

    [Fact]
    public void Normalize_KeepsEmptyMainHostWithEmptyRootGroupOfSameId()
    {
        var layout = L.Build(new TabGroup("root", []));

        var normalized = LayoutTree.Normalize(layout);

        var root = Assert.IsType<TabGroup>(Assert.Single(normalized.Hosts).Root);
        Assert.Equal("root", root.Id);
        LayoutTree.Validate(normalized);
    }

    [Fact]
    public void Normalize_KeepsFloatingHostThatOnlyHasRails()
    {
        var floating = new DockHost("float", new TabGroup("fg", []), [new(DockSide.Left, L.Group("r", "b"), null)], new());
        var layout = L.Build(L.Group("g", "a"), floating: [floating]);

        Assert.Equal(2, LayoutTree.Normalize(layout).Hosts.Length);
    }

    [Fact]
    public void Normalize_DropsEmptyRails()
    {
        var layout = L.Build(L.Group("g", "a"), rails: [new(DockSide.Left, new TabGroup("r", []), "g")]);

        Assert.Empty(LayoutTree.Normalize(layout).Hosts[0].Rails);
    }

    [Fact]
    public void Normalize_RemovesPanelStatesWithoutSlots()
    {
        var layout = L.TwoColumns();
        layout = layout with { Panels = layout.Panels.Add("orphan", new PanelState("orphan", "text")) };

        var normalized = LayoutTree.Normalize(layout);

        Assert.DoesNotContain("orphan", normalized.Panels.Keys);
        Assert.Equal(3, normalized.Panels.Count);
    }

    [Fact]
    public void Normalize_IsIdempotentForUnevenWeights()
    {
        var inner = new SplitNode("inner", SplitAxis.Horizontal, [L.Group("b", "y"), L.Group("c", "z")], [1 / 3.0, 2 / 3.0]);
        var outer = new SplitNode("outer", SplitAxis.Horizontal, [L.Group("a", "x"), inner, new TabGroup("e", [])], [0.3, 0.3, 0.4]);

        var once = LayoutTree.Normalize(L.Build(outer));
        var twice = LayoutTree.Normalize(once);

        Assert.Equal(((SplitNode)once.Hosts[0].Root).Weights.ToArray(), ((SplitNode)twice.Hosts[0].Root).Weights.ToArray());
    }
}
