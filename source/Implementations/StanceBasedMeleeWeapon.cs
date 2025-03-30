using CombatOverhaul.Animations;
using CombatOverhaul.DamageSystems;
using CombatOverhaul.Inputs;
using CombatOverhaul.Integration;
using CombatOverhaul.MeleeSystems;
using OpenTK.Mathematics;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace CombatOverhaul.Implementations;

public enum StanceBasedMeleeWeaponState
{
    Idle,
    WindUp,
    Attack,
    Cooldown
}

public enum GripType
{
    OneHanded,
    TwoHanded,
    OffHanded
}

public class StanceBasedMeleeWeaponAttackStats : MeleeAttackStats
{
    public DamageBlockJson? Parry { get; set; }
    public MeleeAttackStats? HandleAttack { get; set; }
    public string? AttackHitSound { get; set; } = null;
    public string? HandleHitSound { get; set; } = null;
}

public class StanceBasedMeleeWeaponGripStats
{
    public float GripLengthFactor { get; set; } = 0;
    public float GripMinLength { get; set; } = 0;
    public float GripMaxLength { get; set; } = 0;

    public string AttackDirectionsType { get; set; } = "None";
    public string InitialStance { get; set; } = "Top";

    public StanceBasedMeleeWeaponAttackStats? DefaultRightClickAttack { get; set; } = null;
    public Dictionary<string, StanceBasedMeleeWeaponAttackStats> StanceToStanceRightClickAttacks { get; set; } = new();
    public StanceBasedMeleeWeaponAttackStats? DefaultLeftClickAttack { get; set; } = null;
    public Dictionary<string, StanceBasedMeleeWeaponAttackStats> StanceToStanceLeftClickAttacks { get; set; } = new();
    public DamageBlockJson? DefaultBlock { get; set; } = null;
    public Dictionary<string, DamageBlockJson> BlockByStance { get; set; } = new();
    public Dictionary<string, string> StanceAnimations { get; set; } = new();
    public Dictionary<string, string[]> RightClickAttacksAnimations { get; set; } = new();
    public Dictionary<string, string[]> LeftClickAttacksAnimations { get; set; } = new();
}

public class StanceBasedMeleeWeaponStats
{
    public string ProficiencyStat { get; set; } = "";

    public StanceBasedMeleeWeaponGripStats? OneHanded { get; set; } = null;
    public StanceBasedMeleeWeaponGripStats? TwoHanded { get; set; } = null;
    public StanceBasedMeleeWeaponGripStats? OffHand { get; set; } = null;

    public bool RenderingOffset { get; set; } = false;
    public float AnimationStaggerOnHitDurationMs { get; set; } = 100;
}

