namespace CollectionShopper.Tests;

public sealed class ShoppingCatalogProjectionTests
{
    [Fact]
    public void BuyingAnOutfitPieceDoesNotPromoteItsRemainingRowsUntilExplicitRefresh()
    {
        var outfits = new[] { Outfit(1, "A", 100_001, 100_002), Outfit(2, "B", 100_003, 100_004, 100_005) };
        var catalog = outfits.SelectMany(outfit => outfit.Items.Select(piece => Candidate(piece.Id, outfit.Id))).ToArray();
        var first = Build(outfits, catalog, [], [], []);
        var afterPurchase = Build(outfits, catalog, [], [100_003], first);
        var visible = ShoppingProjection.BuildVisible(
            ShoppingCatalogProjection.Project(afterPurchase, ExportaramusFixture.State(), Prices()),
            ShopperPage.Outfits, "", false, true, new HashSet<uint> { 100_003 }, 0, false);
        Assert.Equal([100_001U, 100_002U, 100_004U, 100_005U], visible.Select(item => item.ItemId));
        Assert.All(visible.Where(item => item.CollectionId == 2), item => Assert.Equal(2, item.RemainingPiecesInCollection));
        var refreshed = Build(outfits, catalog, [], [100_003], []);
        Assert.Equal(2U, refreshed[0].CollectionId);
    }

    [Fact]
    public void RestoredPieceKeepsItsDisplayedOutfitPriorityAndNewOutfitsAppend()
    {
        var outfits = new[] { Outfit(1, "A", 100_001, 100_002), Outfit(2, "B", 100_003), Outfit(3, "C", 100_004) };
        var catalog = outfits.SelectMany(outfit => outfit.Items.Select(piece => Candidate(piece.Id, outfit.Id))).ToArray();
        // Retained priorities can have gaps after earlier rows disappear.
        var first = Build(outfits, catalog, [100_001, 100_004], [], [])
            .Select((candidate, index) => candidate with { Priority = 10 + index * 10 }).ToArray();
        var next = Build(outfits, catalog, [], [], first);
        Assert.Equal(first.Single(candidate => candidate.ItemId == 100_002).Priority,
            next.Single(candidate => candidate.ItemId == 100_001).Priority);
        Assert.True(next.Single(candidate => candidate.ItemId == 100_004).Priority > first.Max(candidate => candidate.Priority));
    }

    [Fact]
    public void ProviderRemovalRestoresAnOutfitPieceFromRetainedMetadata()
    {
        var outfit = Outfit(1, "Set", 100_001, 100_002);
        var catalog = outfit.Items.Select(piece => Candidate(piece.Id, outfit.Id)).ToArray();
        var first = Build([outfit], catalog, [100_001], [], []);
        Assert.Equal([100_002U], first.Select(candidate => candidate.ItemId));

        var next = Build([outfit], catalog, [], [], first);
        Assert.Equal([100_002U, 100_001U], next.Select(candidate => candidate.ItemId));
        Assert.All(next, candidate => Assert.Equal(2, candidate.RemainingPieces));
        var projected = ShoppingCatalogProjection.Project(next, ExportaramusFixture.State(), Prices());
        Assert.All(projected, item => Assert.True(item.OwnershipKnown && !item.IsLocallyOwned));
        Assert.Equal(catalog[0].CollectionName, projected.Single(item => item.ItemId == 100_001).CollectionName);
    }

    [Fact]
    public void OwnedCompletedAndActiveRowsAreRetainedButDoNotAuthorizePurchases()
    {
        var outfit = Outfit(1, "Set", 100_001, 100_002);
        var catalog = outfit.Items.Select(piece => Candidate(piece.Id, 1)).ToArray();
        var first = Build([outfit], catalog, [], [], []);
        var next = Build([outfit], catalog, [100_001], [100_002], first);
        Assert.Equal(first, next);
        var ownership = ExportaramusFixture.State() with { TrustedOwnedItemIds = [100_001] };
        var items = ShoppingCatalogProjection.Project(next, ownership, Prices());
        var completed = new HashSet<uint> { 100_002 };
        Assert.All(items, item => Assert.False(PurchaseReadinessPolicy.CanContinue(
            item, ExportaramusFixture.ContentId, ExportaramusFixture.ContentId, ownership, completed)));
        Assert.Empty(ShoppingProjection.BuildVisible(items, ShopperPage.Outfits, "", true, true, completed, 0, false));
        Assert.Equal(items, ShoppingProjection.BuildVisible(items, ShopperPage.Outfits, "", false, false, completed, 100_001, false));
    }

