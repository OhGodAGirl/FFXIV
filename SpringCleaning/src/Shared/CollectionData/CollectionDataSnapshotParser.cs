using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Exportaramus.ConsumerKit.V1_2;
using SpringCleaning.Shared.Inventory;

namespace SpringCleaning.Shared.CollectionData;

// The provider validates its wire schema; this projection preserves the proven
// workflow model without creating a second ownership or Dresser-progress tracker.
internal static class CollectionDataSnapshotParser
{
    internal static CollectionDataParseResult Parse(string? json, ulong expectedContentId, DateTimeOffset nowUtc,
        CollectionDataSnapshot? previous = null, InventorySnapshot? inventory = null)
    {
        if (expectedContentId == 0)
            return CollectionDataParseResult.Reject(CollectionDataFailure.NoCharacter);
        var validated = CollectionDataSemanticValidator.ValidateSnapshot(json, expectedContentId.ToString(CultureInfo.InvariantCulture));
        if (!validated.Success)
            return CollectionDataParseResult.Reject(Failure(validated.Code));
        var value = validated.Value!;
        var captured = DateTimeOffset.Parse(value.CapturedAtUtc, CultureInfo.InvariantCulture);
        if (captured > nowUtc + CollectionDataWireContract.MaximumFutureClockSkew)
            return CollectionDataParseResult.Reject(CollectionDataFailure.InvalidTimestamp);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json!)));
        if (previous?.ProviderSessionId == value.ProviderSessionId && previous.ContentId == expectedContentId)
        {
            if ((ulong)value.Revision < previous.Revision)
                return CollectionDataParseResult.Reject(CollectionDataFailure.OutOfOrder);
            if ((ulong)value.Revision == previous.Revision && hash != previous.ContentHash)
                return CollectionDataParseResult.Reject(CollectionDataFailure.InvalidRevision);
            if (inventory is not null && (inventory.Revision < previous.InventoryRevision
                || inventory.Revision == previous.InventoryRevision && inventory.Hash != previous.InventoryHash))
                return CollectionDataParseResult.Reject(CollectionDataFailure.OutOfOrder);
        }

        var result = new CollectionDataSnapshot(value.ProviderInternalName, value.ProviderVersion, expectedContentId,
            (ulong)value.Revision, captured, Source(value, "inventory", inventory), Source(value, "armoryChest", inventory),
            Source(value, "chocoboSaddlebag"), Source(value, "premiumSaddlebag"), Source(value, "retainers"),
            Source(value, "armoire"), Source(value, "glamourDresser"))
        {
            ProviderSessionId = value.ProviderSessionId,
            ContentHash = hash,
            InventoryRevision = inventory?.Revision ?? 0,
            InventoryHash = inventory?.Hash ?? string.Empty,
            InventoryObservation = inventory,
            DresserEntries = value.CurrentItemPresence.GetValueOrDefault("glamourDresser")?.DresserEntries?
                .Select(entry => new CollectionDresserEntry(entry.EntryIndex, entry.RawItemId, entry.BaseItemId, entry.OutfitId,
                    entry.Components.Select(piece => new CollectionDresserComponent(piece.SlotIndex, piece.ItemId)).ToImmutableArray())).ToImmutableArray(),
        };
        return CollectionDataParseResult.Accept(result);
    }

    private static CollectionSourceSnapshot Source(Exportaramus.ConsumerKit.V1_2.CollectionDataSnapshot snapshot,
        string name, InventorySnapshot? inventory = null)
    {
        var health = snapshot.SourceHealth[name];
        var observed = health.ObservedAtUtc is null ? (DateTimeOffset?)null : DateTimeOffset.Parse(health.ObservedAtUtc, CultureInfo.InvariantCulture);
        var status = health.Completeness != "Complete" || health.CapabilityState != "Supported"
            ? CollectionSourceStatus.Unavailable
            : health.SourceStatus switch
            {
                "Fresh" => CollectionSourceStatus.Fresh,
                "Stale" => CollectionSourceStatus.Stale,
                "NeverObserved" => CollectionSourceStatus.NeverObserved,
                _ => CollectionSourceStatus.Unavailable,
            };
        ImmutableArray<uint> ids = snapshot.CurrentItemPresence.GetValueOrDefault(name)?.Items.Select(item => item.ItemId).ToImmutableArray() ?? [];
        if (name is "inventory" or "armoryChest")
        {
            if (!InventoryPresenceAuthority.IsReady(inventory, snapshot.ContentId, name))
                return new(CollectionSourceStatus.Unavailable, observed, [], "Waiting for matching Exportaramus Inventory observations.");
            ids = InventoryPresenceAuthority.CurrentIds(inventory!, name, ids).ToImmutableArray();
        }

        return new(status, observed, ids, health.Reason);
    }

    private static CollectionDataFailure Failure(CollectionDataValidationCode failure) => failure switch
    {
        CollectionDataValidationCode.NoPayload => CollectionDataFailure.EmptyPayload,
        CollectionDataValidationCode.PayloadTooLarge => CollectionDataFailure.PayloadTooLarge,
        CollectionDataValidationCode.WrongCharacter => CollectionDataFailure.WrongCharacter,
        CollectionDataValidationCode.UnsupportedSchemaVersion => CollectionDataFailure.UnsupportedProtocol,
        _ => CollectionDataFailure.MalformedJson,
    };
}
