using System.Collections.Immutable;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Pangaea.Dock.Core;
using Pangaea.Dock.Runtime;

namespace Pangaea.Dock.Avalonia;

/// <summary>
/// Standalone docking surface. Owns presentation/floating windows, not the supplied workspace or registry.
/// Construct on the UI thread; dispose before disposing the workspace. Include your normal Avalonia control theme.
/// </summary>
public sealed partial class DockWorkspaceControl : Grid, IDisposable
{
    private readonly PanelViewRegistry _viewRegistry;
    private readonly Dictionary<string, Control> _views = [];
    private readonly Dictionary<string, DockSurface> _surfaces = [];
    private readonly Dictionary<string, Window> _windows = [];
    private readonly HashSet<string> _closing = [];
    private bool _refreshing, _disposed;
    private readonly DockSurface _main;
    public DockWorkspace Workspace { get; }
    public IReadOnlyDictionary<string, DockSurface> Surfaces => _surfaces;
    public string? LastMessage { get; private set; }
    public event Action<string>? StatusChanged;
    public Func<Window>? FloatingWindowFactory { get; set; }
    public Func<ImmutableArray<string>, Task<bool>>? CloseRequested { get; set; }
    public Func<PanelState, string>? PanelTitle { get; set; }
    public Action<PanelState, Control, TextBlock>? ConfigureTab { get; set; }
    public Func<TabGroup, Control?>? EmptyGroupContent { get; set; }
    public Action<string>? PanelActivated { get; set; }
    public bool AllowPanelCreation { get; set; } = true;
    public bool AllowLayoutHistory { get; set; } = true;
    public bool ShowCloseButtons { get; set; }
    public Action<DockSurface>? ConfigureSurface { get; set; }

