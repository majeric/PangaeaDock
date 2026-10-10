using Pangaea.Dock.Core;
using Pangaea.Dock.Tests.Support;

namespace Pangaea.Dock.Tests.Core;

public class DockGeometryAllocateTests
{
    [Fact]
    public void Allocate_WithoutClamping_IsProportionalToWeights()
    {
        Assert.Equal([250, 750], DockGeometry.Allocate(1000, [0.25, 0.75], [10, 10]));
    }

    [Fact]
    public void Allocate_ChildBelowMinimum_GetsMinimumAndRestIsShared()
    {
        Assert.Equal([200, 800], DockGeometry.Allocate(1000, [0.1, 0.9], [200, 100]));
    }

    [Fact]
    public void Allocate_ClampingCascades_UntilAllRemainingFit()
    {
        // Pass 1: 100 < 300 clamps the first child only (450 >= 360).
        // Pass 2: the remaining 700 gives the second child 350 < 360, so it clamps too; the last takes the rest.
        var sizes = DockGeometry.Allocate(1000, [0.1, 0.45, 0.45], [300, 360, 10]);

        Assert.Equal([300, 360, 340], sizes);
    }

    [Fact]
    public void Allocate_TotalBelowSumOfMinima_ReturnsMinima()
    {
        Assert.Equal([300, 200], DockGeometry.Allocate(100, [0.5, 0.5], [300, 200]));
    }

    [Fact]
    public void Allocate_AlwaysFillsTheAvailableLength()
    {
        var sizes = DockGeometry.Allocate(977, [0.2, 0.3, 0.5], [100, 100, 100]);

        Assert.Equal(977, sizes.Sum(), 9);
    }
}

public class DockGeometryMinimumTests
{
    private static readonly Dictionary<string, string> Types = new() { ["w"] = "wide" };

    [Fact]
    public void Minimum_EmptyGroup_UsesFloorSizePlusTabStrip()
    {
        var layout = L.Build(new TabGroup("g", [], RegionKey: "primary"));

        Assert.Equal(new DockSize(120, DockGeometry.TabHeight + 60), DockGeometry.Minimum(layout.Hosts[0].Root, layout, Catalog.All));
    }

    [Fact]
    public void Minimum_Group_UsesLargestDescriptorMinimum()
    {
        var layout = L.Build(L.Group("g", "a", "w"), types: Types);

        Assert.Equal(new DockSize(400, DockGeometry.TabHeight + 250), DockGeometry.Minimum(layout.Hosts[0].Root, layout, Catalog.All));
    }

    [Fact]
    public void Minimum_Group_NeverGoesBelowFloor()
    {
        var catalog = new Dictionary<string, PanelDescriptor> { ["text"] = new("text", "Tiny", MinWidth: 5, MinHeight: 5) };
        var layout = L.Build(L.Group("g", "a"));

        Assert.Equal(new DockSize(120, DockGeometry.TabHeight + 60), DockGeometry.Minimum(layout.Hosts[0].Root, layout, catalog));
    }

    [Fact]
    public void Minimum_Group_IgnoresUnregisteredPanelTypes()
    {
        var layout = L.Build(L.Group("g", "a"), types: new Dictionary<string, string> { ["a"] = "unknown" });

        Assert.Equal(new DockSize(120, DockGeometry.TabHeight + 60), DockGeometry.Minimum(layout.Hosts[0].Root, layout, Catalog.All));
    }

    [Fact]
    public void Minimum_HorizontalSplit_SumsWidthsPlusSplittersAndTakesMaxHeight()
    {
        var layout = L.Build(L.H("h", L.Group("g1", "a"), L.Group("g2", "w"), L.Group("g3", "b")), types: Types);

        var size = DockGeometry.Minimum(layout.Hosts[0].Root, layout, Catalog.All);

        Assert.Equal(160 + 400 + 160 + 2 * DockGeometry.SplitterSize, size.Width);
        Assert.Equal(DockGeometry.TabHeight + 250, size.Height);
    }

