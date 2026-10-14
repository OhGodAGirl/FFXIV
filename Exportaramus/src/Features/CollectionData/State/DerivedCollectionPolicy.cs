using System.Collections.Immutable;
using Exportaramus.Features.CollectionData.Catalog;
using Exportaramus.Features.CollectionData.Contracts;
namespace Exportaramus.Features.CollectionData.State;

public static class DerivedCollectionPolicy
{
    public static CollectionResult Derive(CollectionDefinition definition, CollectionState state)
    {
        var results = ImmutableArray.CreateBuilder<CollectionItem>();
        var inputs = new List<CollectionSourceStatus>();
        foreach (var row in definition.Items)
        {
            var facts = ImmutableArray.CreateBuilder<OwnershipFact>();
            bool? owned = null;
            if (definition.Key == "outfits")
            {
                if (row.RequiredItemIds.IsDefaultOrEmpty) return Unavailable(definition, state, ReasonCode.MappingValidationFailed);
                var values = row.RequiredItemIds.Select(id => Presence(id, state, facts, inputs)).ToArray();
                owned = values.All(value => value == true) ? true : values.Any(value => value == false) ? false : null;
            }
            else
            {
                owned = Presence(row.Id, state, facts, inputs);
                var achievement = row.AchievementId.GetValueOrDefault();
                if (row.AchievementId.HasValue)
                {
                    var source = state.Unlocks["achievements"];
                    bool? completed = source.Health.IsUsable ? source.UnlockedIds.Contains(achievement) : null;
                    if (completed is not null)
                    {
                        facts.Add(new(DataType.PermanentUnlock, "achievements", achievement, completed.Value ? "Present" : "Absent", source.Health.SourceStatus));
                        inputs.Add(source.Health);
                    }
                    owned = owned == true || completed == true ? true : owned == false && completed == false ? false : null;
                }
            }
            if (owned is null) return Unavailable(definition, state, ReasonCode.IncompleteContainerSet);
            results.Add(new(row.Id, row.Name, owned.Value, new(definition.Key == "outfits" ? "AllRequiredConstituents" : "PermanentUnlockOrCurrentItemPresence", facts.ToImmutable())));
        }
        int inputCount = inputs.Count;
        if (inputCount == 0) return Unavailable(definition, state, ReasonCode.MappingValidationFailed);
        var stale = inputs.Any(input => input.SourceStatus == SourceStatus.Stale);
        var health = new CollectionSourceStatus(DataType.Derived, stale ? SourceStatus.Stale : SourceStatus.Fresh,
            Completeness.Complete, ProvenanceType.Derived,
            inputs.Any(input => input.CapabilityState == CapabilityState.Degraded) ? CapabilityState.Degraded : CapabilityState.Supported,
            ReasonCode.None, string.Empty, inputs.Min(input => input.ObservedAtUtc), state.CaptureGeneration);
        return new(definition.Label, DataType.Derived, true, health, results.ToImmutable());
    }

    private static bool? Presence(uint itemId, CollectionState state, ImmutableArray<OwnershipFact>.Builder facts, List<CollectionSourceStatus> inputs)
    {
        foreach (var key in CollectionKeys.Presence)
        {
            var source = state.CurrentItemPresence[key];
            if (source.Health.IsUsable && source.Items.Any(item => item.ItemId == itemId))
            {
                facts.Add(new(DataType.CurrentItemPresence, key, itemId, "Present", source.Health.SourceStatus)); inputs.Add(source.Health); return true;
            }
        }
        if (CollectionKeys.Presence.Any(key => !state.CurrentItemPresence[key].Health.IsUsable)) return null;
        foreach (var key in CollectionKeys.Presence)
        {
            var source = state.CurrentItemPresence[key];
            facts.Add(new(DataType.CurrentItemPresence, key, itemId, "Absent", source.Health.SourceStatus)); inputs.Add(source.Health);
        }
        return false;
    }

    private static CollectionResult Unavailable(CollectionDefinition definition, CollectionState state, ReasonCode reason) =>
        new(definition.Label, DataType.Derived, true,
            new(DataType.Derived, SourceStatus.Unavailable, Completeness.Unknown, ProvenanceType.None,
                CapabilityState.UnavailablePendingValidation, reason, "Required collection inputs are unavailable.", null, state.CaptureGeneration), []);
}
