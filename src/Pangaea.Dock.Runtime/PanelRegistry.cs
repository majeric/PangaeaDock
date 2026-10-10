using Pangaea.Dock.Core;

namespace Pangaea.Dock.Runtime;

/// <summary>Nonvisual panel lifetime. Document ownership and save/close decisions remain with the consumer.</summary>
public abstract class PanelSession : IDisposable
{
    public virtual string CaptureState() => "{}";
    public virtual void RestoreState(string json, int version) { }
    public virtual void Dispose() { }
}
public sealed class MissingPanelSession(string typeId) : PanelSession
{
    public string TypeId { get; } = typeId;
    public string OriginalState { get; set; } = "{}";
    public override string CaptureState() => OriginalState;
    public override void RestoreState(string json, int version) => OriginalState = json;
}
public sealed class PanelRegistry
{
    private readonly Dictionary<string, PanelDescriptor> _descriptors = [];
    private readonly Dictionary<string, Func<PanelState, PanelSession>> _factories = [];
    public IReadOnlyDictionary<string, PanelDescriptor> Descriptors => _descriptors;
    /// <summary>Remove a provider after its live and history-retained sessions have been released.</summary>
    public void Unregister(string typeId) { _descriptors.Remove(typeId); _factories.Remove(typeId); }
    public void Register(PanelDescriptor descriptor, Func<PanelState, PanelSession> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (string.IsNullOrWhiteSpace(descriptor.Id) || !double.IsFinite(descriptor.MinWidth) ||
            !double.IsFinite(descriptor.MinHeight) || descriptor.MinWidth < 1 || descriptor.MinHeight < 1)
            throw new ArgumentException("Panel metadata needs a stable ID and finite positive minimum size.");
        _descriptors.Add(descriptor.Id, descriptor); _factories.Add(descriptor.Id, factory);
    }
    internal PanelSession Create(PanelState state)
    {
        var session = _factories.TryGetValue(state.TypeId, out var factory) ? factory(state) : new MissingPanelSession(state.TypeId);
        try { session.RestoreState(state.State, state.StateVersion); return session; }
        catch { session.Dispose(); throw; }
    }
}
