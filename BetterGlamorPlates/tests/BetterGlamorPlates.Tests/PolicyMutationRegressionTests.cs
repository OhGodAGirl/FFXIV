using BetterGlamorPlates.Features.Examine;
using BetterGlamorPlates.Features.GlamourPlates;
using BetterGlamorPlates.Features.Main;
using BetterGlamorPlates.Shared.CollectionData;

namespace BetterGlamorPlates.Tests;

/// <summary>Observable policy boundaries identified during mutation qualification.</summary>
public sealed class PolicyMutationRegressionTests
{
    /// <summary>Failure diagnostics always explain why collection data is unavailable.</summary>
    [Fact]
    public void EveryProviderFailureHasDiagnosticDetail()
    {
        foreach (var failure in Enum.GetValues<CollectionDataFailure>().Append((CollectionDataFailure)999))
        {
            var result = CollectionProviderPolicy.CreateDiagnostics(true, failure, null);
            Assert.False(string.IsNullOrWhiteSpace(result.GlamourDresser.Detail));
            Assert.Equal(result.GlamourDresser.Detail, result.Armoire.Detail);
        }
        var unknown = TestDomainValues.SharedSnapshot(glamourDresser: (CollectionSourceStatus)999);
        Assert.Equal("Unknown", CollectionProviderPolicy.CreateDiagnostics(true, CollectionDataFailure.None, unknown).GlamourDresser.ProviderStatus);
    }

    /// <summary>Current and last-known presence have different completeness copy.</summary>
    [Fact]
    public void CurrentCompletenessDoesNotClaimLastKnownPresence()
    {
        var plate = TestDomainValues.Plate("A", 20);
        var fresh = TestDomainValues.SharedSnapshot(dresserItems: [20]);
        Assert.Equal("1 of 1 collected; 0 not collected; 0 unknown.", CollectionProviderPolicy.CreatePlateCompleteness(plate, fresh).Summary);
        var stale = TestDomainValues.SharedSnapshot(armoire: CollectionSourceStatus.Stale);
        Assert.Equal("Collection ready from last-known data.", CollectionProviderPolicy.DescribeSummary(true, CollectionDataFailure.None, stale));
        Assert.Equal("Unavailable", CollectionProviderPolicy.DescribePrimaryStatus("Unavailable", 42));
        Assert.Equal("Collection ready \u00b7 42 owned", CollectionProviderPolicy.DescribePrimaryStatus("Collection ready.", 42));
        Assert.Equal("Collection partially available \u00b7 42 owned", CollectionProviderPolicy.DescribePrimaryStatus("Collection partially available.", 42));
        Assert.Throws<ArgumentException>(() => CollectionProviderPolicy.DescribePrimaryStatus(" ", 42));
    }

    /// <summary>Both dye channels determine closest-source preference independently.</summary>
    [Fact]
    public void ClosestSourceBalancesBothChannels()
    {
        var desired = new SavedPlateItem { ItemId = 20, Stain0Id = 2, Stain1Id = 3 };
        NativeCollectionItem[] items = [new(20, CollectionSource.Dresser, 0, 0, 0), new(20, CollectionSource.Dresser, 4, 2, 0), new(20, CollectionSource.Dresser, 9, 2, 3)];
        Assert.Equal(items[2], CollectionSourceResolver.ResolveClosest(items, desired));
        Assert.Equal(items[1], CollectionSourceResolver.ResolveClosest(items.Take(2), desired));
        Assert.Equal("plates", Assert.Throws<ArgumentNullException>(() => SavedPlateQuery.Apply(null!, null)).ParamName);
    }

