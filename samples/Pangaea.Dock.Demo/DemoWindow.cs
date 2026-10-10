using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Styling;
using Avalonia.Threading;
using Pangaea.Dock.Avalonia;
using Pangaea.Dock.Core;
using Pangaea.Dock.Runtime;

namespace Pangaea.Dock.Demo;

public sealed class DemoWindow : Window
{
    public DockWorkspace Workspace { get; }
    public DockWorkspaceControl DockControl { get; }
    private readonly TextBlock _status = new() { FontSize = 11, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Dictionary<string, DockLayout> _named = [];
    private readonly string? _storage;
    private readonly DispatcherTimer _autosave = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private bool _initializing = true;
    public DemoWindow(bool persist = true)
    {
        Title = "Pangaea Dock · Bring your panels together"; Width = 1320; Height = 850; MinWidth = 800; MinHeight = 520;
        Styles.Add(new DockTheme()); Bind(BackgroundProperty, new DynamicResourceExtension("Pangaea.Dock.Background"));
        _storage = persist ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PangaeaDock", "Demo") : null;
        var registrations = DemoContent.Registrations(); var initial = DemoContent.Layout();
        if (_storage is not null) initial = WorkspaceStore.LoadWithBackup(Path.Combine(_storage, "session.json")) ?? initial;
        Workspace = new(registrations.Registry, initial); DockControl = new(Workspace, registrations.Views);
        _named["Explore"] = DemoContent.Layout(); _named["Focus"] = DemoContent.Layout("Focus");
        if (_storage is not null && Directory.Exists(_storage))
            foreach (var file in Directory.EnumerateFiles(_storage, "workspace-*.json"))
                if (WorkspaceStore.LoadWithBackup(file) is { } saved) _named[Path.GetFileNameWithoutExtension(file)[10..]] = saved;
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto") };
        var banner = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(22, 18, 22, 16) };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        brand.Children.Add(new TextBlock { Text = "◈", FontSize = 34, Foreground = Brush.Parse("#67DBC0"), VerticalAlignment = VerticalAlignment.Center });
        var titles = new StackPanel { Spacing = 2 };
        titles.Children.Add(new TextBlock { Text = "PANGAEA DOCK", FontSize = 17, FontWeight = FontWeight.SemiBold, LetterSpacing = 2 });
        titles.Children.Add(new TextBlock { Text = "Bring your panels together.", FontSize = 12, Opacity = 0.55 }); brand.Children.Add(titles); banner.Children.Add(brand);
        var badge = new TextBlock { Text = "INTERACTIVE PREVIEW  /  0.1", FontSize = 10, LetterSpacing = 1.5, Opacity = 0.55, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(badge, 1); banner.Children.Add(badge); root.Children.Add(banner);
        var toolbar = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 0, 20, 8) };
        Button Button(string text, Action action)
        {
            var b = new Button { Content = text, Padding = new Thickness(12, 6), FontSize = 12 }; b.Click += (_, _) => action(); return b;
        }
        toolbar.Children.Add(Button("+ Add panel", () => _ = DockControl.ShowPickerAsync()));
        var undo = Button("↶ Undo layout", () => _ = Workspace.UndoAsync()); ToolTip.SetTip(undo, "Undo layout · Ctrl+Alt+Z");
        var redo = Button("↷", () => _ = Workspace.RedoAsync()); ToolTip.SetTip(redo, "Redo layout · Ctrl+Alt+Y");
        toolbar.Children.Add(undo); toolbar.Children.Add(redo);
        var workspaces = new ComboBox { Width = 140, ItemsSource = _named.Keys.ToArray(), SelectedIndex = 0, FontSize = 12 };
        toolbar.Children.Add(workspaces);
        workspaces.SelectionChanged += async (_, _) =>
        {
            if (!_initializing && workspaces.SelectedItem is string key) await Workspace.ApplyArrangementAsync(_named[key], key);
        };
        toolbar.Children.Add(Button("Save as…", async () =>
        {
            string? name = await AskNameAsync(); if (string.IsNullOrWhiteSpace(name)) return;
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) { _status.Text = "Please use a name without filename punctuation."; return; }
            DockControl.CaptureFloatingPositions(); _named[name] = Workspace.Capture();
            if (_storage is not null) SaveSafely(Path.Combine(_storage, "workspace-" + name + ".json"), _named[name]);
            _initializing = true; workspaces.ItemsSource = _named.Keys.ToArray(); workspaces.SelectedItem = name; _initializing = false;
            _status.Text = "Saved workspace “" + name + "”.";
        }));
        toolbar.Children.Add(Button("Export", async () =>
        {
            var file = await StorageProvider.SaveFilePickerAsync(new() { Title = "Export workspace", SuggestedFileName = "Pangaea-workspace.json", DefaultExtension = "json" });
            if (file?.TryGetLocalPath() is { } path) SaveSafely(path, Workspace.Capture());
        }));
        toolbar.Children.Add(Button("Import", async () =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new() { Title = "Import workspace", AllowMultiple = false });
            if (files.FirstOrDefault()?.TryGetLocalPath() is { } path)
                try { await Workspace.ApplyArrangementAsync(WorkspaceStore.Load(path), "imported layout"); }
                catch (Exception e) { _status.Text = "Import failed: " + e.Message; }
        }));
        toolbar.Children.Add(Button("Reset", () => _ = Workspace.ApplyArrangementAsync(DemoContent.Layout(), "Explore")));
        toolbar.Children.Add(Button("◐ Theme", () => { RequestedThemeVariant = ActualThemeVariant == ThemeVariant.Dark ? ThemeVariant.Light : ThemeVariant.Dark; DockControl.UpdateFloatingThemes(); }));
        toolbar.Children.Add(Button("? Guide", async () => await GuideAsync()));
        foreach (var item in toolbar.Children) item.Margin = new Thickness(0, 0, 8, 6);
        Grid.SetRow(toolbar, 1); root.Children.Add(toolbar); Grid.SetRow(DockControl, 2); root.Children.Add(DockControl);
        var statusRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(18, 9) };
        _status.Text = "Drag a tab to rearrange. Right-click for Replace, Swap, Duplicate and Float."; statusRow.Children.Add(_status);
        var independent = new TextBlock { Text = "INDEPENDENT AVALONIA LIBRARY", FontSize = 9, LetterSpacing = 1.2, Opacity = 0.4, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(independent, 1); statusRow.Children.Add(independent); Grid.SetRow(statusRow, 3); root.Children.Add(statusRow);
        DockControl.StatusChanged += message => _status.Text = message;
        Workspace.Changed += change =>
        {
            undo.IsEnabled = Workspace.CanUndo; redo.IsEnabled = Workspace.CanRedo;
            ToolTip.SetTip(undo, "Undo " + Workspace.UndoDescription + " · Ctrl+Alt+Z");
            if (_storage is not null) { _autosave.Stop(); _autosave.Start(); }
        };
        undo.IsEnabled = false; redo.IsEnabled = false;
        _autosave.Tick += (_, _) => { _autosave.Stop(); SaveSession(); };
        Closing += (_, _) => { _autosave.Stop(); DockControl.CaptureFloatingPositions(); SaveSession(); };
        Closed += (_, _) => { _autosave.Stop(); DockControl.Dispose(); Workspace.Dispose(); };
        Content = root; _initializing = false;
    }
    private void SaveSession()
    {
        if (_storage is not null) SaveSafely(Path.Combine(_storage, "session.json"), Workspace.Capture());
    }
    private void SaveSafely(string path, DockLayout layout)
    {
        try { WorkspaceStore.Save(path, layout); }
        catch (Exception e) { _status.Text = "Could not save workspace: " + e.Message; }
    }
    private async Task<string?> AskNameAsync()
    {
        var dialog = new Window { Title = "Save workspace", Width = 370, Height = 185, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var stack = new StackPanel { Margin = new Thickness(24), Spacing = 12 };
        stack.Children.Add(new TextBlock { Text = "Name this arrangement", FontSize = 17 });
        var input = new TextBox { Watermark = "My workspace" }; stack.Children.Add(input);
        var save = new Button { Content = "Save workspace", HorizontalAlignment = HorizontalAlignment.Right };
        save.Click += (_, _) => dialog.Close(input.Text); stack.Children.Add(save); dialog.Content = stack;
        return await dialog.ShowDialog<string?>(this);
    }
    private async Task GuideAsync()
    {
        var dialog = new Window { Title = "Pangaea Dock · Evaluation guide", Width = 560, Height = 475, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var text = new TextBlock { Margin = new Thickness(28), TextWrapping = TextWrapping.Wrap, FontSize = 14, LineHeight = 26,
            Text = "MAKE THE WORKSPACE YOURS\n\n1. Drag a tab to another panel’s center to group it.\n2. Drag near an edge to see the exact split before releasing.\n3. Drag the dotted grip to move an entire group.\n4. Right-click a tab to replace, duplicate, swap or float it.\n5. Use the group ⋯ menu to collapse to a sidebar.\n6. Click a sidebar icon to open it; pin it with ↗.\n7. Switch Explore / Focus; your live panel contents remain.\n8. Save an arrangement, export it, or undo a reset.\n\nCtrl+Alt+Z / Y — layout undo / redo\nEsc — cancel drag or close a sidebar\nTab, arrow keys, Enter and Shift+F10 — keyboard controls\n\nThis demo’s exports include scratch notes. Native multi-monitor and macOS behavior still need platform evaluation." };
        dialog.Content = new ScrollViewer { Content = text }; await dialog.ShowDialog(this);
    }
}
