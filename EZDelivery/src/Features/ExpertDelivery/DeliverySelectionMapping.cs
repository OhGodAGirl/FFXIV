namespace EZDelivery.Features.ExpertDelivery;

/// <summary>Native selection fields needed to apply the current in-game filter.</summary>
public readonly record struct DeliverySelectionRecord(byte Tab, bool Removed, uint Quantity, int Container, bool GearSet);

/// <summary>Maps filtered native selection order to eligible delivery records.</summary>
public static class DeliverySelectionMapping
{
    // Matches AgentGrandCompanySupply's native visible-index resolver, including
    // its exact Armoury predicate. See reference/delivery-native-verification.md.
    /// <summary>Resolves all visible rows or rejects inconsistent counts and mappings.</summary>
    public static bool TryResolve(IReadOnlyList<DeliverySelectionRecord> records, IReadOnlyList<int> order,
        int filter, int displayedCount, out IReadOnlyList<int> selected)
        => TryResolve(records, order, filter, displayedCount, out selected, out _);

    /// <summary>Resolves all visible rows or rejects inconsistent counts and mappings.</summary>
    public static bool TryResolve(IReadOnlyList<DeliverySelectionRecord> records, IReadOnlyList<int> order,
        int filter, int displayedCount, out IReadOnlyList<int> selected, out string check)
    {
        selected = Array.Empty<int>();
        check = $"Invalid bounds; records={records.Count}; order={order.Count}; filter={filter}; displayed={displayedCount}";
        if (records.Count is < 11 or > 2059 || order.Count != records.Count || filter is < 0 or > 2 || displayedCount is < 0 or > 2048)
            return false;
        var result = new List<int>();
        var seen = new HashSet<int>();
        foreach (var index in order)
        {
            if (index < 0 || index >= records.Count) { check = $"Index out of range; index={index}; records={records.Count}"; return false; }
            if (!seen.Add(index)) { check = $"Duplicate sorted index; index={index}; eligibleSoFar={result.Count}"; return false; }
            var row = records[index];
            if (row.Tab != 2 || row.Removed || row.Quantity == 0 ||
                filter == 1 && row.GearSet || filter == 2 && IsArmoury(row.Container)) continue;
            if (index < 11) { check = $"Expert record in prefix; index={index}"; return false; }
            result.Add(index);
        }
        // Do not advance while the presentation is still showing surrendered rows.
        if (result.Count != displayedCount) { check = $"Count mismatch; eligible={result.Count}; displayed={displayedCount}; records={records.Count}; filter={filter}"; return false; }
        selected = result;
        check = $"Matched; eligible={result.Count}; displayed={displayedCount}";
        return true;
    }

    private static bool IsArmoury(int container) => container is >= 3200 and <= 3209 or 3300 or 3400 or 3500;
}
