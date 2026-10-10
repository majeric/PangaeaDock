using System.Collections.Immutable;
using Avalonia;
using Avalonia.Input;
using Pangaea.Dock.Core;

namespace Pangaea.Dock.Avalonia;

public sealed partial class DockWorkspaceControl
{
    private sealed class DragSession(DockSurface source, IPointer pointer, PixelPoint start, ImmutableArray<string> slots,
        long revision, SplitterGeometry? splitter = null)
    {
        public DockSurface Source = source;
        public IPointer Pointer = pointer;
        public PixelPoint Start = start;
        public ImmutableArray<string> Slots = slots;
        public long Revision = revision;
        public SplitterGeometry? Splitter = splitter;
        public bool Started;
        public OperationPlan? Plan;
        public DockSurface? Target;
        public DockRect? Preview;
        public long Generation;
    }
    private DragSession? _drag;
    private long _geometryGeneration;
    public OperationPlan? CurrentPreviewPlan => _drag?.Plan;
    public DockRect? CurrentPreviewRectangle => _drag?.Preview;
    internal void InvalidatePreview() { _geometryGeneration++; if (_drag is not null) _drag.Plan = null; }
    internal void BeginDrag(DockSurface source, ImmutableArray<string> slots, PointerPressedEventArgs e)
    {
        if (slots.IsEmpty || !e.GetCurrentPoint(source).Properties.IsLeftButtonPressed || Workspace.IsLayoutLocked) return;
        var start = source.PointToScreen(e.GetPosition(source));
        CancelDrag();
        if (slots.Length == 1) _ = Workspace.ExecuteAsync(new SelectTab(slots[0]));
        _drag = new(source, e.Pointer, start, slots, Workspace.Layout.Revision);
        source.Focus(); e.Pointer.Capture(source); e.Handled = true;
    }
    internal void BeginResize(DockSurface source, SplitterGeometry splitter, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(source).Properties.IsLeftButtonPressed || Workspace.IsLayoutLocked) return;
        CancelDrag(); _drag = new(source, e.Pointer, source.PointToScreen(e.GetPosition(source)), [], Workspace.Layout.Revision, splitter) { Started = true };
        source.Focus(); e.Pointer.Capture(source); e.Handled = true;
    }
    internal void DragMoved(DockSurface source, PointerEventArgs e)
    {
        var drag = _drag; if (drag is null || drag.Source != source) return;
        var screen = source.PointToScreen(e.GetPosition(source));
        if (!drag.Started && Math.Abs(screen.X - drag.Start.X) + Math.Abs(screen.Y - drag.Start.Y) < 8) return;
        drag.Started = true;
        if (drag.Revision != Workspace.Layout.Revision) { CancelDrag(); Report("Layout changed; please drag again."); return; }
        if (drag.Splitter is { } splitter) { ResizePreview(drag, screen, splitter); return; }
        foreach (var s in _surfaces.Values) s.Preview(null);
        drag.Plan = null; drag.Preview = null;
        foreach (var target in _surfaces.Values.Reverse())
        {
            Point local = target.PointToClient(screen);
            if (!new Rect(target.Bounds.Size).Contains(local) || target.Geometry is null) continue;
            Point canvas = target.ToCanvas(local);
            var hit = target.Geometry.Groups.FirstOrDefault(p => p.Value.Contains(canvas.X, canvas.Y));
            if (hit.Key is null) continue;
            var rect = hit.Value; var group = Workspace.Layout.Group(hit.Key);
            DockSide side = DockSide.Center; int index = -1;
            if (canvas.Y < rect.Y + DockGeometry.TabHeight)
            {
                index = group.Slots.Length;
                if (target.GroupViews.TryGetValue(group.Id, out var view))
                {
                    for (int i = 0; i < group.Slots.Length; i++)
                    {
                        var tab = view.TabHeaders[group.Slots[i].Id];
                        var tabPoint = tab.TranslatePoint(new Point(tab.Bounds.Width / 2, 0), target);
                        if (tabPoint.HasValue && local.X < tabPoint.Value.X) { index = i; break; }
                    }
                }
            }
            else
            {
                double x = canvas.X - rect.X, y = canvas.Y - rect.Y;
                var distances = new[] { (DockSide.Left, x), (DockSide.Right, rect.Width - x), (DockSide.Top, y - DockGeometry.TabHeight), (DockSide.Bottom, rect.Height - y) };
                var nearest = distances.MinBy(d => d.Item2);
                double band = Math.Min(48, Math.Min(rect.Width, rect.Height) * 0.25);
                if (nearest.Item2 < band) side = nearest.Item1;
            }
            var result = Workspace.Evaluate(new MoveTabs(drag.Slots, group.Id, side, index));
            if (result.Plan is not { } plan || !Fits(plan, out _)) { Report(result.Error ?? "Not enough room for this split."); return; }
            var host = plan.Candidate.Hosts.FirstOrDefault(h => h.Id == target.HostId); if (host is null) return;
            var candidateGroup = LayoutTree.Groups(host.Root).FirstOrDefault(g => g.Slots.Any(s => s.Id == drag.Slots[0]));
            if (candidateGroup is null) return;
            var geometry = DockGeometry.Arrange(host.Root, target.CanvasBounds, plan.Candidate, Workspace.Registry.Descriptors);
            var preview = geometry.Groups[candidateGroup.Id];
            drag.Plan = plan; drag.Target = target; drag.Preview = preview; drag.Generation = _geometryGeneration;
            target.Preview(preview, side == DockSide.Center ? "Join tabs" : "Split " + side.ToString().ToLowerInvariant());
            Report("Release to " + (side == DockSide.Center ? "join tabs" : "split " + side.ToString().ToLowerInvariant()) + " · Esc cancels"); return;
        }
        Report("Drop onto a workspace · Use Move to New Window to float");
    }
    private void ResizePreview(DragSession drag, PixelPoint screen, SplitterGeometry splitter)
    {
        var split = Workspace.Layout.Hosts.SelectMany(h => LayoutTree.Nodes(h.Root)).OfType<SplitNode>().First(s => s.Id == splitter.SplitId);
        bool horizontal = split.Axis == SplitAxis.Horizontal;
        double total = (horizontal ? splitter.ParentBounds.Width : splitter.ParentBounds.Height) - DockGeometry.SplitterSize * (split.Children.Length - 1);
        var minima = split.Children.Select(c => DockGeometry.Minimum(c, Workspace.Layout, Workspace.Registry.Descriptors))
            .Select(s => horizontal ? s.Width : s.Height).ToArray();
        var sizes = DockGeometry.Allocate(total, split.Weights.ToArray(), minima);
        double scale = global::Avalonia.Controls.TopLevel.GetTopLevel(drag.Source)?.RenderScaling ?? 1;
        double delta = (horizontal ? screen.X - drag.Start.X : screen.Y - drag.Start.Y) / scale;
        delta = Math.Clamp(delta, minima[splitter.Index] - sizes[splitter.Index], sizes[splitter.Index + 1] - minima[splitter.Index + 1]);
        sizes[splitter.Index] += delta; sizes[splitter.Index + 1] -= delta;
        var result = Workspace.Evaluate(new ResizeSplit(split.Id, sizes.Select(s => s / sizes.Sum()).ToImmutableArray()));
        if (result.Plan is not { } plan) return;
        drag.Plan = plan; drag.Generation = _geometryGeneration; drag.Source.ArrangeContent(plan.Candidate);
        Report("Resize panels · Release to keep · Esc cancels");
    }
    internal async void DragReleased(PointerReleasedEventArgs e)
    {
        var drag = _drag; if (drag is null) return;
        _drag = null; e.Pointer.Capture(null);
        foreach (var surface in _surfaces.Values) { surface.Preview(null); surface.ArrangeContent(); }
        if (drag.Started && drag.Plan is { } plan && drag.Generation == _geometryGeneration)
        {
            bool success = await Workspace.CommitAsync(plan); if (!success) Refresh();
        }
        e.Handled = true;
    }
    internal void CaptureLost() { if (_drag is not null) CancelDrag(); }
    private void CancelDrag()
    {
        var drag = _drag; _drag = null; drag?.Pointer.Capture(null);
        foreach (var surface in _surfaces.Values) { surface.Preview(null); surface.ArrangeContent(); }
    }
}