public class GripSpecificStats
{
    public GripSpecificStats(ICoreClientAPI api, Item item, StanceBasedMeleeWeaponGripStats stats)
    {
        Stats = stats;

        LeftClickAttacks = new();
        LeftClickHandleAttacks = new();
        LeftClickParries = new();
        LeftClickAttackStats = new();
        LeftClickAttacksAnimations = new();
        RightClickAttacks = new();
        RightClickHandleAttacks = new();
        RightClickParries = new();
        RightClickAttackStats = new();
        RightClickAttacksAnimations = new();
        Blocks = new();

        DirectionsType = Enum.Parse<DirectionsConfiguration>(stats.AttackDirectionsType);
        InitialStance = Enum.Parse<AttackDirection>(stats.InitialStance);

        IEnumerable<AttackDirection> directions = DirectionController.Configurations[DirectionsType].Select(element => (AttackDirection)element);

        foreach (AttackDirection fromStance in directions)
        {
            foreach (AttackDirection toStance in directions)
            {
                string attackCode = $"{fromStance}-{toStance}";

                if (stats.StanceToStanceLeftClickAttacks.ContainsKey(attackCode))
                {
                    LeftClickAttacks.Add((fromStance, toStance), new(api, stats.StanceToStanceLeftClickAttacks[attackCode]));
                    LeftClickHandleAttacks.Add((fromStance, toStance), stats.StanceToStanceLeftClickAttacks[attackCode].HandleAttack == null ? null : new(api, stats.StanceToStanceLeftClickAttacks[attackCode].HandleAttack));
                    LeftClickParries.Add((fromStance, toStance), stats.StanceToStanceLeftClickAttacks[attackCode].Parry);
                    LeftClickAttackStats.Add((fromStance, toStance), stats.StanceToStanceLeftClickAttacks[attackCode]);
                    LeftClickAttacksAnimations.Add((fromStance, toStance), stats.LeftClickAttacksAnimations[attackCode]);

                    RegisterCollider(item.Code.ToString(), $"left-click-{fromStance}-{toStance}", LeftClickAttacks[(fromStance, toStance)]);
                    if (LeftClickHandleAttacks[(fromStance, toStance)] != null) RegisterCollider(item.Code.ToString(), $"left-click-handle-{fromStance}-{toStance}", LeftClickHandleAttacks[(fromStance, toStance)]);
                }
                else
                {
                    LeftClickAttacks.Add((fromStance, toStance), new(api, stats.DefaultLeftClickAttack));
                    LeftClickHandleAttacks.Add((fromStance, toStance), stats.DefaultLeftClickAttack.HandleAttack == null ? null : new(api, stats.DefaultLeftClickAttack.HandleAttack));
                    LeftClickParries.Add((fromStance, toStance), stats.DefaultLeftClickAttack.Parry);
                    LeftClickAttackStats.Add((fromStance, toStance), stats.DefaultLeftClickAttack);
                    LeftClickAttacksAnimations.Add((fromStance, toStance), stats.LeftClickAttacksAnimations[attackCode]);

                    RegisterCollider(item.Code.ToString(), $"left-click-{fromStance}-{toStance}", LeftClickAttacks[(fromStance, toStance)]);
                    if (LeftClickHandleAttacks[(fromStance, toStance)] != null) RegisterCollider(item.Code.ToString(), $"left-click-handle-{fromStance}-{toStance}", LeftClickHandleAttacks[(fromStance, toStance)]);
                }

                if (stats.StanceToStanceRightClickAttacks.ContainsKey(attackCode))
                {
                    RightClickAttacks.Add((fromStance, toStance), new(api, stats.StanceToStanceRightClickAttacks[attackCode]));
                    RightClickHandleAttacks.Add((fromStance, toStance), stats.StanceToStanceRightClickAttacks[attackCode].HandleAttack == null ? null : new(api, stats.StanceToStanceRightClickAttacks[attackCode].HandleAttack));
                    RightClickParries.Add((fromStance, toStance), stats.StanceToStanceRightClickAttacks[attackCode].Parry);
                    RightClickAttackStats.Add((fromStance, toStance), stats.StanceToStanceRightClickAttacks[attackCode]);
                    RightClickAttacksAnimations.Add((fromStance, toStance), stats.RightClickAttacksAnimations[attackCode]);

                    RegisterCollider(item.Code.ToString(), $"right-click-{fromStance}-{toStance}", RightClickAttacks[(fromStance, toStance)]);
                    if (RightClickHandleAttacks[(fromStance, toStance)] != null) RegisterCollider(item.Code.ToString(), $"right-click-handle-{fromStance}-{toStance}", RightClickHandleAttacks[(fromStance, toStance)]);
                }
                else
                {
                    RightClickAttacks.Add((fromStance, toStance), new(api, stats.DefaultRightClickAttack));
                    RightClickHandleAttacks.Add((fromStance, toStance), stats.DefaultRightClickAttack.HandleAttack == null ? null : new(api, stats.DefaultRightClickAttack.HandleAttack));
                    RightClickParries.Add((fromStance, toStance), stats.DefaultRightClickAttack.Parry);
                    RightClickAttackStats.Add((fromStance, toStance), stats.DefaultRightClickAttack);
                    RightClickAttacksAnimations.Add((fromStance, toStance), stats.RightClickAttacksAnimations[attackCode]);

                    RegisterCollider(item.Code.ToString(), $"right-click-{fromStance}-{toStance}", RightClickAttacks[(fromStance, toStance)]);
                    if (RightClickHandleAttacks[(fromStance, toStance)] != null) RegisterCollider(item.Code.ToString(), $"right-click-handle-{fromStance}-{toStance}", RightClickHandleAttacks[(fromStance, toStance)]);
                }
            }
        }

        foreach (AttackDirection fromStance in Enum.GetValues<AttackDirection>())
        {
            if (stats.BlockByStance.ContainsKey(fromStance.ToString()))
            {
                Blocks.Add(fromStance, stats.BlockByStance[fromStance.ToString()]);
            }
            else
            {
                Blocks.Add(fromStance, stats.DefaultBlock);
            }
        }
    }

    public virtual DirectionsConfiguration DirectionsType { get; protected set; } = DirectionsConfiguration.None;

    public GripController? GripController;

