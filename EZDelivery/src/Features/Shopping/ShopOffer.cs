namespace EZDelivery.Features.Shopping;

/// <summary>Collection category offered by the Quartermaster browser.</summary>
public enum ShopCategory
{
    /// <summary>Missing weapon appearances.</summary>
    Weapons,
    /// <summary>Missing armor appearances.</summary>
    Armor,
    /// <summary>Missing chocobo barding unlocks.</summary>
    Barding,
    /// <summary>Furnishings absent from tracked storage; placed furnishings are not counted.</summary>
    Furniture,
}
/// <summary>Whether collection evidence establishes presence or absence.</summary>
public enum PurchasePresence
{
    /// <summary>Evidence establishes that the item is already owned.</summary>
    Present,
    /// <summary>Complete evidence establishes absence.</summary>
    Missing,
    /// <summary>Available evidence cannot establish presence or absence.</summary>
    Unknown,
}
/// <summary>Presence evidence for a purchase decision; unknown evidence cannot establish absence.</summary>
public sealed record PurchaseKnowledge(PurchasePresence Presence, string Reason = "");

/// <summary>Catalog offer identity, unlock metadata, native routing and unit seal price.</summary>
public sealed record ShopOffer(
    string OfferKey, uint ItemId, uint IconId, string Name, ShopCategory Category,
    byte Company, byte RequiredRank, int Tier, int NativeCategory, uint UnitCost,
    uint BardingUnlockId, uint SheetRowId, ushort SheetSubrowId, byte SortKey,
    uint StackSize = 1, bool IsUnique = false);

/// <summary>Character and native shop context bound to one purchase run.</summary>
public readonly record struct ShopIdentity(ulong Character, uint Territory, byte Company, byte Rank, ulong Addon);
/// <summary>Purchase transaction state, including waits and uncertain outcomes.</summary>
public enum PurchasePhase
{
    /// <summary>No operation is active.</summary>
    Idle,
    /// <summary>Validating the next queued offer.</summary>
    ValidateNext,
    /// <summary>Waiting for native rank-tier navigation.</summary>
    WaitTier,
    /// <summary>Waiting for native item-category navigation.</summary>
    WaitCategory,
    /// <summary>Waiting for the owned purchase dialog.</summary>
    WaitConfirmation,
    /// <summary>Checking inventory and seals for the transaction outcome.</summary>
    Reconcile,
    /// <summary>All queued purchases have completed.</summary>
    Completed,
    /// <summary>No new actions will be dispatched.</summary>
    Stopped,
    /// <summary>Required conditions prevent the purchase.</summary>
    Blocked,
    /// <summary>An action may have occurred but its outcome is not proven.</summary>
    OutcomeUncertain,
}
/// <summary>Native navigation or confirmation action for one purchase.</summary>
public enum ShopCommandKind
{
    /// <summary>Selects the required native rank tier.</summary>
    SelectTier,
    /// <summary>Selects the required native item category.</summary>
    SelectCategory,
    /// <summary>Requests one unit of the matched offer.</summary>
    RequestOne,
    /// <summary>Accepts the correlated currency confirmation.</summary>
    ConfirmOnce,
    /// <summary>Accepts the correlated equipment warning.</summary>
    ConfirmEquipment,
    /// <summary>Accepts the correlated exchange confirmation.</summary>
    ConfirmExchange,
}
/// <summary>Purchase action with expected row, dialog, seals and inventory count.</summary>
public sealed record ShopCommand(long Generation, ShopCommandKind Kind, ShopOffer Offer, ShopIdentity Identity, int NativeRow, ulong DialogIdentity, uint ExpectedSeals, int ExpectedCount);
/// <summary>Native purchase confirmation variant.</summary>
public enum PurchaseDialogKind
{
    /// <summary>Currency purchase confirmation.</summary>
    Currency,
    /// <summary>Equipment warning or equipment ownership evidence.</summary>
    Equipment,
    /// <summary>Final item-for-seals exchange confirmation.</summary>
    Exchange,
}
/// <summary>Observed purchase confirmation correlated to an exact item, quantity and total.</summary>
public sealed record PurchaseDialog(ulong Identity, bool Correlated, uint ItemId, uint Quantity, uint TotalCost, PurchaseDialogKind Kind = PurchaseDialogKind.Currency);
/// <summary>Native shop readiness, selected offer, inventory capacity and confirmation facts.</summary>
public sealed record PurchaseObservation(
    bool Ready, string Reason, ShopIdentity Identity, int Tier, int NativeCategory,
    int NativeRow, uint LiveItemId, uint LiveCost, uint Seals, int ItemCount,
    bool HasCapacity, PurchaseDialog? Dialog = null);
/// <summary>Native send outcome and whether the action expects a confirmation dialog.</summary>
public sealed record ShopDispatchResult(bool Sent, bool PossiblySent, string Reason = "", bool ExpectsConfirmation = true);
/// <summary>Native shop window bounds for attached category tabs.</summary>
public sealed record ShopAnchor(float X, float Y, float Width, float Height);

/// <summary>Native purchase observation and dispatch boundary.</summary>
public interface IShoppingNative
{
    /// <summary>Reads native shop facts for the optional pending offer.</summary>
    PurchaseObservation Observe(ShopOffer? pending);
    /// <summary>Attempts one correlated native purchase action.</summary>
    ShopDispatchResult Dispatch(ShopCommand command);
}
