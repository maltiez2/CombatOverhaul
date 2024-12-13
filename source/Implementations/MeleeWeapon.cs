using Bullseye.Animations;
using Bullseye.Inputs;
using Bullseye.RangedSystems;
using Bullseye.RangedSystems.Aiming;
using System.Numerics;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace Bullseye.Implementations;

public enum MeleeWeaponState
{
    Idle,
    StartingAim,
    Aiming,
    Throwing
}

public class ThrowWeaponStats
{
    public string AimAnimation { get; set; } = "";
    public string ThrowAnimation { get; set; } = "";
    public AimingStatsJson Aiming { get; set; } = new();
    public float DamageStrength { get; set; }
    public float Knockback { get; set; } = 0;
    public int DurabilityDamage { get; set; } = 1;
    public float Velocity { get; set; } = 1;
    public float Zeroing { get; set; } = 1.5f;
}

public class MeleeWeaponStats : WeaponStats
{
    public ThrowWeaponStats? ThrowAttack { get; set; } = null;
}

public class MeleeWeaponClient : IClientWeaponLogic
{
    public MeleeWeaponClient(ICoreClientAPI api, Item item)
    {
        Item = item;
        Api = api;

        BullseyeSystem system = api.ModLoader.GetModSystem<BullseyeSystem>();
        SoundsSystem = system.ClientSoundsSynchronizer ?? throw new Exception();
        RangedWeaponSystem = system.ClientRangedWeaponSystem ?? throw new Exception();
        AimingSystem = system.AimingSystem ?? throw new Exception();

        Stats = item.Attributes.AsObject<MeleeWeaponStats>();
        AimingStats = Stats.ThrowAttack?.Aiming.ToStats();
    }

    public int ItemId => Item.Id;

    public virtual void OnSelected(ItemSlot slot, EntityPlayer player, bool mainHand, ref int state)
    {
        SetState(MeleeWeaponState.Idle, mainHand);
    }
    public virtual void OnDeselected(EntityPlayer player, bool mainHand, ref int state)
    {
        AnimationBehavior?.StopSpeedModifier();
        AimingAnimationController?.Stop(mainHand);
        AimingSystem.StopAiming();
        AnimationBehavior?.StopAllVanillaAnimations(mainHand);
    }
    public virtual void OnRegistered(ActionsManagerPlayerBehavior behavior, ICoreClientAPI api)
    {
        PlayerBehavior = behavior;
        AnimationBehavior = behavior.entity.GetBehavior<FirstPersonAnimationsBehavior>();

        if (AimingStats != null) AimingAnimationController = new(AimingSystem, AnimationBehavior, AimingStats);
    }

    protected readonly Item Item;
    protected readonly ICoreClientAPI Api;
    protected readonly RangedWeaponSystemClient RangedWeaponSystem;
    protected readonly ClientAimingSystem AimingSystem;
    protected FirstPersonAnimationsBehavior? AnimationBehavior;
    protected ActionsManagerPlayerBehavior? PlayerBehavior;
    protected SoundsSynchronizerClient SoundsSystem;
    protected AimingAnimationController? AimingAnimationController;
    internal const int _maxStates = 100;
    protected const int MaxState = _maxStates;
    protected readonly MeleeWeaponStats Stats;
    protected const string PlayerStatsMainHandCategory = "Bullseye:held-item-mainhand";
    protected const string PlayerStatsOffHandCategory = "Bullseye:held-item-offhand";
    protected bool ParryButtonReleased = true;

    protected long MainHandAttackCooldownTimer = -1;
    protected long OffHandAttackCooldownTimer = -1;
    protected long MainHandBlockCooldownTimer = -1;
    protected long OffHandBlockCooldownTimer = -1;
    protected int MainHandAttackCounter = 0;
    protected int OffHandAttackCounter = 0;
    protected bool HandleHitTerrain = false;
    protected const bool EditColliders = false;

    protected readonly AimingStats? AimingStats;

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Active)]
    protected virtual bool Aim(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand)
    {
        if (eventData.AltPressed || Stats.ThrowAttack == null || AimingStats == null) return false;
        if (!CheckState(mainHand, MeleeWeaponState.Idle)) return false;

        SetState(MeleeWeaponState.StartingAim, mainHand);
        AimingSystem.AimingState = WeaponAimingState.Blocked;
        AimingAnimationController?.Play(mainHand);
        AimingSystem.StartAiming(AimingStats);

        AnimationBehavior?.Play(mainHand, Stats.ThrowAttack.AimAnimation, callback: () => AimAnimationCallback(slot, mainHand));

        return true;
    }
    protected virtual bool AimAnimationCallback(ItemSlot slot, bool mainHand)
    {
        SetState(MeleeWeaponState.Aiming, mainHand);
        AimingSystem.AimingState = WeaponAimingState.FullCharge;

        return true;
    }

    [ActionEventHandler(EnumEntityAction.LeftMouseDown, ActionState.Pressed)]
    protected virtual bool Throw(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand)
    {
        if (Stats.ThrowAttack == null) return false;
        if (!CheckState(mainHand, MeleeWeaponState.Aiming)) return false;

        SetState(MeleeWeaponState.Throwing, mainHand);
        AnimationBehavior?.Play(mainHand, Stats.ThrowAttack.ThrowAnimation, callback: () => ThrowAnimationCallback(slot, player, mainHand));

        return true;
    }
    protected virtual bool ThrowAnimationCallback(ItemSlot slot, EntityPlayer player, bool mainHand)
    {
        if (Stats.ThrowAttack == null) return false;

        SetState(MeleeWeaponState.Idle, mainHand);
        AimingSystem.AimingState = WeaponAimingState.FullCharge;

        Vintagestory.API.MathTools.Vec3d position = player.LocalEyePos + player.Pos.XYZ;
        Vector3 targetDirection = AimingSystem.TargetVec;

        targetDirection = ClientAimingSystem.Zeroing(targetDirection, Stats.ThrowAttack.Zeroing);

        RangedWeaponSystem.Shoot(slot, 1, new Vector3((float)position.X, (float)position.Y, (float)position.Z), new Vector3(targetDirection.X, targetDirection.Y, targetDirection.Z), mainHand, _ => { });

        slot.TakeOut(1);

        return true;
    }

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Released)]
    protected virtual bool Ease(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand)
    {
        if (!CheckState(mainHand, MeleeWeaponState.StartingAim, MeleeWeaponState.Aiming)) return false;

        AnimationBehavior?.PlayReadyAnimation(mainHand);
        SetState(MeleeWeaponState.Idle, mainHand);
        AimingAnimationController?.Stop(mainHand);
        AimingSystem.StopAiming();

        return true;
    }

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
}

