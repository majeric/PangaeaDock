using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Pangaea.Dock.Avalonia;
using Pangaea.Dock.Core;
using Pangaea.Dock.Tests.Support;

namespace Pangaea.Dock.Tests.Avalonia;

public class TabContextMenuTests
{
    [AvaloniaFact]
    public void TabMenu_ListsPanelActionsInOrder()
    {
        using var ui = new DockUi();

        var menu = ui.Tab("a").ContextMenu!;

        Assert.Equal(["Replace with…", "Duplicate", "Swap with", "Move / split", "Move to new window", "Close"], menu.Headers());
    }

    [AvaloniaFact]
    public void TabMenu_WithoutPanelCreation_OmitsReplaceAndDuplicate()
    {
        using var ui = new DockUi(configure: c => c.AllowPanelCreation = false);

        Assert.Equal(["Swap with", "Move / split", "Move to new window", "Close"], ui.Tab("a").ContextMenu!.Headers());
    }

    [AvaloniaFact]
    public void TabMenu_DisablesDuplicateForSingletonAndCloseForFixedPanel()
    {
        using var ui = new DockUi(L.Build(L.Group("g", "s", "fx"), types: new Dictionary<string, string> { ["s"] = "single", ["fx"] = "fixed" }));

        Assert.False(ui.Tab("s").ContextMenu!.Item("Duplicate").IsEnabled);
        Assert.True(ui.Tab("s").ContextMenu!.Item("Close").IsEnabled);
        Assert.True(ui.Tab("fx").ContextMenu!.Item("Duplicate").IsEnabled);
        Assert.False(ui.Tab("fx").ContextMenu!.Item("Close").IsEnabled);
    }

    [AvaloniaFact]
    public void TabMenu_Close_ClosesTab()
    {
        using var ui = new DockUi();

        ui.Tab("b").ContextMenu!.Item("Close").Invoke(); ui.Pump();

        Assert.Equal(["a"], ui.Workspace.Layout.Group("left").SlotIds());
    }

    [AvaloniaFact]
    public void TabMenu_Duplicate_AddsCopyToSameGroup()
    {
        using var ui = new DockUi();

        ui.Tab("c").ContextMenu!.Item("Duplicate").Invoke(); ui.Pump();

        Assert.Equal(2, ui.Workspace.Layout.Group("right").Slots.Length);
    }

    [AvaloniaFact]
    public void TabMenu_SwapWith_ListsOtherTabsAndSwapsPanels()
    {
        using var ui = new DockUi(configure: c => c.PanelTitle = p => p.Id);
        var swap = ui.Tab("a").ContextMenu!.Item("Swap with");

        Assert.Equal(["p-b", "p-c"], swap.Headers());
        swap.Item("p-c").Invoke(); ui.Pump();

        Assert.Equal("p-c", ui.Workspace.Layout.Slot("a").InstanceId);
    }

    [AvaloniaFact]
    public void TabMenu_MoveToNewWindow_FloatsTab()
    {
        using var ui = new DockUi();

        ui.Tab("c").ContextMenu!.Item("Move to new window").Invoke(); ui.Pump();

        Assert.Equal(2, ui.Workspace.Layout.Hosts.Length);
        Assert.Equal(2, ui.Control.Surfaces.Count);
    }

    [AvaloniaFact]
    public void MoveMenu_HasEveryDropTargetAndDisablesInvalidOnes()
    {
        using var ui = new DockUi(configure: c => c.PanelTitle = p => p.Id);
        var move = ui.Tab("c").ContextMenu!.Item("Move / split");

        Assert.Equal(["p-a", "p-c"], move.Headers());
        var own = move.Item("p-c");
        Assert.Equal(["Join tabs", "Split left", "Split right", "Split top", "Split bottom"], own.Headers());
        Assert.False(own.Item("Split left").IsEnabled);
        Assert.True(move.Item("p-a", "Split bottom").IsEnabled);
    }

    [AvaloniaFact]
    public void MoveMenu_Split_MovesTabIntoNewGroup()
    {
        using var ui = new DockUi(configure: c => c.PanelTitle = p => p.Id);

        ui.Tab("b").ContextMenu!.Item("Move / split", "p-c", "Split bottom").Invoke(); ui.Pump();

        var group = ui.Workspace.Layout.GroupOf("b");
        Assert.Equal(["b"], group.SlotIds());
        Assert.Equal(3, ui.Main.GroupViews.Count);
    }

    [AvaloniaFact]
    public void MoveMenu_EmptyGroupIsNamedEmptyWorkspace()
    {
        using var ui = new DockUi(L.Build(L.H("h", L.Region("p", "primary"), L.Group("g", "a"))));

        Assert.Contains("Empty workspace", ui.Tab("a").ContextMenu!.Item("Move / split").Headers());
    }
}

public class GroupMenuTests
{
    private static ContextMenu GroupMenu(DockUi ui, string groupId) => ui.GroupView(groupId).ButtonWithTip("Group actions").ContextMenu!;

    [AvaloniaFact]
    public void GroupMenu_ListsGroupActions()
    {
        using var ui = new DockUi();

        Assert.Equal(["Add panel…", "Undo layout · Ctrl+Alt+Z", "Redo layout · Ctrl+Alt+Y", "Collapse to sidebar", "Move / split",
            "Float group", "Close group", "Resize neighboring split"], GroupMenu(ui, "left").Headers());
    }

