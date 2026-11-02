using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CollectionShopper;

internal enum DiagnosticResultKind
{
    Pass,
    Warning,
    Failure,
    Waiting,
    NotAvailable,
}

