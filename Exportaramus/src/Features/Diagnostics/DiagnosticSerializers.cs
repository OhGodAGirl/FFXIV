using System.Collections.Immutable;

namespace Exportaramus.Features.Diagnostics;

public sealed record DiagnosticSerializers(string Compact, string Expanded, string FfxivCollect);
