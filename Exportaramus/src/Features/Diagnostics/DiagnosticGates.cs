using System.Collections.Immutable;

namespace Exportaramus.Features.Diagnostics;

public sealed record DiagnosticGates(bool GetCapabilities = false, bool GetSnapshot = false, bool SnapshotChanged = false,
    bool SnapshotWithdrawn = false, bool ReportOwnershipEvidence = false);
