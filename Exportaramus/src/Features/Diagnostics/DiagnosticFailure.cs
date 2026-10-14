using System.Collections.Immutable;

namespace Exportaramus.Features.Diagnostics;

public sealed record DiagnosticFailure(DateTime OccurredAtUtc, string Component, string Operation, string Code,
    string? ExceptionType, string Message, ImmutableArray<DiagnosticFrame> Frames);
