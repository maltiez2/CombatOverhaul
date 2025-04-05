using Vintagestory.API.Common;

namespace Bullseye;

public sealed class BullseyeSystem : ModSystem
{
    public override void Start(ICoreAPI api)
    {
        api.RegisterItemClass("Bullseye:Spear", typeof(SpearItem));
    }
}