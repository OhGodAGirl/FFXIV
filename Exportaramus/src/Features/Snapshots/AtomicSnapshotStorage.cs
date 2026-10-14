using Exportaramus.Features.Diagnostics;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Exportaramus.Features.Snapshots;

public sealed class AtomicSnapshotStorage
{
    private readonly string root;
    private readonly DiagnosticMeasurements measurements;
    private DiagnosticStorage diagnostic = new();
    public DiagnosticStorage Diagnostic => Volatile.Read(ref diagnostic);
    private readonly IAtomicFileOperations files;
    private bool recoveryChecked;
    private static readonly Regex HistoryName = new("^(automatic|manual)-snapshot-[0-9]{8}-[0-9]{6}-[0-9]{3}\\.json$", RegexOptions.CultureInvariant);
    public AtomicSnapshotStorage(string root, IAtomicFileOperations? files = null, DiagnosticMeasurements? measurements = null)
    { this.root = Path.GetFullPath(root); this.files = files ?? new AtomicFileOperations(); this.measurements = measurements ?? new(); }
    public string CanonicalPath => Path.Combine(root, "current-snapshot.json");
    public string RichV12Path => Path.Combine(root, "current-snapshot-v1.2.json");
    public string CharacterPath(ulong id) => id == 0 ? throw new ArgumentOutOfRangeException(nameof(id)) : Path.Combine(root, "snapshots", "characters", id.ToString(CultureInfo.InvariantCulture), "collection-data.json");
    public string EvidencePath(ulong id) => Path.Combine(Path.GetDirectoryName(CharacterPath(id))!, "reported-evidence.json");
    public string RecoveryState { get; private set; } = "None";
    private string RecoveryPath => Path.Combine(root, "state", "write-recovery.json");

    public async Task<string?> ReadAsync(ulong contentId, bool evidence, CancellationToken cancellationToken)
    {
        using var timing = new DiagnosticTiming(measurements, "FileRead", true);
        try
        {
            var text = await ReadCoreAsync(contentId, evidence, cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref diagnostic, Diagnostic with { LastRead = new(text is null ? "NotFound" : "Succeeded", text is null ? 0 : Encoding.UTF8.GetByteCount(text), timing.ElapsedMilliseconds, null), RecoveryState = RecoveryState });
            return text;
        }
        catch (Exception exception)
        {
            measurements.Failure("Storage", "Read", "ReadFailed", exception);
            Volatile.Write(ref diagnostic, Diagnostic with { LastRead = new("Failed", 0, timing.ElapsedMilliseconds, "ReadFailed"), RecoveryState = RecoveryState });
            throw;
        }
    }
    public async Task<StorageCommitResult> CommitAsync(ulong contentId, string compact, string? rich, string? reports,
        bool changed, bool manual, DateTimeOffset capturedAt, CancellationToken cancellationToken, string? richV12 = null)
    {
        using var timing = new DiagnosticTiming(measurements, "FileWrite", true);
        try
        {
            var result = await CommitCoreAsync(contentId, compact, rich, reports, changed, manual, capturedAt, cancellationToken, richV12).ConfigureAwait(false);
            var prior = Diagnostic;
            Volatile.Write(ref diagnostic, prior with
            {
                CanonicalAvailable = rich is not null || prior.CanonicalAvailable,
                CanonicalBytes = rich is null ? prior.CanonicalBytes : Encoding.UTF8.GetByteCount(rich),
                HistoryFileCount = result.HistoryCount,
                HistoryBytes = result.HistoryBytes,
                LastWrite = new("Succeeded", Encoding.UTF8.GetByteCount(compact) + Encoding.UTF8.GetByteCount(rich ?? "") + Encoding.UTF8.GetByteCount(richV12 ?? "") + Encoding.UTF8.GetByteCount(reports ?? ""), timing.ElapsedMilliseconds, null),
                RecoveryState = RecoveryState
            });
            return result;
        }
        catch (Exception exception)
        {
            measurements.Failure("Storage", "Write", "WriteFailed", exception);
            Volatile.Write(ref diagnostic, Diagnostic with { LastWrite = new("Failed", 0, timing.ElapsedMilliseconds, "WriteFailed"), RecoveryState = RecoveryState });
            throw;
        }
    }
    private async Task<string?> ReadCoreAsync(ulong contentId, bool evidence, CancellationToken cancellationToken)
    {
        RecoverPending();
        var path = evidence ? EvidencePath(contentId) : CharacterPath(contentId);
        if (!File.Exists(path)) return null;
        await using var stream = files.OpenRead(path);
        if (stream.Length > 4194304) throw new InvalidDataException("Stored snapshot exceeds its byte limit.");
        using var buffer = new MemoryStream();
        var bytes = new byte[8192]; int read;
        while ((read = await stream.ReadAsync(bytes, cancellationToken).ConfigureAwait(false)) != 0)
        {
            if (buffer.Length + read > 4194304) throw new InvalidDataException("Stored snapshot exceeds its byte limit.");
            buffer.Write(bytes, 0, read);
        }
        return new UTF8Encoding(false, true).GetString(buffer.ToArray());
    }