    public Dictionary<(AttackDirection, AttackDirection), MeleeAttack> RightClickAttacks { get; }
    public Dictionary<(AttackDirection, AttackDirection), MeleeAttack?> RightClickHandleAttacks { get; }
    public Dictionary<(AttackDirection, AttackDirection), DamageBlockJson?> RightClickParries { get; }
    public Dictionary<(AttackDirection, AttackDirection), StanceBasedMeleeWeaponAttackStats> RightClickAttackStats { get; }
    public Dictionary<(AttackDirection, AttackDirection), string[]> RightClickAttacksAnimations { get; }
    public Dictionary<(AttackDirection, AttackDirection), MeleeAttack> LeftClickAttacks { get; }
    public Dictionary<(AttackDirection, AttackDirection), MeleeAttack?> LeftClickHandleAttacks { get; }
    public Dictionary<(AttackDirection, AttackDirection), DamageBlockJson?> LeftClickParries { get; }
    public Dictionary<(AttackDirection, AttackDirection), StanceBasedMeleeWeaponAttackStats> LeftClickAttackStats { get; }
    public Dictionary<(AttackDirection, AttackDirection), string[]> LeftClickAttacksAnimations { get; }
    public Dictionary<AttackDirection, DamageBlockJson?> Blocks { get; }
    public AttackDirection InitialStance { get; }

    public bool ParryButtonReleased { get; set; } = true;
    public int AttackCounter { get; set; } = 0;
    public bool HandleHitTerrain { get; set; } = false;
    public AttackDirection CurrentStance { get; set; } = AttackDirection.Top;
    public MeleeAttack? CurrentAttack { get; set; } = null;
    public MeleeAttack? CurrentHandleAttack { get; set; } = null;
    public StanceBasedMeleeWeaponAttackStats? CurrentAttackStats { get; set; } = null;

    public StanceBasedMeleeWeaponGripStats Stats { get; set; }

    protected static void RegisterCollider(string item, string type, MeleeAttack attack)
    {
#if DEBUG
        int typeIndex = 0;
        foreach (MeleeDamageType damageType in attack.DamageTypes)
        {
            DebugWindowManager.RegisterCollider(item, type + typeIndex++, damageType);
        }
#endif
    }
}

public class StanceBasedMeleeWeaponClient : IClientWeaponLogic, IHasDynamicIdleAnimations, IOnGameTick, IRestrictAction
{
    public StanceBasedMeleeWeaponClient(ICoreClientAPI api, Item item)
    {
        Item = item;
        Api = api;

        CombatOverhaulSystem system = api.ModLoader.GetModSystem<CombatOverhaulSystem>();
        MeleeBlockSystem = system.ClientBlockSystem ?? throw new Exception();
        SoundsSystem = system.ClientSoundsSynchronizer ?? throw new Exception();
        Settings = system.Settings;

        Stats = item.Attributes.AsObject<StanceBasedMeleeWeaponStats>();

        if (Stats.OneHanded != null) OneHandedStats = new(api, item, Stats.OneHanded);
        if (Stats.TwoHanded != null) TwoHandedStats = new(api, item, Stats.TwoHanded);
        if (Stats.OffHand != null) OffHandStats = new(api, item, Stats.OffHand);
    }

    public int ItemId => Item.Id;

    public virtual DirectionsConfiguration DirectionsType { get; protected set; } = DirectionsConfiguration.None;

    public AnimationRequestByCode? GetIdleAnimation(bool mainHand)
    {
        GripSpecificStats? stats = GetGripSpecificStats(mainHand, PlayerBehavior?.entity as EntityPlayer);

        string animation = stats?.Stats.StanceAnimations[stats.Stats.InitialStance] ?? "";

        return new(animation, 1, 1, AnimationCategory(mainHand), TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false);
    }
    public AnimationRequestByCode? GetReadyAnimation(bool mainHand)
    {
        GripSpecificStats? stats = GetGripSpecificStats(mainHand, PlayerBehavior?.entity as EntityPlayer);

        string animation = stats?.Stats.StanceAnimations[stats.Stats.InitialStance] ?? "";

        return new(animation, 1, 1, AnimationCategory(mainHand), TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false);
    }

