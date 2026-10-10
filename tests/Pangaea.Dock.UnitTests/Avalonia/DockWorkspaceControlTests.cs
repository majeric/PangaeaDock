using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Pangaea.Dock.Avalonia;
using Pangaea.Dock.Core;
using Pangaea.Dock.Tests.Support;

namespace Pangaea.Dock.Tests.Avalonia;

public class DockWorkspaceControlConstructionTests
{
    [AvaloniaFact]
    public void Show_RendersOneGroupViewPerLayoutGroup()
    {
        using var ui = new DockUi();

        Assert.Equal(["left", "right"], ui.Main.GroupViews.Keys.Order());
        Assert.Equal(2, ui.Main.Geometry!.Groups.Count);
        Assert.Single(ui.Main.Geometry.Splitters);
        Assert.All(ui.Main.GroupViews.Values, v => Assert.True(v.Bounds.Width > 400 && v.Bounds.Height > 600));
    }

    [AvaloniaFact]
    public void Show_GroupViewBoundsMatchComputedGeometry()
    {
        using var ui = new DockUi();

        var rect = ui.Main.Geometry!.Groups["right"];
        var view = ui.GroupView("right");

        Assert.Equal(rect.X, view.Bounds.X, 3);
        Assert.Equal(rect.Width, view.Bounds.Width, 3);
        Assert.Equal(rect.Height, view.Bounds.Height, 3);
    }

    [AvaloniaFact]
    public void Show_DisplaysTheActivePanelViewOfEachGroup()
    {
        using var ui = new DockUi();

        var shown = ui.Window.Descendants<PanelView>().ToArray();

        Assert.Equal(2, shown.Length);
        Assert.Contains(shown, v => ReferenceEquals(v.Tag, ui.F.SessionOf("a")));
        Assert.Contains(shown, v => ReferenceEquals(v.Tag, ui.F.SessionOf("c")));
    }

    [AvaloniaFact]
    public void WorkspaceChange_RebuildsGroupViewsAndReportsDescription()
    {
        using var ui = new DockUi();

        Assert.True(ui.Workspace.ExecuteAsync(new MoveTabs(["c"], "left")).Result); ui.Pump();

        Assert.Equal(["left"], ui.Main.GroupViews.Keys);
        Assert.Equal(["a", "b", "c"], ui.GroupView("left").TabHeaders.Keys);
        Assert.Equal("Move tabs", ui.Control.LastMessage);
        Assert.Contains("Move tabs", ui.Status);
    }

    [AvaloniaFact]
    public void MovingAPanel_ReusesItsViewInstance()
    {
        using var ui = new DockUi();
        var view = ui.Window.Descendants<PanelView>().Single(v => ReferenceEquals(v.Tag, ui.F.SessionOf("a")));

        Assert.True(ui.Run(new MoveTabs(["a"], "right", DockSide.Bottom)));

        Assert.Same(view, ui.Window.Descendants<PanelView>().Single(v => ReferenceEquals(v.Tag, ui.F.SessionOf("a"))));
        Assert.Single(ui.CreatedViews, v => ReferenceEquals(v.Tag, ui.F.SessionOf("a")));
    }

    [AvaloniaFact]
    public void WorkspaceDiagnostic_IsSurfacedAsStatus()
    {
        using var ui = new DockUi();

        ui.Workspace.Report("Something happened");

        Assert.Equal("Something happened", ui.Control.LastMessage);
    }

    [AvaloniaFact]
    public void PanelDisposedByWorkspace_DisposesItsView()
    {
        using var ui = new DockUi();
        var view = ui.Window.Descendants<PanelView>().Single(v => ReferenceEquals(v.Tag, ui.F.SessionOf("c")));
        ui.Workspace.HistoryLimit = 1;

        Assert.True(ui.Run(new CloseTabs(["c"])));
        Assert.Equal(0, view.DisposeCount); // still retained by undo
        Assert.True(ui.Run(new MoveTabs(["a"], "left", Index: 2)));

        Assert.Equal(1, view.DisposeCount);
    }

