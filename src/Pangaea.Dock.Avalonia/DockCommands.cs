using System.Collections.Immutable;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Pangaea.Dock.Core;

namespace Pangaea.Dock.Avalonia;

public sealed partial class DockWorkspaceControl
{
    private static MenuItem Item(string title, Action action)
    {
        var item = new MenuItem { Header = title }; item.Click += (_, _) => action(); return item;
    }
    internal ContextMenu TabMenu(DockSurface surface, TabGroup group, DockSlot slot)
    {
        var descriptor = Workspace.Registry.Descriptors.GetValueOrDefault(Workspace.Layout.Panels[slot.InstanceId].TypeId);
        var duplicate = Item("Duplicate", () => _ = Workspace.DuplicateAsync(slot.Id));
        duplicate.IsEnabled = descriptor is { CanDuplicate: true, Singleton: false };
        var swap = new MenuItem { Header = "Swap with" };
        foreach (var other in Workspace.Layout.Slots.Where(s => s.Id != slot.Id))
            swap.Items.Add(Item(Title(other), () => _ = RunAsync(new SwapPanels(slot.Id, other.Id))));
        var menu = new ContextMenu();
        if (AllowPanelCreation) { menu.Items.Add(Item("Replace with…", () => _ = ShowPickerAsync(group.Id, slot.Id))); menu.Items.Add(duplicate); }
        menu.Items.Add(swap); menu.Items.Add(new Separator());
        menu.Items.Add(MoveMenu([slot.Id], group.Id));
        menu.Items.Add(Item("Move to new window", () => _ = RunAsync(new FloatTabs([slot.Id]))));
        menu.Items.Add(new Separator());
        var close = Item("Close", () => _ = RunAsync(new CloseTabs([slot.Id]))); close.IsEnabled = descriptor?.CanClose != false; menu.Items.Add(close);
        return menu;
    }
    internal ContextMenu GroupMenu(DockSurface surface, TabGroup group, bool rail)
    {
        var menu = new ContextMenu(); var slots = group.Slots.Select(s => s.Id).ToImmutableArray();
        if (AllowPanelCreation) menu.Items.Add(Item("Add panel…", () => _ = ShowPickerAsync(group.Id)));
        if (AllowLayoutHistory)
        {
            var undo = Item("Undo layout · Ctrl+Alt+Z", () => _ = Workspace.UndoAsync()); undo.IsEnabled = Workspace.CanUndo;
            var redo = Item("Redo layout · Ctrl+Alt+Y", () => _ = Workspace.RedoAsync()); redo.IsEnabled = Workspace.CanRedo;
            menu.Items.Add(undo); menu.Items.Add(redo);
        }
        if (rail) menu.Items.Add(Item("Pin group", () => _ = RunAsync(new PinGroup(group.Id))));
        else
        {
            var collapse = new MenuItem { Header = "Collapse to sidebar" };
            foreach (var side in new[] { DockSide.Left, DockSide.Right, DockSide.Top, DockSide.Bottom })
                collapse.Items.Add(Item(side.ToString(), () => _ = RunAsync(new CollapseGroup(group.Id, side))));
            menu.Items.Add(collapse);
        }
        if (slots.Length > 0)
        {
            menu.Items.Add(MoveMenu(slots, group.Id));
            menu.Items.Add(Item("Float group", () => _ = RunAsync(new FloatTabs(slots))));
            var close = Item("Close group", () => _ = RunAsync(new CloseTabs(slots)));
            close.IsEnabled = slots.All(id => Workspace.Registry.Descriptors.GetValueOrDefault(Workspace.Layout.Panels[Workspace.Layout.Slot(id).InstanceId].TypeId)?.CanClose != false);
            menu.Items.Add(close);
        }
        if (!rail)
        {
            var resize = new MenuItem { Header = "Resize neighboring split" };
            // Menus are built before the new layout is arranged. Previous geometry may
            // reference splits removed by this move, so derive actions from the live tree.
            var host = Workspace.Layout.Hosts.First(h => h.Id == surface.HostId);
            foreach (var split in LayoutTree.Nodes(host.Root).OfType<SplitNode>())
            {
                if (!LayoutTree.Groups(split).Any(g => g.Id == group.Id)) continue;
                foreach (var index in Enumerable.Range(0, split.Children.Length - 1))
                foreach (double delta in new[] { -0.04, 0.04 })
                    resize.Items.Add(Item($"{split.Axis}: {(delta < 0 ? "decrease" : "increase")} first section", () =>
                    {
                        var weights = split.Weights.ToArray();
                        double change = Math.Clamp(delta, -weights[index] + 0.01, weights[index + 1] - 0.01);
                        weights[index] += change; weights[index + 1] -= change;
                        _ = RunAsync(new ResizeSplit(split.Id, [.. weights]));
                    }));
            }
            if (resize.Items.Count > 0) menu.Items.Add(resize);
        }
        return menu;
    }
    private MenuItem MoveMenu(ImmutableArray<string> slots, string sourceGroup)
    {
        var move = new MenuItem { Header = "Move / split" };
        foreach (var group in Workspace.Layout.Groups)
        {
            string name = group.ActiveSlot is { } slot ? Title(slot) : "Empty workspace";
            var target = new MenuItem { Header = name };
            foreach (var side in Enum.GetValues<DockSide>())
            {
                var operation = new MoveTabs(slots, group.Id, side);
                var item = Item(side == DockSide.Center ? "Join tabs" : "Split " + side.ToString().ToLowerInvariant(), () => _ = RunAsync(operation));
                item.IsEnabled = Workspace.Evaluate(operation).Success;
                target.Items.Add(item);
            }
            move.Items.Add(target);
        }
        return move;
    }
    public async Task ShowPickerAsync(string? groupId = null, string? replaceSlot = null)
    {
        groupId ??= (Workspace.Layout.Groups.FirstOrDefault(g => g.RegionKey == "primary") ?? Workspace.Layout.Groups.First()).Id;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        var dialog = new Window { Title = replaceSlot is null ? "Add panel" : "Replace panel", Width = 450, Height = 530,
            CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, RequestedThemeVariant = ActualThemeVariant };
        dialog.Styles.Add(new DockTheme());
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), Margin = new Thickness(22) };
        DockTheme.Resource(dialog, Window.BackgroundProperty, "Surface");
        var title = DockTheme.Text(replaceSlot is null ? "Make room for an idea" : "Same place. A different perspective.", 18);
        title.Margin = new Thickness(0, 0, 0, 16); root.Children.Add(title);
        var search = new TextBox { Watermark = "Search panels…", Margin = new Thickness(0, 0, 0, 12) }; Grid.SetRow(search, 1); root.Children.Add(search);
        var results = new ListBox { Background = global::Avalonia.Media.Brushes.Transparent, BorderThickness = new Thickness(0) };
        Grid.SetRow(results, 2); root.Children.Add(results);
        var footer = DockTheme.Text("Enter to choose · Esc to cancel", 11, "Muted"); footer.Margin = new Thickness(0, 12, 0, 0); Grid.SetRow(footer, 3); root.Children.Add(footer);
        void Filter()
        {
            var query = search.Text ?? "";
            results.Items.Clear();
            foreach (var d in Workspace.Registry.Descriptors.Values.OrderBy(d => d.Category).ThenBy(d => d.Title))
            {
                if (!(d.Title + " " + d.Category + " " + d.Keywords).Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
                bool open = d.Singleton && Workspace.Layout.Panels.Values.Any(p => p.TypeId == d.Id);
                var item = new ListBoxItem { Tag = d.Id, Padding = new Thickness(12), Content = new TextBlock
                    { Text = $"{d.Icon}   {d.Title}\n      {d.Category}" + (open ? "  · Already open — activate" : ""), FontSize = 13 } };
                results.Items.Add(item);
            }
            if (results.Items.Count > 0) results.SelectedIndex = 0;
        }
        async Task Choose()
        {
            if (results.SelectedItem is not ListBoxItem { Tag: string type }) return;
            var descriptor = Workspace.Registry.Descriptors[type];
            var existing = descriptor.Singleton ? Workspace.Layout.Panels.Values.FirstOrDefault(p => p.TypeId == type) : null;
            if (existing is not null)
            {
                var existingSlot = Workspace.Layout.Slots.First(s => s.InstanceId == existing.Id);
                await Workspace.ExecuteAsync(new SelectTab(existingSlot.Id)); dialog.Close(); return;
            }
            var result = await RunAsync(new AddPanel(new PanelState(Ids.New(), type), groupId, replaceSlot));
            if (result) dialog.Close(); else footer.Text = LastMessage;
        }
        search.TextChanged += (_, _) => Filter(); results.DoubleTapped += async (_, _) => await Choose();
        dialog.KeyDown += async (_, e) => { if (e.Key == Key.Escape) dialog.Close(); if (e.Key == Key.Enter) await Choose();
            if (e.Key == Key.Down && search.IsFocused) { results.Focus(); e.Handled = true; } };
        dialog.Content = root; Filter(); dialog.Opened += (_, _) => search.Focus(); await dialog.ShowDialog(owner);
    }
}
