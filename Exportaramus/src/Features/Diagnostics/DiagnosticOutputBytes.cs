using System.Collections.Immutable;

namespace Exportaramus.Features.Diagnostics;

public sealed record DiagnosticOutputBytes(int Compact, int Compendium, int FfxivCollect);
