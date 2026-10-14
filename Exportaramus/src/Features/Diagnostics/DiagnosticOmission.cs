using System.Collections.Immutable;

namespace Exportaramus.Features.Diagnostics;

public sealed record DiagnosticOmission(string SourceKey, string ReasonCode);
