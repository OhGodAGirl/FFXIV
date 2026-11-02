namespace BetterGlamorPlates.Tests.Features.GlamourPlates;

using BetterGlamorPlates.Features.GlamourPlates;

/// <summary>Verifies the pure saved-plate rail query.</summary>
public sealed class SavedPlateQueryTests
{
    /// <summary>Verifies trimmed, case-insensitive search and stable ordering.</summary>
    [Fact]
    public void ApplyFiltersAndOrdersWithoutMutatingTheLibrary()
    {
        var older = TestDomainValues.Plate("Raid Blue", 1);
        older.UpdatedAtUtc = TestDomainValues.Timestamp;
        var newer = TestDomainValues.Plate("raid Gold", 2);
        newer.UpdatedAtUtc = TestDomainValues.Timestamp.AddMinutes(1);
        var unrelated = TestDomainValues.Plate("Crafting", 3);
        var source = new[] { older, unrelated, newer };

        var result = SavedPlateQuery.Apply(source, " RAID ");

        Assert.Equal([older.Id, newer.Id], result.Select(plate => plate.Id));
        Assert.Equal([older.Id, unrelated.Id, newer.Id], source.Select(plate => plate.Id));
    }

    /// <summary>Verifies null arguments fail before enumeration.</summary>
    [Fact]
    public void ApplyRejectsNullSource()
        => Assert.Throws<ArgumentNullException>(() => SavedPlateQuery.Apply(null!, null));
}
