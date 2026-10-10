using System.Collections.Immutable;
using Pangaea.Dock.Core;

namespace Pangaea.Dock.Tests.Support;

/// <summary>Hand-built layouts for pure model tests. Slot "x" binds to panel instance "p-x".</summary>
internal static class L
{
    public const string Main = "main";
    public static string P(string slotId) => "p-" + slotId;
    public static DockSlot Slot(string id) => new(id, P(id));
    public static TabGroup Group(string id, params string[] slots) =>
        new(id, slots.Select(Slot).ToImmutableArray(), slots.FirstOrDefault());
    public static TabGroup Region(string id, string region, params string[] slots) => Group(id, slots) with { RegionKey = region };
    public static SplitNode H(string id, params LayoutNode[] children) => Split(id, SplitAxis.Horizontal, children);
    public static SplitNode V(string id, params LayoutNode[] children) => Split(id, SplitAxis.Vertical, children);
    public static SplitNode Split(string id, SplitAxis axis, LayoutNode[] children) =>
        new(id, axis, [.. children], [.. children.Select(_ => 1.0 / children.Length)]);

    /// <summary>Builds a layout whose panel states are derived from every slot. Types default to "text".</summary>
    public static DockLayout Build(LayoutNode mainRoot, ImmutableArray<RailGroup> rails = default,
        IEnumerable<DockHost>? floating = null, IReadOnlyDictionary<string, string>? types = null)
    {
        var hosts = new List<DockHost> { new(Main, mainRoot, rails.IsDefault ? [] : rails) };
        if (floating is not null) hosts.AddRange(floating);
        var layout = new DockLayout("ws", Main, [.. hosts], ImmutableDictionary<string, PanelState>.Empty);
        var panels = layout.Slots.ToImmutableDictionary(s => s.InstanceId,
            s => new PanelState(s.InstanceId, types?.GetValueOrDefault(s.Id) ?? "text"));
        return layout with { Panels = panels };
    }
    public static DockHost Floating(string id, LayoutNode root, WindowPlacement? placement = null) => new(id, root, [], placement ?? new());

    /// <summary>left[a,b] | right[c]; the left group is the primary region.</summary>
    public static DockLayout TwoColumns(IReadOnlyDictionary<string, string>? types = null) =>
        Build(H("cols", Region("left", "primary", "a", "b"), Group("right", "c")), types: types);

    public static TabGroup GroupOf(this DockLayout layout, string slotId) => layout.Groups.First(g => g.Slots.Any(s => s.Id == slotId));
    public static SplitNode SplitById(this DockLayout layout, string id) =>
        layout.Hosts.SelectMany(h => LayoutTree.Nodes(h.Root)).OfType<SplitNode>().First(s => s.Id == id);
    public static string[] SlotIds(this TabGroup group) => group.Slots.Select(s => s.Id).ToArray();
}

internal static class Catalog
{
    public static readonly PanelDescriptor Text = new("text", "Text");
    public static readonly PanelDescriptor Single = new("single", "Singleton", Singleton: true, CanDuplicate: false);
    public static readonly PanelDescriptor Other = new("other", "Other singleton", Singleton: true, CanDuplicate: false);
    public static readonly PanelDescriptor Fixed = new("fixed", "Fixed", CanClose: false, CanFloat: false, CanCollapse: false);
    public static readonly PanelDescriptor Wide = new("wide", "Wide", MinWidth: 400, MinHeight: 250);

    public static IReadOnlyDictionary<string, PanelDescriptor> All { get; } =
        new[] { Text, Single, Other, Fixed, Wide }.ToDictionary(d => d.Id);
}
