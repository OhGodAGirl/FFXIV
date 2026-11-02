namespace EZDelivery.Features.Shopping;

/// <summary>One fixed queue, one pending native action, and two required transaction receipts.</summary>
public sealed class PurchaseEngine
{
    private ShopOffer[] queue = [];
    private int index;
    private ShopIdentity identity;
    private DateTimeOffset deadline;
    private DateTimeOffset nextPurchaseAt;
    private bool stopRequested;
    private int baselineCount;
    private uint baselineSeals;
    private long generation;
    private readonly HashSet<uint> suppressed = [];
    private readonly HashSet<uint> uncertain = [];
    private ShopCommand? awaitingDispatch;
    private bool equipmentConfirmed;
    private bool exchangeConfirmed;

    /// <summary>Current purchase transaction phase.</summary>
    public PurchasePhase Phase { get; private set; } = PurchasePhase.Idle;
    /// <summary>Player-readable purchase progress or stop reason.</summary>
    public string Status { get; private set; } = "Ready";
    /// <summary>Number of purchases verified by inventory and seal changes in this run.</summary>
    public int ConfirmedCount { get; private set; }
    /// <summary>Whether the purchase state machine owns an active operation.</summary>
    public bool IsRunning => Phase is PurchasePhase.ValidateNext or PurchasePhase.WaitTier or PurchasePhase.WaitCategory or PurchasePhase.WaitConfirmation or PurchasePhase.Reconcile;
    /// <summary>Offer currently being navigated, confirmed or reconciled.</summary>
    public ShopOffer? Pending => IsRunning && index < queue.Length ? queue[index] : null;
    /// <summary>Item IDs locally suppressed after confirmed acquisition.</summary>
    public IReadOnlySet<uint> Suppressed => suppressed;
    /// <summary>Raised only when item count and seal debit confirm a purchase.</summary>
    public event Action<ShopOffer>? Acquired;

    /// <summary>Allows reacquisition of an item confirmed delivered by the same character.</summary>
    public void ConfirmDelivery(ulong character, uint itemId)
    {
        if (character == 0 || character != identity.Character) return;
        suppressed.Remove(itemId);
        // An uncertain purchase is not resolved by an unrelated delivery receipt.
    }

    /// <summary>Clears local suppression of previously purchased item IDs.</summary>
    public bool ResetPurchaseTracking()
    {
        if (IsRunning) return false;
        suppressed.Clear();
        return true;
    }

    /// <summary>Captures a purchase queue and binds it to the supplied shop identity.</summary>
    public bool Start(IEnumerable<ShopOffer> displayed, ShopIdentity current)
    {
        if (IsRunning) return false;
        if (current.Character == 0 || current.Company is < 1 or > 3 || current.Addon == 0)
        { Finish(PurchasePhase.Blocked, "Open your Grand Company Seal Exchange."); return false; }
        if (identity.Character != current.Character) { suppressed.Clear(); uncertain.Clear(); }
        queue = CatalogClassification.Snapshot(displayed);
        if (queue.Length == 0) { Finish(PurchasePhase.Completed, "No missing items shown."); return false; }
        identity = current;
        index = 0;
        ConfirmedCount = 0;
        generation++;
        stopRequested = false;
        awaitingDispatch = null;
        Phase = PurchasePhase.ValidateNext;
        Status = "Checking next item";
        return true;
    }

    /// <summary>Stops new purchases while preserving reconciliation of an action already sent.</summary>
    public void Stop()
    {
        stopRequested = true;
        if (IsRunning && Phase != PurchasePhase.Reconcile && awaitingDispatch == null)
            Finish(PurchasePhase.Stopped, "Purchasing stopped.");
    }

    /// <summary>Clears local purchase suppression when character identity changes.</summary>
    public void ClearCharacter(ulong currentCharacter)
    {
        if (currentCharacter == identity.Character) return;
        if (IsRunning) Finish(Phase == PurchasePhase.Reconcile ? PurchasePhase.OutcomeUncertain : PurchasePhase.Stopped, "Character changed.");
        suppressed.Clear();
        uncertain.Clear();
        identity = default;
    }

