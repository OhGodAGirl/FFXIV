using System.Collections.Immutable;

namespace Exportaramus.Features.Diagnostics;

public sealed record DiagnosticCatalog(string? KeyHash, string Status, double BuildMilliseconds);
