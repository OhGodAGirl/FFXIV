using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace CollectionShopper;

internal static class CollectionMappingCatalog
{
    internal static IReadOnlyDictionary<ShoppingCategory, HashSet<uint>> Read(IDataManager data)
        => new Dictionary<ShoppingCategory, HashSet<uint>>
        {
            [ShoppingCategory.Minion] = data.GetExcelSheet<Companion>().Select(row => row.RowId).ToHashSet(),
            [ShoppingCategory.Mount] = data.GetExcelSheet<Mount>().Select(row => row.RowId).ToHashSet(),
            [ShoppingCategory.Orchestrion] = data.GetExcelSheet<Orchestrion>().Select(row => row.RowId).ToHashSet(),
            [ShoppingCategory.Hairstyle] = data.GetExcelSheet<CharaMakeCustomize>().Select(row => (uint)row.UnlockLink).ToHashSet(),
            [ShoppingCategory.Fashion] = data.GetExcelSheet<Ornament>().Select(row => row.RowId).ToHashSet(),
            [ShoppingCategory.Facewear] = CollectionMappingPolicy.Facewear(data.GetExcelSheet<GlassesStyle>().Select(row => (row.RowId, !row.Name.IsEmpty))),
        };

}
