using EZDelivery.Features.ExpertDelivery;

namespace EZDelivery.Tests.ExpertDelivery;

public sealed class DeliveryMappingBoundaryTests
{
    [Theory]
    [InlineData(10, 10, 0, 0)]
    [InlineData(2060, 2060, 0, 0)]
    [InlineData(11, 10, 0, 0)]
    [InlineData(11, 11, -1, 0)]
    [InlineData(11, 11, 3, 0)]
    [InlineData(11, 11, 0, -1)]
    [InlineData(11, 11, 0, 2049)]
    public void StructuralBoundsFailWithoutPartialSelection(int records, int order, int filter, int displayed)
    {
        Assert.False(DeliverySelectionMapping.TryResolve(new DeliverySelectionRecord[records], Enumerable.Range(0, order).ToArray(), filter, displayed, out var selected));
        Assert.Empty(selected);
    }

    [Fact]
    public void ExpertRecordInSupplyPrefixCannotBeSelected()
    {
        var records = new DeliverySelectionRecord[11]; records[0] = new(2, false, 1, 0, false);
        Assert.False(DeliverySelectionMapping.TryResolve(records, Enumerable.Range(0, 11).ToArray(), 0, 1, out var selected, out var reason));
        Assert.Empty(selected); Assert.Contains("Expert record in prefix", reason);
    }

    [Fact]
    public void GeneratedNativeFiltersPreserveSortedEligibleInstances()
    {
        var random = new Random(2420);
        int[] containers = [0, 1, 3199, 3200, 3209, 3210, 3300, 3301, 3400, 3401, 3500, 3501];
        for (var sample = 0; sample < 200; sample++)
        {
            var count = random.Next(1, 60);
            var records = Enumerable.Repeat(new DeliverySelectionRecord(0, false, 1, 0, false), 11)
                .Concat(Enumerable.Range(0, count).Select(_ => new DeliverySelectionRecord(2, random.Next(4) == 0, (uint)random.Next(0, 3), containers[random.Next(containers.Length)], random.Next(2) == 0))).ToArray();
            var order = Enumerable.Range(0, records.Length).OrderBy(_ => random.Next()).ToArray();
            for (var filter = 0; filter <= 2; filter++)
            {
                var expected = order.Where(index => index >= 11 && !records[index].Removed && records[index].Quantity > 0)
                    .Where(index => filter != 1 || !records[index].GearSet)
                    .Where(index => filter != 2 || !new[] { 3200, 3209, 3300, 3400, 3500 }.Contains(records[index].Container)).ToArray();
                Assert.True(DeliverySelectionMapping.TryResolve(records, order, filter, expected.Length, out var selected));
                Assert.Equal(expected, selected);
            }
        }
    }

    [Fact]
    public void SourceMappingRejectsEveryIdentityAndSizeViolation()
    {
        var source = new DeliverySource(0, 1, 100, 100, false, 1, 1);
        var row = new DeliveryRow(0, 11, source, true, 10, "Item");
        var invalid = new[]
        {
            row with { VisibleIndex = 1 }, row with { AgentIndex = 10 },
            row with { Source = source with { ItemId = 0 } }, row with { Source = source with { Quantity = 0 } },
        };
        foreach (var entry in invalid) Assert.False(DeliveryRules.ValidMapping([entry]));
        Assert.False(DeliveryRules.ValidMapping(Enumerable.Repeat(row, 2049).ToArray()));
        Assert.False(DeliveryRules.ValidMapping([row, row with { VisibleIndex = 1, Source = source with { Slot = 2 } }]));
        Assert.True(DeliveryRules.ExactRemoval(source, source with { ItemId = 0, Quantity = 0 }));
        Assert.False(DeliveryRules.ExactRemoval(source, source with { ItemId = 0, Quantity = 1 }));
        Assert.False(DeliveryRules.ExactRemoval(source with { Quantity = 2 }, null));
        Assert.False(DeliveryRules.SameInstance(source, null));
        Assert.False(DeliveryRules.RewardFits(0, 100, null));
    }
}