    [Fact]
    public void TransientProviderLossCannotInferAbsenceAndRecoveryReclassifiesRetainedRows()
    {
        var outfit = Outfit(1, "Set", 100_001, 100_002);
        var catalog = outfit.Items.Select(piece => Candidate(piece.Id, 1)).ToArray();
        var candidates = Build([outfit], catalog, [100_001], [], []);
        var pending = Build([outfit], catalog, [], [], candidates);
        var unknown = ShoppingCatalogProjection.Project(pending, CollectionDataOwnershipPolicy.NotChecked, Prices());
        Assert.All(unknown, item => Assert.False(item.OwnershipKnown));
        Assert.All(unknown, item => Assert.False(PurchaseReadinessPolicy.CanContinue(
            item, ExportaramusFixture.ContentId, ExportaramusFixture.ContentId,
            CollectionDataOwnershipPolicy.NotChecked, new HashSet<uint>())));
        var recovered = ExportaramusFixture.State() with { TrustedOwnedItemIds = [100_001] };
        var current = ShoppingCatalogProjection.Project(Build([outfit], catalog, [100_001], [], pending), recovered, Prices());
        Assert.True(current.Single(item => item.ItemId == 100_001).IsLocallyOwned);
        Assert.False(current.Single(item => item.ItemId == 100_002).IsLocallyOwned);
    }

    [Fact]
    public void PriceAttachmentAndAllMetadataArePreservedByItemId()
    {
        var candidate = new ShoppingCandidate(100_001, "Name", 17, 99, "Collection", "ui/icon.tex",
            "7.5", "Source", ShoppingCategory.Minion, 14, 3, false);
        var quote = new MarketPriceQuote(new(100_001, 123, 1, "World", false), new(100_001, 456, 1, "World", true));
        var projected = ShoppingCatalogProjection.Project([candidate, candidate with { ItemId = 100_002 }],
            ExportaramusFixture.State(), new Dictionary<uint, MarketPriceQuote> { [100_001] = quote });
        Assert.Equal(new ShoppingItem(100_001, "Name", 99, "Collection", "ui/icon.tex", "7.5", "Source", 17,
            false, false, quote, ShoppingCategory.Minion, 14, 3, false), projected[0]);
        Assert.Null(projected[1].Prices);
        Assert.Equal(100_002U, projected[1].ItemId);
        Assert.Empty(ShoppingCatalogProjection.Project([], ExportaramusFixture.State(), Prices()));
    }

    [Fact]
    public void ReplanningUpdatesCountsWithoutMovingDisplayedOutfitsOrOtherCategories()
    {
        var outfits = new[] { Outfit(1, "A", 100_001), Outfit(2, "B", 100_002, 100_003) };
        var weapon = Candidate(100_004, 99) with { Category = ShoppingCategory.Weapon, Priority = 1 };
        var catalog = new[] { Candidate(100_001, 1), Candidate(100_002, 2), Candidate(100_003, 2), weapon };
        var first = Build(outfits, catalog, [], [], []);
        Assert.Equal(3, first.Single(candidate => candidate.ItemId == weapon.ItemId).Priority);
        var next = Build(outfits, catalog, [100_001, 100_002], [], first);
        Assert.Equal(3, next.Single(candidate => candidate.ItemId == weapon.ItemId).Priority);
        Assert.Equal(1, next.Single(candidate => candidate.ItemId == 100_003).Priority);
        Assert.Equal(1, next.Single(candidate => candidate.ItemId == 100_003).RemainingPieces);
        Assert.Equal(first.Select(candidate => candidate.ItemId), next.Select(candidate => candidate.ItemId));
    }

