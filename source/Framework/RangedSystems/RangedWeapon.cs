using CombatOverhaul.Animations;
using CombatOverhaul.Inputs;
using OpenTK.Mathematics;
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
        AttachmentSystem = system.ClientAttachmentSystem ?? throw new Exception();

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
        TpAnimationBehavior = behavior.entity.GetBehavior<ThirdPersonAnimationsBehavior>();
    }
    public virtual void OnSelected(ItemSlot slot, EntityPlayer player, bool mainHand, ref int state)
    {

    }

    protected readonly Item Item;
    protected readonly ICoreClientAPI Api;
    protected readonly RangedWeaponSystemClient RangedWeaponSystem;
    protected readonly AttachableSystemClient AttachmentSystem;
    protected FirstPersonAnimationsBehavior? AnimationBehavior;
    protected ThirdPersonAnimationsBehavior? TpAnimationBehavior;
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
        return Math.Clamp(manipulationSpeed + proficiencyBonus, min, max);
    }
    protected bool CheckForOtherHandEmpty(bool mainHand, EntityPlayer player)
    {
        if (mainHand && !player.LeftHandItemSlot.Empty)
        {
            (player.World.Api as ICoreClientAPI)?.TriggerIngameError(this, "offhandShouldBeEmpty", Lang.Get("combatoverhaul:message-offhand-empty"));
            return false;
        }

        if (!mainHand && !player.RightHandItemSlot.Empty)
        {
            (player.World.Api as ICoreClientAPI)?.TriggerIngameError(this, "mainHandShouldBeEmpty", Lang.Get("combatoverhaul:message-mainhand-empty"));
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

    protected Vector3d GetDirectionWithDispersion(double[] direction, float[] dispersionMOA)
    {
        Vector3d directionVector = new(direction[0], direction[1], direction[2]);
        Vector2 dispersionVector = new(dispersionMOA[0], dispersionMOA[1]);

        return GetDirectionWithDispersion(directionVector, dispersionVector);
    }

    protected Vector3d GetDirectionWithDispersion(Vector3d direction, Vector2 dispersionMOA)
    {
        float randomPitch = RandomFloat.nextFloat() * dispersionMOA.Y * MathF.PI / MinutesInRadian;
        float randomYaw = RandomFloat.nextFloat() * dispersionMOA.X * MathF.PI / MinutesInRadian;

        Vector3 verticalAxis = new(0, 0, 1);
        bool directionIsVertical = (verticalAxis - direction).Length < 1E6 || (verticalAxis + direction).Length < 1E6;
        if (directionIsVertical) verticalAxis = new(0, 1, 0);

        Vector3d forwardAxis = Vector3d.Normalize(direction);
        Vector3d yawAxis = Vector3d.Normalize(Vector3d.Cross(forwardAxis, verticalAxis));
        Vector3d pitchAxis = Vector3d.Normalize(Vector3d.Cross(yawAxis, forwardAxis));

        Vector3d yawComponent = yawAxis * Math.Tan(randomYaw);
        Vector3d pitchComponent = pitchAxis * Math.Tan(randomPitch);

        return Vector3d.Normalize(forwardAxis + yawComponent + pitchComponent);
    }
}