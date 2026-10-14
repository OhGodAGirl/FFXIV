using System.Collections.Immutable;
using Exportaramus.Features.CollectionData.State;

namespace Exportaramus.Features.CollectionData.Capture;

public static class DresserCapturePolicy
{
    public static DresserEntry Entry(int index, uint raw, IReadOnlySet<uint> knownItems,
        ImmutableArray<uint> slots = default, ImmutableArray<bool> unlocked = default)
    {
        var id = DresserSetExpansionPolicy.NormalizeStoredId(raw);
        if (index is < 0 or >= 800 || raw == 0 || id == 0 || !knownItems.Contains(id))
            throw new SourceReadException(ReasonCode.MappingValidationFailed);
        if (slots.IsDefault && unlocked.IsDefault) return new(index, raw, id, null, [new(0, id)]);
        if (slots.IsDefault || unlocked.IsDefault || slots.Length != 11 || unlocked.Length != 11)
            throw new SourceReadException(ReasonCode.MappingValidationFailed);
        var components = ImmutableArray.CreateBuilder<DresserEntryComponent>();
        for (var slot = 0; slot < slots.Length; slot++)
        {
            if (!unlocked[slot] || slots[slot] == 0) continue;
            if (!knownItems.Contains(slots[slot]))
                throw new SourceReadException(ReasonCode.MappingValidationFailed);
            components.Add(new(slot, slots[slot]));
        }
        if (components.Count == 0) throw new SourceReadException(ReasonCode.MappingValidationFailed);
        return new(index, raw, id, id, components.ToImmutable());
    }

    public static CurrentItemPresenceState Complete(IEnumerable<DresserEntry> captured, long generation, DateTimeOffset now)
    {
        var entries = captured.OrderBy(e => e.EntryIndex).ToImmutableArray();
        if (entries.Length > 800 || entries.Select(e => e.EntryIndex).Distinct().Count() != entries.Length)
            throw new SourceReadException(ReasonCode.MappingValidationFailed);
        return CaptureReadPolicy.Presence(entries.SelectMany(e => e.Components).Select(c => new PresenceItem(c.ItemId, null)),
            false, true, generation, now) with
        { DresserEntries = entries };
    }
}

