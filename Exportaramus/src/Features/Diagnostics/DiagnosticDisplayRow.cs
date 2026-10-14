using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Exportaramus.Features.Diagnostics;

public sealed record DiagnosticDisplayRow(string Label, string Value);

