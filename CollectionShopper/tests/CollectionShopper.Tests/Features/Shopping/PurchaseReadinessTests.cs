namespace CollectionShopper.Tests;

public sealed class PurchaseReadinessTests
{
    [Theory]
    [InlineData(0UL, 0UL, "Character identity changed or is unavailable.")]
    [InlineData(1UL, 2UL, "Character identity changed or is unavailable.")]
    [InlineData(42UL, 42UL, "Ownership snapshot belongs to another character.")]
    public void InvalidCharacterAuthorityCannotClaimOwnershipOrUnlocks(ulong active, ulong run, string reason)
    {
        var item = new ShoppingItem(99999, "Item", 99, "Unlock", null, "", "", 0, true, false,
            null, ShoppingCategory.Minion, 0, 1);
        Assert.Equal(new ItemOwnership(false, false, false, reason),
            PurchaseReadinessPolicy.Evaluate(item, active, run, ExportaramusFixture.State(), new HashSet<uint>()));
    }

    [Fact]
    public void QueuedItemMustStillBeMissingForTheSameCurrentCharacter()
    {
        var item = new ShoppingItem(99999, "Item", 99, "Unlock", null, "", "", 0, true, false,
            null, ShoppingCategory.Minion, 0, 1);
        var state = ExportaramusFixture.State();
        var contentId = ExportaramusFixture.ContentId;
        Assert.True(PurchaseReadinessPolicy.CanContinue(item, contentId, contentId, state, new HashSet<uint>()));
        Assert.False(PurchaseReadinessPolicy.CanContinue(item, 0, 0, state, new HashSet<uint>()));
        Assert.False(PurchaseReadinessPolicy.CanContinue(item, contentId, 42, state, new HashSet<uint>()));
        Assert.False(PurchaseReadinessPolicy.CanContinue(item, 42, 42, state, new HashSet<uint>()));
        Assert.False(PurchaseReadinessPolicy.CanContinue(item, contentId, contentId, CollectionDataOwnershipPolicy.NotChecked, new HashSet<uint>()));
        Assert.False(PurchaseReadinessPolicy.CanContinue(item, contentId, contentId, state, new HashSet<uint> { 99999 }));
        Assert.False(PurchaseReadinessPolicy.CanContinue(item with { CollectionId = 1 }, contentId, contentId, state, new HashSet<uint>()));
        Assert.False(PurchaseReadinessPolicy.CanContinue(item with { MappingValid = false }, contentId, contentId, state, new HashSet<uint>()));
        var withdrawn = state with { Snapshot = null };
        Assert.False(PurchaseReadinessPolicy.CanContinue(item, contentId, contentId, withdrawn, new HashSet<uint>()));
    }
    [Fact]
    public void ReadinessDistinguishesDuplicateEvidenceFromUnavailableAuthority()
    {
        var item = new ShoppingItem(99999, "Item", 99, "Unlock", null, "", "", 0, true, false,
            null, ShoppingCategory.Minion, 0, 1);
        var state = ExportaramusFixture.State();
        var id = ExportaramusFixture.ContentId;
        ItemOwnership Read(ShoppingItem candidate, CollectionDataOwnershipState ownership, params uint[] purchased)
            => PurchaseReadinessPolicy.Evaluate(candidate, id, id, ownership, purchased.ToHashSet());
        Assert.Equal(new ItemOwnership(true, true, false, "Purchase confirmed; waiting for Exportaramus to observe the item."), Read(item, state, 99999));
        Assert.True(Read(item with { CollectionId = 1 }, state).SuppressPurchase);
        var unavailable = Read(item, state with { Snapshot = null }, 99999);
        Assert.Equal(new ItemOwnership(false, false, false, state.StatusDetail), unavailable);
        Assert.False(Read(item with { MappingValid = false }, state).Known);
        Assert.False(Read(item, state).SuppressPurchase);
        Assert.Equal("Character identity changed or is unavailable.",
            PurchaseReadinessPolicy.Evaluate(item, 0, id, state, new HashSet<uint>()).Reason);
        Assert.Equal("Ownership snapshot belongs to another character.",
            PurchaseReadinessPolicy.Evaluate(item, 42, 42, state, new HashSet<uint>()).Reason);
    }
}