    /// <summary>Reference geometry remains stable across responsive thresholds and scaling.</summary>
    [Fact]
    public void LayoutLandmarksAreExact()
    {
        var minimum = MainLayoutPolicy.Calculate(0, 0);
        Assert.Equal(224, minimum.RailWidth); Assert.Equal(566, minimum.MainWidth); Assert.Equal(206, minimum.CollectionPanelHeight);
        var middle = MainLayoutPolicy.Calculate(900, 700);
        Assert.Equal(270, middle.RailWidth); Assert.Equal(624, middle.MainWidth); Assert.Equal(7, middle.CollectionColumns);
        Assert.Equal(68, MainLayoutPolicy.FooterSize(650, false));
        var cell = MainLayoutPolicy.EquipmentCell(7, 600, 1.5f);
        Assert.Equal(100, cell.Position.X); Assert.Equal(213, cell.Position.Y); Assert.Equal(100, cell.Width); Assert.Equal(105, cell.IconSize);
        Assert.Equal(0, MainLayoutPolicy.CalculateRowCount(0, 3));
        Assert.Equal(0, MainLayoutPolicy.CalculateRowCount(1, 0));
        Assert.Equal(0, MainLayoutPolicy.CalculateRowCount(-1, 3));
        Assert.Equal(6, MainLayoutPolicy.CollectionGrid(600, 300, 10).Columns);
    }

    /// <summary>Examine accepts its exact supported name/list limits while ignoring invalid entries.</summary>
    [Fact]
    public void ExamineAcceptsLimitsAndIgnoresUnsupportedSlots()
    {
        ExaminedAppearance[] items = [new(14, 1, 0, 0, 0, 0, 0), new(14, 2, 0, 0, 0, 0, 0), new(1, 0, 0, 0, 0, 0, 0), new(1, 0, 0, 0, 0, 0, 0), new(0, 20, 0, 0, 0, 0, 0)];
        Assert.Null(ExaminedOutfitPolicy.Create(new string('A', 65), items, TestDomainValues.Timestamp, Guid.NewGuid()));
        var result = ExaminedOutfitPolicy.Create(new string('A', 64), items, TestDomainValues.Timestamp, Guid.NewGuid());
        Assert.NotNull(result); Assert.Equal(20u, Assert.Single(result.Items, item => item is not null)!.ItemId);
        var fourteen = Enumerable.Range(0, 14).Select(value => new ExaminedAppearance((byte)value, value == 0 ? 20u : 0u, 0, 0, 0, 0, 0)).ToArray();
        Assert.NotNull(ExaminedOutfitPolicy.Create("Name", fourteen, TestDomainValues.Timestamp, Guid.NewGuid()));
    }

    /// <summary>Each non-finite native bound rejects companion placement.</summary>
    [Fact]
    public void CompanionBoundsRejectEveryNonFiniteCoordinate()
    {
        WindowBounds[] invalid = [new(float.NaN, 0, 600, 900), new(0, float.NaN, 600, 900), new(0, 0, float.NaN, 900), new(0, 0, 600, float.NaN), new(0, 0, 600, 0)];
        foreach (var bounds in invalid) Assert.Null(CompanionPlacementPolicy.Calculate(bounds, new(0, 0, 1920, 1080)));
        Assert.Null(CompanionPlacementPolicy.Calculate(new(0, 0, 600, 900), new(0, 0, 8, 100)));
    }
    /// <summary>Companion docking prefers the right on ties and respects all viewport insets.</summary>
    [Fact]
    public void CompanionDockingRespectsSideAndDrawerReservation()
    {
        var equal = CompanionPlacementPolicy.Calculate(new(600, 0, 200, 900), new(0, 0, 1400, 1080));
        Assert.NotNull(equal); Assert.Equal(804, equal.Value.Position.X);
        Assert.Null(CompanionPlacementPolicy.Calculate(new(16, 0, 72, 100), new(0, 0, 100, 100)));
        var clamped = CompanionPlacementPolicy.Calculate(new(-100, 0, 50, 900), new(0, 100, 1920, 1080));
        Assert.NotNull(clamped); Assert.Equal(4, clamped.Value.Position.X); Assert.Equal(104, clamped.Value.Position.Y);
        var left = CompanionPlacementPolicy.Calculate(new(1200, 0, 600, 900), new(0, 0, 1920, 1080), 196);
        Assert.NotNull(left); Assert.Equal(4, left.Value.Position.X, 2);
        Assert.Equal(1196, left.Value.Position.X + left.Value.Size.X + 196 * left.Value.Scale, 2);
        var offRight = CompanionPlacementPolicy.Calculate(new(2000, 0, 600, 900), new(0, 0, 1920, 1080));
        Assert.NotNull(offRight); Assert.True(offRight.Value.Position.X + offRight.Value.Size.X <= 1916);
    }

