using System.Text;
using Exportaramus.Shared.Ui;

namespace Exportaramus.Features.Diagnostics;

public static class DiagnosticSamplingPolicy
{
    public static DiagnosticUi? PrepareUi(DiagnosticUiObservation? observation)
    {
        if (!observation.HasValue) return null;
        var value = observation.Value;
        if (!InRange(value.X, -100000, 100000) || !InRange(value.Y, -100000, 100000) ||
            !InRange(value.Width, double.Epsilon, 100000) || !InRange(value.Height, double.Epsilon, 100000) ||
            !InRange(value.ContentWidth, 0, 3600000) || !InRange(value.Scale, double.Epsilon, 16) ||
            value.StyleVariables < 0 || value.StyleVariables > 64 || value.Colors < 0 || value.Colors > 128 ||
            value.Revision < 0 || value.Revision > 9007199254740991) return null;
        var layout = LayoutPolicy.At(value.ContentWidth);
        return new()
        {
            WindowPosition = new(value.X, value.Y),
            WindowSize = new(value.Width, value.Height),
            ContentWidth = value.ContentWidth,
            UiScale = value.Scale,
            WideActions = layout.Wide,
            CollectionColumnCount = layout.CollectionColumns,
            FontReady = value.FontReady,
            PushedStyleVarCount = value.StyleVariables,
            PushedColorCount = value.Colors,
            ReduceMotion = value.ReduceMotion,
            ViewStateRevision = value.Revision,
        };
    }

    private static bool InRange(double value, double minimum, double maximum) => double.IsFinite(value) && value >= minimum && value <= maximum;

    public static string ClientVersion(ReadOnlySpan<byte> buffer)
    {
        var end = buffer.IndexOf((byte)0);
        if (end != 20) return "Unavailable";
        for (var index = 0; index < end; index++)
        {
            if (index == 4 || index == 7 || index == 10 || index == 15)
            {
                if (buffer[index] != '.') return "Unavailable";
            }
            else if (buffer[index] is < (byte)'0' or > (byte)'9') return "Unavailable";
        }
        return Encoding.ASCII.GetString(buffer[..end]);
    }
}
