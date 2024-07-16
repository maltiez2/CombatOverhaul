using CombatOverhaul.Animations;
using CombatOverhaul.DamageSystems;
using CombatOverhaul.Inputs;
using CombatOverhaul.MeleeSystems;
using System.Numerics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using VSImGui.Debug;

namespace CombatOverhaul.Implementations;

public enum MeleeWeaponState
{
    Idle,
    WindingUp,
    Attacking,
    Parrying,
    Blocking,
    Cooldown
}

public enum MeleeWeaponStance
{
    MainHand,
    OffHand,
    TwoHanded
}

public interface IHasMeleeWeaponActions
{
    bool CanAttack(bool mainHand);
    bool CanBlock(bool mainHand);
}

public class StanceStats
{
    public bool CanAttack { get; set; } = true;
    public bool CanParry { get; set; } = true;
    public bool CanBlock { get; set; } = true;
    public bool CanSprint { get; set; } = true;
    public float SpeedPenalty { get; set; } = 0;
    public float BlockSpeedPenalty { get; set; } = 0;

    public float GripLengthFactor { get; set; } = 0;
    public float GripMinLength { get; set; } = 0;
    public float GripMaxLength { get; set; } = 0;

    public MeleeAttackStats? Attack { get; set; }
    public DamageBlockJson? Block { get; set; }
    public DamageBlockJson? Parry { get; set; }

