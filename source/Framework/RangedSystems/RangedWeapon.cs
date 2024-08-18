using CombatOverhaul.Animations;
using CombatOverhaul.Inputs;
using System.Numerics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
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

    public virtual void OnDeselected(EntityPlayer player, bool mainHand, ref int state)
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
    protected float GetAnimationSpeed(Entity player, string proficiencyStat, float min = 0.5f, float max = 2f)
    {
        float manipulationSpeed = PlayerBehavior?.ManipulationSpeed ?? 1;
        float proficiencyBonus = proficiencyStat == "" ? 0 : player.Stats.GetBlended(proficiencyStat) - 1;
        Console.WriteLine($"Stat '{proficiencyStat}': {player.Stats.GetBlended(proficiencyStat)}");
        return Math.Clamp(manipulationSpeed + proficiencyBonus, min, max);
    }
    protected bool CheckForOtherHandEmpty(bool mainHand, EntityPlayer player)
    {
        if (mainHand && !player.LeftHandItemSlot.Empty)
        {
            (player.World.Api as ICoreClientAPI)?.TriggerIngameError(this, "offhandShouldBeEmpty", Lang.Get("combatoverhaul:message-mainhand-empty"));
            return false;
        }

        if (!mainHand && !player.RightHandItemSlot.Empty)
        {
            (player.World.Api as ICoreClientAPI)?.TriggerIngameError(this, "mainHandShouldBeEmpty", Lang.Get("combatoverhaul:message-offhand-empty"));
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
    protected readonly NatFloat RandomFloat = new(0, 1, EnumDistribution.GAUSSIAN);
    protected const float MinutesInRadian = 180f * 60f;

    protected Vector3 GetDirectionWithDispersion(float[] direction, float[] dispersionMOA)
    {
        Vector3 directionVector = new(direction);
        Vector2 dispersionVector = new(dispersionMOA);

        return GetDirectionWithDispersion(directionVector, dispersionVector);
    }

    protected Vector3 GetDirectionWithDispersion(Vector3 direction, Vector2 dispersionMOA)
    {
        float randomPitch = RandomFloat.nextFloat() * dispersionMOA.Y * MathF.PI / MinutesInRadian;
        float randomYaw = RandomFloat.nextFloat() * dispersionMOA.X * MathF.PI / MinutesInRadian;

        Vector3 verticalAxis = new(0, 0, 1);
        bool directionIsVertical = (verticalAxis - direction).Length() < 1E6 || (verticalAxis + direction).Length() < 1E6;
        if (directionIsVertical) verticalAxis = new(0, 1, 0);

        Vector3 forwardAxis = Vector3.Normalize(direction);
        Vector3 yawAxis = Vector3.Normalize(Vector3.Cross(forwardAxis, verticalAxis));
        Vector3 pitchAxis = Vector3.Normalize(Vector3.Cross(yawAxis, forwardAxis));

        Vector3 yawComponent = yawAxis * MathF.Tan(randomYaw);
        Vector3 pitchComponent = pitchAxis * MathF.Tan(randomPitch);

        return Vector3.Normalize(forwardAxis + yawComponent + pitchComponent);
    }
}