using System.Text.Json.Nodes;
using Exportaramus.ConsumerKit.V1_2;

namespace CollectionShopper.Tests;

public sealed class ExportaramusConsumerTests
{
    [Fact]
    public async Task PublicProviderReleaseVersionUsesThePinnedVersion12Contract()
    {
        var transport = new Transport();
        var capabilities = JsonNode.Parse(transport.Capabilities)!;
        var snapshot = JsonNode.Parse(transport.Payload)!;
        capabilities["providerVersion"] = "1.0.0.3";
        snapshot["providerVersion"] = "1.0.0.3";
        transport.Capabilities = capabilities.ToJsonString();
        transport.Payload = snapshot.ToJsonString();
        using var consumer = Create(transport);

        await Poll(consumer);

        Assert.Equal(CollectionDataValidationCode.None, consumer.State.Failure);
        Assert.True(consumer.State.CompleteFor(ShoppingCategory.Minion));
        Assert.Equal("1.0.0.3", consumer.State.Snapshot!.ProviderVersion);
    }

    [Fact]
    public void TransportGateConstantsSelectVersion12()
    {
        // Arrange: the independently versioned IPC addresses required by this integration.
        string[] suffixes = [ "GetCapabilities", "GetSnapshot", "SnapshotChanged", "SnapshotWithdrawn", "ReportOwnershipEvidence"];
            var expected = suffixes.Select(name => "Exportaramus.CollectionData.V1_2." + name);
        // Act
        var actual = new[] { CollectionDataGates.GetCapabilities, CollectionDataGates.GetSnapshot,
            CollectionDataGates.SnapshotChanged, CollectionDataGates.SnapshotWithdrawn, CollectionDataGates.ReportOwnershipEvidence };
        // Assert
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("1.0")]
    [InlineData("1.1")]
    [InlineData("1.3")]
    public async Task DifferentContractVersionsCannotAuthorizeMissingItems(string version)
    {
        // Arrange
        var transport = new Transport();
        var capabilities = JsonNode.Parse(transport.Capabilities)!;
        capabilities["schemaVersion"] = version;
        transport.Capabilities = capabilities.ToJsonString();
        using var consumer = Create(transport);
        // Act
        await Poll(consumer);
        // Assert
        Assert.Equal(CollectionDataValidationCode.UnsupportedSchemaVersion, consumer.State.Failure);
        Assert.False(consumer.State.CompleteFor(ShoppingCategory.Minion));
    }

    [Fact]
    public async Task ProviderLossDisposesTheBindingAndRediscoversTheRestartedProvider()
    {
        var original = new Transport();
        var restarted = new Transport();
        var binding = original;
        var bindCount = 0;
        using var consumer = new ExportaramusConsumer((changed, withdrawn) =>
        {
            bindCount++;
            binding.Changed = changed;
            binding.Withdrawn = withdrawn;
            return binding;
        });
        await Poll(consumer);
        Assert.True(consumer.State.CompleteFor(ShoppingCategory.Minion));
        original.ReadSnapshotOverride = () => throw new InvalidOperationException("Provider unloaded.");

        await Poll(consumer, 6);

        Assert.Equal(CollectionDataValidationCode.ProviderUnavailable, consumer.State.Failure);
        Assert.False(consumer.State.Classify(ShoppingCategory.Minion, 99999, 99).Known);
        Assert.Equal(1, original.DisposeCount);
        binding = restarted;
        await Poll(consumer, 12);
        Assert.Equal(2, bindCount);
        Assert.True(consumer.State.CompleteFor(ShoppingCategory.Minion));
        consumer.Dispose();
        await consumer.Disposal;
        Assert.Equal(1, original.DisposeCount);
        Assert.Equal(1, restarted.DisposeCount);
    }