    [AvaloniaFact]
    public void Dispose_UnsubscribesClosesFloatingWindowsAndDisposesViews()
    {
        var ui = new DockUi();
        Assert.True(ui.Run(new FloatTabs(["c"])));
        var floating = (Window)TopLevel.GetTopLevel(ui.Control.Surfaces.Values.Single(s => s.HostId != "main"))!;
        var views = ui.CreatedViews.ToArray();

        ui.Control.Dispose();
        ui.Pump();

        Assert.False(floating.IsVisible);
        Assert.All(views, v => Assert.Equal(1, v.DisposeCount));
        Assert.Empty(ui.Control.Surfaces);
        Assert.True(ui.Workspace.ExecuteAsync(new MoveTabs(["b"], "left", DockSide.Bottom)).Result);
        Assert.Empty(ui.Control.Surfaces);
        Assert.Null(ui.Workspace.PreparePresentation);
        ui.Control.Dispose(); // idempotent
        ui.Window.Close(); ui.F.Dispose();
    }

    [AvaloniaFact]
    public void Fits_SplitThatCannotFitInWindow_IsRejectedWithReason()
    {
        using var ui = new DockUi(width: 400, height: 300);

        Assert.False(ui.Run(new MoveTabs(["a"], "right", DockSide.Right)));

        Assert.Contains("isn't enough room", ui.Control.LastMessage);
        Assert.Equal(2, ui.Workspace.Layout.Groups.Count());
    }

    [AvaloniaFact]
    public void Fits_ReplacementThatNeedsMoreThanItsGroup_IsRejected()
    {
        using var ui = new DockUi(width: 700);

        Assert.False(ui.Run(new AddPanel(new PanelState("w1", "wide"), "right", "c")));

        Assert.Contains("needs more space", ui.Control.LastMessage);
        Assert.Equal("p-c", ui.Workspace.Layout.Slot("c").InstanceId);
    }

    [AvaloniaFact]
    public void Replacement_KeepsTheGroupBounds()
    {
        using var ui = new DockUi();
        var before = ui.Main.Geometry!.Groups["right"];

        Assert.True(ui.Run(new AddPanel(new PanelState("n1", "text"), "right", "c")));

        Assert.Equal(before, ui.Main.Geometry!.Groups["right"]);
    }

    [AvaloniaFact]
    public void RunAsync_RejectedOperation_ReportsEngineError()
    {
        using var ui = new DockUi();

        Assert.False(ui.Run(new SwapPanels("a", "a")));

        Assert.Equal("Choose another tab to swap with.", ui.Control.LastMessage);
    }

    [AvaloniaFact]
    public void ViewFactoryFailureDuringCommit_AbortsTheOperation()
    {
        using var ui = new DockUi(views: v =>
        {
            v.Register("text", s => new PanelView("text", s));
            v.Register("single", _ => throw new InvalidOperationException("no view"));
        });

        Assert.False(ui.Run(new AddPanel(new PanelState("s1", "single"), "right")));

        Assert.DoesNotContain("s1", ui.Workspace.Layout.Panels.Keys);
        Assert.Contains("no view", ui.Control.LastMessage);
    }

    [AvaloniaFact]
    public void ViewFactoryFailureOnFirstRender_ShowsFailureInsteadOfContent()
    {
        using var ui = new DockUi(L.TwoColumns(new Dictionary<string, string> { ["c"] = "single" }), views: v =>
        {
            v.Register("text", s => new PanelView("text", s));
            v.Register("single", _ => throw new InvalidOperationException("no view"));
        });

        Assert.Contains("Panel failed to load: no view", ui.GroupView("right").Texts());
    }

    [AvaloniaFact]
    public void SharedViewForTwoPanels_IsRejected()
    {
        var shared = new Border();
        using var ui = new DockUi(views: v => v.Register("text", _ => shared));

        Assert.Contains("Panel failed to load: A panel factory must return an unattached control.", ui.GroupView("right").Texts());
    }

    [AvaloniaFact]
    public void MissingViewProvider_ShowsPlaceholder()
    {
        using var ui = new DockUi(views: _ => { });

        Assert.Contains(ui.GroupView("right").Texts(), t => t.StartsWith("Panel provider unavailable: text"));
    }

    [AvaloniaFact]
    public void EmptyGroup_ShowsDefaultInvitationWithAddButton()
    {
        using var ui = new DockUi(L.Build(L.Region("p", "primary")));

        Assert.Contains("A little room to explore", ui.GroupView("p").Texts());
        Assert.True(ui.GroupView("p").HasButtonWithTip("Add a panel"));
    }

    [AvaloniaFact]
    public void EmptyGroup_UsesConsumerContentWhenProvided()
    {
        using var ui = new DockUi(L.Build(L.Region("p", "primary")), configure: c => c.EmptyGroupContent = g => new TextBlock { Text = "custom " + g.Id });

        Assert.Contains("custom p", ui.GroupView("p").Texts());
        Assert.DoesNotContain("A little room to explore", ui.GroupView("p").Texts());
    }

