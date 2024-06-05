using CombatOverhaul.Inputs;
using CombatOverhaul.RangedSystems;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace CombatOverhaul.Implementations.Vanilla;

[HasActionEventHandlers]
public sealed class BowClient : RangeWeaponClient
{
    public BowClient(ICoreClientAPI api, Item item) : base(api, item)
    {
        _api = api;
    }

    private readonly ICoreClientAPI _api;

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Pressed)]
    private bool Load(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand)
    {
        if (state != 0 || eventData.AltPressed) return false;

        return true;
    }


    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Active)]
    private bool Aim(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand)
    {
        if (state != 1 || eventData.AltPressed) return false;

        return true;
    }

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Released)]
    private bool Shoot(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand)
    {
        if (state != 2 || eventData.AltPressed) return false;

        return true;
    }
}

public class BowServer : RangeWeaponServer
{
    public BowServer(ICoreServerAPI api, Item item) : base(api, item)
    {
    }
}

public class BowItem : Item
{

}
