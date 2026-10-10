using System.Text.Json;
using Pangaea.Dock.Core;
using Pangaea.Dock.Runtime;
using Pangaea.Dock.Tests.Support;

namespace Pangaea.Dock.Tests.Runtime;

public class WorkspaceStoreSerializationTests
{
    private static readonly DockLayout Rich = L.Build(
        L.H("cols", L.Region("left", "primary", "a", "b"), L.V("stack", L.Group("top", "c"), L.Group("bottom", "d"))),
        rails: [new(DockSide.Bottom, L.Group("rail", "e"), "left", 240)],
        floating: [L.Floating("float", L.Group("fg", "f"), new WindowPlacement(10, 20, 300, 200, 1.25, true))]);

    [Fact]
    public void Serialize_RoundTripsEveryLayoutFeature()
    {
        var json = WorkspaceStore.Serialize(Rich);

        var restored = WorkspaceStore.Deserialize(json);

        Assert.Equal(json, WorkspaceStore.Serialize(restored));
        Assert.Equal(240, restored.Hosts[0].Rails[0].Extent);
        Assert.True(restored.Hosts[1].Placement!.Maximized);
        Assert.Equal("primary", restored.Group("left").RegionKey);
    }

    [Fact]
    public void Serialize_WritesVersionedEnvelopeWithoutClrTypeNames()
    {
        var json = WorkspaceStore.Serialize(Rich);

        using var document = JsonDocument.Parse(json);
        Assert.Equal("pangaea-dock", document.RootElement.GetProperty("Format").GetString());
        Assert.Equal(1, document.RootElement.GetProperty("SchemaVersion").GetInt32());
        Assert.DoesNotContain("$type", json);
        Assert.DoesNotContain("Pangaea.Dock", json);
        Assert.Contains("\"kind\": \"split\"", json);
        Assert.Contains("\"kind\": \"tabs\"", json);
    }

    [Fact]
    public void Serialize_InvalidLayout_Throws()
    {
        Assert.Throws<InvalidDataException>(() => WorkspaceStore.Serialize(Rich with { MainHostId = "x" }));
    }

    [Fact]
    public void Deserialize_UnsupportedSchemaVersion_Throws()
    {
        var json = WorkspaceStore.Serialize(Rich).Replace("\"SchemaVersion\": 1", "\"SchemaVersion\": 99");

        var error = Assert.Throws<InvalidDataException>(() => WorkspaceStore.Deserialize(json));
        Assert.Contains("Unsupported", error.Message);
    }

    [Fact]
    public void Deserialize_ForeignFormat_Throws()
    {
        var json = WorkspaceStore.Serialize(Rich).Replace("\"pangaea-dock\"", "\"other-dock\"");

        Assert.Throws<InvalidDataException>(() => WorkspaceStore.Deserialize(json));
    }

    [Fact]
    public void Deserialize_JsonNull_Throws()
    {
        Assert.Throws<InvalidDataException>(() => WorkspaceStore.Deserialize("null"));
    }

    [Theory]
    [InlineData("{\"Format\":\"pangaea-dock\",\"SchemaVersion\":1,\"Layout\":null}")]
    [InlineData("{\"Format\":\"pangaea-dock\",\"SchemaVersion\":1,\"Layout\":{\"Id\":\"x\",\"MainHostId\":\"m\",\"Hosts\":[null],\"Panels\":{}}}")]
    [InlineData("{\"Format\":\"pangaea-dock\",\"SchemaVersion\":1,\"Layout\":{\"Id\":\"x\",\"MainHostId\":\"m\",\"Hosts\":[{\"Id\":\"m\",\"Root\":null,\"Rails\":[]}],\"Panels\":{}}}")]
    public void Deserialize_MalformedNullData_ThrowsInvalidData(string json)
    {
        Assert.Throws<InvalidDataException>(() => WorkspaceStore.Deserialize(json));
    }

    [Fact]
    public void Deserialize_NotJson_ThrowsJsonException()
    {
        Assert.ThrowsAny<JsonException>(() => WorkspaceStore.Deserialize("not json"));
    }

    [Fact]
    public void Deserialize_LargerThan5MiB_IsRejectedBeforeParsing()
    {
        var json = "\"" + new string('x', 5 * 1024 * 1024) + "\"";

        var error = Assert.Throws<InvalidDataException>(() => WorkspaceStore.Deserialize(json));
        Assert.Contains("5 MiB", error.Message);
    }

    [Fact]
    public void Deserialize_ExcessiveJsonDepth_Throws()
    {
        var json = new string('[', 100) + new string(']', 100);

        Assert.ThrowsAny<JsonException>(() => WorkspaceStore.Deserialize(json));
    }
}