    [Fact]
    public void Minimum_VerticalSplit_SumsHeightsPlusSplitterAndTakesMaxWidth()
    {
        var layout = L.Build(L.V("v", L.Group("g1", "a"), L.Group("g2", "w")), types: Types);

        var size = DockGeometry.Minimum(layout.Hosts[0].Root, layout, Catalog.All);

        Assert.Equal(400, size.Width);
        Assert.Equal(DockGeometry.TabHeight + 100 + DockGeometry.TabHeight + 250 + DockGeometry.SplitterSize, size.Height);
    }
}

public class DockGeometryArrangeTests
{
    [Fact]
    public void Arrange_SingleGroup_FillsBoundsWithNoSplitters()
    {
        var layout = L.Build(L.Group("g", "a"));

        var geometry = DockGeometry.Arrange(layout.Hosts[0].Root, new DockRect(5, 7, 800, 600), layout, Catalog.All);

        Assert.Equal(new DockRect(5, 7, 800, 600), geometry.Groups["g"]);
        Assert.Empty(geometry.Splitters);
    }

    [Fact]
    public void Arrange_HorizontalSplit_PlacesChildrenAndSplitterSideBySide()
    {
        var layout = L.TwoColumns();
        var bounds = new DockRect(0, 0, 1006, 500);

        var geometry = DockGeometry.Arrange(layout.Hosts[0].Root, bounds, layout, Catalog.All);

        Assert.Equal(new DockRect(0, 0, 500, 500), geometry.Groups["left"]);
        Assert.Equal(new DockRect(506, 0, 500, 500), geometry.Groups["right"]);
        var splitter = Assert.Single(geometry.Splitters);
        Assert.Equal(new SplitterGeometry("cols", 0, SplitAxis.Horizontal, new DockRect(500, 0, 6, 500), bounds), splitter);
    }

    [Fact]
    public void Arrange_VerticalSplit_StacksChildren()
    {
        var layout = L.Build(L.V("v", L.Group("top", "a"), L.Group("bottom", "b")));

        var geometry = DockGeometry.Arrange(layout.Hosts[0].Root, new DockRect(10, 20, 300, 406), layout, Catalog.All);

        Assert.Equal(new DockRect(10, 20, 300, 200), geometry.Groups["top"]);
        Assert.Equal(new DockRect(10, 226, 300, 200), geometry.Groups["bottom"]);
        Assert.Equal(new DockRect(10, 220, 300, 6), geometry.Splitters[0].Bounds);
    }

    [Fact]
    public void Arrange_NestedSplits_ReportsEverySplitterWithItsIndex()
    {
        var layout = L.Build(L.H("h", L.Group("a", "x"), L.V("v", L.Group("b", "y"), L.Group("c", "z")), L.Group("d", "w")));

        var geometry = DockGeometry.Arrange(layout.Hosts[0].Root, new DockRect(0, 0, 1200, 800), layout, Catalog.All);

        Assert.Equal(4, geometry.Groups.Count);
        Assert.Equal([("h", 0), ("v", 0), ("h", 1)], geometry.Splitters.Select(s => (s.SplitId, s.Index)));
        Assert.Equal(geometry.Groups["b"].Width, geometry.Groups["c"].Width);
    }

    [Fact]
    public void Arrange_EnforcesPanelMinimumOverWeight()
    {
        var layout = L.Build(new SplitNode("h", SplitAxis.Horizontal, [L.Group("g1", "a"), L.Group("g2", "w")], [0.9, 0.1]),
            types: new Dictionary<string, string> { ["w"] = "wide" });

        var geometry = DockGeometry.Arrange(layout.Hosts[0].Root, new DockRect(0, 0, 1006, 400), layout, Catalog.All);

        Assert.Equal(400, geometry.Groups["g2"].Width);
        Assert.Equal(600, geometry.Groups["g1"].Width);
    }

    [Fact]
    public void Arrange_BoundsSmallerThanMinimum_OverflowsInsteadOfShrinking()
    {
        var layout = L.TwoColumns();

        var geometry = DockGeometry.Arrange(layout.Hosts[0].Root, new DockRect(0, 0, 100, 100), layout, Catalog.All);

        Assert.All(geometry.Groups.Values, r => Assert.Equal(160, r.Width));
    }
}