    [AvaloniaFact]
    public void GroupMenu_HistoryItemsReflectAvailability()
    {
        using var ui = new DockUi();
        Assert.False(GroupMenu(ui, "left").Item("Undo layout · Ctrl+Alt+Z").IsEnabled);

        Assert.True(ui.Run(new MoveTabs(["b"], "right")));

        Assert.True(GroupMenu(ui, "left").Item("Undo layout · Ctrl+Alt+Z").IsEnabled);
        Assert.False(GroupMenu(ui, "left").Item("Redo layout · Ctrl+Alt+Y").IsEnabled);
    }

    [AvaloniaFact]
    public void GroupMenu_UndoThenRedo_ReplaysLayout()
    {
        using var ui = new DockUi();
        Assert.True(ui.Run(new MoveTabs(["b"], "right")));

        GroupMenu(ui, "left").Item("Undo layout · Ctrl+Alt+Z").Invoke(); ui.Pump();
        Assert.Equal(["a", "b"], ui.Workspace.Layout.Group("left").SlotIds());

        GroupMenu(ui, "left").Item("Redo layout · Ctrl+Alt+Y").Invoke(); ui.Pump();
        Assert.Equal(["a"], ui.Workspace.Layout.Group("left").SlotIds());
    }

    [AvaloniaFact]
    public void GroupMenu_WithoutHistoryOrCreation_OmitsThoseItems()
    {
        using var ui = new DockUi(configure: c => { c.AllowLayoutHistory = false; c.AllowPanelCreation = false; });

        var headers = GroupMenu(ui, "left").Headers().ToArray();

        Assert.DoesNotContain("Add panel…", headers);
        Assert.DoesNotContain(headers, h => h.StartsWith("Undo"));
    }

    [AvaloniaFact]
    public void GroupMenu_CollapseToSidebar_OffersFourEdges()
    {
        using var ui = new DockUi();

        var collapse = GroupMenu(ui, "right").Item("Collapse to sidebar");

        Assert.Equal(["Left", "Right", "Top", "Bottom"], collapse.Headers());
        collapse.Item("Bottom").Invoke(); ui.Pump();
        Assert.Equal(DockSide.Bottom, ui.Workspace.Layout.Hosts[0].Rails.Single().Side);
    }

    [AvaloniaFact]
    public void GroupMenu_FloatGroup_FloatsAllTabs()
    {
        using var ui = new DockUi();

        GroupMenu(ui, "left").Item("Float group").Invoke(); ui.Pump();

        var floating = ui.Workspace.Layout.Hosts.Single(h => h.Id != "main");
        Assert.Equal(["a", "b"], ((TabGroup)floating.Root).SlotIds());
    }

    [AvaloniaFact]
    public void GroupMenu_CloseGroup_ClosesAllTabs()
    {
        using var ui = new DockUi();

        GroupMenu(ui, "right").Item("Close group").Invoke(); ui.Pump();

        Assert.Equal(2, ui.Workspace.Layout.Slots.Count());
        Assert.Single(ui.Main.GroupViews);
    }

    [AvaloniaFact]
    public void GroupMenu_CloseGroup_DisabledWhenAnyPanelIsFixed()
    {
        using var ui = new DockUi(L.TwoColumns(new Dictionary<string, string> { ["b"] = "fixed" }));

        Assert.False(GroupMenu(ui, "left").Item("Close group").IsEnabled);
        Assert.True(GroupMenu(ui, "right").Item("Close group").IsEnabled);
    }

    [AvaloniaFact]
    public void GroupMenu_EmptyGroup_HasNoTabActions()
    {
        using var ui = new DockUi(L.Build(L.H("h", L.Region("p", "primary"), L.Group("g", "a"))));

        var headers = GroupMenu(ui, "p").Headers().ToArray();

        Assert.DoesNotContain("Float group", headers);
        Assert.DoesNotContain("Close group", headers);
    }

    [AvaloniaFact]
    public void GroupMenu_ResizeNeighboringSplit_AdjustsWeights()
    {
        using var ui = new DockUi();
        var resize = GroupMenu(ui, "left").Item("Resize neighboring split");

        Assert.Equal(["Horizontal: decrease first section", "Horizontal: increase first section"], resize.Headers());
        resize.Item("Horizontal: increase first section").Invoke(); ui.Pump();

        Assert.Equal(0.54, ui.Workspace.Layout.SplitById("cols").Weights[0], 9);
    }

    [AvaloniaFact]
    public void GroupMenu_SingleGroupHost_HasNoResizeItem()
    {
        using var ui = new DockUi(L.Build(L.Group("g", "a")));

        Assert.DoesNotContain("Resize neighboring split", GroupMenu(ui, "g").Headers());
    }

    [AvaloniaFact]
    public void GroupMenu_RebuiltAfterLastTabMove_UsesCurrentTree()
    {
        // Regression: menus built during presentation must not reference splits removed by the move.
        using var ui = new DockUi();

        Assert.True(ui.Run(new MoveTabs(["c"], "left")));

        Assert.DoesNotContain(ui.Status, s => s.StartsWith("Presenter notification"));
        Assert.DoesNotContain("Resize neighboring split", GroupMenu(ui, "left").Headers());
    }

    [AvaloniaFact]
    public void GroupActionsButton_OpensTheGroupMenu()
    {
        using var ui = new DockUi();
        var more = ui.GroupView("left").ButtonWithTip("Group actions");

        more.Invoke(); ui.Pump();

        Assert.True(more.ContextMenu!.IsOpen);
        more.ContextMenu.Close();
    }
}
