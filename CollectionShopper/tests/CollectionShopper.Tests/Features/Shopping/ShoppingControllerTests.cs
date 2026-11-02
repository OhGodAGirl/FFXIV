namespace CollectionShopper.Tests;

/// <summary>Exercises the production purchase lifecycle with controlled native facts and time.</summary>
public sealed class ShoppingControllerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeListingRequestDiagnosticsSurviveRejectionOrTimeout(bool accepted)
    {
        using var f = new Fixture();
        f.Market.AcceptSearch = accepted;
        f.Market.LastListingRequestChecks = Enumerable.Range(0, 20)
            .Select(index => new DiagnosticPredicate($"Native request {index}", null, "Observed", $"Request {index}; waiting True"))
            .ToArray();
        Assert.True(f.Controller.Start([Item()]));
        f.Market.Tick();
        if (accepted)
        {
            f.Advance(6000);
            f.Advance(2000);
            f.Advance(6000);
        }
        var failure = Assert.IsType<DiagnosticFailure>(f.Controller.CaptureDiagnostics().LastFailure);
        var nativeChecks = failure.Checks.Where(check => check.Name.StartsWith("Native request ", StringComparison.Ordinal)).ToArray();
        Assert.Equal(16, nativeChecks.Length);
        Assert.All(nativeChecks, check => Assert.Null(check.Passed));
        Assert.Equal("Request 0; waiting True", nativeChecks[0].Observed);
        Assert.Contains(failure.Checks, check => check.Name == "Listing request accepted" && check.Passed == accepted);
        Assert.Empty(f.Market.Submissions);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void InvalidCapsRetainEachCapResult(bool itemUnlimited, bool runUnlimited)
    {
        using var f = new Fixture();
        f.Context.Settings = f.Context.Settings with
        {
            MaximumPricePerItem = 0,
            MaximumTotalSpend = 0,
            UnlimitedPricePerItem = itemUnlimited,
            UnlimitedTotalSpend = runUnlimited
        };
        Assert.False(f.Controller.Start([Item()]));
        var failure = Assert.IsType<DiagnosticFailure>(f.Controller.CaptureDiagnostics().LastFailure);
        Assert.Contains(failure.Checks, check => check.Name == "Per-item cap enabled value" && check.Passed == itemUnlimited);
        Assert.Contains(failure.Checks, check => check.Name == "Run cap enabled value" && check.Passed == runUnlimited);
    }

    [Theory]
    [InlineData(0UL, 1U)]
    [InlineData(1UL, 0U)]
    public void UnknownRunIdentityRetainsFailureWithoutExposingIdentifiers(ulong character, uint world)
    {
        using var f = new Fixture();
        f.Context.CurrentContentId = character;
        f.Context.CurrentWorldId = world;
        Assert.True(f.Controller.Start([Item()]));
        f.Market.Tick();
        var failure = Assert.IsType<DiagnosticFailure>(f.Controller.CaptureDiagnostics().LastFailure);
        Assert.Contains(failure.Checks, check => check.Passed == false);
        Assert.Empty(f.Market.Submissions);
    }

    [Fact]
    public void NativeSubmissionDiagnosticsRetainUnknownChecksAfterRejection()
    {
        using var f = new Fixture();
        f.Market.AcceptPurchase = false;
        f.Market.LastSubmissionChecks = [new("Proxy row", null, "Available", "Not observed")];
        Assert.True(f.Controller.Start([Item()]));
        f.PreparePurchase();
        var failure = Assert.IsType<DiagnosticFailure>(f.Controller.CaptureDiagnostics().LastFailure);
        Assert.Contains(failure.Checks, check => check.Name == "Proxy row" && check.Passed is null);
        Assert.Contains(failure.Checks, check => check.Name == "Purchase request accepted" && check.Passed == false);
    }

    [Fact]
    public void FailedConfirmationPredicatesSurviveStopNewRunAndDispose()
    {
        using var f = new Fixture();
        f.Reach(ShoppingPhase.WaitingForPurchase);
        f.Context.DiagnosticSources = [new("inventory", "stale", true, 0, "2026-09-13T00:00:00Z", null)];
        f.Market.Purchase(42, quantity: 2);
        var snapshot = f.Controller.CaptureDiagnostics();
        var failure = Assert.IsType<DiagnosticFailure>(snapshot.LastFailure);
        Assert.Equal("WaitingForPurchase", failure.Phase);
        Assert.Equal("Idle", snapshot.Phase);
        Assert.Equal(0, failure.ConfirmedActions);
        f.Context.DiagnosticSources = [];
        Assert.Equal("stale", Assert.Single(failure.Sources!).Status);
        Assert.Contains(failure.Checks, check => check.Name == "Confirmation quantity" && check.Passed == false && check.Expected == "1" && check.Observed == "2");
        Assert.Contains(failure.Checks, check => check.Name == "Confirmation item matches" && check.Passed == true);
        Assert.Empty(snapshot.Checks);
        var submissions = f.Market.Submissions.Count;
        f.Controller.Stop();
        Assert.True(f.Controller.Start([Item(43)]));
        f.Advance(50);
        Assert.Same(failure, f.Controller.CaptureDiagnostics().LastFailure);
        Assert.Equal(submissions, f.Market.Submissions.Count);
        f.Controller.Dispose();
        Assert.Same(failure, f.Controller.CaptureDiagnostics().LastFailure);
    }

    [Fact]
    public void LastFailureCountsOnlyConfirmedActionsAndExplainsRejectedRows()
    {
        using var f = new Fixture();
        Assert.True(f.Controller.Start([Item(), Item(43)]));
        f.PreparePurchase();
        f.Market.Purchase(42);
        f.Market.Listings = [Listing(43) with { Quantity = 2, IsHighQuality = true }];
        f.Advance(600);
        f.Market.Offer(43);
        f.Advance(250);
        var failure = Assert.IsType<DiagnosticFailure>(f.Controller.CaptureDiagnostics().LastFailure);
        Assert.Equal(1, failure.ConfirmedActions);
        Assert.Contains(failure.Checks, check => check.Name.EndsWith("quantity is one", StringComparison.Ordinal) && check.Passed == false && check.Observed == "2");
        Assert.Contains(failure.Checks, check => check.Name.EndsWith("quality allowed", StringComparison.Ordinal) && check.Passed == false);
        Assert.Single(f.Market.Submissions);
    }

    [Fact]
    public void SkipAndTimeoutKeepTheRejectingPredicateAndOriginalTimestamp()
    {
        using var f = new Fixture();
        f.Context.Settings = f.Context.Settings with { MaximumPricePerItem = 1 };
        Assert.True(f.Controller.Start([Item()]));
        f.PreparePurchase();
        var failure = Assert.IsType<DiagnosticFailure>(f.Controller.CaptureDiagnostics().LastFailure);
        Assert.Contains(failure.Checks, check => check.Name == "Per-item budget" && check.Passed == false && check.Expected == "1" && check.Observed == "1050");
        f.Advance(1000);
        Assert.Same(failure, f.Controller.CaptureDiagnostics().LastFailure);
        Assert.Equal(TimeSpan.Zero, failure.AtUtc.Offset);
        Assert.Empty(f.Market.Submissions);
    }

    [Theory]
    [InlineData("rejected", "The game declined the listing request.")]
    [InlineData("timeout", "No matching live listings arrived after two search attempts.")]
    [InlineData("no-nq", "No NQ single-item listing is available (HQ purchases are disabled).")]
    [InlineData("no-single", "No ordinary single-item listing is available.")]
    [InlineData("cap", "Cheapest listing is 1,050 gil after tax, above the 1 cap.")]
    public void EachSkipLogsItsItemAndExactReasonOnceBeforeAdvancing(string cause, string expectedReason)
    {
        using var f = new Fixture();
        if (cause == "rejected") f.Market.AcceptSearch = false;
        if (cause == "no-nq") f.Market.Listings = [Listing() with { IsHighQuality = true }];
        if (cause == "no-single")
        {
            f.Context.Settings = f.Context.Settings with { EnableHighQualityPurchases = true };
            f.Market.Listings = [Listing() with { Quantity = 2 }];
        }
        if (cause == "cap") f.Context.Settings = f.Context.Settings with { MaximumPricePerItem = 1 };
        Assert.True(f.Controller.Start([Item(), Item(43)]));
        f.Market.Tick();
        if (cause == "timeout") { f.Advance(6_000); f.Advance(2_000); f.Advance(6_000); }
        else if (cause != "rejected")
        {
            f.Market.Offer(42);
            f.Advance(250);
        }
        Assert.Equal((42U, "Item 42", expectedReason), Assert.Single(f.Context.SkippedLogs));
        Assert.Equal($"Skipped Item 42: {expectedReason}", f.Controller.Status);
        Assert.Equal(1, f.Controller.SkippedCount);
        Assert.Empty(f.Market.Submissions);
        f.Market.AcceptSearch = true;
        f.Advance(600);
        Assert.Equal(43U, f.Controller.CurrentItemId);
        Assert.Single(f.Context.SkippedLogs);
    }

    /// <summary>A fresh explicit Start discards an incomplete plan left by a failed candidate enumeration.</summary>
    [Fact]
    public void StartAfterCandidateFailureDiscardsTheIncompletePlan()
    {
        using var f = new Fixture();
        Assert.Throws<InvalidOperationException>(() => f.Controller.Start(FailingCandidates()));
        Assert.False(f.Controller.IsRunning);
        Assert.True(f.Controller.Start([Item(43)]));
        Assert.Equal(1, f.Controller.InitialCount);
        f.Market.Tick();
        Assert.Equal(43U, f.Controller.CurrentItemId);
        Assert.Equal(43U, f.Market.SearchedItemId);
        Assert.Equal(1, f.Controller.RemainingCount);

        static IEnumerable<ShoppingItem> FailingCandidates()
        {
            yield return Item();
            throw new InvalidOperationException("The candidate source became unavailable.");
        }
    }

    /// <summary>Context loss before the first search preserves its recovery reason and cannot fall through to another phase.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ContextLossBeforeFirstSearchStopsWithoutOverwritingTheReason(bool world)
    {
        using var f = new Fixture();
        f.Reach(ShoppingPhase.WaitingToSearch);
        if (world) f.Context.CurrentWorldId = 2; else f.Context.CurrentContentId = 2;
        f.Market.Tick();
        Assert.False(f.Controller.IsRunning);
        Assert.Equal(0, f.Market.Searches);
        Assert.Equal("Character or world changed; automated purchasing stopped.", f.Controller.Status);
    }

    /// <summary>Skipped items retain the chosen delay, subject to the same 500 ms minimum as confirmed purchases.</summary>
    [Theory]
    [InlineData(100, 500)]
    [InlineData(900, 900)]
    public void SkippingWaitsTheExactActionDelayBeforeSearchingTheNextItem(int configuredDelay, int expectedDelay)
    {
        using var f = new Fixture();
        f.Context.Settings = f.Context.Settings with { ActionDelayMilliseconds = configuredDelay };
        f.Market.AcceptSearch = false;
        Assert.True(f.Controller.Start([Item(), Item(43)]));
        f.Market.Tick();
        Assert.Equal(1, f.Controller.SkippedCount);
        Assert.Equal(1, f.Market.Searches);
        f.Market.AcceptSearch = true;
        f.Advance(expectedDelay - 1);
        Assert.Equal(1, f.Market.Searches);
        f.Advance(1);
        Assert.Equal(2, f.Market.Searches);
        Assert.Equal(43U, f.Controller.CurrentItemId);
        Assert.Equal(ShoppingPhase.WaitingForListings, f.Controller.Phase);
    }

    /// <summary>An unrelated event fault while idle cannot replace the current player-facing status.</summary>
    [Fact]
    public void IdleEventFaultDoesNotChangeTheStatus()
    {
        using var f = new Fixture();
        const string status = "Set spending options, then start at an open Market Board.";
        f.Market.Fault();
        Assert.Equal(status, f.Controller.Status);
        f.Controller.Stop("Stopped deliberately.");
        f.Market.Fault();
        Assert.Equal("Stopped deliberately.", f.Controller.Status);
    }

    /// <summary>Start guards leave native operations untouched.</summary>
    [Theory]
    [InlineData("disposed")]
    [InlineData("closed")]
    [InlineData("running")]
    [InlineData("per-item-zero")]
    [InlineData("total-zero")]
    [InlineData("empty")]
    [InlineData("owned")]
    public void StartGuardsPreventNativeRequests(string scenario)
    {
        using var f = new Fixture();
        if (scenario == "disposed") f.Controller.Dispose();
        if (scenario == "closed") f.Market.IsOpen = false;
        if (scenario == "running") Assert.True(f.Controller.Start([Item()]));
        if (scenario == "per-item-zero") f.Context.Settings = f.Context.Settings with { MaximumPricePerItem = 0 };
        if (scenario == "total-zero") f.Context.Settings = f.Context.Settings with { MaximumTotalSpend = 0 };
        if (scenario == "owned") f.Context.Missing = false;

        Assert.False(f.Controller.Start(scenario == "empty" ? [] : [Item()]));
        Assert.Equal(0, f.Market.Searches);
        Assert.Empty(f.Market.Submissions);
        Assert.Equal(scenario switch
        {
            "disposed" => "Automated purchasing stopped because the plugin is unloading.",
            "closed" => "Open a Market Board before starting.",
            "running" => "Automated purchasing started for 1 items.",
            "per-item-zero" or "total-zero" => "Set each enabled spending cap above zero before starting.",
            _ => "There are no visible missing items to buy.",
        }, f.Controller.Status);
    }

    /// <summary>A displayed plan preserves input order, filters duplicates and accounts only confirmed requests.</summary>
    [Fact]
    public void CompleteRunUsesOneRequestAtATimeAndTaxInclusiveConfirmedAccounting()
    {
        using var f = new Fixture();
        var items = new[] { Item(), Item(), Item(43) };
        Assert.True(f.Controller.Start(items));
        Assert.Equal("Automated purchasing started for 2 items.", f.Controller.Status);
        Assert.Equal(2, f.Controller.InitialCount);
        Assert.Equal(2, f.Controller.RemainingCount);
        f.PreparePurchase();
        Assert.Equal(ShoppingPhase.WaitingForPurchase, f.Controller.Phase);
        Assert.Equal(42U, f.Controller.CurrentItemId);
        Assert.Equal("Item 42", f.Controller.CurrentItemName);
        Assert.Equal(2, f.Controller.RemainingCount);
        Assert.Equal("Purchasing Item 42 for 1,050 gil...", f.Controller.Status);
        Assert.Single(f.Context.RequestedLogs);
        Assert.Equal(0UL, f.Controller.Spent);
        Assert.Equal(0, f.Controller.PurchasedCount);
        f.Market.Tick();
        Assert.Single(f.Market.Submissions);
        f.Market.Purchase(42);
        Assert.Equal(1_050UL, f.Controller.Spent);
        Assert.Equal(1, f.Controller.PurchasedCount);
        Assert.Equal("Purchased Item 42. Preparing the next item...", f.Controller.Status);
        Assert.Single(f.Context.ConfirmedLogs);
        Assert.Equal(new uint[] { 42 }, f.Context.Confirmed);
        Assert.Equal(1, f.Controller.RemainingCount);
        f.Advance(599);
        Assert.Equal(1, f.Market.Searches);
        f.Advance(1);
        Assert.Equal(2, f.Market.Searches);
        f.Market.Listings = [Listing(43, 2)];
        f.Market.Offer(43);
        f.Advance(250);
        Assert.Equal(2, f.Market.Submissions.Count);
        f.Market.Purchase(43);
        f.Advance(600);
        Assert.False(f.Controller.IsRunning);
        Assert.Equal(2, f.Controller.PurchasedCount);
        Assert.Equal(2, f.Context.RequestedLogs.Count);
        Assert.Equal(2, f.Context.ConfirmedLogs.Count);
        Assert.Equal(2_100UL, f.Controller.Spent);
        Assert.Equal(0, f.Controller.RemainingCount);
        Assert.Contains("Shopping complete", f.Controller.Status, StringComparison.Ordinal);
    }

    /// <summary>Single-item starts retain the existing single-item status and the fixed settings selected by the player.</summary>
    [Fact]
    public void StartSingleFreezesPriceQualityAndDelaySettings()
    {
        using var f = new Fixture();
        f.Context.Settings = new(0, true, 0, true, true, 100);
        Assert.True(f.Controller.StartSingle(Item()));
        Assert.Contains("Finding a listing to buy", f.Controller.Status, StringComparison.Ordinal);
        f.Context.Settings = new(1, false, 1, false, false, 10_000);
        f.Market.Listings = [Listing() with { IsHighQuality = true }];
        f.PreparePurchase();
        Assert.True(Assert.Single(f.Market.Submissions).IsHighQuality);
        f.Market.Purchase(42, isHighQuality: true);
        f.Advance(499);
        Assert.True(f.Controller.IsRunning);
        f.Advance(1);
        Assert.False(f.Controller.IsRunning);
    }

    /// <summary>Raising mutable configuration cannot expand a running finite purchase plan.</summary>
    [Fact]
    public void ChangingSettingsCannotRaiseRunCapsOrEnableHq()
    {
        using var f = new Fixture();
        f.Context.Settings = new(1_000, false, 1_000, false, false, 600);
        Assert.True(f.Controller.Start([Item()]));
        f.Context.Settings = new(0, true, 0, true, true, 600);
        f.PreparePurchase();
        Assert.Empty(f.Market.Submissions);
        Assert.Equal(1, f.Controller.SkippedCount);
        Assert.Contains("above the 1,000 cap", f.Controller.Status, StringComparison.Ordinal);
    }

    /// <summary>Every phase stops when the Market Board closes or changes into retainer context.</summary>
    [Theory]
    [InlineData((int)ShoppingPhase.WaitingToSearch)]
    [InlineData((int)ShoppingPhase.WaitingForListings)]
    [InlineData((int)ShoppingPhase.WaitingForPurchase)]
    public void ClosingMarketStopsEveryPhase(int phase)
    {
        using var f = new Fixture();
        f.Reach((ShoppingPhase)phase);
        var submitted = f.Market.Submissions.Count;
        f.Market.IsOpen = false;
        f.Market.Tick();
        Assert.False(f.Controller.IsRunning);
        Assert.Equal("Market Board closed; automated purchasing stopped.", f.Controller.Status);
        Assert.Equal(0, f.Controller.RemainingCount);
        f.Market.Purchase(42);
        Assert.Empty(f.Context.Confirmed);
        Assert.Equal(submitted, f.Market.Submissions.Count);
        var failure = Assert.IsType<DiagnosticFailure>(f.Controller.CaptureDiagnostics().LastFailure);
        AssertCheck(failure.Checks, "Market Board open", false);
    }

    /// <summary>Changing character, logging out or changing world prevents any further native action or confirmation.</summary>
    [Theory]
    [InlineData("character", false)]
    [InlineData("logout", false)]
    [InlineData("world", false)]
    [InlineData("unknown-world", false)]
    [InlineData("character", true)]
    [InlineData("world", true)]
    public void ContextLossStopsUpdatesAndConfirmations(string change, bool confirmation)
    {
        using var f = new Fixture();
        f.Reach(ShoppingPhase.WaitingForPurchase);
        if (change == "character") f.Context.CurrentContentId = 2;
        if (change == "logout") f.Context.CurrentContentId = 0;
        if (change == "world") f.Context.CurrentWorldId = 2;
        if (change == "unknown-world") f.Context.CurrentWorldId = 0;
        if (confirmation) f.Market.Purchase(42); else f.Market.Tick();
        Assert.False(f.Controller.IsRunning);
        Assert.Empty(f.Context.Confirmed);
        Assert.Equal(0UL, f.Controller.Spent);
        Assert.Single(f.Market.Submissions);
        Assert.Equal("Character or world changed; automated purchasing stopped.", f.Controller.Status);
        var failure = Assert.IsType<DiagnosticFailure>(f.Controller.CaptureDiagnostics().LastFailure);
        AssertCheck(failure.Checks, "Run identity unchanged", false);
        AssertCheck(failure.Checks, "Character matches run", change is not ("character" or "logout"));
        AssertCheck(failure.Checks, "World matches run", change is not ("world" or "unknown-world"));
    }

    /// <summary>Unavailable ownership and native search services stop before querying or purchasing.</summary>
    [Theory]
    [InlineData("ownership")]
    [InlineData("unavailable")]
    [InlineData("rejected")]
    public void SearchFailureHasNoPurchaseSideEffect(string cause)
    {
        using var f = new Fixture();
        Assert.True(f.Controller.Start([Item()]));
        if (cause == "ownership") f.Context.Missing = false;
        if (cause == "unavailable") f.Market.IsAvailable = false;
        if (cause == "rejected") f.Market.AcceptSearch = false;
        f.Market.Tick();
        if (cause == "ownership") f.Advance(10_000);
        Assert.Empty(f.Market.Submissions);
        Assert.Equal(cause == "rejected" ? 1 : 0, f.Controller.SkippedCount);
        Assert.Equal(cause == "rejected", f.Controller.IsRunning);
        Assert.Equal(cause switch
        {
            "ownership" => "Stopped at Item 42 (42): ownership refresh timed out after 10 seconds. Provider authority unavailable.",
            "unavailable" => "The Market Board listing service is unavailable.",
            _ => "Skipped Item 42: The game declined the listing request.",
        }, f.Controller.Status);
    }

    /// <summary>Listings wait for their existing quiet interval; a further packet restarts that interval.</summary>
    [Fact]
    public void ListingQuietPeriodResetsForEachMatchingPacket()
    {
        using var f = new Fixture();
        f.Reach(ShoppingPhase.WaitingForListings);
        Assert.Equal("Finding live listings for Item 42...", f.Controller.Status);
        f.Market.Offer(42);
        f.Advance(249);
        Assert.Empty(f.Market.Submissions);
        f.Market.Offer(42);
        f.Advance(249);
        Assert.Empty(f.Market.Submissions);
        f.Advance(1);
        Assert.Single(f.Market.Submissions);
    }

    /// <summary>Existing nonempty packet correlation uses item IDs; empty packets require the current request ID.</summary>
    [Theory]
    [InlineData("wrong-item", false)]
    [InlineData("mixed-items", false)]
    [InlineData("empty-wrong-request", false)]
    [InlineData("empty-current-request", true)]
    [InlineData("same-item-other-request", true)]
    public void OfferingsCorrelationPreservesExistingNativeContract(string packet, bool accepted)
    {
        using var f = new Fixture();
        f.Reach(ShoppingPhase.WaitingForListings);
        var ids = packet switch
        {
            "wrong-item" => new uint[] { 99 },
            "mixed-items" => [42, 99],
            "same-item-other-request" => [42],
            _ => [],
        };
        f.Market.Offer(ids, packet == "empty-current-request" ? 7 : 9);
        f.Advance(250);
        Assert.Equal(accepted ? 1 : 0, f.Market.Submissions.Count);
    }

    /// <summary>Unanswered listing requests skip at six seconds; a ready quiet response retains precedence at the boundary.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ListingDeadlinePreservesQuietResponsePrecedence(bool response)
    {
        using var f = new Fixture();
        f.Reach(ShoppingPhase.WaitingForListings);
        f.Advance(5_749);
        if (response) f.Market.Offer(42);
        f.Advance(250);
        if (!response) Assert.Equal(0, f.Controller.SkippedCount);
        f.Advance(1);
        if (!response) { f.Advance(2_000); f.Advance(6_000); }
        Assert.Equal(response ? 0 : 1, f.Controller.SkippedCount);
        Assert.Equal(response ? 1 : 0, f.Market.Submissions.Count);
        Assert.Equal(response ? ShoppingPhase.WaitingForPurchase : ShoppingPhase.WaitingToSearch, f.Controller.Phase);
        if (!response)
            Assert.Equal("Skipped Item 42: No matching live listings arrived after two search attempts.", f.Controller.Status);
    }

    /// <summary>Read failures, item changes, unsupported listings and exceeded caps never spend gil.</summary>
    [Theory]
    [InlineData("read-unavailable")]
    [InlineData("wrong-item")]
    [InlineData("no-nq")]
    [InlineData("no-single")]
    [InlineData("total-cap")]
    public void ListingDecisionFailuresDoNotSubmit(string cause)
    {
        using var f = new Fixture();
        if (cause == "no-single") f.Context.Settings = f.Context.Settings with { EnableHighQualityPurchases = true };
        if (cause == "total-cap") f.Context.Settings = f.Context.Settings with { MaximumTotalSpend = 1_000 };
        f.Reach(ShoppingPhase.WaitingForListings);
        if (cause == "read-unavailable") f.Market.CanRead = false;
        if (cause == "wrong-item") f.Market.SearchedItemId = 99;
        if (cause == "no-nq") f.Market.Listings = [Listing() with { IsHighQuality = true }];
        if (cause == "no-single") f.Market.Listings = [Listing() with { Quantity = 2 }];
        f.Market.Offer(42);
        f.Advance(250);
        Assert.Empty(f.Market.Submissions);
        Assert.Equal(cause is "no-nq" or "no-single" ? 1 : 0, f.Controller.SkippedCount);
        Assert.Equal(cause is "no-nq" or "no-single", f.Controller.IsRunning);
        Assert.Equal(cause switch
        {
            "no-nq" => "Skipped Item 42: No NQ single-item listing is available (HQ purchases are disabled).",
            "no-single" => "Skipped Item 42: No ordinary single-item listing is available.",
            "total-cap" => "Total budget reached before Item 42; 0 gil spent.",
            _ => "The Market Board changed to another item; stopped before spending gil.",
        }, f.Controller.Status);
    }

    /// <summary>Each changed field is compared against the candidate that actually passed eligibility and spending checks.</summary>
    [Theory]
    [InlineData("missing")]
    [InlineData("item")]
    [InlineData("listing")]
    [InlineData("price")]
    [InlineData("tax")]
    [InlineData("quantity")]
    [InlineData("quality")]
    [InlineData("mannequin")]
    [InlineData("index")]
    public void ChangedListingCannotBypassOriginalSelectionOrCaps(string changed)
    {
        using var f = new Fixture();
        var original = Listing();
        f.Market.Reread = changed switch
        {
            "missing" => _ => null,
            "item" => _ => original with { ItemId = 99 },
            "listing" => _ => original with { ListingId = 2 },
            "price" => _ => original with { UnitPrice = 100_000 },
            "tax" => _ => original with { TotalTax = 100_000 },
            "quantity" => _ => original with { Quantity = 2 },
            "quality" => _ => original with { IsHighQuality = true },
            "mannequin" => _ => original with { IsMannequin = true },
            _ => _ => original with { SourceIndex = 1 },
        };
        Assert.True(f.Controller.Start([Item()]));
        f.PreparePurchase();
        Assert.Empty(f.Market.Submissions);
        Assert.False(f.Controller.IsRunning);
        Assert.Contains("moved", f.Controller.Status, StringComparison.Ordinal);
        var failure = Assert.IsType<DiagnosticFailure>(f.Controller.CaptureDiagnostics().LastFailure);
        AssertCheck(failure.Checks, "Selected listing unchanged", false);
    }

    /// <summary>Authority or world invalidation during listing verification blocks the final submission.</summary>
    [Theory]
    [InlineData("ownership")]
    [InlineData("world")]
    [InlineData("character")]
    public void PreSubmitAuthorityIsRechecked(string changed)
    {
        using var f = new Fixture();
        f.Market.Reread = listing =>
        {
            if (changed == "world") f.Context.CurrentWorldId = 2;
            else if (changed == "character") f.Context.CurrentContentId = 2;
            else f.Context.Missing = false;
            return listing;
        };
        Assert.True(f.Controller.Start([Item()]));
        f.PreparePurchase();
        if (changed == "ownership") f.Advance(10_000);
        Assert.Empty(f.Market.Submissions);
        Assert.False(f.Controller.IsRunning);
        Assert.Equal(changed == "ownership"
            ? "Stopped at Item 42 (42): ownership refresh timed out after 10 seconds. Provider authority unavailable."
            : "Stopped at Item 42 (42): character or world changed before purchase.", f.Controller.Status);
        Assert.Equal(f.Controller.Status, Assert.Single(f.Context.StoppedLogs));
        var failure = Assert.IsType<DiagnosticFailure>(f.Controller.CaptureDiagnostics().LastFailure);
        Assert.Contains(failure.Checks, check => check.Passed == false);
        if (changed == "ownership")
            Assert.Contains(failure.Checks, check => check.Name == "Ownership still permits missing item" && check.Passed == false);
    }

    /// <summary>Rejected or uncertain requests stay registered across stop and subsequent explicit starts.</summary>
    [Theory]
    [InlineData("rejected", false)]
    [InlineData("stop", false)]
    [InlineData("timeout", false)]
    [InlineData("stop", true)]
    public void UncertainRequestsCannotBeResubmitted(string ending, bool reuseListingForDifferentItem)
    {
        using var f = new Fixture();
        f.Market.AcceptPurchase = ending != "rejected";
        Assert.True(f.Controller.Start([Item()]));
        f.PreparePurchase();
        if (ending == "stop") f.Controller.Stop();
        if (ending == "timeout")
        {
            f.Advance(7_999);
            Assert.True(f.Controller.IsRunning);
            f.Advance(1);
            Assert.Contains("timed out", f.Controller.Status, StringComparison.Ordinal);
        }
        Assert.False(f.Controller.IsRunning);
        if (ending == "rejected") Assert.Equal("The game rejected the purchase request for Item 42.", f.Controller.Status);
        f.Market.Purchase(42);
        Assert.Empty(f.Context.Confirmed);
        var nextItemId = reuseListingForDifferentItem ? 43U : 42U;
        f.Market.Listings = [Listing(nextItemId)];
        f.Market.AcceptPurchase = true;
        Assert.True(f.Controller.Start([Item(nextItemId)]));
        f.PreparePurchase(nextItemId);
        Assert.Single(f.Market.Submissions);
        Assert.Contains("duplicate", f.Controller.Status, StringComparison.Ordinal);
    }

    /// <summary>Wrong purchase responses stop the run without advancing accounting or history.</summary>
    [Theory]
    [InlineData(99U, 1U)]
    [InlineData(42U, 0U)]
    [InlineData(42U, 2U)]
    public void UnexpectedPurchaseResponseStopsWithoutConfirmation(uint itemId, uint quantity)
    {
        using var f = new Fixture();
        f.Reach(ShoppingPhase.WaitingForPurchase);
        f.Market.Purchase(itemId, quantity);
        Assert.False(f.Controller.IsRunning);
        Assert.Empty(f.Context.Confirmed);
        Assert.Equal(0, f.Controller.PurchasedCount);
        Assert.Equal(0UL, f.Controller.Spent);
        Assert.Equal($"An unexpected Market Board purchase event arrived (item {itemId}, quantity {quantity}); stopped before making another request.",
            f.Controller.Status);
    }

    /// <summary>The confirmation's raw catalog quality must match the approved listing.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void ConfirmationQualityMustMatchSelectedListing(bool selectedHq, bool confirmedHq)
    {
        using var f = new Fixture();
        f.Context.Settings = f.Context.Settings with { EnableHighQualityPurchases = true };
        f.Market.Listings = [Listing() with { IsHighQuality = selectedHq }];
        f.Reach(ShoppingPhase.WaitingForPurchase);
        f.Market.Purchase(42, isHighQuality: confirmedHq);
        Assert.Equal(selectedHq == confirmedHq ? 1 : 0, f.Controller.PurchasedCount);
        Assert.Equal(selectedHq == confirmedHq ? 1_050UL : 0UL, f.Controller.Spent);
        Assert.Equal(selectedHq == confirmedHq ? 1 : 0, f.Context.Confirmed.Count);
    }

    /// <summary>A packet delivered during native submission is processed on the next framework update.</summary>
    [Fact]
    public void QueuedConfirmationDuringSubmitIsProcessedAfterRequestStateIsReady()
    {
        using var f = new Fixture();
        f.Market.OnSubmit = () => f.Market.PendingEvents.Enqueue(new ShoppingPurchase(42, 42, 1));
        f.Reach(ShoppingPhase.WaitingForPurchase);
        Assert.Empty(f.Context.Confirmed);
        Assert.Equal(0UL, f.Controller.Spent);
        f.Market.Tick();
        Assert.Equal(new uint[] { 42 }, f.Context.Confirmed);
        Assert.Equal(1_050UL, f.Controller.Spent);
        Assert.Single(f.Market.Submissions);
    }

    /// <summary>An overflow stops an active run and preserves the uncertain request registry.</summary>
    [Fact]
    public void PacketOverflowStopsBeforeAnyAdditionalAction()
    {
        using var f = new Fixture();
        f.Market.Fault();
        Assert.False(f.Controller.IsRunning);
        f.Reach(ShoppingPhase.WaitingForPurchase);
        for (var index = 0; index <= ShoppingEventQueue.MaximumPendingEvents; index++)
            f.Market.PendingEvents.Enqueue(new ShoppingPurchase(42, 42, 1));
        f.Market.Tick();
        Assert.False(f.Controller.IsRunning);
        Assert.Empty(f.Context.Confirmed);
        Assert.Contains("exceeded its limit", f.Controller.Status, StringComparison.Ordinal);
        Assert.True(f.Controller.Start([Item()]));
        f.PreparePurchase();
        Assert.Single(f.Market.Submissions);
        Assert.Contains("duplicate", f.Controller.Status, StringComparison.Ordinal);
    }

    /// <summary>Disposal is idempotent and callbacks captured before unsubscription cannot revive disposed work.</summary>
    [Fact]
    public void DisposeUnsubscribesAndRejectsAlreadyCapturedCallbacks()
    {
        using var f = new Fixture();
        f.Reach(ShoppingPhase.WaitingForPurchase);
        var callbacks = f.Market.CaptureCallbacks();
        f.Controller.Dispose();
        f.Controller.Dispose();
        Assert.Equal("Automated purchasing stopped because the plugin is unloading.", f.Controller.Status);
        callbacks();
        f.Market.Tick();
        f.Market.Offer(42);
        f.Market.Purchase(42);
        Assert.Equal(1, f.Market.Disposals);
        Assert.Equal(0, f.Market.SubscriberCount);
        Assert.False(f.Controller.Start([Item()]));
        Assert.False(f.Controller.IsRunning);
        Assert.Empty(f.Context.Confirmed);
        Assert.Single(f.Market.Submissions);
    }

    /// <summary>Idle and nonmatching-phase events cannot change the purchase lifecycle.</summary>
    [Fact]
    public void EventsOutsideTheirExpectedPhaseAreIgnored()
    {
        using var f = new Fixture();
        var status = f.Controller.Status;
        Assert.Equal("Set spending options, then start at an open Market Board.", status);
        f.Market.Tick();
        f.Market.Offer(42);
        f.Market.Purchase(42);
        Assert.Equal(status, f.Controller.Status);
        Assert.True(f.Controller.Start([Item()]));
        f.Market.Offer(42);
        f.Market.Purchase(42);
        Assert.Equal(ShoppingPhase.WaitingToSearch, f.Controller.Phase);
        f.Controller.Stop("Stopped by test");
        Assert.Equal("Stopped by test", f.Controller.Status);
        Assert.Equal(string.Empty, f.Controller.CurrentItemName);
        Assert.Equal(0U, f.Controller.CurrentItemId);
    }

    /// <summary>The bounded request ledger stops accepting new IDs at its exact existing session limit.</summary>
    [Fact]
    public void SessionRequestLedgerStopsAt4096Requests()
    {
        using var f = new Fixture();
        for (uint index = 1; index <= 4_096; index++)
        {
            f.Market.Listings = [Listing(index, index)];
            Assert.True(f.Controller.Start([Item(index)]));
            f.PreparePurchase(index);
            Assert.Equal(ShoppingPhase.WaitingForPurchase, f.Controller.Phase);
            f.Controller.Stop();
        }
        f.Market.Listings = [Listing(4_097, 4_097)];
        Assert.True(f.Controller.Start([Item(4_097)]));
        f.PreparePurchase(4_097);
        Assert.Equal(4_096, f.Market.Submissions.Count);
        Assert.False(f.Controller.IsRunning);
        Assert.Contains("registry reached its limit", f.Controller.Status, StringComparison.Ordinal);
    }

    [Fact]
    public void ChangingTheDisplayedCandidateListCannotReplanAnActiveRun()
    {
        using var f = new Fixture();
        var displayedItems = new List<ShoppingItem> { Item(), Item(43) };
        Assert.True(f.Controller.Start(displayedItems));
        displayedItems.Clear();
        displayedItems.Add(Item(44));

        f.PreparePurchase();
        f.Market.Purchase(42);
        f.Market.Listings = [Listing(43, 2)];
        f.Advance(600);
        Assert.Equal(43U, f.Market.SearchedItemId);
        f.Market.Offer(43);
        f.Advance(250);
        f.Market.Purchase(43);
        f.Advance(600);

        Assert.False(f.Controller.IsRunning);
        Assert.Equal(2, f.Controller.InitialCount);
        Assert.Equal(new uint[] { 42, 43 }, f.Market.Submissions.Select(listing => listing.ItemId));
        Assert.Equal(new uint[] { 42, 43 }, f.Context.Confirmed);
    }

    [Theory]
    [InlineData(2_099, false)]
    [InlineData(2_100, true)]
    public void ConfirmedSpendConstrainsTheNextPurchaseAtTheExactTaxInclusiveRunLimit(int runLimit, bool secondAllowed)
    {
        using var f = new Fixture();
        f.Context.Settings = f.Context.Settings with { MaximumPricePerItem = 1_050, MaximumTotalSpend = runLimit };
        Assert.True(f.Controller.Start([Item(), Item(43)]));
        f.PreparePurchase();
        f.Market.Purchase(42);
        f.Market.Listings = [Listing(43, 2)];
        f.Advance(600);
        f.Market.Offer(43);
        f.Advance(250);

        Assert.Equal(secondAllowed ? 2 : 1, f.Market.Submissions.Count);
        Assert.Equal(1_050UL, f.Controller.Spent);
        Assert.Equal(1, f.Controller.PurchasedCount);
        if (secondAllowed)
        {
            f.Market.Purchase(43);
            f.Advance(600);
            Assert.Equal(2_100UL, f.Controller.Spent);
            Assert.Equal(2, f.Controller.PurchasedCount);
        }
        else
        {
            var failure = Assert.IsType<DiagnosticFailure>(f.Controller.CaptureDiagnostics().LastFailure);
            Assert.Equal(1, failure.ConfirmedActions);
            Assert.Contains(failure.Checks, check => check.Name == "Run budget" && check.Passed == false
                && check.Expected == "2099" && check.Observed == "2100");
        }
        Assert.False(f.Controller.IsRunning);
    }

    [Fact]
    public void CancelKeepsConfirmedHistoryAndBlocksAnUnconfirmedItemAfterHistoryIsCleared()
    {
        using var f = new Fixture();
        Assert.True(f.Controller.Start([Item(), Item(43), Item(44)]));
        f.PreparePurchase();
        f.Market.Purchase(42);
        f.Market.Listings = [Listing(43, 2)];
        f.Advance(600);
        f.Market.Offer(43);
        f.Advance(250);
        f.Controller.Stop();
        f.Market.Purchase(43);
        f.Advance(8_000);

        Assert.False(f.Controller.IsRunning);
        Assert.Equal(0, f.Controller.RemainingCount);
        Assert.Equal(1_050UL, f.Controller.Spent);
        Assert.Equal(1, f.Controller.PurchasedCount);
        Assert.Equal(new uint[] { 42 }, f.Context.Confirmed);
        Assert.Equal(new uint[] { 42 }, f.Context.Completed);
        Assert.Equal(2, f.Market.Submissions.Count);

        f.Context.Completed.Clear();
        f.Market.Listings = [Listing(43, 3)];
        Assert.True(f.Controller.StartSingle(Item(43)));
        f.PreparePurchase(43);
        Assert.False(f.Controller.IsRunning);
        Assert.Equal(2, f.Market.Submissions.Count);
        Assert.Contains("duplicate purchase request", f.Controller.Status, StringComparison.Ordinal);
    }

    [Fact]
    public void OwnershipInvalidationAfterSubmissionRecordsConfirmationButBlocksTheNextItem()
    {
        using var f = new Fixture();
        Assert.True(f.Controller.Start([Item(), Item(43)]));
        f.PreparePurchase();
        f.Context.Missing = false;
        f.Market.Purchase(42);

        Assert.Equal(1_050UL, f.Controller.Spent);
        Assert.Equal(new uint[] { 42 }, f.Context.Confirmed);
        f.Advance(600);
        Assert.Equal(ShoppingPhase.WaitingForOwnership, f.Controller.Phase);
        f.Advance(10_000);
        Assert.False(f.Controller.IsRunning);
        Assert.Single(f.Market.Submissions);
        Assert.Equal(1, f.Market.Searches);
        var failure = Assert.IsType<DiagnosticFailure>(f.Controller.CaptureDiagnostics().LastFailure);
        Assert.Equal(1, failure.ConfirmedActions);
        Assert.Contains(failure.Checks, check => check.Name == "Ownership authorizes missing item" && check.Passed == false);
    }

    [Fact]
    public void RejectedStartsRetainTheirOwnChecksAndANewStartClearsThePreviousAttempt()
    {
        using var f = new Fixture();
        f.Market.IsOpen = false;
        Assert.False(f.Controller.Start([Item()]));
        var closed = Assert.IsType<DiagnosticFailure>(f.Controller.CaptureDiagnostics().LastFailure);
        Assert.Equal("Open a Market Board before starting.", closed.Reason);
        AssertCheck(closed.Checks, "Market Board open", false);

        f.Market.IsOpen = true;
        Assert.False(f.Controller.Start([]));
        var empty = Assert.IsType<DiagnosticFailure>(f.Controller.CaptureDiagnostics().LastFailure);
        Assert.NotSame(closed, empty);
        Assert.Equal("There are no visible missing items to buy.", empty.Reason);
        AssertCheck(empty.Checks, "Market Board open", true);
        AssertCheck(empty.Checks, "Eligible plan has items", false);

        Assert.True(f.Controller.Start([Item()]));
        var current = f.Controller.CaptureDiagnostics();
        Assert.Same(empty, current.LastFailure);
        Assert.Equal(new DiagnosticPredicate("Market Board open", true, "true", "True"), Assert.Single(current.Checks));
        AssertCheck(closed.Checks, "Market Board open", false);
        AssertCheck(empty.Checks, "Eligible plan has items", false);
    }

    [Theory]
    [InlineData(-1, 100, false, true)]
    [InlineData(100, -1, true, false)]
    [InlineData(0, 100, false, true)]
    [InlineData(100, 0, true, false)]
    public void InvalidCapDiagnosticsDistinguishEachPositiveZeroAndNegativeValue(int itemCap, int runCap, bool itemValid, bool runValid)
    {
        using var f = new Fixture();
        f.Context.Settings = f.Context.Settings with { MaximumPricePerItem = itemCap, MaximumTotalSpend = runCap };
        Assert.False(f.Controller.Start([Item()]));
        var failure = Assert.IsType<DiagnosticFailure>(f.Controller.CaptureDiagnostics().LastFailure);
        AssertCheck(failure.Checks, "Per-item cap enabled value", itemValid);
        AssertCheck(failure.Checks, "Run cap enabled value", runValid);
        Assert.Empty(f.Market.Submissions);
    }

    [Fact]
    public void DiagnosticTimelineReportsSearchCorrelationUnlimitedLimitsAndConfirmation()
    {
        using var f = new Fixture();
        f.Context.Settings = f.Context.Settings with { UnlimitedPricePerItem = true, UnlimitedTotalSpend = true };
        Assert.True(f.Controller.Start([Item()]));
        AssertCheck(f.Controller.CaptureDiagnostics().Checks, "Market Board open", true);
        f.Market.Tick();
        var search = f.Controller.CaptureDiagnostics();
        Assert.Equal(new[]
        {
            new DiagnosticPredicate("Ownership authorizes missing item", true, "true", "True"),
            new DiagnosticPredicate("Listing service available", true, "true", "True"),
            new DiagnosticPredicate("Listing request accepted", true, "true", "True"),
        }, search.Checks);

        f.Market.Offer(99);
        var unrelated = f.Controller.CaptureDiagnostics();
        AssertCheck(unrelated.Checks, "Listing response belongs to request", false);
        f.Market.Offer(42);
        AssertCheck(f.Controller.CaptureDiagnostics().Checks, "Listing response belongs to request", true);
        AssertCheck(unrelated.Checks, "Listing response belongs to request", false);
        f.Advance(250);
        var submitted = f.Controller.CaptureDiagnostics();
        AssertCheck(submitted.Checks, "Market Board open", true);
        AssertCheck(submitted.Checks, "Run identity unchanged", true);
        AssertCheck(submitted.Checks, "Character matches run", true);
        AssertCheck(submitted.Checks, "World matches run", true);
        AssertCheck(submitted.Checks, "Listings readable", true);
        AssertCheck(submitted.Checks, "Search item matches", true);
        AssertCheck(submitted.Checks, "Eligible single-item listing", true);
        AssertCheck(submitted.Checks, "Per-item budget", true, "Unlimited", "1050");
        AssertCheck(submitted.Checks, "Run budget", true, "Unlimited", "1050");
        AssertCheck(submitted.Checks, "Selected listing unchanged", true);
        AssertCheck(submitted.Checks, "Ownership still permits missing item", true);
        AssertCheck(submitted.Checks, "Purchase request accepted", true);

        f.Market.Purchase(42);
        var confirmed = f.Controller.CaptureDiagnostics();
        AssertCheck(confirmed.Checks, "Confirmation item matches", true);
        AssertCheck(confirmed.Checks, "Confirmation quantity", true, "1", "1");
        AssertCheck(confirmed.Checks, "Confirmation quality matches", true);
        Assert.Equal(1, confirmed.ConfirmedActions);
        Assert.Single(f.Market.Submissions);
    }

    [Fact]
    public void UnreadableListingsReportAnUnknownItemComparisonWithoutInventingFalseOrTrue()
    {
        using var f = new Fixture();
        f.Reach(ShoppingPhase.WaitingForListings);
        f.Market.CanRead = false;
        f.Market.Offer(42);
        f.Advance(250);
        var failure = Assert.IsType<DiagnosticFailure>(f.Controller.CaptureDiagnostics().LastFailure);
        AssertCheck(failure.Checks, "Listings readable", false);
        AssertCheck(failure.Checks, "Search item matches", null);
        Assert.Empty(f.Market.Submissions);
    }

    [Theory]
    [InlineData(false, "Listings arrived before deadline", "2026-09-14T00:00:14.0000000Z")]
    [InlineData(true, "Purchase confirmed before deadline", "2026-09-14T00:00:08.2500000Z")]
    public void TimeoutDiagnosticsRetainExactUtcDeadlineAndObservation(bool purchase, string checkName, string expectedTime)
    {
        using var f = new Fixture();
        f.Reach(purchase ? ShoppingPhase.WaitingForPurchase : ShoppingPhase.WaitingForListings);
        if (!purchase) { f.Advance(6_000); f.Advance(2_000); }
        f.Advance(purchase ? 8_000 : 6_000);
        var failure = Assert.IsType<DiagnosticFailure>(f.Controller.CaptureDiagnostics().LastFailure);
        AssertCheck(failure.Checks, checkName, false, expectedTime, expectedTime);
        Assert.Equal(purchase ? 1 : 0, f.Market.Submissions.Count);
        Assert.Equal(0, failure.ConfirmedActions);
    }

    [Fact]
    public void RejectedListingDiagnosticsExplainEachRowPredicateWithinTheFourRowSample()
    {
        using var f = new Fixture();
        f.Market.Listings = [
            Listing(99, 0) with { SourceIndex = 0, UnitPrice = 0, Quantity = 2, IsMannequin = true },
            Listing(42, 2) with { SourceIndex = 1, Quantity = 2, IsHighQuality = true },
            Listing(42, 3) with { SourceIndex = 2, Quantity = 2 },
            Listing(42, 4) with { SourceIndex = 3, Quantity = 2 },
            Listing(42, 5) with { SourceIndex = 4, Quantity = 2 },
        ];
        Assert.True(f.Controller.Start([Item()]));
        f.PreparePurchase();
        var failure = Assert.IsType<DiagnosticFailure>(f.Controller.CaptureDiagnostics().LastFailure);
        AssertCheck(failure.Checks, "Eligible single-item listing", false);
        AssertCheck(failure.Checks, "Row 0 item matches", false);
        AssertCheck(failure.Checks, "Row 0 listing identity present", false);
        AssertCheck(failure.Checks, "Row 0 price nonzero", false);
        AssertCheck(failure.Checks, "Row 0 quantity is one", false, "1", "2");
        AssertCheck(failure.Checks, "Row 0 ordinary listing", false);
        AssertCheck(failure.Checks, "Row 0 quality allowed", true);
        AssertCheck(failure.Checks, "Row 1 item matches", true);
        AssertCheck(failure.Checks, "Row 1 listing identity present", true);
        AssertCheck(failure.Checks, "Row 1 price nonzero", true);
        AssertCheck(failure.Checks, "Row 1 ordinary listing", true);
        AssertCheck(failure.Checks, "Row 1 quality allowed", false);
        Assert.Equal(24, failure.Checks.Count(check => check.Name.StartsWith("Row ", StringComparison.Ordinal)));
        Assert.DoesNotContain(failure.Checks, check => check.Name.StartsWith("Row 4 ", StringComparison.Ordinal));
        Assert.Empty(f.Market.Submissions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NewlyPurchasedQueuedItemIsSkippedAndNextItemStillRuns(bool beforeSubmission)
    {
        using var f = new Fixture();
        Assert.True(f.Controller.Start([Item(), Item(43)]));
        if (beforeSubmission)
            f.Market.Reread = listing => { f.Context.Completed.Add(42); return listing; };
        else
            f.Context.Completed.Add(42);
        f.PreparePurchase();
        Assert.True(f.Controller.IsRunning);
        Assert.Equal(1, f.Controller.SkippedCount);
        Assert.Empty(f.Market.Submissions);
        var skip = Assert.Single(f.Context.SkippedLogs);
        Assert.Equal(42U, skip.ItemId);
        Assert.Equal("Confirmed purchase history.", skip.Reason);
        Assert.Empty(f.Context.StoppedLogs);
        f.Market.Reread = listing => listing;
        f.Market.Listings = [Listing(43)];
        f.Advance(600);
        f.Market.Offer(43);
        f.Advance(250);
        Assert.Single(f.Market.Submissions);
        f.Market.Purchase(43);
        f.Advance(600);
        Assert.False(f.Controller.IsRunning);
        Assert.Equal(1, f.Controller.PurchasedCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnavailableOwnershipStopsWithItemAndReasonWithoutSubmitting(bool beforeSubmission)
    {
        using var f = new Fixture();
        Assert.True(f.Controller.Start([Item(), Item(43)]));
        if (beforeSubmission)
            f.Market.Reread = listing => { f.Context.Missing = false; return listing; };
        else
            f.Context.Missing = false;
        f.PreparePurchase();
        Assert.Equal(ShoppingPhase.WaitingForOwnership, f.Controller.Phase);
        f.Advance(10_000);
        Assert.False(f.Controller.IsRunning);
        Assert.Empty(f.Market.Submissions);
        Assert.Empty(f.Context.SkippedLogs);
        Assert.Equal("Stopped at Item 42 (42): ownership refresh timed out after 10 seconds. Provider authority unavailable.",
            Assert.Single(f.Context.StoppedLogs));
        Assert.Equal(f.Controller.Status, f.Controller.CaptureDiagnostics().LastFailure!.Reason);
        f.Context.Missing = true;
        f.Advance(1000);
        Assert.Empty(f.Market.Submissions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnershipRecoverySearchesAgainBeforeAnyPurchase(bool atSubmission)
    {
        using var f = new Fixture();
        Assert.True(f.Controller.Start([Item(), Item(43)]));
        if (atSubmission)
            f.Market.Reread = listing => { f.Context.Missing = false; return listing; };
        else
            f.Context.Missing = false;
        f.PreparePurchase();
        Assert.Equal(ShoppingPhase.WaitingForOwnership, f.Controller.Phase);
        Assert.Equal("Waiting for ownership refresh before Item 42...", f.Controller.Status);
        var searches = f.Market.Searches;
        f.Advance(1000);
        AssertCheck(f.Controller.CaptureDiagnostics().Checks, "Ownership authorizes missing item", false);
        Assert.Equal(searches, f.Market.Searches);
        Assert.Empty(f.Market.Submissions);
        Assert.Equal(2, f.Controller.RemainingCount);
        f.Context.Missing = true;
        f.Market.Reread = listing => listing;
        f.Advance(1);
        Assert.Equal(ShoppingPhase.WaitingForListings, f.Controller.Phase);
        Assert.Equal(searches + 1, f.Market.Searches);
        Assert.Empty(f.Market.Submissions);
        f.Market.Offer(42);
        f.Advance(250);
        Assert.Single(f.Market.Submissions);
        f.Market.Purchase(42);
        Assert.Equal(1, f.Controller.PurchasedCount);
        Assert.Equal(1, f.Controller.RemainingCount);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("close")]
    [InlineData("character")]
    [InlineData("world")]
    [InlineData("dispose")]
    public void OwnershipWaitCannotOutliveRunContext(string change)
    {
        using var f = new Fixture();
        Assert.True(f.Controller.Start([Item()]));
        f.Context.Missing = false;
        f.Market.Tick();
        Assert.Equal(ShoppingPhase.WaitingForOwnership, f.Controller.Phase);
        if (change == "cancel") f.Controller.Stop();
        if (change == "close") f.Market.IsOpen = false;
        if (change == "character") f.Context.CurrentContentId = 2;
        if (change == "world") f.Context.CurrentWorldId = 2;
        if (change == "dispose") f.Controller.Dispose();
        f.Context.Missing = true;
        f.Advance(1);
        Assert.False(f.Controller.IsRunning);
        Assert.Empty(f.Market.Submissions);
        Assert.Equal(0, f.Market.Searches);
    }

    [Fact]
    public void OwnershipRecoverySkipsNewDuplicateAndRetainsNextItem()
    {
        using var f = new Fixture();
        Assert.True(f.Controller.Start([Item(), Item(43)]));
        f.Context.Missing = false;
        f.Market.Tick();
        f.Context.Completed.Add(42);
        f.Context.Missing = true;
        f.Advance(1);
        Assert.Equal(1, f.Controller.SkippedCount);
        Assert.Equal(1, f.Controller.RemainingCount);
        Assert.Equal(0, f.Market.Searches);
        f.Advance(600);
        Assert.Equal(43U, f.Market.SearchedItemId);
    }

    [Fact]
    public void RepeatedOwnershipInvalidationDoesNotExtendDeadline()
    {
        using var f = new Fixture();
        Assert.True(f.Controller.Start([Item()]));
        f.Context.Missing = false;
        f.Market.Tick();
        f.Advance(9000);
        f.Context.Missing = true;
        f.Advance(1);
        f.Market.Reread = listing => { f.Context.Missing = false; return listing; };
        f.Market.Offer(42);
        f.Advance(250);
        Assert.Equal(ShoppingPhase.WaitingForOwnership, f.Controller.Phase);
        f.Advance(748);
        Assert.True(f.Controller.IsRunning);
        f.Advance(1);
        Assert.False(f.Controller.IsRunning);
        Assert.Contains("timed out", f.Controller.Status);
        Assert.Empty(f.Market.Submissions);
    }

    [Fact]
    public void RecoveredAuthorityDoesNotExpireWhileFreshListingsArrive()
    {
        using var f = new Fixture();
        Assert.True(f.Controller.Start([Item()]));
        f.Context.Missing = false;
        f.Market.Tick();
        f.Advance(9000);
        f.Context.Missing = true;
        f.Advance(1);
        f.Market.Offer(42);
        f.Advance(1000);
        Assert.Single(f.Market.Submissions);
        Assert.Equal(ShoppingPhase.WaitingForPurchase, f.Controller.Phase);
    }

    [Fact]
    public void AuthorityArrivingAtOwnershipDeadlineCannotRestartTheTimedOutItem()
    {
        using var f = new Fixture();
        Assert.True(f.Controller.Start([Item()]));
        f.Context.Missing = false;
        f.Market.Tick();
        f.Context.Missing = true;
        f.Advance(10_000);
        Assert.False(f.Controller.IsRunning);
        Assert.Equal(0, f.Market.Searches);
        Assert.Empty(f.Market.Submissions);
        Assert.Contains("ownership refresh timed out after 10 seconds", Assert.Single(f.Context.StoppedLogs));
    }

    [Fact]
    public void UnansweredSearchRetriesSameItemOnceAndCanPurchase()
    {
        using var f = new Fixture();
        Assert.True(f.Controller.Start([Item(), Item(43)]));
        f.Market.Tick();
        f.Advance(6000);
        Assert.Equal(new uint[] { 42 }, f.Context.SearchRetries);
        Assert.Equal("No matching listing response within six seconds; retrying once after two seconds.",
            Assert.Single(f.Context.SearchRetryReasons));
        Assert.Equal("Retrying listings for Item 42...", f.Controller.Status);
        Assert.Equal(0, f.Controller.SkippedCount);
        f.Advance(1999);
        Assert.Equal(1, f.Market.Searches);
        f.Advance(1);
        Assert.Equal(2, f.Market.Searches);
        Assert.Equal(42U, f.Market.SearchedItemId);
        f.Market.Offer(99);
        f.Advance(250);
        Assert.Empty(f.Market.Submissions);
        f.Market.Offer(42);
        f.Advance(250);
        Assert.Single(f.Market.Submissions);
        f.Market.Purchase(42);
        f.Advance(600);
        Assert.Equal(43U, f.Market.SearchedItemId);
        Assert.Equal(1, f.Controller.PurchasedCount);
    }

    [Theory]
    [InlineData("owned")]
    [InlineData("unknown")]
    [InlineData("cancel")]
    [InlineData("world")]
    public void ListingRetryRechecksAuthorityAndCancellation(string change)
    {
        using var f = new Fixture();
        Assert.True(f.Controller.Start([Item()]));
        f.Market.Tick();
        f.Advance(6000);
        if (change == "owned") f.Context.Completed.Add(42);
        if (change == "unknown") f.Context.Missing = false;
        if (change == "cancel") f.Controller.Stop();
        if (change == "world") f.Context.CurrentWorldId = 2;
        f.Advance(2000);
        Assert.Equal(1, f.Market.Searches);
        Assert.Empty(f.Market.Submissions);
        Assert.Equal(change == "owned" ? 1 : 0, f.Controller.SkippedCount);
        if (change == "unknown") Assert.Equal(ShoppingPhase.WaitingForOwnership, f.Controller.Phase);
    }

    [Fact]
    public void ConfirmedItemCanBeBoughtAgainAfterOwnershipReconciliationWithANewListing()
    {
        using var f = new Fixture();
        Assert.True(f.Controller.Start([Item()]));
        f.PreparePurchase();
        f.Market.Purchase(42);
        f.Advance(600);
        // Context retires the confirmed guard only after canonical acknowledgement.
        f.Context.Completed.Clear();
        f.Market.Listings = [Listing(42, 2)];
        Assert.True(f.Controller.Start([Item()]));
        f.PreparePurchase();
        Assert.Equal(2, f.Market.Submissions.Count);
        f.Market.Purchase(42);
        Assert.Equal(2, f.Context.Confirmed.Count);
    }

    private static void AssertCheck(IReadOnlyList<DiagnosticPredicate> checks, string name, bool? passed,
        string expected = "true", string? observed = null)
    {
        var check = Assert.Single(checks, check => check.Name == name);
        Assert.Equal(new DiagnosticPredicate(name, passed, expected, observed ?? passed?.ToString() ?? "Not observed"), check);
    }

    private static ShoppingItem Item(uint itemId = 42)
        => new(itemId, $"Item {itemId}", 1, "Collection", null, "7.4", "Source", 0,
            true, false, null, ShoppingCategory.Outfit, 0, 1);

    private static PurchaseListingCandidate Listing(uint itemId = 42, ulong listingId = 1)
        => new(0, itemId, listingId, 1_000, 50, 1, false, false);

    private sealed class Fixture : IDisposable
    {
        internal readonly Context Context = new();
        internal readonly Market Market = new();
        internal readonly ShoppingController Controller;
        private DateTime now = new(2026, 9, 14, 0, 0, 0, DateTimeKind.Utc);

        internal Fixture() => Controller = new ShoppingController(Context, Market, () => now);
        public void Dispose() => Controller.Dispose();
        internal void Advance(int milliseconds) { now = now.AddMilliseconds(milliseconds); Market.Tick(); }
        internal void PreparePurchase(uint itemId = 42)
        {
            Market.Tick();
            Market.Offer(itemId);
            Advance(250);
        }
        internal void Reach(ShoppingPhase phase)
        {
            Assert.True(Controller.Start([Item()]));
            if (phase == ShoppingPhase.WaitingForListings) Market.Tick();
            if (phase == ShoppingPhase.WaitingForPurchase) PreparePurchase();
            Assert.Equal(phase, Controller.Phase);
        }
    }

    private sealed class Context : IShoppingContext
    {
        public IReadOnlyList<CollectionDataOwnershipSourceState> DiagnosticSources { get; set; } = Array.Empty<CollectionDataOwnershipSourceState>();
        public ulong CurrentContentId { get; set; } = 1;
        public uint CurrentWorldId { get; set; } = 1;
        public ShoppingRunSettings Settings { get; set; } = new(10_000, false, 100_000, false, false, 600);
        public IReadOnlySet<uint> CompletedItemIds => Completed;
        internal HashSet<uint> Completed { get; } = [];
        internal bool Missing { get; set; } = true;
        internal List<uint> Confirmed { get; } = [];
        internal List<(uint ItemId, PurchaseListingCandidate Listing)> RequestedLogs { get; } = [];
        internal List<(uint ItemId, ulong ListingId, uint Quantity)> ConfirmedLogs { get; } = [];
        internal List<(uint ItemId, string Name, string Reason)> SkippedLogs { get; } = [];
        public void LogSkipped(ShoppingItem item, string reason)
            => SkippedLogs.Add((item.ItemId, item.ItemName, reason));
        public ItemOwnership ReadPurchaseReadiness(ShoppingItem item, ulong runContentId)
            => !Missing ? new(false, false, false, "Provider authority unavailable.")
                : new(true, Completed.Contains(item.ItemId), false, "Confirmed purchase history.");
        internal List<string> StoppedLogs { get; } = [];
        public void LogStopped(ShoppingItem item, string reason) => StoppedLogs.Add(reason);
        internal List<uint> SearchRetries { get; } = [];
        internal List<string> SearchRetryReasons { get; } = [];
        public void LogSearchRetry(ShoppingItem item, string reason)
        {
            SearchRetries.Add(item.ItemId);
            SearchRetryReasons.Add(reason);
        }
        public void MarkPurchased(uint itemId, ulong purchaseContentId)
        {
            Assert.Equal(CurrentContentId, purchaseContentId);
            Confirmed.Add(itemId);
            Completed.Add(itemId);
        }
        public void LogRequested(ShoppingItem item, PurchaseListingCandidate listing)
        {
            Assert.Equal(item.ItemId, listing.ItemId);
            RequestedLogs.Add((item.ItemId, listing));
        }
        public void LogConfirmed(ShoppingItem item, ulong listingId, uint quantity)
        {
            Assert.NotEqual(0U, item.ItemId);
            Assert.NotEqual(0UL, listingId);
            Assert.Equal(1U, quantity);
            ConfirmedLogs.Add((item.ItemId, listingId, quantity));
        }
    }

    private sealed class Market : IShoppingMarketBoard
    {
        public IReadOnlyList<DiagnosticPredicate> LastListingRequestChecks { get; set; } = Array.Empty<DiagnosticPredicate>();
        public IReadOnlyList<DiagnosticPredicate> LastSubmissionChecks { get; set; } = Array.Empty<DiagnosticPredicate>();
        public event Action? Updated;
        public event Action<ShoppingOfferings>? OfferingsReceived;
        public event Action<ShoppingPurchase>? ItemPurchased;
        public event Action? EventStreamFaulted;
        public bool IsOpen { get; set; } = true;
        public bool IsAvailable { get; set; } = true;
        internal bool AcceptSearch { get; set; } = true;
        internal bool AcceptPurchase { get; set; } = true;
        internal bool CanRead { get; set; } = true;
        internal uint SearchedItemId { get; set; }
        internal int Searches { get; private set; }
        internal int Disposals { get; private set; }
        internal PurchaseListingCandidate[] Listings { get; set; } = [Listing()];
        internal Func<PurchaseListingCandidate, PurchaseListingCandidate?> Reread { get; set; } = listing => listing;
        internal List<PurchaseListingCandidate> Submissions { get; } = [];
        internal Action? OnSubmit { get; set; }
        internal ShoppingEventQueue PendingEvents { get; } = new();
        internal int SubscriberCount => (Updated?.GetInvocationList().Length ?? 0)
            + (OfferingsReceived?.GetInvocationList().Length ?? 0) + (ItemPurchased?.GetInvocationList().Length ?? 0)
            + (EventStreamFaulted?.GetInvocationList().Length ?? 0);
        public bool RequestListings(uint itemId, out int requestId)
        {
            Searches++;
            SearchedItemId = itemId;
            requestId = 7;
            return AcceptSearch;
        }
        public bool TryReadListings(out uint itemId, out IReadOnlyList<PurchaseListingCandidate> listings)
        {
            itemId = SearchedItemId;
            listings = Listings;
            return CanRead;
        }
        public PurchaseListingCandidate? ReadListing(int index) => Reread(Listings[index]);
        public bool SubmitPurchase(PurchaseListingCandidate expected)
        {
            Submissions.Add(expected);
            OnSubmit?.Invoke();
            return AcceptPurchase;
        }
        public void Dispose() { Disposals++; PendingEvents.Dispose(); }
        internal void Tick()
        {
            var events = PendingEvents.TakePending(out var overflow);
            if (overflow) Fault();
            foreach (var marketEvent in events)
            {
                if (marketEvent.Offerings is { } offerings) OfferingsReceived?.Invoke(offerings);
                if (marketEvent.Purchase is { } purchase) ItemPurchased?.Invoke(purchase);
            }
            Updated?.Invoke();
        }
        internal void Fault() => EventStreamFaulted?.Invoke();
        internal void Offer(uint itemId) => Offer([itemId], 7);
        internal void Offer(uint[] itemIds, int requestId) => OfferingsReceived?.Invoke(new(requestId, itemIds));
        internal void Purchase(uint itemId, uint quantity = 1, bool isHighQuality = false)
            => ItemPurchased?.Invoke(new(itemId + (isHighQuality ? 1_000_000U : 0), itemId, quantity));
        internal Action CaptureCallbacks()
        {
            var update = Updated;
            var offerings = OfferingsReceived;
            var purchase = ItemPurchased;
            var fault = EventStreamFaulted;
            return () =>
            {
                update?.Invoke();
                offerings?.Invoke(new(7, new uint[] { 42 }));
                purchase?.Invoke(new(42, 42, 1));
                fault?.Invoke();
            };
        }
    }
}
