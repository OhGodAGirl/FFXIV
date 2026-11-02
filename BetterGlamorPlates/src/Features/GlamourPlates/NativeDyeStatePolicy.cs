using BetterGlamorPlates.Features.Diagnostics;

namespace BetterGlamorPlates.Features.GlamourPlates;

/// <summary>Checks existing dye allocations and replaces only native dye capability bits.</summary>
internal static class NativeDyeStatePolicy
{
    internal static PlateDyePlan? RejectPending(Func<int, bool> hasPending, DiagnosticJournal diagnostics)
    {
        for (var index = 0; index < PlateRules.SlotCount; index++)
            if (hasPending(index))
            {
                diagnostics.Check(DiagnosticKey.PendingDye, 0, 1, index);
                return new([], [new(PlateApplyFailureKind.PendingDyes, (PlateSlot)index)]);
            }
        return null;
    }

    internal static uint ChannelFlags(uint flags, byte channels, uint firstMask, uint secondMask)
    {
        flags &= ~(firstMask | secondMask);
        if (channels > 0) flags |= firstMask;
        if (channels > 1) flags |= secondMask;
        return flags;
    }
}
