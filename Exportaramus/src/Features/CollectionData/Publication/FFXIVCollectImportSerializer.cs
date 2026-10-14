using System.Collections.Immutable;
using System.Text;
using Exportaramus.Features.CollectionData.Contracts;
using Exportaramus.Features.CollectionData.State;
using Exportaramus.Shared.Serialization;
namespace Exportaramus.Features.CollectionData.Publication;

public static class FFXIVCollectImportSerializer
{
    public static PreparedFFXIVCollectExport Serialize(CollectionData data, FFXIVCollectContract? contract)
    {
        if (contract is null) return PreparedFFXIVCollectExport.Unavailable("FFXIV Collect output is unavailable. The mapping contract is invalid.");
        if (!contract.SupportsGameVersion(data.Catalog.GameDataVersion)) return PreparedFFXIVCollectExport.Unavailable("FFXIV Collect output is unavailable for this game-data version.");
        var included = ImmutableArray.CreateBuilder<string>(); var omitted = ImmutableArray.CreateBuilder<string>();
        var json = JsonOutput.Text(writer =>
        {
            writer.WriteStartObject();
            foreach (var key in CollectionKeys.Collect)
            {
                CollectionResult? collection = null;
                ImmutableHashSet<uint>? recognized = null;
                if (!data.Collections.TryGetValue(key, out collection) || !Eligible(collection) ||
                    !contract.RecognizedIds.TryGetValue(key, out recognized) || !FFXIVCollectIdMapper.Covers(data.Catalog.Collections[key], recognized))
                { omitted.Add(key); continue; }
                var ids = FFXIVCollectIdMapper.Map(collection, recognized);
                included.Add(key); writer.WriteStartArray(key); foreach (var id in ids) writer.WriteNumberValue(id); writer.WriteEndArray();
            }
            if (contract.Beasts is { } beasts)
            {
                if (beasts.SupportsGameVersion(data.Catalog.GameDataVersion) && data.Collections.TryGetValue("beasts", out var collection) &&
                    Eligible(collection) && FFXIVCollectIdMapper.Covers(data.Catalog.Collections["beasts"], beasts.RecognizedIds))
                {
                    included.Add("beasts"); writer.WriteStartArray("beasts");
                    foreach (var id in FFXIVCollectIdMapper.Map(collection, beasts.RecognizedIds)) writer.WriteNumberValue(id);
                    writer.WriteEndArray();
                }
                else omitted.Add("beasts");
            }
            writer.WriteEndObject();
        });
        var includedCount = included.Count;
        if (includedCount == 0) return PreparedFFXIVCollectExport.Unavailable("No complete collections are available for FFXIV Collect. Sync after opening the required collection windows.");
        if (Encoding.UTF8.GetByteCount(json) > 1048576) return PreparedFFXIVCollectExport.Unavailable("FFXIV Collect output exceeds its size limit.");
        return new(json, included.ToImmutable(), omitted.ToImmutable(), null);
    }

    private static bool Eligible(CollectionResult collection) => collection.Health.IsUsable && collection.Items.All(item =>
        item.OwnershipEvidence.Facts.All(fact => fact.SourceStatus != SourceStatus.Stale || fact.SourceKey is "armoire" or "glamourDresser"));
}
