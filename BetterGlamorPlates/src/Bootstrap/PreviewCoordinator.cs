using BetterGlamorPlates.Features.GlamourPlates;
using BetterGlamorPlates.Features.Main;

namespace BetterGlamorPlates.Bootstrap;

internal readonly record struct PreviewContext(bool WindowOpen, bool EditorOpen, bool DyeWindowOpen,
    ulong CharacterId, MainViewState View);

/// <summary>Coordinates preview intent with native availability and user-visible outcomes.</summary>
internal sealed class PreviewCoordinator(
    Func<PreviewContext> context,
    Func<long> clock,
    Func<IReadOnlyList<SavedSlotView>, PlateDyeMode, OutfitPreviewAttempt> apply,
    Action release,
    Action<bool> report,
    Action rejected,
    Action<Exception> error) : IDisposable
{
    private readonly OutfitPreviewRequest request = new();
    private bool reportSuccess;
    internal bool IsPending => request.IsPending;

    internal void Request(bool reportApplied)
    {
        var current = context();
        var plateId = current.View.SelectedPlateId;
        if (!plateId.HasValue)
        {
            Cancel();
            return;
        }
        reportSuccess = reportApplied;
        request.Start(new(current.CharacterId, plateId.GetValueOrDefault(), current.View.OutfitRevision, current.View.PreviewDyeMode),
            current.View.Slots, clock());
        Advance();
    }

    internal void Cancel()
    {
        request.Cancel();
        release();
    }

    internal void Advance()
    {
        if (!request.IsPending) return;
        var current = context();
        var plateId = current.View.SelectedPlateId;
        if (!current.WindowOpen || !current.EditorOpen || current.DyeWindowOpen || !plateId.HasValue)
        {
            Cancel();
            return;
        }
        try
        {
            var progress = request.Advance(new(current.CharacterId, plateId.GetValueOrDefault(), current.View.OutfitRevision,
                current.View.PreviewDyeMode), clock(), apply);
            if (progress == OutfitPreviewProgress.Applied && reportSuccess) report(true);
            else if (progress is OutfitPreviewProgress.Rejected or OutfitPreviewProgress.Expired)
            {
                release();
                rejected();
                report(false);
            }
        }
        catch (Exception exception)
        {
            // An ambiguous native write is never retried automatically.
            release();
            report(false);
            error(exception);
        }
    }

    public void Dispose() => request.Dispose();
}
