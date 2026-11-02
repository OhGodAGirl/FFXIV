using System.Numerics;

namespace EZDelivery.Features.Interface;

internal enum ThemeColorRole
{
    Text,
    TextDisabled,
    WindowBg,
    ChildBg,
    PopupBg,
    Border,
    BorderShadow,
    Separator,
    TitleBg,
    TitleBgActive,
    TitleBgCollapsed,
    FrameBg,
    FrameBgHovered,
    FrameBgActive,
    Button,
    ButtonHovered,
    ButtonActive,
    CheckMark,
    TableHeaderBg,
    TableBorderStrong,
    TableBorderLight,
    TableRowBg,
    TableRowBgAlt,
    ScrollbarBg,
    ScrollbarGrab,
    ScrollbarGrabHovered,
    ScrollbarGrabActive,
    Header,
    HeaderHovered,
    HeaderActive,
    TextSelectedBg
}

internal static class ThemeColors
{
    internal static Vector4 Resolve(uint theme, ThemeColorRole role)
    {
        var original = role switch
        {
            ThemeColorRole.Text => new Vector4(0.90f, 0.89f, 0.85f, 1f),
            ThemeColorRole.TextDisabled => new Vector4(0.62f, 0.61f, 0.57f, 1f),
            ThemeColorRole.WindowBg => new Vector4(0.18f, 0.182f, 0.185f, 0.99f),
            ThemeColorRole.ChildBg => new Vector4(0.19f, 0.20f, 0.208f, 1f),
            ThemeColorRole.PopupBg => new Vector4(0.19f, 0.20f, 0.208f, 1f),
            ThemeColorRole.Border => new Vector4(0.48f, 0.42f, 0.30f, 1f),
            ThemeColorRole.BorderShadow => new Vector4(0.035f, 0.035f, 0.035f, 0.9f),
            ThemeColorRole.Separator => new Vector4(0.48f, 0.42f, 0.30f, 1f),
            ThemeColorRole.TitleBg => new Vector4(0.23f, 0.235f, 0.23f, 1f),
            ThemeColorRole.TitleBgActive => new Vector4(0.27f, 0.278f, 0.27f, 1f),
            ThemeColorRole.TitleBgCollapsed => new Vector4(0.20f, 0.205f, 0.20f, 1f),
            ThemeColorRole.FrameBg => new Vector4(0.32f, 0.32f, 0.32f, 1f),
            ThemeColorRole.FrameBgHovered => new Vector4(0.37f, 0.37f, 0.365f, 1f),
            ThemeColorRole.FrameBgActive => new Vector4(0.42f, 0.415f, 0.40f, 1f),
            ThemeColorRole.Button => new Vector4(0.28f, 0.282f, 0.28f, 1f),
            ThemeColorRole.ButtonHovered => new Vector4(0.38f, 0.365f, 0.32f, 1f),
            ThemeColorRole.ButtonActive => new Vector4(0.47f, 0.42f, 0.32f, 1f),
            ThemeColorRole.CheckMark => new Vector4(0.82f, 0.70f, 0.43f, 1f),
            ThemeColorRole.TableHeaderBg => new Vector4(0.29f, 0.295f, 0.295f, 1f),
            ThemeColorRole.TableBorderStrong => new Vector4(0.48f, 0.42f, 0.30f, 1f),
            ThemeColorRole.TableBorderLight => new Vector4(0.36f, 0.36f, 0.35f, 0.75f),
            ThemeColorRole.TableRowBg => new Vector4(0.19f, 0.195f, 0.20f, 0.92f),
            ThemeColorRole.TableRowBgAlt => new Vector4(0.225f, 0.23f, 0.235f, 0.92f),
            ThemeColorRole.ScrollbarBg => new Vector4(0.13f, 0.135f, 0.14f, 0.9f),
            ThemeColorRole.ScrollbarGrab => new Vector4(0.38f, 0.375f, 0.35f, 1f),
            ThemeColorRole.ScrollbarGrabHovered => new Vector4(0.50f, 0.44f, 0.32f, 1f),
            ThemeColorRole.ScrollbarGrabActive => new Vector4(0.82f, 0.70f, 0.43f, 1f),
            ThemeColorRole.Header => new Vector4(0.35f, 0.31f, 0.235f, 0.9f),
            ThemeColorRole.HeaderHovered => new Vector4(0.46f, 0.39f, 0.27f, 1f),
            ThemeColorRole.HeaderActive => new Vector4(0.54f, 0.44f, 0.28f, 1f),
            ThemeColorRole.TextSelectedBg => new Vector4(0.55f, 0.44f, 0.24f, 0.55f),
            _ => throw new ArgumentOutOfRangeException(nameof(role)),
        };
        return ThemePalette.Transform(theme, original);
    }
}