    /// <summary>Undyed loads remove both stored channels while preserving the local appearance.</summary>
    [Fact]
    public void UndyedLoadClearsBothChannelsInDesiredPlan()
    {
        var plate = TestDomainValues.Plate("A", 20);
        plate.Items[3]!.Stain0Id = 7; plate.Items[3]!.Stain1Id = 9;
        var plan = PlateLoadPlanner.Create(plate, new([new(20, CollectionSource.Dresser, 0, 7, 9)], true, true, TestDomainValues.Timestamp), (_, _) => true, PlateDyeMode.None);
        Assert.Equal(0, plan.Steps[3].SavedItem!.Stain0Id);
        Assert.Equal(0, plan.Steps[3].SavedItem!.Stain1Id);
        Assert.Equal(9, plate.Items[3]!.Stain1Id);
    }

    /// <summary>Disposal cancels a pending preview before any adapter callback.</summary>
    [Fact]
    public void DisposingPendingPreviewCancelsIt()
    {
        var request = new OutfitPreviewRequest();
        var binding = new OutfitPreviewBinding(1, Guid.NewGuid(), 1, PlateDyeMode.Saved);
        request.Start(binding, [], 0);
        Assert.True(request.IsPending);
        request.Dispose();
        Assert.False(request.IsPending);
        Assert.Equal(OutfitPreviewProgress.Idle, request.Advance(binding, 0, (_, _) => throw new InvalidOperationException()));
        Assert.Null(CompanionPlacementPolicy.Calculate(new(0, 0, 620, 900), new(float.NaN, 0, 1920, 1080)));
    }

    /// <summary>Native plans reject an extra duplicate slot even if twelve distinct slots remain.</summary>
    [Fact]
    public void NativePlanRejectsThirteenSteps()
    {
        var steps = Enum.GetValues<PlateSlot>().Select(slot => new PlateLoadStep(slot, null, null)).ToArray();
        Assert.False(PlateResultPolicy.IsExecutable(new(steps.Append(steps[0]).ToArray(), [])));
    }

    /// <summary>Equal native locators with conflicting colours retain the first equally close observation.</summary>
    [Fact]
    public void EquallyCloseDuplicateLocatorRetainsFirstObservation()
    {
        NativeCollectionItem first = new(20, CollectionSource.Dresser, 3, 7, 0);
        NativeCollectionItem next = new(20, CollectionSource.Dresser, 3, 0, 9);
        Assert.Same(first, CollectionSourceResolver.ResolveClosest([first, next], new SavedPlateItem { ItemId = 20, Stain0Id = 7, Stain1Id = 9 }));
    }

    /// <summary>An unsupported ownership enum cannot become the accessory aggregate.</summary>
    [Fact]
    public void InvalidAccessoryOwnershipDoesNotReplaceEmptySummary()
    {
        var result = EquipmentSummaryPolicy.Create([new(PlateSlot.Ears, "Ears", "Item", true, 1, (SharedOwnershipState)99, null)]);
        Assert.Equal(SharedOwnershipState.Empty, result.Ownership);
        Assert.Equal("Empty", result.Label);
    }

    /// <summary>An offscreen native window does not let the left companion exceed the viewport margin.</summary>
    [Fact]
    public void LeftCompanionClampsToViewportRightInset()
    {
        var placement = CompanionPlacementPolicy.Calculate(new(2000, 0, 500, 900), new(0, 0, 1000, 1000));
        Assert.NotNull(placement);
        Assert.Equal(4f, placement.Value.Position.X, 2);
        Assert.Equal(992f, placement.Value.Size.X, 2);
    }
}

