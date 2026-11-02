namespace EZDelivery.Features.ExpertDelivery;

/// <summary>Framework-thread operation owner. No native observations or commands occur from Draw.</summary>
public sealed class ExpertDeliveryFacade(IDeliveryNativeAdapter adapter)
{
    private readonly HashSet<(ulong Character, int Container, int Slot, uint Item, ulong Fingerprint)> uncertainSources = [];
    private DeliveryPending? pending;
    private DeliveryContext? context;
    private int filter;
    private int sort;
    private long generation;
    private long deadline;
    private long confirmedAt;
    private long confirmedGeneration;
    private bool refreshedAfterReceipt;
    private bool stopping;
    private bool hqConfirmed;
    private bool spiritbondConfirmed;
    /// <summary>Raised only after exact inventory removal and the full seal reward agree.</summary>
    public event Action<ulong, uint, DateTimeOffset>? Delivered;
    /// <summary>Latest delivery facts read from the native adapter.</summary>
    public DeliveryObservation Observation { get; private set; } = DeliveryObservation.Unavailable(DeliveryReason.NotReady);
    /// <summary>Prepared phase, reason and counters for the player interface.</summary>
    public DeliveryStatus Status { get; private set; } = new(DeliveryPhase.Idle, DeliveryReason.None, "Start Expert Deliveries", 0, 0);
    /// <summary>Whether the delivery state machine owns an active operation.</summary>
    public bool IsRunning => Status.Phase is DeliveryPhase.ReadList or DeliveryPhase.WaitDialog or DeliveryPhase.WaitReward or DeliveryPhase.Reconcile or DeliveryPhase.Cooldown;

    /// <summary>Starts a valid delivery run or requests that an active run stop.</summary>
    public void Toggle(long nowMs)
    {
        if (IsRunning) { Stop(); return; }
        Observation = adapter.Observe(null);
        generation++;
        stopping = false;
        pending = null;
        hqConfirmed = false;
        spiritbondConfirmed = false;
        refreshedAfterReceipt = false;
        Status = new(DeliveryPhase.ReadList, DeliveryReason.None, "Reading the native item list", 0, 0);
        context = Observation.Context;
        filter = Observation.Filter;
        sort = Observation.Sort;
        deadline = nowMs + 5000;
    }

    /// <summary>Stops new delivery actions while reconciling any action already sent.</summary>
    public void Stop()
    {
        stopping = true;
        if (!IsRunning) Finish(DeliveryReason.Cancelled);
    }