    [Fact]
    public async Task NewProviderSessionCanRestartRevisionAfterRevokingPreparedAuthority()
    {
        const string restartedSession = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
        var transport = new Transport();
        var snapshot = JsonNode.Parse(transport.Payload)!;
        snapshot["revision"] = 9;
        transport.Payload = snapshot.ToJsonString();
        using var consumer = Create(transport);
        await Poll(consumer);
        Assert.Equal(9, consumer.State.Snapshot!.Revision);
        var notice = JsonNode.Parse(ExportaramusFixture.Read("snapshot-changed-valid.json"))!;
        notice["providerSessionId"] = restartedSession;
        notice["revision"] = 1;
        notice["changedDataTypes"] = new JsonArray("Unlocks");
        notice["changedSources"] = new JsonArray("minions");
        transport.Changed!(notice.ToJsonString());
        Assert.Null(consumer.State.Snapshot);
        var capabilities = JsonNode.Parse(transport.Capabilities)!;
        capabilities["providerSessionId"] = restartedSession;
        snapshot["providerSessionId"] = restartedSession;
        snapshot["revision"] = 1;
        transport.Capabilities = capabilities.ToJsonString();
        transport.Payload = snapshot.ToJsonString();

        await Poll(consumer, 1);

        Assert.True(consumer.State.CompleteFor(ShoppingCategory.Minion));
        Assert.Equal(restartedSession, consumer.State.Snapshot!.ProviderSessionId);
        Assert.Equal(1, consumer.State.Snapshot.Revision);
    }

    [Fact]
    public async Task DisposalWaitsForTheOwnedPublicationContinuationWithoutCapturingCallerContext()
    {
        using var release = new ManualResetEventSlim();
        using var entered = new ManualResetEventSlim();
        var transport = new Transport();
        transport.ReadSnapshotOverride = () =>
        {
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
            return transport.Payload;
        };
        var consumer = Create(transport);
        consumer.Tick(ExportaramusFixture.ContentId, ExportaramusFixture.Now);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        var pending = consumer.Completion;
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var gate = typeof(ExportaramusConsumer).GetField("gate", flags)!.GetValue(consumer)!;
        var client = (CollectionDataIpcClient)typeof(ExportaramusConsumer).GetField("client", flags)!.GetValue(consumer)!;
        var worker = client.TickAsync(ExportaramusFixture.ContentId.ToString(System.Globalization.CultureInfo.InvariantCulture), ExportaramusFixture.Now);
        var context = new ObservedContext();
        var previous = SynchronizationContext.Current;
        // Contend on the real publication mutex. No task or provider state is replaced.
        lock (gate)
        {
            release.Set();
            Assert.True(SpinWait.SpinUntil(() => worker.IsCompleted, TimeSpan.FromSeconds(10)));
            Assert.False(pending.IsCompleted);
            try
            {
                SynchronizationContext.SetSynchronizationContext(context);
                consumer.Dispose();
                Assert.False(consumer.Disposal.IsCompleted);
            }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
        }
        await consumer.Disposal;
        await worker;
        Assert.True(pending.IsCompleted);
        Assert.Equal(0, context.Posts);
    }

