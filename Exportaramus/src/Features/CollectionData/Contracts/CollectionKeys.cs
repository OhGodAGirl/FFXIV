using System.Collections.Immutable;
namespace Exportaramus.Features.CollectionData.Contracts;

public static class CollectionKeys
{
    public static ImmutableArray<string> Unlocks { get; } = ["mounts", "minions", "emotes", "bardings", "cards", "npcs", "fashions", "frames", "occult_records", "orchestrions", "spells", "aetherytes", "chocobo_routes", "leves", "hairstyles", "field_records", "survey_records", "quests", "achievements", "facewear", "beasts"];
    public static ImmutableArray<string> Presence { get; } = ["inventory", "armoryChest", "chocoboSaddlebag", "premiumSaddlebag", "retainers", "armoire", "glamourDresser"];
    public static ImmutableArray<string> RichOnly { get; } = ["armoires", "glamour_dresser", "outfits", "relics"];
    public static ImmutableArray<string> Collect { get; } = ["armoires", "bardings", "cards", "fashions", "field_records", "frames", "hairstyles", "leves", "npcs", "occult_records", "orchestrions", "outfits", "relics", "spells", "survey_records"];
    public static ImmutableArray<string> Compact { get; } = [.. Unlocks, .. Presence, "reportedOwnership"];
    public static ImmutableArray<string> All { get; } = [.. Unlocks, .. RichOnly, .. Presence, "reportedOwnership"];
    public static bool SupportsQuantity(string key) => Presence.IndexOf(key) is >= 0 and < 5;
}
