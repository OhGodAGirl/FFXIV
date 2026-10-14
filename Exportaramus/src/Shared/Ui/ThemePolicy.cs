using System.Numerics;

namespace Exportaramus.Shared.Ui;

public static class ThemePolicy
{
    // Shared approved bases: Obsidian/FFXIV Addons/Shared/Game Theme Palettes.md.
    private static readonly ThemePalette Dark = Create(0, new(.27f, .275f, .27f, 1));
    private static readonly ThemePalette Light = Create(1, new(.94f, .81f, .65f, 1));
    private static readonly ThemePalette Classic = Create(2, new(.22f, .10f, .76f, 1));
    private static readonly ThemePalette Blue = Create(3, new(.22f, .32f, .55f, 1));
    private static readonly ThemePalette White = Create(4, new(.78f, .78f, .80f, 1));
    private static readonly ThemePalette Green = Create(5, new(.27f, .46f, .16f, 1));
    private static readonly ThemePalette Grey = Create(6, new(.30f, .31f, .32f, 1));
    private static readonly ThemePalette Pink = Create(7, new(.93f, .63f, .85f, 1));

    private static ThemePalette Create(uint theme, Vector4 surface)
    {
        var light = theme is 1 or 4 or 7;
        Vector4 Map(Vector4 original)
        {
            if (theme == 0) return original;
            if (original.X > .6f && original.Y > .6f && original.Z > .5f)
                return light ? new(.20f, .16f, .20f, original.W) : original;
            if (original.X > original.Z * 1.3f && original.X > .4f)
                return light ? new(.41f, .28f, .045f, original.W) : new(.91f, .76f, .43f, original.W);
            var offset = ((original.X + original.Y + original.Z) / 3f - .27f) * (light ? -1 : 1);
            return new(Math.Clamp(surface.X + offset, 0, 1), Math.Clamp(surface.Y + offset, 0, 1), Math.Clamp(surface.Z + offset, 0, 1), original.W);
        }
        var window = Map(new(.18f, .182f, .185f, .99f));
        var panel = Map(new(.19f, .20f, .208f, 1));
        var palette = light ? LightPalette(window, panel) : DarkPalette(window, panel);
        return palette with
        {
            Surface = surface,
            Heading = light ? palette.Heading : new(.96f, .86f, .63f, 1),
            Warning = light ? palette.Warning : new(.96f, .83f, .55f, 1),
            Primary = Map(new(.90f, .89f, .85f, 1)),
            Secondary = light ? new(.20f, .16f, .20f, 1) : new(.86f, .84f, .80f, 1),
            Muted = light ? new(.20f, .16f, .20f, 1) : new(.82f, .81f, .78f, 1),
            Icon = light ? new(.41f, .28f, .045f, 1) : new(.91f, .76f, .43f, 1),
            Border = light ? new(.48f, .40f, .27f, 1) : new(.72f, .62f, .42f, 1),
            Raised = Map(new(.28f, .282f, .28f, 1)),
            Alternate = Map(new(.225f, .23f, .235f, .92f)),
            FrameHovered = Map(new(.37f, .37f, .365f, 1)),
            FrameActive = Map(new(.42f, .415f, .40f, 1)),
            ButtonHovered = Map(new(.38f, .365f, .32f, 1)),
            ButtonActive = Map(new(.47f, .42f, .32f, 1)),
            TableBorder = Map(new(.36f, .36f, .35f, .75f)),
            Scrollbar = Map(new(.13f, .135f, .14f, .9f)),
            ScrollbarGrab = Map(new(.38f, .375f, .35f, 1)),
            Header = Map(new(.35f, .31f, .235f, .9f)),
            HeaderHovered = Map(new(.46f, .39f, .27f, 1)),
            HeaderActive = Map(new(.54f, .44f, .28f, 1)),
            Selection = Map(new(.55f, .44f, .24f, .55f)),
        };
    }
    // ColorThemeType order follows the game's Theme Settings choices.
    public static ThemePalette Resolve(uint? theme) => theme switch
    {
        1 => Light,
        2 => Classic,
        3 => Blue,
        4 => White,
        5 => Green,
        6 => Grey,
        7 => Pink,
        _ => Dark,
    };

    private static ThemePalette DarkPalette(Vector4 window, Vector4 panel) => new ThemePalette() with
    {
        Window = window,
        Panel = panel,
        Raised = panel + new Vector4(.025f, .025f, .025f, 0),
        Alternate = panel + new Vector4(.03f, .03f, .03f, 0),
        FrameHovered = panel + new Vector4(.06f, .06f, .06f, 0),
        FrameActive = panel + new Vector4(.08f, .08f, .08f, 0),
        ButtonHovered = panel + new Vector4(.06f, .06f, .06f, 0),
        ButtonActive = panel + new Vector4(.08f, .08f, .08f, 0),
        Scrollbar = window,
        Error = new(.99f, .82f, .78f, 1),
    };

    private static ThemePalette LightPalette(Vector4 window, Vector4 panel) => new()
    {
        Window = window,
        Panel = panel,
        Raised = panel + new Vector4(.025f, .025f, .025f, 0),
        Alternate = panel - new Vector4(.035f, .035f, .035f, 0),
        Primary = new(.12f, .12f, .13f, 1),
        Secondary = new(.23f, .22f, .22f, 1),
        Muted = new(.31f, .29f, .29f, 1),
        Gold = new(.46f, .33f, .10f, 1),
        Heading = new(.34f, .23f, .035f, 1),
        Icon = new(.41f, .28f, .045f, 1),
        Border = new(.48f, .40f, .27f, 1),
        Count = new(.36f, .25f, .015f, 1),
        Success = new(.055f, .32f, .10f, 1),
        Warning = new(.43f, .23f, .025f, 1),
        Error = new(.57f, .075f, .055f, 1),
        FrameHovered = panel - new Vector4(.045f, .045f, .045f, 0),
        FrameActive = panel - new Vector4(.08f, .08f, .08f, 0),
        ButtonHovered = panel - new Vector4(.045f, .045f, .045f, 0),
        ButtonActive = panel - new Vector4(.08f, .08f, .08f, 0),
        TableBorder = new(.48f, .40f, .27f, .7f),
        Scrollbar = window,
        ScrollbarGrab = new(.48f, .43f, .34f, 1),
        Header = panel - new Vector4(.03f, .03f, .03f, 0),
        HeaderHovered = panel - new Vector4(.055f, .055f, .055f, 0),
        HeaderActive = panel - new Vector4(.08f, .08f, .08f, 0),
        Selection = new(.58f, .44f, .17f, .35f),
        PanelHighlight = new(1, 1, 1, .4f),
        ButtonHighlight = new(1, 1, 1, .6f),
    };
}
