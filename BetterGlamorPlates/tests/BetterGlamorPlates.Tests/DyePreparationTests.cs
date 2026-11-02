using BetterGlamorPlates.Features.GlamourPlates;

namespace BetterGlamorPlates.Tests;

/// <summary>Tests dye catalog and carried supply preparation from copied native facts.</summary>
public sealed class DyePreparationTests
{
    /// <summary>Only named, usable byte-sized stains become ordered options with reagent descriptions.</summary>
    [Fact]
    public void OptionsFilterOrderAndClassifyReagents()
    {
        // Arrange
        DyeRowFact Row(uint id, byte shade, byte order, string name) => new(id, shade, order, name, 0x123456, []);
        var rows = new[]
        {
            Row(0, 1, 1, "Zero"), Row(256, 1, 1, "Too large"), Row(1, 0, 1, "Hidden"), Row(2, 1, 1, ""),
            Row(3, 2, 0, "Special") with { Reagents = [new(9, "Rare"), new(0, "Ignored")] },
            Row(255, 1, 2, "Regular") with { Reagents = [new(52254, "Pigment"), new(10, "Other")] },
            Row(4, 1, 1, "First"),
        };
        // Act
        var options = DyePreparation.Options(rows);
        // Assert
        Assert.Equal(new byte[] { 4, 255, 3 }, options.Select(o => o.Id));
        Assert.Equal("First", options[0].Name);
        Assert.Equal(0x123456u, options[0].Color);
        Assert.False(options[1].SpecialReagent);
        Assert.True(options[2].SpecialReagent);
        Assert.Equal("Pigment / Other", options[1].Materials);
        Assert.Equal("Rare", options[2].Materials);
    }

    /// <summary>Catalog projection remains bounded even if an input enumerates duplicate records.</summary>
    [Fact]
    public void OptionsHaveHard255Bound()
    {
        var row = new DyeRowFact(1, 1, 1, "Dye", 0, []);
        var result = DyePreparation.Options(Enumerable.Repeat(row, 300));
        Assert.Equal(255, result.Length);
    }

    /// <summary>Only loaded occupied steps request reagent rows; shared reagents and repeated channels deduplicate.</summary>
    [Fact]
    public void ReagentMapFiltersStepsAndDeduplicatesSharedColours()
    {
        var item = new SavedPlateItem { ItemId = 100, Stain0Id = 5, Stain1Id = 6 };
        var source = new NativeCollectionItem(100, CollectionSource.Dresser, 1, 0, 0);
        var plan = new PlateLoadPlan([
            new(PlateSlot.Body, item, source), new(PlateSlot.Hands, item, source),
            new(PlateSlot.Head, item, null) { IsSkipped = true }, new(PlateSlot.Legs, null, null),
            new(PlateSlot.Feet, item, null),
        ], []);
        var requested = new List<byte>();
        var result = DyePreparation.ReagentMap(plan, id => { requested.Add(id); return [0u, 200u, 200u, (uint)(300 + id)]; });
        Assert.Equal(new byte[] { 5, 6 }, requested);
        Assert.Equal(new byte[] { 5, 6 }, result[200]);
        Assert.Equal(new byte[] { 5 }, result[305]);
        Assert.Equal(new byte[] { 6 }, result[306]);
        Assert.False(result.ContainsKey(0));
    }

    /// <summary>Each excluded step shape independently avoids requesting its distinct dye.</summary>
    [Fact]
    public void ExcludedStepShapesNeverRequestDistinctDyes()
    {
        var source = new NativeCollectionItem(100, CollectionSource.Dresser, 1, 0, 0);
        var valid = new SavedPlateItem { ItemId = 100, Stain0Id = 1, Stain1Id = 1 };
        var missing = new SavedPlateItem { ItemId = 100, Stain0Id = 2, Stain1Id = 2 };
        var skipped = new SavedPlateItem { ItemId = 100, Stain0Id = 3, Stain1Id = 3 };
        var plan = new PlateLoadPlan([
            new(PlateSlot.Body, valid, source),
            new(PlateSlot.Feet, missing, null),
            new(PlateSlot.Head, skipped, source) { IsSkipped = true },
            new(PlateSlot.Hands, null, source),
        ], []);
        var requested = new List<byte>();
        var result = DyePreparation.ReagentMap(plan, id => { requested.Add(id); return [1000u + id]; });
        Assert.Equal(new byte[] { 1 }, requested);
        Assert.Equal(new uint[] { 1001 }, result.Keys);
    }

    /// <summary>Missing stain rows produce no phantom dye source.</summary>
    [Fact]
    public void MissingReagentRowsStayEmpty()
    {
        var plan = new PlateLoadPlan([new(PlateSlot.Body, new() { ItemId = 1 }, new(1, CollectionSource.Armoire, 1, 0, 0))], []);
        Assert.Empty(DyePreparation.ReagentMap(plan, _ => []));
    }

    /// <summary>Native inventory bounds are checked before reading any slot.</summary>
    [Theory]
    [InlineData(false, 35, false)]
    [InlineData(true, -1, false)]
    [InlineData(true, 0, true)]
    [InlineData(true, 35, true)]
    [InlineData(true, 36, false)]
    public void InventoryBounds(bool loaded, int size, bool expected)
        => Assert.Equal(expected, DyePreparation.CanReadInventory(loaded, size));

    /// <summary>Only positive carried reagent stacks become supplies, preserving physical slot identity.</summary>
    [Fact]
    public void SuppliesPreserveAliasesLocatorAndQuantity()
    {
        var mapping = new Dictionary<uint, List<byte>> { [200] = [5, 6] };
        var facts = new DyeInventoryFact[] { new(3, 4, 200, 2), new(3, 5, 200, 0), new(3, 6, 200, -1), new(3, 7, 999, 5) };
        var result = DyePreparation.Supplies(mapping, facts);
        Assert.Equal([new DyeSupply(5, 200, 3, 4, 2), new DyeSupply(6, 200, 3, 4, 2)], result);
    }
}
