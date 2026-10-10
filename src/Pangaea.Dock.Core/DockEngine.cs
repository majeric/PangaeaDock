using System.Collections.Immutable;

namespace Pangaea.Dock.Core;

public abstract record DockOperation;
public sealed record MoveTabs(ImmutableArray<string> SlotIds, string TargetGroupId, DockSide Side = DockSide.Center, int Index = -1) : DockOperation;
public sealed record AddPanel(PanelState Panel, string TargetGroupId, string? ReplaceSlotId = null) : DockOperation;
public sealed record SwapPanels(string FirstSlotId, string SecondSlotId) : DockOperation;
public sealed record CloseTabs(ImmutableArray<string> SlotIds) : DockOperation;
public sealed record FloatTabs(ImmutableArray<string> SlotIds, WindowPlacement? Placement = null) : DockOperation;
public sealed record CollapseGroup(string GroupId, DockSide Side) : DockOperation;
public sealed record PinGroup(string GroupId) : DockOperation;
public sealed record ResizeSplit(string SplitId, ImmutableArray<double> Weights) : DockOperation;
public sealed record SelectTab(string SlotId) : DockOperation;
/// <summary>Consumer-authored replacement layout. Capability flags restrict user operations, not this one:
/// the consumer decides which panels exist (for example after closing a document it owns).</summary>
public sealed record ApplyLayout(DockLayout Layout, string Label = "Apply workspace") : DockOperation;
public sealed record OperationPlan(string WorkspaceId, long ExpectedRevision, DockLayout Candidate, string Description, bool RecordHistory = true);
public sealed record OperationResult(OperationPlan? Plan, string? Error)
{
    public bool Success => Plan is not null;
}

