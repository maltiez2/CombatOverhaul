using CombatOverhaul.Animations;
using CombatOverhaul.Implementations;
using CombatOverhaul.Inputs;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
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


    public virtual void OnDeselected(EntityPlayer player)
    {

    }
    public virtual void OnRegistered(ActionsManagerPlayerBehavior behavior, ICoreClientAPI api)
    {
        PlayerBehavior = behavior;
        AnimationBehavior = behavior.entity.GetBehavior<FirstPersonAnimationsBehavior>();
    }
    public virtual void OnSelected(ItemSlot slot, EntityPlayer player, bool mainHand, ref int state)
    {

    }

    protected readonly Item Item;
    protected readonly ICoreClientAPI Api;
    protected readonly RangedWeaponSystemClient RangedWeaponSystem;
    protected FirstPersonAnimationsBehavior? AnimationBehavior;
    protected ActionsManagerPlayerBehavior? PlayerBehavior;

    protected static bool CheckState<TState>(int state, params TState[] statesToCheck)
        where TState : struct, Enum
    {
        return statesToCheck.Contains((TState)Enum.ToObject(typeof(TState), state));
    }
    protected bool CheckState<TState>(bool mainHand, params TState[] statesToCheck)
        where TState : struct, Enum
    {
        return statesToCheck.Contains((TState)Enum.ToObject(typeof(TState), PlayerBehavior?.GetState(mainHand) ?? 0));
    }
    protected void SetState<TState>(TState state, bool mainHand = true)
        where TState : struct, Enum
    {
        PlayerBehavior?.SetState((int)Enum.ToObject(typeof(TState), state), mainHand);
    }
    protected TState GetState<TState>(bool mainHand = true)
        where TState : struct, Enum
    {
        return (TState)Enum.ToObject(typeof(TState), PlayerBehavior?.GetState(mainHand) ?? 0);
    }

    protected bool CheckForOtherHandEmpty(bool mainHand, EntityPlayer player)
    {
        if (mainHand && !player.LeftHandItemSlot.Empty)
        {
            (player.World.Api as ICoreClientAPI)?.TriggerIngameError(this, "offhandShouldBeEmpty", Lang.Get("Offhand should be empty"));
            return false;
        }

        if (!mainHand && !player.RightHandItemSlot.Empty)
        {
            (player.World.Api as ICoreClientAPI)?.TriggerIngameError(this, "mainHandShouldBeEmpty", Lang.Get("Main hand should be empty"));
            return false;
        }

        return true;
    }
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
    public virtual bool Shoot(IServerPlayer player, ItemSlot slot, ShotPacket packet, Entity shooter)
    {
        return false;
    }

    protected ProjectileSystemServer ProjectileSystem;
    protected readonly Item Item;
    protected readonly ICoreServerAPI Api;
}