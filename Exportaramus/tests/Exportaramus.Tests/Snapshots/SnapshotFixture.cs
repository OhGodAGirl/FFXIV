using System.Collections.Immutable;
using Exportaramus.Features.CollectionData;
using Exportaramus.Features.CollectionData.Catalog;
using Exportaramus.Features.CollectionData.Contracts;
using Exportaramus.Features.CollectionData.State;
namespace Exportaramus.Tests.Snapshots;

public static class SnapshotFixture
{
    public static readonly DateTimeOffset Now = new(2026, 9, 7, 0, 0, 0, TimeSpan.Zero);
    public static CollectionSourceStatus Health(DataType type, SourceStatus status = SourceStatus.Fresh) => new(type, status,
        status is SourceStatus.Fresh or SourceStatus.Stale ? Completeness.Complete : Completeness.Unknown,
        status is SourceStatus.Fresh or SourceStatus.Stale ? ProvenanceType.Observed : ProvenanceType.None,
        CapabilityState.Supported, ReasonCode.None, "", status is SourceStatus.Fresh or SourceStatus.Stale ? Now : null, 1);
    public static CollectionData Create()
    {
        var unlocks = CollectionKeys.Unlocks.ToImmutableDictionary(key => key, key => new PermanentUnlockState(key == "achievements" ? [129] : [1], Health(DataType.PermanentUnlock)));
        var presence = CollectionKeys.Presence.ToImmutableDictionary(key => key, key => new CurrentItemPresenceState([.. new uint[] { 100, 1675, 2642, 2965, 3747 }.Select(id => new PresenceItem(id, CollectionKeys.SupportsQuantity(key) ? 2u : null))], CollectionKeys.SupportsQuantity(key), Health(DataType.CurrentItemPresence)));
        var definitions = CollectionKeys.Unlocks.Concat(CollectionKeys.RichOnly).ToImmutableDictionary(key => key, key =>
            new CollectionDefinition(key, key, CollectionKeys.Unlocks.Contains(key) ? DataType.PermanentUnlock : key is "outfits" or "relics" ? DataType.Derived : DataType.CurrentItemPresence,
                key != "glamour_dresser", [new CatalogItem(key == "outfits" ? 45094u : key == "relics" ? 1675u : key == "achievements" ? 129u : key == "glamour_dresser" ? 100u : 1u, key == "armoires" ? 0u : 1u, key, false, key is "armoires" or "glamour_dresser" ? 100u : null, key == "outfits" ? [2642, 2965, 3747] : [], key == "relics" ? 129u : null)]));
        var catalog = new CollectionCatalog("English", "2026.08.11.0000.0000", definitions, [100, 1675, 2642, 2965, 3747]);
        var state = new CollectionState(123, Guid.Parse("f8b58df7-dc88-4ae8-b0e5-6dce6416e365"), 1, CaptureType.Full, Now, unlocks, presence,
            new([], Health(DataType.ReportedOwnership) with { ProvenanceType = ProvenanceType.Reported }), [new(1, QuestStatus.Completed, 255)]);
        return CollectionDataAssembler.Assemble(catalog, state);
    }
}
