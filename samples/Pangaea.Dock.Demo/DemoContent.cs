using System.Collections.Immutable;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Styling;
using Pangaea.Dock.Avalonia;
using Pangaea.Dock.Core;
using Pangaea.Dock.Runtime;

namespace Pangaea.Dock.Demo;

public sealed class DemoSession : PanelSession
{
    public string Text { get; set; } = "Ideas for my workspace\n\n• Drag the Notes tab beside the Atlas.\n• Replace a panel without moving its tab.\n• Float a group onto another monitor.\n• Undo an arrangement with Ctrl+Alt+Z.\n\nYour scratch notes stay with this panel.";
    public double Zoom { get; set; } = 62;
    public override string CaptureState() => JsonSerializer.Serialize(new { Text, Zoom });
    public override void RestoreState(string json, int version)
    {
        if (version != 1) throw new InvalidDataException("Unknown demo panel state version.");
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty("Text", out var text)) Text = text.GetString() ?? "";
        if (document.RootElement.TryGetProperty("Zoom", out var zoom)) Zoom = zoom.GetDouble();
    }
}
public static class DemoContent
{
    public static (PanelRegistry Registry, PanelViewRegistry Views) Registrations()
    {
        var registry = new PanelRegistry(); var views = new PanelViewRegistry();
        void Add(PanelDescriptor descriptor, Func<DemoSession, Control> create)
        {
            registry.Register(descriptor, _ => new DemoSession()); views.Register(descriptor.Id, s => create((DemoSession)s));
        }
        Add(new("atlas", "Atlas", "Editors", "◉", MinWidth: 280, MinHeight: 180, Keywords: "map world viewport"), Atlas);
        Add(new("notes", "Notes", "Editors", "≡", MinWidth: 220, Keywords: "text writing scratchpad"), Notes);
        Add(new("inspector", "Inspector", "Tools", "◈", Singleton: true, CanDuplicate: false, MinWidth: 205), Inspector);
        Add(new("explorer", "Expedition", "Tools", "⌘", Singleton: true, CanDuplicate: false, MinWidth: 190), Explorer);
        Add(new("activity", "Activity", "Tools", "☷", MinWidth: 210), Activity);
        Add(new("swatches", "Palette", "Tools", "▦", MinWidth: 190), Swatches);
        return (registry, views);
    }
    public static DockLayout Layout(string preset = "Explore")
    {
        TabGroup Group(string id, params string[] types) => new(id, types.Select((t, i) => new DockSlot(id + "-" + i, id + "-panel-" + i)).ToImmutableArray(), id + "-0", id == "center" ? "primary" : null);
        var explorer = Group("explorer-group", "explorer"); var center = Group("center", "atlas", "notes");
        var inspector = Group("inspector-group", "inspector", "swatches"); var activity = Group("activity-group", "activity");
        LayoutNode root = preset == "Focus" ? center : new SplitNode("columns", SplitAxis.Horizontal,
            [explorer, new SplitNode("main-stack", SplitAxis.Vertical, [center, activity], [0.76, 0.24]), inspector], [0.2, 0.56, 0.24]);
        ImmutableArray<RailGroup> rails = preset == "Focus" ? [new(DockSide.Left, explorer, "center"), new(DockSide.Right, inspector, "center"), new(DockSide.Bottom, activity, "center")] : [];
        var groups = new[] { explorer, center, inspector, activity };
        var states = groups.SelectMany(g => g.Slots.Select((s, i) => new PanelState(s.InstanceId,
            g.Id == "center" ? (i == 0 ? "atlas" : "notes") : g.Id == "inspector-group" ? (i == 0 ? "inspector" : "swatches") : g.Id == "explorer-group" ? "explorer" : "activity")));
        return new("pangaea-demo", "main", [new("main", root, rails)], states.ToImmutableDictionary(p => p.Id));
    }
    private static StackPanel Stack(string eyebrow, string title)
    {
        var stack = new StackPanel { Margin = new Thickness(18), Spacing = 12 };
        stack.Children.Add(new TextBlock { Text = eyebrow.ToUpperInvariant(), FontSize = 10, LetterSpacing = 2, Foreground = Brush.Parse("#7DA99F") });
        stack.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeight.SemiBold }); return stack;
    }
    private static Control Notes(DemoSession session)
    {
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Margin = new Thickness(18) };
        grid.Children.Add(new TextBlock { Text = "FIELD NOTES", FontSize = 10, LetterSpacing = 2, Opacity = 0.6, Margin = new Thickness(0, 0, 0, 12) });
        var text = new TextBox { Text = session.Text, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Background = Brushes.Transparent,
            BorderThickness = new Thickness(0), FontSize = 14, Padding = new Thickness(0), VerticalContentAlignment = VerticalAlignment.Top };
        text.TextChanged += (_, _) => session.Text = text.Text ?? ""; Grid.SetRow(text, 1); grid.Children.Add(text); return grid;
    }
    private static Control Inspector(DemoSession session)
    {
        var stack = Stack("Properties", "Continental shelf");
        stack.Children.Add(new TextBlock { Text = "A place for the details.", Opacity = 0.55, FontSize = 12 });
        stack.Children.Add(new Border { Height = 1, Background = Brush.Parse("#33404B"), Margin = new Thickness(0, 8) });
        stack.Children.Add(new TextBlock { Text = "Name", FontSize = 11, Opacity = 0.65 });
        stack.Children.Add(new TextBox { Text = "Laurentia", FontSize = 13 });
        stack.Children.Add(new TextBlock { Text = "Elevation", FontSize = 11, Opacity = 0.65 });
        var slider = new Slider { Minimum = 0, Maximum = 100, Value = session.Zoom };
        var value = new TextBlock { Text = $"{session.Zoom:0}%", FontSize = 12, Opacity = 0.7 };
        slider.PropertyChanged += (_, e) => { if (e.Property == Slider.ValueProperty) { session.Zoom = slider.Value; value.Text = $"{session.Zoom:0}%"; } };
        stack.Children.Add(slider); stack.Children.Add(value);
        stack.Children.Add(new CheckBox { Content = "Show coastlines", IsChecked = true });
        stack.Children.Add(new CheckBox { Content = "Show grid", IsChecked = true });
        stack.Children.Add(new TextBlock { Text = "Try replacing this panel with Notes.\nIts position stays exactly the same.", FontSize = 12, Opacity = 0.55, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 20, 0, 0) });
        return new ScrollViewer { Content = stack };
    }
    private static Control Explorer(DemoSession session)
    {
        var stack = Stack("Collection / 06", "Expedition");
        stack.Children.Add(new TextBox { Watermark = "Filter places…", FontSize = 12 });
        var tree = new TreeView { ItemsSource = new[]
        {
            new TreeViewItem { Header = "◉  Continental atlas", IsExpanded = true, ItemsSource = new[] { "   Laurentia", "   Avalonia", "   Baltica", "   Gondwana" } },
            new TreeViewItem { Header = "≡  Field notes", ItemsSource = new[] { "   Coastlines", "   Shelf formations" } },
            new TreeViewItem { Header = "▦  Resources", ItemsSource = new[] { "   Color studies", "   References" } }
        } };
        stack.Children.Add(tree);
        stack.Children.Add(new TextBlock { Text = "YOUR WORKSPACE,\nYOUR WAY.", FontSize = 11, LetterSpacing = 1.5, Opacity = 0.4, Margin = new Thickness(0, 30, 0, 0) });
        return new ScrollViewer { Content = stack };
    }
    private static Control Activity(DemoSession session)
    {
        var stack = new StackPanel { Margin = new Thickness(16, 12), Spacing = 9 };
        foreach (var (text, color) in new[] { ("●  All panels are ready to explore", "Accent"), ("   Drag a tab to an edge to split, or to the middle to group.", "Muted"), ("   Right-click any tab for Replace, Duplicate, Swap and Float.", "Muted") })
        {
            var line = new TextBlock { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap };
            line.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("Pangaea.Dock." + color)); stack.Children.Add(line);
        }
        return new ScrollViewer { Content = stack };
    }
    private static Control Swatches(DemoSession session)
    {
        var stack = Stack("Materials", "Earth & ocean"); var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (string color in new[] { "#67DBC0", "#80B9AD", "#496F69", "#D9BA84", "#B79766", "#677F9C", "#9AADC4", "#CF9F91", "#E5D8BE" })
            wrap.Children.Add(new Border { Width = 48, Height = 48, Margin = new Thickness(0, 0, 8, 8), CornerRadius = new CornerRadius(5), Background = Brush.Parse(color) });
        stack.Children.Add(wrap); return stack;
    }
    private static Control Atlas(DemoSession session)
    {
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(20) };
        var title = new StackPanel { Spacing = 5 };
        title.Children.Add(new TextBlock { Text = "PANGAEA  /  ATLAS", FontSize = 10, LetterSpacing = 2, Foreground = Brush.Parse("#78B9AA") });
        title.Children.Add(new TextBlock { Text = "Everything finds its place.", FontSize = 26, FontWeight = FontWeight.Light });
        grid.Children.Add(title); var atlas = new AtlasView(); Grid.SetRow(atlas, 1); grid.Children.Add(atlas);
        var footer = new TextBlock { Text = "ILLUSTRATIVE MAP     ·     DRAG THE TABS TO EXPLORE", FontSize = 10, LetterSpacing = 1.2, Opacity = 0.5 };
        Grid.SetRow(footer, 2); grid.Children.Add(footer); return grid;
    }
}