public sealed class WorkspaceStoreFileTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "pangaea-store-tests", Guid.NewGuid().ToString("N"));
    private string PathOf(string name) => Path.Combine(_folder, name);
    private static readonly DockLayout Empty = DockLayout.Empty();

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
    }

    [Fact]
    public void Save_CreatesMissingDirectoryAndWritesLoadableFile()
    {
        var path = PathOf("nested/dir/layout.json");

        WorkspaceStore.Save(path, Empty with { Revision = 5 });

        Assert.Equal(5, WorkspaceStore.Load(path).Revision);
        Assert.False(File.Exists(path + ".bak"));
    }

    [Fact]
    public void Save_OverExistingFile_ReplacesItAndKeepsPreviousAsBackup()
    {
        var path = PathOf("layout.json");
        WorkspaceStore.Save(path, Empty);

        WorkspaceStore.Save(path, Empty with { Revision = 10 });

        Assert.Equal(10, WorkspaceStore.Load(path).Revision);
        Assert.Equal(0, WorkspaceStore.Load(path + ".bak").Revision);
    }

    [Fact]
    public void Save_LeavesNoTemporaryFiles()
    {
        var path = PathOf("layout.json");

        WorkspaceStore.Save(path, Empty); WorkspaceStore.Save(path, Empty);

        Assert.Empty(Directory.GetFiles(_folder, "*.tmp"));
    }

    [Fact]
    public void Save_InvalidLayout_ThrowsAndLeavesExistingFileUntouched()
    {
        var path = PathOf("layout.json");
        WorkspaceStore.Save(path, Empty with { Revision = 3 });

        Assert.Throws<InvalidDataException>(() => WorkspaceStore.Save(path, Empty with { MainHostId = "x" }));

        Assert.Equal(3, WorkspaceStore.Load(path).Revision);
    }

    [Fact]
    public void Save_OverCorruptFile_KeepsLastGoodBackup()
    {
        var path = PathOf("layout.json");
        WorkspaceStore.Save(path, Empty); WorkspaceStore.Save(path, Empty with { Revision = 1 });
        File.WriteAllText(path, "corrupt");

        WorkspaceStore.Save(path, Empty with { Revision = 2 });

        Assert.Equal(2, WorkspaceStore.Load(path).Revision);
        Assert.Equal(0, WorkspaceStore.Load(path + ".bak").Revision);
    }

    [Fact]
    public void Load_FileLargerThan5MiB_Throws()
    {
        var path = PathOf("big.json"); Directory.CreateDirectory(_folder);
        File.WriteAllText(path, new string(' ', 5 * 1024 * 1024 + 1));

        Assert.Throws<InvalidDataException>(() => WorkspaceStore.Load(path));
    }

    [Fact]
    public void LoadWithBackup_NoFiles_ReturnsNull()
    {
        var messages = new List<string>();

        Assert.Null(WorkspaceStore.LoadWithBackup(PathOf("missing.json"), messages.Add));
        Assert.Empty(messages);
    }

    [Fact]
    public void LoadWithBackup_ValidMainFile_IsUsed()
    {
        var path = PathOf("layout.json");
        WorkspaceStore.Save(path, Empty); WorkspaceStore.Save(path, Empty with { Revision = 7 });

        Assert.Equal(7, WorkspaceStore.LoadWithBackup(path)!.Revision);
    }

    [Fact]
    public void LoadWithBackup_CorruptMainFile_FallsBackToBackupAndReports()
    {
        var path = PathOf("layout.json");
        WorkspaceStore.Save(path, Empty); WorkspaceStore.Save(path, Empty with { Revision = 7 });
        File.WriteAllText(path, "not json");
        var messages = new List<string>();

        Assert.Equal(0, WorkspaceStore.LoadWithBackup(path, messages.Add)!.Revision);
        Assert.Contains(messages, m => m.StartsWith("Could not restore layout.json"));
    }

    [Fact]
    public void LoadWithBackup_UnsupportedSchemaInMainFile_FallsBackToBackup()
    {
        var path = PathOf("layout.json");
        WorkspaceStore.Save(path, Empty); WorkspaceStore.Save(path, Empty with { Revision = 3 });
        File.WriteAllText(path, WorkspaceStore.Serialize(Empty).Replace("\"SchemaVersion\": 1", "\"SchemaVersion\": 99"));

        Assert.Equal(0, WorkspaceStore.LoadWithBackup(path)!.Revision);
    }

    [Fact]
    public void LoadWithBackup_InvalidLayoutInMainFile_FallsBackToBackup()
    {
        var path = PathOf("layout.json");
        WorkspaceStore.Save(path, Empty); WorkspaceStore.Save(path, Empty with { Revision = 3 });
        File.WriteAllText(path, WorkspaceStore.Serialize(Empty).Replace("\"MainHostId\": \"main\"", "\"MainHostId\": \"nope\""));

        Assert.Equal(0, WorkspaceStore.LoadWithBackup(path)!.Revision);
    }

    [Fact]
    public void LoadWithBackup_OnlyBackupExists_UsesBackup()
    {
        var path = PathOf("layout.json");
        WorkspaceStore.Save(path, Empty); WorkspaceStore.Save(path, Empty with { Revision = 3 });
        File.Delete(path);

        Assert.Equal(0, WorkspaceStore.LoadWithBackup(path)!.Revision);
    }

    [Fact]
    public void LoadWithBackup_BothCorrupt_ReturnsNullAndReportsEach()
    {
        var path = PathOf("layout.json"); Directory.CreateDirectory(_folder);
        File.WriteAllText(path, "{"); File.WriteAllText(path + ".bak", "{");
        var messages = new List<string>();

        Assert.Null(WorkspaceStore.LoadWithBackup(path, messages.Add));
        Assert.Equal(2, messages.Count);
    }
}
