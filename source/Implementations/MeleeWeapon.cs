using CombatOverhaul.Animations;
using CombatOverhaul.DamageSystems;
using CombatOverhaul.Inputs;
using CombatOverhaul.MeleeSystems;
using ImPlotNET;
using System;
using System.Numerics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VSImGui.Debug;

namespace CombatOverhaul.Implementations;

public enum MeleeWeaponState
{
    Idle,
    WindingUp,
    Attacking,
    Parrying,
    Blocking
}

public enum MeleeWeaponStance
{
    MainHand,
    OffHand,
    TwoHanded
}

public class StanceStats
{
    public bool CanAttack { get; set; } = true;
    public bool CanParry { get; set; } = true;
    public bool CanBlock { get; set; } = true;
    public bool CanSprint { get; set; } = true;
    public float SpeedPenalty { get; set; } = 0;

    public MeleeAttackStats? Attack { get; set; }
    public DamageBlockJson? Block { get; set; }
    public DamageBlockJson? Parry { get; set; }

    public string AttackAnimation { get; set; } = "";
    public string BlockAnimation { get; set; } = "";
    public string ReadyAnimation { get; set; } = "";
    public string IdleAnimation { get; set; } = "";
}

public class MeleeWeaponStats : WeaponStats
{
    public StanceStats? OneHandedStance { get; set; } = null;
    public StanceStats? TwoHandedStance { get; set; } = null;
    public StanceStats? OffHandStance { get; set; } = null;
}

public class MeleeWeaponClient : IClientWeaponLogic, IHasDynamicIdleAnimations
{
    public MeleeWeaponClient(ICoreClientAPI api, Item item)
    {
        Item = item;
        Api = api;

        CombatOverhaulSystem system = api.ModLoader.GetModSystem<CombatOverhaulSystem>();
        MeleeBlockSystem = system.ClientBlockSystem ?? throw new Exception();

        Stats = item.Attributes.AsObject<MeleeWeaponStats>();

        if (Stats.OneHandedStance?.Attack != null)
        {
            OneHandedAttack = new(api, Stats.OneHandedStance.Attack);
            DebugEditColliders(OneHandedAttack, item.Id * 100 + 0);
        }
        if (Stats.TwoHandedStance?.Attack != null)
        {
            TwoHandedAttack = new(api, Stats.TwoHandedStance.Attack);
            DebugEditColliders(TwoHandedAttack, item.Id * 100 + 1);
        }
        if (Stats.OffHandStance?.Attack != null)
        {
            OffHandAttack = new(api, Stats.OffHandStance.Attack);
            DebugEditColliders(OffHandAttack, item.Id * 100 + 2);
        }
    }

    public int ItemId => Item.Id;

    public virtual DirectionsConfiguration DirectionsType => DirectionsConfiguration.None;

    public AnimationRequestByCode GetIdleAnimation(bool mainHand)
    {
        return GetStance<MeleeWeaponStance>(mainHand) switch
        {
            MeleeWeaponStance.MainHand => new(Stats?.OneHandedStance?.IdleAnimation ?? "", 1, 1, "main", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false),
            MeleeWeaponStance.OffHand => new(Stats?.OffHandStance?.IdleAnimation ?? "", 1, 1, "main", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false),
            MeleeWeaponStance.TwoHanded => new(Stats?.TwoHandedStance?.IdleAnimation ?? "", 1, 1, "main", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false),
            _ => new(Stats?.OneHandedStance?.IdleAnimation ?? "", 1, 1, "main", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false),
        };
    }
    public AnimationRequestByCode GetReadyAnimation(bool mainHand)
    {
        return GetStance<MeleeWeaponStance>(mainHand) switch
        {
            MeleeWeaponStance.MainHand => new(Stats?.OneHandedStance?.ReadyAnimation ?? "", 1, 1, "main", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false),
            MeleeWeaponStance.OffHand => new(Stats?.OffHandStance?.ReadyAnimation ?? "", 1, 1, "main", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false),
            MeleeWeaponStance.TwoHanded => new(Stats?.TwoHandedStance?.ReadyAnimation ?? "", 1, 1, "main", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false),
            _ => new(Stats?.OneHandedStance?.IdleAnimation ?? "", 1, 1, "main", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false),
        };
    }

