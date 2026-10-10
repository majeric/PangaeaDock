using Pangaea.Dock.Core;
using Pangaea.Dock.Runtime;
using Pangaea.Dock.Tests.Support;

namespace Pangaea.Dock.Tests.Runtime;

public class PanelRegistryTests
{
    [Fact]
    public void Register_ExposesDescriptorById()
    {
        var registry = new PanelRegistry();

        registry.Register(Catalog.Text, _ => new TestSession());

        Assert.Same(Catalog.Text, registry.Descriptors["text"]);
    }

    [Fact]
    public void Register_NullFactory_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new PanelRegistry().Register(Catalog.Text, null!));
    }

    [Theory]
    [InlineData("", 160, 100)]
    [InlineData("  ", 160, 100)]
    [InlineData("x", 0.5, 100)]
    [InlineData("x", 160, 0)]
    [InlineData("x", double.NaN, 100)]
    [InlineData("x", 160, double.PositiveInfinity)]
    public void Register_InvalidMetadata_Throws(string id, double minWidth, double minHeight)
    {
        var descriptor = new PanelDescriptor(id, "Title", MinWidth: minWidth, MinHeight: minHeight);

        Assert.Throws<ArgumentException>(() => new PanelRegistry().Register(descriptor, _ => new TestSession()));
    }

    [Fact]
    public void Register_DuplicateId_Throws()
    {
        var registry = new PanelRegistry();
        registry.Register(Catalog.Text, _ => new TestSession());

        Assert.Throws<ArgumentException>(() => registry.Register(Catalog.Text, _ => new TestSession()));
    }

    [Fact]
    public void Unregister_RemovesDescriptorAndAllowsReRegistration()
    {
        var registry = new PanelRegistry();
        registry.Register(Catalog.Text, _ => new TestSession());

        registry.Unregister("text");

        Assert.Empty(registry.Descriptors);
        registry.Register(Catalog.Text, _ => new TestSession());
        Assert.Single(registry.Descriptors);
    }

    [Fact]
    public void Unregister_UnknownId_IsNoOp()
    {
        new PanelRegistry().Unregister("missing");
    }

    [Fact]
    public void Factory_ReceivesThePanelState()
    {
        var registry = new PanelRegistry(); PanelState? seen = null;
        registry.Register(Catalog.Text, s => { seen = s; return new TestSession(); });

        using var workspace = new DockWorkspace(registry, L.Build(L.Group("g", "a")));

        Assert.Equal("p-a", seen!.Id);
    }

    [Fact]
    public void Descriptor_DefaultsAllowEveryUserCapability()
    {
        var d = new PanelDescriptor("id", "Title");

        Assert.True(d.CanClose && d.CanFloat && d.CanCollapse && d.CanDuplicate);
        Assert.False(d.Singleton);
        Assert.Equal(160, d.MinWidth);
        Assert.Equal(100, d.MinHeight);
    }

    [Fact]
    public void MissingPanelSession_RoundTripsOriginalState()
    {
        var session = new MissingPanelSession("gone");

        session.RestoreState("{\"a\":1}", 3);

        Assert.Equal("gone", session.TypeId);
        Assert.Equal("{\"a\":1}", session.CaptureState());
    }

    [Fact]
    public void PanelSession_DefaultsCaptureEmptyObject()
    {
        using var session = new DefaultSession();

        session.RestoreState("ignored", 1);

        Assert.Equal("{}", session.CaptureState());
    }

    private sealed class DefaultSession : PanelSession;
}
