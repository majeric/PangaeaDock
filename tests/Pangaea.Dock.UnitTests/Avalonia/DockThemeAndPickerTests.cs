using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Pangaea.Dock.Avalonia;
using Pangaea.Dock.Core;
using Pangaea.Dock.Runtime;
using Pangaea.Dock.Tests.Support;

namespace Pangaea.Dock.Tests.Avalonia;

public class DockThemeTests
{
    private static readonly string[] Tokens = ["Background", "Surface", "Hover", "Text", "Muted", "Accent", "Line"];

    [AvaloniaFact]
    public void Theme_DefinesEveryTokenForDarkAndLight()
    {
        var theme = new DockTheme();

        foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
            foreach (var token in Tokens)
            {
                Assert.True(theme.TryGetResource("Pangaea.Dock." + token, variant, out var value), $"{variant} {token}");
                Assert.IsAssignableFrom<IBrush>(value);
            }
        Assert.True(theme.TryGetResource("Pangaea.Dock.Preview", ThemeVariant.Dark, out var preview));
        Assert.IsAssignableFrom<IBrush>(preview);
    }

    [AvaloniaFact]
    public void Theme_DarkAndLightPalettesDiffer()
    {
        var theme = new DockTheme();

        theme.TryGetResource("Pangaea.Dock.Surface", ThemeVariant.Dark, out var dark);
        theme.TryGetResource("Pangaea.Dock.Surface", ThemeVariant.Light, out var light);

        Assert.NotEqual(((ISolidColorBrush)dark!).Color, ((ISolidColorBrush)light!).Color);
    }

    [AvaloniaFact]
    public void GroupView_UsesSurfaceBrushOfActiveVariant()
    {
        using var ui = new DockUi();

        foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            ui.Window.RequestedThemeVariant = variant; ui.Pump();
            new DockTheme().TryGetResource("Pangaea.Dock.Surface", variant, out var expected);

            Assert.Equal(((ISolidColorBrush)expected!).Color, ((ISolidColorBrush)ui.GroupView("left").Background!).Color);
        }
    }

    [AvaloniaFact]
    public void ConsumerResourceOverride_IsPickedUpByChrome()
    {
        using var ui = new DockUi();

        ui.Control.Resources["Pangaea.Dock.Surface"] = Brushes.HotPink; ui.Pump();

        Assert.Equal(Colors.HotPink, ((ISolidColorBrush)ui.GroupView("left").Background!).Color);
    }

    [AvaloniaFact]
    public void ActiveTab_UsesAccentUnderline()
    {
        using var ui = new DockUi();
        ui.Window.RequestedThemeVariant = ThemeVariant.Dark; ui.Pump();
        new DockTheme().TryGetResource("Pangaea.Dock.Accent", ThemeVariant.Dark, out var accent);

        var active = (Border)ui.Tab("a"); var inactive = (Border)ui.Tab("b");

        Assert.Equal(((ISolidColorBrush)accent!).Color, ((ISolidColorBrush)active.BorderBrush!).Color);
        Assert.NotEqual(((ISolidColorBrush)accent).Color, ((ISolidColorBrush)inactive.BorderBrush!).Color);
    }

    [AvaloniaFact]
    public void HeadlessFrame_RendersWithoutError()
    {
        using var ui = new DockUi();

        using var frame = ui.Window.CaptureRenderedFrame();

        Assert.NotNull(frame);
        Assert.Equal(1000, frame!.PixelSize.Width);
    }
}

public class PanelViewRegistryTests
{
    [AvaloniaFact]
    public void Register_DuplicateType_Throws()
    {
        var views = new PanelViewRegistry();
        views.Register("text", _ => new Border());

        Assert.Throws<ArgumentException>(() => views.Register("text", _ => new Border()));
    }

    [AvaloniaFact]
    public void Unregister_FallsBackToProviderUnavailablePlaceholder()
    {
        using var ui = new DockUi();
        ui.Views.Unregister("text");

        Assert.True(ui.Run(new AddPanel(new PanelState("n1", "text"), "right")));

        Assert.Contains(ui.GroupView("right").Texts(), t => t.StartsWith("Panel provider unavailable: text"));
    }

    [AvaloniaFact]
    public void Factory_ReceivesTheWorkspaceSession()
    {
        PanelSession? seen = null;
        using var ui = new DockUi(L.Build(L.Group("g", "a")), views: v => v.Register("text", s => { seen = s; return new Border(); }));

        Assert.Same(ui.Workspace.Session("p-a"), seen);
    }
}

public class PanelPickerTests
{
    private static Window OpenPicker(DockUi ui, Task task)
    {
        ui.Pump();
        Assert.False(task.IsCompleted);
        return ui.Window.OwnedWindows.Single();
    }

    private static string[] Results(Window dialog) =>
        dialog.Descendants<ListBox>().Single().Items.OfType<ListBoxItem>().Select(i => (string)i.Tag!).ToArray();

    [AvaloniaFact]
    public void Picker_ListsRegisteredPanelsSortedByCategoryThenTitle()
    {
        using var ui = new DockUi();
        var task = ui.Control.ShowPickerAsync();
        var dialog = OpenPicker(ui, task);

        Assert.Equal("Add panel", dialog.Title);
        Assert.Equal(["broken", "fixed", "other", "single", "text", "wide"], Results(dialog));
        dialog.Close(); ui.Pump();
        Assert.True(task.IsCompleted);
    }

