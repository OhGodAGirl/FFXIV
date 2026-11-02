using BetterGlamorPlates.Features.Main;

namespace BetterGlamorPlates.Features.GlamourPlates;

/// <summary>Maps twelve plate slots to the preview's equipment slots, skipping waist and soul crystal.</summary>
internal static class OutfitPreviewPolicy
{
    internal static byte PreviewDye(byte savedDye, PlateDyeMode mode)
        => mode switch
        {
            PlateDyeMode.Saved => savedDye,
            PlateDyeMode.None => 0,
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };

    internal static byte ToPreviewSlot(PlateSlot slot)
    {
        if ((int)slot is < 0 or >= PlateRules.SlotCount)
            throw new ArgumentOutOfRangeException(nameof(slot));
        return (byte)((int)slot < 5 ? (int)slot : (int)slot + 1);
    }
}

internal enum OutfitPreviewAttempt { Applied, NotReady, Rejected }
internal enum OutfitPreviewProgress { Idle, Waiting, Applied, Rejected, Expired, Cancelled }
internal readonly record struct OutfitPreviewBinding(ulong CharacterId, Guid PlateId, long Revision, PlateDyeMode DyeMode);

/// <summary>Retains only the newest preview intent while the native character view initializes.</summary>
internal sealed class OutfitPreviewRequest : IDisposable
{
    internal const int MaximumAttempts = 20;
    internal const long RetryIntervalMilliseconds = 100;
    internal const long LifetimeMilliseconds = 2000;
    private OutfitPreviewBinding? binding;
    private IReadOnlyList<SavedSlotView> slots = [];
    private long startedAt;
    private long nextAttemptAt;
    private int attempts;
    private long generation;
    private bool disposed;
    internal bool IsPending => binding is not null;

    internal void Start(OutfitPreviewBinding request, IReadOnlyList<SavedSlotView> appearance, long nowMilliseconds)
    {
        Cancel();
        if (disposed || request.CharacterId == 0 || !Enum.IsDefined(request.DyeMode)) return;
        binding = request;
        // Bound and snapshot the complete outfit. The adapter rejects malformed input before writes.
        slots = appearance.Take(PlateRules.SlotCount + 1).ToArray();
        startedAt = nextAttemptAt = nowMilliseconds;
        attempts = 0;
    }

    internal OutfitPreviewProgress Advance(OutfitPreviewBinding current, long nowMilliseconds,
        Func<IReadOnlyList<SavedSlotView>, PlateDyeMode, OutfitPreviewAttempt> attempt)
    {
        if (binding is not { } requested) return OutfitPreviewProgress.Idle;
        if (requested != current)
        {
            Cancel();
            return OutfitPreviewProgress.Cancelled;
        }
        if (nowMilliseconds < startedAt || nowMilliseconds - startedAt >= LifetimeMilliseconds)
        {
            Cancel();
            return OutfitPreviewProgress.Expired;
        }
        if (nowMilliseconds < nextAttemptAt) return OutfitPreviewProgress.Waiting;
        attempts++;
        nextAttemptAt = nowMilliseconds + RetryIntervalMilliseconds;
        var requestedGeneration = generation;
        var result = OutfitPreviewAttempt.Rejected;
        try { result = attempt(slots, requested.DyeMode); }
        catch
        {
            if (generation == requestedGeneration) Cancel();
            throw;
        }
        // A newer request supersedes this callback even if an adapter re-enters synchronously.
        if (generation != requestedGeneration) return OutfitPreviewProgress.Cancelled;
        if (result == OutfitPreviewAttempt.NotReady && attempts < MaximumAttempts)
            return OutfitPreviewProgress.Waiting;
        Cancel();
        return result switch
        {
            OutfitPreviewAttempt.Applied => OutfitPreviewProgress.Applied,
            OutfitPreviewAttempt.NotReady => OutfitPreviewProgress.Expired,
            _ => OutfitPreviewProgress.Rejected,
        };
    }

    internal void Cancel()
    {
        generation++;
        binding = null;
        slots = [];
    }

    public void Dispose()
    {
        disposed = true;
        Cancel();
    }
}
