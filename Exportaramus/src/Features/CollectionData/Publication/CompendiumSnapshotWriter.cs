using Exportaramus.Features.Snapshots;
namespace Exportaramus.Features.CollectionData.Publication;

public sealed class CompendiumSnapshotWriter(AtomicSnapshotStorage storage)
{
    public Task<StorageCommitResult> WriteAsync(ulong contentId, string compactJson, string richJson, string? reports,
        bool changed, bool manual, DateTimeOffset capturedAt, CancellationToken cancellationToken, string? richV12 = null) =>
        storage.CommitAsync(contentId, compactJson, richJson, reports, changed, manual, capturedAt, cancellationToken, richV12);
}
