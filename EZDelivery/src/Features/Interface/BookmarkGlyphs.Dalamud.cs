using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace EZDelivery.Features.Interface;

/// <summary>Code-native 24-unit glyphs. Parcel &amp; Arrow is called only by the Expert Delivery bookmark.</summary>
internal static class BookmarkGlyphs
{
    public static void ParcelAndArrow(ImDrawListPtr draw, Vector2 origin, float scale, uint color)
    {
        Vector2 P(float x, float y) => origin + new Vector2(x, y) * scale;
        draw.AddQuadFilled(P(1, 7), P(7, 4), P(14, 7), P(8, 10), color);
        draw.AddQuadFilled(P(1, 8.5f), P(7, 11.5f), P(7, 21), P(1, 18), color);
        draw.AddQuadFilled(P(9, 11.5f), P(14, 9), P(14, 18), P(9, 21), color);
        draw.AddLine(P(5, 5), P(11, 8), ImGui.GetColorU32(new Vector4(.35f, .30f, .19f, 1)), scale);
        draw.AddRectFilled(P(13, 11), P(20, 15), color);
        draw.AddTriangleFilled(P(19, 7), P(25, 13), P(19, 19), color);
    }

    /// <summary>Selected W3/A1/B1/F2 silhouettes. Supply the socket fill for integrated negative space.</summary>
    public static void Category(ImDrawListPtr draw, ShopCategory category, Vector2 origin, float scale, uint color, uint? cutoutColor = null)
    {
        Vector2 P(float x, float y) => origin + new Vector2(x, y) * scale;
        var cutout = cutoutColor ?? ImGui.GetColorU32(new Vector4(.12f, .14f, .15f, 1));
        var weight = 1.8f * scale;
        switch (category)
        {
            case ShopCategory.Weapons:
                // W3: upright blade and separate bow, with a short outward-pointing arrow.
                Fill(draw, color, [P(4.1f, 4), P(6.2f, .8f), P(8.3f, 4), P(8.3f, 16.5f), P(4.1f, 16.5f)]);
                draw.AddRectFilled(P(5.65f, 5.4f), P(6.75f, 16.5f), cutout);
                Fill(draw, color, [P(1.9f, 16.2f), P(10.5f, 16.2f), P(9.6f, 18), P(2.8f, 18)]);
                draw.AddRectFilled(P(5.3f, 17.5f), P(7.1f, 21), color);
                draw.AddCircleFilled(P(6.2f, 22.1f), 1.25f * scale, color, 12);
                Stroke(draw, color, weight, [P(13.2f, 2), P(13.2f, 22)]);
                Stroke(draw, color, weight, [P(13.5f, 3.2f), P(16.7f, 5.4f), P(19.1f, 8.2f),
                    P(20.2f, 10.9f), P(20.4f, 12), P(20.2f, 13.1f), P(19.1f, 15.8f),
                    P(16.7f, 18.6f), P(13.5f, 20.8f)]);
                draw.AddRectFilled(P(18.5f, 10.5f), P(23.6f, 13.5f), cutout);
                Stroke(draw, color, weight, [P(18.5f, 12), P(22.7f, 12)]);
                draw.AddTriangleFilled(P(22.1f, 10.6f), P(23.9f, 12), P(22.1f, 13.4f), color);
                break;
            case ShopCategory.Armor:
                // A1: shaped pauldrons and split metal breastplate; no cloth sleeves.
                Fill(draw, color, [P(.8f, 8.8f), P(2.4f, 5.8f), P(4.6f, 4.1f), P(7.1f, 3.4f),
                    P(7.5f, 6.8f), P(4.2f, 10.5f)]);
                Fill(draw, color, [P(16.9f, 3.4f), P(19.4f, 4.1f), P(21.6f, 5.8f), P(23.2f, 8.8f),
                    P(19.8f, 10.5f), P(16.5f, 6.8f)]);
                Stroke(draw, color, 1.7f * scale, [P(1.2f, 11), P(3.7f, 11.7f)]);
                Stroke(draw, color, 1.7f * scale, [P(20.3f, 11.7f), P(22.8f, 11)]);
                Fill(draw, color, [P(7.9f, 7.4f), P(11.1f, 9), P(11.1f, 18.3f), P(7.1f, 16.6f), P(5.3f, 11)]);
                Fill(draw, color, [P(12.9f, 9), P(16.1f, 7.4f), P(18.7f, 11), P(16.9f, 16.6f), P(12.9f, 18.3f)]);
                Stroke(draw, color, weight, [P(8.9f, 3.3f), P(9.2f, 5.2f), P(12, 6.7f), P(14.8f, 5.2f), P(15.1f, 3.3f)]);
                draw.AddLine(P(12, 6.9f), P(12, 8.1f), cutout, 1.7f * scale);
                draw.AddQuadFilled(P(6.8f, 18.2f), P(11.1f, 20), P(11.1f, 23), P(5.6f, 20.5f), color);
                draw.AddQuadFilled(P(12.9f, 20), P(17.2f, 18.2f), P(18.4f, 20.5f), P(12.9f, 23), color);
                break;
            case ShopCategory.Barding:
                // B1: left-facing chocobo with feather crest, helmet hinge and neck armor.
                draw.AddQuadFilled(P(11, 12), P(15.8f, 12), P(16.7f, 18.5f), P(10, 18.5f), color);
                draw.AddQuadFilled(P(10, 18.5f), P(16.7f, 18.5f), P(19, 23), P(7.8f, 23), color);
                Fill(draw, color, [P(11.5f, 5.5f), P(16.8f, 1.8f), P(22.5f, .6f), P(18.3f, 5.5f), P(14, 8)]);
                Fill(draw, color, [P(13.3f, 6.4f), P(19.3f, 5.9f), P(23.2f, 7.5f), P(18.2f, 9.1f), P(14, 9.5f)]);
                Fill(draw, color, [P(14.4f, 9), P(19.2f, 9.6f), P(22.5f, 12.2f), P(18.1f, 12.5f), P(14.9f, 11.7f)]);
                draw.AddTriangleFilled(P(14.4f, 11), P(18.5f, 12.5f), P(19, 15.6f), color);
                Fill(draw, color, [P(3.9f, 8.9f), P(7.5f, 5.4f), P(10.2f, 4.2f), P(13.7f, 8.2f),
                    P(12.5f, 11.1f), P(9.9f, 13.8f), P(6.4f, 15)]);
                Fill(draw, color, [P(1.2f, 13.4f), P(2.4f, 10.8f), P(3.9f, 9.7f), P(6.8f, 11),
                    P(8.7f, 13.3f), P(8.3f, 15), P(2.1f, 15)]);
                draw.AddTriangleFilled(P(1.2f, 13.4f), P(2.1f, 15), P(1.7f, 17.2f), color);
                Stroke(draw, cutout, 1.7f * scale, [P(3.9f, 9.5f), P(6.4f, 10.5f), P(8.5f, 12.5f), P(8.9f, 14.8f)]);
                Stroke(draw, cutout, 1.7f * scale, [P(12.6f, 9.4f), P(12.3f, 12.1f), P(10.6f, 14.3f), P(8.8f, 14.9f)]);
                Stroke(draw, cutout, 1.7f * scale, [P(10.1f, 17.4f), P(12.5f, 17), P(16.2f, 15.3f)]);
                draw.AddCircleFilled(P(8.6f, 8.8f), 1.05f * scale, cutout, 12);
                draw.AddCircleFilled(P(12.6f, 8.1f), 2.25f * scale, cutout, 16);
                draw.AddCircleFilled(P(12.6f, 8.1f), 1.2f * scale, color, 12);
                break;
            case ShopCategory.Furniture:
                // F2: open-backed chair beside a round pedestal table.
                Stroke(draw, color, weight, [P(18.4f, 2), P(16.8f, 12.9f)]);
                Stroke(draw, color, weight, [P(23, 2.5f), P(21.3f, 14)]);
                draw.AddQuadFilled(P(18.3f, 2.1f), P(23, 2.7f), P(22.6f, 5), P(17.9f, 4.4f), color);
                Stroke(draw, color, weight, [P(16.2f, 15), P(15.2f, 22.2f)]);
                Stroke(draw, color, weight, [P(20.6f, 15), P(21.9f, 22.2f)]);
                Stroke(draw, color, weight, [P(18.5f, 15.3f), P(18.5f, 23)]);
                Ellipse(draw, P(18.5f, 14.4f), new(3.9f * scale, 1.25f * scale), color);
                Stroke(draw, color, weight, [P(7.1f, 14.7f), P(7.1f, 19.4f)]);
                Stroke(draw, color, weight, [P(7.1f, 19.4f), P(5, 20.3f), P(3.6f, 22.5f), P(2.3f, 22.5f)]);
                Stroke(draw, color, weight, [P(7.1f, 19.4f), P(9.2f, 20.3f), P(10.6f, 22.5f), P(11.5f, 22.5f)]);
                Ellipse(draw, P(7.1f, 13.3f), new(6.3f * scale, 1.55f * scale), color);
                Ellipse(draw, P(7.1f, 11.7f), new(6.7f * scale, 1.9f * scale), cutout);
                Ellipse(draw, P(7.1f, 10.8f), new(6.3f * scale, 1.55f * scale), color);
                break;
        }
    }

    private static void Fill(ImDrawListPtr draw, uint color, ReadOnlySpan<Vector2> vertices)
    {
        foreach (var vertex in vertices) draw.PathLineTo(vertex);
        draw.PathFillConvex(color);
    }

    private static void Stroke(ImDrawListPtr draw, uint color, float width, ReadOnlySpan<Vector2> vertices)
    {
        for (var i = 1; i < vertices.Length; i++) draw.AddLine(vertices[i - 1], vertices[i], color, width);
        // Rounded joints keep the common structural weight legible at the 24-pixel base size.
        foreach (var vertex in vertices) draw.AddCircleFilled(vertex, width * .5f, color, 8);
    }

    private static void Ellipse(ImDrawListPtr draw, Vector2 center, Vector2 radii, uint color)
    {
        const int segments = 24;
        for (var i = 0; i < segments; i++)
        {
            var angle = i * (MathF.Tau / segments);
            draw.PathLineTo(center + new Vector2(MathF.Cos(angle) * radii.X, MathF.Sin(angle) * radii.Y));
        }
        draw.PathFillConvex(color);
    }
}
