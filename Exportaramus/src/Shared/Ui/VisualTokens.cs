using System.Numerics;
using Exportaramus.Features.Main;

namespace Exportaramus.Shared.Ui;

public static class VisualTokens
{
    public static ThemePalette Current { get; private set; } = ThemePolicy.Resolve(null);
    public static void ApplyTheme(uint? theme) => Current = ThemePolicy.Resolve(theme);
    public static Vector4 Window => Current.Window;
    public static Vector4 Panel => Current.Panel;
    public static Vector4 Raised => Current.Raised;
    public static Vector4 Alternate => Current.Alternate;
    public static Vector4 Gold => Current.Gold;
    public static Vector4 Heading => Current.Heading;
    public static Vector4 Icon => Current.Icon;
    public static Vector4 Border => Current.Border;
    public static Vector4 Primary => Current.Primary;
    public static Vector4 Secondary => Current.Secondary;
    public static Vector4 Muted => Current.Muted;
    public static Vector4 Count => Current.Count;
    public static Vector4 Success => Current.Success;
    public static Vector4 Warning => Current.Warning;
    public static Vector4 Error => Current.Error;
    public static Vector4 Shadow => Current.Shadow;
    public static Vector4 Tone(UiTone tone) => tone switch
    {
        UiTone.Muted => Muted,
        UiTone.Success => Success,
        UiTone.Working => Heading,
        UiTone.Warning => Warning,
        UiTone.Error => Error,
        _ => Primary,
    };
}

