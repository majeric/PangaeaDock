using System.Collections.Immutable;
using Pangaea.Dock.Core;
using Pangaea.Dock.Tests.Support;

namespace Pangaea.Dock.Tests.Core;

public class LayoutValidationTests
{
    private static readonly DockLayout Valid = L.Build(
        L.H("cols", L.Region("left", "primary", "a", "b"), L.V("stack", L.Group("top", "c"), L.Group("bottom", "d"))),
        rails: [new(DockSide.Right, L.Group("rail", "e"), "left")],
        floating: [L.Floating("float", L.Group("fg", "f"), new WindowPlacement(10, 20, 300, 200))]);

    private static DockLayout MapGroup(DockLayout layout, string id, Func<TabGroup, TabGroup> change) =>
        LayoutTree.Map(layout, n => n is TabGroup g && g.Id == id ? change(g) : n);
    private static DockLayout MapSplit(DockLayout layout, string id, Func<SplitNode, SplitNode> change) =>
        LayoutTree.Map(layout, n => n is SplitNode s && s.Id == id ? change(s) : n);
    private static DockLayout MapHost(DockLayout layout, string id, Func<DockHost, DockHost> change) =>
        layout with { Hosts = layout.Hosts.Select(h => h.Id == id ? change(h) : h).ToImmutableArray() };

    public static TheoryData<string> InvalidCases => new(Cases.Keys);

    private static readonly Dictionary<string, Func<DockLayout, DockLayout>> Cases = new()
    {
        ["blank workspace id"] = l => l with { Id = " " },
        ["null panel map"] = l => l with { Panels = null! },
        ["no hosts"] = l => l with { Hosts = [] },
        ["default hosts"] = l => l with { Hosts = default },
        ["null host"] = l => l with { Hosts = l.Hosts.Add(null!) },
        ["missing main host"] = l => l with { MainHostId = "nope" },
        ["two main hosts"] = l => l with { Hosts = l.Hosts.Add(l.Hosts[0] with { Root = new TabGroup("other-root", []) }) },
        ["more than 32 hosts"] = l => l with { Hosts = l.Hosts.AddRange(Enumerable.Range(0, 32).Select(i => new DockHost("h" + i, new TabGroup("hg" + i, []), []))) },
        ["duplicate node id"] = l => MapGroup(l, "top", g => g with { Id = "bottom" }),
        ["slot id equals node id"] = l => MapGroup(l, "top", g => g with { Slots = [new DockSlot("left", "p-c")], ActiveSlotId = "left" }),
        ["blank slot id"] = l => MapGroup(l, "top", g => g with { Slots = [new DockSlot("", "p-c")], ActiveSlotId = null }),
        ["blank instance id"] = l => MapGroup(l, "top", g => g with { Slots = [new DockSlot("c", " ")] }),
        ["null slot"] = l => MapGroup(l, "top", g => g with { Slots = [null!], ActiveSlotId = null }),
        ["default slot array"] = l => MapGroup(l, "top", g => g with { Slots = default, ActiveSlotId = null }),
        ["instance without state"] = l => MapGroup(l, "top", g => g with { Slots = [new DockSlot("c", "ghost")] }),
        ["instance in two slots"] = l => MapGroup(l, "top", g => g with { Slots = [new DockSlot("c", "p-d")] }),
        ["active slot outside group"] = l => MapGroup(l, "top", g => g with { ActiveSlotId = "a" }),
        ["split with one child"] = l => MapSplit(l, "stack", s => s with { Children = [s.Children[0]], Weights = [1] }),
        ["weights count mismatch"] = l => MapSplit(l, "stack", s => s with { Weights = [0.2, 0.3, 0.5] }),
        ["weights not summing to one"] = l => MapSplit(l, "stack", s => s with { Weights = [0.5, 0.6] }),
        ["zero weight"] = l => MapSplit(l, "stack", s => s with { Weights = [0, 1] }),
        ["NaN weight"] = l => MapSplit(l, "stack", s => s with { Weights = [double.NaN, 1] }),
        ["default weights"] = l => MapSplit(l, "stack", s => s with { Weights = default }),
        ["default children"] = l => MapSplit(l, "stack", s => s with { Children = default }),
        ["undefined axis"] = l => MapSplit(l, "stack", s => s with { Axis = (SplitAxis)42 }),
        ["null child node"] = l => MapSplit(l, "stack", s => s with { Children = [s.Children[0], null!] }),
        ["center rail"] = l => MapHost(l, L.Main, h => h with { Rails = [h.Rails[0] with { Side = DockSide.Center }] }),
        ["undefined rail side"] = l => MapHost(l, L.Main, h => h with { Rails = [h.Rails[0] with { Side = (DockSide)99 }] }),
        ["rail extent below 80"] = l => MapHost(l, L.Main, h => h with { Rails = [h.Rails[0] with { Extent = 79 }] }),
        ["infinite rail extent"] = l => MapHost(l, L.Main, h => h with { Rails = [h.Rails[0] with { Extent = double.PositiveInfinity }] }),
        ["null rail"] = l => MapHost(l, L.Main, h => h with { Rails = [null!] }),
        ["default rail array"] = l => MapHost(l, L.Main, h => h with { Rails = default }),
        ["placement narrower than 100"] = l => MapHost(l, "float", h => h with { Placement = new(0, 0, 99, 200) }),
        ["placement shorter than 100"] = l => MapHost(l, "float", h => h with { Placement = new(0, 0, 200, 99) }),
        ["NaN placement"] = l => MapHost(l, "float", h => h with { Placement = new(double.NaN, 0, 200, 200) }),
        ["zero scale"] = l => MapHost(l, "float", h => h with { Placement = new(0, 0, 200, 200, Scale: 0) }),
        ["panel key differs from id"] = l => l with { Panels = l.Panels.SetItem("p-a", new PanelState("other", "text")) },
        ["blank panel type"] = l => l with { Panels = l.Panels.SetItem("p-a", new PanelState("p-a", "")) },
        ["null panel state text"] = l => l with { Panels = l.Panels.SetItem("p-a", new PanelState("p-a", "text", null!)) },
        ["panel state over 256 KiB"] = l => l with { Panels = l.Panels.SetItem("p-a", new PanelState("p-a", "text", new string('x', 262145))) },
        ["null panel state"] = l => l with { Panels = l.Panels.SetItem("p-a", null!) },
        ["unowned panel state"] = l => l with { Panels = l.Panels.Add("orphan", new PanelState("orphan", "text")) },
    };

