using System.Collections.Immutable;

namespace Exportaramus.Features.Diagnostics;

public sealed record DiagnosticFrame(string Assembly, string Type, string Method, string? SourceFileName, int? Line);
