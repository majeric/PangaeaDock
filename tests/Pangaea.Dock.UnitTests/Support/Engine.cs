using Pangaea.Dock.Core;

namespace Pangaea.Dock.Tests.Support;

internal static class Engine
{
    public static DockLayout Apply(DockLayout before, DockOperation operation)
    {
        var result = DockEngine.Evaluate(before, operation, Catalog.All);
        Assert.True(result.Success, "Expected success but got: " + result.Error);
        return result.Plan!.Candidate;
    }
    public static string Reject(DockLayout before, DockOperation operation)
    {
        var result = DockEngine.Evaluate(before, operation, Catalog.All);
        Assert.False(result.Success, "Expected the operation to be rejected.");
        Assert.Null(result.Plan);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        return result.Error!;
    }
}
