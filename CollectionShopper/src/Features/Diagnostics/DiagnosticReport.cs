using System.Text.RegularExpressions;

namespace CollectionShopper;

internal static partial class DiagnosticReport
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    internal static string Build(DiagnosticSnapshot snapshot)
    {
        string Safe(string? text)
        {
            text ??= string.Empty;
            return Redact(text[..Math.Min(text.Length, 384)]);
        }
        object[] Predicates(IReadOnlyList<DiagnosticPredicate> checks) => checks.Take(48)
            .Select(check => (object)new
            {
                name = Safe(check.Name),
                passed = check.Passed,
                expected = Safe(check.Expected),
                observed = Safe(check.Observed)
            }).ToArray();
        object[]? Sources(IReadOnlyList<CollectionDataOwnershipSourceState>? sources) => sources?.Take(32)
            .Select(source => (object)new
            {
                source = Safe(source.Source),
                status = Safe(source.Status),
                ownershipUsable = source.AssertsOwnership,
                observedAtUtc = Safe(source.ObservedAtUtc),
                reason = Safe(source.Reason)
            }).ToArray();
        var operation = snapshot.Operation;
        var failure = operation?.LastFailure;
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            capturedAtUtc = snapshot.CheckedAt.ToUniversalTime(),
            plugin = Safe(snapshot.PublicName),
            internalName = Safe(snapshot.InternalName),
            version = Safe(snapshot.Version),
            loadedBuildMvid = snapshot.LoadedBuildId,
            api = snapshot.DalamudApiLevel,
            channel = Safe(snapshot.Channel),
            frameworkObservedAtUtc = snapshot.FrameworkObservedAt,
            collectionCompleteness = Predicates(snapshot.CollectionCompleteness ?? Array.Empty<DiagnosticPredicate>()),
            current = operation is null ? null : new
            {
                phase = Safe(operation.Phase),
                status = Safe(operation.Status),
                confirmedActions = operation.ConfirmedActions,
                checks = Predicates(operation.Checks)
            },
            lastFailure = failure is null ? null : new
            {
                capturedAtUtc = failure.AtUtc.ToUniversalTime(),
                phase = Safe(failure.Phase),
                reason = Safe(failure.Reason),
                confirmedActions = failure.ConfirmedActions,
                checks = Predicates(failure.Checks),
                sources = Sources(failure.Sources)
            },
            sources = Sources(snapshot.Sources),
            checks = snapshot.Checks.Take(32).Select(check => new
            {
                name = Safe(check.Name),
                outcome = Format(check.Outcome),
                detail = Safe(check.Detail),
                recovery = Safe(check.Recovery)
            }).ToArray(),
        }, JsonOptions);
    }

    internal static string Redact(string text)
    {
        var redacted = PointerRegex().Replace(text, "[pointer removed]");
        redacted = SecretAssignmentRegex().Replace(redacted, "$1: [redacted]");
        redacted = IdentityAssignmentRegex().Replace(redacted, "$1: [redacted]");
        redacted = FfxivCollectCharacterUrlRegex().Replace(redacted, "$1[redacted]");
        redacted = WindowsPathRegex().Replace(redacted, "[local path removed]");
        return LongIdentifierRegex().Replace(redacted, "[identifier removed]");
    }

    private static string Format(DiagnosticResultKind result)
        => result == DiagnosticResultKind.NotAvailable ? "Not Available" : result.ToString();

    [GeneratedRegex(@"(?im)\b(token|cookie|authorization|password|api key|secret)\s*[:=]\s*[^\r\n]+")]
    private static partial Regex SecretAssignmentRegex();

    [GeneratedRegex(@"(?im)\b(character id|account id|content id|character name|player name|retainer name)\s*[:=]\s*[^\r\n]+")]
    private static partial Regex IdentityAssignmentRegex();

    [GeneratedRegex(@"(?i)(https?://(?:www\.)?ffxivcollect\.com/characters/)[0-9]+")]
    private static partial Regex FfxivCollectCharacterUrlRegex();

    [GeneratedRegex(@"(?i)(?:\b[A-Z]:[\\/]|\\\\)[^\r\n]+")]
    private static partial Regex WindowsPathRegex();

    [GeneratedRegex(@"(?i)\b0x[0-9a-f]+\b")]
    private static partial Regex PointerRegex();

    [GeneratedRegex(@"\b[0-9]{7,20}\b")]
    private static partial Regex LongIdentifierRegex();

}

