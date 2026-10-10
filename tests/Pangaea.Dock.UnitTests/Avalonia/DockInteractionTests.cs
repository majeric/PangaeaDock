using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Pangaea.Dock.Avalonia;
using Pangaea.Dock.Core;
using Pangaea.Dock.Runtime;
using Pangaea.Dock.Tests.Support;

namespace Pangaea.Dock.Tests.Avalonia;

public class TabDragTests
{
    private static Point TabGrip(DockUi ui, string slot) => ui.At(ui.Tab(slot), 25, 16);
    private static Point Body(DockUi ui, string group) => ui.At(ui.GroupView(group), ui.GroupView(group).Bounds.Width / 2, ui.GroupView(group).Bounds.Height / 2);

    [AvaloniaFact]
    public void Drag_PreviewsWithoutChangingLayout()
    {
        using var ui = new DockUi();
        ui.Press(TabGrip(ui, "a"));
        var before = WorkspaceStore.Serialize(ui.Workspace.Layout);

        ui.Move(Body(ui, "right"));

        Assert.NotNull(ui.Control.CurrentPreviewPlan);
        Assert.NotNull(ui.Control.CurrentPreviewRectangle);
        Assert.Equal(before, WorkspaceStore.Serialize(ui.Workspace.Layout));
        Assert.Contains("Join tabs", ui.Main.Texts());
        Assert.StartsWith("Release to join tabs", ui.Control.LastMessage);
        ui.Release(Body(ui, "right"));
    }

    [AvaloniaFact]
    public void Drag_ReleaseCommitsAndPreviewMatchesCommittedGeometry()
    {
        using var ui = new DockUi();
        ui.Press(TabGrip(ui, "a")); ui.Move(Body(ui, "right"));
        var preview = ui.Control.CurrentPreviewRectangle!.Value;

        ui.Release(Body(ui, "right"));

        Assert.Equal(["c", "a"], ui.Workspace.Layout.Group("right").SlotIds());
        Assert.Equal(preview, ui.Main.Geometry!.Groups["right"]);
        Assert.Null(ui.Control.CurrentPreviewPlan);
        Assert.DoesNotContain("Join tabs", ui.Main.Texts());
    }

    [AvaloniaFact]
    public void Drag_NearBottomEdge_SplitsWithPreviewEqualToResult()
    {
        using var ui = new DockUi();
        var target = ui.GroupView("right");
        var edge = ui.At(target, target.Bounds.Width / 2, target.Bounds.Height - 12);

        ui.Press(TabGrip(ui, "b")); ui.Move(edge);
        var preview = ui.Control.CurrentPreviewRectangle!.Value;
        Assert.Contains("Split bottom", ui.Main.Texts());
        ui.Release(edge);

        var group = ui.Workspace.Layout.GroupOf("b");
        Assert.NotEqual("right", group.Id);
        Assert.Equal(preview, ui.Main.Geometry!.Groups[group.Id]);
        var columns = Assert.IsType<SplitNode>(ui.Workspace.Layout.Hosts[0].Root);
        Assert.Equal(SplitAxis.Vertical, Assert.IsType<SplitNode>(columns.Children[1]).Axis);
    }

    [AvaloniaFact]
    public void Drag_OntoTabStrip_InsertsAtHoveredPosition()
    {
        using var ui = new DockUi();
        var target = ui.At(ui.Tab("c"), 5, 16);

        ui.Drag(TabGrip(ui, "a"), target);

        Assert.Equal(["a", "c"], ui.Workspace.Layout.Group("right").SlotIds());
    }

    [AvaloniaFact]
    public void Drag_OntoEndOfTabStrip_AppendsTab()
    {
        using var ui = new DockUi();
        var strip = ui.GroupView("right");

        ui.Drag(TabGrip(ui, "a"), ui.At(strip, strip.Bounds.Width - 60, 16));

        Assert.Equal(["c", "a"], ui.Workspace.Layout.Group("right").SlotIds());
    }

