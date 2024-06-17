using CombatOverhaul.Inputs;
using CombatOverhaul.RangedSystems;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;

namespace CombatOverhaul.Implementations;

public class CrossbowClient : RangeWeaponClient
{
    public CrossbowClient(ICoreClientAPI api, Item item) : base(api, item)
    {
    }
}

public class CrossbowServer : RangeWeaponServer
{
    public CrossbowServer(ICoreServerAPI api, Item item) : base(api, item)
    {
    }

    public override bool Reload(IServerPlayer player, ItemSlot slot, ItemSlot? ammoSlot, ReloadPacket packet)
    {
        return false;
    }

    public override bool Shoot(IServerPlayer player, ItemSlot slot, ShotPacket packet, Entity shooter)
    {
        return false;
    }
}

public class CrossbowItem : Item, IHasWeaponLogic, IHasRangedWeaponLogic
{
    public CrossbowClient? ClientLogic { get; private set; }
    public CrossbowServer? ServerLogic { get; private set; }

    IClientWeaponLogic? IHasWeaponLogic.ClientLogic => ClientLogic;
    IServerRangedWeaponLogic? IHasRangedWeaponLogic.ServerWeaponLogic => ServerLogic;

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        if (api is ICoreClientAPI clientAPI)
        {
            ClientLogic = new(clientAPI, this);
        }

        if (api is ICoreServerAPI serverAPI)
        {
            ServerLogic = new(serverAPI, this);
        }
    }
}
