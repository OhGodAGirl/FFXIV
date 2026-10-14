using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Exportaramus.Features.Diagnostics;

public sealed record DiagnosticDisplaySection(string Heading, bool InitiallyOpen, ImmutableArray<DiagnosticDisplayRow> Rows)
{
    public string TableId { get; } = "DiagnosticSection" + Heading;
}
