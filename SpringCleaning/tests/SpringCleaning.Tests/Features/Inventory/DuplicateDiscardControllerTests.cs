namespace SpringCleaning.Tests.Features.Inventory;

public sealed class DuplicateDiscardControllerTests
{
    [Fact]
    public void ImpossibleBulkScopeIsRejectedWithoutPartialQueue()
    {
        var fixture = new Fixture();
        fixture.Controller.StartBulk(Enumerable.Range(0, 1025).Select(Duplicate).ToArray());
        Assert.False(fixture.Controller.IsAnyRunning);
        Assert.Equal(0, fixture.Controller.BulkTotal);
        Assert.Contains("Too many", fixture.Controller.Status);
    }

    [Fact]
    public void MaximumBoundedPhysicalScopeIsAcceptedInFull()
    {
        var fixture = new Fixture();
        fixture.Controller.StartBulk(Enumerable.Range(0, 1024).Select(Duplicate).ToArray());
        Assert.Equal(1024, fixture.Controller.BulkTotal);
    }
    [Theory]
    [InlineData(false, 8)]
    [InlineData(true, 10)]
    public void DiscardStopsAtDeadlineWhenDresserCannotClose(bool bulk, int deadlineSeconds)
    {
        var fixture = new Fixture();
        fixture.Runtime.CloseAccepted = false;
        if (bulk)
            fixture.Controller.StartBulk(new[] { Duplicate(1), Duplicate(2) });
        else
            fixture.Controller.StartSingle(Duplicate(1));
        fixture.Controller.Update();
        fixture.Clock.Advance(TimeSpan.FromSeconds(deadlineSeconds - 1));
        fixture.Controller.Update();
        Assert.True(fixture.Controller.IsAnyRunning);
        var closeRequests = fixture.Runtime.CloseRequests;

        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        fixture.Controller.Update();
        fixture.Controller.Update();

        Assert.False(fixture.Controller.IsAnyRunning);
        Assert.Equal(closeRequests, fixture.Runtime.CloseRequests);
        Assert.Equal(0, fixture.Runtime.DiscardRequests);
        Assert.Equal(0, fixture.Runtime.ConfirmationRequests);
        Assert.Equal(0, fixture.Controller.BulkCompleted);
        Assert.Contains("did not close", fixture.Controller.Status, StringComparison.Ordinal);
    }

    [Fact]
    public void SingleDiscardWaitsRevalidatesAndRequestsTheNativePrompt()
    {
        var fixture = new Fixture();
        var candidate = Duplicate(1);

        fixture.Controller.StartSingle(candidate);
        fixture.Controller.Update();
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(900));
        fixture.Controller.Update();