    /// <summary>Advances the delivery state machine using the current monotonic time.</summary>
    public void Tick(long nowMs)
    {
        Observation = adapter.Observe(pending);
        if (!IsRunning) return;
        if (Status.Phase == DeliveryPhase.Reconcile)
        {
            Reconcile(nowMs);
            return;
        }
        if (stopping) { CancelOwnedReward(DeliveryReason.Cancelled); return; }
        if (Status.Phase == DeliveryPhase.Cooldown)
        {
            // A confirmed transaction may close/recreate the list. Only this
            // receipt-backed transition can acquire its refreshed addon instance.
            if (!SameSession(Observation.Context)) { Finish(DeliveryReason.WrongContext); return; }
            if (nowMs >= deadline) { Finish(Observation.Reason == DeliveryReason.MappingMismatch ? DeliveryReason.MappingMismatch : DeliveryReason.NotReady); return; }
            if (Observation.Context!.Addon != 0 && (Observation.Filter != filter || Observation.Sort != sort))
            { Finish(DeliveryReason.FilterChanged); return; }
            if (Observation.Dialog.Kind != DeliveryDialogKind.None) return;
            if (nowMs < confirmedAt + 580) return;
            if (!refreshedAfterReceipt && Observation.Context.Addon != 0 &&
                Observation.State == DeliveryReadState.Inconsistent && Observation.Reason == DeliveryReason.MappingMismatch)
            {
                DeliveryDispatchResult refresh;
                try { refresh = adapter.RefreshExpertList(new(confirmedGeneration, Observation.Context, filter, sort)); }
                catch { refresh = new(false, true, DeliveryReason.DispatchRejected); }
                if (refresh.Dispatched || refresh.MayHaveDispatched)
                {
                    refreshedAfterReceipt = true;
                    Status = Status with { Message = "Refreshing the Expert Delivery list" };
                    return; // Never select using the observation from before refresh.
                }
                if (refresh.Reason != DeliveryReason.NotReady) Finish(refresh.Reason);
                return;
            }
            if (Observation.State == DeliveryReadState.Inconsistent && Observation.Reason == DeliveryReason.MappingMismatch) return;
            if (Observation.State == DeliveryReadState.NotReady || Observation.Context!.Addon == 0) return;
            if (Observation.State is not (DeliveryReadState.Ready or DeliveryReadState.ReadyEmpty))
            { Finish(Observation.Reason == DeliveryReason.None ? DeliveryReason.NotReady : Observation.Reason); return; }
            context = Observation.Context;
            Status = Status with { Phase = DeliveryPhase.ReadList, Message = "Reading the refreshed native list" };
        }
        if (context is null || Observation.Context != context) { Finish(DeliveryReason.WrongContext); return; }
        if (Observation.Filter != filter || Observation.Sort != sort) { Finish(DeliveryReason.FilterChanged); return; }
        if (Observation.Dialog.Kind == DeliveryDialogKind.Unrelated) { Finish(DeliveryReason.UnrelatedDialog); return; }

        if (Status.Phase == DeliveryPhase.ReadList) { SelectNext(nowMs); return; }
        if (nowMs >= deadline) { Finish(DeliveryReason.NotReady); return; }
        if (pending is null) { Finish(DeliveryReason.MappingMismatch); return; }
        if (!Observation.PendingSourceReadable || !DeliveryRules.SameInstance(pending.Row.Source, Observation.PendingSource)) { Finish(DeliveryReason.MappingMismatch); return; }
        var dialog = Observation.Dialog;
        if (dialog.Kind == DeliveryDialogKind.None) return;
        if (!dialog.Correlated || dialog.ItemId != pending.Row.Source.ItemId) { Finish(DeliveryReason.UnrelatedDialog); return; }
        if (!dialog.Enabled) return;
        if (dialog.Kind is DeliveryDialogKind.HqWarning or DeliveryDialogKind.SpiritbondWarning)
        {
            if (dialog.Kind == DeliveryDialogKind.HqWarning)
            {
                if (hqConfirmed && pending.DialogIdentity == dialog.Identity) return;
                if (hqConfirmed || !pending.Row.Source.Hq) { Finish(DeliveryReason.UnrelatedDialog); return; }
                hqConfirmed = true;
            }
            else
            {
                if (spiritbondConfirmed && pending.DialogIdentity == dialog.Identity) return;
                if (spiritbondConfirmed) { Finish(DeliveryReason.UnrelatedDialog); return; }
                spiritbondConfirmed = true;
            }
            pending = pending with { DialogIdentity = dialog.Identity };
            Dispatch(new(dialog.Kind == DeliveryDialogKind.HqWarning ? DeliveryCommandKind.ConfirmHq : DeliveryCommandKind.ConfirmSpiritbond, pending, Observation.MapGeneration), nowMs);
            if (IsRunning) Status = Status with { Phase = DeliveryPhase.WaitReward, Message = "Waiting for the delivery reward" };
            return;
        }
        if (dialog.Kind != DeliveryDialogKind.Reward) { Finish(DeliveryReason.UnrelatedDialog); return; }
        if (dialog.Reward is not > 0) { CancelOwnedReward(DeliveryReason.RewardUnknown); return; }
        if (!DeliveryRules.RewardFits(Observation.Seals, Observation.SealCap, dialog.Reward)) { CancelOwnedReward(DeliveryReason.SealCapacity); return; }
        pending = pending with { BeforeSeals = Observation.Seals, Reward = dialog.Reward.Value, DialogIdentity = dialog.Identity, Submitted = true };
        deadline = nowMs + 5000;
        Status = Status with { Phase = DeliveryPhase.Reconcile, Message = "Checking the delivery result", DispatchedCount = Status.DispatchedCount + 1 };
        Dispatch(new(DeliveryCommandKind.Submit, pending, Observation.MapGeneration), nowMs);
    }