    public DockWorkspaceControl(DockWorkspace workspace, PanelViewRegistry viewRegistry)
    {
        Workspace = workspace; _viewRegistry = viewRegistry; Styles.Add(new DockTheme());
        ClipToBounds = true;
        _main = new DockSurface(this, workspace.Layout.MainHostId); _surfaces.Add(_main.HostId, _main); Children.Add(_main);
        Workspace.Changed += OnWorkspaceChanged; Workspace.Diagnostic += Report;
        Workspace.PreparePresentation += Prepare;
        AttachedToVisualTree += (_, _) => Refresh();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }
    private void OnWorkspaceChanged(WorkspaceChange change)
    {
        if (change.Description == "Window placement") return;
        Refresh(); Report(change.Description);
    }
    internal void Report(string message) { LastMessage = message; StatusChanged?.Invoke(message); }
    private void Prepare(DockLayout layout)
    {
        foreach (var group in layout.Groups)
            if (group.ActiveSlot is { } slot) View(layout.Panels[slot.InstanceId]);
    }
    internal Control View(PanelState state)
    {
        if (_views.TryGetValue(state.Id, out var existing)) return existing;
        var view = _viewRegistry.Create(state.TypeId, Workspace.Session(state.Id));
        if (view.Parent is not null) throw new InvalidOperationException("A panel factory must return an unattached control.");
        if (_views.Values.Any(v => ReferenceEquals(v, view))) throw new InvalidOperationException("Each panel instance needs its own control.");
        _views.Add(state.Id, view); return view;
    }
    public void Refresh()
    {
        if (_disposed || _refreshing) return;
        _refreshing = true;
        try
        {
            foreach (var surface in _surfaces.Values) surface.ReleaseContent();
            foreach (var id in _windows.Keys.Where(id => !Workspace.Layout.Hosts.Any(h => h.Id == id)).ToArray())
            {
                _closing.Add(id); _windows[id].Close(); _windows.Remove(id); _surfaces.Remove(id); _closing.Remove(id);
            }
            foreach (var host in Workspace.Layout.Hosts)
            {
                if (!_surfaces.ContainsKey(host.Id)) CreateFloating(host);
                _surfaces[host.Id].Rebuild(host);
            }
            foreach (var id in _views.Keys.Where(id => !Workspace.IsRetained(id)).ToArray())
            {
                if (_views.Remove(id, out var view) && view is IDisposable disposable) disposable.Dispose();
            }
        }
        finally { _refreshing = false; }
    }
    private void CreateFloating(DockHost host)
    {
        var surface = new DockSurface(this, host.Id); surface.Styles.Add(new DockTheme());
        ConfigureSurface?.Invoke(surface);
        var window = FloatingWindowFactory?.Invoke() ?? new Window();
        var p = host.Placement ?? new(); window.Title = "Pangaea Dock · Floating panels";
        window.Width = p.Width; window.Height = p.Height; window.MinWidth = 280; window.MinHeight = 180;
        window.Content = surface; window.RequestedThemeVariant = ActualThemeVariant;
        _surfaces.Add(host.Id, surface); _windows.Add(host.Id, window);
        window.Closing += async (_, e) =>
        {
            if (_disposed || _closing.Contains(host.Id)) return;
            e.Cancel = true;
            var current = Workspace.Layout.Hosts.FirstOrDefault(h => h.Id == host.Id);
            if (current is not null)
                await RunAsync(new CloseTabs(LayoutTree.Groups(current.Root).Concat(current.Rails.Select(r => r.Group))
                    .SelectMany(g => g.Slots).Select(s => s.Id).ToImmutableArray()));
        };
        window.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        if (TopLevel.GetTopLevel(this) is Window owner) window.Show(owner); else window.Show();
        var screen = window.Screens.ScreenFromPoint(new PixelPoint((int)p.X, (int)p.Y)) ?? window.Screens.Primary;
        if (screen is not null)
        {
            var area = screen.WorkingArea; double scale = screen.Scaling;
            window.Width = Math.Min(p.Width, area.Width / scale); window.Height = Math.Min(p.Height, area.Height / scale);
            window.Position = new PixelPoint((int)Math.Clamp(p.X, area.X, Math.Max(area.X, area.Right - window.Width * scale)),
                (int)Math.Clamp(p.Y, area.Y, Math.Max(area.Y, area.Bottom - window.Height * scale)));
        }
        if (p.Maximized) window.WindowState = WindowState.Maximized;
    }
    public void CaptureFloatingPositions()
    {
        foreach (var (id, window) in _windows)
        {
            if (window.WindowState != WindowState.Normal) continue;
            Workspace.UpdatePlacement(id, new(window.Position.X, window.Position.Y, window.Bounds.Width, window.Bounds.Height, window.RenderScaling));
        }
    }
    public void UpdateFloatingThemes()
    {
        foreach (var window in _windows.Values) window.RequestedThemeVariant = ActualThemeVariant;
    }
    public async Task<bool> RunAsync(DockOperation operation)
    {
        if (operation is CloseTabs close && CloseRequested is not null) return await CloseRequested(close.SlotIds);
        var evaluated = Workspace.Evaluate(operation);
        if (evaluated.Plan is null) { Report(evaluated.Error!); return false; }
        if (!Fits(evaluated.Plan, out var reason)) { Report(reason!); return false; }
        bool success = await Workspace.CommitAsync(evaluated.Plan);
        if (!success) Refresh(); // Release any views prepared for an aborted transaction.
        return success;
    }
    internal bool Fits(OperationPlan plan, out string? reason)
    {
        reason = null;
        foreach (var host in plan.Candidate.Hosts)
        {
            if (!_surfaces.TryGetValue(host.Id, out var surface)) continue;
            var minimum = DockGeometry.Minimum(host.Root, plan.Candidate, Workspace.Registry.Descriptors);
            var oldHost = Workspace.Layout.Hosts.FirstOrDefault(h => h.Id == host.Id);
            var oldMin = oldHost is null ? new DockSize(0, 0) : DockGeometry.Minimum(oldHost.Root, Workspace.Layout, Workspace.Registry.Descriptors);
            if ((minimum.Width > surface.ContentBounds.Width + 1 && minimum.Width > oldMin.Width + 1) ||
                (minimum.Height > surface.ContentBounds.Height + 1 && minimum.Height > oldMin.Height + 1))
            { reason = "There isn't enough room here. Resize the window or choose another destination."; return false; }
            if ((plan.Description.StartsWith("Replace with ", StringComparison.Ordinal) || plan.Description == "Swap panels") && surface.Geometry is { } geometry)
            {
                foreach (var group in LayoutTree.Groups(host.Root))
                {
                    if (!geometry.Groups.TryGetValue(group.Id, out var existing)) continue;
                    var required = DockGeometry.Minimum(group, plan.Candidate, Workspace.Registry.Descriptors);
                    if (required.Width > existing.Width + 1 || required.Height > existing.Height + 1)
                    { reason = "That panel needs more space. Resize this group first; replacement keeps its position and size."; return false; }
                }
            }
        }
        return true;
    }
    internal string Title(DockSlot slot) => PanelTitle?.Invoke(Workspace.Layout.Panels[slot.InstanceId]) ?? Workspace.Registry.Descriptors.GetValueOrDefault(Workspace.Layout.Panels[slot.InstanceId].TypeId)?.Title
        ?? Workspace.Layout.Panels[slot.InstanceId].TypeId;
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _drag is not null) { CancelDrag(); e.Handled = true; }
        // Layout undo deliberately does not intercept document/text Ctrl+Z.
        if (AllowLayoutHistory && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Alt) && e.Key is Key.Z or Key.Y)
        {
            if (e.Key == Key.Z) _ = Workspace.UndoAsync(); else _ = Workspace.RedoAsync(); e.Handled = true;
        }
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; CancelDrag();
        Workspace.Changed -= OnWorkspaceChanged; Workspace.Diagnostic -= Report; Workspace.PreparePresentation -= Prepare;
        foreach (var surface in _surfaces.Values) surface.ReleaseContent();
        foreach (var window in _windows.Values.ToArray()) window.Close();
        foreach (var view in _views.Values.OfType<IDisposable>()) view.Dispose();
        _windows.Clear(); _surfaces.Clear(); _views.Clear();
    }
}

