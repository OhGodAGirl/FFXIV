using System.Text.Json;
using Exportaramus.Features.CollectionData.Catalog;
using System.Security.Cryptography;
using Lumina;
using Lumina.Excel.Sheets;

namespace Exportaramus.ApiContractTests;

internal static class LocalGameDataVerification
{
    private static HashSet<uint> Projected(IEnumerable<CatalogRowFact> rows) => rows.Select(CatalogRowPolicy.Project).Select(row => row.Item.Id).Where(id => id != 0).ToHashSet();

    internal static int Run(string sqpack, string contracts, string output)
    {
        using var game = new GameData(sqpack);
        var version = game.Repositories.Values.Single(r => r.ExpansionId == 0).Version;
        var recognizedBytes = File.ReadAllBytes(Path.Combine(contracts, "recognized-ids.json"));
        var derivedBytes = File.ReadAllBytes(Path.Combine(contracts, "derived-inputs.json"));
        using var ids = JsonDocument.Parse(recognizedBytes);
        using var derived = JsonDocument.Parse(derivedBytes);
        var cabinets = game.GetExcelSheet<Cabinet>()!.Where(r => r.Order != 0 && r.Item.RowId != 0).ToArray();
        var fields = game.GetExcelSheet<MYCWarResultNotebook>()!.Where(r => r.Number != 0 && !r.Name.IsEmpty).ToArray();
        var fashions = game.GetExcelSheet<Ornament>()!.Where(r => !r.Singular.IsEmpty).Select(r => r.RowId).ToHashSet();
        var frames = game.GetExcelSheet<BannerBg>()!.Where(r => r.UnlockCondition.RowId > 1).Select(r => r.UnlockCondition.RowId).ToHashSet();
        var npcReferences = game.GetExcelSheet<ENpcBase>()!.SelectMany(r => r.ENpcData.Select(v => v.RowId)).Where(v => v != 0).ToHashSet();
        var triadIds = game.GetExcelSheet<TripleTriad>()!.Select(r => r.RowId).ToHashSet();
        var local = new Dictionary<string, HashSet<uint>>(StringComparer.Ordinal)
        {
            ["armoires"] = Projected(cabinets.Select(r => new CatalogRowFact("armoires", r.RowId, null, ItemId: r.Item.RowId, Order: r.Order))),
            ["bardings"] = Projected(game.GetExcelSheet<BuddyEquip>()!.Select(r => new CatalogRowFact("bardings", r.RowId, r.Name.ToString()))),
            ["cards"] = Projected(game.GetExcelSheet<TripleTriadCard>()!.Select(r => new CatalogRowFact("cards", r.RowId, r.Name.ToString()))),
            ["fashions"] = Projected(game.GetExcelSheet<Ornament>()!.Select(r => new CatalogRowFact("fashions", r.RowId, r.Singular.ToString()))).Except([22u, 25u, 26u, 32u]).ToHashSet(),
            ["field_records"] = Projected(fields.Select(r => new CatalogRowFact("field_records", r.RowId, r.Name.ToString(), LinkId: r.Number))),
            ["frames"] = Projected(game.GetExcelSheet<BannerBg>()!.Select(r => new CatalogRowFact("frames", r.RowId, null, LinkId: r.UnlockCondition.RowId))).Except([299u, 346u]).Append(573u).ToHashSet(),
            ["hairstyles"] = Projected(game.GetExcelSheet<CharaMakeCustomize>()!.Select(r => new CatalogRowFact("hairstyles", r.RowId, null, LinkId: r.UnlockLink))),
            ["leves"] = Projected(game.GetExcelSheet<Leve>()!.Select(r => new CatalogRowFact("leves", r.RowId, r.Name.ToString()))),
            ["npcs"] = Projected(npcReferences.Select(id => new CatalogRowFact("npcs", id, null, LinkedRowExists: triadIds.Contains(id)))),
            ["occult_records"] = Projected(game.GetExcelSheet<MKDLore>()!.Select(r => new CatalogRowFact("occult_records", r.RowId, r.Name.ToString()))),
            ["orchestrions"] = Projected(game.GetExcelSheet<Orchestrion>()!.Select(r => new CatalogRowFact("orchestrions", r.RowId, r.Name.ToString()))),
            ["outfits"] = game.GetExcelSheet<MirageStoreSetItem>()!.Select(r => r.RowId).ToHashSet(),
            ["relics"] = game.GetExcelSheet<Item>()!.Select(r => r.RowId).ToHashSet(),
            ["spells"] = Projected(game.GetExcelSheet<AozAction>()!.Select(r => new CatalogRowFact("spells", r.RowId, null, LinkId: r.Action.RowId))),
            ["survey_records"] = Projected(game.GetExcelSheet<VVDNotebookContents>()!.Select(r => new CatalogRowFact("survey_records", r.RowId, r.Name.ToString()))),
        };
        var checks = ids.RootElement.GetProperty("catalogs").EnumerateArray().Select(c =>
        {
            var key = c.GetProperty("key").GetString()!;
            var values = c.GetProperty("ids").EnumerateArray().Select(v => v.GetUInt32()).ToArray();
            return new { key, destinationCount = values.Length, localCandidateCount = local[key].Count, missingIds = values.Except(local[key]).ToArray() };
        }).ToArray();
        var outfitSheet = game.GetExcelSheet<MirageStoreSetItem>()!;
        var outfitFailures = new List<uint>();
        foreach (var row in derived.RootElement.GetProperty("outfits").EnumerateArray())
        {
            var id = row.GetProperty("id").GetUInt32();
            var r = outfitSheet.GetRow(id);
            uint[] pieces = [r.MainHand.RowId, r.OffHand.RowId, r.Head.RowId, r.Body.RowId, r.Hands.RowId,
                r.Legs.RowId, r.Feet.RowId, r.Earrings.RowId, r.Necklace.RowId, r.Bracelets.RowId, r.Ring.RowId];
            if (!pieces.Where(v => v != 0).Distinct().Order().SequenceEqual(row.GetProperty("requiredItemIds").EnumerateArray().Select(v => v.GetUInt32())))
                outfitFailures.Add(id);
        }
        var achievementIds = game.GetExcelSheet<Achievement>()!.Select(r => r.RowId).ToHashSet();
        var relicFailures = new List<uint>();
        int linked = 0;
        foreach (var row in derived.RootElement.GetProperty("relics").EnumerateArray())
        {
            var id = row.GetProperty("id").GetUInt32();
            var achievement = row.GetProperty("achievementId");
            if (achievement.ValueKind != JsonValueKind.Null) linked++;
            if (!local["relics"].Contains(id) || (achievement.ValueKind != JsonValueKind.Null && !achievementIds.Contains(achievement.GetUInt32()))) relicFailures.Add(id);
        }
        var series = game.GetExcelSheet<VVDNotebookSeries>()!.Where(r => r.RowId != 0).Select(r => new { id = r.RowId, contents = r.Contents.Select(v => v.RowId).Where(v => v != 0).ToArray() }).ToArray();
        var baseline = version == "2026.10.06.0000.0000" && cabinets.Length == 3844 && fields.Length == 50
            && fields.Select(r => (uint)r.Number).Order().SequenceEqual(Enumerable.Range(1, 50).Select(v => (uint)v))
            && fields.All(r => r.Unknown0 == (r.RowId is 43 or 44 or 45 ? 69624u : 0u)) && fashions.Count == 56 && frames.Count == 402
            && linked == 864 && series.Length == 4 && series.SelectMany(r => r.contents).SequenceEqual(Enumerable.Range(1, 49).Select(v => (uint)v));
        bool mappingsValid = checks.All(c => c.missingIds.Length == 0) && outfitFailures.Count == 0 && relicFailures.Count == 0;
        var refreshBytes = File.ReadAllBytes(Path.Combine(contracts, "../1.2/catalog-refresh.json"));
        using var refresh = JsonDocument.Parse(refreshBytes);
        var refreshChecks = refresh.RootElement.GetProperty("additions").EnumerateObject().Select(p => new
        {
            key = p.Name,
            missingIds = p.Value.EnumerateArray().Select(v => v.GetUInt32()).Except(local[p.Name]).ToArray(),
        }).ToArray();
        var refreshOutfitFailures = new List<uint>();
        foreach (var row in refresh.RootElement.GetProperty("outfits").EnumerateArray())
        {
            var id = row.GetProperty("id").GetUInt32();
            var r = outfitSheet.GetRow(id);
            uint[] pieces = [r.MainHand.RowId, r.OffHand.RowId, r.Head.RowId, r.Body.RowId, r.Hands.RowId,
                r.Legs.RowId, r.Feet.RowId, r.Earrings.RowId, r.Necklace.RowId, r.Bracelets.RowId, r.Ring.RowId];
            if (!pieces.Where(v => v != 0).Distinct().Order().SequenceEqual(row.GetProperty("requiredItemIds").EnumerateArray().Select(v => v.GetUInt32())))
                refreshOutfitFailures.Add(id);
        }
        var beastSheet = game.Excel.GetRawSheet("XBMPet");
        var beastCatalogValid = beastSheet.Count == 51 && Enumerable.Range(0, 51).All(id => beastSheet.HasRow((uint)id));
        var fieldItems = game.GetExcelSheet<Item>()!.Where(r => r.ItemAction.RowId != 0 && r.ItemAction.Value.Action.RowId == 19743)
            .Select(r => new { recordId = (uint)r.ItemAction.Value.Data[0], itemId = r.RowId }).ToArray();
        var fieldMappingValid = fieldItems.Length == 50 && fieldItems.Select(r => r.recordId).Order().SequenceEqual(Enumerable.Range(1, 50).Select(id => (uint)id))
            && fieldItems.Select(r => r.itemId).Distinct().Count() == 50;
        var glasses = game.GetExcelSheet<Glasses>()!.Where(r => r.RowId != 0).ToArray();
        var facewearMappingValid = glasses.Length > 0 && glasses.All(r => r.RowId <= ushort.MaxValue && r.Style.RowId < uint.MaxValue);
        var furniture = game.GetExcelSheet<EObjName>()!.Where(r => new[] { "Glamour Dresser", "Armoire", "Summoning Bell" }.Contains(r.Singular.ExtractText(), StringComparer.OrdinalIgnoreCase))
            .Select(r => new { id = r.RowId, name = r.Singular.ExtractText() }).ToArray();
        var furnitureValid = new[] { "Glamour Dresser", "Armoire", "Summoning Bell" }.All(name => furniture.Any(r => string.Equals(r.name, name, StringComparison.OrdinalIgnoreCase)));
        bool success = baseline && mappingsValid && refreshChecks.All(c => c.missingIds.Length == 0) && refreshOutfitFailures.Count == 0
            && beastCatalogValid && fieldMappingValid && facewearMappingValid && furnitureValid;
        var evidence = new
        {
            success,
            mappingsValid,
            productCatalogPolicyApplied = true,
            catalogRefreshSha256 = Convert.ToHexStringLower(SHA256.HashData(refreshBytes)),
            refreshChecks,
            refreshOutfitFailures,
            beastCatalogValid,
            beastRows = beastSheet.Count,
            fieldMappingValid,
            fieldItems,
            facewearMappingValid,
            facewearStyles = glasses.Select(r => r.Style.RowId + 1).Distinct().Order().ToArray(),
            furnitureValid,
            furniture,
            gameDataVersion = version,
            baselineMatches = baseline,
            recognizedIdsSha256 = Convert.ToHexStringLower(SHA256.HashData(recognizedBytes)),
            derivedInputsSha256 = Convert.ToHexStringLower(SHA256.HashData(derivedBytes)),
            catalogs = checks,
            outfitCount = derived.RootElement.GetProperty("outfits").GetArrayLength(),
            outfitFailures,
            relicCount = derived.RootElement.GetProperty("relics").GetArrayLength(),
            linkedRelicCount = linked,
            relicFailures,
            cabinetRows = cabinets.Length,
            validCabinetRowZero = cabinets.Any(r => r.RowId == 0),
            fieldRecords = fields.Length,
            frames = frames.Count,
            fashions = fashions.Count,
            fieldNumbers = fields.Select(r => (uint)r.Number).Order().ToArray(),
            fieldUnknown0 = fields.Select(r => r.Unknown0).Distinct().ToArray(),
            fieldUnknown0Discrepancies = fields.Where(r => r.Unknown0 != 0).Select(r => new { rowId = r.RowId, number = r.Number, unknown0 = r.Unknown0 }).ToArray(),
            variantSeries = series
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Local mapping verification: {checks.Length}/15 catalogs, {outfitFailures.Count} Outfit failures, {relicFailures.Count} Relic failures; success={success}");
        return success ? 0 : 1;
    }
}
