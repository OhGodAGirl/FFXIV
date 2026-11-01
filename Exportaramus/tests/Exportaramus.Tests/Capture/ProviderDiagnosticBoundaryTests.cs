using System.Collections.Immutable;
using Exportaramus.Features.CollectionData.Capture;
using Exportaramus.Features.CollectionData.Catalog;

namespace Exportaramus.Tests.Capture;

public sealed class ProviderDiagnosticBoundaryTests
{
    [Fact]
    public void InvalidDresserMappingExplainsTheSlotRequirement()
    {
        var items = Enumerable.Repeat(1u, 11).ToImmutableArray();
        var unlocked = Enumerable.Repeat(true, 11).ToImmutableArray();
        Assert.Equal(new uint[] { 1 }, DresserSetExpansionPolicy.Expand(items, unlocked));
        var error = Assert.Throws<ArgumentException>(() => DresserSetExpansionPolicy.Expand(items.RemoveAt(0), unlocked));
        Assert.Contains("slot", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnsupportedTargetedCaptureExplainsTheUnsupportedOperation()
    {
        var implementation = new UntargetedPort();
        using ISnapshotCapturePort port = implementation;
        port.Begin(true, 1, 1, DateTimeOffset.UnixEpoch);
        Assert.True(implementation.Began);
        Assert.False(port.SupportsTargetedCapture);
        var error = Assert.Throws<NotSupportedException>(() => port.BeginTargeted(["inventory"], 1, 1, DateTimeOffset.UnixEpoch));
        Assert.Contains("targeted", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvalidDresserFingerprintExplainsTheSlotRequirement()
    {
        var valid = new bool[11];
        _ = StorageFingerprint.AddDresserEntry(StorageFingerprint.Initial, 1, valid);
        var error = Assert.Throws<ArgumentException>(() => StorageFingerprint.AddDresserEntry(StorageFingerprint.Initial, 1, new bool[1]));
        Assert.Contains("slot", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingCatalogVersionIdentifiesTheMissingInput()
    {
        using var validRows = Enumerable.Empty<(string Key, CatalogItem Item)>().GetEnumerator();
        using var valid = new BoundedCatalogProjection("English", "build", validRows, () => ("English", "build"), () => 0);
        Assert.True(valid.Advance());
        using var invalidRows = Enumerable.Empty<(string Key, CatalogItem Item)>().GetEnumerator();
        var error = Assert.Throws<InvalidOperationException>(() => new BoundedCatalogProjection("English", "", invalidRows, () => ("English", ""), () => 0));
        Assert.Contains("version", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ChangedCatalogIdentityExplainsWhyProjectionStopped()
    {
        var version = "build";
        using var rows = Enumerable.Repeat(("mounts", new CatalogItem(1, 1, "Mount", false)), 2).GetEnumerator();
        using var projection = new BoundedCatalogProjection("English", version, rows, () => ("English", version), () => 0);
        Assert.False(projection.Advance(1));
        version = "changed-build";
        var error = Assert.Throws<InvalidOperationException>(() => projection.Advance(1));
        Assert.Contains("changed", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class UntargetedPort : ISnapshotCapturePort
    {
        public bool Began
        {
            get; private set;
        }
        public CollectionCatalog? Catalog => null;
        public bool PrepareCatalog() => true;
        public void Begin(bool full, ulong contentId, long generation, DateTimeOffset now) => Began = true;
        public bool Advance() => true;
        public RawCaptureBatch? TakeResult() => null;
        public void ResetSession()
        {
        }
        public void Dispose()
        {
        }
    }
}
