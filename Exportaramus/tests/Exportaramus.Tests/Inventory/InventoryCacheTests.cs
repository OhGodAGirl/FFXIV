using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Exportaramus.Features.Inventory;

namespace Exportaramus.Tests.Inventory;

public sealed class InventoryCacheTests : IDisposable
{
    private readonly string directory = Path.Combine(AppContext.BaseDirectory, "isolated-test-recovery/inventory-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task LoadingAwaitsAsynchronousStreamDisposal()
    {
        var real = new InventoryFileCache(directory);
        await real.SaveAsync(InventoryServiceTests.Saved(1), default);
        var stream = new DeferredDisposalStream(await File.ReadAllBytesAsync(real.CharacterPath(1)));
        var cache = new InventoryFileCache(directory, _ => stream);
        var load = cache.LoadAsync(1, default);
        try
        {
            var winner = await Task.WhenAny(stream.DisposalStarted.Task, load).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Same(stream.DisposalStarted.Task, winner);
            Assert.False(load.IsCompleted);
        }
        finally { stream.AllowDisposal.TrySetResult(); }
        Assert.NotNull(await load.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    private sealed class DeferredDisposalStream(byte[] bytes) : MemoryStream(bytes)
    {
        public TaskCompletionSource DisposalStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowDisposal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask DisposeAsync()
        {
            DisposalStarted.SetResult();
            await AllowDisposal.Task;
            base.Dispose();
        }
    }

    [Fact]
    public async Task RetainedObservationRemainsRetainedAcrossCacheRoundTrip()
    {
        var cache = new InventoryFileCache(directory);
        var saved = InventoryServiceTests.Saved(1);
        await cache.SaveAsync(saved with { Locations = saved.Locations.Select(location => location with { State = InventoryObservationState.Retained }).ToImmutableArray() }, default);
        Assert.All((await cache.LoadAsync(1, default))!.Locations, location => Assert.Equal(InventoryObservationState.Retained, location.State));
    }

    [Fact]
    public async Task UnobservedLocationsRoundTripWithoutAcquiringObservationEvidence()
    {
        var cache = new InventoryFileCache(directory);
        await cache.SaveAsync(InventoryPolicy.Empty(1, 1), default);
        var restored = await cache.LoadAsync(1, default);
        Assert.All(restored!.Locations, location =>
        {
            Assert.False(location.HasObservation);
            Assert.Null(location.AttemptedAtUtc);
            Assert.Empty(location.Items);
        });
    }

    [Fact]
    public async Task OversizedSaveAndDefaultCollectionsCannotReplaceValidCache()
    {
        var cache = new InventoryFileCache(directory);
        var saved = InventoryServiceTests.Saved(1);
        await cache.SaveAsync(saved, default);
        var prior = await File.ReadAllBytesAsync(cache.CharacterPath(1));
        var items = Enumerable.Range(1, 50000).Select(i => new InventoryItemQuantity((uint)i, 1, 0)).ToImmutableArray();
        Assert.Equal("InventoryCacheSizeLimit", (await Assert.ThrowsAsync<InvalidDataException>(() => cache.SaveAsync(saved with
        {
            Locations = [saved.Locations[0] with { Items = items }, saved.Locations[1] with { Items = items }]
        }, default))).Message);
        Assert.Equal("InventoryCacheInvalid", (await Assert.ThrowsAsync<InvalidDataException>(() => cache.SaveAsync(saved with { Locations = default }, default))).Message);
        Assert.Equal("InventoryCacheInvalid", (await Assert.ThrowsAsync<InvalidDataException>(() => cache.SaveAsync(saved with { Locations = [saved.Locations[0] with { Items = default }] }, default))).Message);
        Assert.Equal("InventoryCacheInvalid", (await Assert.ThrowsAsync<InvalidDataException>(() => cache.SaveAsync(saved with
        {
            Locations = Enumerable.Repeat(saved.Locations[0], 18).ToImmutableArray()
        }, default))).Message);
        Assert.Equal(prior, await File.ReadAllBytesAsync(cache.CharacterPath(1)));
    }

    [Fact]
    public async Task GrowthDuringReadIsRejectedAndFailedCommitRemovesTemporaryFile()
    {
        var growing = new InventoryFileCache(directory, _ => new GrowingStream());
        Assert.Equal("InventoryCacheChangedDuringRead", (await Assert.ThrowsAsync<InvalidDataException>(() => growing.LoadAsync(1, default))).Message);
        var cache = new InventoryFileCache(directory, commitFile: (_, _) => throw new IOException("Commit failed"));
        await Assert.ThrowsAsync<IOException>(() => cache.SaveAsync(InventoryServiceTests.Saved(1), default));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(cache.CharacterPath(1))!, "*.tmp"));
        Assert.False(File.Exists(cache.CharacterPath(1)));
    }

    private sealed class GrowingStream : MemoryStream
    {
        public GrowingStream() : base([1, 2]) { }
        public override long Length => 1;
    }

    [Theory]
    [InlineData("nullDocument")]
    [InlineData("nullSnapshot")]
    [InlineData("revision")]
    [InlineData("nullLocation")]
    [InlineData("invalidKind")]
    [InlineData("invalidState")]
    [InlineData("emptyName")]
    [InlineData("defaultObservation")]
    [InlineData("missingAttempt")]
    [InlineData("missingObservation")]
    [InlineData("retainedMissingObservation")]
    [InlineData("emptyMissingObservation")]
    [InlineData("emptyRetainedMissingObservation")]
    [InlineData("itemsWithoutObservation")]
    [InlineData("attemptBeforeObservation")]
    [InlineData("unsorted")]
    public async Task CacheRejectsMalformedIdentityTimeAndOrdering(string defect)
    {
        var cache = new InventoryFileCache(directory);
        await cache.SaveAsync(InventoryServiceTests.Saved(1), default);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(cache.CharacterPath(1)))!;
        var location = json["snapshot"]!["locations"]![0]!;
        switch (defect)
        {
            case "nullDocument": json = null; break;
            case "nullSnapshot": json["snapshot"] = null; break;
            case "revision": json["snapshot"]!["revision"] = -1; break;
            case "nullLocation": json["snapshot"]!["locations"]![0] = null; break;
            case "invalidKind": location["key"]!["kind"] = 100; break;
            case "invalidState": location["state"] = 100; break;
            case "emptyName": location["name"] = ""; break;
            case "defaultObservation": location["observedAtUtc"] = "0001-01-01T00:00:00+00:00"; break;
            case "missingAttempt": location["attemptedAtUtc"] = null; break;
            case "missingObservation": location["observedAtUtc"] = null; break;
            case "retainedMissingObservation": location["observedAtUtc"] = null; location["state"] = "Retained"; break;
            case "emptyMissingObservation": location["observedAtUtc"] = null; location["items"] = new JsonArray(); break;
            case "emptyRetainedMissingObservation": location["observedAtUtc"] = null; location["state"] = "Retained"; location["items"] = new JsonArray(); break;
            case "itemsWithoutObservation": location["observedAtUtc"] = null; location["state"] = "ReadFailed"; break;
            case "attemptBeforeObservation": location["attemptedAtUtc"] = "2000-01-01T00:00:00Z"; break;
            case "unsorted": location["items"]!.AsArray().Add(new JsonObject { ["itemId"] = 1, ["normalQuantity"] = 1, ["highQualityQuantity"] = 0 }); break;
        }
        await File.WriteAllTextAsync(cache.CharacterPath(1), json?.ToJsonString() ?? "null");
        Assert.Equal("InventoryCacheInvalid", (await Assert.ThrowsAsync<InvalidDataException>(() => cache.LoadAsync(1, default))).Message);
    }