    [AvaloniaFact]
    public void Drag_LastTabOfGroup_RemovesGroupAndKeepsAllContentRendered()
    {
        using var ui = new DockUi();

        ui.Drag(TabGrip(ui, "c"), Body(ui, "left"));

        Assert.Equal(["left"], ui.Main.GroupViews.Keys);
        Assert.DoesNotContain(ui.Window.Texts(), t => t.StartsWith("Panel failed to load"));
        Assert.True(ui.Workspace.UndoAsync().Result); ui.Pump();
        Assert.Equal(2, ui.Main.GroupViews.Count);
    }

    [AvaloniaFact]
    public void Drag_BelowThreshold_DoesNotPreviewOrMove()
    {
        using var ui = new DockUi();
        var start = TabGrip(ui, "b");

        ui.Press(start); ui.Move(start + new Vector(3, 2));
        Assert.Null(ui.Control.CurrentPreviewPlan);
        ui.Release(start + new Vector(3, 2));

        Assert.Equal(["a", "b"], ui.Workspace.Layout.Group("left").SlotIds());
        Assert.Equal("b", ui.Workspace.Layout.Group("left").ActiveSlotId);
        Assert.False(ui.Workspace.CanUndo);
    }

    [AvaloniaFact]
    public void Drag_EscapeCancelsWithoutMutation()
    {
        using var ui = new DockUi();
        ui.Press(TabGrip(ui, "a")); ui.Move(Body(ui, "right"));
        var before = WorkspaceStore.Serialize(ui.Workspace.Layout);

        ui.Key(PhysicalKey.Escape);
        Assert.Null(ui.Control.CurrentPreviewPlan);
        ui.Release(Body(ui, "right"));

        Assert.Equal(before, WorkspaceStore.Serialize(ui.Workspace.Layout));
        Assert.DoesNotContain("Join tabs", ui.Main.Texts());
    }

    [AvaloniaFact]
    public void Drag_GroupGrip_MovesEveryTabInGroup()
    {
        using var ui = new DockUi();
        var grip = ui.At(ui.GroupView("left"), 10, 17);

        ui.Drag(grip, Body(ui, "right"));

        Assert.Equal(["c", "a", "b"], ui.Workspace.Layout.Group("right").SlotIds());
    }

    [AvaloniaFact]
    public void Drag_WhileLayoutLocked_DoesNothing()
    {
        using var ui = new DockUi();
        ui.Workspace.IsLayoutLocked = true;

        ui.Press(TabGrip(ui, "a")); ui.Move(Body(ui, "right"));

        Assert.Null(ui.Control.CurrentPreviewPlan);
        ui.Release(Body(ui, "right"));
        Assert.Equal(["a", "b"], ui.Workspace.Layout.Group("left").SlotIds());
    }

    [AvaloniaFact]
    public void Drag_LayoutChangedMidDrag_CancelsAndAsksToRetry()
    {
        using var ui = new DockUi();
        ui.Press(TabGrip(ui, "a"));
        Assert.True(ui.Workspace.ExecuteAsync(new ResizeSplit("cols", [0.6, 0.4])).Result);

        ui.Move(Body(ui, "right"));

        Assert.Null(ui.Control.CurrentPreviewPlan);
        Assert.Equal("Layout changed; please drag again.", ui.Control.LastMessage);
        ui.Release(Body(ui, "right"));
        Assert.Equal(["a", "b"], ui.Workspace.Layout.Group("left").SlotIds());
    }

    [AvaloniaFact]
    public void Drag_RejectedTarget_ReportsReasonAndHasNoPlan()
    {
        using var ui = new DockUi();
        var own = ui.GroupView("right");
        var nearOwnLeftEdge = ui.At(own, 12, own.Bounds.Height / 2);

        ui.Press(TabGrip(ui, "c")); ui.Move(nearOwnLeftEdge);

        Assert.Null(ui.Control.CurrentPreviewPlan);
        Assert.Contains("beside itself", ui.Control.LastMessage);
        ui.Release(nearOwnLeftEdge);
    }