    [Fact]
    public async Task ProviderPollAndDisposalDoNotQueueWorkOnTheCallingContext()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new Transport();
        transport.ReadSnapshotOverride = () =>
        {
            entered.SetResult();
            Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
            return transport.Payload;
        };
        var consumer = Create(transport);
        var context = new ObservedContext();
        var previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            consumer.Tick(ExportaramusFixture.ContentId, ExportaramusFixture.Now);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        await entered.Task;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            consumer.Dispose();
            var firstDisposal = consumer.Disposal;
            consumer.Dispose();
            Assert.Same(firstDisposal, consumer.Disposal);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        release.Set();
        await consumer.Disposal;
        Assert.True(consumer.Completion.IsCompleted);
        Assert.Equal(0, context.Posts);
    }

    [Fact]
    public async Task MixedRelevantAndUnrelatedChangedSourcesInvalidateUntilReread()
    {
        var transport = new Transport();
        using var consumer = Create(transport);
        await Poll(consumer);
        var notice = JsonNode.Parse(ExportaramusFixture.Read("snapshot-changed-valid.json"))!;
        notice["revision"] = 2;
        notice["changedDataTypes"] = new JsonArray("Unlocks");
        notice["changedSources"] = new JsonArray("achievements", "minions");
        Assert.True(CollectionDataSemanticValidator.ValidateChanged(notice.ToJsonString()).Success);
        transport.Changed!(notice.ToJsonString());
        Assert.Null(consumer.State.Snapshot);
        await Poll(consumer, 1);
        Assert.NotNull(consumer.State.Snapshot);
    }

    [Fact]
    public async Task DisposalPublishesPendingStateAndRejectsCharacterResetWithoutAnotherTask()
    {
        var consumer = Create(new Transport());
        await Poll(consumer);
        consumer.Dispose();
        var disposal = consumer.Disposal;
        var completion = consumer.Completion;
        Assert.False(consumer.State.WasChecked);
        consumer.Dispose();
        Assert.Same(disposal, consumer.Disposal);
        consumer.ClearCharacter();
        Assert.Same(completion, consumer.Completion);
        await disposal;
    }

    [Theory]
    [InlineData("inventory")]
    [InlineData("armoryChest")]
    [InlineData("chocoboSaddlebag")]
    [InlineData("premiumSaddlebag")]
    [InlineData("retainers")]
    [InlineData("armoire")]
    [InlineData("glamourDresser")]
    [InlineData("minions")]
    [InlineData("mounts")]
    [InlineData("orchestrions")]
    [InlineData("hairstyles")]
    [InlineData("facewear")]
    [InlineData("fashions")]
    [InlineData("reportedOwnership")]
    [InlineData("new-session")]
    public async Task RelevantChangedSourcesRequireReread(string source)
    {
        var transport = new Transport();
        using var consumer = Create(transport);
        await Poll(consumer);
        var notice = JsonNode.Parse(ExportaramusFixture.Read("snapshot-changed-valid.json"))!;
        notice["revision"] = 2;
        notice["changedDataTypes"] = new JsonArray("SourceHealth");
        notice["changedSources"] = new JsonArray(source == "new-session" ? "minions" : source);
        if (source == "new-session") notice["providerSessionId"] = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
        var text = notice.ToJsonString();
        Assert.True(CollectionDataSemanticValidator.ValidateChanged(text).Success);
        transport.Changed!(text);
        Assert.Null(consumer.State.Snapshot);
        await Poll(consumer, 1);
        Assert.NotNull(consumer.State.Snapshot);
    }
    [Fact]
    public async Task ChangedNoticeRemovesSubmissionAuthorityBeforeRereadWithoutRestartingCatalogWork()
    {
        var transport = new Transport();
        using var consumer = Create(transport);
        await Poll(consumer);
        var item = new ShoppingItem(99999, "Item", 99, "Unlock", null, "", "", 0, true, false, null, ShoppingCategory.Minion, 0, 1);
        bool Allowed() => PurchaseReadinessPolicy.CanContinue(item, ExportaramusFixture.ContentId,
            ExportaramusFixture.ContentId, consumer.State, new HashSet<uint>());
        Assert.True(Allowed());
        var notice = JsonNode.Parse(ExportaramusFixture.Read("snapshot-changed-valid.json"))!;
        notice["revision"] = 2;
        notice["changedDataTypes"] = new JsonArray("Unlocks");
        notice["changedSources"] = new JsonArray("minions");
        transport.Changed!(notice.ToJsonString());
        Assert.False(Allowed());
        Assert.Null(consumer.State.Snapshot);
        var snapshot = JsonNode.Parse(transport.Payload)!;
        snapshot["revision"] = 2;
        transport.Payload = snapshot.ToJsonString();
        await Poll(consumer, 1);
        Assert.True(Allowed());
        Assert.Equal(2, consumer.State.Snapshot!.Revision);
    }

    [Theory]
    [InlineData("old-revision")]
    [InlineData("wrong-character")]
    [InlineData("unrelated-source")]
    [InlineData("malformed")]
    [InlineData("empty-sources")]
    public async Task UnrelatedChangedNoticesDoNotRemoveCurrentAuthority(string kind)
    {
        var transport = new Transport();
        using var consumer = Create(transport);
        await Poll(consumer);
        var before = consumer.State;
        var notice = JsonNode.Parse(ExportaramusFixture.Read("snapshot-changed-valid.json"))!;
        notice["revision"] = kind == "old-revision" ? 1 : 2;
        notice["changedDataTypes"] = new JsonArray("Unlocks");
        notice["changedSources"] = new JsonArray(kind == "unrelated-source" ? "achievements" : "minions");
        if (kind == "empty-sources") notice["changedSources"] = new JsonArray();
        if (kind == "wrong-character") notice["contentId"] = "42";
        transport.Changed!(kind == "malformed" ? "{" : notice.ToJsonString());
        Assert.Same(before, consumer.State);
        await Poll(consumer, 1);
        Assert.Same(before, consumer.State);
    }
    [Theory]
    [InlineData("character")]
    [InlineData("withdrawal")]
    [InlineData("dispose")]
    public async Task LateProviderCompletionCannotRepublishAfterInvalidation(string action)
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new Transport();
        transport.ReadSnapshotOverride = () =>
        {
            entered.SetResult();
            Assert.True(release.Wait(TimeSpan.FromSeconds(10)), "Test did not release the controlled provider read.");
            return transport.Payload;
        };
        using var consumer = Create(transport);
        consumer.Tick(ExportaramusFixture.ContentId, ExportaramusFixture.Now);
        var pending = consumer.Completion;
        await entered.Task;
        consumer.Tick(ExportaramusFixture.ContentId, ExportaramusFixture.Now.AddSeconds(1));
        Assert.Same(pending, consumer.Completion);
        if (action == "character") consumer.ClearCharacter();
        else if (action == "withdrawal") transport.Withdrawn!("notice");
        else consumer.Dispose();
        // The publication guard must prevent late writes, independently of the reader's epoch guard.
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var stateField = typeof(ExportaramusConsumer).GetField("state", flags)!;
        var acceptedField = typeof(ExportaramusConsumer).GetField("acceptedView", flags)!;
        var invalidatedState = stateField.GetValue(consumer);
        var invalidatedView = acceptedField.GetValue(consumer);
        release.Set();
        await pending;
        await consumer.Disposal;
        Assert.Null(consumer.State.Snapshot);
        Assert.False(consumer.State.CanInferMissingItemIds);
        Assert.Same(invalidatedState, stateField.GetValue(consumer));
        Assert.Same(invalidatedView, acceptedField.GetValue(consumer));
    }

    [Fact]
    public async Task LoggedOutPollingSettlesWithoutAdmittingTheProvidersCharacter()
    {
        using var consumer = Create(new Transport());
        consumer.Tick(0, ExportaramusFixture.Now);
        await consumer.Completion;
        Assert.Null(consumer.State.Snapshot);
        Assert.Equal(CollectionDataValidationCode.NoPayload, consumer.State.Failure);
        Assert.Contains("character is loaded", consumer.State.StatusDetail, StringComparison.Ordinal);
    }
    [Fact]
    public async Task UnavailableProviderSettlesAndLaterDiscoveryRecovers()
    {
        var available = false;
        var transport = new Transport();
        using var consumer = new ExportaramusConsumer((changed, withdrawn) =>
        {
            if (!available) throw new InvalidOperationException("Provider absent.");
            transport.Changed = changed;
            transport.Withdrawn = withdrawn;
            return transport;
        });
        await Poll(consumer);
        Assert.Equal(CollectionDataValidationCode.ProviderUnavailable, consumer.State.Failure);
        Assert.True(consumer.TryTakeRefreshRequest());
        Assert.False(consumer.TryTakeRefreshRequest());
        available = true;
        await Poll(consumer, 6);
        Assert.NotNull(consumer.State.Snapshot);
        Assert.True(consumer.TryTakeRefreshRequest());
        var state = consumer.State;
        await Poll(consumer, 12);
        Assert.Same(state, consumer.State);
        Assert.False(consumer.TryTakeRefreshRequest());
    }

    [Fact]
    public async Task WithdrawalImmediatelyRemovesPreparedPermissionAndEmptyProviderRemainsUnavailable()
    {
        var transport = new Transport();
        using var consumer = Create(transport);
        await Poll(consumer);
        Assert.True(consumer.State.CompleteFor(ShoppingCategory.Minion));
        transport.Withdrawn!(ExportaramusFixture.Read("snapshot-withdrawn-valid.json"));
        Assert.Same(CollectionDataOwnershipPolicy.NotChecked, consumer.State);
        transport.Payload = "";
        await Poll(consumer, 1);
        Assert.Null(consumer.State.Snapshot);
        Assert.False(consumer.State.Classify(ShoppingCategory.Minion, 99999, 99).Known);
        Assert.True(consumer.Completion.IsCompletedSuccessfully);
        transport.Payload = ExportaramusFixture.Read("compact-valid-full.json");
        transport.Changed!(ExportaramusFixture.Read("snapshot-changed-valid.json"));
        await Poll(consumer, 2);
        Assert.True(consumer.State.CompleteFor(ShoppingCategory.Minion));
    }

    [Fact]
    public async Task UnrelatedWithdrawalMayTemporarilySuppressButCannotPermanentlyStallSameRevision()
    {
        var transport = new Transport();
        using var consumer = Create(transport);
        await Poll(consumer);
        transport.Withdrawn!("invalid notice");
        Assert.Null(consumer.State.Snapshot);
        await Poll(consumer, 6);
        Assert.True(consumer.State.CompleteFor(ShoppingCategory.Minion));
    }

    [Fact]
    public async Task IdentityChangeRejectsOldCharacterAndRecoversWithMatchingPublication()
    {
        var transport = new Transport();
        using var consumer = Create(transport);
        await Poll(consumer);
        consumer.ClearCharacter();
        Assert.Null(consumer.State.Snapshot);
        await consumer.Completion;
        consumer.Tick(42, ExportaramusFixture.Now.AddSeconds(6));
        await consumer.Completion;
        Assert.Equal(CollectionDataValidationCode.WrongCharacter, consumer.State.Failure);
        var node = JsonNode.Parse(transport.Payload)!;
        node["contentId"] = "42";
        transport.Payload = node.ToJsonString();
        transport.Changed!("reread");
        consumer.Tick(42, ExportaramusFixture.Now.AddSeconds(7));
        await consumer.Completion;
        Assert.Equal("42", consumer.State.Snapshot!.ContentId);
    }

    [Fact]
    public async Task RepeatedProviderChangesDoNotCancelAnIndependentCatalogRefresh()
    {
        var transport = new Transport();
        using var consumer = Create(transport);
        using var operation = new SingleFlightOperation(CancellationToken.None);
        var activity = new RefreshActivity();
        var catalog = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var published = false;
        Assert.True(activity.TryBegin());
        Assert.True(operation.TryStart(token => activity.RunAsync(async cancellation =>
        {
            await catalog.Task.WaitAsync(cancellation);
            published = true;
        }, token)));
        for (var index = 0; index < 12; index++)
        {
            transport.Changed!("reread");
            await Poll(consumer, index);
        }
        Assert.True(activity.IsActive);
        Assert.False(published);
        catalog.SetResult();
        await operation.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(published);
        Assert.False(activity.IsActive);
        Assert.Null(operation.LastException);
        Assert.True(consumer.State.CompleteFor(ShoppingCategory.Minion));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CatalogFailureOrCancellationAlwaysSettlesActivity(bool cancel)
    {
        using var lifetime = new CancellationTokenSource();
        using var operation = new SingleFlightOperation(lifetime.Token);
        var activity = new RefreshActivity();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.True(activity.TryBegin());
        Assert.True(operation.TryStart(token => activity.RunAsync(async cancellation =>
        {
            started.SetResult();
            await release.Task.WaitAsync(cancellation);
            throw new HttpRequestException("Catalog unavailable.");
        }, token)));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (cancel) lifetime.Cancel(); else release.SetResult();
        await operation.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(activity.IsActive);
        if (cancel) Assert.Null(operation.LastException);
        else Assert.IsType<HttpRequestException>(operation.LastException);
    }

    [Theory]
    [InlineData("schema", CollectionDataValidationCode.SchemaHashMismatch)]
    [InlineData("version", CollectionDataValidationCode.UnsupportedSchemaVersion)]
    [InlineData("session", CollectionDataValidationCode.WrongProviderSession)]
    [InlineData("json", CollectionDataValidationCode.InvalidJson)]
    [InlineData("empty", CollectionDataValidationCode.NoPayload)]
    public async Task CanonicalRejectionIsVisibleAndSettled(string fault, CollectionDataValidationCode expected)
    {
        var transport = new Transport();
        var capabilities = JsonNode.Parse(transport.Capabilities)!;
        if (fault == "schema") capabilities["collectionDataSchema"]!["sha256"] = new string('0', 64);
        if (fault == "version") capabilities["schemaVersion"] = "1.1";
        if (fault == "session") capabilities["providerSessionId"] = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
        if (fault == "json") transport.Payload = "{";
        if (fault == "empty") transport.Payload = "";
        transport.Capabilities = capabilities.ToJsonString();
        using var consumer = Create(transport);
        await Poll(consumer);
        Assert.Equal(expected, consumer.State.Failure);
        Assert.Null(consumer.State.Snapshot);
        Assert.Contains(expected.ToString(), consumer.State.StatusDetail, StringComparison.Ordinal);
        Assert.True(consumer.Completion.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task ReportsNeverModifyPreparedOwnershipAndDisposeIsObserved()
    {
        var transport = new Transport();
        var consumer = Create(transport);
        await Poll(consumer);
        var state = consumer.State;
        Assert.Equal("Accepted", consumer.ReportConfirmedAcquisition(ExportaramusFixture.ContentId, 99999));
        Assert.Equal((ExportaramusFixture.ContentId.ToString(System.Globalization.CultureInfo.InvariantCulture), 99999U, PluginIdentity.InternalName), transport.LastReport);
        Assert.Same(state, consumer.State);
        Assert.False(consumer.State.Classify(ShoppingCategory.Minion, 99999, 99).SuppressPurchase);
        consumer.Dispose();
        consumer.Dispose();
        await consumer.Disposal;
        consumer.ClearCharacter();
        consumer.Tick(ExportaramusFixture.ContentId, ExportaramusFixture.Now);
        Assert.Null(consumer.State.Snapshot);
        Assert.Null(consumer.DisposalFailure);
        Assert.Equal(1, transport.DisposeCount);
    }

    [Fact]
    public async Task TeardownFailureIsCapturedOnOwnedDisposalTask()
    {
        var transport = new Transport { FailDispose = true };
        var consumer = Create(transport);
        await Poll(consumer);
        consumer.Dispose();
        await consumer.Disposal;
        Assert.IsType<InvalidOperationException>(consumer.DisposalFailure);
        Assert.Null(consumer.State.Snapshot);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedAdvisoryReportsCannotChangePreparedOwnershipOrEscapeTheBoundary(bool throws)
    {
        var transport = new Transport { ReportResult = "Unexpected response", FailReport = throws };
        using var consumer = Create(transport);
        await Poll(consumer);
        var before = consumer.State;

        Assert.Equal("Unavailable", consumer.ReportConfirmedAcquisition(ExportaramusFixture.ContentId, 99999));
        Assert.Equal(1, transport.ReportCount);
        Assert.Same(before, consumer.State);
        Assert.False(consumer.State.Classify(ShoppingCategory.Minion, 99999, 99).SuppressPurchase);
    }

    [Fact]
    public async Task DisposedConsumerRejectsAdvisoryReportsAndAlreadyCapturedNotices()
    {
        var transport = new Transport();
        var consumer = Create(transport);
        await Poll(consumer);
        var changed = transport.Changed!;
        var withdrawn = transport.Withdrawn!;
        consumer.Dispose();
        await consumer.Disposal;
        var completion = consumer.Completion;

        changed(ExportaramusFixture.Read("snapshot-changed-valid.json"));
        withdrawn(ExportaramusFixture.Read("snapshot-withdrawn-valid.json"));
        consumer.Tick(ExportaramusFixture.ContentId, ExportaramusFixture.Now.AddSeconds(6));
        Assert.Equal("ProviderStopping", consumer.ReportConfirmedAcquisition(ExportaramusFixture.ContentId, 99999));
        Assert.Equal(0, transport.ReportCount);
        Assert.Same(completion, consumer.Completion);
        Assert.Null(consumer.State.Snapshot);
        Assert.False(consumer.State.CanInferMissingItemIds);
        Assert.Equal(1, transport.DisposeCount);
    }

    private sealed class ObservedContext : SynchronizationContext
    {
        private int posts;
        internal int Posts => Volatile.Read(ref posts);
        public override void Post(SendOrPostCallback callback, object? state)
        {
            Interlocked.Increment(ref posts);
            ThreadPool.QueueUserWorkItem(_ => callback(state));
        }
    }

    private static ExportaramusConsumer Create(Transport transport) => new((changed, withdrawn) =>
    {
        transport.Changed = changed;
        transport.Withdrawn = withdrawn;
        return transport;
    });

    private static async Task Poll(ExportaramusConsumer consumer, int seconds = 0)
    {
        consumer.Tick(ExportaramusFixture.ContentId, ExportaramusFixture.Now.AddSeconds(seconds));
        await consumer.Completion;
    }

    private sealed class Transport : ICollectionDataTransport
    {
        internal string Payload = ExportaramusFixture.Read("compact-valid-full.json");
        internal string Capabilities = ExportaramusFixture.Read("capabilities-valid.json");
        internal Action<string>? Changed;
        internal Action<string>? Withdrawn;
        internal int DisposeCount;
        internal bool FailDispose;
        internal bool FailReport;
        internal string ReportResult = "Accepted";
        internal int ReportCount;
        internal Func<string>? ReadSnapshotOverride;
        internal (string, uint, string) LastReport;
        public string ReadCapabilities() => Capabilities;
        public string ReadSnapshot() => ReadSnapshotOverride?.Invoke() ?? Payload;
        public string Report(string contentId, uint itemId, string reporter)
        {
            ReportCount++;
            if (FailReport) throw new InvalidOperationException("Advisory report failed.");
            LastReport = (contentId, itemId, reporter);
            return ReportResult;
        }
        public void Dispose()
        {
            DisposeCount++;
            if (FailDispose) throw new InvalidOperationException("Subscription teardown failed.");
        }
    }
}