    [Fact]
    public void Validate_AcceptsWellFormedLayoutWithRailsAndFloatingHost()
    {
        LayoutTree.Validate(Valid);
    }

    [Fact]
    public void Validate_AcceptsPanelStateOfExactlyTheSizeLimit()
    {
        LayoutTree.Validate(Valid with { Panels = Valid.Panels.SetItem("p-a", new PanelState("p-a", "text", new string('x', 262144))) });
    }

    [Fact]
    public void Validate_NullLayout_ThrowsInvalidData()
    {
        Assert.Throws<InvalidDataException>(() => LayoutTree.Validate(null!));
    }

    [Theory]
    [MemberData(nameof(InvalidCases))]
    public void Validate_RejectsCorruptLayout(string corruption)
    {
        var corrupt = Cases[corruption](Valid);

        Assert.Throws<InvalidDataException>(() => LayoutTree.Validate(corrupt));
    }

    [Fact]
    public void Validate_RejectsNestingDeeperThan32()
    {
        LayoutNode node = L.Group("leaf", "a");
        for (int i = 0; i < 33; i++)
            node = new SplitNode("s" + i, i % 2 == 0 ? SplitAxis.Horizontal : SplitAxis.Vertical, [node, new TabGroup("e" + i, [], RegionKey: "r")], [0.5, 0.5]);

        var error = Assert.Throws<InvalidDataException>(() => LayoutTree.Validate(L.Build(node)));
        Assert.Contains("nested too deeply", error.Message);
    }

    [Fact]
    public void Validate_RejectsMoreThan2048Identities()
    {
        var slots = Enumerable.Range(0, 2048).Select(i => "s" + i).ToArray();

        var error = Assert.Throws<InvalidDataException>(() => LayoutTree.Validate(L.Build(L.Group("g", slots))));
        Assert.Contains("2048", error.Message);
    }
}
