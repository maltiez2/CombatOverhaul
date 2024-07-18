using CombatOverhaul.Animations;
using CombatOverhaul.DamageSystems;
using CombatOverhaul.Inputs;
using CombatOverhaul.MeleeSystems;
using CombatOverhaul.RangedSystems;
using CombatOverhaul.RangedSystems.Aiming;
using System.Numerics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using VSImGui.Debug;

namespace CombatOverhaul.Implementations;

public enum JavelinState
{
    Idle,
    WindingUp,
    Attacking,
    StartingAim,
    Aiming,
    Throwing
}

public class JavelinStats : WeaponStats
{
    public string AttackAnimation { get; set; } = "";
    public string AimAnimation { get; set; } = "";
    public string ThrowAnimation { get; set; } = "";

    public float AttackCooldownMs { get; set; } = 0;

    public MeleeAttackStats Attack { get; set; } = new();
    public AimingStatsJson Aiming { get; set; } = new();
    public float DamageStrength { get; set; } = 0;
    public float Velocity { get; set; } = 1;
    public float Zeroing { get; set; } = 1.5f;
    public bool RenderingOffset { get; set; } = false;
}

public class JavelinClient : IClientWeaponLogic
{
    public JavelinClient(ICoreClientAPI api, Item item)
    {
        Item = item;
        Api = api;

        CombatOverhaulSystem system = api.ModLoader.GetModSystem<CombatOverhaulSystem>();
        RangedWeaponSystem = system.ClientRangedWeaponSystem ?? throw new Exception();
        AimingSystem = system.AimingSystem ?? throw new Exception();

        Stats = item.Attributes.AsObject<JavelinStats>();
        AimingStats = Stats.Aiming.ToStats();

        MeleeAttack = new(api, Stats.Attack);

        //DebugEditColliders(MeleeAttack, 0);
    }

    public int ItemId => Item.Id;

    public virtual DirectionsConfiguration DirectionsType => DirectionsConfiguration.Eight;

    public virtual void OnSelected(ItemSlot slot, EntityPlayer player, bool mainHand, ref int state)
    {
        SetState(MeleeWeaponState.Idle, mainHand);
    }
    public virtual void OnDeselected(EntityPlayer player)
    {
        StopAttackCooldown(true);
        StopAttackCooldown(false);
        AimingAnimationController?.Stop(true);
        AimingSystem.StopAiming();
    }
    public virtual void OnRegistered(ActionsManagerPlayerBehavior behavior, ICoreClientAPI api)
    {
        PlayerBehavior = behavior;
        AnimationBehavior = behavior.entity.GetBehavior<FirstPersonAnimationsBehavior>();
        AimingAnimationController = new(AimingSystem, AnimationBehavior, AimingStats);
    }

    public virtual void RenderDebugCollider(ItemSlot inSlot, IClientPlayer byPlayer)
    {
        foreach (MeleeDamageType damageType in MeleeAttack.DamageTypes)
        {
            damageType.RelativeCollider.Transform(byPlayer.Entity.Pos, byPlayer.Entity, inSlot, Api, right: true)?.Render(Api, byPlayer.Entity);
        }
    }

    protected readonly Item Item;
    protected readonly ICoreClientAPI Api;
    protected readonly RangedWeaponSystemClient RangedWeaponSystem;
    protected readonly ClientAimingSystem AimingSystem;
    protected FirstPersonAnimationsBehavior? AnimationBehavior;
    protected ActionsManagerPlayerBehavior? PlayerBehavior;
    protected AimingAnimationController? AimingAnimationController;
    protected const int MaxStates = 100;
    protected readonly JavelinStats Stats;
    protected readonly AimingStats AimingStats;

    protected long MainHandAttackCooldownTimer = -1;
    protected long OffHandAttackCooldownTimer = -1;

    protected MeleeAttack MeleeAttack;

    [ActionEventHandler(EnumEntityAction.LeftMouseDown, ActionState.Active)]
    protected virtual bool Attack(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (eventData.AltPressed || eventData.Modifiers.Contains(EnumEntityAction.CtrlKey)) return false;
        if (IsAttackOnCooldown(mainHand))
        {
            return false;
        }
        if (PlayerBehavior?.GetState(!mainHand) % MeleeWeaponClient.MaxStates != 0) return false;

        switch (GetState<MeleeWeaponState>(mainHand))
        {
            case MeleeWeaponState.Idle:
                {
                    SetState(MeleeWeaponState.WindingUp, mainHand);
                    MeleeAttack.Start(player.Player);
                    AnimationBehavior?.Play(mainHand, Stats.AttackAnimation, animationSpeed: PlayerBehavior?.ManipulationSpeed ?? 1, callback: () => AttackAnimationCallback(mainHand), callbackHandler: code => AttackAnimationCallbackHandler(code, mainHand));
                }
                break;
            case MeleeWeaponState.WindingUp:
                break;
            case MeleeWeaponState.Attacking:
                {
                    TryAttack(MeleeAttack, slot, player, mainHand);
                }
                break;
            default:
                return false;
        }

        return true;
    }
    protected virtual void TryAttack(MeleeAttack attack, ItemSlot slot, EntityPlayer player, bool mainHand)
    {
        attack.Attack(
            player.Player,
            slot,
            mainHand,
            out IEnumerable<(Block block, System.Numerics.Vector3 point)> terrainCollision,
            out IEnumerable<(Vintagestory.API.Common.Entities.Entity entity, System.Numerics.Vector3 point)> entitiesCollision);
    }
    protected virtual bool AttackAnimationCallback(bool mainHand)
    {
        AnimationBehavior?.PlayReadyAnimation(mainHand);
        SetState(JavelinState.Idle, mainHand);

        return true;
    }
    protected virtual void AttackAnimationCallbackHandler(string callbackCode, bool mainHand)
    {
        switch (callbackCode)
        {
            case "start":
                SetState(MeleeWeaponState.Attacking, mainHand);
                break;
            case "stop":
                //SetState(MeleeWeaponState.Idle, mainHand);
                break;
        }
    }

