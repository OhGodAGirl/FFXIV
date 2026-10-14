using System.Numerics;

namespace Exportaramus.Shared.Ui;

public sealed record ThemePalette
{
    public Vector4 Surface { get; init; } = new(.27f, .275f, .27f, 1);
    public Vector4 Window { get; init; } = new(.20f, .21f, .22f, .995f);
    public Vector4 Panel { get; init; } = new(.23f, .24f, .25f, 1);
    public Vector4 Raised { get; init; } = new(.25f, .26f, .27f, 1);
    public Vector4 Alternate { get; init; } = new(.26f, .27f, .28f, .820f);
    public Vector4 Gold { get; init; } = new(.780f, .639f, .353f, 1);
    public Vector4 Heading { get; init; } = new(.898f, .780f, .498f, 1);
    public Vector4 Icon { get; init; } = new(.941f, .788f, .420f, 1);
    public Vector4 Border { get; init; } = new(.459f, .380f, .243f, 1);
    public Vector4 Primary { get; init; } = new(.949f, .933f, .898f, 1);
    public Vector4 Secondary { get; init; } = new(.780f, .753f, .702f, 1);
    public Vector4 Muted { get; init; } = new(.74f, .72f, .69f, 1);
    public Vector4 Count { get; init; } = new(1, .890f, .310f, 1);
    public Vector4 Success { get; init; } = new(.514f, .894f, .478f, 1);
    public Vector4 Warning { get; init; } = new(.882f, .678f, .333f, 1);
    public Vector4 Error { get; init; } = new(.98f, .62f, .58f, 1);
    public Vector4 Shadow { get; init; } = new(.027f, .027f, .027f, .800f);
    public Vector4 FrameHovered { get; init; } = new(.200f, .204f, .216f, 1);
    public Vector4 FrameActive { get; init; } = new(.235f, .224f, .192f, 1);
    public Vector4 ButtonHovered { get; init; } = new(.210f, .204f, .180f, 1);
    public Vector4 ButtonActive { get; init; } = new(.250f, .224f, .170f, 1);
    public Vector4 TableBorder { get; init; } = new(.290f, .278f, .235f, .700f);
    public Vector4 Scrollbar { get; init; } = new(.060f, .063f, .070f, .900f);
    public Vector4 ScrollbarGrab { get; init; } = new(.310f, .290f, .235f, 1);
    public Vector4 Header { get; init; } = new(.260f, .224f, .155f, .900f);
    public Vector4 HeaderHovered { get; init; } = new(.340f, .282f, .175f, 1);
    public Vector4 HeaderActive { get; init; } = new(.410f, .325f, .185f, 1);
    public Vector4 Selection { get; init; } = new(.550f, .440f, .240f, .550f);
    public Vector4 PanelHighlight { get; init; } = new(.40f, .38f, .32f, .35f);
    public Vector4 ButtonHighlight { get; init; } = new(.65f, .60f, .48f, .4f);
}
