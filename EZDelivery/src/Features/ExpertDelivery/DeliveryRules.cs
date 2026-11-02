namespace EZDelivery.Features.ExpertDelivery;

/// <summary>Pure validation and player status rules for delivery operations.</summary>
public static class DeliveryRules
{
    /// <summary>Checks whether visible or pending state still belongs to the same delivery agent.</summary>
    public static bool CanObserveListContext(bool ready, bool visible, long addon, long pendingAddon, bool pendingGenerationMatches)
        => ready && addon != 0 && (visible || pendingAddon == addon && pendingGenerationMatches);

    // The callback map is keyed by the visible addon ID and supplies its actual owner.
    // The agent's cached warning handle is not a second authoritative addon identity.
    /// <summary>Recognizes an owned HQ or spiritbond warning from correlated native facts.</summary>
    public static DeliveryDialogKind WarningKind(bool generationMatches, bool selectedRowMatches,
        bool hqCallback, bool materiaCallback, bool hqPrompt, bool materiaPrompt, bool pendingHq)
    {
        if (!generationMatches || !selectedRowMatches) return DeliveryDialogKind.Unrelated;
        if (hqCallback && hqPrompt && pendingHq) return DeliveryDialogKind.HqWarning;
        if (materiaCallback && materiaPrompt) return DeliveryDialogKind.SpiritbondWarning;
        return DeliveryDialogKind.Unrelated;
    }

    /// <summary>Checks the complete reward against remaining seal capacity without integer wrap.</summary>
    public static bool RewardFits(uint balance, uint cap, uint? reward) => reward is > 0 && balance <= cap && reward.Value <= cap - balance;

    /// <summary>Compares inventory location and item fingerprint for exact instance identity.</summary>
    public static bool SameInstance(DeliverySource before, DeliverySource? after) => after is not null && before == after;

    /// <summary>Requires an empty source slot or an exact one-item quantity decrement.</summary>
    public static bool ExactRemoval(DeliverySource before, DeliverySource? after) =>
        before.Quantity == 1 ? after is null || after.ItemId == 0 && after.Quantity == 0 :
        after is not null && before with { Quantity = before.Quantity - 1 } == after;

    /// <summary>Rejects duplicate source slots and invalid visible or native row identities.</summary>
    public static bool ValidMapping(IReadOnlyList<DeliveryRow> rows)
    {
        if (rows.Count > 2048) return false;
        var indices = new HashSet<int>();
        var sources = new HashSet<(int, int)>();
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.VisibleIndex != i || row.AgentIndex < 11 || !indices.Add(row.AgentIndex) ||
                !sources.Add((row.Source.Container, row.Source.Slot)) || row.Source.ItemId == 0 || row.Source.Quantity < 1) return false;
        }
        return true;
    }

    /// <summary>Maps a delivery stop reason to player-facing text.</summary>
    public static string Message(DeliveryReason reason) => reason switch
    {
        DeliveryReason.Empty => "No eligible items remain in the selected native list.",
        DeliveryReason.SealCapacity => "Stopped: not enough seal capacity for the next item.",
        DeliveryReason.RewardUnknown => "Stopped: the next seal reward could not be verified.",
        DeliveryReason.MappingMismatch => "Stopped: the native item list could not be matched to inventory.",
        DeliveryReason.FilterChanged => "Stopped: the native filter or sorting changed.",
        DeliveryReason.UnrelatedDialog => "Stopped: an unrelated confirmation is open.",
        DeliveryReason.OutcomeUncertain => "Stopped: the last delivery could not be confirmed. It will not be retried.",
        DeliveryReason.Cancelled => "Expert Deliveries stopped.",
        DeliveryReason.UnsupportedBuild => "Expert Delivery is unavailable for this unreviewed game build.",
        DeliveryReason.NoCharacter => "No character is available.",
        DeliveryReason.WrongContext => "Stopped: the Expert Delivery window changed or closed.",
        DeliveryReason.DispatchRejected => "Stopped: the game did not accept the delivery action.",
        _ => "The Expert Delivery window is not ready.",
    };
}