        Assert.Equal(1, fixture.Runtime.CloseRequests);
        Assert.Equal(1, fixture.Runtime.SafetyChecks);
        Assert.Equal(1, fixture.Runtime.DiscardRequests);
        Assert.Equal(0, fixture.Runtime.CancelRequests);
        Assert.True(fixture.Controller.IsActive(candidate));
    }

    [Fact]
    public void SingleDiscardStopsWhenTheSlotChanged()
    {
        var fixture = new Fixture();
        fixture.Runtime.Safe = false;
        var candidate = Duplicate(1);

        fixture.Controller.StartSingle(candidate);
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        fixture.Controller.Update();

        Assert.False(fixture.Controller.IsAnyRunning);
        Assert.Equal(0, fixture.Runtime.DiscardRequests);
        Assert.Contains("changed", fixture.Controller.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SingleDiscardCompletesOnlyAfterTheItemDisappears()
    {
        var fixture = new Fixture();
        var candidate = Duplicate(1);
        fixture.Controller.StartSingle(candidate);
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(900));
        fixture.Controller.Update();
        fixture.Runtime.Present = false;

        fixture.Controller.Update();

        Assert.False(fixture.Controller.IsAnyRunning);
        Assert.Contains("Discarded", fixture.Controller.Status, StringComparison.Ordinal);
        Assert.Equal(TimeSpan.FromMilliseconds(500), Assert.Single(fixture.RefreshRequests));
    }

    [Fact]
    public void SingleDiscardDoesNotSelectAnUnownedConfirmation()
    {
        var fixture = new Fixture();
        fixture.Runtime.ConfirmationOpen = true;
        var candidate = Duplicate(1);
        fixture.Controller.StartSingle(candidate);
        fixture.Clock.Advance(TimeSpan.FromSeconds(8));

        fixture.Controller.Update();

        Assert.False(fixture.Controller.IsAnyRunning);
        Assert.Equal(0, fixture.Runtime.DiscardRequests);
        Assert.Equal(0, fixture.Runtime.ConfirmationRequests);
        Assert.Equal(0, fixture.Runtime.CancelRequests);
        Assert.Equal(
            "The FFXIV discard confirmation timed out. Spring Cleaning stopped.",
            fixture.Controller.Status);
    }

    [Fact]
    public void SingleDiscardTimesOutWhileOccupied()
    {
        var fixture = new Fixture();
        fixture.Runtime.Occupied = true;
        var candidate = Duplicate(1);
        fixture.Controller.StartSingle(candidate);
        fixture.Clock.Advance(TimeSpan.FromSeconds(8));

        fixture.Controller.Update();

        Assert.False(fixture.Controller.IsAnyRunning);
        Assert.Contains("occupied", fixture.Controller.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SingleDiscardRetriesADeclinedPromptRequestUntilItsDeadline()
    {
        var fixture = new Fixture();
        fixture.Runtime.DiscardAccepted = false;
        var candidate = Duplicate(1);
        fixture.Controller.StartSingle(candidate);
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));

        fixture.Controller.Update();
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(250));
        fixture.Controller.Update();
        fixture.Clock.Advance(TimeSpan.FromSeconds(7));
        fixture.Controller.Update();

        Assert.False(fixture.Controller.IsAnyRunning);
        Assert.Equal(2, fixture.Runtime.DiscardRequests);
        Assert.Contains("changed", fixture.Controller.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BulkDiscardAdvancesOnlyAfterEachItemDisappears()
    {
        var fixture = new Fixture();
        fixture.Runtime.DresserOpen = false;
        var first = Duplicate(1);
        var second = Duplicate(2);
        fixture.Controller.StartBulk(new[] { first, second });
        Assert.True(fixture.Controller.IsQueued(first));
        Assert.True(fixture.Controller.IsQueued(second));

        fixture.Controller.Update();
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(250));
        fixture.Controller.Update();
        fixture.Runtime.Present = false;
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(250));
        fixture.Controller.Update();

        Assert.Equal(1, fixture.Controller.BulkCompleted);
        Assert.Equal(2, fixture.Controller.BulkTotal);
        Assert.Equal(TimeSpan.FromMilliseconds(750), Assert.Single(fixture.RefreshRequests));
    }

    [Fact]
    public void BulkDiscardStopsOnUnsafeStaleCandidate()
    {
        var fixture = new Fixture();
        fixture.Runtime.Safe = false;
        fixture.Runtime.DresserOpen = false;
        fixture.Controller.StartBulk(new[] { Duplicate(1), Duplicate(2) });
        fixture.Controller.Update();
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(250));

        fixture.Controller.Update();

        Assert.False(fixture.Controller.IsBulkRunning);
        Assert.Equal(0, fixture.Runtime.DiscardRequests);
        Assert.Contains("manual review", fixture.Controller.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CancellingOwnedBulkPromptSelectsOnlyNativeNo()
    {
        var fixture = new Fixture();
        fixture.Runtime.DresserOpen = false;
        fixture.Controller.StartBulk(new[] { Duplicate(1) });
        fixture.Controller.Update();
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(250));
        fixture.Controller.Update();

        fixture.Controller.CancelBulk();

        Assert.False(fixture.Controller.IsBulkRunning);
        Assert.Equal(1, fixture.Runtime.CancelRequests);
        Assert.True(fixture.Runtime.LastCancelOwnedRequest);
    }

    [Theory]
    [InlineData(true, "Choose Store in Glamour Dresser")]
    [InlineData(false, "slot changed")]
    public void StoreMenuCommandReportsBoundaryResult(bool opened, string expected)
    {
        var fixture = new Fixture();
        fixture.Runtime.InventoryContextOpened = opened;

        fixture.Controller.OpenStoreMenu(Duplicate(1));

        Assert.Contains(expected, fixture.Controller.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NewSingleCommandReplacesObsoletePendingCandidate()
    {
        var fixture = new Fixture();
        var obsolete = Duplicate(1);
        var current = Duplicate(2);
        fixture.Controller.StartSingle(obsolete);
        fixture.Controller.StartSingle(current);

        Assert.False(fixture.Controller.IsActive(obsolete));
        Assert.True(fixture.Controller.IsActive(current));
    }

    [Fact]
    public void BulkCommandIncludesBothCarriedSourcesBeyondFourBagCapacity()
    {
        var fixture = new Fixture();
        var candidates = Enumerable.Range(0, 145).Select(Duplicate).ToArray();

        fixture.Controller.StartBulk(candidates);

        Assert.Equal(145, fixture.Controller.BulkTotal);
        Assert.Equal(0, fixture.Controller.BulkCompleted);
        Assert.True(fixture.Controller.IsAnyRunning);
        Assert.True(fixture.Controller.IsQueued(candidates[139]));
        Assert.True(fixture.Controller.IsQueued(candidates[144]));
        Assert.Equal("Preparing to discard 145 duplicates…", fixture.Controller.Status);
    }

    [Fact]
    public void EmptyBulkCommandHasNoActiveWork()
    {
        var fixture = new Fixture();

        fixture.Controller.StartBulk(Array.Empty<InventoryDuplicateItem>());
        fixture.Controller.Update();

        Assert.False(fixture.Controller.IsBulkRunning);
        Assert.False(fixture.Controller.IsAnyRunning);
        Assert.Equal("Preparing to discard 0 duplicates…", fixture.Controller.Status);
    }

    [Fact]
    public void BulkCompletionReportsIntermediateAndFinalProgressExactly()
    {
        var fixture = new Fixture();
        fixture.Runtime.DresserOpen = false;
        var first = Duplicate(1);
        var second = Duplicate(2);
        fixture.Controller.StartBulk(new[] { first, second });

        fixture.Controller.Update();
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(250));
        fixture.Controller.Update();
        fixture.Runtime.Present = false;
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(250));
        fixture.Controller.Update();
        Assert.Equal("Discarded 1 of 2 safe carried copies.", fixture.Controller.Status);

        fixture.Runtime.Present = true;
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(500));
        fixture.Controller.Update();
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(250));
        fixture.Controller.Update();
        fixture.Runtime.Present = false;
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(250));
        fixture.Controller.Update();

        Assert.False(fixture.Controller.IsBulkRunning);
        Assert.Equal(2, fixture.Controller.BulkCompleted);
        Assert.Equal("Discarded 2 safe carried copies.", fixture.Controller.Status);
        Assert.Equal(new[] { TimeSpan.FromMilliseconds(750), TimeSpan.FromMilliseconds(750) }, fixture.RefreshRequests);
    }

    [Fact]
    public void BulkWaitsForDresserClosureBeforeCheckingSafety()
    {
        var fixture = new Fixture();
        var candidate = Duplicate(1);
        fixture.Controller.StartBulk(new[] { candidate });
        fixture.Controller.Update();
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(250));

        fixture.Controller.Update();

        Assert.True(fixture.Controller.IsActive(candidate));
        Assert.Equal(1, fixture.Runtime.CloseRequests);
        Assert.Equal(0, fixture.Runtime.SafetyChecks);
        Assert.Equal("Closing the Glamour Dresser before discarding Item 1…", fixture.Controller.Status);
    }

    [Fact]
    public void BulkDoesNotSelectAnUnownedConfirmation()
    {
        var fixture = new Fixture();
        fixture.Runtime.DresserOpen = false;
        fixture.Runtime.ConfirmationOpen = true;
        fixture.Controller.StartBulk(new[] { Duplicate(1) });
        fixture.Controller.Update();
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(250));

        fixture.Controller.Update();
        Assert.True(fixture.Controller.IsBulkRunning);
        Assert.Equal("Waiting for FFXIV to confirm discarding Item 1…", fixture.Controller.Status);
        Assert.Equal(0, fixture.Runtime.ConfirmationRequests);

        fixture.Clock.Advance(TimeSpan.FromMilliseconds(9_750));
        fixture.Controller.Update();

        Assert.False(fixture.Controller.IsBulkRunning);
        Assert.Equal("The FFXIV discard confirmation timed out. Batch discard stopped.", fixture.Controller.Status);
        Assert.Equal(0, fixture.Runtime.ConfirmationRequests);
        Assert.Equal(TimeSpan.FromMilliseconds(750), Assert.Single(fixture.RefreshRequests));
    }

    [Fact]
    public void BulkSelectsAnOwnedConfirmationOnce()
    {
        var fixture = new Fixture();
        fixture.Runtime.DresserOpen = false;
        var candidate = Duplicate(1);
        fixture.Controller.StartBulk([candidate]);
        fixture.Controller.Update();
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(250));
        fixture.Controller.Update();
        fixture.Runtime.ConfirmationOpen = true;
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(250));

        fixture.Controller.Update();
        Assert.Equal("Discarding Item 1 · 1 of 1…", fixture.Controller.Status);
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(250));
        fixture.Controller.Update();

        Assert.True(fixture.Controller.IsBulkRunning);
        Assert.Equal(1, fixture.Runtime.DiscardRequests);
        Assert.Equal(1, fixture.Runtime.ConfirmationRequests);
        Assert.True(fixture.Runtime.LastConfirmationOwnedRequest);
        Assert.Same(candidate, fixture.Runtime.LastConfirmedCandidate);
    }

    [Fact]
    public void BulkOccupiedWaitsUntilTheExactDeadlineThenStops()
    {
        var fixture = new Fixture();
        fixture.Runtime.DresserOpen = false;
        fixture.Runtime.Occupied = true;
        fixture.Controller.StartBulk(new[] { Duplicate(1) });
        fixture.Controller.Update();
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(250));

        fixture.Controller.Update();
        Assert.True(fixture.Controller.IsBulkRunning);

        fixture.Clock.Advance(TimeSpan.FromMilliseconds(9_750));
        fixture.Controller.Update();

        Assert.False(fixture.Controller.IsBulkRunning);
        Assert.Equal("The game remained occupied. Batch discard stopped without touching the remaining copies.", fixture.Controller.Status);
    }

    [Fact]
    public void BulkOwnedRequestWaitsUntilTheExactDeadlineThenStops()
    {
        var fixture = new Fixture();
        fixture.Runtime.DresserOpen = false;
        fixture.Controller.StartBulk(new[] { Duplicate(1) });
        fixture.Controller.Update();
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(250));
        fixture.Controller.Update();

        Assert.Equal("Requesting discard confirmation for Item 1…", fixture.Controller.Status);
        Assert.Equal(1, fixture.Runtime.DiscardRequests);

        fixture.Clock.Advance(TimeSpan.FromMilliseconds(250));
        fixture.Controller.Update();
        Assert.True(fixture.Controller.IsBulkRunning);
        Assert.Equal(1, fixture.Runtime.DiscardRequests);

        fixture.Clock.Advance(TimeSpan.FromMilliseconds(9_500));
        fixture.Controller.Update();

        Assert.False(fixture.Controller.IsBulkRunning);
        Assert.Equal("The game did not open a discard confirmation for Item 1. Batch discard stopped.", fixture.Controller.Status);
        Assert.Equal(1, fixture.Runtime.DiscardRequests);
    }

    [Fact]
    public void BulkRejectedRequestRetriesThenStopsAtTheExactDeadline()
    {
        var fixture = new Fixture();
        fixture.Runtime.DresserOpen = false;
        fixture.Runtime.DiscardAccepted = false;
        fixture.Controller.StartBulk(new[] { Duplicate(1) });
        fixture.Controller.Update();
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(250));
        fixture.Controller.Update();
        Assert.True(fixture.Controller.IsBulkRunning);

        fixture.Clock.Advance(TimeSpan.FromMilliseconds(250));
        fixture.Controller.Update();
        Assert.Equal(2, fixture.Runtime.DiscardRequests);

        fixture.Clock.Advance(TimeSpan.FromMilliseconds(9_500));
        fixture.Controller.Update();

        Assert.False(fixture.Controller.IsBulkRunning);
        Assert.Equal(2, fixture.Runtime.DiscardRequests);
        Assert.Equal("An inventory slot changed before it could be discarded. Batch discard stopped.", fixture.Controller.Status);
    }

    [Fact]
    public void CancelAllCancelsAnOwnedSinglePromptAndClearsEveryWorkKind()
    {
        var fixture = new Fixture();
        var candidate = Duplicate(1);
        fixture.Controller.StartSingle(candidate);
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(900));
        fixture.Controller.Update();
        Assert.True(fixture.Controller.IsAnyRunning);

        fixture.Controller.CancelAll();
        Assert.Equal(1, fixture.Runtime.CancelRequests);
        fixture.Controller.CancelAll();

        Assert.False(fixture.Controller.IsAnyRunning);
        Assert.False(fixture.Controller.IsActive(candidate));
        Assert.Equal(1, fixture.Runtime.CancelRequests);
        Assert.True(fixture.Runtime.LastCancelOwnedRequest);
        Assert.Equal("Discarding duplicates cancelled.", fixture.Controller.Status);
    }

    [Fact]
    public void CancelAllCancelsAnOwnedBulkPromptAndClearsEveryWorkKind()
    {
        var fixture = new Fixture();
        fixture.Runtime.DresserOpen = false;
        var candidate = Duplicate(1);
        fixture.Controller.StartBulk(new[] { candidate });
        fixture.Controller.Update();
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(250));
        fixture.Controller.Update();
        Assert.Equal(1, fixture.Runtime.DiscardRequests);

        fixture.Controller.CancelAll();

        Assert.False(fixture.Controller.IsAnyRunning);
        Assert.False(fixture.Controller.IsActive(candidate));
        Assert.Equal(1, fixture.Runtime.CancelRequests);
        Assert.True(fixture.Runtime.LastCancelOwnedRequest);
        Assert.Equal("Discarding duplicates cancelled.", fixture.Controller.Status);
    }

    [Fact]
    public void CancelBulkWithoutAnOwnedPromptDoesNotTouchNativeConfirmation()
    {
        var fixture = new Fixture();
        fixture.Controller.StartBulk(new[] { Duplicate(1) });

        fixture.Controller.CancelBulk();

        Assert.False(fixture.Controller.IsBulkRunning);
        Assert.Equal(0, fixture.Runtime.CancelRequests);
        Assert.Equal("Discarding duplicates cancelled.", fixture.Controller.Status);
        Assert.Equal(TimeSpan.FromMilliseconds(750), Assert.Single(fixture.RefreshRequests));
    }

    [Fact]
    public void SingleDiscardClosesAReopenedDresserWithoutAdvancingThePrompt()
    {
        var fixture = new Fixture();
        var candidate = Duplicate(1);
        fixture.Controller.StartSingle(candidate);
        fixture.Runtime.DresserOpen = true;
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(900));

        fixture.Controller.Update();

        Assert.True(fixture.Controller.IsActive(candidate));
        Assert.Equal(2, fixture.Runtime.CloseRequests);
        Assert.Equal(0, fixture.Runtime.SafetyChecks);
    }

    [Fact]
    public void SingleOwnedRequestWaitsUntilTheExactDeadlineThenStops()
    {
        var fixture = new Fixture();
        var candidate = Duplicate(1);
        fixture.Controller.StartSingle(candidate);
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(900));
        fixture.Controller.Update();

        Assert.Equal("Waiting for the discard confirmation for Item 1…", fixture.Controller.Status);
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(250));
        fixture.Controller.Update();
        Assert.True(fixture.Controller.IsAnyRunning);
        Assert.Equal(1, fixture.Runtime.DiscardRequests);

        fixture.Clock.Advance(TimeSpan.FromMilliseconds(6_850));
        fixture.Controller.Update();

        Assert.False(fixture.Controller.IsAnyRunning);
        Assert.Equal("The game did not open a discard confirmation for Item 1.", fixture.Controller.Status);
        Assert.Equal(1, fixture.Runtime.DiscardRequests);
    }

    [Fact]
    public void SingleUnownedConfirmationKeepsWaitingWithoutSelectingYes()
    {
        var fixture = new Fixture();
        var candidate = Duplicate(1);
        fixture.Controller.StartSingle(candidate);
        fixture.Runtime.ConfirmationOpen = true;
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(900));

        fixture.Controller.Update();

        Assert.True(fixture.Controller.IsAnyRunning);
        Assert.Equal("Waiting for FFXIV to confirm discarding Item 1…", fixture.Controller.Status);
        Assert.Equal(0, fixture.Runtime.ConfirmationRequests);
        Assert.Equal(0, fixture.Runtime.CancelRequests);
    }

    [Fact]
    public void SingleSelectsAnOwnedConfirmationOnce()
    {
        var fixture = new Fixture();
        var candidate = Duplicate(1);
        fixture.Controller.StartSingle(candidate);
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(900));
        fixture.Controller.Update();
        fixture.Runtime.ConfirmationOpen = true;

        fixture.Controller.Update();
        Assert.Equal("Discarding Item 1…", fixture.Controller.Status);
        fixture.Controller.Update();

        Assert.True(fixture.Controller.IsAnyRunning);
        Assert.Equal(1, fixture.Runtime.DiscardRequests);
        Assert.Equal(1, fixture.Runtime.ConfirmationRequests);
        Assert.True(fixture.Runtime.LastConfirmationOwnedRequest);
        Assert.Same(candidate, fixture.Runtime.LastConfirmedCandidate);
    }

    [Fact]
    public void SingleOccupiedBeforeDeadlineKeepsWaitingWithoutOpeningAPrompt()
    {
        var fixture = new Fixture();
        var candidate = Duplicate(1);
        fixture.Controller.StartSingle(candidate);
        fixture.Runtime.Occupied = true;
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(900));

        fixture.Controller.Update();

        Assert.True(fixture.Controller.IsAnyRunning);
        Assert.Equal(0, fixture.Runtime.SafetyChecks);
        Assert.Equal(0, fixture.Runtime.DiscardRequests);
    }

    [Fact]
    public void PublicCommandsRejectNullCandidates()
    {
        var fixture = new Fixture();

        var bulkError = Assert.Throws<ArgumentNullException>(() => fixture.Controller.StartBulk(null!));
        var singleError = Assert.Throws<ArgumentNullException>(() => fixture.Controller.StartSingle(null!));
        var menuError = Assert.Throws<ArgumentNullException>(() => fixture.Controller.OpenStoreMenu(null!));

        Assert.Equal("candidates", bulkError.ParamName);
        Assert.Equal("candidate", singleError.ParamName);
        Assert.Equal("candidate", menuError.ParamName);
    }

    [Fact]
    public void NewBulkCommandReplacesThePreviousQueue()
    {
        var fixture = new Fixture();
        var obsolete = Duplicate(1);
        var current = Duplicate(2);
        fixture.Controller.StartBulk(new[] { obsolete });

        fixture.Controller.StartBulk(new[] { current });

        Assert.False(fixture.Controller.IsQueued(obsolete));
        Assert.True(fixture.Controller.IsQueued(current));
        Assert.Equal(1, fixture.Controller.BulkTotal);
    }

    [Fact]
    public void StartingSingleReportsTheExactCandidateBeforeClosingTheDresser()
    {
        var fixture = new Fixture();

        fixture.Controller.StartSingle(Duplicate(1));

        Assert.Equal("Closing the Glamour Dresser before discarding Item 1…", fixture.Controller.Status);
        Assert.Equal(1, fixture.Runtime.CloseRequests);
    }

    [Fact]
    public void ActiveBulkWorkPreventsPendingSingleSubmission()
    {
        var fixture = new Fixture();
        fixture.Runtime.DresserOpen = false;
        fixture.Controller.StartSingle(Duplicate(9));
        fixture.Controller.StartBulk(new[] { Duplicate(1) });
        fixture.Controller.Update();
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(900));

        fixture.Controller.Update();

        Assert.Equal(1, fixture.Runtime.DiscardRequests);
        Assert.True(fixture.Controller.IsActive(Duplicate(9)));
        Assert.Equal("Requesting discard confirmation for Item 1…", fixture.Controller.Status);
    }

    [Fact]
    public void CancelAllClearsQueuedBulkWorkBeforeItBecomesActive()
    {
        var fixture = new Fixture();
        var candidate = Duplicate(1);
        fixture.Controller.StartBulk(new[] { candidate });

        fixture.Controller.CancelAll();

        Assert.False(fixture.Controller.IsQueued(candidate));
        Assert.False(fixture.Controller.IsAnyRunning);
    }

    [Fact]
    public void CompletedOwnedBulkRequestCannotCancelALaterUnownedPrompt()
    {
        var fixture = new Fixture();
        fixture.Runtime.DresserOpen = false;
        fixture.Controller.StartBulk(new[] { Duplicate(1) });
        fixture.Controller.Update();
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(250));
        fixture.Controller.Update();
        Assert.Equal(1, fixture.Runtime.DiscardRequests);
        fixture.Runtime.Present = false;
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(250));
        fixture.Controller.Update();

        fixture.Controller.CancelAll();

        Assert.Equal(0, fixture.Runtime.CancelRequests);
    }

    [Fact]
    public void StoppedOwnedBulkRequestCannotCancelALaterUnownedPrompt()
    {
        var fixture = new Fixture();
        fixture.Runtime.DresserOpen = false;
        fixture.Controller.StartBulk(new[] { Duplicate(1) });
        fixture.Controller.Update();
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(250));
        fixture.Controller.Update();
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(9_750));
        fixture.Controller.Update();
        Assert.False(fixture.Controller.IsBulkRunning);

        fixture.Controller.CancelAll();

        Assert.Equal(0, fixture.Runtime.CancelRequests);
    }

    [Fact]
    public void CompletingSingleAfterItsDelayDoesNotSubmitAStaleRequest()
    {
        var fixture = new Fixture();
        fixture.Controller.StartSingle(Duplicate(1));
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(900));
        fixture.Runtime.Present = false;

        fixture.Controller.Update();

        Assert.Equal(0, fixture.Runtime.SafetyChecks);
        Assert.Equal(0, fixture.Runtime.DiscardRequests);
        Assert.False(fixture.Controller.IsAnyRunning);
    }

    [Fact]
    public void InitialSingleDelayPreventsEarlySafetyChecksAndSubmissions()
    {
        var fixture = new Fixture();
        fixture.Controller.StartSingle(Duplicate(1));

        fixture.Controller.Update();

        Assert.Equal(0, fixture.Runtime.SafetyChecks);
        Assert.Equal(0, fixture.Runtime.DiscardRequests);
        Assert.True(fixture.Controller.IsAnyRunning);
    }

    [Fact]
    public void RejectedSingleRequestStopsAtTheExactDeadline()
    {
        var fixture = new Fixture();
        fixture.Runtime.DiscardAccepted = false;
        fixture.Controller.StartSingle(Duplicate(1));
        fixture.Clock.Advance(TimeSpan.FromSeconds(8));

        fixture.Controller.Update();

        Assert.False(fixture.Controller.IsAnyRunning);
        Assert.Equal(0, fixture.Runtime.DiscardRequests);
        Assert.Equal("That inventory slot changed before the discard confirmation could be opened.", fixture.Controller.Status);
    }

    [Fact]
    public void RejectedBulkRequestDoesNotRetryBeforeThePollInterval()
    {
        var fixture = new Fixture();
        fixture.Runtime.DresserOpen = false;
        fixture.Runtime.DiscardAccepted = false;
        fixture.Controller.StartBulk(new[] { Duplicate(1) });
        fixture.Controller.Update();
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(250));
        fixture.Controller.Update();

        fixture.Controller.Update();

        Assert.Equal(1, fixture.Runtime.DiscardRequests);
        Assert.True(fixture.Controller.IsBulkRunning);
    }

    [Fact]
    public void RejectedSingleRequestDoesNotRetryBeforeThePollInterval()
    {
        var fixture = new Fixture();
        fixture.Runtime.DiscardAccepted = false;
        fixture.Controller.StartSingle(Duplicate(1));
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(900));
        fixture.Controller.Update();

        fixture.Controller.Update();

        Assert.Equal(1, fixture.Runtime.DiscardRequests);
        Assert.True(fixture.Controller.IsAnyRunning);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CopyMovedBeforeRequestStopsWithoutClaimingDiscard(bool bulk)
    {
        var fixture = new Fixture();
        if (bulk)
            fixture.Controller.StartBulk([Duplicate(1), Duplicate(2)]);
        else
            fixture.Controller.StartSingle(Duplicate(1));
        fixture.Runtime.Present = false;
        fixture.Controller.Update();
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(250));
        fixture.Controller.Update();
        Assert.False(fixture.Controller.IsAnyRunning);
        Assert.Equal(0, fixture.Controller.BulkCompleted);
        Assert.Equal(0, fixture.Runtime.DiscardRequests);
        Assert.Contains("moved before", fixture.Controller.Status);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void UnavailableInventoryNeverProvesDiscardAndStopsAtDeadline(bool bulk, bool issued)
    {
        // Arrange
        var fixture = new Fixture();
        fixture.Runtime.DresserOpen = false;
        if (bulk)
            fixture.Controller.StartBulk([Duplicate(1), Duplicate(2)]);
        else
            fixture.Controller.StartSingle(Duplicate(1));
        fixture.Controller.Update();
        if (issued)
        {
            fixture.Clock.Advance(TimeSpan.FromMilliseconds(900));
            fixture.Controller.Update();
        }
        fixture.Runtime.Present = null;
        fixture.Runtime.ConfirmationOpen = true;

        // Act: a missing native container or provider scope is not an empty slot.
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        fixture.Controller.Update();

        // Assert
        Assert.True(fixture.Controller.IsAnyRunning);
        Assert.Equal(0, fixture.Controller.BulkCompleted);
        Assert.Equal(issued ? 1 : 0, fixture.Runtime.DiscardRequests);
        Assert.Equal(0, fixture.Runtime.ConfirmationRequests);
        Assert.Equal("Waiting for the duplicate's Inventory or Armoury Chest location, kupo!", fixture.Controller.Status);
        var deadlineMilliseconds = bulk ? 10_000 : 8_000;
        var elapsedMilliseconds = issued ? 1_900 : 1_000;
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(deadlineMilliseconds - elapsedMilliseconds));
        fixture.Controller.Update();
        Assert.False(fixture.Controller.IsAnyRunning);
        Assert.Equal(0, fixture.Controller.BulkCompleted);
        Assert.Contains("could not be verified", fixture.Controller.Status);
        Assert.Equal(0, fixture.Runtime.CancelRequests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InventoryObservationCanRecoverWithoutSubmittingTheDiscardTwice(bool bulk)
    {
        var fixture = new Fixture();
        fixture.Runtime.DresserOpen = false;
        if (bulk)
            fixture.Controller.StartBulk([Duplicate(1)]);
        else
            fixture.Controller.StartSingle(Duplicate(1));
        fixture.Controller.Update();
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(900));
        fixture.Controller.Update();
        fixture.Runtime.Present = null;
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(250));
        fixture.Controller.Update();

        fixture.Runtime.Present = true;
        fixture.Runtime.ConfirmationOpen = true;
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(250));
        fixture.Controller.Update();
        Assert.Equal(1, fixture.Runtime.DiscardRequests);
        Assert.Equal(1, fixture.Runtime.ConfirmationRequests);
        fixture.Runtime.Present = false;
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(250));
        fixture.Controller.Update();
        Assert.False(fixture.Controller.IsAnyRunning);
        Assert.Contains("Discarded", fixture.Controller.Status);
    }

    [Theory]
    [InlineData(false, 8)]
    [InlineData(true, 10)]
    public void LateOwnedConfirmationIsNeverAcceptedAfterDeadline(bool bulk, int seconds)
    {
        var fixture = new Fixture();
        fixture.Runtime.DresserOpen = false;
        if (bulk)
            fixture.Controller.StartBulk([Duplicate(1), Duplicate(2)]);
        else
            fixture.Controller.StartSingle(Duplicate(1));
        fixture.Controller.Update();
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(900));
        fixture.Controller.Update();
        Assert.Equal(1, fixture.Runtime.DiscardRequests);

        fixture.Runtime.ConfirmationOpen = true;
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(seconds * 1000 - 900));
        fixture.Controller.Update();
        fixture.Controller.Update();

        Assert.False(fixture.Controller.IsAnyRunning);
        Assert.Equal(0, fixture.Runtime.ConfirmationRequests);
        Assert.Equal(0, fixture.Controller.BulkCompleted);
        Assert.Contains("timed out", fixture.Controller.Status);
    }

    [Theory]
    [InlineData(false, 8)]
    [InlineData(true, 10)]
    public void FirstAvailableFrameAfterDeadlineCannotStartDiscard(bool bulk, int seconds)
    {
        var fixture = new Fixture();
        fixture.Runtime.DresserOpen = false;
        if (bulk)
            fixture.Controller.StartBulk([Duplicate(1)]);
        else
            fixture.Controller.StartSingle(Duplicate(1));
        fixture.Controller.Update();

        fixture.Clock.Advance(TimeSpan.FromSeconds(seconds));
        fixture.Controller.Update();

        Assert.False(fixture.Controller.IsAnyRunning);
        Assert.Equal(0, fixture.Runtime.DiscardRequests);
        Assert.Equal(0, fixture.Runtime.SafetyChecks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancelCarriesTheOwnedItemIdentityAndNeverResumesLater(bool bulk)
    {
        var fixture = new Fixture();
        fixture.Runtime.DresserOpen = false;
        var candidate = Duplicate(1);
        if (bulk)
            fixture.Controller.StartBulk([candidate, Duplicate(2)]);
        else
            fixture.Controller.StartSingle(candidate);
        fixture.Controller.Update();
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(900));
        fixture.Controller.Update();

        fixture.Controller.CancelAll();
        fixture.Runtime.ConfirmationOpen = true;
        fixture.Clock.Advance(TimeSpan.FromSeconds(20));
        fixture.Controller.Update();

        Assert.Same(candidate, fixture.Runtime.LastCancelledCandidate);
        Assert.Equal(1, fixture.Runtime.CancelRequests);
        Assert.Equal(1, fixture.Runtime.DiscardRequests);
        Assert.Equal(0, fixture.Runtime.ConfirmationRequests);
        Assert.False(fixture.Controller.IsAnyRunning);
    }

    [Fact]
    public void FailureObserverSeesCompletedCopiesBeforeTheRemainingQueueIsCleared()
    {
        var fixture = new Fixture();
        fixture.Runtime.DresserOpen = false;
        var observed = new List<(string Reason, int Count, bool Running)>();
        fixture.Controller.Stopped = (reason, count) =>
            observed.Add((reason, count, fixture.Controller.IsBulkRunning));
        fixture.Controller.StartBulk([Duplicate(1), Duplicate(2)]);
        fixture.Controller.Update();
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(250));
        fixture.Controller.Update();
        fixture.Runtime.Present = false;
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(250));
        fixture.Controller.Update();
        fixture.Runtime.Present = true;
        fixture.Runtime.Safe = false;
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(500));
        fixture.Controller.Update();
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(250));

        fixture.Controller.Update();

        var failure = Assert.Single(observed);
        Assert.Equal(1, failure.Count);
        Assert.True(failure.Running);
        Assert.Contains("manual review", failure.Reason);
        Assert.False(fixture.Controller.IsAnyRunning);
        Assert.Equal(1, fixture.Runtime.DiscardRequests);
    }

    private static InventoryDuplicateItem Duplicate(int slot) => new(
        0,
        slot,
        (uint)(1000 + slot),
        false,
        $"Item {slot}",
        0,
        new InventoryItemSafety(true, true, "Ready"),
        false,
        true,
        InventoryDuplicateDisposition.RemovalReview);

    private sealed class Fixture
    {
        public Fixture()
        {
            Controller = new DuplicateDiscardController(
                Runtime,
                Clock.GetUtcNow,
                RefreshRequests.Add);
        }

        public TestClock Clock { get; } = new();
        public FakeRuntime Runtime { get; } = new();
        public List<TimeSpan> RefreshRequests { get; } = [];
        public DuplicateDiscardController Controller { get; }
    }

    private sealed class TestClock
    {
        private DateTime now = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        public DateTime GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now = now.Add(duration);
    }

    private sealed class FakeRuntime : IDuplicateDiscardRuntime
    {
        public bool? Present { get; set; } = true;
        public bool DresserOpen { get; set; } = true;
        public bool CloseAccepted { get; set; } = true;
        public bool ConfirmationOpen { get; set; }
        public bool Occupied { get; set; }
        public bool Safe { get; set; } = true;
        public bool DiscardAccepted { get; set; } = true;
        public bool ConfirmationAccepted { get; set; } = true;
        public bool InventoryContextOpened { get; set; } = true;
        public int CloseRequests { get; private set; }
        public int SafetyChecks { get; private set; }
        public int DiscardRequests { get; private set; }
        public int ConfirmationRequests { get; private set; }
        public int CancelRequests { get; private set; }
        public bool LastConfirmationOwnedRequest { get; private set; }
        public InventoryDuplicateItem? LastConfirmedCandidate { get; private set; }
        public bool LastCancelOwnedRequest { get; private set; }
        public InventoryDuplicateItem? LastCancelledCandidate { get; private set; }
        public bool IsDresserOpen => DresserOpen;
        public bool IsDiscardConfirmationOpen => ConfirmationOpen;
        public bool IsOccupied => Occupied;
        public bool? IsInventoryItemPresent(InventoryDuplicateItem candidate) => Present;
        public void CloseDresser()
        {
            CloseRequests++;
            if (CloseAccepted)
                DresserOpen = false;
        }
        public bool CanSafelyOpenDiscardPrompt(InventoryDuplicateItem candidate)
        {
            SafetyChecks++;
            return Safe;
        }
        public bool RequestDiscard(InventoryDuplicateItem candidate)
        {
            DiscardRequests++;
            return DiscardAccepted;
        }
        public bool ConfirmDiscardConfirmation(InventoryDuplicateItem candidate, bool requestIssued)
        {
            ConfirmationRequests++;
            LastConfirmationOwnedRequest = requestIssued;
            LastConfirmedCandidate = candidate;
            return ConfirmationAccepted;
        }
        public void CancelDiscardConfirmation(InventoryDuplicateItem candidate, bool requestIssued)
        {
            CancelRequests++;
            LastCancelOwnedRequest = requestIssued;
            LastCancelledCandidate = candidate;
        }
        public bool OpenInventoryContext(InventoryDuplicateItem candidate) => InventoryContextOpened;
    }
}
