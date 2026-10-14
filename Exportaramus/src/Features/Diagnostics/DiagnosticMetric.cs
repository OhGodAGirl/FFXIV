using System.Collections.Immutable;

namespace Exportaramus.Features.Diagnostics;

public sealed record DiagnosticMetric(int SampleCount, DiagnosticPercentiles DurationMilliseconds, DiagnosticPercentiles AllocatedBytes);