    public virtual void OnSelected(ItemSlot slot, EntityPlayer player, bool mainHand, ref int state)
    {
        GripSpecificStats? stats = GetGripSpecificStats(mainHand, player);

        if (stats == null)
        {
            return;
        }

        stats.CurrentStance = stats.InitialStance;
        DirectionsType = stats.DirectionsType;

        AnimationBehavior?.Play(
            mainHand,
            stats.Stats.StanceAnimations[stats.CurrentStance.ToString()],
            animationSpeed: GetAnimationSpeed(player, Stats.ProficiencyStat),
            category: AnimationCategory(mainHand),
            callbackHandler: (code) => StanceAnimationCallbackHandler(code, mainHand, stats, stats.CurrentStance));
        TpAnimationBehavior?.Play(
            mainHand,
            stats.Stats.StanceAnimations[stats.CurrentStance.ToString()],
            animationSpeed: GetAnimationSpeed(player, Stats.ProficiencyStat),
            category: AnimationCategory(mainHand));

        SetState(MeleeWeaponState.Cooldown, mainHand);

        if (stats.Blocks[stats.CurrentStance] != null) MeleeBlockSystem.StartBlock(stats.Blocks[stats.CurrentStance], mainHand);
    }
    public virtual void OnDeselected(EntityPlayer player, bool mainHand, ref int state)
    {
        MeleeBlockSystem.StopBlock(mainHand);
        AnimationBehavior?.StopSpeedModifier();
        PlayerBehavior?.SetStat("walkspeed", mainHand ? PlayerStatsMainHandCategory : PlayerStatsOffHandCategory);
        AnimationBehavior?.StopAllVanillaAnimations(mainHand);

        GripSpecificStats? stats = GetGripSpecificStats(mainHand, player);
        if (stats != null) stats.CurrentStance = stats.InitialStance;
    }
    public virtual void OnRegistered(ActionsManagerPlayerBehavior behavior, ICoreClientAPI api)
    {
        PlayerBehavior = behavior;
        AnimationBehavior = behavior.entity.GetBehavior<FirstPersonAnimationsBehavior>();
        TpAnimationBehavior = behavior.entity.GetBehavior<ThirdPersonAnimationsBehavior>();

        if (OneHandedStats != null) OneHandedStats.GripController = new(AnimationBehavior);
        if (TwoHandedStats != null) TwoHandedStats.GripController = new(AnimationBehavior);
        if (OffHandStats != null) OffHandStats.GripController = new(AnimationBehavior);
    }

    public virtual void RenderDebugCollider(ItemSlot inSlot, IClientPlayer byPlayer)
    {
        if (DebugWindowManager._currentCollider != null)
        {
            DebugWindowManager._currentCollider.Value.Transform(byPlayer.Entity.Pos, byPlayer.Entity, inSlot, Api, right: true)?.Render(Api, byPlayer.Entity, ColorUtil.ColorFromRgba(255, 125, 125, 255));
        }
    }

    public virtual bool OnMouseWheel(ItemSlot slot, IClientPlayer byPlayer, float delta)
    {
        if (PlayerBehavior?.ActionListener.IsActive(EnumEntityAction.RightMouseDown) == false) return false;

        GripSpecificStats? stats = GetGripSpecificStats(true, byPlayer.Entity);

        if (stats == null) return false;

        bool mainHand = byPlayer.Entity.RightHandItemSlot == slot;
        float canChangeGrip = stats.Stats?.GripLengthFactor ?? 0;

        if (canChangeGrip != 0 && Stats != null)
        {
            stats.GripController?.ChangeGrip(delta, mainHand, canChangeGrip, stats.Stats.GripMinLength, stats.Stats.GripMaxLength);
            return true;
        }
        else
        {
            stats.GripController?.ResetGrip(mainHand);
            return false;
        }
    }

    public virtual void OnGameTick(ItemSlot slot, EntityPlayer player, ref int state, bool mainHand)
    {
        GripSpecificStats? stats = GetGripSpecificStats(mainHand, player);

        if (stats?.CurrentAttack == null && stats?.CurrentHandleAttack == null) return;

        switch (GetState<StanceBasedMeleeWeaponState>(mainHand))
        {
            case StanceBasedMeleeWeaponState.Idle:
                break;
            case StanceBasedMeleeWeaponState.WindUp:
                break;
            case StanceBasedMeleeWeaponState.Cooldown:
                break;
            case StanceBasedMeleeWeaponState.Attack:
                {
                    TryAttack(stats.CurrentAttack, stats.CurrentHandleAttack, stats, slot, player, mainHand);
                }
                break;
            default:
                break;
        }
    }
    public void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {

    }
    public bool RestrictRightHandAction() => !CheckState(false, StanceBasedMeleeWeaponState.Idle);
    public bool RestrictLeftHandAction() => !CheckState(true, StanceBasedMeleeWeaponState.Idle);

