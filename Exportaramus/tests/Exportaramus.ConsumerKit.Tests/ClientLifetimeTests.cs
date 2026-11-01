using Exportaramus.ConsumerKit.V1_2;

namespace Exportaramus.ConsumerKit.Tests;

public sealed class ClientLifetimeTests
{
    private static Task Inline(Action action, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        action();
        return Task.CompletedTask;
    }

    private static CollectionDataIpcClient Client(FakeTransport transport) => new((_, _) => transport, Inline);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NotificationAtExactByteLimitTriggersRefresh(bool multibyte)
    {
        var transport = new FakeTransport();
        await using var client = Client(transport);
        await client.TickAsync(ContractFixture.Character, ContractFixture.Now);
        client.Changed(multibyte ? new string('é', 2048) : new string('x', 4096));
        await client.TickAsync(ContractFixture.Character, ContractFixture.Now.AddSeconds(1));
        Assert.Equal(2, transport.ReadCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResettingScopeAllowsSameSessionToRestartRevision(bool withdrawal)
    {
        var transport = new FakeTransport();
        transport.Snapshot = ContractFixture.Edit(transport.Snapshot, root => root["revision"] = 10);
        await using var client = Client(transport);
        await client.TickAsync(ContractFixture.Character, ContractFixture.Now);
        Assert.Equal(10, client.View.Revision);
        transport.Snapshot = "";
        if (withdrawal) client.Withdrawn(ContractFixture.Read("snapshot-withdrawn-valid.json"));
        else await client.TickAsync(null, ContractFixture.Now);
        await client.TickAsync(ContractFixture.Character, ContractFixture.Now);
        transport.Snapshot = ContractFixture.Edit(ContractFixture.Read("compact-valid-beasts-populated.json"), root => root["revision"] = 1);
        client.Changed("{}");
        await client.TickAsync(ContractFixture.Character, ContractFixture.Now);
        Assert.Equal(CollectionDataAvailability.Ready, client.View.Availability);
        Assert.Equal(1, client.View.Revision);
    }

    [Fact]
    public async Task MissingProviderIsRetriedOnDiscoveryAndThenBecomesReady()
    {
        var available = false;
        var attempts = 0;
        var transport = new FakeTransport();
        await using var client = new CollectionDataIpcClient((_, _) =>
        {
            attempts++;
            return available ? transport : throw new InvalidOperationException("Not registered.");
        }, Inline);
        await client.TickAsync(ContractFixture.Character, ContractFixture.Now);
        Assert.Equal(CollectionDataAvailability.Unavailable, client.View.Availability);
        var failedAttempts = attempts;
        available = true;
        await client.TickAsync(ContractFixture.Character, ContractFixture.Now.AddSeconds(4));
        Assert.Equal(failedAttempts, attempts);
        await client.TickAsync(ContractFixture.Character, ContractFixture.Now.AddSeconds(5));
        Assert.Equal(CollectionDataAvailability.Ready, client.View.Availability);
        Assert.Equal(ContractFixture.Character, client.View.ContentId);
    }

    [Fact]
    public async Task ProviderReadFailureClearsTheViewAndRebinds()
    {
        var first = new FakeTransport();
        var replacement = new FakeTransport();
        var bindings = 0;
        await using var client = new CollectionDataIpcClient((_, _) => ++bindings == 1 ? first : replacement, Inline);
        await client.TickAsync(ContractFixture.Character, ContractFixture.Now);
        Assert.NotNull(client.View.Snapshot);
        first.FailReads = true;
        await client.TickAsync(ContractFixture.Character, ContractFixture.Now.AddSeconds(5));
        Assert.Null(client.View.Snapshot);
        Assert.Equal(CollectionDataValidationCode.ProviderUnavailable, client.View.Failure);
        Assert.Equal(1, first.DisposeCount);
        await client.TickAsync(ContractFixture.Character, ContractFixture.Now.AddSeconds(10));
        Assert.Equal(CollectionDataAvailability.Ready, client.View.Availability);
        Assert.Equal(2, bindings);
    }

    [Fact]
    public async Task NotificationsCoalesceAndBypassTheDiscoveryDelay()
    {
        var transport = new FakeTransport();
        await using var client = Client(transport);
        await client.TickAsync(ContractFixture.Character, ContractFixture.Now);
        for (var i = 0; i < 100; i++)
            client.Changed("{}");
        await client.TickAsync(ContractFixture.Character, ContractFixture.Now.AddSeconds(1));
        await client.TickAsync(ContractFixture.Character, ContractFixture.Now.AddSeconds(2));
        Assert.Equal(2, transport.ReadCount);
        client.Changed(new string('x', 4097));
        await client.TickAsync(ContractFixture.Character, ContractFixture.Now.AddSeconds(3));
        Assert.Equal(2, transport.ReadCount);
    }

    [Fact]
    public async Task LowerRevisionIsRejectedButNewProviderSessionCanRestartAtOne()
    {
        var transport = new FakeTransport();
        transport.Snapshot = ContractFixture.Edit(transport.Snapshot, value => value["revision"] = 10);
        await using var client = Client(transport);
        await client.TickAsync(ContractFixture.Character, ContractFixture.Now);
        Assert.Equal(10, client.View.Revision);
        transport.Snapshot = ContractFixture.Edit(transport.Snapshot, value => value["revision"] = 1);
        client.Changed("{}");
        await client.TickAsync(ContractFixture.Character, ContractFixture.Now);
        Assert.Equal(CollectionDataValidationCode.LowerRevision, client.View.Failure);
        Assert.Null(client.View.Snapshot);
        transport.Capabilities = ContractFixture.Edit(transport.Capabilities, value => value["providerSessionId"] = ContractFixture.OtherSession);
        transport.Snapshot = ContractFixture.Edit(transport.Snapshot, value => value["providerSessionId"] = ContractFixture.OtherSession);
        client.Changed("{}");
        await client.TickAsync(ContractFixture.Character, ContractFixture.Now);
        Assert.Equal(CollectionDataAvailability.Ready, client.View.Availability);
        Assert.Equal(1, client.View.Revision);
    }

    [Fact]
    public async Task EqualRevisionWithDifferentBytesIsRejected()
    {
        var transport = new FakeTransport();
        await using var client = Client(transport);
        await client.TickAsync(ContractFixture.Character, ContractFixture.Now);
        transport.Snapshot += " ";
        client.Changed("{}");
        await client.TickAsync(ContractFixture.Character, ContractFixture.Now);
        Assert.Equal(CollectionDataValidationCode.EqualRevisionConflict, client.View.Failure);
        Assert.Null(client.View.Snapshot);
    }

    [Fact]
    public async Task LogoutAndCharacterSwitchCannotReuseThePreviousCharactersSnapshot()
    {
        var transport = new FakeTransport();
        await using var client = Client(transport);
        await client.TickAsync(ContractFixture.Character, ContractFixture.Now);
        await client.TickAsync(null, ContractFixture.Now);
        Assert.Null(client.View.Snapshot);
        Assert.Equal(CollectionDataValidationCode.NoPayload, client.View.Failure);
        await client.TickAsync("42", ContractFixture.Now);
        Assert.Null(client.View.Snapshot);
        Assert.Equal(CollectionDataValidationCode.WrongCharacter, client.View.Failure);
        transport.Snapshot = ContractFixture.Edit(transport.Snapshot, value => value["contentId"] = "42");
        client.Changed("{}");
        await client.TickAsync("42", ContractFixture.Now);
        Assert.Equal("42", client.View.ContentId);
    }

    [Fact]
    public async Task CharacterChangeWhileWorkerIsPendingRejectsItsResult()
    {
        var transport = new FakeTransport();
        var worker = new DeferredWorker();
        await using var client = new CollectionDataIpcClient((_, _) => transport, worker.Start);
        var old = client.TickAsync(ContractFixture.Character, ContractFixture.Now);
        Assert.Same(old, client.TickAsync("42", ContractFixture.Now));
        Assert.Equal(1, worker.Count);
        worker.CompleteNext();
        await old;
        Assert.Null(client.View.Snapshot);
        Assert.Equal(0, transport.ReadCount);
        transport.Snapshot = ContractFixture.Edit(transport.Snapshot, value => value["contentId"] = "42");
        var current = client.TickAsync("42", ContractFixture.Now);
        worker.CompleteNext();
        await current;
        Assert.Equal("42", client.View.ContentId);
    }

    [Theory]
    [InlineData("invalid", CollectionDataValidationCode.InvalidJson)]
    [InlineData("session", CollectionDataValidationCode.WrongProviderSession)]
    [InlineData("character", CollectionDataValidationCode.WrongCharacter)]
    public async Task InvalidRepliesNeverBecomeUsable(string fault, CollectionDataValidationCode expected)
    {
        var transport = new FakeTransport();
        if (fault == "invalid")
            transport.Snapshot = "{";
        if (fault == "session") transport.Snapshot = ContractFixture.Edit(transport.Snapshot, value => value["providerSessionId"] = ContractFixture.OtherSession);
        if (fault == "character") transport.Snapshot = ContractFixture.Edit(transport.Snapshot, value => value["contentId"] = "42");
        await using var client = Client(transport);
        await client.TickAsync(ContractFixture.Character, ContractFixture.Now);
        Assert.Equal(CollectionDataAvailability.Rejected, client.View.Availability);
        Assert.Equal(expected, client.View.Failure);
        Assert.Null(client.View.Snapshot);
    }

    [Fact]
    public async Task OnlyMatchingWithdrawalRemovesAcceptedData()
    {
        var transport = new FakeTransport();
        await using var client = Client(transport);
        await client.TickAsync(ContractFixture.Character, ContractFixture.Now);
        var notice = ContractFixture.Read("snapshot-withdrawn-valid.json");
        client.Withdrawn(ContractFixture.Edit(notice, value => value["providerSessionId"] = ContractFixture.OtherSession));
        await client.TickAsync(ContractFixture.Character, ContractFixture.Now.AddSeconds(1));
        Assert.Equal(CollectionDataAvailability.Ready, client.View.Availability);
        transport.Snapshot = "";
        client.Withdrawn(notice);
        await client.TickAsync(ContractFixture.Character, ContractFixture.Now.AddSeconds(2));
        Assert.Equal(CollectionDataAvailability.Unavailable, client.View.Availability);
        Assert.Null(client.View.Snapshot);
    }

    [Theory]
    [InlineData("Accepted", "Accepted")]
    [InlineData("QueueFull", "QueueFull")]
    [InlineData("MadeUpSuccess", "Unavailable")]
    public async Task ReportsRequireReadyMatchingCharacterAndKnownResultCode(string reply, string expected)
    {
        var transport = new FakeTransport { ReportResult = reply };
        await using var client = Client(transport);
        Assert.Equal("Unavailable", client.ReportConfirmedAcquisition(ContractFixture.Character, 100, "Example"));
        await client.TickAsync(ContractFixture.Character, ContractFixture.Now);
        Assert.Equal("Unavailable", client.ReportConfirmedAcquisition("42", 100, "Example"));
        Assert.Empty(transport.Reports);
        Assert.Equal(expected, client.ReportConfirmedAcquisition(ContractFixture.Character, 100, "Example"));
        Assert.Equal((ContractFixture.Character, 100u, "Example"), Assert.Single(transport.Reports));
        transport.FailReports = true;
        Assert.Equal("Unavailable", client.ReportConfirmedAcquisition(ContractFixture.Character, 100, "Example"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WithdrawalDuringAReadCannotPublishItsMatchingSnapshot(bool previouslyRejected)
    {
        // Arrange: withdraw after capture but before accepting the response.
        var transport = new FakeTransport();
        var snapshot = transport.Snapshot;
        await using var client = Client(transport);
        if (previouslyRejected)
        {
            transport.Snapshot = "{";
            await client.TickAsync(ContractFixture.Character, ContractFixture.Now);
            Assert.Equal(CollectionDataAvailability.Rejected, client.View.Availability);
            transport.Snapshot = snapshot;
            client.Changed("{}");
        }
        transport.BeforeSnapshotReturns = () => client.Withdrawn(ContractFixture.Read("snapshot-withdrawn-valid.json"));

        // Act.
        await client.TickAsync(ContractFixture.Character, ContractFixture.Now);

        // Assert: reject the old response and allow a new read.
        Assert.Equal(CollectionDataAvailability.Unavailable, client.View.Availability);
        Assert.Null(client.View.Snapshot);
        transport.BeforeSnapshotReturns = null;
        await client.TickAsync(ContractFixture.Character, ContractFixture.Now);
        Assert.Equal(CollectionDataAvailability.Ready, client.View.Availability);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForeignScopeWithdrawalDuringInitialReadDoesNotDiscardCurrentSnapshot(bool differentCharacter)
    {
        // Arrange.
        var transport = new FakeTransport();
        await using var client = Client(transport);
        var notice = ContractFixture.Edit(ContractFixture.Read("snapshot-withdrawn-valid.json"), value =>
            value[differentCharacter ? "contentId" : "providerSessionId"] =
                differentCharacter ? "42" : ContractFixture.OtherSession);
        transport.BeforeSnapshotReturns = () => client.Withdrawn(notice);

        // Act.
        await client.TickAsync(ContractFixture.Character, ContractFixture.Now);

        // Assert.
        Assert.Equal(CollectionDataAvailability.Ready, client.View.Availability);
        Assert.Equal(ContractFixture.Session, client.View.ProviderSessionId);
    }

    [Fact]
    public async Task FaultedOwnedWorkerStillReleasesItsConnectionAndPropagatesFailure()
    {
        // Arrange: fail the worker outside the refresh action.
        var transport = new FakeTransport();
        var failure = new InvalidOperationException("Worker failed.");
        var client = new CollectionDataIpcClient((_, _) => transport,
            (_, _) => Task.FromException(failure));
        var pending = client.TickAsync(ContractFixture.Character, ContractFixture.Now);
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => pending));

        // Act.
        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => client.DisposeAsync().AsTask());

        // Assert: clean up and report the worker failure.
        Assert.Same(failure, actual);
        Assert.Equal(1, transport.DisposeCount);
        Assert.Equal(CollectionDataAvailability.Disposed, client.View.Availability);
        await client.DisposeAsync();
        Assert.Equal(1, transport.DisposeCount);
    }

    [Fact]
    public async Task DisposalReleasesWorkerCancellationResources()
    {
        var transport = new FakeTransport();
        CancellationToken workerToken = default;
        await using var client = new CollectionDataIpcClient((_, _) => transport, (action, token) =>
        {
            workerToken = token;
            return Inline(action, token);
        });
        await client.TickAsync(ContractFixture.Character, ContractFixture.Now);
        Assert.True(workerToken.CanBeCanceled);
        var handle = workerToken.WaitHandle;
        Assert.False(handle.SafeWaitHandle.IsClosed);

        await client.DisposeAsync();

        Assert.True(handle.SafeWaitHandle.IsClosed);
        Assert.Throws<ObjectDisposedException>(() => { _ = workerToken.WaitHandle; });
    }

    [Fact]
    public async Task DisposeWaitsForOwnedWorkerAndPreventsSubsequentCalls()
    {
        var transport = new FakeTransport();
        var worker = new DeferredWorker();
        var client = new CollectionDataIpcClient((_, _) => transport, worker.Start);
        var pending = client.TickAsync(ContractFixture.Character, ContractFixture.Now);
        var disposal = client.DisposeAsync().AsTask();
        Assert.False(disposal.IsCompleted);
        Assert.Equal(CollectionDataAvailability.Disposed, client.View.Availability);
        worker.CompleteNext();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await disposal;
        await client.DisposeAsync();
        client.Changed("{}");
        client.Withdrawn("{}");
        await client.TickAsync(ContractFixture.Character, ContractFixture.Now.AddSeconds(10));
        Assert.Equal(1, transport.DisposeCount);
        Assert.Equal(0, transport.ReadCount);
        Assert.Equal(0, worker.Count);
        Assert.Equal("ProviderStopping", client.ReportConfirmedAcquisition(ContractFixture.Character, 100, "Example"));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("characters")]
    [InlineData("utf8-bytes")]
    [InlineData("invalid-utf16")]
    public async Task UnboundedOrInvalidlyEncodedNoticesCannotRefreshOrWithdraw(string input)
    {
        // Arrange.
        var transport = new FakeTransport();
        await using var client = Client(transport);
        await client.TickAsync(ContractFixture.Character, ContractFixture.Now);
        var payload = input switch
        {
            "null" => null,
            "characters" => new string('x', 4097),
            "utf8-bytes" => new string('\u00e9', 2049),
            _ => "\ud800",
        };

        // Act.
        client.Changed(payload!);
        client.Withdrawn(payload!);
        await client.TickAsync(ContractFixture.Character, ContractFixture.Now.AddSeconds(1));

        // Assert.
        Assert.Equal(1, transport.ReadCount);
        Assert.Equal(CollectionDataAvailability.Ready, client.View.Availability);
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("character")]
    [InlineData("session")]
    public async Task InvalidOrUnrelatedWithdrawalsDoNotClearAcceptedData(string fault)
    {
        // Arrange.
        var transport = new FakeTransport();
        await using var client = Client(transport);
        await client.TickAsync(ContractFixture.Character, ContractFixture.Now);
        var notice = ContractFixture.Read("snapshot-withdrawn-valid.json");
        var payload = fault == "malformed" ? "{}" : ContractFixture.Edit(notice, value =>
            value[fault == "character" ? "contentId" : "providerSessionId"] =
                fault == "character" ? "42" : ContractFixture.OtherSession);

        // Act.
        client.Withdrawn(payload);
        await client.TickAsync(ContractFixture.Character, ContractFixture.Now.AddSeconds(1));

        // Assert.
        Assert.Equal(CollectionDataAvailability.Ready, client.View.Availability);
        Assert.Equal(1, transport.ReadCount);
        Assert.Equal(ContractFixture.Character, client.View.ContentId);
    }

    [Fact]
    public async Task WithdrawalQueueOverflowInvalidatesAcceptedDataEvenWhenNoticesAreUnrelated()
    {
        // Arrange: overflow the queue so a matching notice may be lost.
        var transport = new FakeTransport();
        await using var client = Client(transport);
        await client.TickAsync(ContractFixture.Character, ContractFixture.Now);
        var notice = ContractFixture.Edit(ContractFixture.Read("snapshot-withdrawn-valid.json"),
            value => value["providerSessionId"] = ContractFixture.OtherSession);
        transport.Snapshot = "";
        for (var i = 0; i < 17; i++) client.Withdrawn(notice);

        // Act.
        await client.TickAsync(ContractFixture.Character, ContractFixture.Now.AddSeconds(1));

        // Assert: overflow triggers an early reread.
        Assert.Equal(2, transport.ReadCount);
        Assert.Equal(CollectionDataAvailability.Unavailable, client.View.Availability);
        Assert.Null(client.View.Snapshot);
    }

    [Fact]
    public async Task BadCapabilitiesAreRejectedBeforeReadingAnySnapshot()
    {
        // Arrange.
        var transport = new FakeTransport { Capabilities = "{" };
        await using var client = Client(transport);

        // Act.
        await client.TickAsync(ContractFixture.Character, ContractFixture.Now);

        // Assert.
        Assert.Equal(CollectionDataValidationCode.InvalidJson, client.View.Failure);
        Assert.Equal(CollectionDataAvailability.Rejected, client.View.Availability);
        Assert.Equal(0, transport.SnapshotReadCount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CharacterChangeOrDisposalDuringAReadSuppressesSuccessAndRejection(bool dispose, bool invalidReply)
    {
        // Arrange: invalidate the scope after capturing the reply.
        var transport = new FakeTransport();
        if (invalidReply)
            transport.Snapshot = "{";
        var worker = new DeferredWorker();
        var client = new CollectionDataIpcClient((_, _) => transport, worker.Start);
        Task? cleanup = null;
        transport.BeforeSnapshotReturns = () =>
        {
            if (dispose) cleanup = client.DisposeAsync().AsTask();
            else _ = client.TickAsync("42", ContractFixture.Now);
        };

        // Act.
        var pending = client.TickAsync(ContractFixture.Character, ContractFixture.Now);
        worker.CompleteNext();
        await pending;
        if (cleanup is not null) await cleanup;

        // Assert: neither an old result nor an old error changes the new state.
        Assert.Null(client.View.Snapshot);
        Assert.Equal(dispose ? CollectionDataAvailability.Disposed : CollectionDataAvailability.Unavailable,
            client.View.Availability);
        Assert.Equal(dispose ? CollectionDataValidationCode.Disposed : CollectionDataValidationCode.NoPayload,
            client.View.Failure);
        await client.DisposeAsync();
        Assert.Equal(1, transport.DisposeCount);
    }

    [Fact]
    public async Task DefaultWorkerCompletesARealBackgroundRefresh()
    {
        // Arrange: use the default scheduler.
        var transport = new FakeTransport();
        await using var client = new CollectionDataIpcClient((_, _) => transport);

        // Act.
        await client.TickAsync(ContractFixture.Character, ContractFixture.Now).WaitAsync(TimeSpan.FromSeconds(2));

        // Assert.
        Assert.Equal(CollectionDataAvailability.Ready, client.View.Availability);
        Assert.Equal(1, transport.SnapshotReadCount);
    }

    [Fact]
    public async Task NeverConnectedClientCanReportUnavailabilityAndDisposeWithoutWork()
    {
        // Arrange.
        var client = new CollectionDataIpcClient((_, _) => throw new InvalidOperationException("Provider absent."));
        Assert.Equal("Unavailable", client.ReportConfirmedAcquisition(ContractFixture.Character, 100, "Example"));

        // Act.
        await client.DisposeAsync();
        client.Changed("{}");
        client.Withdrawn("{}");
        await client.TickAsync(ContractFixture.Character, ContractFixture.Now);

        // Assert.
        Assert.Equal(CollectionDataAvailability.Disposed, client.View.Availability);
        Assert.Null(client.View.Snapshot);
        await client.DisposeAsync();
    }

    [Fact]
    public async Task CleanupFailureDuringDisconnectDoesNotPreventLaterDiscovery()
    {
        // Arrange.
        var first = new FakeTransport { FailDisposal = true };
        var replacement = new FakeTransport();
        var attempts = 0;
        await using var client = new CollectionDataIpcClient((_, _) => ++attempts == 1 ? first : replacement, Inline);
        await client.TickAsync(ContractFixture.Character, ContractFixture.Now);
        first.FailReads = true;

        // Act.
        await client.TickAsync(ContractFixture.Character, ContractFixture.Now.AddSeconds(5));
        Assert.Equal(CollectionDataValidationCode.ProviderUnavailable, client.View.Failure);
        await client.TickAsync(ContractFixture.Character, ContractFixture.Now.AddSeconds(10));

        // Assert.
        Assert.Equal(1, first.DisposeCount);
        Assert.Equal(CollectionDataAvailability.Ready, client.View.Availability);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task SubscriptionCleanupFailurePropagatesAfterTheClientBecomesDisposed()
    {
        // Arrange.
        var transport = new FakeTransport { FailDisposal = true };
        var client = Client(transport);

        // Act.
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.DisposeAsync().AsTask());

        // Assert.
        Assert.Equal(CollectionDataAvailability.Disposed, client.View.Availability);
        Assert.Equal(1, transport.DisposeCount);
        await client.DisposeAsync();
        Assert.Equal(1, transport.DisposeCount);
    }

    [Fact]
    public async Task CancellationCallbackFailureStillObservesOwnedWorkAndReleasesTheConnection()
    {
        // Arrange.
        var transport = new FakeTransport();
        var callbackCount = 0;
        var client = new CollectionDataIpcClient((_, _) => transport, (action, token) =>
        {
            token.Register(() => { callbackCount++; throw new InvalidOperationException("Cancellation callback failed."); });
            return Inline(action, token);
        });
        await client.TickAsync(ContractFixture.Character, ContractFixture.Now);

        // Act.
        await Assert.ThrowsAsync<AggregateException>(() => client.DisposeAsync().AsTask());

        // Assert.
        Assert.Equal(1, callbackCount);
        Assert.Equal(1, transport.DisposeCount);
        Assert.Equal(CollectionDataAvailability.Disposed, client.View.Availability);
        await client.DisposeAsync();
    }
}