    [ActionEventHandler(EnumEntityAction.LeftMouseDown, ActionState.Released)]
    protected virtual bool StopAttack(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (!CheckState(mainHand, MeleeWeaponState.Attacking, MeleeWeaponState.WindingUp)) return false;

        AnimationBehavior?.PlayReadyAnimation(mainHand);
        SetState(MeleeWeaponState.Idle, mainHand);

        if (Stats.AttackCooldownMs != 0)
        {
            StartAttackCooldown(mainHand, TimeSpan.FromMilliseconds(Stats.AttackCooldownMs));
        }

        return true;
    }

    [ActionEventHandler(EnumEntityAction.LeftMouseDown, ActionState.Active)]
    protected virtual bool Aim(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (eventData.AltPressed || !eventData.Modifiers.Contains(EnumEntityAction.CtrlKey)) return false;
        if (!CheckState(mainHand, JavelinState.Idle)) return false;

        SetState(JavelinState.StartingAim, mainHand);
        AimingSystem.AimingState = WeaponAimingState.Blocked;
        AimingAnimationController?.Play(mainHand);
        AimingSystem.StartAiming(AimingStats);

        AnimationBehavior?.Play(mainHand, Stats.AimAnimation, animationSpeed: PlayerBehavior?.ManipulationSpeed ?? 1, callback: () => AimAnimationCallback(slot, mainHand));

        return true;
    }
    protected virtual bool AimAnimationCallback(ItemSlot slot, bool mainHand)
    {
        SetState(JavelinState.Aiming, mainHand);
        AimingSystem.AimingState = WeaponAimingState.FullCharge;
        
        return true;
    }

    [ActionEventHandler(EnumEntityAction.LeftMouseDown, ActionState.Released)]
    protected virtual bool Throw(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (!CheckState(mainHand, JavelinState.Aiming)) return false;

        SetState(JavelinState.Throwing, mainHand);
        AnimationBehavior?.Play(mainHand, Stats.ThrowAnimation, animationSpeed: PlayerBehavior?.ManipulationSpeed ?? 1, callback: () => ThrowAnimationCallback(slot, player, mainHand));

        return true;
    }
    protected virtual bool ThrowAnimationCallback(ItemSlot slot, EntityPlayer player, bool mainHand)
    {
        SetState(JavelinState.Idle, mainHand);
        AimingSystem.AimingState = WeaponAimingState.FullCharge;
        
        Vintagestory.API.MathTools.Vec3d position = player.LocalEyePos + player.Pos.XYZ;
        Vector3 targetDirection = AimingSystem.TargetVec;

        targetDirection = ClientAimingSystem.Zeroing(targetDirection, Stats.Zeroing);

        RangedWeaponSystem.Shoot(slot, 1, new Vector3((float)position.X, (float)position.Y, (float)position.Z), new Vector3(targetDirection.X, targetDirection.Y, targetDirection.Z), mainHand, _ => { });

        slot.TakeOut(1);

        return true;
    }

    [ActionEventHandler(EnumEntityAction.LeftMouseDown, ActionState.Released)]
    protected virtual bool Ease(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (!CheckState(mainHand, JavelinState.StartingAim)) return false;

        AnimationBehavior?.PlayReadyAnimation(mainHand);
        SetState(JavelinState.Idle, mainHand);
        AimingAnimationController?.Stop(mainHand);
        AimingSystem.StopAiming();

        return true;
    }