    protected readonly Item Item;
    protected readonly ICoreClientAPI Api;
    protected readonly StanceBasedMeleeWeaponStats Stats;
    protected readonly MeleeBlockSystemClient MeleeBlockSystem;
    protected FirstPersonAnimationsBehavior? AnimationBehavior;
    protected ThirdPersonAnimationsBehavior? TpAnimationBehavior;
    protected ActionsManagerPlayerBehavior? PlayerBehavior;
    protected SoundsSynchronizerClient SoundsSystem;
    protected Settings Settings;
    protected const string PlayerStatsMainHandCategory = "CombatOverhaul:held-item-mainhand";
    protected const string PlayerStatsOffHandCategory = "CombatOverhaul:held-item-offhand";

    protected readonly GripSpecificStats? OneHandedStats;
    protected readonly GripSpecificStats? TwoHandedStats;
    protected readonly GripSpecificStats? OffHandStats;

    [ActionEventHandler(EnumEntityAction.LeftMouseDown, ActionState.Active)]
    protected virtual bool LeftClickAttack(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (eventData.AltPressed) return false;
        if (!mainHand && CanLeftClickAttackWithOtherHand(player, mainHand)) return false;
        if (ActionRestricted(player, mainHand)) return false;
        if (GetState<StanceBasedMeleeWeaponState>(mainHand) != StanceBasedMeleeWeaponState.Idle) return false;

        GripSpecificStats? stats = GetGripSpecificStats(mainHand, player);

        if (stats == null) return false;

        MeleeAttack attack = stats.LeftClickAttacks[(stats.CurrentStance, direction)];
        MeleeAttack? handle = stats.LeftClickHandleAttacks[(stats.CurrentStance, direction)];
        StanceBasedMeleeWeaponAttackStats attackStats = stats.LeftClickAttackStats[(stats.CurrentStance, direction)];

        stats.CurrentAttack = attack;
        stats.CurrentHandleAttack = handle;
        stats.CurrentAttackStats = attackStats;
        DirectionsType = stats.DirectionsType;

        if (stats.CurrentStance != direction)
        {
            stats.AttackCounter = 0;
        }

        string[] animations = stats.LeftClickAttacksAnimations[(stats.CurrentStance, direction)];
        string attackAnimation = animations[stats.AttackCounter % animations.Length];

        stats.AttackCounter++;

        MeleeBlockSystem.StopBlock(mainHand);
        SetState(StanceBasedMeleeWeaponState.WindUp, mainHand);
        attack.Start(player.Player);
        handle?.Start(player.Player);
        AnimationBehavior?.Play(
            mainHand,
            attackAnimation,
            animationSpeed: GetAnimationSpeed(player, Stats.ProficiencyStat),
            category: AnimationCategory(mainHand),
            callback: () => LeftClickAttackCallback(mainHand, player, direction),
            callbackHandler: code => LeftClickAttackCallbackHandler(code, mainHand, stats, direction));
        TpAnimationBehavior?.Play(
            mainHand,
            attackAnimation,
            animationSpeed: GetAnimationSpeed(player, Stats.ProficiencyStat),
            category: AnimationCategory(mainHand));

        stats.HandleHitTerrain = false;

        return true;
    }
    protected virtual bool LeftClickAttackCallback(bool mainHand, EntityPlayer player, AttackDirection direction)
    {
        SetState(StanceBasedMeleeWeaponState.Idle, mainHand);

        return true;
    }
    protected virtual void LeftClickAttackCallbackHandler(string callbackCode, bool mainHand, GripSpecificStats stats, AttackDirection stance)
    {
        switch (callbackCode)
        {
            case "start":
                SetState(StanceBasedMeleeWeaponState.Attack, mainHand);
                break;
            case "stop":
                SetState(StanceBasedMeleeWeaponState.Cooldown, mainHand);
                break;
            case "startParry":
                DamageBlockJson? parry = stats.LeftClickParries[(stats.CurrentStance, stance)];
                if (parry != null) MeleeBlockSystem.StartBlock(parry, mainHand);
                break;
            case "stopParry":
                MeleeBlockSystem.StopBlock(mainHand);
                break;
            case "ready":
                SetState(StanceBasedMeleeWeaponState.Idle, mainHand);
                stats.CurrentStance = stance;

                if (stats?.Blocks[stats.CurrentStance] != null)
                {
                    MeleeBlockSystem.StartBlock(stats.Blocks[stats.CurrentStance], mainHand);
                }
                break;
        }
    }

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Active)]
    protected virtual bool RightClickAttack(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (eventData.AltPressed) return false;
        if (!mainHand && CanLeftClickAttackWithOtherHand(player, mainHand)) return false;
        if (ActionRestricted(player, mainHand)) return false;
        if (GetState<StanceBasedMeleeWeaponState>(mainHand) != StanceBasedMeleeWeaponState.Idle) return false;

        GripSpecificStats? stats = GetGripSpecificStats(mainHand, player);

        if (stats == null) return false;

        MeleeAttack attack = stats.RightClickAttacks[(stats.CurrentStance, direction)];
        MeleeAttack? handle = stats.RightClickHandleAttacks[(stats.CurrentStance, direction)];
        StanceBasedMeleeWeaponAttackStats attackStats = stats.RightClickAttackStats[(stats.CurrentStance, direction)];

        stats.CurrentAttack = attack;
        stats.CurrentHandleAttack = handle;
        stats.CurrentAttackStats = attackStats;
        DirectionsType = stats.DirectionsType;

        if (stats.CurrentStance != direction)
        {
            stats.AttackCounter = 0;
        }

        string[] animations = stats.RightClickAttacksAnimations[(stats.CurrentStance, direction)];
        string attackAnimation = animations[stats.AttackCounter % animations.Length];

        stats.AttackCounter++;

        MeleeBlockSystem.StopBlock(mainHand);
        SetState(StanceBasedMeleeWeaponState.WindUp, mainHand);
        attack.Start(player.Player);
        handle?.Start(player.Player);
        AnimationBehavior?.Play(
            mainHand,
            attackAnimation,
            animationSpeed: GetAnimationSpeed(player, Stats.ProficiencyStat),
            category: AnimationCategory(mainHand),
            callback: () => RightClickAttackCallback(mainHand, player, direction),
            callbackHandler: code => RightClickAttackCallbackHandler(code, mainHand, stats, direction));
        TpAnimationBehavior?.Play(
            mainHand,
            attackAnimation,
            animationSpeed: GetAnimationSpeed(player, Stats.ProficiencyStat),
            category: AnimationCategory(mainHand));

        stats.HandleHitTerrain = false;

        return true;
    }
    protected virtual bool RightClickAttackCallback(bool mainHand, EntityPlayer player, AttackDirection direction)
    {
        SetState(StanceBasedMeleeWeaponState.Idle, mainHand);

        return true;
    }
    protected virtual void RightClickAttackCallbackHandler(string callbackCode, bool mainHand, GripSpecificStats stats, AttackDirection stance)
    {
        switch (callbackCode)
        {
            case "start":
                SetState(StanceBasedMeleeWeaponState.Attack, mainHand);
                break;
            case "stop":
                SetState(StanceBasedMeleeWeaponState.Cooldown, mainHand);
                break;
            case "startParry":
                DamageBlockJson? parry = stats.RightClickParries[(stats.CurrentStance, stance)];
                if (parry != null) MeleeBlockSystem.StartBlock(parry, mainHand);
                break;
            case "stopParry":
                MeleeBlockSystem.StopBlock(mainHand);
                break;
            case "ready":
                SetState(StanceBasedMeleeWeaponState.Idle, mainHand);
                stats.CurrentStance = stance;

                if (stats?.Blocks[stats.CurrentStance] != null)
                {
                    MeleeBlockSystem.StartBlock(stats.Blocks[stats.CurrentStance], mainHand);
                }
                break;
        }
    }

    protected virtual void StanceAnimationCallbackHandler(string callbackCode, bool mainHand, GripSpecificStats stats, AttackDirection stance)
    {
        switch (callbackCode)
        {
            case "startBlock":
                if (stats.Blocks[stance] != null) MeleeBlockSystem.StartBlock(stats.Blocks[stance], mainHand);
                break;
            case "stopBlock":
                MeleeBlockSystem.StopBlock(mainHand);
                break;
            case "ready":
                SetState(StanceBasedMeleeWeaponState.Idle, mainHand);
                break;
        }
    }
    protected virtual void TryAttack(MeleeAttack? attack, MeleeAttack? handle, GripSpecificStats stats, ItemSlot slot, EntityPlayer player, bool mainHand)
    {
        StanceBasedMeleeWeaponAttackStats? attackStats = stats.CurrentAttackStats;

        if (handle != null)
        {
            handle.Attack(
                        player.Player,
                        slot,
                        mainHand,
                        out IEnumerable<(Block block, Vector3d point)> handleTerrainCollision,
                        out IEnumerable<(Vintagestory.API.Common.Entities.Entity entity, Vector3d point)> handleEntitiesCollision);

            if (!stats.HandleHitTerrain && handleTerrainCollision.Any())
            {
                if (attackStats?.HandleHitSound != null) SoundsSystem.Play(attackStats.HandleHitSound);
                stats.HandleHitTerrain = true;
            }

            if (handleTerrainCollision.Any()) return;
        }

        if (attack == null) return;

        attack.Attack(
            player.Player,
            slot,
            mainHand,
            out IEnumerable<(Block block, Vector3d point)> terrainCollision,
            out IEnumerable<(Vintagestory.API.Common.Entities.Entity entity, Vector3d point)> entitiesCollision);

        if (handle != null) handle.AddAttackedEntities(attack);

        if (entitiesCollision.Any() && attackStats?.AttackHitSound != null)
        {
            SoundsSystem.Play(attackStats.AttackHitSound);
        }

        if (entitiesCollision.Any() && Stats.AnimationStaggerOnHitDurationMs > 0)
        {
            AnimationBehavior?.SetSpeedModifier(AttackImpactFunction);
        }
    }

    protected static void RegisterCollider(string item, string type, MeleeAttack attack)
    {
#if DEBUG
        int typeIndex = 0;
        foreach (MeleeDamageType damageType in attack.DamageTypes)
        {
            DebugWindowManager.RegisterCollider(item, type + typeIndex++, damageType);
        }
#endif
    }
    protected float GetAnimationSpeed(Entity player, string proficiencyStat, float min = 0.5f, float max = 2f)
    {
        float manipulationSpeed = PlayerBehavior?.ManipulationSpeed ?? 1;
        float proficiencyBonus = proficiencyStat == "" ? 0 : player.Stats.GetBlended(proficiencyStat) - 1;
        return Math.Clamp(manipulationSpeed + proficiencyBonus, min, max);
    }
    protected bool CheckState<TState>(bool mainHand, params TState[] statesToCheck)
    {
        return statesToCheck.Contains((TState)Enum.ToObject(typeof(TState), PlayerBehavior?.GetState(mainHand) ?? 0));
    }
    protected void SetState<TState>(TState state, bool mainHand)
    {
        PlayerBehavior?.SetState((int)Enum.ToObject(typeof(TState), state), mainHand);
    }
    protected TState GetState<TState>(bool mainHand)
    {
        return (TState)Enum.ToObject(typeof(TState), PlayerBehavior?.GetState(mainHand) ?? 0);
    }
    protected static string AnimationCategory(bool mainHand = true) => mainHand ? "main" : "mainOffhand";
    protected GripType GetGripType(bool mainHand, EntityPlayer player)
    {
        if (!mainHand) return GripType.OffHanded;

        if (player.LeftHandItemSlot.Empty)
        {
            return GripType.TwoHanded;
        }
        else
        {
            return GripType.OneHanded;
        }
    }
    public GripSpecificStats? GetGripSpecificStats(bool mainHand, EntityPlayer player)
    {
        GripType gripType = GetGripType(mainHand, player);

        return gripType switch
        {
            GripType.OneHanded => OneHandedStats,
            GripType.TwoHanded => TwoHandedStats ?? OneHandedStats,
            GripType.OffHanded => OffHandStats,
            _ => throw new Exception()
        };
    }
    protected bool CanRightClickAttackWithOtherHand(EntityPlayer player, bool mainHand = true)
    {
        ItemSlot otherHandSlot = mainHand ? player.LeftHandItemSlot : player.RightHandItemSlot;
        return (otherHandSlot.Itemstack?.Item as IHasMeleeWeaponActions)?.CanAttack(!mainHand) ?? false;
    }
    protected bool CanLeftClickAttackWithOtherHand(EntityPlayer player, bool mainHand = true)
    {
        ItemSlot otherHandSlot = mainHand ? player.LeftHandItemSlot : player.RightHandItemSlot;
        return (otherHandSlot.Itemstack?.Item as IHasMeleeWeaponActions)?.CanBlock(!mainHand) ?? false;
    }
    protected virtual bool AttackImpactFunction(TimeSpan duration, ref TimeSpan delta)
    {
        TimeSpan totalDuration = TimeSpan.FromMilliseconds(Stats.AnimationStaggerOnHitDurationMs);

        /*double multiplier = duration / totalDuration;
        multiplier = Math.Pow(multiplier, 3);
        delta = delta * multiplier;*/

        delta = TimeSpan.Zero;

        return duration < totalDuration;
    }
    protected static bool ActionRestricted(EntityPlayer player, bool mainHand = true)
    {
        if (mainHand)
        {
            return (player.LeftHandItemSlot.Itemstack?.Item as IRestrictAction)?.RestrictRightHandAction() ?? false;
        }
        else
        {
            return (player.RightHandItemSlot.Itemstack?.Item as IRestrictAction)?.RestrictLeftHandAction() ?? false;
        }
    }
}