    private async Task<StorageCommitResult> CommitCoreAsync(ulong contentId, string compact, string? rich, string? reports,
        bool changed, bool manual, DateTimeOffset capturedAt, CancellationToken cancellationToken, string? richV12)
    {
        RecoverPending();
        var writes = new List<(string Path, string Json, int Limit)> { (CharacterPath(contentId), compact, 4194304) };
        if (reports is not null) writes.Add((EvidencePath(contentId), reports, 4194304));
        if (rich is not null) writes.Add((CanonicalPath, rich, 16777216));
        if (richV12 is not null) writes.Add((RichV12Path, richV12, 16777216));
        if (rich is not null && changed)
        {
            var name = $"{(manual ? "manual" : "automatic")}-snapshot-{capturedAt.UtcDateTime:yyyyMMdd-HHmmss-fff}.json";
            var path = Path.Combine(root, "snapshots", name);
            if (File.Exists(path)) throw new IOException("History timestamp collision.");
            writes.Add((path, rich, 16777216));
        }
        var prepared = new List<PreparedFile>();
        var previousHistory = History();
        var removedHistory = rich is not null && changed ? previousHistory.Take(Math.Max(0, previousHistory.Length - 24)).ToArray() : [];
        var retainedHistory = previousHistory.Except(removedHistory, StringComparer.Ordinal).ToArray();
        var historyBytes = retainedHistory.Sum(path => new FileInfo(path).Length) + (rich is not null && changed ? Encoding.UTF8.GetByteCount(rich) : 0);
        var historyCount = retainedHistory.Length + (rich is not null && changed ? 1 : 0);
        DateTimeOffset? latestHistory = rich is not null && changed ? capturedAt : retainedHistory.Length == 0 ? null : File.GetLastWriteTimeUtc(retainedHistory[^1]);
        bool journalWritten = false;
        try
        {
            foreach (var write in writes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bytes = new UTF8Encoding(false, true).GetBytes(write.Json);
                if (bytes.Length > write.Limit) throw new OutputSizeLimitException();
                using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 64 });
                Directory.CreateDirectory(Path.GetDirectoryName(write.Path)!);
                var temporary = write.Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                var backup = temporary + ".bak";
                prepared.Add(new(write.Path, temporary, backup, File.Exists(write.Path), false));
                WriteRecovery(prepared, false); journalWritten = true; RecoveryState = "Pending";
                FileOptions writeOptions = FileOptions.Asynchronous | FileOptions.WriteThrough;
                await using (var stream = files.OpenWrite(temporary, FileMode.CreateNew, 8192, writeOptions))
                {
                    await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false); stream.Flush(true);
                }
            }
            foreach (var path in removedHistory)
            {
                var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                prepared.Add(new(path, temporary, temporary + ".bak", true, true));
            }
            WriteRecovery(prepared, false); journalWritten = true; RecoveryState = "Pending";
            foreach (var file in prepared)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (file.Delete) files.Move(file.Destination, file.Backup);
                else if (file.Existed) files.Replace(file.Temporary, file.Destination, file.Backup);
                else files.Move(file.Temporary, file.Destination);
                file.Committed = true;
            }
            cancellationToken.ThrowIfCancellationRequested();
            WriteRecovery(prepared, true);
        }
        catch
        {
            try
            {
                Rollback(prepared.Where(file => file.Committed));
            }
            catch
            {
                RecoveryState = "Failed"; recoveryChecked = false;
                WriteRecovery(prepared, false, true);
                throw;
            }
            try
            {
                if (journalWritten) WriteRecovery(prepared, false, true);
                Cleanup(prepared, journalWritten);
            }
            catch { RecoveryState = "Pending"; recoveryChecked = false; throw; }
            throw;
        }
        try { Cleanup(prepared, true); }
        catch (IOException) { RecoveryState = "Pending"; recoveryChecked = false; }
        catch (UnauthorizedAccessException) { RecoveryState = "Pending"; recoveryChecked = false; }
        return new(historyCount, latestHistory, historyBytes);
    }

    private string[] History()
    {
        var folder = Path.Combine(root, "snapshots");
        return !Directory.Exists(folder) ? [] : Directory.GetFiles(folder, "*-snapshot-*.json", SearchOption.TopDirectoryOnly)
            .Where(path => HistoryName.IsMatch(Path.GetFileName(path))).OrderBy(File.GetLastWriteTimeUtc).ThenBy(path => path, StringComparer.Ordinal).ToArray();
    }
    private void RecoverPending()
    {
        try { RecoverPendingCore(); }
        catch { RecoveryState = "Failed"; throw; }
    }

    private void RecoverPendingCore()
    {
        if (recoveryChecked) return;
        if (!File.Exists(RecoveryPath)) { recoveryChecked = true; return; }
        RecoveryState = "Pending";
        if (new FileInfo(RecoveryPath).Length > 65536) { RecoveryState = "Failed"; throw new InvalidDataException("Recovery record exceeds its limit."); }
        using var document = JsonDocument.Parse(File.ReadAllBytes(RecoveryPath), new JsonDocumentOptions { MaxDepth = 8 });
        var value = document.RootElement;
        if (value.GetProperty("format").GetString() != "exportaramus-write-recovery" || value.GetProperty("version").GetInt32() != 1)
            throw new InvalidDataException("Recovery record has an unsupported format.");
        var rows = value.GetProperty("files");
        if (rows.GetArrayLength() is < 1 or > 64) throw new InvalidDataException("Recovery file count is invalid.");
        var files = new List<PreparedFile>(); var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows.EnumerateArray())
        {
            string relative = row.GetProperty("destination").GetString()!;
            string temporary = row.GetProperty("temporary").GetString()!;
            string backup = row.GetProperty("backup").GetString()!;
            if (!ValidDestination(relative) || !destinations.Add(relative) || !Regex.IsMatch(temporary,
                "^" + Regex.Escape(relative) + "\\.[0-9a-f]{32}\\.tmp$", RegexOptions.CultureInvariant) || backup != temporary + ".bak")
                throw new InvalidDataException("Recovery paths are invalid.");
            bool delete = row.GetProperty("delete").GetBoolean();
            bool existed = row.GetProperty("existed").GetBoolean();
            if (delete && (!existed || !relative.StartsWith("snapshots/", StringComparison.Ordinal) || relative.Contains("characters", StringComparison.Ordinal)))
                throw new InvalidDataException("Recovery deletion scope is invalid.");
            JsonElement applied = default;
            bool hasApplied = row.TryGetProperty("applied", out applied);
            files.Add(new(Path.Combine(root, relative), Path.Combine(root, temporary), Path.Combine(root, backup), existed, delete)
            { Committed = hasApplied && applied.GetBoolean() });
        }
        if (!value.GetProperty("committed").GetBoolean())
        {
            // Existence of the uniquely named backup (or consumed new-file temporary) records the atomic operation even if the process stopped before its next instruction.
            if (!value.TryGetProperty("rollbackFailed", out var failed) || !failed.GetBoolean())
                foreach (var file in files) file.Committed = File.Exists(file.Backup) || (!file.Existed && !File.Exists(file.Temporary) && File.Exists(file.Destination));
            if (files.Any(file => file.Committed))
            {
                try { Rollback(files.Where(file => file.Committed)); }
                catch
                {
                    WriteRecovery(files, false, true);
                    throw;
                }
                WriteRecovery(files, false, true);
            }
        }
        Cleanup(files, true); recoveryChecked = true; RecoveryState = "Recovered";
    }

    private static bool ValidDestination(string relative) => relative is "current-snapshot.json" or "current-snapshot-v1.2.json"
        || Regex.IsMatch(relative, "^snapshots/characters/[1-9][0-9]{0,19}/(collection-data|reported-evidence)\\.json$", RegexOptions.CultureInvariant)
        || (relative.StartsWith("snapshots/", StringComparison.Ordinal) && HistoryName.IsMatch(relative[10..]));

    private void WriteRecovery(List<PreparedFile> files, bool committed, bool rollbackFailed = false)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(RecoveryPath)!);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject(); writer.WriteString("format", "exportaramus-write-recovery"); writer.WriteNumber("version", 1); writer.WriteBoolean("committed", committed);
            writer.WriteBoolean("rollbackFailed", rollbackFailed);
            writer.WriteStartArray("files");
            foreach (var file in files)
            {
                writer.WriteStartObject(); writer.WriteString("destination", Path.GetRelativePath(root, file.Destination).Replace('\\', '/'));
                writer.WriteString("temporary", Path.GetRelativePath(root, file.Temporary).Replace('\\', '/'));
                writer.WriteString("backup", Path.GetRelativePath(root, file.Backup).Replace('\\', '/'));
                writer.WriteBoolean("existed", file.Existed); writer.WriteBoolean("delete", file.Delete); writer.WriteBoolean("applied", file.Committed); writer.WriteEndObject();
            }
            writer.WriteEndArray(); writer.WriteEndObject(); writer.Flush();
        }
        // Each path is generated from one fixed destination shape and a 32-digit token; 64 entries remain below the read-side 64 KiB limit.
        int fileCount = files.Count;
        if (fileCount > 64) throw new InvalidDataException("Recovery record exceeds its limit.");
        var temporary = RecoveryPath + ".tmp";
        using (var file = this.files.OpenWrite(temporary, FileMode.Create, 4096, FileOptions.WriteThrough))
        { file.Write(stream.ToArray()); file.Flush(true); }
        if (File.Exists(RecoveryPath)) this.files.Replace(temporary, RecoveryPath, null); else this.files.Move(temporary, RecoveryPath);
    }

    private void Rollback(IEnumerable<PreparedFile> prepared)
    {
        Exception? firstError = null;
        foreach (var file in prepared.Reverse())
        {
            try
            {
                if (!file.Existed) { if (File.Exists(file.Destination)) files.Delete(file.Destination); file.Committed = false; continue; }
                // An evidence file deliberately removed while unloaded must never be reconstructed from recovery state.
                if (!File.Exists(file.Destination) && file.Destination.EndsWith("reported-evidence.json", StringComparison.Ordinal)) { file.Committed = false; continue; }
                if (!File.Exists(file.Backup)) throw new IOException("Previous snapshot backup is unavailable.");
                // A pruned history destination can reappear only after its prior rollback restore completed.
                if (file.Delete && File.Exists(file.Destination)) { file.Committed = false; continue; }
                // Keep the original backup replayable until durable rollback progress precedes cleanup.
                var attributes = File.GetAttributes(file.Backup);
                if (File.Exists(file.Temporary)) PreparePrivateFile(file.Temporary);
                files.Copy(file.Backup, file.Temporary, true);
                PreparePrivateFile(file.Temporary);
                using (var restored = files.OpenWrite(file.Temporary, FileMode.Open, 8192, FileOptions.WriteThrough)) restored.Flush(true);
                File.SetAttributes(file.Temporary, attributes);
                if (File.Exists(file.Destination)) files.Replace(file.Temporary, file.Destination, null);
                else files.Move(file.Temporary, file.Destination);
                file.Committed = false;
            }
            catch (Exception exception) { firstError ??= exception; }
        }
        if (firstError is not null) ExceptionDispatchInfo.Capture(firstError).Throw();
    }

    private void Cleanup(IEnumerable<PreparedFile> prepared, bool journal)
    {
        foreach (var file in prepared)
        {
            if (File.Exists(file.Temporary)) { PreparePrivateFile(file.Temporary); files.Delete(file.Temporary); }
            if (File.Exists(file.Backup)) { PreparePrivateFile(file.Backup); files.Delete(file.Backup); }
        }
        if (journal && File.Exists(RecoveryPath)) files.Delete(RecoveryPath);
        RecoveryState = "None";
    }

    // Only exact owned temporary/backup paths reach this helper; destination attributes are preserved.
    private static void PreparePrivateFile(string path) => File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);

    private sealed class PreparedFile(string destination, string temporary, string backup, bool existed, bool delete)
    {
        public string Destination { get; } = destination;
        public string Temporary { get; } = temporary;
        public string Backup { get; } = backup;
        public bool Existed { get; } = existed;
        public bool Delete { get; } = delete;
        public bool Committed { get; set; }
    }
}
public sealed record StorageCommitResult(int HistoryCount, DateTimeOffset? LatestHistoryUtc, long HistoryBytes);
