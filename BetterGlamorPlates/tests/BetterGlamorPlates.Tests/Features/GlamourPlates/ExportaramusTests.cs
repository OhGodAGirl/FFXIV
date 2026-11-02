using BetterGlamorPlates.Adapters.CollectionData;
using BetterGlamorPlates.Features.GlamourPlates;
using BetterGlamorPlates.Shared.CollectionData;
using Kit = Exportaramus.ConsumerKit.V1_2;

namespace BetterGlamorPlates.Tests.Features.GlamourPlates;

/// <summary>Checks actual pinned payloads, conservative source mapping and lifecycle invalidation.</summary>
public sealed class ExportaramusTests
{
    private const ulong Character = 12345678901234567;

    /// <summary>Exercises the kit's schema and semantic validation before projecting actual presence.</summary>
    [Fact]
    public void FullFixtureProjectsDresserAndArmoire()
    {
        var snapshot = Snapshot();
        var result = ExportaramusProjection.Project(Ready(snapshot), Character);
        Assert.True(result.Accepted);
        Assert.Equal(snapshot.CurrentItemPresence["glamourDresser"].Items.Select(item => item.ItemId),
            result.Snapshot!.GlamourDresser.ItemIds);
        Assert.Equal(CollectionSourceStatus.Fresh, result.Snapshot.Armoire.Status);
    }

