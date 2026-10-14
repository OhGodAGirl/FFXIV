using System.Text.Json.Serialization;

namespace Exportaramus.Features.Diagnostics;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DiagnosticReport))]
internal partial class DiagnosticJsonContext : JsonSerializerContext;