    [Fact]
    public async Task MissingFileInExistingDirectoryAndDamagedPriorCacheAreHandled()
    {
        var cache = new InventoryFileCache(directory);
        Directory.CreateDirectory(Path.GetDirectoryName(cache.CharacterPath(1))!);
        Assert.Null(await cache.LoadAsync(1, default));
        await File.WriteAllTextAsync(cache.CharacterPath(1), "{");
        await cache.SaveAsync(InventoryServiceTests.Saved(1), default);
        Assert.NotNull(await cache.LoadAsync(1, default));
        Assert.Equal("InventoryCacheInvalid", (await Assert.ThrowsAsync<InvalidDataException>(() => cache.SaveAsync(InventoryServiceTests.Saved(1) with { ContentId = 0 }, default))).Message);
        var sample = InventoryServiceTests.Saved(1).Locations[0];
        Assert.Equal("InventoryCacheInvalid", (await Assert.ThrowsAsync<InvalidDataException>(() => cache.SaveAsync(InventoryServiceTests.Saved(1) with
        {
            Locations = Enumerable.Range(1, 11).Select(i => sample with { Key = new(InventoryLocationKind.Retainer, (ulong)i) }).ToImmutableArray()
        }, default))).Message);
    }

    [Fact]
    public async Task RoundTripIsCharacterScopedAndAtomicReplacementPreservesOtherOutputs()
    {
        var cache = new InventoryFileCache(directory);
        Assert.Null(await cache.LoadAsync(1, default));
        var value = InventoryServiceTests.Saved(1);
        await cache.SaveAsync(value, default);
        var restored = await cache.LoadAsync(1, default);
        Assert.Equal(value.ContentId, restored!.ContentId);
        Assert.Equal(value.Locations.Length, restored.Locations.Length);
        for (var index = 0; index < value.Locations.Length; index++)
        {
            Assert.Equal(value.Locations[index].Key, restored.Locations[index].Key);
            Assert.Equal(value.Locations[index].Name, restored.Locations[index].Name);
            Assert.Equal(value.Locations[index].ObservedAtUtc, restored.Locations[index].ObservedAtUtc);
            Assert.True(value.Locations[index].Items.SequenceEqual(restored.Locations[index].Items));
        }
        Assert.Null(await cache.LoadAsync(2, default));
        var unrelated = Path.Combine(directory, "current-snapshot.json");
        await File.WriteAllTextAsync(unrelated, "untouched");
        var cleared = value with { Locations = [value.Locations[0] with { Items = [], ObservedAtUtc = value.Locations[0].ObservedAtUtc!.Value.AddSeconds(1), AttemptedAtUtc = value.Locations[0].AttemptedAtUtc!.Value.AddSeconds(1) }] };
        await cache.SaveAsync(cleared, default);
        Assert.Empty((await cache.LoadAsync(1, default))!.Locations.Single().Items);
        Assert.Equal("untouched", await File.ReadAllTextAsync(unrelated));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task EarlyFreshCaptureKeepsUnseenSameCharacterRetainerCache()
    {
        var cache = new InventoryFileCache(directory);
        var saved = InventoryServiceTests.Saved(1);
        await cache.SaveAsync(saved, default);
        var early = InventoryPolicy.Empty(1, 3) with { Locations = [saved.Locations[0] with { Items = [] }] };
        await cache.SaveAsync(early, default);
        var result = await cache.LoadAsync(1, default);
        Assert.Empty(result!.Locations[0].Items);
        Assert.True(saved.Locations[1].Items.SequenceEqual(result.Locations[1].Items));
        Assert.Equal(InventoryObservationState.Retained, result.Locations[1].State);
    }

    [Fact]
    public async Task CancellationAndInvalidCandidatesPreservePriorBytes()
    {
        var cache = new InventoryFileCache(directory);
        var saved = InventoryServiceTests.Saved(1);
        await cache.SaveAsync(saved, default);
        var prior = await File.ReadAllBytesAsync(cache.CharacterPath(1));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.SaveAsync(saved, cancellation.Token));
        Assert.Equal("InventoryCacheInvalid", (await Assert.ThrowsAsync<InvalidDataException>(() => cache.SaveAsync(saved with { Locations = [saved.Locations[0] with { Items = [new(100, uint.MaxValue, 1)] }] }, default))).Message);
        Assert.Equal(prior, await File.ReadAllBytesAsync(cache.CharacterPath(1)));
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("schema")]
    [InlineData("quantity")]
    [InlineData("unknownWithItems")]
    [InlineData("duplicate")]
    [InlineData("missingQuantity")]
    public async Task InvalidSavedFactsAreRejected(string defect)
    {
        var cache = new InventoryFileCache(directory);
        await cache.SaveAsync(InventoryServiceTests.Saved(1), default);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(cache.CharacterPath(1)))!;
        var location = json["snapshot"]!["locations"]![0]!;
        switch (defect)
        {
            case "identity": json["snapshot"]!["contentId"] = 2; break;
            case "schema": json["schemaVersion"] = "2.0"; break;
            case "quantity": location["items"]![0]!["normalQuantity"] = 0; break;
            case "unknownWithItems": location["state"] = "Unknown"; break;
            case "duplicate": json["snapshot"]!["locations"]!.AsArray().Add(location.DeepClone()); break;
            case "missingQuantity": location["items"]![0]!.AsObject().Remove("normalQuantity"); break;
        }
        await File.WriteAllTextAsync(cache.CharacterPath(1), json.ToJsonString());
        Assert.Equal("InventoryCacheInvalid", (await Assert.ThrowsAsync<InvalidDataException>(() => cache.LoadAsync(1, default))).Message);
    }

    [Fact]
    public async Task OversizedAndMalformedCacheAreRejectedWithoutReadingAnotherCharacter()
    {
        var cache = new InventoryFileCache(directory);
        Directory.CreateDirectory(Path.GetDirectoryName(cache.CharacterPath(1))!);
        await File.WriteAllTextAsync(cache.CharacterPath(1), "{");
        Assert.Equal("InventoryCacheInvalid", (await Assert.ThrowsAsync<InvalidDataException>(() => cache.LoadAsync(1, default))).Message);
        await using (var stream = File.Create(cache.CharacterPath(1))) stream.SetLength(InventoryFileCache.MaximumBytes + 1L);
        Assert.Equal("InventoryCacheSizeLimit", (await Assert.ThrowsAsync<InvalidDataException>(() => cache.LoadAsync(1, default))).Message);
        Assert.Null(await cache.LoadAsync(2, default));
        Assert.Throws<ArgumentOutOfRangeException>(() => cache.CharacterPath(0));
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
