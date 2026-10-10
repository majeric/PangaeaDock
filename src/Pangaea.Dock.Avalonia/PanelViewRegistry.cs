using Avalonia.Controls;
using Pangaea.Dock.Runtime;

namespace Pangaea.Dock.Avalonia;

public sealed class PanelViewRegistry
{
    private readonly Dictionary<string, Func<PanelSession, Control>> _factories = [];
    public void Register(string typeId, Func<PanelSession, Control> factory) => _factories.Add(typeId, factory);
    public void Unregister(string typeId) => _factories.Remove(typeId);
    internal Control Create(string typeId, PanelSession session) => _factories.TryGetValue(typeId, out var factory)
        ? factory(session) : new TextBlock { Text = $"Panel provider unavailable: {typeId}\nUse Replace With to select another panel.",
            TextWrapping = global::Avalonia.Media.TextWrapping.Wrap, Margin = new(20) };
}
