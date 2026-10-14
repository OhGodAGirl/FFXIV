using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace Exportaramus.Features.Diagnostics;

public static partial class DiagnosticRedactionPolicy
{
    [GeneratedRegex(@"[\p{Cc}]|[A-Za-z]:[\\/]|\\\\|/(?:home|Users|usr|tmp)/|\b0x[0-9a-fA-F]+\b|[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}|\b(?:password|passwd|token|secret|credential|content.?id|character.?name|item.?id|retainer.?id)\s*[:=]|\b[0-9]{16,20}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProhibitedPattern();
    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_.]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex SymbolPattern();
    public static bool IsSafe(string value) => value.Length <= 512 && !ProhibitedPattern().IsMatch(value);
    public static bool IsSymbol(string value) => SymbolPattern().IsMatch(value) && IsSafe(value);
    public static DiagnosticFailure Failure(DateTime time, string component, string operation, string code, string? exceptionType)
    {
        return new(time, IsSymbol(component) ? component : "Unknown", IsSymbol(operation) ? operation : "Unknown",
            IsSymbol(code) ? code : "OperationFailed", exceptionType is not null && IsSymbol(exceptionType) ? exceptionType : null,
            "Operation failed; raw failure text was discarded.", ImmutableArray<DiagnosticFrame>.Empty);
    }
}
