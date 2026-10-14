using System.Collections.Immutable;

namespace Exportaramus.Features.Diagnostics;

public sealed record DiagnosticIoResult(string Status, long ByteCount, double ElapsedMilliseconds, string? FailureCode);
