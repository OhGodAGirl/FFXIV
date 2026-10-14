using System.Collections.Immutable;
using Exportaramus.Features.CollectionData.Catalog;
using Exportaramus.Features.CollectionData.Contracts;
namespace Exportaramus.Features.CollectionData.State;

public static class CollectionDataAssembler
{
    public static CollectionData Assemble(CollectionCatalog catalog, CollectionState state)
    {
        var collections = ImmutableDictionary.CreateBuilder<string, CollectionResult>(StringComparer.Ordinal);
        var health = ImmutableDictionary.CreateBuilder<string, CollectionSourceStatus>(StringComparer.Ordinal);
        foreach (var key in CollectionKeys.Unlocks)
        {
            var source = state.Unlocks[key]; health[key] = source.Health;
            if (!source.Health.IsUsable) continue;
            var definition = catalog.Collections[key]; var ids = source.UnlockedIds.ToImmutableHashSet();
            var quests = state.Quests.ToDictionary(q => q.Id);
            var items = definition.Items.Select(item =>
            {
                var owned = ids.Contains(item.Id);
                QuestState? quest = key == "quests" ? quests.GetValueOrDefault(item.Id) : null;
                return new CollectionItem(item.Id, item.Name, owned,
                    new(key == "quests" ? "QuestCompletion" : "DirectPermanentUnlock",
                        [new(DataType.PermanentUnlock, key, item.Id, owned ? "Present" : "Absent", source.Health.SourceStatus)]),
                    key == "quests" ? (owned ? QuestStatus.Completed : quest?.Status ?? QuestStatus.NotStarted) : null, quest?.Sequence);
            }).ToImmutableArray();
            collections[key] = new(definition.Label, definition.OwnershipKind, definition.TotalKnown, source.Health, items);
        }
        foreach (var pair in state.CurrentItemPresence) health[pair.Key] = pair.Value.Health;
        health["reportedOwnership"] = state.ReportedEvidence.Health;
        AddPresenceCollection("armoires", "armoire", catalog, state, collections, health);
        AddPresenceCollection("glamour_dresser", "glamourDresser", catalog, state, collections, health);
        foreach (var key in new[] { "outfits", "relics" })
        {
            var result = DerivedCollectionPolicy.Derive(catalog.Collections[key], state);
            health[key] = result.Health;
            if (result.Health.IsUsable) collections[key] = result;
        }
        return new(catalog, state, collections.ToImmutable(), health.ToImmutable());
    }

    private static void AddPresenceCollection(string key, string sourceKey, CollectionCatalog catalog, CollectionState state,
        ImmutableDictionary<string, CollectionResult>.Builder collections, ImmutableDictionary<string, CollectionSourceStatus>.Builder health)
    {
        var source = state.CurrentItemPresence[sourceKey]; health[key] = source.Health;
        if (!source.Health.IsUsable) return;
        var definition = catalog.Collections[key]; var ids = source.Items.Select(item => item.ItemId).ToImmutableHashSet();
        var rows = definition.Items;
        if (key == "glamour_dresser") rows = [.. rows.Where(item => ids.Contains(item.ItemId ?? item.Id))];
        var items = rows.Select(item => new CollectionItem(item.Id, item.Name, ids.Contains(item.ItemId ?? item.Id),
            new("DirectCurrentItemPresence", [new(DataType.CurrentItemPresence, sourceKey, item.ItemId ?? item.Id,
                ids.Contains(item.ItemId ?? item.Id) ? "Present" : "Absent", source.Health.SourceStatus)]))).ToImmutableArray();
        collections[key] = new(definition.Label, definition.OwnershipKind, definition.TotalKnown, source.Health, items);
    }
}