public class MeleeWeaponServer : RangeWeaponServer
{
    public MeleeWeaponServer(ICoreServerAPI api, Item item) : base(api, item)
    {
        ProjectileSystem = api.ModLoader.GetModSystem<BullseyeSystem>().ServerProjectileSystem ?? throw new Exception();
        Stats = item.Attributes.AsObject<MeleeWeaponStats>();
    }

    public override bool Shoot(IServerPlayer player, ItemSlot slot, ShotPacket packet, Entity shooter)
    {
        if (slot?.Itemstack == null || slot.Itemstack.StackSize < 1) return false;

        ProjectileStats? stats = slot.Itemstack.Item.GetCollectibleBehavior<ProjectileBehavior>(true)?.Stats;

        if (stats == null)
        {
            return false;
        }

        ProjectileSpawnStats spawnStats = new()
        {
            ProducerEntityId = player.Entity.EntityId,
            DamageMultiplier = 1,
            DamageStrength = Stats.ThrowAttack?.DamageStrength ?? 0,
            Position = new Vector3(packet.Position[0], packet.Position[1], packet.Position[2]),
            Velocity = Vector3.Normalize(new Vector3(packet.Velocity[0], packet.Velocity[1], packet.Velocity[2])) * (Stats.ThrowAttack?.Velocity ?? 1)
        };

        AssetLocation projectileCode = slot.Itemstack.Item.Code.Clone();

        ProjectileSystem.Spawn(packet.ProjectileId[0], stats, spawnStats, slot.TakeOut(1), shooter);

        SwapToNewProjectile(player, slot, projectileCode);

        slot.MarkDirty();

        return true;
    }

    protected readonly MeleeWeaponStats Stats;

    protected static void SwapToNewProjectile(IServerPlayer player, ItemSlot slot, AssetLocation projectileCode)
    {
        if (slot.Itemstack == null || slot.Itemstack.StackSize == 0)
        {
            ItemSlot? replacementSlot = null;
            player.Entity.WalkInventory(slot =>
            {
                if (slot?.Itemstack?.Item == null) return true;

                if (slot.Itemstack.Item.Code.ToString() == projectileCode.ToString())
                {
                    replacementSlot = slot;
                    return false;
                }

                return true;
            });

            if (replacementSlot == null)
            {
                string projectilePath = projectileCode.ToShortString();

                while (projectilePath.Contains('-'))
                {
                    int delimiterIndex = projectilePath.LastIndexOf('-');
                    projectilePath = projectilePath.Substring(0, delimiterIndex);
                    string wildcard = $"{projectilePath}-*";

                    player.Entity.WalkInventory(slot =>
                    {
                        if (slot?.Itemstack?.Item == null) return true;

                        if (WildcardUtil.Match(wildcard, slot.Itemstack.Item.Code.ToShortString()))
                        {
                            replacementSlot = slot;
                            return false;
                        }

                        return true;
                    });

                    if (replacementSlot != null) break;
                }
            }

            if (replacementSlot != null)
            {
                slot.TryFlipWith(replacementSlot);
                replacementSlot.MarkDirty();
            }
        }
    }
}

public class MeleeWeapon : Item, IHasWeaponLogic, IHasRangedWeaponLogic
{
    public MeleeWeaponClient? ClientLogic { get; private set; }
    public MeleeWeaponServer? ServerLogic { get; private set; }

    IClientWeaponLogic? IHasWeaponLogic.ClientLogic => ClientLogic;
    IServerRangedWeaponLogic? IHasRangedWeaponLogic.ServerWeaponLogic => ServerLogic;

    public bool RenderingOffset { get; set; }

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        if (api is ICoreClientAPI clientAPI)
        {
            ClientLogic = new(clientAPI, this);
            MeleeWeaponStats Stats = Attributes.AsObject<MeleeWeaponStats>();
        }

        if (api is ICoreServerAPI serverAPI)
        {
            ServerLogic = new(serverAPI, this);
        }
    }
}