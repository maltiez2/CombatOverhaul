using CombatOverhaul.Integration;
using CombatOverhaul.PlayerAnimations;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace CombatOverhaul;


public sealed class CombatOverhaulModSystem : ModSystem
{
    public AnimationsManager? AnimationsManager { get; private set; }

    public override void StartClientSide(ICoreClientAPI api)
    {
        api.RegisterEntityBehaviorClass("CombatOverhaul:FirstPersonAnimations", typeof(FirstPersonAnimationsBehavior));

        AnimatorPatch.Patch("CombatOverhaul");
    }

    public override void AssetsFinalize(ICoreAPI api)
    {
        if (api is ICoreClientAPI clientApi)
        {
            AnimationsManager = new(clientApi);
        }
    }

    public override void Dispose()
    {
        AnimatorPatch.Unpatch("CombatOverhaul");
    }
}