    protected virtual void StartAttackCooldown(bool mainHand, TimeSpan time)
    {
        StopAttackCooldown(mainHand);

        if (mainHand)
        {
            MainHandAttackCooldownTimer = Api.World.RegisterCallback(_ => MainHandAttackCooldownTimer = -1, (int)(time.TotalMilliseconds / PlayerBehavior?.ManipulationSpeed ?? 1));
        }
        else
        {
            OffHandAttackCooldownTimer = Api.World.RegisterCallback(_ => OffHandAttackCooldownTimer = -1, (int)(time.TotalMilliseconds / PlayerBehavior?.ManipulationSpeed ?? 1));
        }
    }
    protected virtual void StopAttackCooldown(bool mainHand)
    {
        if (mainHand)
        {
            if (MainHandAttackCooldownTimer != -1)
            {
                Api.World.UnregisterCallback(MainHandAttackCooldownTimer);
                MainHandAttackCooldownTimer = -1;
            }
        }
        else
        {
            if (OffHandAttackCooldownTimer != -1)
            {
                Api.World.UnregisterCallback(OffHandAttackCooldownTimer);
                OffHandAttackCooldownTimer = -1;
            }
        }
    }
    protected virtual bool IsAttackOnCooldown(bool mainHand) => mainHand ? MainHandAttackCooldownTimer != -1 : OffHandAttackCooldownTimer != -1;

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

    private void DebugEditColliders(MeleeAttack attack, int attackIndex)
    {
        int typeIndex = 0;
        foreach (MeleeDamageType damageType in attack.DamageTypes)
        {
            int index = attackIndex * 100 + typeIndex++;

            DebugWidgets.Float3Drag("test", "colliders", $"{Item.Code}: Collider {index} tail", () =>
            {
                return new Vec3f(damageType.RelativeCollider.Position.X, damageType.RelativeCollider.Position.Y, damageType.RelativeCollider.Position.Z);
            },
            newTail =>
            {
                damageType.RelativeCollider = new(new Vector3(newTail.X, newTail.Y, newTail.Z), damageType.RelativeCollider.Direction);
            });
            DebugWidgets.Float3Drag("test", "colliders", $"{Item.Code}: Collider {index} head", () =>
            {
                return new Vec3f(
                    damageType.RelativeCollider.Direction.X + damageType.RelativeCollider.Position.X,
                    damageType.RelativeCollider.Direction.Y + damageType.RelativeCollider.Position.Y,
                    damageType.RelativeCollider.Direction.Z + damageType.RelativeCollider.Position.Z);
            },
            newHead =>
            {
                damageType.RelativeCollider = new(damageType.RelativeCollider.Position, new Vector3(newHead.X, newHead.Y, newHead.Z) - damageType.RelativeCollider.Position);
            });
        }
    }
}

public class JavelinServer : RangeWeaponServer
{
    public JavelinServer(ICoreServerAPI api, Item item) : base(api, item)
    {
        _projectileSystem = api.ModLoader.GetModSystem<CombatOverhaulSystem>().ServerProjectileSystem ?? throw new Exception();
        _stats = item.Attributes.AsObject<JavelinStats>();
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
            DamageStrength = _stats.DamageStrength,
            Position = new Vector3(packet.Position[0], packet.Position[1], packet.Position[2]),
            Velocity = Vector3.Normalize(new Vector3(packet.Velocity[0], packet.Velocity[1], packet.Velocity[2])) * _stats.Velocity
        };

        _projectileSystem.Spawn(packet.ProjectileId, stats, spawnStats, slot.TakeOut(1), shooter);

        slot.MarkDirty();

        return true;
    }

    private readonly ProjectileSystemServer _projectileSystem;
    private readonly JavelinStats _stats;
}

public class JavelinItem : Item, IHasWeaponLogic, IHasRangedWeaponLogic, IHasIdleAnimations, IHasMeleeWeaponActions, IHasServerBlockCallback, ISetsRenderingOffset
{
    public JavelinClient? ClientLogic { get; private set; }
    public JavelinServer? ServerLogic { get; private set; }

    IClientWeaponLogic? IHasWeaponLogic.ClientLogic => ClientLogic;
    IServerRangedWeaponLogic? IHasRangedWeaponLogic.ServerWeaponLogic => ServerLogic;

    public bool RenderingOffset { get; set; }

    public AnimationRequestByCode IdleAnimation { get; set; }
    public AnimationRequestByCode ReadyAnimation { get; set; }

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        if (api is ICoreClientAPI clientAPI)
        {
            ClientLogic = new(clientAPI, this);
            JavelinStats stats = Attributes.AsObject<JavelinStats>();
            RenderingOffset = stats.RenderingOffset;

            IdleAnimation = new(stats.IdleAnimation, 1, 1, "main", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false);
            ReadyAnimation = new(stats.ReadyAnimation, 1, 1, "main", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false);
        }

        if (api is ICoreServerAPI serverAPI)
        {
            ServerLogic = new(serverAPI, this);
        }
    }

    public override void OnHeldRenderOpaque(ItemSlot inSlot, IClientPlayer byPlayer)
    {
        base.OnHeldRenderOpaque(inSlot, byPlayer);

        if (AnimationsManager.RenderDebugColliders)
        {
            ClientLogic?.RenderDebugCollider(inSlot, byPlayer);
        }
    }

    public bool CanAttack(bool mainHand) => true;
    public bool CanBlock(bool mainHand) => false;

    public override void OnHeldAttackStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, ref EnumHandHandling handling)
    {
        handling = EnumHandHandling.PreventDefault;
    }

    public void BlockCallback(IServerPlayer player, ItemSlot slot, bool mainHand)
    {
        DamageItem(player.Entity.World, player.Entity, slot);
    }
}
