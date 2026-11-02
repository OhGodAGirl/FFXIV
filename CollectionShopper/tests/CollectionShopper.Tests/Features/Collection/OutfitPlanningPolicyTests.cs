namespace CollectionShopper.Tests;

/// <summary>
/// Verifies local ownership is authoritative when deriving missing outfit purchases.
/// </summary>
public sealed class OutfitPlanningPolicyTests
{
    /// <summary>
    /// Verifies every required planning dependency is guarded at the boundary.
    /// </summary>
    [Fact]
    public void BuildPlansRejectsMissingInputs()
    {
        var outfits = new[] { CreateOutfit(10, "Fixture Set", 100) };
        var owned = new HashSet<uint>();
        var completed = new HashSet<uint>();

        Assert.Equal("outfits", Assert.Throws<ArgumentNullException>(() =>
            OutfitPlanningPolicy.BuildPlans(null!, owned, completed, _ => true)).ParamName);
        Assert.Equal("locallyOwnedItemIds", Assert.Throws<ArgumentNullException>(() =>
            OutfitPlanningPolicy.BuildPlans(outfits, null!, completed, _ => true)).ParamName);
        Assert.Equal("completedItemIds", Assert.Throws<ArgumentNullException>(() =>
            OutfitPlanningPolicy.BuildPlans(outfits, owned, null!, _ => true)).ParamName);
        Assert.Equal("isMarketEligible", Assert.Throws<ArgumentNullException>(() =>
            OutfitPlanningPolicy.BuildPlans(outfits, owned, completed, null!)).ParamName);
    }

    /// <summary>
    /// Verifies each deterministic ordering key participates in plan order.
    /// </summary>
    [Fact]
    public void BuildPlansAppliesEveryOrderingKey()
    {
        var alpha = CreateOutfit(1, "Alpha", 10, 11, 12);
        var beta = CreateOutfit(2, "Beta", 20, 21, 22);
        var fewerRemaining = CreateOutfit(3, "Fewer", 30, 31);
        var unowned = CreateOutfit(4, "Unowned", 40);
        var aardvark = CreateOutfit(5, "Aardvark", 50, 51, 52);
        var owned = new HashSet<uint> { 10, 20, 21, 30, 50 };

        var plans = OutfitPlanningPolicy.BuildPlans(
            [unowned, fewerRemaining, beta, alpha, aardvark], owned, new HashSet<uint>(), _ => true);

        Assert.Equal([2U, 3U, 5U, 1U, 4U], plans.Select(plan => plan.Outfit.Id));
    }

    /// <summary>
    /// Verifies an outfit disappears once local and confirmed ownership covers every piece.
    /// </summary>
    [Fact]
    public void BuildPlansOmitsLocallyCompletedOutfit()
    {
        // Arrange
        var outfit = CreateOutfit(10, "Complete Set", 100, 101);
        IReadOnlySet<uint> locallyOwned = new HashSet<uint> { 100 };
        IReadOnlySet<uint> completed = new HashSet<uint> { 101 };

        // Act
        var plans = OutfitPlanningPolicy.BuildPlans(
            [outfit], locallyOwned, completed, _ => true);

        // Assert
        Assert.Empty(plans);
    }

    /// <summary>
    /// Verifies candidate outfit pieces remain available for review when ownership is unverified.
    /// </summary>
    [Fact]
    public void BuildPlansIncludesUnverifiedCandidatePieces()
    {
        var plans = OutfitPlanningPolicy.BuildPlans(
            [CreateOutfit(10, "Unknown Set", 100)],
            new HashSet<uint>(),
            new HashSet<uint>(),
            _ => true);

        var plan = Assert.Single(plans);
        Assert.Equal(100U, Assert.Single(plan.EligibleMissingPieces).Id);
    }

    /// <summary>
    /// Verifies only locally missing, Market Board-eligible pieces enter the plan.
    /// </summary>
    [Fact]
    public void BuildPlansIncludesOnlyEligibleMissingPieces()
    {
        // Arrange
        var outfit = CreateOutfit(10, "Mixed Set", 100, 101, 102);
        IReadOnlySet<uint> locallyOwned = new HashSet<uint> { 100 };
        IReadOnlySet<uint> completed = new HashSet<uint>();

        // Act
        var plans = OutfitPlanningPolicy.BuildPlans(
            [outfit], locallyOwned, completed, itemId => itemId == 101);

        // Assert
        var plan = Assert.Single(plans);
        Assert.Equal(1, plan.OwnedPieceCount);
        Assert.Equal(1, plan.RemainingEligiblePieceCount);
        Assert.Equal(101U, Assert.Single(plan.EligibleMissingPieces).Id);
    }

