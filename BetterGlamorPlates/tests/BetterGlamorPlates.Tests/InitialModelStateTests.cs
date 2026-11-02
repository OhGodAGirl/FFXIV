using BetterGlamorPlates.Features.GlamourPlates;

namespace BetterGlamorPlates.Tests;

/// <summary>Initial state must not imply native readiness or hide unavailable ownership.</summary>
public sealed class InitialModelStateTests
{
    /// <summary>A new unsaved record has a readable name and exactly twelve empty positions.</summary>
    [Fact]
    public void NewRecordIsNamedAndEmpty()
    {
        var plate = new SavedPlate();
        Assert.False(string.IsNullOrWhiteSpace(plate.Name));
        Assert.Equal(12, plate.Items.Length);
        Assert.All(plate.Items, Assert.Null);
    }

    /// <summary>Empty native observations cannot authorize a plate write.</summary>
    [Fact]
    public void EmptyNativeStateHasNeitherSourceReady()
    {
        Assert.False(NativeCollectionSnapshot.Empty.IsDresserLoaded);
        Assert.False(NativeCollectionSnapshot.Empty.IsArmoireLoaded);
        Assert.Empty(NativeCollectionSnapshot.Empty.Items);
        var plan = PlateLoadPlanner.Create(TestDomainValues.Plate("Empty"), NativeCollectionSnapshot.Empty, (_, _) => true);
        Assert.Contains(plan.Issues, issue => issue.Kind == PlateLoadIssueKind.CollectionNotReady);
    }

    /// <summary>Unavailable initial ownership supplies source labels and useful guidance.</summary>
    [Fact]
    public void InitialDiagnosticsExplainUnknownOwnership()
    {
        var diagnostics = SharedCollectionDiagnostics.Empty;
        Assert.Contains("unknown", diagnostics.Guidance, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Glamour Dresser", diagnostics.GlamourDresser.Name);
        Assert.Equal("Armoire", diagnostics.Armoire.Name);
        foreach (var source in new[] { diagnostics.GlamourDresser, diagnostics.Armoire })
        {
            Assert.Equal("Not Available", source.ProviderStatus);
            Assert.False(string.IsNullOrWhiteSpace(source.Detail));
        }
        Assert.False(string.IsNullOrWhiteSpace(SharedPlateCompleteness.Empty.Summary));
        Assert.Equal(0, SharedPlateCompleteness.Empty.FilledSlots);
    }
}
