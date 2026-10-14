using System.Collections.Immutable;

namespace Exportaramus.Features.Diagnostics;

public sealed record DiagnosticCounters(long Accepted, long Rejected, long Throttled);