    [AvaloniaFact]
    public void Drag_OutsideEveryHost_HintsAtFloatingAndDropsNothing()
    {
        using var ui = new DockUi();
        var outside = new Point(1400, 900);

        ui.Press(TabGrip(ui, "a")); ui.Move(Body(ui, "right")); ui.Move(outside);

        Assert.Null(ui.Control.CurrentPreviewPlan);
        Assert.Equal("Drop onto a workspace · Use Move to New Window to float", ui.Control.LastMessage);
        Assert.DoesNotContain("Join tabs", ui.Main.Texts());
        ui.Release(outside);
        Assert.Equal(["a", "b"], ui.Workspace.Layout.Group("left").SlotIds());
    }

    [AvaloniaFact]
    public void Drag_SplitThatDoesNotFit_IsNotPreviewed()
    {
        using var ui = new DockUi(width: 400, height: 400);
        var target = ui.GroupView("right");
        var edge = ui.At(target, target.Bounds.Width - 8, target.Bounds.Height / 2);

        ui.Press(TabGrip(ui, "a")); ui.Move(edge);

        Assert.Null(ui.Control.CurrentPreviewPlan);
        Assert.Equal("Not enough room for this split.", ui.Control.LastMessage);
        ui.Release(edge);
    }

    [AvaloniaFact]
    public void Drag_SizeChangeDuringDrag_InvalidatesPreview()
    {
        using var ui = new DockUi();
        ui.Press(TabGrip(ui, "a")); ui.Move(Body(ui, "right"));
        Assert.NotNull(ui.Control.CurrentPreviewPlan);

        ui.Window.Width = 900; ui.Pump();

        Assert.Null(ui.Control.CurrentPreviewPlan);
        ui.Release(Body(ui, "right"));
        Assert.Equal(["c"], ui.Workspace.Layout.Group("right").SlotIds());
    }
}

public class SplitterResizeTests
{
    private static Point SplitterPoint(DockUi ui)
    {
        var splitter = ui.Main.Geometry!.Splitters.Single();
        return ui.At(ui.Main, splitter.Bounds.X + 3, splitter.Bounds.Y + 100);
    }

    [AvaloniaFact]
    public void Splitter_DragPreviewsThenCommitsOnRelease()
    {
        using var ui = new DockUi();
        var start = SplitterPoint(ui); var leftWidth = ui.Main.Geometry!.Groups["left"].Width;
        var before = WorkspaceStore.Serialize(ui.Workspace.Layout);

        ui.Press(start); ui.Move(start + new Vector(40, 0));

        Assert.NotNull(ui.Control.CurrentPreviewPlan);
        Assert.Equal(before, WorkspaceStore.Serialize(ui.Workspace.Layout));
        Assert.Equal(leftWidth + 40, ui.GroupView("left").Bounds.Width, 1);
        Assert.StartsWith("Resize panels", ui.Control.LastMessage);

        ui.Release(start + new Vector(40, 0));

        Assert.Equal(leftWidth + 40, ui.Main.Geometry!.Groups["left"].Width, 1);
        Assert.True(ui.Workspace.CanUndo);
        Assert.Equal("Resize panels", ui.Workspace.UndoDescription);
    }

    [AvaloniaFact]
    public void Splitter_DragIsClampedToNeighbourMinimum()
    {
        using var ui = new DockUi();
        var start = SplitterPoint(ui);

        ui.Drag(start, start + new Vector(900, 0));

        Assert.Equal(160, ui.Main.Geometry!.Groups["right"].Width, 1);
    }

    [AvaloniaFact]
    public void Splitter_EscapeRestoresOriginalSizes()
    {
        using var ui = new DockUi();
        var start = SplitterPoint(ui); var leftWidth = ui.GroupView("left").Bounds.Width;
        ui.Press(start); ui.Move(start + new Vector(60, 0));

        ui.Key(PhysicalKey.Escape);
        ui.Release(start + new Vector(60, 0));

        Assert.Equal(leftWidth, ui.GroupView("left").Bounds.Width, 1);
        Assert.False(ui.Workspace.CanUndo);
    }