    /// <summary>Advances one purchase step from native facts and collection evidence.</summary>
    public ShopCommand? Tick(PurchaseObservation observation, PurchaseKnowledge knowledge, DateTimeOffset now)
    {
        if (!IsRunning || awaitingDispatch != null) return null;
        var offer = queue[index];
        if (Phase == PurchasePhase.Reconcile)
        {
            // Addon closure is allowed while observing an already sent transaction; character/company loss is not.
            if (observation.Identity.Character != identity.Character || observation.Identity.Company != identity.Company)
            { Uncertain(offer, "Purchase outcome unknown after context loss."); return null; }
            if (observation.ItemCount == (long)baselineCount + 1 && baselineSeals >= offer.UnitCost && observation.Seals == baselineSeals - offer.UnitCost)
            {
                suppressed.Add(offer.ItemId);
                ConfirmedCount++;
                nextPurchaseAt = now.AddMilliseconds(580);
                index++;
                Phase = PurchasePhase.ValidateNext;
                Status = "Purchase confirmed";
                if (stopRequested || observation.Identity != identity) Finish(PurchasePhase.Stopped, "Purchasing stopped after checking the sent purchase.");
                else if (index == queue.Length) Finish(PurchasePhase.Completed, $"Purchased {ConfirmedCount} item(s).");
                Acquired?.Invoke(offer);
            }
            else if (now < deadline && !stopRequested && observation.Dialog is { Kind: PurchaseDialogKind.Equipment or PurchaseDialogKind.Exchange } warning &&
                !(warning.Kind == PurchaseDialogKind.Equipment ? equipmentConfirmed : exchangeConfirmed))
            {
                if (!observation.Ready || observation.Identity != identity || !ExactOffer(observation, offer) ||
                    !warning.Correlated || warning.ItemId != offer.ItemId || warning.Quantity != 1 || warning.TotalCost != offer.UnitCost ||
                    observation.ItemCount != baselineCount || observation.Seals != baselineSeals || !observation.HasCapacity ||
                    knowledge.Presence != PurchasePresence.Missing || offer.RequiredRank > identity.Rank)
                { Uncertain(offer, "The transaction confirmation no longer matches the pending purchase."); return null; }
                return Emit(warning.Kind == PurchaseDialogKind.Equipment ? ShopCommandKind.ConfirmEquipment : ShopCommandKind.ConfirmExchange, observation, offer, warning.Identity);
            }
            else if (now >= deadline) Uncertain(offer, "Purchase outcome unknown: inventory and seals did not both confirm.");
            return null;
        }
        if (stopRequested) { Finish(PurchasePhase.Stopped, "Purchasing stopped."); return null; }
        if (!observation.Ready || observation.Identity != identity)
        { Finish(PurchasePhase.Blocked, observation.Reason.Length > 0 ? observation.Reason : "Shop context changed."); return null; }
        if (Phase == PurchasePhase.ValidateNext && now < nextPurchaseAt)
        { Status = "Purchase confirmed; waiting for the shop to settle"; return null; }
        if (offer.Company != identity.Company || offer.RequiredRank > identity.Rank)
        { Finish(PurchasePhase.Blocked, "This offer no longer matches your company and rank."); return null; }
        if (uncertain.Contains(offer.ItemId))
        { Finish(PurchasePhase.Blocked, "This item's earlier purchase outcome remains unknown."); return null; }
        if (Phase == PurchasePhase.ValidateNext && (knowledge.Presence == PurchasePresence.Present || suppressed.Contains(offer.ItemId) || observation.ItemCount > 0))
        {
            if (++index == queue.Length) Finish(PurchasePhase.Completed, $"Purchased {ConfirmedCount} item(s).");
            return null;
        }
        if (knowledge.Presence != PurchasePresence.Missing)
        { Finish(PurchasePhase.Blocked, knowledge.Reason.Length > 0 ? knowledge.Reason : "Missing items cannot be determined yet."); return null; }
        if (observation.Seals < offer.UnitCost) { Finish(PurchasePhase.Stopped, "Stopped: not enough seals for the next item."); return null; }
        if (observation.ItemCount < 0 || !observation.HasCapacity) { Finish(PurchasePhase.Blocked, "Inventory space could not be confirmed."); return null; }

        if (Phase == PurchasePhase.WaitConfirmation)
        {
            if (now >= deadline) { Finish(PurchasePhase.Blocked, "The matching purchase confirmation did not appear."); return null; }
            if (observation.Dialog is { } dialog)
            {
                if (!dialog.Correlated || dialog.ItemId != offer.ItemId || dialog.Quantity != 1 || dialog.TotalCost != offer.UnitCost)
                { Finish(PurchasePhase.Blocked, "Stopped: an unrelated confirmation is open."); return null; }
                if (!ExactOffer(observation, offer) || observation.ItemCount != baselineCount || observation.Seals != baselineSeals)
                { Finish(PurchasePhase.Blocked, "The offer or resources changed before confirmation."); return null; }
                return Emit(dialog.Kind switch { PurchaseDialogKind.Equipment => ShopCommandKind.ConfirmEquipment, PurchaseDialogKind.Exchange => ShopCommandKind.ConfirmExchange, _ => ShopCommandKind.ConfirmOnce }, observation, offer, dialog.Identity);
            }
            return null;
        }
        if (observation.Dialog != null) { Finish(PurchasePhase.Blocked, "Close the existing confirmation first."); return null; }
        if (Phase == PurchasePhase.WaitTier)
        {
            if (now >= deadline) { Finish(PurchasePhase.Blocked, "The rank tab did not become ready."); return null; }
            if (observation.Tier != offer.Tier)
                return null;
            Phase = PurchasePhase.ValidateNext;
        }
        if (Phase == PurchasePhase.WaitCategory)
        {
            if (now >= deadline) { Finish(PurchasePhase.Blocked, "The category tab did not become ready."); return null; }
            if (observation.Tier != offer.Tier) { Finish(PurchasePhase.Blocked, "The selected rank tab changed."); return null; }
            if (observation.NativeCategory != offer.NativeCategory)
                return null;
            Phase = PurchasePhase.ValidateNext;
        }
        if (observation.Tier != offer.Tier) return Emit(ShopCommandKind.SelectTier, observation, offer);
        if (observation.NativeCategory != offer.NativeCategory) return Emit(ShopCommandKind.SelectCategory, observation, offer);
        if (!ExactOffer(observation, offer)) { Finish(PurchasePhase.Blocked, "The exact native item and seal price could not be matched."); return null; }
        baselineCount = observation.ItemCount;
        baselineSeals = observation.Seals;
        equipmentConfirmed = false;
        exchangeConfirmed = false;
        return Emit(ShopCommandKind.RequestOne, observation, offer);
    }

