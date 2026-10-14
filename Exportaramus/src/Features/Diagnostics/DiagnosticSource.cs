using System.Collections.Immutable;

namespace Exportaramus.Features.Diagnostics;

public sealed record DiagnosticSource(string SourceKey, string CapabilityState, string SourceStatus, string Completeness,
    string ProvenanceType, double? ObservationAgeSeconds, double CaptureMilliseconds, string ReasonCode);