    public float AttackCooldownMs { get; set; } = 0;
    public float BlockCooldownMs { get; set; } = 0;

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
    public bool RenderingOffset { get; set; } = false;
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
            //DebugEditColliders(OneHandedAttack, item.Id * 100 + 0);
        }
        if (Stats.TwoHandedStance?.Attack != null)
        {
            TwoHandedAttack = new(api, Stats.TwoHandedStance.Attack);
            //DebugEditColliders(TwoHandedAttack, item.Id * 100 + 1);
        }
        if (Stats.OffHandStance?.Attack != null)
        {
            OffHandAttack = new(api, Stats.OffHandStance.Attack);
            //DebugEditColliders(OffHandAttack, item.Id * 100 + 2);
        }
    }

    public int ItemId => Item.Id;

    public virtual DirectionsConfiguration DirectionsType => DirectionsConfiguration.Eight;

    public AnimationRequestByCode? GetIdleAnimation(bool mainHand)
    {
        EnsureStance(Api.World.Player.Entity, mainHand);
        return GetStance<MeleeWeaponStance>(mainHand) switch
        {
            MeleeWeaponStance.MainHand => Stats?.OneHandedStance?.IdleAnimation == null ? null : new(Stats.OneHandedStance.IdleAnimation, 1, 1, AnimationCategory(mainHand), TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false),
            MeleeWeaponStance.OffHand => Stats?.OffHandStance?.IdleAnimation == null ? null : new(Stats.OffHandStance.IdleAnimation, 1, 1, AnimationCategory(mainHand), TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false),
            MeleeWeaponStance.TwoHanded => Stats?.TwoHandedStance?.IdleAnimation == null ? null : new(Stats.TwoHandedStance.IdleAnimation, 1, 1, AnimationCategory(mainHand), TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false),
            _ => null
        };
    }
    public AnimationRequestByCode? GetReadyAnimation(bool mainHand)
    {
        EnsureStance(Api.World.Player.Entity, mainHand);
        return GetStance<MeleeWeaponStance>(mainHand) switch
        {
            MeleeWeaponStance.MainHand => Stats?.OneHandedStance?.ReadyAnimation == null ? null : new(Stats.OneHandedStance.ReadyAnimation, 1, 1, AnimationCategory(mainHand), TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false),
            MeleeWeaponStance.OffHand => Stats?.OffHandStance?.ReadyAnimation == null ? null : new(Stats.OffHandStance.ReadyAnimation, 1, 1, AnimationCategory(mainHand), TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false),
            MeleeWeaponStance.TwoHanded => Stats?.TwoHandedStance?.ReadyAnimation == null ? null : new(Stats.TwoHandedStance.ReadyAnimation, 1, 1, AnimationCategory(mainHand), TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false),
            _ => null,
        };
    }

    public virtual void OnSelected(ItemSlot slot, EntityPlayer player, bool mainHand, ref int state)
    {
        EnsureStance(player, mainHand);
        SetState(MeleeWeaponState.Idle, mainHand);
    }
    public virtual void OnDeselected(EntityPlayer player)
    {
        MeleeBlockSystem.StopBlock(true);
        MeleeBlockSystem.StopBlock(false);
        StopAttackCooldown(true);
        StopBlockCooldown(true);
        StopAttackCooldown(false);
        StopBlockCooldown(false);
        GripController?.ResetGrip(true);
        GripController?.ResetGrip(false);
    }
    public virtual void OnRegistered(ActionsManagerPlayerBehavior behavior, ICoreClientAPI api)
    {
        PlayerBehavior = behavior;
        AnimationBehavior = behavior.entity.GetBehavior<FirstPersonAnimationsBehavior>();
        GripController = new(AnimationBehavior);
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

    public virtual bool OnMouseWheel(ItemSlot slot, IClientPlayer byPlayer, float delta)
    {
        if (PlayerBehavior?._actionListener.IsActive(EnumEntityAction.RightMouseDown) == false) return false;

        bool mainHand = byPlayer.Entity.RightHandItemSlot == slot;
        StanceStats? stance = GetStanceStats(mainHand);
        float canChangeGrip = stance?.GripLengthFactor ?? 0;

        if (canChangeGrip != 0 && stance != null)
        {
            GripController?.ChangeGrip(delta, mainHand, canChangeGrip, stance.GripMinLength, stance.GripMaxLength);
            return true;
        }
        else
        {
            GripController?.ResetGrip(mainHand);
            return false;
        }
    }

    public bool CanAttack(bool mainHand = true) => GetStanceStats(mainHand)?.CanAttack ?? false;
    public bool CanBlock(bool mainHand = true) => GetStanceStats(mainHand)?.CanBlock ?? false;
    public bool CanParry(bool mainHand = true) => GetStanceStats(mainHand)?.CanParry ?? false;

    protected readonly Item Item;
    protected readonly ICoreClientAPI Api;
    protected readonly MeleeBlockSystemClient MeleeBlockSystem;
    protected FirstPersonAnimationsBehavior? AnimationBehavior;
    protected ActionsManagerPlayerBehavior? PlayerBehavior;
    private GripController? GripController;
    internal const int MaxStates = 100;
    protected readonly MeleeWeaponStats Stats;

    protected long MainHandAttackCooldownTimer = -1;
    protected long OffHandAttackCooldownTimer = -1;
    protected long MainHandBlockCooldownTimer = -1;
    protected long OffHandBlockCooldownTimer = -1;

    protected MeleeAttack? OneHandedAttack;
    protected MeleeAttack? TwoHandedAttack;
    protected MeleeAttack? OffHandAttack;

    [ActionEventHandler(EnumEntityAction.LeftMouseDown, ActionState.Active)]
    protected virtual bool Attack(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (eventData.AltPressed) return false;
        if (!mainHand && CanAttackWithOtherHand(player, mainHand)) return false;
        EnsureStance(player, mainHand);
        if (!CanAttack(mainHand)) return false;
        if (IsAttackOnCooldown(mainHand)) return false;

        if (GetState<MeleeWeaponState>(!mainHand) == MeleeWeaponState.Blocking) return false;

        MeleeAttack? attack = GetStanceAttack(mainHand);
        StanceStats? stats = GetStanceStats(mainHand);

        if (attack == null || stats == null) return false;

        switch (GetState<MeleeWeaponState>(mainHand))
        {
            case MeleeWeaponState.Idle:
                {
                    MeleeBlockSystem.StopBlock(mainHand);
                    SetState(MeleeWeaponState.WindingUp, mainHand);
                    attack.Start(player.Player);
                    AnimationBehavior?.Play(mainHand, stats.AttackAnimation, category: AnimationCategory(mainHand), callback: () => AttackAnimationCallback(mainHand), callbackHandler: code => AttackAnimationCallbackHandler(code, mainHand));
                }
                break;
            case MeleeWeaponState.WindingUp:
                break;
            case MeleeWeaponState.Cooldown:
                break;
            case MeleeWeaponState.Attacking:
                {
                    TryAttack(attack, stats, slot, player, mainHand);
                }
                break;
            default:
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
                //SetState(MeleeWeaponState.Cooldown, mainHand);
                break;
        }
    }

    [ActionEventHandler(EnumEntityAction.LeftMouseDown, ActionState.Released)]
    protected virtual bool StopAttack(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (!CheckState(mainHand, MeleeWeaponState.Attacking, MeleeWeaponState.WindingUp)) return false;

        AnimationBehavior?.PlayReadyAnimation(mainHand);
        SetState(MeleeWeaponState.Idle, mainHand);

        float cooldown = GetStanceStats(mainHand)?.AttackCooldownMs ?? 0;
        if (cooldown != 0)
        {
            StartAttackCooldown(mainHand, TimeSpan.FromMilliseconds(cooldown));
        }

        return true;
    }

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Active)]
    protected virtual bool Block(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (eventData.AltPressed) return false;
        if (!CheckState(mainHand, MeleeWeaponState.Idle, MeleeWeaponState.WindingUp, MeleeWeaponState.Attacking)) return false;
        if (IsBlockOnCooldown(mainHand)) return false;
        EnsureStance(player, mainHand);
        if (!CanBlock(mainHand) && !CanParry(mainHand)) return false;
        if (mainHand && CanBlockWithOtherHand(player, mainHand)) return false;

        if (GetState<MeleeWeaponState>(!mainHand) == MeleeWeaponState.Attacking) return false;

        StanceStats? stats = GetStanceStats(mainHand);
        DamageBlockJson? parryStats = stats?.Parry;
        DamageBlockJson? blockStats = stats?.Block;

        if (CanParry(mainHand) && parryStats != null && stats != null)
        {
            SetState(MeleeWeaponState.Parrying, mainHand);
            AnimationBehavior?.Play(mainHand, stats.BlockAnimation, category: AnimationCategory(mainHand), callback: () => BlockAnimationCallback(mainHand), callbackHandler: code => BlockAnimationCallbackHandler(code, mainHand));
        }
        else if (CanBlock(mainHand) && blockStats != null && stats != null)
        {
            SetState(MeleeWeaponState.Blocking, mainHand);
            MeleeBlockSystem.StartBlock(blockStats, mainHand);
            AnimationBehavior?.Play(mainHand, stats.BlockAnimation, category: AnimationCategory(mainHand), callback: () => BlockAnimationCallback(mainHand), callbackHandler: code => BlockAnimationCallbackHandler(code, mainHand));
        }

        return true;
    }
    protected virtual void BlockAnimationCallbackHandler(string callbackCode, bool mainHand)
    {
        switch (callbackCode)
        {
            case "startParry":
                {
                    StanceStats? stats = GetStanceStats(mainHand);
                    DamageBlockJson? parryStats = stats?.Parry;

                    if (CanParry(mainHand) && parryStats != null)
                    {
                        SetState(MeleeWeaponState.Parrying, mainHand);
                        MeleeBlockSystem.StartBlock(parryStats, mainHand);
                    }
                }
                break;
            case "stopParry":
                {
                    StanceStats? stats = GetStanceStats(mainHand);
                    DamageBlockJson? blockStats = stats?.Block;
                    if (CanBlock(mainHand) && blockStats != null)
                    {
                        SetState(MeleeWeaponState.Blocking, mainHand);
                        MeleeBlockSystem.StartBlock(blockStats, mainHand);
                    }
                    else
                    {
                        MeleeBlockSystem.StopBlock(mainHand);
                    }
                }
                break;
        }
    }
    protected virtual bool BlockAnimationCallback(bool mainHand)
    {
        if (!CheckState(mainHand, MeleeWeaponState.Parrying)) return true;

        SetState(MeleeWeaponState.Idle, mainHand);
        AnimationBehavior?.PlayReadyAnimation(mainHand);

        float cooldown = GetStanceStats(mainHand)?.BlockCooldownMs ?? 0;
        if (cooldown != 0)
        {
            StartBlockCooldown(mainHand, TimeSpan.FromMilliseconds(cooldown));
        }

        return true;
    }

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Released)]
    protected virtual bool StopBlock(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (!CheckState(mainHand, MeleeWeaponState.Blocking, MeleeWeaponState.Parrying)) return false;

        MeleeBlockSystem.StopBlock(mainHand);
        AnimationBehavior?.PlayReadyAnimation(mainHand);
        SetState(MeleeWeaponState.Idle, mainHand);

        float cooldown = GetStanceStats(mainHand)?.BlockCooldownMs ?? 0;
        if (cooldown != 0)
        {
            StartBlockCooldown(mainHand, TimeSpan.FromMilliseconds(cooldown));
        }

        return true;
    }

    protected virtual void EnsureStance(EntityPlayer player, bool mainHand)
    {
        MeleeWeaponStance currentStance = GetStance<MeleeWeaponStance>(mainHand);

        if (!mainHand)
        {
            SetStance(MeleeWeaponStance.OffHand, mainHand);
            if (currentStance != MeleeWeaponStance.OffHand)
            {
                AnimationBehavior?.PlayReadyAnimation(mainHand);
            }
            return;
        }

        bool offHandEmpty = CheckForOtherHandEmptyNoError(mainHand, player);
        if (offHandEmpty && Stats.TwoHandedStance != null)
        {
            SetStance(MeleeWeaponStance.TwoHanded, mainHand);
            if (currentStance != MeleeWeaponStance.TwoHanded)
            {
                AnimationBehavior?.PlayReadyAnimation(mainHand);
            }
        }
        else
        {
            SetStance(MeleeWeaponStance.MainHand, mainHand);
            if (currentStance != MeleeWeaponStance.MainHand)
            {
                AnimationBehavior?.PlayReadyAnimation(mainHand);
            }
        }
    }

    protected virtual void StartAttackCooldown(bool mainHand, TimeSpan time)
    {
        StopAttackCooldown(mainHand);

        if (mainHand)
        {
            MainHandAttackCooldownTimer = Api.World.RegisterCallback(_ => MainHandAttackCooldownTimer = -1, (int)time.TotalMilliseconds);
        }
        else
        {
            OffHandAttackCooldownTimer = Api.World.RegisterCallback(_ => OffHandAttackCooldownTimer = -1, (int)time.TotalMilliseconds);
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

    protected virtual void StartBlockCooldown(bool mainHand, TimeSpan time)
    {
        StopBlockCooldown(mainHand);

        if (mainHand)
        {
            MainHandBlockCooldownTimer = Api.World.RegisterCallback(_ => MainHandBlockCooldownTimer = -1, (int)time.TotalMilliseconds);
        }
        else
        {
            OffHandBlockCooldownTimer = Api.World.RegisterCallback(_ => OffHandBlockCooldownTimer = -1, (int)time.TotalMilliseconds);
        }
    }
    protected virtual void StopBlockCooldown(bool mainHand)
    {
        if (mainHand)
        {
            if (MainHandBlockCooldownTimer != -1)
            {
                Api.World.UnregisterCallback(MainHandBlockCooldownTimer);
                MainHandBlockCooldownTimer = -1;
            }
        }
        else
        {
            if (OffHandBlockCooldownTimer != -1)
            {
                Api.World.UnregisterCallback(OffHandBlockCooldownTimer);
                OffHandBlockCooldownTimer = -1;
            }
        }
    }
    protected virtual bool IsBlockOnCooldown(bool mainHand) => mainHand ? MainHandBlockCooldownTimer != -1 : OffHandBlockCooldownTimer != -1;

    protected string AnimationCategory(bool mainHand = true) => mainHand ? "main" : "mainOffhand";

    protected MeleeAttack? GetStanceAttack(bool mainHand = true)
    {
        MeleeWeaponStance stance = GetStance<MeleeWeaponStance>(mainHand);
        return stance switch
        {
            MeleeWeaponStance.MainHand => OneHandedAttack,
            MeleeWeaponStance.OffHand => OffHandAttack,
            MeleeWeaponStance.TwoHanded => TwoHandedAttack,
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
    protected bool CanAttackWithOtherHand(EntityPlayer player, bool mainHand = true)
    {
        ItemSlot otherHandSlot = mainHand ? player.LeftHandItemSlot : player.RightHandItemSlot;
        return (otherHandSlot.Itemstack?.Item as IHasMeleeWeaponActions)?.CanAttack(!mainHand) ?? false;
    }
    protected bool CanBlockWithOtherHand(EntityPlayer player, bool mainHand = true)
    {
        ItemSlot otherHandSlot = mainHand ? player.LeftHandItemSlot : player.RightHandItemSlot;
        return (otherHandSlot.Itemstack?.Item as IHasMeleeWeaponActions)?.CanBlock(!mainHand) ?? false;
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
    protected bool CheckForOtherHandEmptyNoError(bool mainHand, EntityPlayer player)
    {
        if (mainHand && !player.LeftHandItemSlot.Empty)
        {
            return false;
        }

        if (!mainHand && !player.RightHandItemSlot.Empty)
        {
            return false;
        }

        return true;
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

public class MeleeWeapon : Item, IHasWeaponLogic, IHasDynamicIdleAnimations, IHasMeleeWeaponActions, IHasServerBlockCallback, ISetsRenderingOffset, IMouseWheelInput
{
    public MeleeWeaponClient? ClientLogic { get; private set; }

    IClientWeaponLogic? IHasWeaponLogic.ClientLogic => ClientLogic;

    public bool RenderingOffset { get; set; }

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        if (api is ICoreClientAPI clientAPI)
        {
            ClientLogic = new(clientAPI, this);
            MeleeWeaponStats Stats = Attributes.AsObject<MeleeWeaponStats>();
            RenderingOffset = Stats.RenderingOffset;
        }
    }

    public AnimationRequestByCode? GetIdleAnimation(bool mainHand) => ClientLogic?.GetIdleAnimation(mainHand);
    public AnimationRequestByCode? GetReadyAnimation(bool mainHand) => ClientLogic?.GetReadyAnimation(mainHand);

    public override void OnHeldRenderOpaque(ItemSlot inSlot, IClientPlayer byPlayer)
    {
        base.OnHeldRenderOpaque(inSlot, byPlayer);

        if (AnimationsManager.RenderDebugColliders)
        {
            ClientLogic?.RenderDebugCollider(inSlot, byPlayer);
        }
    }

    public bool CanAttack(bool mainHand) => ClientLogic?.CanAttack(mainHand) ?? false;
    public bool CanBlock(bool mainHand) => (ClientLogic?.CanBlock(mainHand) ?? false) || (ClientLogic?.CanParry(mainHand) ?? false);

    public override void OnHeldAttackStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, ref EnumHandHandling handling)
    {
        handling = EnumHandHandling.PreventDefault;
    }

    public void BlockCallback(IServerPlayer player, ItemSlot slot, bool mainHand)
    {
        DamageItem(player.Entity.World, player.Entity, slot);
    }

    public bool OnMouseWheel(ItemSlot slot, IClientPlayer byPlayer, float delta) => ClientLogic?.OnMouseWheel(slot, byPlayer, delta) ?? false;
}

public interface IMouseWheelInput
{
    bool OnMouseWheel(ItemSlot slot, IClientPlayer byPlayer, float delta);
}

public sealed class GripController
{
    public GripController(FirstPersonAnimationsBehavior? animationBehavior)
    {
        _animationBehavior = animationBehavior;
    }


    public void ChangeGrip(float delta, bool mainHand, float gripFactor, float min, float max)
    {
        _grip = GameMath.Clamp(_grip + delta * gripFactor, min, max);

        PlayAnimation(mainHand);

    }
    public void ResetGrip(bool mainHand)
    {
        _grip = 0;

        PlayAnimation(mainHand);
    }

    private float _grip = 0;
    private readonly Animations.Animation _gripAnimation = Animations.Animation.Zero;
    private readonly FirstPersonAnimationsBehavior? _animationBehavior;

    private PLayerKeyFrame GetAimingFrame()
    {
        AnimationElement element = new(_grip, null, null, null, null, null);
        AnimationElement nullElement = new(null, null, null, null, null, null);

        PlayerFrame frame = new(rightHand: new(element, nullElement, nullElement));

        return new PLayerKeyFrame(frame, TimeSpan.Zero, EasingFunctionType.Linear);
    }
    private void PlayAnimation(bool mainHand)
    {
        _gripAnimation.PlayerKeyFrames[0] = GetAimingFrame();
        _gripAnimation.Hold = true;

        AnimationRequest request = new(_gripAnimation, 1.0f, 0, "grip", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), true);
        _animationBehavior?.Play(request, mainHand);
    }
}
