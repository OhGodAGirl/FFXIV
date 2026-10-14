using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace Exportaramus.Shared.Ui;

/// <summary>Product-owned vector line icons share one stroke weight and warm-gold treatment.</summary>
internal static class UiIcons
{
    internal static void Draw(string key, Vector2 origin, float size)
    {
        var draw = ImGui.GetWindowDrawList(); var color = Drawing.Color(VisualTokens.Icon);
        void Line(float x1, float y1, float x2, float y2) => draw.AddLine(origin + new Vector2(x1, y1) * size, origin + new Vector2(x2, y2) * size, color, 1.4f);
        void Box(float x, float y, float w, float h) => draw.AddRect(origin + new Vector2(x, y) * size, origin + new Vector2(x + w, y + h) * size, color, 1);
        void Circle(float x, float y, float radius) => draw.AddCircle(origin + new Vector2(x, y) * size, radius * size, color, 20, 1.4f);
        switch (key)
        {
            case "Close": Line(.2f, .2f, .8f, .8f); Line(.8f, .2f, .2f, .8f); break;
            case "Chevron": Line(.2f, .4f, .5f, .7f); Line(.5f, .7f, .8f, .4f); break;
            case "Clock": Circle(.5f, .5f, .43f); Line(.5f, .15f, .5f, .5f); Line(.5f, .5f, .7f, .65f); break;
            case "Sync": Circle(.5f, .5f, .35f); Line(.8f, .1f, .8f, .4f); Line(.8f, .4f, .55f, .4f); break;
            case "Capture": Box(.1f, .1f, .8f, .8f); Circle(.5f, .5f, .2f); break;
            case "Copy": Box(.1f, .1f, .6f, .7f); Box(.3f, .25f, .6f, .7f); break;
            case "Scroll": Box(.2f, .12f, .6f, .76f); Line(.3f, .32f, .7f, .32f); Line(.3f, .48f, .7f, .48f); Circle(.2f, .13f, .12f); Circle(.8f, .86f, .12f); break;
            case "Diamond": Line(.5f, .05f, .8f, .4f); Line(.8f, .4f, .5f, .95f); Line(.5f, .95f, .2f, .4f); Line(.2f, .4f, .5f, .05f); Line(.2f, .4f, .8f, .4f); break;
            case "Storage": Box(.1f, .15f, .8f, .65f); Circle(.5f, .48f, .2f); Line(.15f, .8f, .15f, 1); Line(.85f, .8f, .85f, 1); break;
            case "Shield": Line(.1f, .15f, .9f, .15f); Line(.1f, .15f, .2f, .65f); Line(.2f, .65f, .5f, .95f); Line(.5f, .95f, .8f, .65f); Line(.8f, .65f, .9f, .15f); break;
            case "Cards": Box(.12f, .15f, .55f, .65f); Box(.35f, .3f, .55f, .65f); break;
            case "Smile": Circle(.5f, .5f, .43f); Circle(.35f, .35f, .03f); Circle(.65f, .35f, .03f); Line(.3f, .65f, .5f, .75f); Line(.5f, .75f, .7f, .65f); break;
            case "Glasses": Box(.05f, .35f, .35f, .3f); Box(.6f, .35f, .35f, .3f); Line(.4f, .45f, .6f, .45f); break;
            case "Frame": Box(.1f, .1f, .8f, .8f); Circle(.1f, .1f, .05f); Circle(.9f, .9f, .05f); break;
            case "Music": Circle(.3f, .8f, .14f); Line(.44f, .8f, .44f, .1f); Line(.44f, .1f, .9f, .3f); Line(.44f, .3f, .9f, .5f); break;
            case "Quest": Circle(.5f, .5f, .43f); Line(.5f, .2f, .5f, .6f); Circle(.5f, .77f, .025f); break;
            case "Relic": Circle(.5f, .5f, .35f); Circle(.5f, .5f, .18f); Line(.5f, 0, .5f, .15f); Line(0, .5f, .15f, .5f); Line(.85f, .5f, 1, .5f); Line(.5f, .85f, .5f, 1); break;
            case "Shirt": Line(.3f, .1f, .05f, .3f); Line(.05f, .3f, .2f, .5f); Line(.2f, .5f, .3f, .4f); Line(.3f, .4f, .3f, .9f); Line(.3f, .9f, .7f, .9f); Line(.7f, .9f, .7f, .4f); Line(.7f, .4f, .8f, .5f); Line(.8f, .5f, .95f, .3f); Line(.95f, .3f, .7f, .1f); Line(.3f, .1f, .5f, .25f); Line(.5f, .25f, .7f, .1f); break;
            case "Trophy": Box(.3f, .1f, .4f, .45f); Line(.5f, .55f, .5f, .85f); Line(.2f, .9f, .8f, .9f); Box(.05f, .15f, .25f, .3f); Box(.7f, .15f, .25f, .3f); break;
            case "Dungeon": Box(.1f, .4f, .8f, .55f); Circle(.5f, .4f, .4f); Line(.35f, .4f, .35f, .95f); Line(.65f, .4f, .65f, .95f); break;
            case "Head": Circle(.5f, .35f, .3f); Line(.3f, .5f, .4f, .9f); Line(.7f, .5f, .6f, .9f); break;
            case "Pet": Circle(.5f, .65f, .3f); Line(.3f, .4f, .2f, .05f); Line(.7f, .4f, .8f, .05f); Circle(.4f, .65f, .025f); Circle(.6f, .65f, .025f); break;
            case "Mount": case "Route": Line(.2f, .9f, .3f, .45f); Line(.3f, .45f, .2f, .2f); Line(.2f, .2f, .45f, .05f); Line(.45f, .05f, .8f, .4f); Line(.8f, .4f, .55f, .6f); Line(.55f, .6f, .8f, .9f); Line(.2f, .9f, .8f, .9f); break;
            case "Book": Box(.05f, .15f, .45f, .7f); Box(.5f, .15f, .45f, .7f); break;
            default: Box(.1f, .1f, .8f, .8f); Line(.3f, .3f, .75f, .3f); Line(.3f, .5f, .75f, .5f); Line(.3f, .7f, .75f, .7f); break;
        }
    }
}
