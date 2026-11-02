namespace BetterGlamorPlates.Features.GlamourPlates;

internal sealed record DyeSupply(byte DyeId, uint ItemId, uint Container, ushort InventorySlot, uint Quantity);
internal sealed record PlateDyeChange(PlateSlot Slot, byte Channel, byte DyeId, DyeSupply Supply);
internal sealed record PlateDyePlan(IReadOnlyList<PlateDyeChange> Changes, IReadOnlyList<PlateApplyFailure> Failures);

/// <summary>Allocates carried dyes across the entire outfit before any native slot is changed.</summary>
internal static class PlateDyePlanner
{
    internal static PlateDyePlan Create(PlateLoadPlan plate, IReadOnlyList<DyeSupply> supplies,
        Func<uint, byte> channelCount)
    {
        var changes = new List<PlateDyeChange>(24);
        var failures = new List<PlateApplyFailure>();
        var used = new Dictionary<(uint, ushort), uint>();
        foreach (var step in plate.Steps)
        {
            if (step.IsSkipped) continue;
            var desired = step.SavedItem;
            var source = step.Source;
            if (desired is null || source is null) continue;
            var channels = channelCount(desired.ItemId);
            if (channels > 2)
            {
                failures.Add(new(PlateApplyFailureKind.UnsupportedDye, step.Slot, desired.ItemId));
                continue;
            }
            for (byte channel = 0; channel < 2; channel++)
            {
                var wanted = channel == 0 ? desired.Stain0Id : desired.Stain1Id;
                var stored = channel == 0 ? source.Stain0Id : source.Stain1Id;
                if (channel >= channels && (wanted != 0 || stored != 0))
                {
                    failures.Add(new(PlateApplyFailureKind.UnsupportedDye, step.Slot, desired.ItemId) { DyeId = wanted, DyeChannel = channel });
                    continue;
                }
                if (wanted == stored) continue;
                var supply = supplies.FirstOrDefault(value => value.DyeId == wanted && value.ItemId != 0
                    && value.Quantity > used.GetValueOrDefault((value.Container, value.InventorySlot)));
                if (supply is null)
                {
                    failures.Add(new(PlateApplyFailureKind.DyeUnavailable, step.Slot, desired.ItemId) { DyeId = wanted, DyeChannel = channel });
                    continue;
                }
                var key = (supply.Container, supply.InventorySlot);
                used[key] = used.GetValueOrDefault(key) + 1;
                changes.Add(new(step.Slot, channel, wanted, supply));
            }
        }
        return failures.Count == 0 ? new(changes, []) : new([], failures);
    }

    internal static bool Matches(PlateDyeChange change, byte pendingDye, bool fromInventory,
        bool fromItemId, uint itemId, uint container, ushort inventorySlot)
        => pendingDye == change.DyeId && !fromItemId && fromInventory
            && itemId == change.Supply.ItemId && container == change.Supply.Container
            && inventorySlot == change.Supply.InventorySlot;
}
