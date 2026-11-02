using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CollectionShopper;

internal sealed record DiagnosticCheck(
    string Name,
    DiagnosticResultKind Outcome,
    string Detail,
    string? Recovery = null);

