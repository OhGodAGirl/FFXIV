using SpringCleaning.Shared.CollectionData;

namespace SpringCleaning.Tests.CollectionData;

public sealed class CollectionDataClientTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SupersededReadRetainsOnlyChangedNoticePresentation(bool withdrawn)
    {
        var transport = new Transport();
        using var client = new CollectionDataClient(transport);
        var presentation = new PresentationRefreshPolicy();
        Assert.True(client.Refresh(42, ProviderFixture.Now).Accepted);
        var epoch = client.PresentationEpoch;
        presentation.RetainDisplay(ProviderFixture.Now.UtcDateTime, 42, epoch, true, false, false);
        transport.BeforeRead = withdrawn ? transport.Withdrawn : transport.Changed;

        var superseded = client.Refresh(42, ProviderFixture.Now);

        Assert.Equal(CollectionDataFailure.StaleCompletion, superseded.Failure);
        Assert.Null(client.Latest);
        var retained = presentation.RetainDisplay(ProviderFixture.Now.UtcDateTime.AddMilliseconds(10),
            42, client.PresentationEpoch, false, true,
            superseded.Failure is not CollectionDataFailure.None and not CollectionDataFailure.StaleCompletion);
        Assert.Equal(!withdrawn, retained);
        Assert.Equal(withdrawn, presentation.ClearDisplay);
        Assert.Equal(!withdrawn, epoch == client.PresentationEpoch);
    }

    [Fact]
    public void SoftNoticeCanRetainDisplayButNeverRetainsActionAuthority()
    {
        var transport = new Transport();
        using var client = new CollectionDataClient(transport);
        using var authority = new CollectionDataRefreshController();
        var presentation = new PresentationRefreshPolicy();
        authority.Observe(42, true);
        var result = client.Refresh(42, ProviderFixture.Now);
        authority.Complete(42, result, client.Latest);
        presentation.RetainDisplay(ProviderFixture.Now.UtcDateTime, 42, client.PresentationEpoch,
            authority.LatestSnapshot is not null, false, false);
        var epoch = client.PresentationEpoch;

        transport.Changed();
        authority.Observe(42, true, invalidated: client.Latest is null);

        Assert.Equal(epoch, client.PresentationEpoch);
        Assert.Null(client.Latest);
        Assert.Null(authority.LatestSnapshot);
        Assert.False(CollectionOwnershipPolicy.AreSourcesFresh(authority.LatestSnapshot,
            CollectionSourceName.Inventory, CollectionSourceName.GlamourDresser));
        Assert.True(presentation.RetainDisplay(ProviderFixture.Now.UtcDateTime.AddMilliseconds(10),
            42, client.PresentationEpoch, false, true, false));

        transport.Withdrawn();
        Assert.NotEqual(epoch, client.PresentationEpoch);
        Assert.False(presentation.RetainDisplay(ProviderFixture.Now.UtcDateTime.AddMilliseconds(20),
            42, client.PresentationEpoch, false, true, false));
        Assert.True(presentation.ClearDisplay);
    }

    [Fact]
    public void MissedWithdrawalSessionChangeStillRevokesOldPresentation()
    {
        var transport = new Transport();
        using var client = new CollectionDataClient(transport);
        Assert.True(client.Refresh(42, ProviderFixture.Now).Accepted);
        var epoch = client.PresentationEpoch;
        var session = Guid.NewGuid().ToString();
        var caps = System.Text.Json.Nodes.JsonNode.Parse(transport.Caps)!;
        var payload = System.Text.Json.Nodes.JsonNode.Parse(transport.Payload)!;
        caps["providerSessionId"] = session;
        payload["providerSessionId"] = session;
        transport.Caps = caps.ToJsonString();
        transport.Payload = payload.ToJsonString();
        Assert.True(client.Refresh(42, ProviderFixture.Now).Accepted);
        Assert.NotEqual(epoch, client.PresentationEpoch);
    }

    [Fact]
    public void DiscoveryAndNoticeRequestsAreCoalescedWithoutPerFrameReads()
    {
        var transport = new Transport(); using var client = new CollectionDataClient(transport);
        var now = ProviderFixture.Now;
        Assert.True(client.TryTakeRefreshRequest(now));
        Assert.False(client.TryTakeRefreshRequest(now.AddSeconds(4.999)));
        Assert.True(client.TryTakeRefreshRequest(now.AddSeconds(5)));
        transport.Changed(); transport.Changed();
        Assert.True(client.TryTakeRefreshRequest(now.AddSeconds(5)));
        Assert.False(client.TryTakeRefreshRequest(now.AddSeconds(5)));
        Assert.Equal(0, transport.Reads);
    }

    [Fact]
    public void ValidReadBindsCapabilitiesBeforeAndAfterBothIndependentSnapshots()
    {
        var transport = new Transport(); using var client = new CollectionDataClient(transport);
        Assert.True(client.Refresh(42, ProviderFixture.Now).Accepted);
        Assert.Equal(2, transport.CapabilityReads);
        var reread = client.Refresh(42, ProviderFixture.Now);
        Assert.Same(client.Latest, reread.Snapshot);
        Assert.Equal(CollectionSourceStatus.Fresh, client.Latest!.Inventory.Status);
        transport.Changed(); Assert.Null(client.Latest);
        Assert.True(client.Refresh(42, ProviderFixture.Now).Accepted);
        transport.Withdrawn(); Assert.Null(client.Latest);
    }

    [Fact]
    public void InvalidInventoryOnlyWithdrawsCarriedLocations()
    {
        var transport = new Transport { Inventory = "{}" }; using var client = new CollectionDataClient(transport);
        Assert.True(client.Refresh(42, ProviderFixture.Now).Accepted);
        Assert.Equal(CollectionSourceStatus.Unavailable, client.Latest!.Inventory.Status);
        Assert.Equal(CollectionSourceStatus.Fresh, client.Latest.GlamourDresser.Status);
        transport.Inventory = ProviderFixture.Inventory(); transport.InventoryCaps = "{}";
        Assert.True(client.Refresh(42, ProviderFixture.Now).Accepted);
        Assert.Equal(CollectionSourceStatus.Unavailable, client.Latest!.ArmoryChest.Status);
    }

    [Fact]
    public void FailedProviderAndBadHandshakeInvalidatePreviousFacts()
    {
        var transport = new Transport(); using var client = new CollectionDataClient(transport);
        Assert.True(client.Refresh(42, ProviderFixture.Now).Accepted);
        transport.Caps = "{}";
        Assert.Equal(CollectionDataFailure.UnsupportedProtocol, client.Refresh(42, ProviderFixture.Now).Failure);
        Assert.Null(client.Latest);
        transport.Caps = ProviderFixture.Capabilities(); transport.Payload = "{";
        Assert.Equal(CollectionDataFailure.MalformedJson, client.Refresh(42, ProviderFixture.Now).Failure);
        transport.Payload = ProviderFixture.Snapshot().ToJsonString(); transport.BeforeRead = () => throw new IOException("provider gone");
        Assert.Equal(CollectionDataFailure.ProviderUnavailable, client.Refresh(42, ProviderFixture.Now).Failure);
    }

    [Theory]
    [InlineData("providerSessionId")]
    [InlineData("gameDataVersion")]
    [InlineData("invalid")]
    public void CapabilityChangeDuringReadRejectsTheWholePublication(string field)
    {
        var transport = new Transport(); using var client = new CollectionDataClient(transport);
        transport.BeforeRead = () =>
        {
            var caps = System.Text.Json.Nodes.JsonNode.Parse(transport.Caps)!;
            if (field == "invalid") transport.Caps = "{}";
            else { caps[field] = field == "providerSessionId" ? Guid.NewGuid().ToString() : "2026.09.01.0000.0000"; transport.Caps = caps.ToJsonString(); }
        };
        Assert.Equal(CollectionDataFailure.StaleCompletion, client.Refresh(42, ProviderFixture.Now).Failure);
        Assert.Null(client.Latest);
    }

    [Fact]
    public void SnapshotAndCapabilitySessionMustAgree()
    {
        var root = ProviderFixture.Snapshot(); root["providerSessionId"] = Guid.NewGuid().ToString();
        using var client = new CollectionDataClient(new Transport { Payload = root.ToJsonString() });
        Assert.Equal(CollectionDataFailure.StaleCompletion, client.Refresh(42, ProviderFixture.Now).Failure);
    }

    [Theory]
    [InlineData("changed")]
    [InlineData("withdrawn")]
    [InlineData("character")]
    [InlineData("disposed")]
    [InlineData("failed")]
    public async Task LateWorkerCannotPublishAcrossInvalidation(string action)
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var transport = new Transport(); using var client = new CollectionDataClient(transport);
        transport.BeforeRead = () => { entered.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(3))); if (action == "failed") throw new IOException(); };
        var worker = Task.Run(() => client.Refresh(42, ProviderFixture.Now));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
            Assert.Equal(CollectionDataFailure.Busy, client.Refresh(42, ProviderFixture.Now).Failure);
            if (action is "changed" or "failed") transport.Changed();
            else if (action == "withdrawn") transport.Withdrawn();
            else if (action == "character") client.ClearCharacter();
            else client.Dispose();
        }
        finally { release.Set(); }
        var result = await worker.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.False(result.Accepted);
        Assert.Null(client.Latest);
    }

    [Fact]
    public void IdentityAndDisposalAreTerminalForOldReadsAndCallbacks()
    {
        var transport = new Transport(); var client = new CollectionDataClient(transport);
        var delayedChanged = transport.CaptureChanged();
        var delayedWithdrawn = transport.CaptureWithdrawn();
        Assert.True(client.Refresh(42, ProviderFixture.Now).Accepted);
        Assert.Equal(CollectionDataFailure.NoCharacter, client.Refresh(0, ProviderFixture.Now).Failure);
        Assert.Null(client.Latest);
        Assert.Equal(CollectionDataFailure.WrongCharacter, client.Refresh(77, ProviderFixture.Now).Failure);
        client.Dispose(); client.Dispose(); transport.Changed(); transport.Withdrawn();
        delayedChanged(); delayedWithdrawn();
        Assert.Equal(1, transport.Disposals);
        Assert.False(client.TryTakeRefreshRequest(ProviderFixture.Now));
        Assert.Equal(CollectionDataFailure.Disposed, client.Refresh(42, ProviderFixture.Now).Failure);
    }

    internal sealed class Transport : ICollectionDataTransport
    {
        internal string Caps = ProviderFixture.Capabilities();
        internal string Payload = ProviderFixture.Snapshot().ToJsonString();
        internal string Inventory = ProviderFixture.Inventory();
        internal string InventoryCaps = ProviderFixture.InventoryCapabilities;
        internal Action? BeforeRead;
        internal int Reads, CapabilityReads, Disposals;
        public event Action? SnapshotChanged;
        public event Action? SnapshotWithdrawn;
        internal void Changed() => SnapshotChanged?.Invoke();
        internal void Withdrawn() => SnapshotWithdrawn?.Invoke();
        internal Action CaptureChanged() => SnapshotChanged!;
        internal Action CaptureWithdrawn() => SnapshotWithdrawn!;
        public string ReadCapabilities() { CapabilityReads++; return Caps; }
        public string ReadSnapshot() { Reads++; BeforeRead?.Invoke(); return Payload; }
        public string ReadInventoryCapabilities() => InventoryCaps;
        public string ReadInventorySnapshot() => Inventory;
        public void Dispose() => Disposals++;
    }
}
