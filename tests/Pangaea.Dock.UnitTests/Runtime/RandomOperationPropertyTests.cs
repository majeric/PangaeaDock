using Pangaea.Dock.Core;
using Pangaea.Dock.Runtime;
using Pangaea.Dock.Tests.Support;

namespace Pangaea.Dock.Tests.Runtime;

/// <summary>
/// Seeded random sequences of float/collapse/move/split/pin operations. Every step must conserve slots and panel
/// bindings, validate, normalize idempotently, and the whole history must undo and redo back to the same layout.
/// </summary>
public class RandomOperationPropertyTests
{
    private const int Steps = 240;

    [Theory]
    [InlineData(20261010)]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(987654321)]
    public async Task RandomOperations_ConserveIdentityAndRoundTripThroughHistory(int seed)
    {
        using var f = new WorkspaceFixture();
        f.Workspace.HistoryLimit = Steps + 10;
        await f.AddTextPanels(8);
        f.Workspace.ClearHistory();
        var initial = Shape(f.Layout);
        var slotIds = f.Layout.Slots.Select(s => s.Id).ToHashSet();
        var instances = f.Layout.Panels.Keys.ToHashSet();
        var random = new Random(seed); int succeeded = 0;

        for (int i = 0; i < Steps; i++)
        {
            var operation = Next(f.Layout, random, i);
            var bindings = f.Layout.Slots.ToDictionary(s => s.Id, s => s.InstanceId);

            if (await f.Workspace.ExecuteAsync(operation)) succeeded++;

            LayoutTree.Validate(f.Layout);
            Assert.True(slotIds.SetEquals(f.Layout.Slots.Select(s => s.Id)), $"step {i}: slots conserved");
            Assert.All(f.Layout.Slots, s => Assert.Equal(bindings[s.Id], s.InstanceId));
            Assert.True(instances.SetEquals(f.Layout.Panels.Keys), $"step {i}: panels conserved");
            var normalized = LayoutTree.Normalize(f.Layout);
            Assert.Equal(WorkspaceStore.Serialize(normalized), WorkspaceStore.Serialize(LayoutTree.Normalize(normalized)));
        }
        // Rejections still preserve invariants, but most of the sequence must really change the layout.
        Assert.True(succeeded > Steps / 2, $"only {succeeded}/{Steps} operations succeeded");

        var final = Shape(f.Layout); int undone = 0;
        while (f.Workspace.CanUndo) { Assert.True(await f.Workspace.UndoAsync(), $"undo {undone}"); undone++; }
        Assert.Equal(initial, Shape(f.Layout));
        for (int i = 0; i < undone; i++) Assert.True(await f.Workspace.RedoAsync(), $"redo {i}");
        Assert.Equal(final, Shape(f.Layout));
        Assert.All(f.Sessions, s => Assert.False(s.Disposed));
    }

    private static DockOperation Next(DockLayout layout, Random random, int step)
    {
        var groups = layout.Groups.ToArray(); var slots = layout.Slots.ToArray();
        var rails = layout.Hosts.SelectMany(h => h.Rails).ToArray();
        return (step % 6) switch
        {
            0 => new FloatTabs([slots[random.Next(slots.Length)].Id]),
            1 => new CollapseGroup(groups[random.Next(groups.Length)].Id, (DockSide)random.Next(1, 5)),
            2 when rails.Length > 0 => new PinGroup(rails[random.Next(rails.Length)].Group.Id),
            _ => new MoveTabs([slots[random.Next(slots.Length)].Id], groups[random.Next(groups.Length)].Id, (DockSide)random.Next(5)),
        };
    }

    private static string Shape(DockLayout layout) => WorkspaceStore.Serialize(layout with { Revision = 0 });
}
