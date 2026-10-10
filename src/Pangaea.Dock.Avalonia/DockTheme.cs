using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;

namespace Pangaea.Dock.Avalonia;

/// <summary>Neutral chrome resources. Consumers may override Pangaea.Dock.* keys; no application theme is installed.</summary>
public sealed class DockTheme : Styles
{
    public DockTheme()
    {
        Resources.ThemeDictionaries[ThemeVariant.Dark] = Palette("#171B22", "#202630", "#2C3441", "#E7ECF3", "#97A6B8", "#67DBC0", "#405366");
        Resources.ThemeDictionaries[ThemeVariant.Light] = Palette("#EDF1F4", "#FFFFFF", "#DFE7ED", "#1F2C3B", "#526579", "#087D69", "#A7B8C8");
        Resources["Pangaea.Dock.Preview"] = new SolidColorBrush(Color.Parse("#4467DBC0"));
    }
    private static ResourceDictionary Palette(string background, string surface, string hover, string text, string muted, string accent, string line) => new()
    {
        ["Pangaea.Dock.Background"] = Brush.Parse(background), ["Pangaea.Dock.Surface"] = Brush.Parse(surface),
        ["Pangaea.Dock.Hover"] = Brush.Parse(hover), ["Pangaea.Dock.Text"] = Brush.Parse(text),
        ["Pangaea.Dock.Muted"] = Brush.Parse(muted), ["Pangaea.Dock.Accent"] = Brush.Parse(accent),
        ["Pangaea.Dock.Line"] = Brush.Parse(line)
    };
    internal static void Resource(AvaloniaObject control, AvaloniaProperty property, string token) =>
        control.Bind(property, new DynamicResourceExtension("Pangaea.Dock." + token));
    internal static TextBlock Text(string text, double size = 12, string color = "Text")
    {
        var block = new TextBlock { Text = text, FontSize = size, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center };
        Resource(block, TextBlock.ForegroundProperty, color); return block;
    }
    internal static Button Button(string text, string tooltip, Action action)
    {
        var button = new Button { Content = text, Padding = new Thickness(8, 3), MinHeight = 26, FontSize = 12,
            Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        ToolTip.SetTip(button, tooltip); global::Avalonia.Automation.AutomationProperties.SetName(button, tooltip);
        button.Click += (_, _) => action(); return button;
    }
}