    [AvaloniaFact]
    public void Splitter_LockedLayout_IgnoresPress()
    {
        using var ui = new DockUi();
        ui.Workspace.IsLayoutLocked = true;
        var start = SplitterPoint(ui);

        ui.Press(start); ui.Move(start + new Vector(40, 0));

        Assert.Null(ui.Control.CurrentPreviewPlan);
        ui.Release(start + new Vector(40, 0));
    }

    [AvaloniaFact]
    public void Splitter_HandleUsesResizeCursor()
    {
        using var ui = new DockUi();

        var handle = ui.Main.Descendants<Border>().Single(b => b.Tag is SplitterGeometry);

        Assert.NotNull(handle.Cursor);
    }
}

public class RailTests
{
    private static DockUi Collapsed(DockSide side = DockSide.Right)
    {
        var ui = new DockUi();
        Assert.True(ui.Run(new CollapseGroup("right", side)));
        return ui;
    }

    private static DockGroupView? Overlay(DockUi ui) =>
        ui.Main.Descendants<DockGroupView>().FirstOrDefault(v => !ui.Main.GroupViews.Values.Contains(v));

    [AvaloniaFact]
    public void Collapse_ShowsRailButtonAndReservesEdge()
    {
        using var ui = Collapsed();

        var button = ui.Main.ButtonWithTip("Open Text");

        Assert.Equal(30, button.Bounds.Width);
        Assert.Equal(30, ui.Main.Bounds.Width - ui.Main.ContentBounds.Width - ui.Main.ContentBounds.X, 1);
        Assert.Null(Overlay(ui));
    }

    [AvaloniaTheory]
    [InlineData(DockSide.Left)]
    [InlineData(DockSide.Top)]
    [InlineData(DockSide.Bottom)]
    public void Collapse_ReservesTheChosenEdge(DockSide side)
    {
        using var ui = Collapsed(side);

        var content = ui.Main.ContentBounds;

        Assert.Equal(side == DockSide.Left ? 30 : 0, content.X);
        Assert.Equal(side == DockSide.Top ? 30 : 0, content.Y);
        Assert.Equal(side == DockSide.Bottom ? ui.Main.Bounds.Height - 30 : ui.Main.Bounds.Height - (side == DockSide.Top ? 30 : 0), content.Height, 1);
    }

    [AvaloniaFact]
    public void RailButton_ClickOpensTemporaryOverlayWithContent()
    {
        using var ui = Collapsed();

        ui.Click(ui.CenterOf(ui.Main.ButtonWithTip("Open Text")));

        var overlay = Overlay(ui);
        Assert.NotNull(overlay);
        Assert.Equal("right", overlay!.GroupId);
        Assert.Contains(overlay.Descendants<PanelView>(), v => ReferenceEquals(v.Tag, ui.F.SessionOf("c")));
        Assert.Equal(300, overlay.Bounds.Width, 1);
        Assert.True(overlay.HasButtonWithTip("Pin group into the workspace"));
    }

    [AvaloniaFact]
    public void RailButton_SecondClickClosesOverlay()
    {
        using var ui = Collapsed();
        var at = ui.CenterOf(ui.Main.ButtonWithTip("Open Text"));

        ui.Click(at); ui.Click(at);

        Assert.Null(Overlay(ui));
    }

    [AvaloniaFact]
    public void Overlay_EscapeCloses()
    {
        using var ui = Collapsed();
        ui.Click(ui.CenterOf(ui.Main.ButtonWithTip("Open Text")));

        ui.Key(PhysicalKey.Escape);

        Assert.Null(Overlay(ui));
    }

    [AvaloniaFact]
    public void Overlay_PointerPressOutsideCloses()
    {
        using var ui = Collapsed();
        ui.Click(ui.CenterOf(ui.Main.ButtonWithTip("Open Text")));

        ui.Click(ui.At(ui.GroupView("left"), 40, 200));

        Assert.Null(Overlay(ui));
    }

