namespace EZDelivery.Features.ExpertDelivery;

/// <summary>Completeness and validity of a native delivery observation.</summary>
public enum DeliveryReadState
{
    /// <summary>A complete usable observation is available.</summary>
    Ready,
    /// <summary>The complete filtered list contains no items.</summary>
    ReadyEmpty,
    /// <summary>Required native state is not ready.</summary>
    NotReady,
    /// <summary>The native layout is not supported.</summary>
    Unsupported,
    /// <summary>Observed fields disagree.</summary>
    Inconsistent,
    /// <summary>The native read failed.</summary>
    Failed,
}
/// <summary>Delivery transaction state; only reconciliation establishes completion.</summary>
public enum DeliveryPhase
{
    /// <summary>No operation is active.</summary>
    Idle,
    /// <summary>Waiting for a usable item list.</summary>
    ReadList,
    /// <summary>Waiting for the selected item dialog.</summary>
    WaitDialog,
    /// <summary>Waiting for the delivery reward dialog.</summary>
    WaitReward,
    /// <summary>Checking inventory and seals for the transaction outcome.</summary>
    Reconcile,
    /// <summary>Waiting before the next item action.</summary>
    Cooldown,
    /// <summary>No new actions will be dispatched.</summary>
    Stopped,
    /// <summary>An action may have occurred but its outcome is not proven.</summary>
    OutcomeUncertain,
}
/// <summary>Reason a delivery cannot start or continue.</summary>
public enum DeliveryReason
{
    /// <summary>No reason or dialog is present.</summary>
    None,
    /// <summary>No active character is available.</summary>
    NoCharacter,
    /// <summary>The NPC or native window context changed.</summary>
    WrongContext,
    /// <summary>Required native state is not ready.</summary>
    NotReady,
    /// <summary>No eligible items remain.</summary>
    Empty,
    /// <summary>Displayed rows cannot be matched to exact inventory sources.</summary>
    MappingMismatch,
    /// <summary>The full seal reward cannot be established.</summary>
    RewardUnknown,
    /// <summary>The next reward would exceed the seal cap.</summary>
    SealCapacity,
    /// <summary>The selected in-game item filter changed.</summary>
    FilterChanged,
    /// <summary>The open dialog does not belong to this transaction.</summary>
    UnrelatedDialog,
    /// <summary>The native action was rejected before dispatch.</summary>
    DispatchRejected,
    /// <summary>An action may have occurred but its outcome is not proven.</summary>
    OutcomeUncertain,
    /// <summary>The player requested a stop.</summary>
    Cancelled,
    /// <summary>The running game or structure binary is not admitted.</summary>
    UnsupportedBuild,
}
/// <summary>Native action requested by the delivery state machine.</summary>
public enum DeliveryCommandKind
{
    /// <summary>Selects one mapped delivery row.</summary>
    Select,
    /// <summary>Accepts the correlated high-quality warning.</summary>
    ConfirmHq,
    /// <summary>Accepts the correlated spiritbond warning.</summary>
    ConfirmSpiritbond,
    /// <summary>Submits the correlated reward dialog.</summary>
    Submit,
    /// <summary>Cancels the correlated reward dialog.</summary>
    CancelReward,
}
/// <summary>Recognized delivery dialog category.</summary>
public enum DeliveryDialogKind
{
    /// <summary>No reason or dialog is present.</summary>
    None,
    /// <summary>Expert Delivery reward confirmation.</summary>
    Reward,
    /// <summary>High-quality item confirmation.</summary>
    HqWarning,
    /// <summary>Spiritbond item confirmation.</summary>
    SpiritbondWarning,
    /// <summary>A dialog not correlated to the pending item.</summary>
    Unrelated,
}
/// <summary>Delivery window bounds and native scale used to place its attached tab.</summary>
public sealed record DeliveryAnchor(float X, float Y, float Width, float Height, float Scale);
/// <summary>Character, territory, company, rank and addon identity bound to a delivery run.</summary>
public sealed record DeliveryContext(ulong Character, uint Territory, byte Company, byte Rank, long Addon);
/// <summary>Exact inventory slot and item fingerprint used to detect replacement or removal.</summary>
public sealed record DeliverySource(int Container, int Slot, uint RawItemId, uint ItemId, bool Hq, int Quantity, ulong Fingerprint);
/// <summary>Visible selection mapped to its native record, source item and seal reward.</summary>
public sealed record DeliveryRow(int VisibleIndex, int AgentIndex, DeliverySource Source, bool Available, uint? Reward, string Name);
/// <summary>Observed dialog identity, reward and correlation with the pending delivery.</summary>
public sealed record DeliveryDialog(DeliveryDialogKind Kind, long Identity, uint ItemId, bool Correlated, uint? Reward, bool Enabled);
/// <summary>One coherent native read, including list mapping, currency and pending dialog facts.</summary>
public sealed record DeliveryObservation(
    DeliveryReadState State, DeliveryReason Reason, DeliveryContext? Context, DeliveryAnchor? Anchor,
    int Filter, int Sort, long MapGeneration, uint Seals, uint SealCap,
    IReadOnlyList<DeliveryRow> Rows, DeliverySource? PendingSource, bool PendingSourceReadable,
    DeliveryDialog Dialog)
{
    /// <summary>Creates a non-ready observation with no usable native identities.</summary>
    public static DeliveryObservation Unavailable(DeliveryReason reason) => new(DeliveryReadState.NotReady, reason, null, null, -1, -1, 0, 0, 0, [], null, false, new(DeliveryDialogKind.None, 0, 0, false, null, false));
}
/// <summary>Transaction identity and pre-dispatch values retained until the outcome is reconciled.</summary>
public sealed record DeliveryPending(long Generation, DeliveryContext Context, int Filter, int Sort, DeliveryRow Row,
    uint BeforeSeals, uint Reward, long DialogIdentity, bool Submitted);
/// <summary>One native action bound to the pending transaction and observed list generation.</summary>
public sealed record DeliveryCommand(DeliveryCommandKind Kind, DeliveryPending Pending, long MapGeneration);
/// <summary>Request to refresh the same expert list without changing its filter or sort.</summary>
public sealed record DeliveryRefreshRequest(long Generation, DeliveryContext Context, int Filter, int Sort);
/// <summary>Distinguishes rejected dispatch from a sent or possibly sent native action.</summary>
public sealed record DeliveryDispatchResult(bool Dispatched, bool MayHaveDispatched, DeliveryReason Reason)
{
    /// <summary>Result indicating that the native action was dispatched.</summary>
    public static DeliveryDispatchResult Sent { get; } = new(true, true, DeliveryReason.None);
    /// <summary>Creates a result proving that no native action was sent.</summary>
    public static DeliveryDispatchResult Rejected(DeliveryReason reason) => new(false, false, reason);
}
/// <summary>Current delivery phase, stop reason and confirmed versus dispatched totals.</summary>
public sealed record DeliveryStatus(DeliveryPhase Phase, DeliveryReason Reason, string Message, int ConfirmedCount, int DispatchedCount);
/// <summary>Native observation and dispatch boundary; product decisions remain in the facade.</summary>
public interface IDeliveryNativeAdapter
{
    /// <summary>Reads current native facts for an optional pending delivery.</summary>
    DeliveryObservation Observe(DeliveryPending? pending);
    /// <summary>Attempts one correlated command and reports send certainty.</summary>
    DeliveryDispatchResult Dispatch(DeliveryCommand command);
    /// <summary>Requests a list refresh without changing the retained context or filter.</summary>
    DeliveryDispatchResult RefreshExpertList(DeliveryRefreshRequest request);
}
