using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Pangaea.Dock.Core;
using Pangaea.Dock.Demo;
using Pangaea.Dock.Runtime;

namespace Pangaea.Dock.Harness;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        AppBuilder.Configure<HarnessApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .UseSkia().SetupWithoutStarting();
        var output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/ui"); Directory.CreateDirectory(output);
        int count = 0;
        void Check(bool condition, string label) { if (!condition) throw new Exception("FAIL: " + label); count++; }
        var window = new DemoWindow(persist: false); window.Show();
        void Pump() { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); }
        void Capture(string name)
        {
            Pump(); using var frame = window.CaptureRenderedFrame() ?? throw new Exception("No rendered frame"); frame.Save(Path.Combine(output, name + ".png"));
        }
        bool Run(DockOperation op) => window.DockControl.RunAsync(op).GetAwaiter().GetResult();
        Pump(); Capture("01-explore-dark");
        var control = window.DockControl; var workspace = window.Workspace; var surface = control.Surfaces[workspace.Layout.MainHostId];
        Check(surface.Geometry!.Groups.Count == 4, "four real laid out groups");
        Check(surface.GroupViews["center"].Bounds.Width > 450, "central canvas is prioritized");
        Check(window.GetVisualDescendants().OfType<AtlasView>().Any(v => v.Bounds.Width > 100), "actual Atlas control rendered");
        var initial = workspace.Layout;
        var diagnostics = new List<string>();
        workspace.Diagnostic += diagnostics.Add;
        // Drag an already-selected tab, including the last tab in a non-primary group.
        foreach (var slotId in new[] { "center-0", "explorer-group-0", "activity-group-0", "inspector-group-0" })
        {
            var sourceGroup = workspace.Layout.Groups.First(g => g.Slots.Any(s => s.Id == slotId));
            var header = surface.GroupViews[sourceGroup.Id].TabHeaders[slotId];
            var start = header.TranslatePoint(new Point(25, 16), window)!.Value;
            var targetId = sourceGroup.Id == "center" ? "inspector-group" : "center";
            var end = surface.GroupViews[targetId].TranslatePoint(new Point(90, 140), window)!.Value;
            window.MouseDown(start, MouseButton.Left); Pump();
            window.MouseMove(end); Pump();
            Check(control.CurrentPreviewPlan is not null, "selected tab preview: " + slotId);
            window.MouseUp(end, MouseButton.Left); Pump();
            Capture("regression-" + slotId);
            Check(diagnostics.Count == 0, "no presenter failure: " + string.Join("; ", diagnostics));
            Check(surface.GroupViews.Count == LayoutTree.Groups(workspace.Layout.Hosts[0].Root).Count(), "all groups remain rendered after " + slotId);
            Check(surface.GroupViews.Values.All(g => g.Bounds.Width > 100 && g.Bounds.Height > 80 && g.GetVisualRoot() == window), "groups stay visible after " + slotId);
            Check(window.GetVisualDescendants().OfType<TextBlock>().All(t => !(t.Text ?? "").StartsWith("Panel failed to load:")), "all panel content reparents after " + slotId);
            workspace.UndoAsync().GetAwaiter().GetResult(); Pump();
            Check(surface.GroupViews.Count == 4 && diagnostics.Count == 0, "undo restores every group after " + slotId);
        }

        var splitter = surface.Geometry.Splitters.First(s => s.Axis == SplitAxis.Horizontal);
        var splitterAt = surface.TranslatePoint(new Point(splitter.Bounds.X + 3, splitter.Bounds.Y + 70), window)!.Value;
        var beforeResize = WorkspaceStore.Serialize(workspace.Layout);
        window.MouseDown(splitterAt, MouseButton.Left); window.MouseMove(splitterAt + new Vector(35, 0)); Pump();
        Check(control.CurrentPreviewPlan is not null && WorkspaceStore.Serialize(workspace.Layout) == beforeResize, "resize previews without committing");
        window.MouseUp(splitterAt + new Vector(35, 0), MouseButton.Left); Pump();
        Check(WorkspaceStore.Serialize(workspace.Layout) != beforeResize, "resize commits on release");
        workspace.UndoAsync().GetAwaiter().GetResult(); Pump();

        // Drive actual headless pointer input through the tab capture/preview/drop path.
        var notes = surface.GroupViews["center"].TabHeaders["center-1"];
        var press = notes.TranslatePoint(new Point(25, 16), window)!.Value;
        var destination = surface.GroupViews["inspector-group"].TranslatePoint(new Point(90, 140), window)!.Value;
        window.MouseDown(press, MouseButton.Left); Pump();
        window.MouseMove(destination); Pump();
        Check(control.CurrentPreviewPlan is not null, "real pointer drag produced preview plan");
        Check(workspace.Layout.Group("center").Slots.Length == 2, "preview did not mutate source tree");
        var preview = control.CurrentPreviewRectangle!.Value; Capture("02-drag-preview");
        window.MouseUp(destination, MouseButton.Left); Pump();
        Check(workspace.Layout.Group("inspector-group").Slots.Any(s => s.Id == "center-1"), "drop merged notes");
        var actual = surface.Geometry!.Groups["inspector-group"];
        Check(Math.Abs(preview.Width - actual.Width) < 1 && Math.Abs(preview.X - actual.X) < 1, "preview equals committed geometry");
        Check(workspace.UndoAsync().GetAwaiter().GetResult(), "undo actual drag"); Pump();

        // Split the notes below the Atlas. The immutable candidate supplies the exact rendered size.
        notes = surface.GroupViews["center"].TabHeaders["center-1"];
        press = notes.TranslatePoint(new Point(25, 16), window)!.Value;
        var center = surface.GroupViews["center"];
        destination = center.TranslatePoint(new Point(center.Bounds.Width / 2, center.Bounds.Height - 12), window)!.Value;
        window.MouseDown(press, MouseButton.Left); window.MouseMove(destination); Pump();
        Check(control.CurrentPreviewPlan is not null, "split preview");
        preview = control.CurrentPreviewRectangle!.Value;
        window.MouseUp(destination, MouseButton.Left); Pump();
        var newGroup = workspace.Layout.Groups.First(g => g.Slots.Any(s => s.Id == "center-1"));
        actual = surface.Geometry!.Groups[newGroup.Id];
        Check(newGroup.Id != "center" && Math.Abs(preview.Height - actual.Height) < 1, "split commit matches preview");
        Capture("03-split-notes");
        workspace.UndoAsync().GetAwaiter().GetResult(); Pump();

        notes = surface.GroupViews["center"].TabHeaders["center-1"];
        press = notes.TranslatePoint(new Point(25, 16), window)!.Value;
        window.MouseDown(press, MouseButton.Left); window.MouseMove(destination); Pump();
        var beforeCancel = WorkspaceStore.Serialize(workspace.Layout);
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None); window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        window.MouseUp(destination, MouseButton.Left); Pump();
        Check(WorkspaceStore.Serialize(workspace.Layout) == beforeCancel, "Escape cancels drag without layout mutation");

        var slot = workspace.Layout.Slot("inspector-group-0"); var originalRect = surface.Geometry!.Groups["inspector-group"];
        Check(Run(new AddPanel(new(Ids.New(), "notes"), "inspector-group", slot.Id)), "replace through presentation transaction"); Pump();
        Check(surface.Geometry!.Groups["inspector-group"] == originalRect, "replacement preserves group bounds");
        Capture("04-replacement"); workspace.UndoAsync().GetAwaiter().GetResult(); Pump();

        Check(Run(new CollapseGroup("inspector-group", DockSide.Right)), "collapse"); Pump();
        Check(workspace.Layout.Hosts[0].Rails.Length == 1, "rail exists"); Capture("05-sidebar");
        var railButton = surface.GetVisualDescendants().OfType<Button>().First(b => (ToolTip.GetTip(b)?.ToString() ?? "").StartsWith("Open Inspector"));
        Check((railButton.Content as string ?? "").Contains("Inspector") && railButton.Bounds.Width > 60, "rail button names its panel instead of showing only a glyph");
        var at = railButton.TranslatePoint(new Point(15, 15), window)!.Value;
        window.MouseDown(at, MouseButton.Left); window.MouseUp(at, MouseButton.Left); Pump(); Capture("06-auto-hide-open");
        Check(surface.GetVisualDescendants().OfType<TextBox>().Any(t => t.Text == "Laurentia"), "rail click realized inspector content");
        Check(Run(new PinGroup("inspector-group")), "pin restores"); Pump();

        Check(Run(new FloatTabs(["center-1"])), "float"); Pump();
        Check(control.Surfaces.Count == 2, "native host is created on headless platform");
        var floating = control.Surfaces.First(p => p.Key != "main").Value;
        var floatingWindow = (Window)TopLevel.GetTopLevel(floating)!;
        using (var frame = floatingWindow.CaptureRenderedFrame()) frame?.Save(Path.Combine(output, "07-floating.png"));
        var floatingTab = floating.GroupViews.Values.Single().TabHeaders["center-1"];
        var floatPress = floatingTab.TranslatePoint(new Point(25, 16), floatingWindow)!.Value;
        // Target an exposed part of the main host, not a point occluded by the floating host.
        var returnView = surface.GroupViews["center"];
        var returnTarget = returnView.PointToScreen(new Point(returnView.Bounds.Width - 60, returnView.Bounds.Height / 2));
        var floatTarget = floatingWindow.PointToClient(returnTarget);
        floatingWindow.MouseDown(floatPress, MouseButton.Left); floatingWindow.MouseMove(floatTarget); Pump();
        Check(control.CurrentPreviewPlan is not null, "captured drag crosses headless top-level windows");
        floatingWindow.MouseUp(floatTarget, MouseButton.Left); Pump();
        Check(workspace.Layout.Group("center").Slots.Any(s => s.Id == "center-1"), "cross-host pointer drop commits");
        Check(Run(new MoveTabs(["center-1"], "center")), "dock back"); Pump();
        Check(control.Surfaces.Count == 1, "empty native host closed");

        workspace.ApplyArrangementAsync(DemoContent.Layout("Focus"), "Focus").GetAwaiter().GetResult(); Pump(); Capture("08-focus");
        Check(workspace.Layout.Panels.Count == initial.Panels.Count, "workspace switch retains all live panels");
        workspace.ApplyArrangementAsync(DemoContent.Layout(), "Explore").GetAwaiter().GetResult(); Pump();
        window.RequestedThemeVariant = ThemeVariant.Light; Pump(); Capture("09-explore-light");
        window.Width = 850; window.Height = 600; Pump(); Capture("10-small-window");
        Check(surface.GroupViews.Values.All(g => g.Bounds.Width >= 120 && g.Bounds.Height >= 90), "small window preserves panel minima");
        Check(control.Bounds.Width <= window.ClientSize.Width + 1, "workspace does not force the shell beyond the window");
        var guide = window.GetVisualDescendants().OfType<Button>().First(b => b.Content?.ToString() == "? Guide");
        var guideRight = guide.TranslatePoint(new Point(guide.Bounds.Width, guide.Bounds.Height), window)!.Value;
        Check(guideRight.X <= window.ClientSize.Width && guideRight.Y < window.ClientSize.Height, "toolbar wraps to keep commands accessible");
        Check(diagnostics.Count == 0, "no presenter errors throughout UI interactions: " + string.Join("; ", diagnostics));
        window.Close();
        Console.WriteLine($"PASS: {count} headless UI/interaction checks. Frames: {output}"); return 0;
    }
}
internal sealed class HarnessApp : DemoApp
{
    public override void OnFrameworkInitializationCompleted() { }
}
