using System.Collections.Immutable;

namespace Exportaramus.Features.CollectionData.Capture;

public static class ReaderCompatibility
{
    public const string CurrentGameVersion = "2026.10.16.0000.0000";
    private static readonly ImmutableHashSet<string> Sources = ImmutableHashSet.Create(StringComparer.Ordinal,
        "mounts", "minions", "emotes", "bardings", "cards", "fashions", "frames", "occult_records",
        "orchestrions", "spells", "chocobo_routes", "leves", "hairstyles", "achievements",
        "aetherytes", "npcs", "quests", "survey_records", "inventory", "armoryChest",
        "chocoboSaddlebag", "premiumSaddlebag", "retainers", "armoire", "glamourDresser");

    // API and catalog checks allow a live read but do not prove it works in game.
    public static ImmutableHashSet<string> SourcesFor(string version) =>
        version is CurrentGameVersion or "2026.10.06.0000.0000" or "2026.09.01.0000.0000" ? Sources.Add("facewear").Add("field_records").Add("beasts") :
        version == "2026.08.11.0000.0000" ? Sources : ImmutableHashSet<string>.Empty;
}
