using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Pangaea.Dock.Core;

public enum SplitAxis { Horizontal, Vertical }
public enum DockSide { Center, Left, Right, Top, Bottom }
public sealed record DockSlot(string Id, string InstanceId);
public sealed record PanelState(string Id, string TypeId, string State = "{}", int StateVersion = 1, string? DocumentId = null);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(TabGroup), "tabs")]
[JsonDerivedType(typeof(SplitNode), "split")]
public abstract record LayoutNode(string Id);
public sealed record TabGroup(string Id, ImmutableArray<DockSlot> Slots, string? ActiveSlotId = null,
    string? RegionKey = null) : LayoutNode(Id)
{
    [JsonIgnore] public DockSlot? ActiveSlot => Slots.FirstOrDefault(s => s.Id == ActiveSlotId) ?? Slots.FirstOrDefault();
}
public sealed record SplitNode(string Id, SplitAxis Axis, ImmutableArray<LayoutNode> Children,
    ImmutableArray<double> Weights) : LayoutNode(Id);
public sealed record RailGroup(DockSide Side, TabGroup Group, string? ReturnGroupId, double Extent = 300);
public sealed record WindowPlacement(double X = 120, double Y = 120, double Width = 640, double Height = 480,
    double Scale = 1, bool Maximized = false);
public sealed record DockHost(string Id, LayoutNode Root, ImmutableArray<RailGroup> Rails,
    WindowPlacement? Placement = null);
public sealed record DockLayout(string Id, string MainHostId, ImmutableArray<DockHost> Hosts,
    ImmutableDictionary<string, PanelState> Panels, long Revision = 0)
{
    public static DockLayout Empty() => new(Ids.New(), "main",
        [new("main", new TabGroup(Ids.New(), [], RegionKey: "primary"), [])],
        ImmutableDictionary<string, PanelState>.Empty);
    [JsonIgnore] public IEnumerable<TabGroup> Groups => Hosts.SelectMany(h => LayoutTree.Groups(h.Root)
        .Concat(h.Rails.Select(r => r.Group)));
    [JsonIgnore] public IEnumerable<DockSlot> Slots => Groups.SelectMany(g => g.Slots);
    public TabGroup Group(string id) => Groups.FirstOrDefault(g => g.Id == id)
        ?? throw new InvalidOperationException("The target group is no longer available.");
    public DockSlot Slot(string id) => Slots.FirstOrDefault(s => s.Id == id)
        ?? throw new InvalidOperationException("The tab is no longer available.");
}
public static class Ids { public static string New() => Guid.NewGuid().ToString("N"); }

