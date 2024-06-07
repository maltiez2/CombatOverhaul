using CombatOverhaul.Inputs;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace CombatOverhaul.RangedSystems;

public class RangeWeaponClient : IClientWeaponLogic
{
    public RangeWeaponClient(ICoreClientAPI api, Item item)
    {
        CombatOverhaulSystem system = api.ModLoader.GetModSystem<CombatOverhaulSystem>();

        RangedWeaponSystem = system.ClientRangedWeaponSystem ?? throw new Exception();

        Item = item;
        Api = api;
    }

    public int ItemId => Item.Id;

    public virtual DirectionsConfiguration DirectionsType => DirectionsConfiguration.None;
    public virtual string ProjectileInventoryId => "projectile";


    public virtual void OnDeselected(EntityPlayer player)
    {

    }
    public virtual void OnRegistered(ActionsManagerPlayerBehavior behavior, ICoreClientAPI api)
    {
        PlayerBehavior = behavior;
    }
    public virtual void OnSelected(ItemSlot slot, EntityPlayer player, bool mainHand, ref int state)
    {

    }

    protected readonly Item Item;
    protected readonly ICoreClientAPI Api;
    protected readonly RangedWeaponSystemClient RangedWeaponSystem;
    protected ActionsManagerPlayerBehavior PlayerBehavior;
}

public class RangeWeaponServer : IServerRangedWeaponLogic
{
    public RangeWeaponServer(ICoreServerAPI api, Item item)
    {
        CombatOverhaulSystem system = api.ModLoader.GetModSystem<CombatOverhaulSystem>();

        ProjectileSystem = system.ServerProjectileSystem ?? throw new Exception();
        Item = item;
        Api = api;
    }

    public virtual bool Reload(IServerPlayer player, ItemSlot slot, ItemSlot? ammoSlot, ReloadPacket packet)
    {
        return false;
    }
    public virtual bool Shoot(IServerPlayer player, ItemSlot slot, ShotPacket packet)
    {
        return false;
    }

    protected ProjectileSystemServer ProjectileSystem;
    protected readonly Item Item;
    protected readonly ICoreServerAPI Api;
}