using System.Globalization;
using System.Text.Json;

namespace Exportaramus.Features.Inventory;

/// <summary>Worker-only, character-scoped cache; unrelated collection output is never touched.</summary>
public sealed class InventoryFileCache(string configurationDirectory, Func<string, Stream>? openRead = null, Action<string, string>? commitFile = null) : IInventoryCache
{
    public const int MaximumBytes = 4 * 1024 * 1024;
    public string CharacterPath(ulong contentId)
    {
        ArgumentOutOfRangeException.ThrowIfZero(contentId);
        return Path.Combine(configurationDirectory, "snapshots", "characters", contentId.ToString(CultureInfo.InvariantCulture), "inventory.json");
    }

    public async Task<InventorySnapshot?> LoadAsync(ulong contentId, CancellationToken cancellationToken)
    {
        var path = CharacterPath(contentId);
        try
        {
            InventorySnapshot parsed;
            await using (var stream = openRead is null
                ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous)
                : openRead(path))
            {
                if (stream.Length > MaximumBytes) throw new InvalidDataException("InventoryCacheSizeLimit");
                var bytes = new byte[checked((int)stream.Length)];
                await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
                // Reject growth after the size check rather than reading an unbounded stream.
                if (stream.ReadByte() != -1) throw new InvalidDataException("InventoryCacheChangedDuringRead");
                parsed = Parse(bytes, contentId);
            }
            return parsed;
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    public async Task SaveAsync(InventorySnapshot snapshot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(snapshot, snapshot.ContentId);
        InventorySnapshot? prior;
        try { prior = await LoadAsync(snapshot.ContentId, cancellationToken).ConfigureAwait(false); }
        catch (InvalidDataException) { prior = null; }
        // Captures may precede startup restoration. Preserve unseen locations in the same cache only.
        if (prior is not null) snapshot = InventoryPolicy.Restore(snapshot, prior);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new InventoryCacheDocument("1.0", snapshot), InventoryCacheJsonContext.Default.InventoryCacheDocument);
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("InventoryCacheSizeLimit");
        _ = Parse(bytes, snapshot.ContentId);
        var path = CharacterPath(snapshot.ContentId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            var written = await File.ReadAllBytesAsync(temporary, cancellationToken).ConfigureAwait(false);
            _ = Parse(written, snapshot.ContentId);
            cancellationToken.ThrowIfCancellationRequested();
            if (commitFile is not null) commitFile(temporary, path);
            else if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static InventorySnapshot Parse(byte[] bytes, ulong contentId)
    {
        InventoryCacheDocument? document = null;
        try { document = JsonSerializer.Deserialize(bytes, InventoryCacheJsonContext.Default.InventoryCacheDocument); }
        catch (JsonException) { throw new InvalidDataException("InventoryCacheInvalid"); }
        if (document?.SchemaVersion != "1.0" || document.Snapshot is null) throw new InvalidDataException("InventoryCacheInvalid");
        Validate(document.Snapshot, contentId);
        return document.Snapshot;
    }

    private static void Validate(InventorySnapshot value, ulong contentId)
    {
        if (contentId == 0 || value.ContentId != contentId || value.Revision < 0 || value.Locations.IsDefault
            || value.Locations.Length > 7 + InventoryPolicy.MaximumRetainers) throw new InvalidDataException("InventoryCacheInvalid");
        var keys = new HashSet<InventoryLocationKey>();
        var retainerCount = 0;
        foreach (var location in value.Locations)
        {
            if (location is null || !location.Key.IsValid || !keys.Add(location.Key) || !InventoryPolicy.ValidName(location.Name))
                throw new InvalidDataException("InventoryCacheInvalid");
            if (!Enum.IsDefined(location.State) || location.Items.IsDefault
                || !InventoryPolicy.TryNormalize(location.Items, out var normalized, location.SupportsQuantity)
                || !location.Items.SequenceEqual(normalized))
                throw new InvalidDataException("InventoryCacheInvalid");
            if (location.HasObservation && location.ObservedAtUtc!.Value == default(DateTimeOffset))
                throw new InvalidDataException("InventoryCacheInvalid");
            if (location.HasObservation && !location.AttemptedAtUtc.HasValue)
                throw new InvalidDataException("InventoryCacheInvalid");
            if (location.State is InventoryObservationState.Observed or InventoryObservationState.Retained && !location.HasObservation)
                throw new InvalidDataException("InventoryCacheInvalid");
            if (!location.HasObservation && location.Items.Length != 0)
                throw new InvalidDataException("InventoryCacheInvalid");
            if (location.State == InventoryObservationState.Unknown && location.HasObservation)
                throw new InvalidDataException("InventoryCacheInvalid");
            if (location.AttemptedAtUtc < location.ObservedAtUtc)
                throw new InvalidDataException("InventoryCacheInvalid");
            if (location.Key.Kind == InventoryLocationKind.Retainer && ++retainerCount > InventoryPolicy.MaximumRetainers)
                throw new InvalidDataException("InventoryCacheInvalid");
        }
    }
}
