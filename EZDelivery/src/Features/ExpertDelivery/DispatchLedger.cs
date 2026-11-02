namespace EZDelivery.Features.ExpertDelivery;

/// <summary>Framework-thread reservations. Native identity/readiness checks remain the adapter's responsibility.</summary>
public sealed class DispatchLedger
{
    private readonly HashSet<(long Generation, DeliveryCommandKind Kind, long Dialog)> dispatches = [];
    private long refreshedGeneration;

    /// <summary>Generation of the last accepted row selection.</summary>
    public long SelectionGeneration { get; private set; }

    /// <summary>Reports whether this generation has not yet selected a row.</summary>
    public bool IsNewSelection(long generation) => generation > SelectionGeneration;

    /// <summary>Claims a new selection generation and resets its action tokens.</summary>
    public bool TryBeginSelection(long generation)
    {
        if (!IsNewSelection(generation)) return false;
        SelectionGeneration = generation;
        dispatches.Clear();
        return true;
    }

    /// <summary>Reports whether the action token was already reserved for dispatch.</summary>
    public bool IsDuplicate(long generation, DeliveryCommandKind kind, long dialog)
        => dispatches.Contains((generation, kind, dialog));

    /// <summary>Reserve before calling native code. A thrown/possibly-sent callback does not release the reservation.</summary>
    public bool TryRecordDispatch(long generation, DeliveryCommandKind kind, long dialog)
        => dispatches.Add((generation, kind, dialog));

    /// <summary>Allows refresh only after a selection belonging to this generation.</summary>
    public bool CanRefresh(long generation) => generation == SelectionGeneration && generation > refreshedGeneration;

    /// <summary>One refresh per selected generation, including callbacks whose outcome is uncertain.</summary>
    public bool TryRecordRefresh(long generation)
    {
        if (!CanRefresh(generation)) return false;
        refreshedGeneration = generation;
        return true;
    }
}
