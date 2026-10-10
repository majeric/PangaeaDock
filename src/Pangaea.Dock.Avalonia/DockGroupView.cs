using System.Collections.Immutable;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Pangaea.Dock.Core;

namespace Pangaea.Dock.Avalonia;

public sealed class DockGroupView : Border
{
    private readonly ContentControl _content = new();
    public string GroupId { get; }
    public IReadOnlyDictionary<string, Control> TabHeaders => _headers;
    private readonly Dictionary<string, Control> _headers = [];
    internal DockGroupView(DockWorkspaceControl owner, DockSurface surface, TabGroup group, bool rail)
    {
        GroupId = group.Id; Focusable = true; ClipToBounds = true;
        DockTheme.Resource(this, BackgroundProperty, "Surface");
        var grid = new Grid { RowDefinitions = new RowDefinitions("34,*") }; Child = grid;
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("20,*,Auto") };
        DockTheme.Resource(header, Panel.BackgroundProperty, "Background");
        var grip = DockTheme.Text("⠿", 16, "Muted"); grip.HorizontalAlignment = HorizontalAlignment.Center;
        ToolTip.SetTip(grip, "Drag to move this group");
        grip.PointerPressed += (_, e) => owner.BeginDrag(surface, group.Slots.Select(s => s.Id).ToImmutableArray(), e);
        header.Children.Add(grip);
        var tabs = new StackPanel { Orientation = Orientation.Horizontal };
        var scroll = new ScrollViewer { Content = tabs, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetColumn(scroll, 1); header.Children.Add(scroll);
        foreach (var slot in group.Slots)
        {
            var descriptor = owner.Workspace.Registry.Descriptors.GetValueOrDefault(owner.Workspace.Layout.Panels[slot.InstanceId].TypeId);
            bool active = group.ActiveSlot?.Id == slot.Id;
            var tab = new Border { Padding = new Thickness(10, 0), BorderThickness = new Thickness(0, 0, 0, 2),
                Focusable = true, MaxWidth = 210, MinWidth = 70, Height = 34 };
            DockTheme.Resource(tab, BackgroundProperty, active ? "Surface" : "Background");
            DockTheme.Resource(tab, BorderBrushProperty, active ? "Accent" : "Background");
            var title = DockTheme.Text((descriptor?.Icon ?? "◇") + "  " + owner.Title(slot), 12, active ? "Text" : "Muted");
            title.TextTrimming = TextTrimming.CharacterEllipsis; tab.Child = title;
            AutomationProperties.SetName(tab, owner.Title(slot) + (active ? ", selected" : ""));
            ToolTip.SetTip(tab, owner.Title(slot) + " · Drag to dock · Right-click for panel actions");
            tab.ContextMenu = owner.TabMenu(surface, group, slot);
            owner.ConfigureTab?.Invoke(owner.Workspace.Layout.Panels[slot.InstanceId], tab, title);
            if (owner.ShowCloseButtons && descriptor?.CanClose == true)
            {
                tab.Child = null;
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
                row.Children.Add(title);
                var close = DockTheme.Button("×", "Close " + owner.Title(slot), () => _ = owner.RunAsync(new CloseTabs([slot.Id])));
                close.PointerPressed += (_, e) => e.Handled = true;
                row.Children.Add(close); tab.Child = row;
            }
            tab.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(tab).Properties.IsLeftButtonPressed) return;
                owner.PanelActivated?.Invoke(slot.InstanceId);
                owner.BeginDrag(surface, [slot.Id], e); e.Handled = true;
            };
            tab.KeyDown += async (_, e) =>
            {
                if (e.Key is Key.Enter or Key.Space) { await owner.Workspace.ExecuteAsync(new SelectTab(slot.Id)); owner.PanelActivated?.Invoke(slot.InstanceId); e.Handled = true; }
                if (e.Key is Key.Left or Key.Right)
                {
                    int index = group.Slots.IndexOf(slot), next = (index + (e.Key == Key.Left ? -1 : 1) + group.Slots.Length) % group.Slots.Length;
                    await owner.Workspace.ExecuteAsync(new SelectTab(group.Slots[next].Id));
                    owner.PanelActivated?.Invoke(group.Slots[next].InstanceId);
                    // Selection rebuilds the group; focus the new header once it is attached.
                    string nextId = group.Slots[next].Id; e.Handled = true;
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (surface.GroupViews.TryGetValue(group.Id, out var current) && current.TabHeaders.TryGetValue(nextId, out var header)) header.Focus();
                    }, DispatcherPriority.Loaded);
                }
                if (e.Key == Key.F10 && e.KeyModifiers.HasFlag(KeyModifiers.Shift)) { tab.ContextMenu.Open(tab); e.Handled = true; }
            };
            tabs.Children.Add(tab); _headers.Add(slot.Id, tab);
        }
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (rail) actions.Children.Add(DockTheme.Button("↗", "Pin group into the workspace", () => _ = owner.RunAsync(new PinGroup(group.Id))));
        var more = DockTheme.Button("⋯", "Group actions", () => { });
        more.ContextMenu = owner.GroupMenu(surface, group, rail);
        more.Click += (_, _) => more.ContextMenu.Open(more); actions.Children.Add(more);
        Grid.SetColumn(actions, 2); header.Children.Add(actions); grid.Children.Add(header);
        Grid.SetRow(_content, 1); grid.Children.Add(_content);
        if (group.ActiveSlot is { } selected)
        {
            try { _content.Content = owner.View(owner.Workspace.Layout.Panels[selected.InstanceId]); }
            catch (Exception e) { _content.Content = new TextBlock { Text = "Panel failed to load: " + e.Message, Margin = new Thickness(16), TextWrapping = TextWrapping.Wrap }; }
            _content.AddHandler(PointerPressedEvent, (_, _) => owner.PanelActivated?.Invoke(selected.InstanceId), global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
            _content.GotFocus += (_, _) => owner.PanelActivated?.Invoke(selected.InstanceId);
        }
        else
        {
            var empty = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            empty.Children.Add(DockTheme.Text("A little room to explore", 17));
            empty.Children.Add(DockTheme.Text("Drop a tab here, or add a panel.", 12, "Muted"));
            if (owner.AllowPanelCreation) empty.Children.Add(DockTheme.Button("+ Add panel", "Add a panel", () => _ = owner.ShowPickerAsync(group.Id)));
            _content.Content = owner.EmptyGroupContent?.Invoke(group) ?? empty;
        }
    }
    internal void ReleaseContent() => _content.Content = null;
}
