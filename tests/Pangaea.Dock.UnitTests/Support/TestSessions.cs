using System.Text.Json;
using Pangaea.Dock.Core;
using Pangaea.Dock.Runtime;

namespace Pangaea.Dock.Tests.Support;

internal sealed class TestSession : PanelSession
{
    public string Text = "";
    public int DisposeCount;
    public bool Disposed => DisposeCount > 0;
    public Exception? ThrowOnDispose;
    public override string CaptureState() => JsonSerializer.Serialize(Text);
    public override void RestoreState(string json, int version)
    {
        if (json == "\"explode\"") throw new InvalidDataException("Restore failed on purpose");
        if (json.StartsWith('"')) Text = JsonSerializer.Deserialize<string>(json)!;
    }
    public override void Dispose() { DisposeCount++; if (ThrowOnDispose is not null) throw ThrowOnDispose; }
}

internal static class Registries
{
    /// <summary>Registers every <see cref="Catalog"/> descriptor plus a "broken" type whose factory throws.</summary>
    public static PanelRegistry Create(List<TestSession>? created = null)
    {
        var registry = new PanelRegistry();
        foreach (var d in Catalog.All.Values)
            registry.Register(d, _ => { var s = new TestSession(); created?.Add(s); return s; });
        registry.Register(new("broken", "Broken"), _ => throw new InvalidOperationException("Factory failed on purpose"));
        return registry;
    }
}

/// <summary>A workspace with recorded diagnostics and changes.</summary>
internal sealed class WorkspaceFixture : IDisposable
{
    public List<TestSession> Sessions { get; } = [];
    public List<string> Diagnostics { get; } = [];
    public List<WorkspaceChange> Changes { get; } = [];
    public DockWorkspace Workspace { get; }
    public WorkspaceFixture(DockLayout? initial = null)
    {
        Workspace = new DockWorkspace(Registries.Create(Sessions), initial);
        Workspace.Diagnostic += Diagnostics.Add; Workspace.Changed += Changes.Add;
    }
    public DockLayout Layout => Workspace.Layout;
    public string Primary => Layout.Groups.First().Id;
    public TestSession SessionOf(string slotId) => (TestSession)Workspace.Session(Layout.Slot(slotId).InstanceId);
    public async Task<string[]> AddTextPanels(int count)
    {
        for (int i = 0; i < count; i++) Assert.True(await Workspace.AddAsync("text", Primary));
        return Layout.Slots.Select(s => s.Id).ToArray();
    }
    public void Dispose() => Workspace.Dispose();
}
