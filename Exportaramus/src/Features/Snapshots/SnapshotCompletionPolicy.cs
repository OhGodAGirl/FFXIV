namespace Exportaramus.Features.Snapshots;

public static class SnapshotCompletionPolicy
{
    public static bool IsCurrent(long resultGeneration, ulong resultContentId, long activeGeneration, ulong activeContentId, bool canceled) =>
        resultGeneration == activeGeneration && resultContentId == activeContentId && !canceled;
}
