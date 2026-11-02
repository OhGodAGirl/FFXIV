using System.Numerics;

namespace BetterGlamorPlates.Features.Main;

/// <summary>
/// Keeps the sidecar's reference geometry and responsive breakpoints outside
/// the renderer so visual landmarks can be verified without a runtime UI.
/// </summary>
internal static class MainLayoutPolicy
{
    internal const float ReferenceWidth = 1080f;
    internal const float ReferenceHeight = 860f;
    internal const float MinimumWidth = 820f;
    internal const float MinimumHeight = 700f;
    internal const float RailWidth = 314f;
    internal const float PaneGap = 6f;
    internal const float CollectionTileWidth = 70f;
    internal const float CollectionTileGap = 8f;
    internal const float FooterHeight = 68f;
    internal const float ReferencePlatePanelHeight = 366f;
    internal const float ReferenceCollectionPanelHeight = 386f;

    internal static MainLayoutMetrics Calculate(float contentWidth, float contentHeight)
    {
        var safeWidth = Normalize(contentWidth, MinimumWidth - 24f);
        var safeHeight = Normalize(contentHeight, MinimumHeight - 48f);
        var railWidth = Math.Min(RailWidth, Math.Max(224f, Math.Min(safeWidth * 0.30f, safeWidth - 572f)));
        var mainWidth = safeWidth - railWidth - PaneGap;
        var collectionColumns = Math.Max(
            4,
            (int)Math.Floor((mainWidth - 24f + CollectionTileGap)
                / (CollectionTileWidth + CollectionTileGap)));
        var platePanelHeight = ReferencePlatePanelHeight;
        var collectionPanelHeight = Math.Max(
            180f,
            safeHeight - platePanelHeight - FooterHeight - PaneGap * 2);

        return new MainLayoutMetrics(
            railWidth,
            mainWidth,
            platePanelHeight,
            collectionPanelHeight,
            collectionColumns);
    }

    internal static float FooterSize(float width, bool feedback)
        => (width < 650 ? 90 : FooterHeight) + (feedback ? 84 : 0);

    internal static EquipmentCellGeometry EquipmentCell(int index, float width, float scale)
    {
        var columnWidth = width / 6;
        return new EquipmentCellGeometry(new Vector2(MathF.Round(index % 6 * columnWidth),
            MathF.Round(index / 6 * 142 * scale)), MathF.Round(columnWidth), MathF.Round(70 * scale));
    }

    internal static int CalculateRowCount(int itemCount, int columns)
        => itemCount <= 0 || columns <= 0 ? 0 : (itemCount + columns - 1) / columns;

    internal static (int Columns, float TileSize, float RowPitch) CollectionGrid(float width, float height, float gap)
    {
        width = Math.Max(1, float.IsFinite(width) ? width : 1);
        height = Math.Max(1, float.IsFinite(height) ? height : 1);
        var target = Math.Max(1, (height - 2 * gap) / 3);
        var columns = Math.Clamp((int)Math.Ceiling((width + gap) / (target + gap)), 1, 32);
        var tileSize = Math.Max(1, (width - (columns - 1) * gap) / columns);
        return (columns, tileSize, Math.Max(tileSize, (height - tileSize) / 2));
    }

    internal static int CalculateItemIndex(int row, int column, int columns)
        => row < 0 || column < 0 || columns <= 0 ? -1 : (row * columns) + column;

    private static float Normalize(float value, float minimum)
        => float.IsFinite(value) ? Math.Max(minimum, value) : minimum;
}

internal readonly record struct MainLayoutMetrics(
    float RailWidth,
    float MainWidth,
    float PlatePanelHeight,
    float CollectionPanelHeight,
    int CollectionColumns);

internal readonly record struct EquipmentCellGeometry(Vector2 Position, float Width, float IconSize);

