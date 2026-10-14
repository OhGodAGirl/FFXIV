using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Exportaramus.ConsumerKit.V1;
using Exportaramus.Features.CollectionData.Contracts;
using Exportaramus.Features.CollectionData.State;
using ReportedEvidence = Exportaramus.Features.CollectionData.State.ReportedEvidence;
using PresenceItem = Exportaramus.Features.CollectionData.State.PresenceItem;
namespace Exportaramus.Features.CollectionData.Publication;

public static class CollectionStateReader
{
    public static CollectionState Read(string compact, ulong contentId, Guid session, DateTimeOffset now)
    {
        using var parsed = JsonDocument.Parse(compact);
        var schemaVersion = parsed.RootElement.TryGetProperty("schemaVersion", out var version) ? version.GetString() : null;
        var extended = schemaVersion is "1.1" or "1.2";
        var identity = contentId.ToString(CultureInfo.InvariantCulture);
        if (schemaVersion == "1.2")
        {
            if (!Exportaramus.ConsumerKit.V1_2.CollectionDataSemanticValidator.ValidateSnapshot(compact, identity).Success)
                throw new InvalidDataException("Stored collection state is invalid.");
        }
        else if (extended)
        {
            if (!Exportaramus.ConsumerKit.V1_1.CollectionDataSemanticValidator.ValidateSnapshot(compact, identity).Success)
                throw new InvalidDataException("Stored collection state is invalid.");
        }
        else if (!CollectionDataSemanticValidator.ValidateSnapshot(compact, identity).Success)
            throw new InvalidDataException("Stored collection state is invalid.");
        var snapshot = parsed.RootElement.Deserialize(CollectionDataJsonContext.Default.CollectionDataSnapshot)!;
        var unknown = CollectionStateFactory.Empty(contentId, session, now).Unlocks["facewear"].Health;
        CollectionSourceStatus Health(string key)
        {
            if (!snapshot.SourceHealth.TryGetValue(key, out var value)) return unknown;
            var observed = value.ObservedAtUtc is { } at ? DateTimeOffset.Parse(at, CultureInfo.InvariantCulture) : (DateTimeOffset?)null;
            return new(Enum.Parse<DataType>(value.DataType), Enum.Parse<SourceStatus>(value.SourceStatus),
                Enum.Parse<Completeness>(value.Completeness), Enum.Parse<ProvenanceType>(value.ProvenanceType),
                Enum.Parse<CapabilityState>(value.CapabilityState), Enum.Parse<ReasonCode>(value.ReasonCode),
                value.Reason, observed, value.CaptureGeneration);
        }
        var unlocks = CollectionKeys.Unlocks.ToImmutableDictionary(key => key, key => new PermanentUnlockState(
            snapshot.Unlocks.TryGetValue(key, out var source) ? source.UnlockedIds : [], Health(key)));
        var presence = CollectionKeys.Presence.ToImmutableDictionary(key => key, key => new CurrentItemPresenceState(
            snapshot.CurrentItemPresence.TryGetValue(key, out var source) ? [.. source.Items.Select(value =>
                new PresenceItem(value.ItemId, value.Quantity))] : [], CollectionKeys.SupportsQuantity(key), Health(key))
        { DresserEntries = key == "glamourDresser" && extended ? ReadDresserEntries(parsed.RootElement) : null });
        var state = new CollectionState(contentId, session, snapshot.CaptureGeneration, CaptureType.Restored,
            now, unlocks, presence, new([], Health("reportedOwnership")), []);
        return CollectionStatePolicy.Restore(state, contentId, session, now);
    }

    private static ImmutableArray<Exportaramus.Features.CollectionData.State.DresserEntry>? ReadDresserEntries(JsonElement root)
    {
        if (!root.GetProperty("currentItemPresence").TryGetProperty("glamourDresser", out var source) ||
            !source.TryGetProperty("dresserEntries", out var entries)) return null;
        return [.. entries.EnumerateArray().Select(entry => new Exportaramus.Features.CollectionData.State.DresserEntry(
            entry.GetProperty("entryIndex").GetInt32(), entry.GetProperty("rawItemId").GetUInt32(),
            entry.GetProperty("baseItemId").GetUInt32(), entry.GetProperty("outfitId").ValueKind == JsonValueKind.Null ? null : entry.GetProperty("outfitId").GetUInt32(),
            [.. entry.GetProperty("components").EnumerateArray().Select(component => new Exportaramus.Features.CollectionData.State.DresserEntryComponent(
                component.GetProperty("slotIndex").GetInt32(), component.GetProperty("itemId").GetUInt32()))]))];
    }

    public static ImmutableArray<ReportedEvidence> ReadReports(string? json, ulong contentId)
    {
        if (json is null) return [];
        if (System.Text.Encoding.UTF8.GetByteCount(json) > 4194304) throw new InvalidDataException("Stored evidence exceeds its size limit.");
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
        var root = document.RootElement;
        RejectDuplicateProperties(root);
        if (root.GetProperty("contentId").GetString() != contentId.ToString(CultureInfo.InvariantCulture)) throw new InvalidDataException("Stored evidence has the wrong scope.");
        var items = root.GetProperty("items");
        if (items.GetArrayLength() > 10000) throw new InvalidDataException("Stored evidence exceeds its record limit.");
        var output = ImmutableArray.CreateBuilder<ReportedEvidence>(); var keys = new HashSet<(uint, string)>();
        foreach (var item in items.EnumerateArray())
        {
            RejectDuplicateProperties(item);
            var id = item.GetProperty("itemId").GetUInt32(); var reporter = item.GetProperty("reporter").GetString();
            if (!ReportOwnershipEvidencePolicy.TryNormalize(contentId.ToString(CultureInfo.InvariantCulture), id, reporter, out _, out var normalized) || normalized != reporter || !keys.Add((id, normalized)))
                throw new InvalidDataException("Stored evidence is invalid.");
            var timestamp = item.GetProperty("reportedAtUtc");
            if (!timestamp.GetString()!.EndsWith('Z')) throw new InvalidDataException("Stored evidence timestamp is invalid.");
            output.Add(new(id, normalized, timestamp.GetDateTimeOffset()));
        }
        return [.. output.OrderBy(value => value.ItemId).ThenBy(value => value.Reporter, StringComparer.Ordinal)];
    }
    private static void RejectDuplicateProperties(JsonElement value)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!names.Add(property.Name)) throw new InvalidDataException("Stored evidence contains duplicate properties.");
    }
}
