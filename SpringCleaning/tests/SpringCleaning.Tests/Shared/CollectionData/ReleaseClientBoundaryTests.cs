using System.Text.Json.Nodes;
using SpringCleaning.Shared.CollectionData;

namespace SpringCleaning.Tests.CollectionData;

public sealed class ReleaseClientBoundaryTests
{
    [Fact]
    public void CharacterSelectionAndClearingEachInvalidatePresentation()
    {
        using var client = new CollectionDataClient(new CollectionDataClientTests.Transport());
        var beforeSelection = client.PresentationEpoch;

        Assert.True(client.Refresh(42, ProviderFixture.Now).Accepted);
        var selected = client.PresentationEpoch;
        client.ClearCharacter();

        Assert.NotEqual(beforeSelection, selected);
        Assert.NotEqual(selected, client.PresentationEpoch);
        Assert.Null(client.Latest);
    }

    [Fact]
    public void ReconnectingTheSameCharacterCannotReuseItsEarlierPresentationEpoch()
    {
        using var client = new CollectionDataClient(new CollectionDataClientTests.Transport());
        var presentation = new PresentationRefreshPolicy();
        Assert.True(client.Refresh(42, ProviderFixture.Now).Accepted);
        var originalEpoch = client.PresentationEpoch;
        presentation.RetainDisplay(ProviderFixture.Now.UtcDateTime, 42, originalEpoch, true, false, false);

        client.ClearCharacter();
        Assert.True(client.Refresh(42, ProviderFixture.Now).Accepted);
        presentation.RetainDisplay(ProviderFixture.Now.UtcDateTime.AddMilliseconds(10),
            42, client.PresentationEpoch, true, false, false);

        Assert.NotEqual(originalEpoch, client.PresentationEpoch);
        Assert.True(presentation.ClearDisplay);
    }

    [Fact]
    public void RejectionAndWithdrawalCannotCancelEachOthersPresentationInvalidation()
    {
        var transport = new CollectionDataClientTests.Transport();
        using var client = new CollectionDataClient(transport);
        Assert.True(client.Refresh(42, ProviderFixture.Now).Accepted);
        var acceptedEpoch = client.PresentationEpoch;

        transport.Caps = "{}";
        Assert.Equal(CollectionDataFailure.UnsupportedProtocol, client.Refresh(42, ProviderFixture.Now).Failure);
        var rejectedEpoch = client.PresentationEpoch;
        transport.Withdrawn();

        Assert.NotEqual(acceptedEpoch, rejectedEpoch);
        Assert.NotEqual(acceptedEpoch, client.PresentationEpoch);
        Assert.NotEqual(rejectedEpoch, client.PresentationEpoch);
        Assert.Null(client.Latest);
    }

    [Fact]
    public void ProviderRestartAndWithdrawalCannotReuseAnOlderPresentationEpoch()
    {
        var transport = new CollectionDataClientTests.Transport();
        using var client = new CollectionDataClient(transport);
        Assert.True(client.Refresh(42, ProviderFixture.Now).Accepted);
        var originalEpoch = client.PresentationEpoch;
        var newSession = Guid.NewGuid().ToString();
        var capabilities = JsonNode.Parse(transport.Caps)!;
        var payload = JsonNode.Parse(transport.Payload)!;
        capabilities["providerSessionId"] = newSession;
        payload["providerSessionId"] = newSession;
        transport.Caps = capabilities.ToJsonString();
        transport.Payload = payload.ToJsonString();

        Assert.True(client.Refresh(42, ProviderFixture.Now).Accepted);
        transport.Withdrawn();

        Assert.NotEqual(originalEpoch, client.PresentationEpoch);
        Assert.Null(client.Latest);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedReadAfterCharacterClearCannotInvalidateTheNewPresentationScope(bool changedNotice)
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var transport = new CollectionDataClientTests.Transport();
        using var client = new CollectionDataClient(transport);
        Assert.True(client.Refresh(42, ProviderFixture.Now).Accepted);
        transport.BeforeRead = () =>
        {
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(3)));
            throw new IOException("Late provider failure");
        };
        var worker = Task.Run(() => client.Refresh(42, ProviderFixture.Now));
        long clearedEpoch;
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
            client.ClearCharacter();
            if (changedNotice)
                transport.Changed();
            clearedEpoch = client.PresentationEpoch;
        }
        finally
        {
            release.Set();
        }

        var result = await worker.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(CollectionDataFailure.ProviderUnavailable, result.Failure);
        Assert.Null(client.Latest);
        Assert.Equal(clearedEpoch, client.PresentationEpoch);
    }

    [Fact]
    public void DelayedCallbacksAfterDisposeCannotChangePresentationState()
    {
        var transport = new CollectionDataClientTests.Transport();
        var client = new CollectionDataClient(transport);
        var changed = transport.CaptureChanged();
        var withdrawn = transport.CaptureWithdrawn();
        Assert.True(client.Refresh(42, ProviderFixture.Now).Accepted);
        client.Dispose();
        var disposedEpoch = client.PresentationEpoch;

        changed();
        withdrawn();

        Assert.Equal(disposedEpoch, client.PresentationEpoch);
        Assert.Null(client.Latest);
        Assert.False(client.TryTakeRefreshRequest(ProviderFixture.Now));
    }

    [Fact]
    public void UnboundSnapshotsAndCopiesDoNotInventProviderIdentityOrHashes()
    {
        var parsed = CollectionDataSnapshotParserTests.Parse(ProviderFixture.Snapshot()).Snapshot!;
        var unbound = new CollectionDataSnapshot(parsed.ProviderInternalName, parsed.ProviderVersion,
            parsed.ContentId, parsed.Revision, parsed.CapturedAtUtc, parsed.Inventory, parsed.ArmoryChest,
            parsed.ChocoboSaddlebag, parsed.PremiumSaddlebag, parsed.Retainers, parsed.Armoire, parsed.GlamourDresser);
        var copy = new InventoryDuplicateItem(0, 1, 100, false, "Item", 0, new(true, true, "Ready"),
            true, true, InventoryDuplicateDisposition.RemovalReview) { ProviderContentId = parsed.ContentId };

        Assert.Equal(string.Empty, unbound.ProviderSessionId);
        Assert.Equal(string.Empty, unbound.ContentHash);
        Assert.Equal(string.Empty, unbound.InventoryHash);
        Assert.Equal(string.Empty, copy.ProviderSessionId);
        Assert.False(CollectionOwnershipPolicy.HasCandidateScope(parsed, copy));
    }

    [Fact]
    public void RejectWithoutFailureExplainsTheInvalidResultConstruction()
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
            CollectionDataParseResult.Reject(CollectionDataFailure.None));

        Assert.Equal("failure", error.ParamName);
        Assert.Equal(CollectionDataFailure.None, error.ActualValue);
        Assert.StartsWith("A rejected result requires a failure reason.", error.Message, StringComparison.Ordinal);
    }
}
