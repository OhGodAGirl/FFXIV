using System.Collections.Immutable;

namespace Exportaramus.Features.Diagnostics;

public sealed record DiagnosticPercentiles(double P50, double P95, double Maximum);
