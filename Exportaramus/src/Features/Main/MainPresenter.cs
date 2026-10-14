using System.Collections.Immutable;
using System.Globalization;

namespace Exportaramus.Features.Main;

public static class MainPresenter
{
    // Explicit frontend row-major order, independent of backend map or serializer ordering.
    public static readonly ImmutableArray<(string Key, string Label, string Icon)> Collections =
    [
        ("aetherytes", "Aetherytes", "Diamond"), ("armoires", "Armoire", "Storage"), ("bardings", "Bardings", "Shield"),
        ("cards", "Triple Triad Cards", "Cards"), ("chocobo_routes", "Chocobo Routes", "Route"), ("emotes", "Emotes", "Smile"),
        ("fashions", "Fashion Accessories", "Glasses"), ("field_records", "Field Records", "List"), ("frames", "Portrait Frames", "Frame"),
        ("glamour_dresser", "Glamour Dresser", "Storage"), ("hairstyles", "Hairstyles", "Head"), ("leves", "Levequests", "Scroll"),
        ("minions", "Minions", "Pet"), ("mounts", "Mounts", "Mount"), ("npcs", "Triple Triad NPCs", "Cards"),
        ("occult_records", "Occult Records", "Book"), ("orchestrions", "Orchestrion Rolls", "Music"), ("outfits", "Outfits", "Shirt"),
        ("quests", "Quests", "Quest"), ("relics", "Relics", "Relic"), ("spells", "Blue Magic Spells", "Book"),
        ("survey_records", "Variant Dungeon Records", "Dungeon"), ("achievements", "Achievements", "Trophy"),
    ];

    public static MainViewState Prepare(MainPresentationInput input, DateTimeOffset now)
    {
        var tone = input.Status switch
        {
            MainStatus.Current => UiTone.Success,
            MainStatus.Partial => UiTone.Warning,
            MainStatus.Error => UiTone.Error,
            MainStatus.Synchronizing => UiTone.Working,
            _ => UiTone.Muted,
        };
        var label = input.Status switch
        {
            MainStatus.NoCharacter => "Waiting",
            MainStatus.NoSnapshot => "No Snapshot",
            MainStatus.Synchronizing => "Synchronizing",
            MainStatus.Current => "Current",
            MainStatus.Partial => "Partial",
            _ => "Error",
        };
        var busy = input.Status == MainStatus.Synchronizing || input.Guided.Active;
        return new(input.Revision, input.Status, input.Guided.Active ? input.Guided.StepLabel : label,
            input.Guided.Active ? UiTone.Working : tone, RelativeTime(input.LastRefresh, now), input.ReduceMotion,
            input.HasCharacter && !busy, input.Guided.Active || input.HasCharacter && !busy,
            input.HasCharacter && input.PreparedExportAvailable, Math.Clamp(input.HistoryCount, 0, 25),
            RelativeTime(input.LatestSnapshot, now), Bound(input.HealthHeadline), input.HealthTone,
            input.HealthFacts.Take(3).Select(x => x with { Text = Bound(x.Text) }).ToImmutableArray(),
            input.HasSnapshot ? Collections.Select(x => Row(x, input.Collections.GetValueOrDefault(x.Key))).ToImmutableArray() : [],
            Bound(input.Message), input.Guided);
    }

    public static CollectionRowView Row((string Key, string Label, string Icon) definition, CollectionRowObservation? observation)
    {
        if (observation?.Count is not { } count)
            return new(definition.Key, definition.Label, definition.Icon, observation?.Status is { Length: > 0 } status ? status : "\u2014", UiTone.Muted, "", "", UiTone.Muted, observation?.Detail ?? "Not captured yet.");
        var delta = observation.Delta;
        return new(definition.Key, definition.Label, definition.Icon, count.ToString("N0", CultureInfo.InvariantCulture) + (observation.Partial ? "+" : ""),
            UiTone.Primary, observation.Stale ? "!" : "", delta.HasValue ? delta.Value.ToString("+0;-0;+0", CultureInfo.InvariantCulture) : "",
            delta < 0 ? observation.Kind == RowEvidenceKind.CurrentItemPresence ? UiTone.Warning : UiTone.Error : UiTone.Success, observation.Detail);
    }

    public static string RelativeTime(DateTimeOffset? timestamp, DateTimeOffset now)
    {
        if (!timestamp.HasValue) return "Never";
        var age = now - timestamp.Value;
        if (age < TimeSpan.FromMinutes(1)) return "Just now";
        if (age < TimeSpan.FromHours(1)) return $"{(int)age.TotalMinutes} minutes ago";
        if (age < TimeSpan.FromDays(1)) return $"{(int)age.TotalHours} hours ago";
        return $"{(int)age.TotalDays} days ago";
    }

    private static string Bound(string value) => value.Length <= 160 ? value : value[..160];
}