/// <summary>Pure layout transformations. No views, file access, lifecycle callbacks or application dependencies.</summary>
public static class DockEngine
{
    public static OperationResult Evaluate(DockLayout before, DockOperation operation, IReadOnlyDictionary<string, PanelDescriptor> catalog)
    {
        try
        {
            var result = before; string label;
            DockLayout Remove(DockLayout layout, HashSet<string> ids) => LayoutTree.Map(layout, n => n is TabGroup g
                ? g with { Slots = g.Slots.Where(s => !ids.Contains(s.Id)).ToImmutableArray(),
                    ActiveSlotId = ids.Contains(g.ActiveSlotId ?? "") ? Next(g, ids) : g.ActiveSlotId } : n);
            ImmutableArray<DockSlot> GetSlots(IEnumerable<string> ids)
            {
                var requested = ids.ToArray();
                if (requested.Length == 0 || requested.Distinct().Count() != requested.Length) throw new InvalidOperationException("Choose one or more distinct tabs.");
                return requested.Select(before.Slot).ToImmutableArray();
            }
            void Capability(IEnumerable<DockSlot> slots, Func<PanelDescriptor, bool> allowed, string reason)
            {
                foreach (var slot in slots)
                    if (catalog.TryGetValue(before.Panels[slot.InstanceId].TypeId, out var d) && !allowed(d)) throw new InvalidOperationException(reason);
            }
            switch (operation)
            {
                case MoveTabs move:
                {
                    var slots = GetSlots(move.SlotIds); var target = before.Group(move.TargetGroupId);
                    var ids = move.SlotIds.ToHashSet();
                    bool inRail = before.Hosts.Any(h => h.Rails.Any(r => r.Group.Id == target.Id));
                    if (move.Side != DockSide.Center && inRail) throw new InvalidOperationException("Pin this group before splitting it.");
                    if (move.Side != DockSide.Center && !target.Slots.IsEmpty && target.Slots.All(s => ids.Contains(s.Id)))
                        throw new InvalidOperationException("A group cannot split beside itself without another tab remaining.");
                    result = Remove(before, ids);
                    if (move.Side == DockSide.Center)
                    {
                        int index = move.Index < 0 ? target.Slots.Length : Math.Clamp(move.Index, 0, target.Slots.Length);
                        index -= target.Slots.Take(index).Count(s => ids.Contains(s.Id));
                        result = LayoutTree.Map(result, n => n.Id == target.Id && n is TabGroup g
                            ? g with { Slots = g.Slots.InsertRange(index, slots), ActiveSlotId = slots[0].Id } : n);
                        label = "Move tabs";
                    }
                    else
                    {
                        var group = new TabGroup(Ids.New(), slots, slots[0].Id);
                        result = InsertBeside(result, target.Id, group, move.Side); label = "Split " + move.Side.ToString().ToLowerInvariant();
                    }
                    break;
                }
                case AddPanel add:
                {
                    if (!catalog.TryGetValue(add.Panel.TypeId, out var descriptor)) throw new InvalidOperationException("Panel type is not registered.");
                    var target = before.Group(add.TargetGroupId);
                    if (descriptor.Singleton && before.Panels.Values.Any(p => p.TypeId == descriptor.Id &&
                        (add.ReplaceSlotId is null || before.Slot(add.ReplaceSlotId).InstanceId != p.Id)))
                        throw new InvalidOperationException(descriptor.Title + " is already open. Activate or swap with it instead.");
                    if (before.Panels.ContainsKey(add.Panel.Id)) throw new InvalidOperationException("New panel identity is already in use.");
                    if (add.ReplaceSlotId is not null)
                    {
                        if (!target.Slots.Any(s => s.Id == add.ReplaceSlotId)) throw new InvalidOperationException("Replacement tab is not in this group.");
                        Capability([before.Slot(add.ReplaceSlotId)], d => d.CanClose, "This panel cannot be replaced.");
                    }
                    string slotId = add.ReplaceSlotId ?? Ids.New();
                    result = before with { Panels = before.Panels.Add(add.Panel.Id, add.Panel) };
                    result = LayoutTree.Map(result, n => n.Id == target.Id && n is TabGroup g ? g with
                    {
                        Slots = add.ReplaceSlotId is null ? g.Slots.Add(new(slotId, add.Panel.Id))
                            : g.Slots.Select(s => s.Id == slotId ? s with { InstanceId = add.Panel.Id } : s).ToImmutableArray(),
                        ActiveSlotId = slotId
                    } : n);
                    label = (add.ReplaceSlotId is null ? "Add " : "Replace with ") + descriptor.Title; break;
                }
                case SwapPanels swap:
                {
                    var first = before.Slot(swap.FirstSlotId); var second = before.Slot(swap.SecondSlotId);
                    if (first == second) throw new InvalidOperationException("Choose another tab to swap with.");
                    result = LayoutTree.Map(before, n => n is TabGroup g ? g with { Slots = g.Slots.Select(s =>
                        s.Id == first.Id ? s with { InstanceId = second.InstanceId } : s.Id == second.Id ? s with { InstanceId = first.InstanceId } : s).ToImmutableArray() } : n);
                    label = "Swap panels"; break;
                }
                case CloseTabs close:
                {
                    var slots = GetSlots(close.SlotIds); Capability(slots, d => d.CanClose, "This panel cannot be closed.");
                    result = Remove(before, close.SlotIds.ToHashSet()); label = "Close tabs"; break;
                }
                case FloatTabs floating:
                {
                    var slots = GetSlots(floating.SlotIds); Capability(slots, d => d.CanFloat, "This panel cannot float.");
                    result = Remove(before, floating.SlotIds.ToHashSet());
                    result = result with { Hosts = result.Hosts.Add(new(Ids.New(), new TabGroup(Ids.New(), slots, slots[0].Id), [], floating.Placement ?? new())) };
                    label = "Move to new window"; break;
                }
                case CollapseGroup collapse:
                {
                    if (collapse.Side == DockSide.Center) throw new InvalidOperationException("Choose an edge.");
                    var group = before.Group(collapse.GroupId);
                    if (group.Slots.IsEmpty) throw new InvalidOperationException("There are no panels to collapse.");
                    Capability(group.Slots, d => d.CanCollapse, "This panel cannot collapse.");
                    var host = before.Hosts.FirstOrDefault(h => LayoutTree.Groups(h.Root).Any(g => g.Id == group.Id))
                        ?? throw new InvalidOperationException("The group is already collapsed.");
                    // A region group leaves an empty placeholder that keeps its region; pinning returns there.
                    // Otherwise pinning returns beside the nearest sibling in the split the group leaves.
                    var placeholder = new TabGroup(Ids.New(), [], RegionKey: group.RegionKey);
                    string? anchor = group.RegionKey is not null ? placeholder.Id
                        : Neighbor(host.Root, group.Id) ?? LayoutTree.Groups(host.Root).FirstOrDefault(g => g.Id != group.Id)?.Id;
                    result = before with { Hosts = before.Hosts.Select(h => h.Id != host.Id ? h : h with
                    {
                        Root = LayoutTree.Map(h.Root, n => n.Id == group.Id ? placeholder : n),
                        Rails = h.Rails.Add(new(collapse.Side, group with { RegionKey = null }, anchor))
                    }).ToImmutableArray() };
                    label = "Collapse group"; break;
                }
                case PinGroup pin:
                {
                    var host = before.Hosts.FirstOrDefault(h => h.Rails.Any(r => r.Group.Id == pin.GroupId))
                        ?? throw new InvalidOperationException("This group is not collapsed.");
                    var rail = host.Rails.First(r => r.Group.Id == pin.GroupId);
                    var target = LayoutTree.Groups(host.Root).FirstOrDefault(g => g.Id == rail.ReturnGroupId) ?? LayoutTree.Groups(host.Root).First();
                    result = before with { Hosts = before.Hosts.Select(h => h.Id == host.Id
                        ? h with { Rails = h.Rails.Remove(rail) } : h).ToImmutableArray() };
                    result = target.Slots.IsEmpty
                        ? LayoutTree.Map(result, n => n.Id == target.Id ? rail.Group with { RegionKey = target.RegionKey ?? rail.Group.RegionKey } : n)
                        : InsertBeside(result, target.Id, rail.Group, rail.Side);
                    label = "Pin group"; break;
                }
                case ResizeSplit resize:
                {
                    var split = before.Hosts.SelectMany(h => LayoutTree.Nodes(h.Root)).OfType<SplitNode>().FirstOrDefault(s => s.Id == resize.SplitId)
                        ?? throw new InvalidOperationException("Splitter no longer exists.");
                    if (resize.Weights.IsDefault || resize.Weights.Length != split.Children.Length)
                        throw new InvalidOperationException("Give one size for each panel beside the splitter.");
                    if (resize.Weights.Any(w => w <= 0 || !double.IsFinite(w))) throw new InvalidOperationException("Invalid splitter size.");
                    result = LayoutTree.Map(before, n => n is SplitNode s && s.Id == resize.SplitId
                        ? s with { Weights = resize.Weights.Select(w => w / resize.Weights.Sum()).ToImmutableArray() } : n);
                    label = "Resize panels"; break;
                }
                case SelectTab select:
                    before.Slot(select.SlotId);
                    result = LayoutTree.Map(before, n => n is TabGroup g && g.Slots.Any(s => s.Id == select.SlotId)
                        ? g with { ActiveSlotId = select.SlotId } : n);
                    label = "Select tab"; break;
                case ApplyLayout apply: result = apply.Layout with { Id = before.Id }; label = apply.Label; break;
                default: throw new InvalidOperationException("Unsupported docking operation.");
            }
            result = LayoutTree.Normalize(result) with { Revision = before.Revision + 1 };
            LayoutTree.Validate(result);
            if (operation is not ApplyLayout) EnforceCapabilities(before, result, catalog);
            foreach (var d in catalog.Values.Where(d => d.Singleton))
                if (result.Panels.Values.Count(p => p.TypeId == d.Id) > 1) throw new InvalidOperationException(d.Title + " can only be open once.");
            return new(new(before.Id, before.Revision, result, label, operation is not SelectTab), null);
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or ArgumentException)
        { return new(null, ex.Message); }
    }
    /// <summary>Every user operation, however it gets there, must respect the close/float/collapse flags.</summary>
    private static void EnforceCapabilities(DockLayout before, DockLayout after, IReadOnlyDictionary<string, PanelDescriptor> catalog)
    {
        var was = Placements(before); var now = Placements(after);
        foreach (var panel in before.Panels.Values)
            if (catalog.GetValueOrDefault(panel.TypeId) is { CanClose: false } d && !after.Panels.ContainsKey(panel.Id))
                throw new InvalidOperationException(d.Title + " cannot be closed.");
        foreach (var panel in after.Panels.Values)
        {
            if (catalog.GetValueOrDefault(panel.TypeId) is not { } d) continue;
            var (floating, rail) = now[panel.Id]; var (wasFloating, wasRail) = was.GetValueOrDefault(panel.Id);
            if (!d.CanFloat && floating && !wasFloating) throw new InvalidOperationException(d.Title + " cannot float.");
            if (!d.CanCollapse && rail && !wasRail) throw new InvalidOperationException(d.Title + " cannot collapse.");
        }
    }
    private static Dictionary<string, (bool Floating, bool Rail)> Placements(DockLayout layout)
    {
        var map = new Dictionary<string, (bool, bool)>();
        foreach (var host in layout.Hosts)
        {
            bool floating = host.Id != layout.MainHostId;
            foreach (var slot in LayoutTree.Groups(host.Root).SelectMany(g => g.Slots)) map[slot.InstanceId] = (floating, false);
            foreach (var slot in host.Rails.SelectMany(r => r.Group.Slots)) map[slot.InstanceId] = (floating, true);
        }
        return map;
    }
    private static string? Neighbor(LayoutNode root, string groupId)
    {
        foreach (var split in LayoutTree.Nodes(root).OfType<SplitNode>())
        {
            int i = split.Children.FindIndex(c => c.Id == groupId);
            if (i >= 0) return LayoutTree.Groups(split.Children[i > 0 ? i - 1 : i + 1]).FirstOrDefault()?.Id;
        }
        return null;
    }
    private static string? Next(TabGroup g, HashSet<string> removed)
    {
        int index = g.Slots.FindIndex(s => s.Id == g.ActiveSlotId);
        return g.Slots.Skip(Math.Max(0, index)).FirstOrDefault(s => !removed.Contains(s.Id))?.Id
            ?? g.Slots.LastOrDefault(s => !removed.Contains(s.Id))?.Id;
    }
    private static DockLayout InsertBeside(DockLayout layout, string target, TabGroup inserted, DockSide side)
    {
        bool first = side is DockSide.Left or DockSide.Top;
        var axis = side is DockSide.Left or DockSide.Right ? SplitAxis.Horizontal : SplitAxis.Vertical;
        return layout with { Hosts = layout.Hosts.Select(h => h with { Root = LayoutTree.Map(h.Root, n => n.Id != target ? n
            : new SplitNode(Ids.New(), axis, first ? [inserted, n] : [n, inserted], [0.5, 0.5])) }).ToImmutableArray() };
    }
    private static int FindIndex<T>(this ImmutableArray<T> items, Func<T, bool> predicate)
    {
        for (int i = 0; i < items.Length; i++) if (predicate(items[i])) return i;
        return -1;
    }
}
