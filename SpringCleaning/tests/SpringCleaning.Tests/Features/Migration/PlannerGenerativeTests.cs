namespace SpringCleaning.Tests.Features.Migration;

public sealed class PlannerGenerativeTests
{
    [Fact]
    public void SeededBroadInputsPreservePlannerInvariants()
    {
        var random = new Random(0x5A17C1EA);
        for (var iteration = 0; iteration < 1_000; iteration++)
        {
            var availableSlots = random.Next(0, 141);
            var eligible = Enumerable.Range(0, random.Next(0, 801))
                .Select(index => Eligible(index, random.Next(0, 13)))
                .ToArray();

            var selected = MigrationBatchPlanner.GetBatchPieceCount(eligible, availableSlots);
            var next = MigrationBatchPlanner.FindNextItem(eligible, availableSlots);

            Assert.InRange(selected, 0, availableSlots);
            Assert.Equal(
                eligible.FirstOrDefault(item =>
                    item.InventorySlotsRequired > 0
                    && item.InventorySlotsRequired <= availableSlots),
                next);
        }
    }

    [Fact]
    public void SeededPrismBudgetsNeverSelectMorePiecesThanAvailable()
    {
        var random = new Random(0x51A7);
        for (var iteration = 0; iteration < 1_000; iteration++)
        {
            var outfits = Enumerable.Range(0, random.Next(0, 41))
                .Select(index => Outfit(index, random.Next(0, 13)))
                .ToArray();
            var items = Enumerable.Range(0, random.Next(0, 141))
                .Select(Item)
                .ToArray();
            var prisms = (uint)random.Next(0, 201);

            var plan = DresserPrismBudgetPlanner.Plan(outfits, items, prisms);
            var total = outfits.Sum(outfit => outfit.Components.Count) + items.Length;

            Assert.InRange(plan.SelectedPieces, 0, (int)prisms);
            Assert.Equal(total, plan.SelectedPieces + plan.SkippedPieces);
            Assert.Equal(
                plan.SelectedPieces,
                plan.Outfits.Sum(outfit => outfit.Components.Count) + plan.Items.Count);
        }
    }

    [Fact]
    public void SeededOutfitSelectionIsDeterministicAndUsesDocumentedPriority()
    {
        var random = new Random(0xC1EA);
        for (var iteration = 0; iteration < 1_000; iteration++)
        {
            var candidates = Enumerable.Range(1, random.Next(1, 25))
                .Select(index => (uint)index)
                .OrderBy(_ => random.Next())
                .ToArray();
            var membership = candidates.ToDictionary(id => id, _ => random.Next(0, 20));
            var existing = candidates.Where(_ => random.Next(0, 2) == 0).ToHashSet();

            var selected = InventoryOutfitSetPlanner.ChooseSet(candidates, membership, existing);
            var expected = candidates
                .OrderByDescending(existing.Contains)
                .ThenByDescending(id => membership[id])
                .ThenBy(id => id)
                .First();

            Assert.Equal(expected, selected);
            Assert.Equal(selected, InventoryOutfitSetPlanner.ChooseSet(
                candidates.Reverse().ToArray(),
                membership,
                existing));
        }
    }

    [Fact]
    public void SeededDuplicateOutfitFilteringSkipsOnlyCompleteExistingCandidates()
    {
        var random = new Random(0x5E7F11);
        for (var iteration = 0; iteration < 1_000; iteration++)
        {
            var completePieces = random.Next(1, 12);
            var candidatePieces = random.Next(0, 14);
            var existing = random.Next(0, 2) == 0;
            var enabled = random.Next(0, 2) == 0;

            var skipped = InventoryOutfitSetPlanner.ShouldSkipCompleteDuplicate(
                enabled,
                existing,
                candidatePieces,
                completePieces);

            Assert.Equal(
                enabled && existing && candidatePieces >= completePieces,
                skipped);
        }
    }

    [Fact]
    public void DuplicateBulkSelectionNeverIncludesUnsafeOrMissingCopies()
    {
        var random = new Random(unchecked((int)0xD0011CA7));
        for (var iteration = 0; iteration < 1_000; iteration++)
        {
            var candidates = Enumerable.Range(0, random.Next(0, 141))
                .Select(index => Duplicate(
                    index,
                    random.Next(0, 2) == 0,
                    random.Next(0, 2) == 0,
                    (InventoryDuplicateDisposition)random.Next(0, 3)))
                .ToArray();

            var selected = InventoryDuplicatePlanner.SelectBulkDiscardCandidates(candidates);

            Assert.All(selected, candidate =>
            {
                Assert.Equal(InventoryDuplicateDisposition.RemovalReview, candidate.Disposition);
                Assert.True(candidate.Safety.IsPresent);
                Assert.True(candidate.Safety.IsSafe);
            });
            Assert.Equal(selected, InventoryDuplicatePlanner.SelectBulkDiscardCandidates(candidates));
        }
    }

    private static EligibleDresserItem Eligible(int index, int pieces) => new(
        index,
        (uint)index,
        $"Eligible {index}",
        Enumerable.Range(0, pieces)
            .Select(slot => new EligibleDresserComponent(slot, (uint)(index * 20 + slot), false, 1, "Piece"))
            .ToArray());

    private static InventoryDresserOutfit Outfit(int index, int pieces) => new(
        (uint)index,
        null,
        $"Outfit {index}",
        Enumerable.Range(0, pieces).Select(slot => Component(index, slot)).ToArray());

    private static InventoryDresserComponent Component(int index, int slot) => new(
        0,
        slot,
        slot,
        (uint)(index * 20 + slot),
        false,
        "Piece",
        0);

    private static InventoryDresserItem Item(int index) => new(0, index, (uint)index, false, "Item", 0);

    private static InventoryDuplicateItem Duplicate(
        int index,
        bool present,
        bool safe,
        InventoryDuplicateDisposition disposition) => new(
        0,
        index,
        (uint)index,
        false,
        "Duplicate",
        0,
        new InventoryItemSafety(present, safe, safe ? "Ready" : "Blocked"),
        false,
        disposition == InventoryDuplicateDisposition.RemovalReview,
        disposition);
}
