using CombatOverhaul.Animations;
using CombatOverhaul.DamageSystems;
using CombatOverhaul.Inputs;
using CombatOverhaul.MeleeSystems;
using CombatOverhaul.RangedSystems;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

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

public class MeleeWeaponClient : IClientWeaponLogic
{
    public MeleeWeaponClient(ICoreClientAPI api, Item item)
    {
        CombatOverhaulSystem system = api.ModLoader.GetModSystem<CombatOverhaulSystem>();
        MeleeBlockSystem = system.ClientBlockSystem ?? throw new Exception();

        Stats = item.Attributes.AsObject<MeleeWeaponStats>();

        Item = item;
        Api = api;
    }

    public int ItemId => Item.Id;

    public virtual DirectionsConfiguration DirectionsType => DirectionsConfiguration.None;

    public AnimationRequestByCode IdleAnimation { get; set; }
    public AnimationRequestByCode ReadyAnimation { get; set; }

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
    protected readonly MeleeBlockSystemClient MeleeBlockSystem;
    protected FirstPersonAnimationsBehavior? AnimationBehavior;
    protected ActionsManagerPlayerBehavior? PlayerBehavior;
    protected const int MaxStates = 100;
    protected readonly MeleeWeaponStats Stats;

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Active)]
    protected virtual bool Attack(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (eventData.AltPressed) return false;
        if (!CanAttack(mainHand)) return false;

        return true;
    }
    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Released)]
    protected virtual bool StopAttack(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        return true;
    }

    [ActionEventHandler(EnumEntityAction.LeftMouseDown, ActionState.Active)]
    protected virtual bool Block(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (eventData.AltPressed) return false;
        if (!CanBlock(mainHand) && !CanParry(mainHand)) return false;

        return true;
    }
    [ActionEventHandler(EnumEntityAction.LeftMouseDown, ActionState.Released)]
    protected virtual bool StopBlock(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        return true;
    }

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Pressed)]
    protected virtual bool ChangeStance(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (!eventData.AltPressed) return false;

        return true;
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

public class MeleeWeapon : Item, IHasWeaponLogic, IHasIdleAnimations
{
    public MeleeWeaponClient? ClientLogic { get; private set; }

    IClientWeaponLogic? IHasWeaponLogic.ClientLogic => ClientLogic;

    public AnimationRequestByCode IdleAnimation => ClientLogic?.IdleAnimation ?? new("idle", 1, 1, "main", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false);
    public AnimationRequestByCode ReadyAnimation => ClientLogic?.ReadyAnimation ?? new("ready", 1, 1, "main", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false);

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        if (api is ICoreClientAPI clientAPI)
        {
            ClientLogic = new(clientAPI, this);
        }
    }
}