    /// <summary>
    /// Verifies outfits with only untradeable missing pieces do not create dead shopping rows.
    /// </summary>
    [Fact]
    public void BuildPlansOmitsOutfitWithoutEligibleMissingPieces()
    {
        // Arrange
        var outfit = CreateOutfit(10, "Untradeable Set", 100, 101);
        IReadOnlySet<uint> locallyOwned = new HashSet<uint> { 100 };
        IReadOnlySet<uint> completed = new HashSet<uint>();

        // Act
        var plans = OutfitPlanningPolicy.BuildPlans(
            [outfit], locallyOwned, completed, _ => false);

        // Assert
        Assert.Empty(plans);
    }

    /// <summary>
    /// Verifies partially owned outfits sort ahead of completely unowned outfits.
    /// </summary>
    [Fact]
    public void BuildPlansPrioritizesPartiallyOwnedOutfits()
    {
        // Arrange
        var unowned = CreateOutfit(10, "Unowned Set", 100, 101);
        var partial = CreateOutfit(11, "Partial Set", 200, 201);
        IReadOnlySet<uint> locallyOwned = new HashSet<uint> { 200 };
        IReadOnlySet<uint> completed = new HashSet<uint>();

        // Act
        var plans = OutfitPlanningPolicy.BuildPlans(
            [unowned, partial], locallyOwned, completed, _ => true);

        // Assert
        Assert.Equal([11U, 10U], plans.Select(plan => plan.Outfit.Id));
    }

    /// <summary>
    /// Verifies repeated item IDs in catalog data produce only one shopping piece.
    /// </summary>
    [Fact]
    public void BuildPlansDeduplicatesCatalogPieces()
    {
        // Arrange
        var outfit = CreateOutfit(10, "Duplicate Set", 100, 100);
        IReadOnlySet<uint> locallyOwned = new HashSet<uint>();
        IReadOnlySet<uint> completed = new HashSet<uint>();

        // Act
        var plans = OutfitPlanningPolicy.BuildPlans(
            [outfit], locallyOwned, completed, _ => true);

        // Assert
        var plan = Assert.Single(plans);
        Assert.Single(plan.EligibleMissingPieces);
        Assert.Equal(1, plan.RemainingEligiblePieceCount);
    }

    /// <summary>
    /// Verifies placeholder catalog pieces do not create an empty outfit plan.
    /// </summary>
    [Fact]
    public void BuildPlansOmitsOutfitWithOnlyPlaceholderPieces()
    {
        // Arrange
        var outfit = CreateOutfit(10, "Placeholder Set", 0);

        // Act
        var plans = OutfitPlanningPolicy.BuildPlans(
            [outfit], new HashSet<uint>(), new HashSet<uint>(), _ => true);

        // Assert
        Assert.Empty(plans);
    }

    /// <summary>
    /// Verifies generated plans remain deterministic, distinct, eligible, and unowned.
    /// </summary>
    [Fact]
    public void GeneratedPlansContainOnlyDistinctEligibleMissingPieces()
    {
        var random = new Random(20260901);
        for (var iteration = 0; iteration < 256; iteration++)
        {
            var outfits = Enumerable.Range(1, 24)
                .Select(index => CreateOutfit(
                    (uint)index,
                    $"Outfit {index:D2}",
                    Enumerable.Range(0, 6)
                        .Select(_ => (uint)random.Next(0, 128))
                        .ToArray()))
                .ToArray();
            var locallyOwned = Enumerable.Range(1, 127)
                .Where(_ => random.Next(5) == 0)
                .Select(value => (uint)value)
                .ToHashSet();
            var completed = Enumerable.Range(1, 127)
                .Where(_ => random.Next(7) == 0)
                .Select(value => (uint)value)
                .ToHashSet();

            var first = OutfitPlanningPolicy.BuildPlans(
                outfits, locallyOwned, completed, itemId => itemId % 2 == 0);
            var second = OutfitPlanningPolicy.BuildPlans(
                outfits, locallyOwned, completed, itemId => itemId % 2 == 0);

            Assert.Equal(
                first.Select(plan => (plan.Outfit.Id, string.Join(',', plan.EligibleMissingPieces.Select(piece => piece.Id)))),
                second.Select(plan => (plan.Outfit.Id, string.Join(',', plan.EligibleMissingPieces.Select(piece => piece.Id)))));
            Assert.All(first, plan =>
            {
                Assert.Equal(
                    plan.EligibleMissingPieces.Count,
                    plan.EligibleMissingPieces.Select(piece => piece.Id).Distinct().Count());
                Assert.All(plan.EligibleMissingPieces, piece =>
                {
                    Assert.NotEqual(0U, piece.Id);
                    Assert.Equal(0U, piece.Id % 2);
                    Assert.DoesNotContain(piece.Id, locallyOwned);
                    Assert.DoesNotContain(piece.Id, completed);
                });
            });
        }
    }

    private static Outfit CreateOutfit(uint id, string name, params uint[] itemIds)
        => new(
            id,
            name,
            "7.5",
            null,
            null,
            itemIds.Select(itemId => new OutfitPiece(itemId, $"Item {itemId}")).ToArray(),
            []);
}
