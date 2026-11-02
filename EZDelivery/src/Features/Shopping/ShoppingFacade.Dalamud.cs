using Dalamud.Plugin.Services;

namespace EZDelivery.Features.Shopping;

public sealed partial class ShoppingFacade
{
    /// <summary>Connects catalog, native shop observations, collection evidence and time.</summary>
    public ShoppingFacade(IDataManager data, IGameGui gui, IClientState client,
        Func<ShopOffer, PurchaseKnowledge> knowledge, Func<bool> canMutate)
        : this(new ShopCatalog(data), new ShoppingNative(gui, client, data, canMutate), knowledge, () => DateTimeOffset.UtcNow)
    {
    }
}
