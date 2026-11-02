using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace EZDelivery.Features.Shopping;

/// <summary>Projects only owned immutable values; never retains native pointers or localized-name joins.</summary>
public sealed class ShopCatalog : IShopCatalog
{
    /// <summary>Validated offers loaded from the game data sheets.</summary>
    public IReadOnlyList<ShopOffer> Offers { get; private set; } = Array.Empty<ShopOffer>();
    /// <summary>Catalog readiness or preparation error description.</summary>
    public string Status { get; private set; } = "Catalog unavailable";
    /// <summary>Number of sheet rows rejected during catalog preparation.</summary>
    public int OmittedRows { get; private set; }

    /// <summary>Builds the supported Grand Company offer catalog from game sheets.</summary>
    public ShopCatalog(IDataManager data)
    {
        try
        {
            var categories = data.GetExcelSheet<GCScripShopCategory>().ToDictionary(x => x.RowId);
            var buddy = data.GetExcelSheet<BuddyEquip>();
            var offers = new List<ShopOffer>();
            foreach (var group in data.GetSubrowExcelSheet<GCScripShopItem>())
                foreach (var entry in group)
                {
                    if (!categories.TryGetValue(entry.RowId, out var category) ||
                        !CatalogRowPolicy.CanReadItem(category.GrandCompany.RowId, category.Tier, category.SubCategory, entry.Item.RowId, entry.CostGCSeals))
                    { OmittedRows++; continue; }
                    var item = entry.Item.Value;
                    var equip = item.EquipSlotCategory.Value;
                    var action = item.ItemAction.Value;
                    var bardingId = action.Action.RowId == 1013 ? action.Data[0] : 0u;
                    var classification = CatalogClassification.Classify(equip.MainHand == 1,
                        equip.OffHand == 1 || equip.Head == 1 || equip.Body == 1 || equip.Gloves == 1 || equip.Legs == 1 ||
                        equip.Feet == 1 || equip.Ears == 1 || equip.Neck == 1 || equip.Wrists == 1 || equip.FingerL == 1 || equip.FingerR == 1,
                        item.ItemUICategory.RowId, action.Action.RowId, bardingId, bardingId > 0 && buddy.HasRow(bardingId));
                    if (!CatalogRowPolicy.CanPublish(classification, entry.RequiredGrandCompanyRank.RowId, item.StackSize))
                    { OmittedRows++; continue; }
                    offers.Add(new($"{category.GrandCompany.RowId}:{entry.RowId}:{entry.SubrowId}:{item.RowId}:{entry.CostGCSeals}",
                        item.RowId, item.Icon, item.Name.ExtractText(), classification!.Value, (byte)category.GrandCompany.RowId,
                        (byte)entry.RequiredGrandCompanyRank.RowId, category.Tier - 1, category.SubCategory - 1,
                        entry.CostGCSeals, bardingId, entry.RowId, entry.SubrowId, entry.SortKey, item.StackSize, item.IsUnique));
                }
            Offers = CatalogRowPolicy.Sort(offers);
            Status = "Catalog ready";
        }
        catch (Exception)
        {
            Offers = Array.Empty<ShopOffer>();
            Status = "Game catalog could not be read.";
        }
    }
}
