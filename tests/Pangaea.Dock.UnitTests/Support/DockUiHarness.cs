using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Pangaea.Dock.Avalonia;
using Pangaea.Dock.Core;
using Pangaea.Dock.Runtime;

[assembly: Avalonia.Headless.AvaloniaTestApplication(typeof(Pangaea.Dock.Tests.Support.TestApp))]

namespace Pangaea.Dock.Tests.Support;

public sealed class TestApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    // Skia rendering (not headless drawing) so text measures realistically and frames can be captured.
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApp>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>A disposable panel view that records which session it shows.</summary>
public sealed class PanelView : Border, IDisposable
{
    public string TypeId { get; }
    public int DisposeCount { get; private set; }
    internal PanelView(string typeId, PanelSession session)
    {
        TypeId = typeId; Tag = session; Background = global::Avalonia.Media.Brushes.Transparent;
        Child = new TextBlock { Text = "view:" + typeId };
    }
    public void Dispose() => DisposeCount++;
}

/// <summary>A shown window hosting a DockWorkspaceControl over a recorded workspace, driven by headless input.</summary>
internal sealed class DockUi : IDisposable
{
    public WorkspaceFixture F { get; }
    public DockWorkspace Workspace => F.Workspace;
    public PanelViewRegistry Views { get; } = new();
    public DockWorkspaceControl Control { get; }
    public Window Window { get; }
    public List<PanelView> CreatedViews { get; } = [];
    public List<string> Status { get; } = [];

    public DockUi(DockLayout? layout = null, double width = 1000, double height = 700, Action<DockWorkspaceControl>? configure = null,
        Action<PanelViewRegistry>? views = null)
    {
        F = new WorkspaceFixture(layout ?? L.TwoColumns());
        if (views is null)
            foreach (var type in Catalog.All.Keys)
                Views.Register(type, s => { var v = new PanelView(type, s); CreatedViews.Add(v); return v; });
        else views(Views);
        Control = new DockWorkspaceControl(F.Workspace, Views);
        Control.StatusChanged += Status.Add;
        configure?.Invoke(Control);
        Window = new Window { Width = width, Height = height, Content = Control };
        Window.Show(); Pump();
    }

    public DockSurface Main => Control.Surfaces[Workspace.Layout.MainHostId];
    public DockGroupView GroupView(string groupId) => Main.GroupViews[groupId];
    public Control Tab(string slotId) => Control.Surfaces.Values.SelectMany(s => s.GroupViews.Values)
        .First(g => g.TabHeaders.ContainsKey(slotId)).TabHeaders[slotId];

    public void Pump()
    {
        for (int i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
        Dispatcher.UIThread.RunJobs();
    }

    public Point At(Visual visual, double x, double y) => visual.TranslatePoint(new Point(x, y), Window)!.Value;
    public Point CenterOf(Visual visual) => At(visual, visual.Bounds.Width / 2, visual.Bounds.Height / 2);

    public void Press(Point p) { Window.MouseDown(p, MouseButton.Left); Pump(); }
    public void Move(Point p) { Window.MouseMove(p); Pump(); }
    public void Release(Point p) { Window.MouseUp(p, MouseButton.Left); Pump(); }
    public void Click(Point p) { Window.MouseDown(p, MouseButton.Left); Window.MouseUp(p, MouseButton.Left); Pump(); }
    public void Drag(Point from, Point to) { Press(from); Move(to); Release(to); }
    public void Key(PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        Window.KeyPressQwerty(key, modifiers);
        Window.KeyReleaseQwerty(key, modifiers);
        Pump();
    }

    public bool Run(DockOperation operation)
    {
        var task = Control.RunAsync(operation); Pump();
        Assert.True(task.IsCompleted, "Operation should complete synchronously without a release guard.");
        return task.Result;
    }

    public void Dispose()
    {
        Window.Close(); Control.Dispose(); F.Dispose();
    }
}

internal static class UiQueries
{
    public static IEnumerable<T> Descendants<T>(this Visual root) => root.GetVisualDescendants().OfType<T>();

    public static Button ButtonWithTip(this Visual root, string tipPrefix) =>
        root.Descendants<Button>().First(b => (ToolTip.GetTip(b)?.ToString() ?? "").StartsWith(tipPrefix, StringComparison.Ordinal));
    public static bool HasButtonWithTip(this Visual root, string tipPrefix) =>
        root.Descendants<Button>().Any(b => (ToolTip.GetTip(b)?.ToString() ?? "").StartsWith(tipPrefix, StringComparison.Ordinal));

    public static IEnumerable<string> Texts(this Visual root) => root.Descendants<TextBlock>().Select(t => t.Text ?? "");

    public static MenuItem Item(this ItemsControl menu, params string[] path)
    {
        ItemsControl current = menu; MenuItem? found = null;
        foreach (var header in path)
        {
            found = current.Items.OfType<MenuItem>().FirstOrDefault(i => i.Header as string == header)
                ?? throw new Xunit.Sdk.XunitException($"No menu item '{header}' among: " +
                    string.Join(", ", current.Items.OfType<MenuItem>().Select(i => i.Header)));
            current = found;
        }
        return found!;
    }
    public static IEnumerable<string> Headers(this ItemsControl menu) => menu.Items.OfType<MenuItem>().Select(i => (string)i.Header!);

    public static void Invoke(this MenuItem item) => item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    public static void Invoke(this Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
}