    /// <summary>Records the send result and starts the appropriate confirmation or reconciliation wait.</summary>
    public void Dispatched(ShopCommand command, ShopDispatchResult result, DateTimeOffset now)
    {
        if (awaitingDispatch != command || command.Generation != generation) return;
        awaitingDispatch = null;
        if (!result.Sent)
        {
            if (result.PossiblySent && command.Kind is ShopCommandKind.ConfirmOnce or ShopCommandKind.ConfirmEquipment or ShopCommandKind.ConfirmExchange or ShopCommandKind.RequestOne)
            {
                // A failed call can still have entered the game. Observe its receipts,
                // but never send another confirmation or start the next queued item.
                stopRequested = true;
                Phase = PurchasePhase.Reconcile;
                deadline = now.AddSeconds(5);
                Status = "Checking the possibly sent purchase; no further actions will be sent.";
                return;
            }
            Finish(PurchasePhase.Blocked, result.Reason.Length > 0 ? result.Reason : "The native action was rejected.");
            return;
        }
        if (command.Kind == ShopCommandKind.ConfirmEquipment) equipmentConfirmed = true;
        if (command.Kind == ShopCommandKind.ConfirmExchange) exchangeConfirmed = true;
        Phase = command.Kind switch
        {
            ShopCommandKind.SelectTier => PurchasePhase.WaitTier,
            ShopCommandKind.SelectCategory => PurchasePhase.WaitCategory,
            ShopCommandKind.RequestOne => result.ExpectsConfirmation ? PurchasePhase.WaitConfirmation : PurchasePhase.Reconcile,
            ShopCommandKind.ConfirmOnce => PurchasePhase.Reconcile,
            ShopCommandKind.ConfirmEquipment => PurchasePhase.Reconcile,
            ShopCommandKind.ConfirmExchange => PurchasePhase.Reconcile,
            _ => throw new ArgumentOutOfRangeException(nameof(command)),
        };
        deadline = now.AddSeconds(Phase is PurchasePhase.WaitTier or PurchasePhase.WaitCategory ? 2 : 5);
        Status = Phase switch { PurchasePhase.Reconcile => "Checking inventory and seals", PurchasePhase.WaitConfirmation => "Waiting for purchase confirmation", _ => "Changing native shop tab" };
    }

    private ShopCommand Emit(ShopCommandKind kind, PurchaseObservation observation, ShopOffer offer, ulong dialog = 0)
        => awaitingDispatch = new(generation, kind, offer, identity, observation.NativeRow, dialog, observation.Seals, observation.ItemCount);
    private static bool ExactOffer(PurchaseObservation observation, ShopOffer offer)
        => observation.NativeRow >= 0 && observation.Tier == offer.Tier && observation.NativeCategory == offer.NativeCategory && observation.LiveItemId == offer.ItemId && observation.LiveCost == offer.UnitCost;
    private void Uncertain(ShopOffer offer, string reason) { uncertain.Add(offer.ItemId); Finish(PurchasePhase.OutcomeUncertain, reason); }
    private void Finish(PurchasePhase phase, string reason) { Phase = phase; Status = reason; awaitingDispatch = null; }
}
