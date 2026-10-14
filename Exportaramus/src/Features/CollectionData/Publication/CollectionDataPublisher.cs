using Exportaramus.Features.Diagnostics;
using System.Collections.Immutable;
using System.Globalization;
using Exportaramus.ConsumerKit.V1;
using Exportaramus.Features.CollectionData.State;
using Exportaramus.Features.Snapshots;
using Exportaramus.Shared.Serialization;
namespace Exportaramus.Features.CollectionData.Publication;

public sealed class CollectionDataPublisher(AtomicSnapshotStorage storage, FFXIVCollectContract? contract, DiagnosticMeasurements? measurements = null)
{
    private readonly DiagnosticMeasurements diagnostics = measurements ?? new();
    public async Task<AcceptedSnapshotBundle> PrepareAndPersistAsync(CollectionData data, string displayName,
        long revision, AcceptedSnapshotBundle? previous, bool manual, bool persistReports, CancellationToken cancellationToken, bool reportsAvailable = true)
        => await PersistAsync(Prepare(data, displayName, revision, previous, manual, persistReports, cancellationToken, reportsAvailable), cancellationToken).ConfigureAwait(false);

    public PreparedSnapshotPublication Prepare(CollectionData data, string displayName,
        long revision, AcceptedSnapshotBundle? previous, bool manual, bool persistReports, CancellationToken cancellationToken, bool reportsAvailable = true)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var compact = diagnostics.Measure("CompactSerializer", () => CollectionDataSerializer.Compact(data, revision));
        var compactResult = CollectionDataSemanticValidator.ValidateSnapshot(compact, data.State.ContentId.ToString(CultureInfo.InvariantCulture));
        if (compactResult.Code == CollectionDataValidationCode.PayloadTooLarge) throw new OutputSizeLimitException();
        if (!compactResult.Success) throw new InvalidDataException("Compact snapshot validation failed: " + compactResult.Code);
        var rich = data.State.CaptureType == CaptureType.Full ? diagnostics.Measure("RichSerializer", () => CollectionDataSerializer.Rich(data, displayName, data.State.CapturedAtUtc)) : null;
        if (rich is not null)
        {
            var richResult = CollectionDataSemanticValidator.ValidateRich(rich);
            if (richResult.Code == CollectionDataValidationCode.PayloadTooLarge) throw new OutputSizeLimitException();
            if (!richResult.Success) throw new InvalidDataException("Rich snapshot validation failed.");
        }
        var fingerprints = diagnostics.Measure("Fingerprints", () => CollectionFingerprint.Sources(data)); var fingerprint = CollectionFingerprint.Combined(fingerprints);
        var changed = previous?.Data.State.ContentId != data.State.ContentId || previous.Fingerprint != fingerprint;
        var export = diagnostics.Measure("FFXIVCollectSerializer", () => FFXIVCollectImportSerializer.Serialize(data, contract));
        var reports = persistReports ? JsonOutput.Text(writer =>
        {
            writer.WriteStartObject(); writer.WriteString("contentId", data.State.ContentId.ToString(CultureInfo.InvariantCulture));
            CollectionDataSerializer.Reports(writer, data.State.ReportedEvidence.Items); writer.WriteEndObject();
        }) : null;
        var prepared = diagnostics.Measure("SnapshotPreparation", () => SnapshotBundlePreparation.Prepare(new(data, revision, compact, rich, fingerprint, fingerprints, export, 0, null), previous, reportsAvailable));
        var v11 = Exportaramus.ConsumerKit.V1_1.CollectionDataSemanticValidator.ValidateSnapshot(prepared.CompactJsonV11, data.State.ContentId.ToString(CultureInfo.InvariantCulture));
        if (v11.Code == Exportaramus.ConsumerKit.V1_1.CollectionDataValidationCode.PayloadTooLarge) throw new OutputSizeLimitException();
        if (!v11.Success) throw new InvalidDataException("CollectionData 1.1 validation failed: " + v11.Code);
        var v12 = Exportaramus.ConsumerKit.V1_2.CollectionDataSemanticValidator.ValidateSnapshot(prepared.CompactJsonV12, data.State.ContentId.ToString(CultureInfo.InvariantCulture));
        if (v12.Code == Exportaramus.ConsumerKit.V1_2.CollectionDataValidationCode.PayloadTooLarge) throw new OutputSizeLimitException();
        if (!v12.Success) throw new InvalidDataException("CollectionData 1.2 validation failed: " + v12.Code);
        if (data.State.CaptureType == CaptureType.Full)
        {
            var richV12 = CollectionDataSerializer.RichV12(data, displayName, data.State.CapturedAtUtc);
            var richResult = Exportaramus.ConsumerKit.V1_2.CollectionDataSemanticValidator.ValidateRich(richV12);
            if (richResult.Code == Exportaramus.ConsumerKit.V1_2.CollectionDataValidationCode.PayloadTooLarge) throw new OutputSizeLimitException();
            if (!richResult.Success) throw new InvalidDataException("Rich snapshot 1.2 validation failed: " + richResult.Code);
            prepared = prepared with { RichJsonV12 = richV12 };
        }
        return new(prepared, reports, changed, manual);
    }

    public async Task<AcceptedSnapshotBundle> PersistAsync(PreparedSnapshotPublication publication, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var prepared = publication.Bundle;
        var state = prepared.Data.State;
        var result = prepared.RichJson is not null
            ? await new CompendiumSnapshotWriter(storage).WriteAsync(state.ContentId, prepared.CompactJsonV12, prepared.RichJson, publication.Reports, publication.Changed, publication.Manual, state.CapturedAtUtc, cancellationToken, prepared.RichJsonV12).ConfigureAwait(false)
            : await storage.CommitAsync(state.ContentId, prepared.CompactJsonV12, null, publication.Reports, publication.Changed, publication.Manual, state.CapturedAtUtc, cancellationToken).ConfigureAwait(false);
        return prepared with { HistoryCount = result.HistoryCount, LatestHistoryUtc = result.LatestHistoryUtc };
    }
}