/// <summary>A host surface, exposed for geometry/interaction tests and custom host integration.</summary>
public sealed class DockSurface : Grid
{
    private readonly DockWorkspaceControl _owner;
    private readonly Canvas _canvas = new(), _overlay = new() { IsHitTestVisible = false };
    private readonly ScrollViewer _scroll;
    private readonly Dictionary<string, DockGroupView> _groups = [];
    private readonly List<Control> _rails = [];
    private DockGroupView? _expanded;
    private string? _expandedId;
    private DockHost? _host;
    public string HostId { get; }
    public DockRect ContentBounds { get; private set; }
    public LayoutGeometry? Geometry { get; private set; }
    public IReadOnlyDictionary<string, DockGroupView> GroupViews => _groups;
    internal DockSurface(DockWorkspaceControl owner, string hostId)
    {
        _owner = owner; HostId = hostId; ClipToBounds = true; Focusable = true;
        DockTheme.Resource(this, BackgroundProperty, "Background");
        _scroll = new ScrollViewer { Content = _canvas, HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        Children.Add(_scroll); Children.Add(_overlay);
        SizeChanged += (_, _) => { _owner.InvalidatePreview(); ArrangeContent(); };
        AddHandler(PointerMovedEvent, (_, e) => _owner.DragMoved(this, e), RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, (_, e) => _owner.DragReleased(e), RoutingStrategies.Tunnel);
        PointerCaptureLost += (_, _) => _owner.CaptureLost();
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (_expanded is not null && e.Source is Visual visual && !IsInside(visual, _expanded) && !_rails.Any(r => IsInside(visual, r))) CloseOverlay();
        }, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, (_, e) => { if (e.Key == Key.Escape && _expanded is not null) { CloseOverlay(); Focus(); e.Handled = true; } });
        LostFocus += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (_expanded is null) return;
            var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
            if (focused is Visual v && TopLevel.GetTopLevel(v) == TopLevel.GetTopLevel(this) && !IsInside(v, _expanded) && !_rails.Any(r => IsInside(v, r))) CloseOverlay();
        });
    }
    private static bool IsInside(Visual child, Visual parent)
    {
        for (Visual? v = child; v is not null; v = v.GetVisualParent()) if (ReferenceEquals(v, parent)) return true;
        return false;
    }
    internal void ReleaseContent()
    {
        foreach (var group in _groups.Values) group.ReleaseContent(); _expanded?.ReleaseContent();
    }
    internal void Rebuild(DockHost host)
    {
        _host = host; _canvas.Children.Clear(); _groups.Clear();
        foreach (var old in _rails) Children.Remove(old); _rails.Clear();
        if (_expanded is not null) { Children.Remove(_expanded); _expanded = null; }
        foreach (var group in LayoutTree.Groups(host.Root))
        {
            var view = new DockGroupView(_owner, this, group, false); _groups.Add(group.Id, view); _canvas.Children.Add(view);
        }
        foreach (var side in new[] { DockSide.Left, DockSide.Right, DockSide.Top, DockSide.Bottom })
        {
            var items = host.Rails.Where(r => r.Side == side).ToArray(); if (items.Length == 0) continue;
            bool vertical = side is DockSide.Left or DockSide.Right;
            var strip = new StackPanel { Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal, Spacing = 4,
                HorizontalAlignment = side == DockSide.Right ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                VerticalAlignment = side == DockSide.Bottom ? VerticalAlignment.Bottom : VerticalAlignment.Top };
            if (vertical) strip.Width = 30; else strip.Height = 30;
            foreach (var rail in items)
            {
                // Label trays with the panel name: an icon alone doesn't say which panel is collapsed.
                var slot = rail.Group.ActiveSlot!;
                string title = _owner.Title(slot);
                string icon = _owner.Workspace.Registry.Descriptors.GetValueOrDefault(_owner.Workspace.Layout.Panels[slot.InstanceId].TypeId)?.Icon ?? "";
                string label = (icon is "" or "◇" ? "" : icon + "  ") + title + (rail.Group.Slots.Length > 1 ? $"  +{rail.Group.Slots.Length - 1}" : "");
                var button = DockTheme.Button(label, "Open " + title, () => ToggleOverlay(rail.Group.Id));
                button.Height = 30; button.MaxWidth = 220; button.Padding = new Thickness(10, 0);
                // Side trays read along the edge, as in IDE tool-window bars.
                strip.Children.Add(vertical
                    ? new LayoutTransformControl { Child = button, LayoutTransform = new RotateTransform(side == DockSide.Left ? -90 : 90) }
                    : button);
            }
            Children.Add(strip); _rails.Add(strip);
        }
        ArrangeContent();
        if (_expandedId is not null && host.Rails.Any(r => r.Group.Id == _expandedId)) OpenOverlay(_expandedId);
        else _expandedId = null;
    }
    internal DockRect CanvasBounds => new(0, 0, _canvas.Width, _canvas.Height);
    internal void ArrangeContent(DockLayout? preview = null)
    {
        if (_host is null) return;
        var layout = preview ?? _owner.Workspace.Layout;
        var host = layout.Hosts.FirstOrDefault(h => h.Id == HostId); if (host is null) return;
        double left = host.Rails.Any(r => r.Side == DockSide.Left) ? 30 : 0, right = host.Rails.Any(r => r.Side == DockSide.Right) ? 30 : 0;
        double top = host.Rails.Any(r => r.Side == DockSide.Top) ? 30 : 0, bottom = host.Rails.Any(r => r.Side == DockSide.Bottom) ? 30 : 0;
        ContentBounds = new(left, top, Math.Max(0, Bounds.Width - left - right), Math.Max(0, Bounds.Height - top - bottom));
        _scroll.Margin = new Thickness(left, top, right, bottom);
        var minimum = DockGeometry.Minimum(host.Root, layout, _owner.Workspace.Registry.Descriptors);
        _canvas.Width = Math.Max(ContentBounds.Width, minimum.Width); _canvas.Height = Math.Max(ContentBounds.Height, minimum.Height);
        Geometry = DockGeometry.Arrange(host.Root, CanvasBounds, layout, _owner.Workspace.Registry.Descriptors);
        foreach (var (id, rect) in Geometry.Groups)
            if (_groups.TryGetValue(id, out var view)) Place(view, rect);
        foreach (var old in _canvas.Children.OfType<Border>().Where(b => b.Tag is SplitterGeometry).ToArray()) _canvas.Children.Remove(old);
        foreach (var splitter in Geometry.Splitters)
        {
            var handle = new Border { Background = Brushes.Transparent, Tag = splitter,
                Cursor = new Cursor(splitter.Axis == SplitAxis.Horizontal ? StandardCursorType.SizeWestEast : StandardCursorType.SizeNorthSouth) };
            handle.PointerPressed += (_, e) => _owner.BeginResize(this, splitter, e);
            _canvas.Children.Add(handle); Place(handle, splitter.Bounds);
        }
    }
    internal Point ToCanvas(Point point) => new(point.X - ContentBounds.X + _scroll.Offset.X, point.Y - ContentBounds.Y + _scroll.Offset.Y);
    internal Point FromCanvas(Point point) => new(point.X + ContentBounds.X - _scroll.Offset.X, point.Y + ContentBounds.Y - _scroll.Offset.Y);
    internal void Preview(DockRect? rectangle, string label = "")
    {
        _overlay.Children.Clear(); if (rectangle is not { } r) return;
        var origin = FromCanvas(new Point(r.X, r.Y));
        var border = new Border { BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(3) };
        DockTheme.Resource(border, Border.BackgroundProperty, "Preview"); DockTheme.Resource(border, Border.BorderBrushProperty, "Accent");
        var text = DockTheme.Text(label, 12); text.HorizontalAlignment = HorizontalAlignment.Center; text.VerticalAlignment = VerticalAlignment.Center;
        border.Child = text; _overlay.Children.Add(border); Place(border, new(origin.X, origin.Y, r.Width, r.Height));
    }
    private void ToggleOverlay(string id) { if (_expandedId == id) CloseOverlay(); else OpenOverlay(id); }
    private void OpenOverlay(string id)
    {
        CloseOverlay(); _expandedId = id;
        var rail = _host!.Rails.First(r => r.Group.Id == id); _expanded = new DockGroupView(_owner, this, rail.Group, true);
        _expanded.HorizontalAlignment = rail.Side == DockSide.Right ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        _expanded.VerticalAlignment = rail.Side == DockSide.Bottom ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        bool vertical = rail.Side is DockSide.Left or DockSide.Right;
        _expanded.Width = vertical ? Math.Min(rail.Extent, ContentBounds.Width) : ContentBounds.Width;
        _expanded.Height = vertical ? ContentBounds.Height : Math.Min(rail.Extent, ContentBounds.Height);
        _expanded.Margin = new Thickness(rail.Side == DockSide.Left ? 30 : 0, rail.Side == DockSide.Top ? 30 : 0,
            rail.Side == DockSide.Right ? 30 : 0, rail.Side == DockSide.Bottom ? 30 : 0);
        Children.Add(_expanded); _expanded.Focus();
    }
    private void CloseOverlay()
    {
        _expanded?.ReleaseContent(); if (_expanded is not null) Children.Remove(_expanded); _expanded = null; _expandedId = null;
    }
    private static void Place(Control control, DockRect rect)
    { Canvas.SetLeft(control, rect.X); Canvas.SetTop(control, rect.Y); control.Width = rect.Width; control.Height = rect.Height; }
}
