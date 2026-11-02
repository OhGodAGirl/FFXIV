namespace CollectionShopper.Tests;

public sealed class ShoppingSubmissionPolicyTests
{
    [Theory]
    [InlineData(0, 5)]
    [InlineData(1, 12)]
    [InlineData(2, 13)]
    public void NativeExceptionStopsSubmissionAndRetainsCompletedChecks(int failingStage, int completedChecks)
    {
        // Arrange
        var failure = new InvalidOperationException("native operation failed");
        var calls = new List<string>();
        IReadOnlyList<DiagnosticPredicate> checks = [];

        // Act
        var observed = Assert.Throws<InvalidOperationException>(() => ShoppingSubmissionPolicy.Submit(Valid, Expected,
            _ => { calls.Add("read"); if (failingStage == 0) throw failure; return Expected; },
            () => { calls.Add("prepare"); if (failingStage == 1) throw failure; return true; },
            () => { calls.Add("send"); throw failure; }, out checks));

        // Assert: no later native operation or retry is attempted. The adapter's
        // finally block can retain the checks completed before the exception.
        Assert.Same(failure, observed);
        Assert.Equal(failingStage == 0 ? ["read"] : failingStage == 1 ? ["read", "prepare"] : ["read", "prepare", "send"], calls);
        Assert.Equal(completedChecks, checks.Count);
        Assert.All(checks, check => Assert.True(check.Passed));
    }

    private static readonly string[] CheckNames = new[] { "Adapter active", "Market Board open", "Listing proxy available", "Search item matches",
            "Listing row in range", "Listing identity matches", "Listing item matches", "Listing price matches",
            "Listing tax matches", "Listing quantity matches", "Listing quality matches", "Listing mannequin status matches",
            "Native purchase prepared" };
    private static readonly PurchaseListingCandidate Expected = new(1, 42, 500, 100, 5, 1, false, false);
    private static readonly ShoppingSubmissionContext Valid = new(true, true, true, 42, 2, 2);

    [Fact]
    public void FirstListingRowCanBePurchased()
    {
        // Arrange
        var candidate = Expected with { SourceIndex = 0 };
        var calls = new List<string>();

        // Act
        var submitted = ShoppingSubmissionPolicy.Submit(Valid, candidate,
            index => { calls.Add($"read:{index}"); return candidate; },
            () => { calls.Add("prepare"); return true; },
            () => { calls.Add("send"); return true; }, out var checks);

        // Assert
        Assert.True(submitted);
        Assert.Equal(["read:0", "prepare", "send"], calls);
        Assert.All(checks, check => Assert.True(check.Passed));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void InvalidContextRejectsBeforeReadingOrPreparing(int failure)
    {
        var context = failure switch
        {
            0 => Valid with { Active = false },
            1 => Valid with { Open = false },
            2 => Valid with { ProxyAvailable = false },
            3 => Valid with { SearchItemId = 99 },
            4 => Valid with { ListingCount = 1 },
            5 => Valid with { Capacity = 1 },
            _ => Valid,
        };
        var expected = failure == 6 ? Expected with { SourceIndex = -1 } : Expected;

        var submitted = ShoppingSubmissionPolicy.Submit(context, expected,
            _ => throw new InvalidOperationException("Read after rejection"),
            () => throw new InvalidOperationException("Prepare after rejection"),
            () => throw new InvalidOperationException("Send after rejection"), out var checks);

        Assert.False(submitted);
        Assert.Equal(5, checks.Count);
        var failedIndex = failure switch { 0 => 0, 1 => 1, 2 => 2, 3 => 3, _ => 4 };
        Assert.False(checks[failedIndex].Passed);
        if (failedIndex < 4) Assert.Equal("False", checks[failedIndex].Observed);
        if (failure == 2)
        {
            Assert.Null(checks[3].Passed);
            Assert.Equal("Not observed", checks[3].Observed);
            Assert.Null(checks[4].Passed);
            Assert.Equal("Not observed", checks[4].Observed);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void EveryChangedCandidateFieldRejectsBeforePreparing(int field)
    {
        var actual = field switch
        {
            0 => Expected with { SourceIndex = 0 },
            1 => Expected with { ItemId = 43 },
            2 => Expected with { ListingId = 501 },
            3 => Expected with { UnitPrice = 101 },
            4 => Expected with { TotalTax = 6 },
            5 => Expected with { Quantity = 2 },
            6 => Expected with { IsHighQuality = true },
            _ => Expected with { IsMannequin = true },
        };
        var reads = new List<int>();

        var submitted = ShoppingSubmissionPolicy.Submit(Valid, Expected,
            index => { reads.Add(index); return actual; },
            () => throw new InvalidOperationException("Prepare after mismatch"),
            () => throw new InvalidOperationException("Send after mismatch"), out var checks);

        Assert.False(submitted);
        Assert.Equal([1], reads);
        Assert.Equal(12, checks.Count);
        var mismatchIndex = field switch { 1 => 6, 2 => 5, 3 => 7, 4 => 8, 5 => 9, 6 => 10, 7 => 11, _ => -1 };
        for (var index = 5; index < 12; index++)
        {
            Assert.Equal(index != mismatchIndex, checks[index].Passed);
            Assert.Equal(index == mismatchIndex ? "False" : "True", checks[index].Observed);
            Assert.Equal("True", checks[index].Expected);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void MatchingCandidatePreparesBeforeSendingAndReturnsNativeResult(bool prepared, bool sent)
    {
        var calls = new List<string>();

        var submitted = ShoppingSubmissionPolicy.Submit(Valid, Expected,
            index => { calls.Add($"read:{index}"); return Expected; },
            () => { calls.Add("prepare"); return prepared; },
            () => { calls.Add("send"); return sent; }, out var checks);

        Assert.Equal(prepared && sent, submitted);
        Assert.Equal(prepared ? ["read:1", "prepare", "send"] : ["read:1", "prepare"], calls);
        Assert.Equal(prepared ? 14 : 13, checks.Count);
        Assert.Equal(prepared, checks[12].Passed);
        if (prepared) Assert.Equal(sent, checks[13].Passed);
        Assert.All(checks.Take(12), check => Assert.True(check.Passed));
        Assert.Equal(CheckNames.Concat(prepared ? ["Native purchase submitted"] : []), checks.Select(check => check.Name));
        Assert.Equal("Row 1; count 2; capacity 2", checks[4].Observed);
        Assert.Equal("Valid row", checks[4].Expected);
        Assert.All(checks.Where((_, index) => index != 4), check => Assert.Equal("True", check.Expected));
        Assert.All(checks.Take(12).Where((_, index) => index != 4), check => Assert.Equal("True", check.Observed));
        Assert.Equal(prepared.ToString(), checks[12].Observed);
        if (prepared) Assert.Equal(sent.ToString(), checks[13].Observed);
    }
}
