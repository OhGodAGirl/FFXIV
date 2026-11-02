using BetterGlamorPlates.Features.GlamourPlates;

namespace BetterGlamorPlates.Bootstrap;

/// <summary>Publishes a completed catalog once and reports failed enumeration after releasing it.</summary>
internal sealed class IconCatalogPublication(
    Func<bool> complete,
    Action advance,
    Action dispose,
    Func<IReadOnlyList<GameIconOption>> icons,
    Action<IReadOnlyList<GameIconOption>> publish,
    Action<Exception> reportFailure)
{
    internal void Update()
    {
        if (complete()) return;
        try { advance(); }
        catch (Exception exception)
        {
            dispose();
            reportFailure(exception);
        }
        if (complete()) publish(icons());
    }
}