public class StanceBasedMeleeWeapon : Item, IHasWeaponLogic, IHasDynamicIdleAnimations, IHasMeleeWeaponActions, IHasServerBlockCallback, ISetsRenderingOffset, IMouseWheelInput, IOnGameTick, IRestrictAction
{
    public StanceBasedMeleeWeaponClient? ClientLogic { get; private set; }

    IClientWeaponLogic? IHasWeaponLogic.ClientLogic => ClientLogic;

    public bool RenderingOffset { get; set; }

    public bool RestrictRightHandAction() => ClientLogic?.RestrictRightHandAction() ?? false;
    public bool RestrictLeftHandAction() => ClientLogic?.RestrictLeftHandAction() ?? false;

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        if (api is ICoreClientAPI clientAPI)
        {
            ClientLogic = new(clientAPI, this);
            StanceBasedMeleeWeaponStats Stats = Attributes.AsObject<StanceBasedMeleeWeaponStats>();
            RenderingOffset = Stats.RenderingOffset;

            ChangeGripInteraction = new()
            {
                MouseButton = EnumMouseButton.Wheel,
                ActionLangCode = "combatoverhaul:interaction-grip-change"
            };

        }

        AltForInteractions = new()
        {
            MouseButton = EnumMouseButton.None,
            HotKeyCode = "Alt",
            ActionLangCode = "combatoverhaul:interaction-hold-alt"
        };
    }

    public AnimationRequestByCode? GetIdleAnimation(bool mainHand) => ClientLogic?.GetIdleAnimation(mainHand);
    public AnimationRequestByCode? GetReadyAnimation(bool mainHand) => ClientLogic?.GetReadyAnimation(mainHand);

    public override void OnHeldRenderOpaque(ItemSlot inSlot, IClientPlayer byPlayer)
    {
        base.OnHeldRenderOpaque(inSlot, byPlayer);

        if (DebugWindowManager.RenderDebugColliders)
        {
            ClientLogic?.RenderDebugCollider(inSlot, byPlayer);
        }
    }

    public bool CanAttack(bool mainHand) => true;
    public bool CanBlock(bool mainHand) => true;
    public void OnGameTick(ItemSlot slot, EntityPlayer player, ref int state, bool mainHand)
    {
        ClientLogic?.OnGameTick(slot, player, ref state, mainHand);
    }

    public override void OnHeldAttackStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, ref EnumHandHandling handling)
    {
        handling = EnumHandHandling.PreventDefaultAction;
    }
    public override float OnBlockBreaking(IPlayer player, BlockSelection blockSel, ItemSlot itemslot, float remainingResistance, float dt, int counter)
    {
        return remainingResistance;
    }

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        ClientLogic?.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        dsc.AppendLine("");

        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
    }

    /*public override WorldInteraction?[]? GetHeldInteractionHelp(ItemSlot inSlot)
    {
        WorldInteraction?[]? interactionHelp = base.GetHeldInteractionHelp(inSlot);

        if (ChangeGripInteraction != null)
        {
            interactionHelp = interactionHelp?.Append(ChangeGripInteraction);
        }

        return interactionHelp?.Append(AltForInteractions);
    }*/

    public void BlockCallback(IServerPlayer player, ItemSlot slot, bool mainHand, float damageBlocked)
    {
        DamageItem(player.Entity.World, player.Entity, slot, 1);
        //DamageItem(player.Entity.World, player.Entity, slot, (int)MathF.Ceiling(damageBlocked)); // Damages swords too much
    }

    public bool OnMouseWheel(ItemSlot slot, IClientPlayer byPlayer, float delta) => ClientLogic?.OnMouseWheel(slot, byPlayer, delta) ?? false;

    protected WorldInteraction? AltForInteractions;
    protected WorldInteraction? ChangeGripInteraction;
}