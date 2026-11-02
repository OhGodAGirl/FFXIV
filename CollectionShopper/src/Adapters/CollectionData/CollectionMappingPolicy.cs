namespace CollectionShopper;

internal static class CollectionMappingPolicy
{
    // GlassesStyle uses zero-based rows; the public collection catalog uses row + one.
    internal static HashSet<uint> Facewear(IEnumerable<(uint RowId, bool HasName)> rows)
        => rows.Where(row => row.HasName).Select(row => checked(row.RowId + 1)).ToHashSet();
}
