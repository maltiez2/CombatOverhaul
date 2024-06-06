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

    }
    public virtual void OnSelected(ItemSlot slot, EntityPlayer player, bool mainHand, ref int state)
    {

    }

    protected readonly Item Item;
    protected readonly ICoreClientAPI Api;
    protected readonly RangedWeaponSystemClient RangedWeaponSystem;
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


public abstract class RangedWeaponItem : Item, IRangedWeapon
{
    public IServerRangedWeaponLogic ServerWeaponLogic => _serverWeaponLogic ?? throw new Exception("Trying to access server side weapon logic from client side, or trying to access it before loaded.");

    public override void OnLoaded(ICoreAPI api)
    {
        if (api is ICoreServerAPI server)
        {
            _serverWeaponLogic = new(server, this);
        }

        if (api is ICoreClientAPI client)
        {
            _clientWeaponLogic = new(client, this);
        }
    }

    private RangeWeaponClient? _clientWeaponLogic;
    private RangeWeaponServer? _serverWeaponLogic;
}