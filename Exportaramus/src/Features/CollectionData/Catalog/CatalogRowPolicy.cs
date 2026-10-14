using System.Collections.Immutable;
using Exportaramus.Features.CollectionData.State;

namespace Exportaramus.Features.CollectionData.Catalog;

public sealed record CatalogRowFact(string Key, uint RowId, string? Name, uint LinkId = 0,
    uint ItemId = 0, int Order = 0, uint Icon = 0, bool IsAetheryte = false, bool Invisible = false,
    bool LinkedRowExists = false);

public static class CatalogRowPolicy
{
    public static (string Key, CatalogItem Item) Project(CatalogRowFact row)
    {
        var named = !string.IsNullOrEmpty(row.Name);
        var eligible = row.Key switch
        {
            "@items" => row.RowId != 0,
            "mounts" => row.RowId != 0 && row.Order >= 0 && row.Icon != 0 && named,
            "minions" => row.RowId != 0 && row.Icon != 0 && named,
            "emotes" => row.RowId != 0 && row.LinkId != 0 && named,
            "npcs" => row.RowId >= 2293760 && row.RowId < 2359296 && row.LinkedRowExists,
            "frames" => row.LinkId > 1,
            "spells" => row.RowId != 0 && row.LinkId != 0,
            "aetherytes" => row.RowId != 0 && row.IsAetheryte && !row.Invisible,
            "hairstyles" => row.LinkId != 0,
            "field_records" => row.LinkId != 0 && named,
            "armoires" => row.Order != 0 && row.ItemId != 0,
            _ => row.RowId != 0 && named,
        };
        if (!eligible) return ("", new(0, 0, "", false));
        bool linkedId = row.Key == "frames" || row.Key == "hairstyles" || row.Key == "field_records";
        var id = linkedId ? row.LinkId : row.Key == "armoires" ? checked(row.RowId + 1) : row.RowId;
        var name = row.Key == "quests" ? null : row.Name;
        if (row.Key == "hairstyles" && row.ItemId == 0) name = $"Hairstyle {id}";
        var fallback = string.IsNullOrWhiteSpace(name) || (row.Key == "hairstyles" && row.ItemId == 0);
        return (row.Key, new(id, row.Key == "frames" ? id : row.RowId,
            string.IsNullOrWhiteSpace(name) ? $"Entry {id}" : name, fallback,
            row.Key == "armoires" ? row.ItemId : null));
    }

    public static CatalogItem WithItemName(CatalogItem item, string name) => item with { Name = name, IsFallbackName = false };

    public static readonly ImmutableArray<CollectionDefinition> Definitions =
    [
        new("mounts", "Mounts", DataType.PermanentUnlock, true, []), new("minions", "Minions", DataType.PermanentUnlock, true, []),
        new("emotes", "Emotes", DataType.PermanentUnlock, true, []), new("bardings", "Bardings", DataType.PermanentUnlock, true, []),
        new("cards", "Triple Triad Cards", DataType.PermanentUnlock, true, []), new("npcs", "Triple Triad NPCs", DataType.PermanentUnlock, true, []),
        new("facewear", "Facewear", DataType.PermanentUnlock, true, []),
        new("beasts", "Beasts", DataType.PermanentUnlock, true, []),
        new("fashions", "Fashion Accessories", DataType.PermanentUnlock, true, []), new("frames", "Portrait Frames", DataType.PermanentUnlock, true, []),
        new("occult_records", "Occult Records", DataType.PermanentUnlock, true, []), new("orchestrions", "Orchestrion Rolls", DataType.PermanentUnlock, true, []),
        new("spells", "Blue Magic Spells", DataType.PermanentUnlock, true, []), new("aetherytes", "Aetherytes", DataType.PermanentUnlock, true, []),
        new("chocobo_routes", "Chocobo Routes", DataType.PermanentUnlock, true, []), new("leves", "Levequests", DataType.PermanentUnlock, true, []),
        new("hairstyles", "Hairstyles", DataType.PermanentUnlock, true, []), new("field_records", "Field Records", DataType.PermanentUnlock, true, []),
        new("survey_records", "Variant Dungeon Records", DataType.PermanentUnlock, true, []), new("quests", "Quests", DataType.PermanentUnlock, true, []),
        new("achievements", "Achievements", DataType.PermanentUnlock, true, []), new("armoires", "Armoire", DataType.CurrentItemPresence, true, []),
        new("glamour_dresser", "Glamour Dresser", DataType.CurrentItemPresence, false, []), new("outfits", "Outfits", DataType.Derived, true, []),
        new("relics", "Relics", DataType.Derived, true, []),
    ];
}
