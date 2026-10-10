namespace Pangaea.Dock.Core;

public readonly record struct DockSize(double Width, double Height);
public readonly record struct DockRect(double X, double Y, double Width, double Height)
{
    public bool Contains(double x, double y) => x >= X && y >= Y && x < X + Width && y < Y + Height;
}
public sealed record PanelDescriptor(string Id, string Title, string Category = "Tools", string Icon = "◇",
    bool Singleton = false, bool CanDuplicate = true, double MinWidth = 160, double MinHeight = 100,
    string Keywords = "", bool CanClose = true, bool CanFloat = true, bool CanCollapse = true);
public sealed record SplitterGeometry(string SplitId, int Index, SplitAxis Axis, DockRect Bounds, DockRect ParentBounds);
public sealed record LayoutGeometry(IReadOnlyDictionary<string, DockRect> Groups, IReadOnlyList<SplitterGeometry> Splitters);

public static class DockGeometry
{
    public const double TabHeight = 34;
    public const double SplitterSize = 6;
    public static DockSize Minimum(LayoutNode node, DockLayout layout, IReadOnlyDictionary<string, PanelDescriptor> catalog)
    {
        if (node is TabGroup g)
        {
            var descriptors = g.Slots.Select(s => catalog.GetValueOrDefault(layout.Panels[s.InstanceId].TypeId)).Where(d => d is not null).ToArray();
            return new(Math.Max(120, descriptors.Select(d => d!.MinWidth).DefaultIfEmpty(120).Max()),
                TabHeight + Math.Max(60, descriptors.Select(d => d!.MinHeight).DefaultIfEmpty(60).Max()));
        }
        var split = (SplitNode)node;
        var sizes = split.Children.Select(c => Minimum(c, layout, catalog)).ToArray();
        return split.Axis == SplitAxis.Horizontal
            ? new(sizes.Sum(s => s.Width) + SplitterSize * (sizes.Length - 1), sizes.Max(s => s.Height))
            : new(sizes.Max(s => s.Width), sizes.Sum(s => s.Height) + SplitterSize * (sizes.Length - 1));
    }
    public static LayoutGeometry Arrange(LayoutNode root, DockRect bounds, DockLayout layout, IReadOnlyDictionary<string, PanelDescriptor> catalog)
    {
        var groups = new Dictionary<string, DockRect>(); var splitters = new List<SplitterGeometry>();
        void Visit(LayoutNode node, DockRect rect)
        {
            if (node is TabGroup g) { groups[g.Id] = rect; return; }
            var split = (SplitNode)node;
            bool horizontal = split.Axis == SplitAxis.Horizontal;
            double length = (horizontal ? rect.Width : rect.Height) - SplitterSize * (split.Children.Length - 1);
            var minima = split.Children.Select(c => Minimum(c, layout, catalog)).Select(s => horizontal ? s.Width : s.Height).ToArray();
            var sizes = Allocate(length, split.Weights.ToArray(), minima);
            double offset = 0;
            for (int i = 0; i < split.Children.Length; i++)
            {
                var childRect = horizontal ? new DockRect(rect.X + offset, rect.Y, sizes[i], rect.Height)
                    : new DockRect(rect.X, rect.Y + offset, rect.Width, sizes[i]);
                Visit(split.Children[i], childRect); offset += sizes[i];
                if (i < split.Children.Length - 1)
                {
                    splitters.Add(new(split.Id, i, split.Axis, horizontal
                        ? new(rect.X + offset, rect.Y, SplitterSize, rect.Height)
                        : new(rect.X, rect.Y + offset, rect.Width, SplitterSize), rect));
                    offset += SplitterSize;
                }
            }
        }
        Visit(root, bounds); return new(groups, splitters);
    }
    public static double[] Allocate(double total, double[] weights, double[] minima)
    {
        total = Math.Max(total, minima.Sum());
        var result = new double[weights.Length]; var remaining = Enumerable.Range(0, weights.Length).ToHashSet();
        while (remaining.Count > 0)
        {
            var sum = remaining.Sum(i => weights[i]);
            var clamped = remaining.Where(i => total * weights[i] / sum < minima[i]).ToArray();
            if (clamped.Length == 0) { foreach (int i in remaining) result[i] = total * weights[i] / sum; break; }
            foreach (int i in clamped) { result[i] = minima[i]; total -= result[i]; remaining.Remove(i); }
        }
        return result;
    }
}