public sealed class AtlasView : Control
{
    public override void Render(DrawingContext context)
    {
        base.Render(context); double w = Bounds.Width, h = Bounds.Height;
        var gridPen = new Pen(Brush.Parse(ActualThemeVariant == ThemeVariant.Light ? "#DFE8E8" : "#233D42"), 1);
        for (double x = 12; x < w; x += 34) context.DrawLine(gridPen, new(x, 18), new(x, h - 15));
        for (double y = 18; y < h - 15; y += 34) context.DrawLine(gridPen, new(12, y), new(w - 12, y));
        double scale = Math.Min(w / 600, h / 360), x0 = (w - 550 * scale) / 2, y0 = (h - 330 * scale) / 2;
        void Island(string points, string fill)
        {
            var pairs = points.Split(' ').Select(s => s.Split(',')).Select(a => new Point(x0 + double.Parse(a[0], System.Globalization.CultureInfo.InvariantCulture) * scale, y0 + double.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture) * scale)).ToArray();
            var geometry = new StreamGeometry(); using (var c = geometry.Open()) { c.BeginFigure(pairs[0], true); foreach (var p in pairs.Skip(1)) c.LineTo(p); c.EndFigure(true); }
            context.DrawGeometry(Brush.Parse(fill), new Pen(Brush.Parse("#78B8A7"), 1.5), geometry);
        }
        Island("92,46 135,23 190,36 202,60 235,68 244,98 273,115 260,142 228,147 210,178 185,170 173,142 140,136 128,111 99,108 74,79", "#355E55");
        Island("232,155 270,139 294,161 294,186 322,207 315,240 292,261 274,288 247,270 231,230 211,207", "#466F60");
        Island("288,84 309,60 350,66 371,48 417,56 433,82 463,91 491,122 466,142 428,130 400,153 365,139 339,112 310,119", "#3B665D");
        Island("336,176 371,169 396,190 385,220 362,235 341,211", "#6F8060");
        Island("431,256 468,242 498,259 482,281 451,289", "#547B68");
        foreach (var (x,y) in new[] { (177d, 96d), (266d, 207d), (385d, 102d) })
        { var p = new Point(x0 + x * scale, y0 + y * scale); context.DrawEllipse(null, new Pen(Brush.Parse("#8067DBC0"), 1), p, 11, 11); context.DrawEllipse(Brush.Parse("#8FEACD"), null, p, 3, 3); }
    }
}
