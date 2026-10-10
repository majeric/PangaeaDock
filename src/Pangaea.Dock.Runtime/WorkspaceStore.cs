using System.Text;
using System.Text.Json;
using Pangaea.Dock.Core;

namespace Pangaea.Dock.Runtime;

public sealed record WorkspaceEnvelope(string Format, int SchemaVersion, DockLayout Layout);
/// <summary>Versioned data-only layouts. Bounded input; temporary-sibling writes and last-good backup.</summary>
public static class WorkspaceStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, MaxDepth = 64 };
    public static string Serialize(DockLayout layout)
    {
        LayoutTree.Validate(layout);
        return JsonSerializer.Serialize(new WorkspaceEnvelope("pangaea-dock", 1, layout), Options);
    }
    public static DockLayout Deserialize(string json)
    {
        if (Encoding.UTF8.GetByteCount(json) > 5 * 1024 * 1024) throw new InvalidDataException("Workspace is larger than 5 MiB.");
        var envelope = JsonSerializer.Deserialize<WorkspaceEnvelope>(json, Options) ?? throw new InvalidDataException("Empty workspace.");
        if (envelope.Format != "pangaea-dock" || envelope.SchemaVersion != 1) throw new InvalidDataException("Unsupported workspace format/version.");
        LayoutTree.Validate(envelope.Layout); return envelope.Layout;
    }
    public static void Save(string path, DockLayout layout)
    {
        string json = Serialize(layout); string full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        string temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(json); stream.Write(bytes); stream.Flush(true);
            }
            // Keep .bak as the last good layout: a corrupt main file must never replace it.
            if (File.Exists(full)) File.Replace(temporary, full, IsReadable(full) ? full + ".bak" : null, true);
            else File.Move(temporary, full);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static DockLayout Load(string path)
    {
        var info = new FileInfo(path);
        if (info.Length > 5 * 1024 * 1024) throw new InvalidDataException("Workspace is larger than 5 MiB.");
        return Deserialize(File.ReadAllText(path));
    }
    public static DockLayout? LoadWithBackup(string path, Action<string>? diagnostic = null)
    {
        foreach (string candidate in new[] { path, path + ".bak" })
        {
            if (!File.Exists(candidate)) continue;
            try { return Load(candidate); }
            catch (Exception e) when (e is IOException or InvalidDataException or JsonException or NotSupportedException
                or ArgumentException or InvalidOperationException or UnauthorizedAccessException)
            { diagnostic?.Invoke("Could not restore " + Path.GetFileName(candidate) + ": " + e.Message); }
        }
        return null;
    }
    private static bool IsReadable(string path)
    {
        try { Load(path); return true; }
        catch { return false; }
    }
}