    [AvaloniaFact]
    public void EmptyGroup_WithoutPanelCreation_HasNoAddButton()
    {
        using var ui = new DockUi(L.Build(L.Region("p", "primary")), configure: c => c.AllowPanelCreation = false);

        Assert.False(ui.GroupView("p").HasButtonWithTip("Add a panel"));
    }
}

public class DockTabTests
{
    [AvaloniaFact]
    public void Tabs_AreRenderedInSlotOrderWithTitles()
    {
        using var ui = new DockUi();

        Assert.Equal(["a", "b"], ui.GroupView("left").TabHeaders.Keys);
        Assert.Contains("◇  Text", ui.GroupView("left").Texts());
    }

    [AvaloniaFact]
    public void ActiveTab_IsAnnouncedAsSelected()
    {
        using var ui = new DockUi();

        Assert.Equal("Text, selected", AutomationProperties.GetName(ui.Tab("a")));
        Assert.Equal("Text", AutomationProperties.GetName(ui.Tab("b")));
    }

    [AvaloniaFact]
    public void PanelTitle_OverridesDescriptorTitle()
    {
        using var ui = new DockUi(configure: c => c.PanelTitle = p => "T:" + p.Id);

        Assert.Equal("T:p-a, selected", AutomationProperties.GetName(ui.Tab("a")));
    }

    [AvaloniaFact]
    public void ConfigureTab_IsCalledForEveryTab()
    {
        var configured = new List<string>();
        using var ui = new DockUi(configure: c => c.ConfigureTab = (p, _, title) => { configured.Add(p.Id); title.Text = "custom"; });

        Assert.Contains("p-a", configured);
        Assert.Contains("p-c", configured);
        Assert.Contains("custom", ui.GroupView("right").Texts());
    }

    [AvaloniaFact]
    public void ClickingTab_SelectsItAndActivatesPanel()
    {
        var activated = new List<string>();
        using var ui = new DockUi(configure: c => c.PanelActivated = activated.Add);

        ui.Click(ui.CenterOf(ui.Tab("b")));

        Assert.Equal("b", ui.Workspace.Layout.Group("left").ActiveSlotId);
        Assert.Contains("p-b", activated);
        Assert.False(ui.Workspace.CanUndo);
        Assert.Equal("Text, selected", AutomationProperties.GetName(ui.Tab("b")));
    }

    [AvaloniaFact]
    public void ArrowKeys_CycleSelectionWithinGroup()
    {
        using var ui = new DockUi();

        ui.Tab("a").Focus(); ui.Key(PhysicalKey.ArrowRight);
        Assert.Equal("b", ui.Workspace.Layout.Group("left").ActiveSlotId);

        ui.Tab("b").Focus(); ui.Key(PhysicalKey.ArrowRight);
        Assert.Equal("a", ui.Workspace.Layout.Group("left").ActiveSlotId);

        ui.Tab("a").Focus(); ui.Key(PhysicalKey.ArrowLeft);
        Assert.Equal("b", ui.Workspace.Layout.Group("left").ActiveSlotId);
    }

    [AvaloniaFact]
    public void ArrowKey_KeepsKeyboardFocusOnTheNewlySelectedTab()
    {
        using var ui = new DockUi();
        ui.Tab("a").Focus();

        ui.Key(PhysicalKey.ArrowRight);

        Assert.Same(ui.Tab("b"), ui.Window.FocusManager!.GetFocusedElement());
    }

    [AvaloniaFact]
    public void EnterKey_SelectsFocusedTab()
    {
        var activated = new List<string>();
        using var ui = new DockUi(configure: c => c.PanelActivated = activated.Add);
        ui.Tab("b").Focus();

        ui.Key(PhysicalKey.Enter);

        Assert.Equal("b", ui.Workspace.Layout.Group("left").ActiveSlotId);
        Assert.Equal(["p-b"], activated);
    }

    [AvaloniaFact]
    public void ShiftF10_OpensTabContextMenu()
    {
        using var ui = new DockUi();
        var tab = ui.Tab("a"); tab.Focus();

        ui.Key(PhysicalKey.F10, RawInputModifiers.Shift);

        Assert.True(tab.ContextMenu!.IsOpen);
        tab.ContextMenu.Close();
    }