    [Fact]
    public void InitialProjectionMatchesThePreviousPlannerAcrossGeneratedOwnershipAndHistory()
    {
        var random = new Random(20260914);
        for (var iteration = 0; iteration < 128; iteration++)
        {
            var outfits = Enumerable.Range(1, 8).Select(index => Outfit((uint)index, $"Set {index}",
                (uint)(100_000 + index), (uint)(100_010 + index), 100_020)).ToArray();
            var catalog = outfits.SelectMany(outfit => outfit.Items.Where(piece => piece.Id % 3 != 0)
                .Select(piece => Candidate(piece.Id, outfit.Id))).Concat([
                    Candidate(100_030, 99) with { Category = ShoppingCategory.Weapon, Priority = 1 },
                    Candidate(100_031, 99) with { Category = ShoppingCategory.Minion, Priority = 2 },
                    Candidate(100_020, 99) with { Category = ShoppingCategory.Mount, Priority = 3 },
                ]).ToArray();
            var owned = catalog.Where(_ => random.Next(3) == 0).Select(candidate => candidate.ItemId).ToHashSet();
            var completed = catalog.Where(_ => random.Next(5) == 0).Select(candidate => candidate.ItemId).ToHashSet();
            var plans = OutfitPlanningPolicy.BuildPlans(outfits, owned, completed, id => id % 3 != 0);
            var expected = plans.SelectMany((plan, priority) => plan.EligibleMissingPieces.Select(piece =>
                catalog.First(candidate => candidate.Category == ShoppingCategory.Outfit
                    && candidate.CollectionId == plan.Outfit.Id && candidate.ItemId == piece.Id)
                    with
                { Priority = priority, RemainingPieces = plan.RemainingEligiblePieceCount }))
                .Concat(catalog.Where(candidate => candidate.Category != ShoppingCategory.Outfit)
                    .Select(candidate => candidate with { Priority = plans.Count + candidate.Priority }))
                .GroupBy(candidate => candidate.ItemId).Select(group => group.First()).ToArray();
            var actual = ShoppingCatalogProjection.BuildCandidates(outfits, catalog, owned, completed, []);
            Assert.Equal(expected, actual);
            Assert.Equal(actual, ShoppingCatalogProjection.BuildCandidates(outfits, catalog, owned, completed, actual));
        }
    }

    [Fact]
    public void DuplicateMetadataUsesFirstEntryAndCompletedMissingItemsStayOutOfNewCandidates()
    {
        var outfit = Outfit(1, "Set", 100_001, 100_002, 0);
        var first = Candidate(100_001, 1);
        var catalog = new[] { first, first with { ItemName = "Duplicate" }, Candidate(100_002, 1) };
        Assert.Equal(first with { RemainingPieces = 1 }, Assert.Single(Build([outfit], catalog, [], [100_002], [])));
        Assert.Empty(Build([], [], [], [], []));
        Assert.Empty(Build([outfit], catalog, [100_001], [100_002], []));
    }

    private static ShoppingCandidate[] Build(IReadOnlyList<Outfit> outfits, IReadOnlyList<ShoppingCandidate> catalog,
        uint[] owned, uint[] completed, IReadOnlyList<ShoppingCandidate> displayed)
        => ShoppingCatalogProjection.BuildCandidates(outfits, catalog, owned.ToHashSet(), completed.ToHashSet(), displayed);

    private static Outfit Outfit(uint id, string name, params uint[] items)
        => new(id, name, "7.5", null, null, items.Select(item => new OutfitPiece(item, $"Item {item}")).ToArray(), []);

    private static ShoppingCandidate Candidate(uint id, uint collectionId)
        => new(id, $"Item {id}", 17, collectionId, $"Set {collectionId}", null, "7.5", "Source", ShoppingCategory.Outfit, 0, 0);

    private static Dictionary<uint, MarketPriceQuote> Prices() => new Dictionary<uint, MarketPriceQuote>();
}