    [AvaloniaFact]
    public void Overlay_SurvivesUnrelatedRebuild()
    {
        using var ui = Collapsed();
        ui.Click(ui.CenterOf(ui.Main.ButtonWithTip("Open Text")));

        Assert.True(ui.Workspace.ExecuteAsync(new SelectTab("b")).Result); ui.Pump();

        Assert.NotNull(Overlay(ui));
    }

    [AvaloniaFact]
    public void Overlay_PinButtonReturnsGroupToWorkspace()
    {
        using var ui = Collapsed();
        ui.Click(ui.CenterOf(ui.Main.ButtonWithTip("Open Text")));

        Overlay(ui)!.ButtonWithTip("Pin group into the workspace").Invoke(); ui.Pump();

        Assert.Empty(ui.Workspace.Layout.Hosts[0].Rails);
        Assert.Equal(2, ui.Main.GroupViews.Count);
        Assert.Null(Overlay(ui));
        Assert.False(ui.Main.HasButtonWithTip("Open Text"));
    }

    [AvaloniaFact]
    public void Overlay_GroupMenuOffersPin()
    {
        using var ui = Collapsed();
        ui.Click(ui.CenterOf(ui.Main.ButtonWithTip("Open Text")));
        var menu = Overlay(ui)!.ButtonWithTip("Group actions").ContextMenu!;

        Assert.DoesNotContain("Collapse to sidebar", menu.Headers());
        menu.Item("Pin group").Invoke(); ui.Pump();

        Assert.Empty(ui.Workspace.Layout.Hosts[0].Rails);
    }
}

public class FloatingWindowTests
{
    private static (DockUi Ui, Window Window, DockSurface Surface) Floated(Action<DockWorkspaceControl>? configure = null)
    {
        var ui = new DockUi(configure: configure);
        Assert.True(ui.Run(new FloatTabs(["c"], new WindowPlacement(50, 60, 500, 300))));
        var surface = ui.Control.Surfaces.Values.Single(s => s.HostId != "main");
        return (ui, (Window)TopLevel.GetTopLevel(surface)!, surface);
    }

    [AvaloniaFact]
    public void Float_CreatesNativeWindowSizedFromPlacement()
    {
        var (ui, window, surface) = Floated();
        using var _ = ui;

        Assert.True(window.IsVisible);
        Assert.Equal("Pangaea Dock · Floating panels", window.Title);
        Assert.Equal(500, window.Width);
        Assert.Equal(300, window.Height);
        Assert.Same(ui.Window, window.Owner);
        Assert.Equal(["c"], surface.GroupViews.Values.Single().TabHeaders.Keys);
    }

    [AvaloniaFact]
    public void Float_UsesConsumerWindowFactoryAndSurfaceHook()
    {
        var custom = new Window(); var configured = new List<string>();
        var (ui, window, surface) = Floated(c => { c.FloatingWindowFactory = () => custom; c.ConfigureSurface = s => configured.Add(s.HostId); });
        using var _ = ui;

        Assert.Same(custom, window);
        Assert.Equal([surface.HostId], configured);
    }

    [AvaloniaFact]
    public void Float_MaximizedPlacement_MaximizesWindow()
    {
        using var ui = new DockUi();

        Assert.True(ui.Run(new FloatTabs(["c"], new WindowPlacement(Maximized: true))));

        var window = (Window)TopLevel.GetTopLevel(ui.Control.Surfaces.Values.Single(s => s.HostId != "main"))!;
        Assert.Equal(WindowState.Maximized, window.WindowState);
    }

    [AvaloniaFact]
    public void DockingLastTabBack_ClosesTheWindow()
    {
        var (ui, window, _) = Floated();
        using var __ = ui;

        Assert.True(ui.Run(new MoveTabs(["c"], "left")));

        Assert.False(window.IsVisible);
        Assert.Single(ui.Control.Surfaces);
    }

