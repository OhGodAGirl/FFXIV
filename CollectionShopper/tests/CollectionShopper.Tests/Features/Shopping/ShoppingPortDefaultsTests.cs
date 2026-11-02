namespace CollectionShopper.Tests;

public sealed class ShoppingPortDefaultsTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public void DefaultMissingCheckRequiresKnownUnsuppressedReadiness(bool known, bool suppress, bool expected)
    {
        // Arrange
        IShoppingContext context = new Context(new(known, suppress, false, "test"));
        // Act
        var result = context.IsStillMissing(ShoppingListPolicyTests.Item(1), 1);
        // Assert
        Assert.Equal(expected, result);
        Assert.Empty(context.DiagnosticSources);
    }

    [Fact]
    public void UninstrumentedMarketPortReturnsEmptyDiagnostics()
    {
        // Arrange
        using IShoppingMarketBoard board = new Board();
        // Act
        var listings = board.LastListingRequestChecks;
        var submission = board.LastSubmissionChecks;
        // Assert
        Assert.Empty(listings);
        Assert.Empty(submission);
    }

    private sealed class Context(ItemOwnership readiness) : IShoppingContext
    {
        public ulong CurrentContentId => 1;
        public uint CurrentWorldId => 1;
        public ShoppingRunSettings Settings => new(0, false, 0, false, false, 600);
        public IReadOnlySet<uint> CompletedItemIds => new HashSet<uint>();
        public ItemOwnership ReadPurchaseReadiness(ShoppingItem item, ulong runContentId) => readiness;
        public void LogStopped(ShoppingItem item, string reason) { }
        public void LogSearchRetry(ShoppingItem item, string reason) { }
        public void MarkPurchased(uint itemId, ulong purchaseContentId) { }
        public void LogRequested(ShoppingItem item, PurchaseListingCandidate listing) { }
        public void LogConfirmed(ShoppingItem item, ulong listingId, uint quantity) { }
        public void LogSkipped(ShoppingItem item, string reason) { }
    }

    private sealed class Board : IShoppingMarketBoard
    {
        public event Action? Updated { add { } remove { } }
        public event Action<ShoppingOfferings>? OfferingsReceived { add { } remove { } }
        public event Action<ShoppingPurchase>? ItemPurchased { add { } remove { } }
        public event Action? EventStreamFaulted { add { } remove { } }
        public bool IsOpen => false;
        public bool IsAvailable => false;
        public bool RequestListings(uint itemId, out int requestId) { requestId = -1; return false; }
        public bool TryReadListings(out uint itemId, out IReadOnlyList<PurchaseListingCandidate> listings)
        { itemId = 0; listings = []; return false; }
        public PurchaseListingCandidate? ReadListing(int index) => null;
        public bool SubmitPurchase(PurchaseListingCandidate expected) => false;
        public void Dispose() { }
    }
}