    private void SelectNext(long nowMs)
    {
        if (Observation.State == DeliveryReadState.ReadyEmpty && Observation.Rows.Count == 0) { Finish(DeliveryReason.Empty); return; }
        if (Observation.State != DeliveryReadState.Ready)
        {
            if (Observation.State == DeliveryReadState.NotReady && nowMs < deadline) return;
            Finish(Observation.Reason == DeliveryReason.None ? DeliveryReason.NotReady : Observation.Reason);
            return;
        }
        if (!DeliveryRules.ValidMapping(Observation.Rows) || Observation.Rows.Count == 0) { Finish(DeliveryReason.MappingMismatch); return; }
        var row = Observation.Rows.FirstOrDefault(x => x.Available);
        if (row is null) { Finish(DeliveryReason.Empty); return; }
        if (uncertainSources.Contains((context!.Character, row.Source.Container, row.Source.Slot, row.Source.ItemId, row.Source.Fingerprint))) { Finish(DeliveryReason.OutcomeUncertain); return; }
        // An unknown preselection estimate cannot authorize submission; the owned reward dialog supplies the final amount.
        if (row.Reward is > 0 && !DeliveryRules.RewardFits(Observation.Seals, Observation.SealCap, row.Reward)) { Finish(DeliveryReason.SealCapacity); return; }
        if (Observation.SealCap == 0 || Observation.Seals > Observation.SealCap) { Finish(DeliveryReason.RewardUnknown); return; }
        if (Observation.Dialog.Kind != DeliveryDialogKind.None) { Finish(DeliveryReason.UnrelatedDialog); return; }
        pending = new(++generation, context!, filter, sort, row, Observation.Seals, 0, 0, false);
        hqConfirmed = false;
        spiritbondConfirmed = false;
        deadline = nowMs + 5000;
        Status = Status with { Phase = DeliveryPhase.WaitDialog, Message = "Waiting for the delivery confirmation" };
        Dispatch(new(DeliveryCommandKind.Select, pending, Observation.MapGeneration), nowMs);
    }

    private void Dispatch(DeliveryCommand command, long nowMs)
    {
        DeliveryDispatchResult result;
        try { result = adapter.Dispatch(command); }
        catch { result = new(false, true, DeliveryReason.DispatchRejected); }
        if (result.Dispatched) return;
        if (command.Kind == DeliveryCommandKind.Submit && result.MayHaveDispatched)
        {
            deadline = nowMs + 5000;
            return;
        }
        if (command.Kind == DeliveryCommandKind.Submit) pending = pending! with { Submitted = false };
        Finish(result.Reason == DeliveryReason.None ? DeliveryReason.DispatchRejected : result.Reason);
    }

    private void Reconcile(long nowMs)
    {
        if (pending is null) { Finish(DeliveryReason.OutcomeUncertain); return; }
        var sameCharacter = Observation.Context?.Character == pending.Context.Character && Observation.Context.Company == pending.Context.Company;
        if (sameCharacter && Observation.PendingSourceReadable && DeliveryRules.ExactRemoval(pending.Row.Source, Observation.PendingSource) &&
            Observation.Seals >= pending.BeforeSeals && Observation.Seals - pending.BeforeSeals == pending.Reward)
        {
            Status = Status with { ConfirmedCount = Status.ConfirmedCount + 1 };
            confirmedAt = nowMs;
            confirmedGeneration = pending.Generation;
            refreshedAfterReceipt = false;
            var deliveredCharacter = pending.Context.Character;
            var deliveredItem = pending.Row.Source.ItemId;
            pending = null;
            Delivered?.Invoke(deliveredCharacter, deliveredItem, DateTimeOffset.UtcNow);
            if (stopping) { Finish(DeliveryReason.Cancelled); return; }
            if (!SameSession(Observation.Context)) { Finish(DeliveryReason.WrongContext); return; }
            deadline = nowMs + 5000;
            Status = Status with { Phase = DeliveryPhase.Cooldown, Message = "Delivery confirmed; waiting for the item list" };
            return;
        }
        if (nowMs < deadline) return;
        uncertainSources.Add((pending.Context.Character, pending.Row.Source.Container, pending.Row.Source.Slot, pending.Row.Source.ItemId, pending.Row.Source.Fingerprint));
        Finish(DeliveryReason.OutcomeUncertain);
    }

    private bool SameSession(DeliveryContext? observed) => context != null && observed != null &&
        observed.Character == context.Character && observed.Territory == context.Territory &&
        observed.Company == context.Company && observed.Rank == context.Rank;

    private void Finish(DeliveryReason reason)
    {
        Status = Status with { Phase = reason == DeliveryReason.OutcomeUncertain ? DeliveryPhase.OutcomeUncertain : DeliveryPhase.Stopped, Reason = reason, Message = DeliveryRules.Message(reason) };
        if (pending?.Submitted != true) pending = null;
    }

    private void CancelOwnedReward(DeliveryReason reason)
    {
        if (pending is not null && Observation.Dialog is { Correlated: true, Kind: DeliveryDialogKind.Reward })
        {
            var owned = pending with { DialogIdentity = Observation.Dialog.Identity };
            try { adapter.Dispatch(new(DeliveryCommandKind.CancelReward, owned, Observation.MapGeneration)); }
            catch { /* Cancellation failure does not authorize a hand-in. */ }
        }
        Finish(reason);
    }
}