    public virtual void OnSelected(ItemSlot slot, EntityPlayer player, bool mainHand, ref int state)
    {

    }
    public virtual void OnDeselected(EntityPlayer player)
    {

    }
    public virtual void OnRegistered(ActionsManagerPlayerBehavior behavior, ICoreClientAPI api)
    {
        PlayerBehavior = behavior;
        AnimationBehavior = behavior.entity.GetBehavior<FirstPersonAnimationsBehavior>();
    }

    public virtual void RenderDebugCollider(ItemSlot inSlot, IClientPlayer byPlayer)
    {
        MeleeAttack? attack = GetStanceAttack(mainHand: true);
        if (attack == null) return;

        foreach (MeleeDamageType damageType in attack.DamageTypes)
        {
            damageType.RelativeCollider.Transform(byPlayer.Entity.Pos, byPlayer.Entity, inSlot, Api, right: true)?.Render(Api, byPlayer.Entity);
        }
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

    protected readonly Item Item;
    protected readonly ICoreClientAPI Api;
    protected readonly MeleeBlockSystemClient MeleeBlockSystem;
    protected FirstPersonAnimationsBehavior? AnimationBehavior;
    protected ActionsManagerPlayerBehavior? PlayerBehavior;
    protected const int MaxStates = 100;
    protected readonly MeleeWeaponStats Stats;

    protected MeleeAttack? OneHandedAttack;
    protected MeleeAttack? TwoHandedAttack;
    protected MeleeAttack? OffHandAttack;

    [ActionEventHandler(EnumEntityAction.LeftMouseDown, ActionState.Active)]
    protected virtual bool Attack(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (eventData.AltPressed) return false;
        if (!CanAttack(mainHand)) return false;

        MeleeAttack? attack = GetStanceAttack(mainHand);
        StanceStats? stats = GetStanceStats(mainHand);

        if (attack == null || stats == null) return false;

        switch (GetState<MeleeWeaponState>(mainHand))
        {
            case MeleeWeaponState.Idle:
                {
                    SetState(MeleeWeaponState.WindingUp, mainHand);
                    attack.Start(player.Player);
                    AnimationBehavior?.Play(mainHand, stats.AttackAnimation, callback: () => AttackAnimationCallback(mainHand), callbackHandler: code => AttackAnimationCallbackHandler(code, mainHand));
                }
                break;
            case MeleeWeaponState.WindingUp:
                break;
            case MeleeWeaponState.Attacking:
                {
                    TryAttack(attack, stats, slot, player, mainHand);
                }
                break;
            case MeleeWeaponState.Parrying:
                return false;
            case MeleeWeaponState.Blocking:
                return false;
        }

        return true;
    }
    protected virtual void TryAttack(MeleeAttack attack, StanceStats stats, ItemSlot slot, EntityPlayer player, bool mainHand)
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
        SetState(MeleeWeaponState.Idle, mainHand);

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
        //AnimationBehavior?.PlayReadyAnimation(mainHand);
        //SetState(MeleeWeaponState.Idle, mainHand);
        return true;
    }

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Active)]
    protected virtual bool Block(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (eventData.AltPressed) return false;
        if (!CanBlock(mainHand) && !CanParry(mainHand)) return false;

        return true;
    }
    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Released)]
    protected virtual bool StopBlock(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        MeleeBlockSystem.StopBlock(mainHand);
        AnimationBehavior?.PlayReadyAnimation(mainHand);
        return true;
    }

    [ActionEventHandler(EnumEntityAction.LeftMouseDown, ActionState.Pressed)]
    protected virtual bool ChangeStance(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (!mainHand || !eventData.AltPressed) return false;

        MeleeWeaponStance stance = GetStance<MeleeWeaponStance>(mainHand);
        switch (stance)
        {
            case MeleeWeaponStance.MainHand:
                if (Stats.TwoHandedStance != null && CheckForOtherHandEmpty(mainHand, player))
                {
                    SetStance(MeleeWeaponStance.MainHand, mainHand);
                    AnimationBehavior?.PlayReadyAnimation(mainHand);
                    return true;
                }
                break;
            case MeleeWeaponStance.TwoHanded:
                if (Stats.OneHandedStance != null)
                {
                    SetStance(MeleeWeaponStance.MainHand, mainHand);
                    AnimationBehavior?.PlayReadyAnimation(mainHand);
                    return true;
                }
                break;
        }

        return false;
    }

    protected MeleeAttack? GetStanceAttack(bool mainHand = true)
    {
        MeleeWeaponStance stance = GetStance<MeleeWeaponStance>(mainHand);
        return stance switch
        {
            MeleeWeaponStance.MainHand => OneHandedAttack,
            MeleeWeaponStance.OffHand => TwoHandedAttack,
            MeleeWeaponStance.TwoHanded => OffHandAttack,
            _ => OneHandedAttack,
        };
    }
    protected StanceStats? GetStanceStats(bool mainHand = true)
    {
        MeleeWeaponStance stance = GetStance<MeleeWeaponStance>(mainHand);
        return stance switch
        {
            MeleeWeaponStance.MainHand => Stats.OneHandedStance,
            MeleeWeaponStance.OffHand => Stats.OffHandStance,
            MeleeWeaponStance.TwoHanded => Stats.TwoHandedStance,
            _ => Stats.OneHandedStance,
        };
    }
    protected bool CanAttack(bool mainHand = true) => GetStanceStats(mainHand)?.CanAttack ?? false;
    protected bool CanBlock(bool mainHand = true) => GetStanceStats(mainHand)?.CanBlock ?? false;
    protected bool CanParry(bool mainHand = true) => GetStanceStats(mainHand)?.CanParry ?? false;
    protected static bool CheckState<TState>(int state, params TState[] statesToCheck)
        where TState : struct, Enum
    {
        return statesToCheck.Contains((TState)Enum.ToObject(typeof(TState), state % MaxStates));
    }
    protected bool CheckState<TState>(bool mainHand, params TState[] statesToCheck)
        where TState : struct, Enum
    {
        return statesToCheck.Contains((TState)Enum.ToObject(typeof(TState), PlayerBehavior?.GetState(mainHand) % MaxStates ?? 0));
    }
    protected void SetStance<TStance>(TStance stance, bool mainHand = true)
        where TStance : struct, Enum
    {
        int stateCombined = PlayerBehavior?.GetState(mainHand) ?? 0;
        int stateInt = stateCombined % MaxStates;
        int stanceInt = (int)Enum.ToObject(typeof(TStance), stance);
        stateCombined = stateInt + MaxStates * stanceInt;

        PlayerBehavior?.SetState(stateCombined, mainHand);
    }
    protected void SetState<TState>(TState state, bool mainHand = true)
        where TState : struct, Enum
    {
        int stateCombined = PlayerBehavior?.GetState(mainHand) ?? 0;
        int stanceInt = stateCombined / MaxStates;
        int stateInt = (int)Enum.ToObject(typeof(TState), state);
        stateCombined = stateInt + MaxStates * stanceInt;

        PlayerBehavior?.SetState(stateCombined, mainHand);
    }
    protected void SetStateAndStance<TState, TStance>(TState state, TStance stance, bool mainHand = true)
        where TState : struct, Enum
        where TStance : struct, Enum
    {
        int stateInt = (int)Enum.ToObject(typeof(TState), state);
        int stanceInt = (int)Enum.ToObject(typeof(TStance), stance);
        int stateCombined = stateInt + MaxStates * stanceInt;

        PlayerBehavior?.SetState(stateCombined, mainHand);
    }
    protected TState GetState<TState>(bool mainHand = true)
        where TState : struct, Enum
    {
        return (TState)Enum.ToObject(typeof(TState), PlayerBehavior?.GetState(mainHand) % MaxStates ?? 0);
    }
    protected TStance GetStance<TStance>(bool mainHand = true)
        where TStance : struct, Enum
    {
        return (TStance)Enum.ToObject(typeof(TStance), PlayerBehavior?.GetState(mainHand) / MaxStates ?? 0);
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

public class MeleeWeapon : Item, IHasWeaponLogic, IHasDynamicIdleAnimations
{
    public MeleeWeaponClient? ClientLogic { get; private set; }

    IClientWeaponLogic? IHasWeaponLogic.ClientLogic => ClientLogic;

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        if (api is ICoreClientAPI clientAPI)
        {
            ClientLogic = new(clientAPI, this);
        }
    }

    public AnimationRequestByCode GetIdleAnimation(bool mainHand) => ((IHasDynamicIdleAnimations)ClientLogic).GetIdleAnimation(mainHand);
    public AnimationRequestByCode GetReadyAnimation(bool mainHand) => ((IHasDynamicIdleAnimations)ClientLogic).GetReadyAnimation(mainHand);

    public override void OnHeldRenderOpaque(ItemSlot inSlot, IClientPlayer byPlayer)
    {
        base.OnHeldRenderOpaque(inSlot, byPlayer);

        if (AnimationsManager.RenderDebugColliders)
        {
            ClientLogic?.RenderDebugCollider(inSlot, byPlayer);
        }
    }
}
