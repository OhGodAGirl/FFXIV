using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Exportaramus.Features.Diagnostics;

public sealed record PreparedDiagnosticReport(DiagnosticReport Model, string Json, int Utf8Bytes, ImmutableArray<DiagnosticDisplaySection> Sections)
{
    public string GeneratedText { get; } = $"Generated: {Model.GeneratedAtUtc:u}";
    public string SizeText { get; } = $"Report size: {Utf8Bytes:N0} bytes";
    public string FingerprintText { get; } = $"Report fingerprint: {Model.FingerprintSha256}";
}
