using System.Collections.Immutable;
using Exportaramus.Features.CollectionData.Capture;
using Exportaramus.Features.CollectionData.Catalog;
using Exportaramus.Features.CollectionData.State;
using Exportaramus.Features.Diagnostics;

namespace Exportaramus.Tests.Capture;

public sealed class CaptureProgressBoundaryTests
{
    [Fact]
    public void InvalidIdentityRejectsBeforeReadingAnyGameSource()
    {
        var reader = new Reader { Identity = false };
        using var operation = new CollectionReadOperation(reader, Catalog(), true, 123, 1, DateTimeOffset.UtcNow);
        Assert.True(operation.Advance(1));
        Assert.True(operation.Rejected);
        Assert.Null(operation.Result);
        Assert.Equal(0, reader.UnlockReads);
        Assert.Equal(0, reader.PresenceReads);
    }

    [Fact]
    public void PerSourceAllocationIncludesTheActualUnlockQueryWork()
    {
        var reader = new Reader();
        var measurements = new DiagnosticMeasurements();
        using var operation = new CollectionReadOperation(reader, Catalog(), true, 123, 1, DateTimeOffset.UtcNow, measurements);
        Assert.True(operation.Advance(128));
        Assert.Equal(1, reader.UnlockReads);
        Assert.Equal(4096, reader.Allocation!.Length);
        var metric = measurements.Snapshot()["Source.mounts"];
        Assert.Equal(1, metric.SampleCount);
        Assert.True(metric.AllocatedBytes.Maximum >= reader.Allocation.Length);
    }

    private static CollectionCatalog Catalog() => new("English", "fixture",
        ImmutableDictionary<string, CollectionDefinition>.Empty.Add("mounts",
            new("mounts", "Mounts", DataType.PermanentUnlock, true, [new(1, 1, "Fixture", false)])), []);

    private sealed class Reader : ICollectionReader
    {
        public bool Identity { get; init; } = true;
        public int UnlockReads { get; private set; }
        public int PresenceReads { get; private set; }
        public byte[]? Allocation { get; private set; }
        public bool IdentityMatches(ulong contentId) => Identity;
        public bool IsAvailable(string key, string version) => true;
        public bool ReadUnlock(string key, CatalogItem row) { UnlockReads++; Allocation = new byte[4096]; return true; }
        public QuestState ReadQuest(uint id, bool completed) => new(id, QuestStatus.Completed, 255);
        public CurrentItemPresenceState ReadPresence(string source, CollectionCatalog catalog, ulong contentId, long generation, DateTimeOffset now)
        {
            PresenceReads++;
            return CaptureReadPolicy.Presence([], true, true, generation, now);
        }
    }
}

