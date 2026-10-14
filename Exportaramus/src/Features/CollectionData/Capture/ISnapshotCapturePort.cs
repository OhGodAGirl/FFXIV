using Exportaramus.Features.CollectionData.Catalog;
using System.Collections.Immutable;
namespace Exportaramus.Features.CollectionData.Capture;

public interface ISnapshotCapturePort : IDisposable
{
    CollectionCatalog? Catalog { get; }
    Task ShutdownTask => Task.CompletedTask;
    ImmutableHashSet<string> TrustedSourceKeys => ImmutableHashSet<string>.Empty;
    bool PrepareCatalog();
    bool SupportsTargetedCapture => false;
    void Begin(bool full, ulong contentId, long generation, DateTimeOffset now);
    void BeginTargeted(ImmutableArray<string> sources, ulong contentId, long generation, DateTimeOffset now) =>
        throw new NotSupportedException("Targeted captures are not supported by this port.");
    bool Advance();
    RawCaptureBatch? TakeResult();
    void ResetSession();
}