public static class LayoutTree
{
    public static IEnumerable<LayoutNode> Nodes(LayoutNode node)
    {
        yield return node;
        if (node is SplitNode split)
            foreach (var child in split.Children)
                foreach (var descendant in Nodes(child)) yield return descendant;
    }
    public static IEnumerable<TabGroup> Groups(LayoutNode node) => Nodes(node).OfType<TabGroup>();
    public static LayoutNode Map(LayoutNode node, Func<LayoutNode, LayoutNode> map) => map(node is SplitNode s
        ? s with { Children = s.Children.Select(c => Map(c, map)).ToImmutableArray() } : node);
    public static DockLayout Map(DockLayout layout, Func<LayoutNode, LayoutNode> map) => layout with
    {
        Hosts = layout.Hosts.Select(h => h with
        {
            Root = Map(h.Root, map),
            Rails = h.Rails.Select(r => r with { Group = (TabGroup)map(r.Group) }).ToImmutableArray()
        }).ToImmutableArray()
    };
    public static DockLayout Normalize(DockLayout layout)
    {
        LayoutNode? Clean(LayoutNode node)
        {
            if (node is TabGroup g)
            {
                if (g.Slots.IsEmpty && g.RegionKey is null) return null;
                return g with { ActiveSlotId = g.Slots.Any(s => s.Id == g.ActiveSlotId) ? g.ActiveSlotId : g.Slots.FirstOrDefault()?.Id };
            }
            var split = (SplitNode)node;
            var children = new List<LayoutNode>();
            var weights = new List<double>();
            for (int i = 0; i < split.Children.Length; i++)
            {
                var child = Clean(split.Children[i]);
                if (child is null) continue;
                if (child is SplitNode inner && inner.Axis == split.Axis)
                {
                    children.AddRange(inner.Children);
                    weights.AddRange(inner.Weights.Select(w => w * split.Weights[i]));
                }
                else { children.Add(child); weights.Add(split.Weights[i]); }
            }
            if (children.Count == 0) return null;
            if (children.Count == 1) return children[0];
            var total = weights.Sum();
            return split with { Children = [.. children], Weights = [.. weights.Select(w => Math.Abs(total - 1) < 1e-12 ? w : w / total)] };
        }
        var hosts = new List<DockHost>();
        foreach (var host in layout.Hosts)
        {
            var root = Clean(host.Root);
            var rails = host.Rails.Where(r => !r.Group.Slots.IsEmpty)
                .Select(r => r with { Group = (TabGroup)Clean(r.Group)! }).ToImmutableArray();
            if (root is null && rails.IsEmpty && host.Id != layout.MainHostId) continue;
            hosts.Add(host with { Root = root ?? new TabGroup(host.Root.Id, []), Rails = rails });
        }
        var result = layout with { Hosts = [.. hosts] };
        var live = result.Slots.Select(s => s.InstanceId).ToHashSet();
        return result with { Panels = result.Panels.Where(p => live.Contains(p.Key)).ToImmutableDictionary() };
    }
    public static void Validate(DockLayout layout)
    {
        if (layout is null || layout.Panels is null || string.IsNullOrWhiteSpace(layout.Id))
            throw new InvalidDataException("Missing workspace data.");
        if (layout.Hosts.IsDefaultOrEmpty || layout.Hosts.Length > 32 || layout.Hosts.Any(h => h is null) || layout.Hosts.Count(h => h.Id == layout.MainHostId) != 1)
            throw new InvalidDataException("A workspace needs exactly one main host (and at most 32 hosts).");
        var ids = new HashSet<string>();
        var instances = new HashSet<string>();
        void Unique(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || !ids.Add(id)) throw new InvalidDataException("Empty or duplicate layout identity.");
            if (ids.Count > 2048) throw new InvalidDataException("Workspace exceeds 2048 layout items.");
        }
        void Visit(LayoutNode node, int depth)
        {
            if (node is null) throw new InvalidDataException("Missing layout node.");
            if (depth > 32) throw new InvalidDataException("Workspace is nested too deeply.");
            Unique(node.Id);
            if (node is SplitNode split)
            {
                if (!Enum.IsDefined(split.Axis) || split.Children.IsDefault || split.Weights.IsDefault || split.Children.Length < 2 || split.Children.Length != split.Weights.Length ||
                    split.Weights.Any(w => !double.IsFinite(w) || w <= 0) || Math.Abs(split.Weights.Sum() - 1) > 0.00001)
                    throw new InvalidDataException("Invalid split weights.");
                foreach (var child in split.Children) Visit(child, depth + 1);
            }
            else if (node is TabGroup group)
            {
                if (group.Slots.IsDefault) throw new InvalidDataException("Missing tab collection.");
                foreach (var slot in group.Slots)
                {
                    if (slot is null || string.IsNullOrWhiteSpace(slot.InstanceId)) throw new InvalidDataException("Missing tab data.");
                    Unique(slot.Id);
                    if (!instances.Add(slot.InstanceId) || !layout.Panels.ContainsKey(slot.InstanceId))
                        throw new InvalidDataException("A panel instance must have exactly one slot and a state record.");
                }
                if (group.ActiveSlotId is not null && !group.Slots.Any(s => s.Id == group.ActiveSlotId))
                    throw new InvalidDataException("Active tab does not belong to its group.");
            }
        }
        foreach (var host in layout.Hosts)
        {
            if (host is null || host.Rails.IsDefault) throw new InvalidDataException("Missing host data.");
            Unique(host.Id); Visit(host.Root, 0);
            foreach (var rail in host.Rails)
            {
                if (rail is null || !Enum.IsDefined(rail.Side) || rail.Side == DockSide.Center || !double.IsFinite(rail.Extent) || rail.Extent < 80)
                    throw new InvalidDataException("Invalid rail.");
                Visit(rail.Group, 0);
            }
            if (host.Placement is { } p && (!double.IsFinite(p.X) || !double.IsFinite(p.Y) ||
                !double.IsFinite(p.Width) || !double.IsFinite(p.Height) || !double.IsFinite(p.Scale) || p.Width < 100 || p.Height < 100 || p.Scale <= 0))
                throw new InvalidDataException("Invalid floating-window bounds.");
        }
        foreach (var panel in layout.Panels)
            if (panel.Value is null || panel.Key != panel.Value.Id || string.IsNullOrWhiteSpace(panel.Value.TypeId) || panel.Value.State is null || panel.Value.State.Length > 262144)
                throw new InvalidDataException("Invalid panel state.");
        if (!instances.SetEquals(layout.Panels.Keys)) throw new InvalidDataException("Unowned panel state.");
    }
}