    [AvaloniaFact]
    public void Picker_SearchFiltersByTitleCategoryAndKeywords()
    {
        using var ui = new DockUi();
        var dialog = OpenPicker(ui, ui.Control.ShowPickerAsync());

        dialog.Descendants<TextBox>().Single().Text = "single"; ui.Pump();

        Assert.Equal(["other", "single"], Results(dialog));
        dialog.Close();
    }

    [AvaloniaFact]
    public void Picker_EnterAddsSelectedPanelToPrimaryGroupAndCloses()
    {
        using var ui = new DockUi();
        var task = ui.Control.ShowPickerAsync();
        var dialog = OpenPicker(ui, task);
        dialog.Descendants<TextBox>().Single().Text = "wide"; ui.Pump();

        dialog.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None); ui.Pump();

        Assert.True(task.IsCompleted);
        Assert.Contains(ui.Workspace.Layout.Group("left").Slots, s => ui.Workspace.Layout.Panels[s.InstanceId].TypeId == "wide");
    }

    [AvaloniaFact]
    public void Picker_ReplaceModeReplacesTheChosenSlot()
    {
        using var ui = new DockUi();
        var task = ui.Control.ShowPickerAsync("right", "c");
        var dialog = OpenPicker(ui, task);
        Assert.Equal("Replace panel", dialog.Title);
        dialog.Descendants<TextBox>().Single().Text = "Other"; ui.Pump();

        dialog.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None); ui.Pump();

        Assert.Equal("other", ui.Workspace.Layout.Panels[ui.Workspace.Layout.Slot("c").InstanceId].TypeId);
        Assert.True(task.IsCompleted);
    }

    [AvaloniaFact]
    public void Picker_OpenSingletonIsMarkedAndChoosingItActivatesIt()
    {
        using var ui = new DockUi(L.TwoColumns(new Dictionary<string, string> { ["b"] = "single" }));
        var dialog = OpenPicker(ui, ui.Control.ShowPickerAsync("right"));
        dialog.Descendants<TextBox>().Single().Text = "Singleton"; ui.Pump();
        var list = dialog.Descendants<ListBox>().Single();
        list.SelectedItem = list.Items.OfType<ListBoxItem>().Single(i => (string)i.Tag! == "single");

        Assert.Contains(dialog.Texts(), t => t.Contains("Singleton") && t.Contains("Already open — activate"));
        Assert.DoesNotContain(dialog.Texts(), t => t.Contains("Other singleton") && t.Contains("Already open"));
        dialog.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None); ui.Pump();

        Assert.Equal("b", ui.Workspace.Layout.Group("left").ActiveSlotId);
        Assert.Single(ui.Workspace.Layout.Panels.Values, p => p.TypeId == "single");
    }

    [AvaloniaFact]
    public void Picker_FailedChoiceKeepsDialogOpenAndShowsReason()
    {
        using var ui = new DockUi();
        var task = ui.Control.ShowPickerAsync();
        var dialog = OpenPicker(ui, task);
        dialog.Descendants<TextBox>().Single().Text = "Broken"; ui.Pump();

        dialog.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None); ui.Pump();

        Assert.False(task.IsCompleted);
        Assert.Contains(dialog.Texts(), t => t.Contains("Factory failed on purpose"));
        dialog.Close();
    }

    [AvaloniaFact]
    public void Picker_EscapeCancels()
    {
        using var ui = new DockUi();
        var task = ui.Control.ShowPickerAsync();
        var dialog = OpenPicker(ui, task);

        dialog.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None); ui.Pump();

        Assert.True(task.IsCompleted);
        Assert.Equal(3, ui.Workspace.Layout.Slots.Count());
    }

    [AvaloniaFact]
    public void Picker_WithoutOwnerWindow_DoesNothing()
    {
        var f = new WorkspaceFixture(L.TwoColumns());
        var control = new DockWorkspaceControl(f.Workspace, new PanelViewRegistry());

        var task = control.ShowPickerAsync();

        Assert.True(task.IsCompleted);
        control.Dispose(); f.Dispose();
    }

    [AvaloniaFact]
    public void TabMenuReplace_OpensPickerInReplaceMode()
    {
        using var ui = new DockUi();

        ui.Tab("c").ContextMenu!.Item("Replace with…").Invoke(); ui.Pump();

        var dialog = ui.Window.OwnedWindows.Single();
        Assert.Equal("Replace panel", dialog.Title);
        dialog.Close();
    }

    [AvaloniaFact]
    public void EmptyGroupAddButton_OpensPickerForThatGroup()
    {
        using var ui = new DockUi(L.Build(L.H("h", L.Region("p", "primary"), L.Group("g", "a"))));

        ui.GroupView("p").ButtonWithTip("Add a panel").Invoke(); ui.Pump();
        var dialog = ui.Window.OwnedWindows.Single();
        dialog.Descendants<TextBox>().Single().Text = "wide"; ui.Pump();
        dialog.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None); ui.Pump();

        Assert.Single(ui.Workspace.Layout.Group("p").Slots);
    }
}