    [AvaloniaFact]
    public void ClosingTheWindow_ClosesItsTabsThroughTheWorkspace()
    {
        var (ui, window, _) = Floated();
        using var __ = ui;

        window.Close(); ui.Pump();

        Assert.False(window.IsVisible);
        Assert.DoesNotContain(ui.Workspace.Layout.Slots, s => s.Id == "c");
        Assert.Single(ui.Workspace.Layout.Hosts);
    }

    [AvaloniaFact]
    public void ClosingTheWindow_CanceledByCloseRequested_KeepsWindowAndTabs()
    {
        var (ui, window, _) = Floated(c => c.CloseRequested = _ => Task.FromResult(false));
        using var __ = ui;

        window.Close(); ui.Pump();

        Assert.True(window.IsVisible);
        Assert.Contains(ui.Workspace.Layout.Slots, s => s.Id == "c");
    }

    [AvaloniaFact]
    public void CaptureFloatingPositions_StoresWindowBounds()
    {
        var (ui, window, surface) = Floated();
        using var _ = ui;
        window.Position = new PixelPoint(70, 80); window.Width = 420; ui.Pump();

        ui.Control.CaptureFloatingPositions();

        var placement = ui.Workspace.Layout.Hosts.Single(h => h.Id == surface.HostId).Placement!;
        Assert.Equal(70, placement.X);
        Assert.Equal(80, placement.Y);
        Assert.Equal(420, placement.Width);
        Assert.Equal(window.RenderScaling, placement.Scale);
    }

    [AvaloniaFact]
    public void CaptureFloatingPositions_SkipsMaximizedWindows()
    {
        var (ui, window, surface) = Floated();
        using var _ = ui;
        window.WindowState = WindowState.Maximized; ui.Pump();
        var revision = ui.Workspace.Layout.Revision;

        ui.Control.CaptureFloatingPositions();

        Assert.Equal(revision, ui.Workspace.Layout.Revision);
    }

    [AvaloniaFact]
    public void CapturedPlacement_DoesNotRebuildSurfaces()
    {
        var (ui, _, surface) = Floated();
        using var __ = ui;
        var view = surface.GroupViews.Values.Single();

        ui.Control.CaptureFloatingPositions(); ui.Pump();

        Assert.Same(view, surface.GroupViews.Values.Single());
    }

    [AvaloniaFact]
    public void UpdateFloatingThemes_FollowsHostTheme()
    {
        var (ui, window, _) = Floated();
        using var _ = ui;
        ui.Window.RequestedThemeVariant = global::Avalonia.Styling.ThemeVariant.Light; ui.Pump();

        ui.Control.UpdateFloatingThemes();

        Assert.Equal(global::Avalonia.Styling.ThemeVariant.Light, window.RequestedThemeVariant);
    }

    [AvaloniaFact]
    public void Drag_FromFloatingWindowIntoMainHost_Commits()
    {
        var (ui, window, surface) = Floated();
        using var _ = ui;
        var tab = surface.GroupViews.Values.Single().TabHeaders["c"];
        var press = tab.TranslatePoint(new Point(25, 16), window)!.Value;
        // Target an exposed part of the main host, not a point covered by the floating window.
        var target = ui.GroupView("left");
        var screen = target.PointToScreen(new Point(target.Bounds.Width - 60, target.Bounds.Height - 120));
        var local = window.PointToClient(screen);

        window.MouseDown(press, MouseButton.Left); window.MouseMove(local); ui.Pump();
        Assert.NotNull(ui.Control.CurrentPreviewPlan);
        window.MouseUp(local, MouseButton.Left); ui.Pump();

        Assert.Contains("c", ui.Workspace.Layout.Group("left").SlotIds());
        Assert.Single(ui.Control.Surfaces);
    }

    [AvaloniaFact]
    public void FloatingWindow_HandlesLayoutShortcuts()
    {
        var (ui, window, surface) = Floated();
        using var _ = ui;
        surface.GroupViews.Values.Single().TabHeaders["c"].Focus();

        window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control | RawInputModifiers.Alt); ui.Pump();

        Assert.Single(ui.Workspace.Layout.Hosts);
    }
}