    [AvaloniaFact]
    public void PointerInContent_ActivatesPanel()
    {
        var activated = new List<string>();
        using var ui = new DockUi(configure: c => c.PanelActivated = activated.Add);
        var view = ui.Window.Descendants<PanelView>().Single(v => ReferenceEquals(v.Tag, ui.F.SessionOf("c")));

        ui.Click(ui.CenterOf(view));

        Assert.Contains("p-c", activated);
    }

    [AvaloniaFact]
    public void CtrlAltZAndY_UndoAndRedoLayout()
    {
        using var ui = new DockUi();
        Assert.True(ui.Run(new MoveTabs(["a"], "right")));
        ui.Tab("b").Focus();

        ui.Key(PhysicalKey.Z, RawInputModifiers.Control | RawInputModifiers.Alt);
        Assert.Equal(["a", "b"], ui.Workspace.Layout.Group("left").SlotIds());

        ui.Tab("b").Focus();
        ui.Key(PhysicalKey.Y, RawInputModifiers.Control | RawInputModifiers.Alt);
        Assert.Equal(["b"], ui.Workspace.Layout.Group("left").SlotIds());
    }

    [AvaloniaFact]
    public void LayoutShortcuts_AreIgnoredWhenHistoryIsDisabled()
    {
        using var ui = new DockUi(configure: c => c.AllowLayoutHistory = false);
        Assert.True(ui.Run(new MoveTabs(["a"], "right")));
        ui.Tab("b").Focus();

        ui.Key(PhysicalKey.Z, RawInputModifiers.Control | RawInputModifiers.Alt);

        Assert.Equal(["b"], ui.Workspace.Layout.Group("left").SlotIds());
    }

    [AvaloniaFact]
    public void PlainCtrlZ_IsLeftForDocumentUndo()
    {
        using var ui = new DockUi();
        Assert.True(ui.Run(new MoveTabs(["a"], "right")));
        ui.Tab("b").Focus();

        ui.Key(PhysicalKey.Z, RawInputModifiers.Control);

        Assert.True(ui.Workspace.CanUndo);
    }
}

public class CloseButtonTests
{
    [AvaloniaFact]
    public void CloseButtons_AreHiddenByDefault()
    {
        using var ui = new DockUi();

        Assert.False(ui.Main.HasButtonWithTip("Close Text"));
    }

    [AvaloniaFact]
    public void CloseButton_ClosesItsTab()
    {
        using var ui = new DockUi(configure: c => c.ShowCloseButtons = true);

        ui.Tab("c").ButtonWithTip("Close Text").Invoke(); ui.Pump();

        Assert.DoesNotContain(ui.Workspace.Layout.Slots, s => s.Id == "c");
    }

    [AvaloniaFact]
    public void CloseButton_PointerPressDoesNotStartTabDrag()
    {
        using var ui = new DockUi(configure: c => c.ShowCloseButtons = true);
        var close = ui.Tab("c").ButtonWithTip("Close Text");

        ui.Press(ui.CenterOf(close));
        ui.Move(ui.CenterOf(ui.GroupView("left")));

        Assert.Null(ui.Control.CurrentPreviewPlan);
        ui.Release(ui.CenterOf(ui.GroupView("left")));
    }

    [AvaloniaFact]
    public void CloseButton_IsOmittedForNonClosablePanel()
    {
        using var ui = new DockUi(L.TwoColumns(new Dictionary<string, string> { ["c"] = "fixed" }), configure: c => c.ShowCloseButtons = true);

        Assert.False(ui.Tab("c").HasButtonWithTip("Close"));
        Assert.True(ui.Tab("a").HasButtonWithTip("Close Text"));
    }

    [AvaloniaFact]
    public void CloseRequested_InterceptsCloseAndReceivesSlotIds()
    {
        var requests = new List<string[]>();
        using var ui = new DockUi(configure: c =>
        {
            c.ShowCloseButtons = true;
            c.CloseRequested = ids => { requests.Add([.. ids]); return Task.FromResult(false); };
        });

        ui.Tab("a").ButtonWithTip("Close Text").Invoke(); ui.Pump();

        Assert.Equal([["a"]], requests);
        Assert.Equal(3, ui.Workspace.Layout.Slots.Count());
    }

    [AvaloniaFact]
    public void CloseRequested_AlsoInterceptsMenuClose()
    {
        var requests = new List<string[]>();
        using var ui = new DockUi(configure: c => c.CloseRequested = ids => { requests.Add([.. ids]); return Task.FromResult(true); });

        ui.Tab("b").ContextMenu!.Item("Close").Invoke(); ui.Pump();

        Assert.Equal([["b"]], requests);
    }
}