    /// <summary>New unlock categories cannot add nonphysical items to the wardrobe.</summary>
    [Theory]
    [InlineData("compact-valid-beasts-populated.json")]
    [InlineData("compact-valid-beasts-empty.json")]
    [InlineData("compact-valid-beasts-degraded.json")]
    [InlineData("compact-valid-beasts-stale-observed.json")]
    [InlineData("compact-valid-beasts-stale-restored.json")]
    public void Version12UnlockStatesPreserveOnlyQualifiedWardrobePresence(string fixture)
    {
        var validation = Kit.CollectionDataSemanticValidator.ValidateSnapshot(Read(fixture),
            Character.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.True(validation.Success, validation.Detail);

        var result = ExportaramusProjection.Project(Ready(validation.Value!), Character);

        Assert.True(result.Accepted);
        Assert.Equal(new uint[] { 100, 2642 }, result.Snapshot!.GlamourDresser.ItemIds);
        Assert.Equal(Snapshot().CurrentItemPresence["armoire"].Items.Select(item => item.ItemId),
            result.Snapshot.Armoire.ItemIds);
    }

    /// <summary>Known empty and restored aggregate presence retain distinct meanings in version 1.2.</summary>
    [Theory]
    [InlineData("compact-valid-empty-dresser.json", false, 0)]
    [InlineData("compact-valid-legacy-restored-composition-unknown.json", true, 2)]
    public void Version12DresserCompositionDoesNotInventOwnership(string fixture, bool stale, int count)
    {
        var validation = Kit.CollectionDataSemanticValidator.ValidateSnapshot(Read(fixture),
            Character.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.True(validation.Success, validation.Detail);

        var result = ExportaramusProjection.Project(Ready(validation.Value!), Character);

        Assert.True(result.Accepted);
        Assert.Equal(stale ? CollectionSourceStatus.Stale : CollectionSourceStatus.Fresh, result.Snapshot!.GlamourDresser.Status);
        Assert.Equal(count, result.Snapshot.GlamourDresser.ItemIds.Length);
    }

    /// <summary>A provider downgrade invalidates current ownership rather than retaining a prior accepted snapshot.</summary>
    [Fact]
    public async Task Version12ConsumerRejectsDowngradedProviderSchema()
    {
        var transport = new FakeTransport();
        using var consumer = new ExportaramusConsumer((_, _) => transport, _ => { });
        var now = DateTimeOffset.UtcNow;
        Assert.True((await consumer.RefreshAsync(Character, now)).Accepted);
        transport.Payload = transport.Payload.Replace("\"schemaVersion\": \"1.2\"", "\"schemaVersion\": \"1.1\"", StringComparison.Ordinal);
        Assert.Contains("\"schemaVersion\": \"1.1\"", transport.Payload, StringComparison.Ordinal);

        var result = await consumer.RefreshAsync(Character, now.AddSeconds(6));

        Assert.False(result.Accepted);
        Assert.Null(consumer.Latest);
        consumer.Dispose();
        await consumer.Disposal;
    }

    /// <summary>Missing version 1.2 gates at startup remain unavailable until later discovery succeeds.</summary>
    [Fact]
    public async Task MissingVersion12ProviderAtStartupRecoversOnLaterDiscovery()
    {
        var available = false;
        var bindings = 0;
        var transport = new FakeTransport();
        using var consumer = new ExportaramusConsumer((_, _) =>
        {
            bindings++;
            return available ? transport : throw new InvalidOperationException("Version 1.2 gates are unavailable.");
        }, _ => { });
        var now = DateTimeOffset.UtcNow;

        var missing = await consumer.RefreshAsync(Character, now);

        Assert.False(missing.Accepted);
        Assert.Equal(CollectionDataFailure.ProviderUnavailable, missing.Failure);
        Assert.Null(consumer.Latest);
        Assert.Equal(2, bindings);

        available = true;
        var beforeDiscovery = await consumer.RefreshAsync(Character, now.AddSeconds(4));
        Assert.False(beforeDiscovery.Accepted);
        Assert.Null(consumer.Latest);
        Assert.Equal(2, bindings);

        var recovered = await consumer.RefreshAsync(Character, now.AddSeconds(5));

        Assert.True(recovered.Accepted);
        Assert.Equal(new uint[] { 100, 2642 }, consumer.Latest!.GlamourDresser.ItemIds);
        Assert.Equal(3, bindings);
        consumer.Dispose();
        await consumer.Disposal;
        Assert.True(transport.Disposed);
    }

    /// <summary>Incomplete sources cannot supply ownership or absence evidence.</summary>
    [Theory]
    [InlineData("Partial", "Fresh")]
    [InlineData("Complete", "Unavailable")]
    public void UnusableSourceDoesNotBecomeEmptyEvidence(string completeness, string status)
    {
        var snapshot = Snapshot();
        var health = snapshot.SourceHealth["glamourDresser"] with { Completeness = completeness, SourceStatus = status };
        snapshot = snapshot with { SourceHealth = snapshot.SourceHealth.SetItem("glamourDresser", health) };
        var result = ExportaramusProjection.Project(Ready(snapshot), Character);
        Assert.Equal(CollectionSourceStatus.Unavailable, result.Snapshot!.GlamourDresser.Status);
    }

    /// <summary>Retained source items remain labelled stale.</summary>
    [Fact]
    public void StalePresenceIsNotPromoted()
    {
        var snapshot = Snapshot();
        snapshot = snapshot with
        {
            SourceHealth = snapshot.SourceHealth.SetItem("glamourDresser",
            snapshot.SourceHealth["glamourDresser"] with { SourceStatus = "Stale" })
        };
        Assert.Equal(CollectionSourceStatus.Stale,
            ExportaramusProjection.Project(Ready(snapshot), Character).Snapshot!.GlamourDresser.Status);
        Assert.Null(ExportaramusProjection.Project(Ready(snapshot), Character + 1).Snapshot);
    }

    /// <summary>Withdrawals clear usable data synchronously and failures cannot resurrect it.</summary>
    [Fact]
    public async Task WithdrawalAndProviderLossClearOwnership()
    {
        var transport = new FakeTransport();
        Action<string>? withdrawn = null;
        using var consumer = new ExportaramusConsumer((_, onWithdrawn) =>
        {
            withdrawn = onWithdrawn;
            return transport;
        }, exception => throw new InvalidOperationException("Unexpected cleanup failure.", exception));
        var now = DateTimeOffset.UtcNow;
        Assert.True((await consumer.RefreshAsync(Character, now)).Accepted);
        Assert.NotNull(consumer.Latest);
        withdrawn!("{}");
        Assert.Null(consumer.Latest);
        transport.Fail = true;
        Assert.False((await consumer.RefreshAsync(Character, now.AddSeconds(6))).Accepted);
        Assert.Null(consumer.Latest);
        consumer.Dispose();
        await consumer.Disposal;
        Assert.True(transport.Disposed);
    }

    /// <summary>Character changes immediately withdraw the old character projection.</summary>
    [Fact]
    public async Task CharacterSwitchRejectsOldPayload()
    {
        using var consumer = new ExportaramusConsumer((_, _) => new FakeTransport(), _ => { });
        Assert.True((await consumer.RefreshAsync(Character, DateTimeOffset.UtcNow)).Accepted);
        consumer.ClearCharacter();
        Assert.Null(consumer.Latest);
        Assert.False((await consumer.RefreshAsync(Character + 1, DateTimeOffset.UtcNow.AddSeconds(6))).Accepted);
        Assert.Null(consumer.Latest);
        consumer.Dispose();
        await consumer.Disposal;
    }

    /// <summary>All pinned malformed snapshot fixtures are rejected by the actual consumer validator.</summary>
    [Fact]
    public void InvalidProviderFixturesAreRejected()
    {
        var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures", "invalid"), "*.json");
        Assert.NotEmpty(files);
        foreach (var file in files)
            Assert.False(Kit.CollectionDataSemanticValidator.ValidateSnapshot(File.ReadAllText(file),
                Character.ToString(System.Globalization.CultureInfo.InvariantCulture)).Success, file);
    }

    /// <summary>A changed provider session accepts a new revision scope, while malformed payloads clear current data.</summary>
    [Fact]
    public async Task RestartAndMalformedPayloadDoNotRetainOldState()
    {
        var transport = new FakeTransport();
        using var consumer = new ExportaramusConsumer((_, _) => transport, _ => { });
        var now = DateTimeOffset.UtcNow;
        Assert.True((await consumer.RefreshAsync(Character, now)).Accepted);
        transport.Capabilities = transport.Capabilities.Replace("7be5461e-8975-4c7a-9f13-4bb33af9c82d", "fd1c8991-f559-4a98-bafb-59aa21ec043c", StringComparison.Ordinal);
        transport.Payload = transport.Payload.Replace("7be5461e-8975-4c7a-9f13-4bb33af9c82d", "fd1c8991-f559-4a98-bafb-59aa21ec043c", StringComparison.Ordinal);
        Assert.True((await consumer.RefreshAsync(Character, now.AddSeconds(6))).Accepted);
        transport.Payload = "{";
        Assert.False((await consumer.RefreshAsync(Character, now.AddSeconds(12))).Accepted);
        Assert.Null(consumer.Latest);
        consumer.Dispose();
        await consumer.Disposal;
    }
    private static Kit.CollectionDataSnapshot Snapshot()
    {
        var result = Kit.CollectionDataSemanticValidator.ValidateSnapshot(Read("compact-valid-full.json"), Character.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.True(result.Success, result.Detail);
        return result.Value!;
    }

    private static Kit.CollectionDataClientView Ready(Kit.CollectionDataSnapshot snapshot)
        => new(Kit.CollectionDataAvailability.Ready, snapshot, snapshot.ProviderSessionId, snapshot.ContentId,
            snapshot.Revision, Kit.CollectionDataValidationCode.None);

    private static string Read(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "valid", name));

    private sealed class FakeTransport : Kit.ICollectionDataTransport
    {
        internal string Capabilities { get; set; } = Read("capabilities-valid.json");
        internal string Payload { get; set; } = Read("compact-valid-full.json");
        internal bool Fail { get; set; }
        internal bool Disposed { get; private set; }
        public string ReadCapabilities() => Fail ? throw new InvalidOperationException("Provider unloaded.") : Capabilities;
        public string ReadSnapshot() => Payload;
        public string Report(string contentId, uint itemId, string reporter) => throw new InvalidOperationException("Read-only consumer.");
        public void Dispose() => Disposed = true;
    }
}

