using Cairo;
using CombatOverhaul.Animations;
using CombatOverhaul.Inputs;
using CombatOverhaul.RangedSystems;
using CombatOverhaul.RangedSystems.Aiming;
using System.Numerics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace CombatOverhaul.Implementations;

public enum CrossbowState
{
    Unloaded,
    Draw,
    Drawn,
    Load,
    Loaded,
    Aimed
}

public class CrossbowStats : WeaponStats
{
    public string DrawAnimation { get; set; } = "";
    public string DrawnAnimation { get; set; } = "";
    public string LoadAnimation { get; set; } = "";
    public string ReleaseAnimation { get; set; } = "";
    public string AimAnimation { get; set; } = "";
    public string LoadedAnimation { get; set; } = "";
    public float DrawSpeedPenalty { get; set; } = -0.1f;
    public float LoadSpeedPenalty { get; set; } = -0.1f;

    public AimingStatsJson Aiming { get; set; } = new();
    public float BoltDamageMultiplier { get; set; } = 1;
    public float BoltDamageStrength { get; set; } = 1;
    public float BoltVelocity { get; set; } = 1;
    public string BoltWildcard { get; set; } = "*bolt-*";
    public string DrawRequirement { get; set; } = "";
    public float Zeroing { get; set; } = 1.5f;
    public float[] DispersionMOA { get; set; } = new float[] { 0, 0 };
}

public class CrossbowClient : RangeWeaponClient
{
    public CrossbowClient(ICoreClientAPI api, Item item, AmmoSelector selector) : base(api, item)
    {
        Attachable = item.GetCollectibleBehavior<AnimatableAttachable>(withInheritance: true) ?? throw new Exception("Crossbow should have AnimatableAttachable behavior.");
        BoltTransform = new(item.Attributes["BoltTransform"].AsObject<ModelTransformNoDefaults>(), ModelTransform.BlockDefaultTp());
        AimingSystem = api.ModLoader.GetModSystem<CombatOverhaulSystem>().AimingSystem ?? throw new Exception();
        Stats = item.Attributes.AsObject<CrossbowStats>();
        AimingStats = Stats.Aiming.ToStats();
        AmmoSelector = selector;
    }

    public override void OnSelected(ItemSlot slot, EntityPlayer player, bool mainHand, ref int state)
    {
        Attachable.ClearAttachments(player.EntityId);

        bool drawn = slot.Itemstack.Attributes.GetBool("crossbow-drawn", defaultValue: false);
        if (drawn)
        {
            state = (int)CrossbowState.Drawn;
            AnimationBehavior?.Play(mainHand, Stats.LoadedAnimation, category: "string", weight: 0.001f);
        }
        else
        {
            state = (int)CrossbowState.Unloaded;
        }
    }
    public override void OnDeselected(EntityPlayer player, bool mainHand, ref int state)
    {
        Attachable.ClearAttachments(player.EntityId);
        AimingAnimationController?.Stop(mainHand);
        AimingSystem.StopAiming();
        BoltSlot = null;
    }
    public override void OnRegistered(ActionsManagerPlayerBehavior behavior, ICoreClientAPI api)
    {
        base.OnRegistered(behavior, api);
        AimingAnimationController = new(AimingSystem, AnimationBehavior, AimingStats);
    }

    protected AimingAnimationController? AimingAnimationController;
    protected readonly AnimatableAttachable Attachable;
    protected readonly ClientAimingSystem AimingSystem;
    protected readonly ModelTransform BoltTransform;
    protected readonly CrossbowStats Stats;
    protected readonly AimingStats AimingStats;
    protected readonly AmmoSelector AmmoSelector;
    protected ItemSlot? BoltSlot;

    protected const string PlayerStatsMainHandCategory = "CombatOverhaul:held-item-mainhand";
    protected const string PlayerStatsOffHandCategory = "CombatOverhaul:held-item-offhand";

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Active)]
    protected virtual bool Draw(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (state != (int)CrossbowState.Unloaded || eventData.AltPressed) return false;
        if (!CheckDrawRequirement(player)) return false;

        AnimationBehavior?.Play(mainHand, Stats.DrawAnimation, callback: () => DrawAnimationCallback(slot, mainHand), animationSpeed: GetAnimationSpeed(player, Stats.ProficiencyStat));

        state = (int)CrossbowState.Draw;

        PlayerBehavior?.SetStat("walkspeed", mainHand ? PlayerStatsMainHandCategory : PlayerStatsOffHandCategory, Stats.DrawSpeedPenalty);

        return true;
    }

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Active)]
    protected virtual bool Load(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (state != (int)CrossbowState.Drawn || eventData.AltPressed) return false;

        player.WalkInventory(slot =>
        {
            if (slot?.Itemstack?.Item == null) return true;

            if (slot.Itemstack.Item.HasBehavior<ProjectileBehavior>() && WildcardUtil.Match(AmmoSelector.SelectedAmmo, slot.Itemstack.Item.Code.ToString()))
            {
                BoltSlot = slot;
                return false;
            }

            return true;
        });

        if (BoltSlot == null)
        {
            player.WalkInventory(slot =>
            {
                if (slot?.Itemstack?.Item == null) return true;

                if (slot.Itemstack.Item.HasBehavior<ProjectileBehavior>() && WildcardUtil.Match(Stats.BoltWildcard, slot.Itemstack.Item.Code.ToString()))
                {
                    BoltSlot = slot;
                    return false;
                }

                return true;
            });
        }

        if (BoltSlot == null) return false;

        Attachable.SetAttachment(player.EntityId, "bolt", BoltSlot.Itemstack, BoltTransform);

        AnimationBehavior?.Play(mainHand, Stats.LoadAnimation, animationSpeed: GetAnimationSpeed(player, Stats.ProficiencyStat), callback: () => LoadAnimationCallback(slot, mainHand, BoltSlot));

        state = (int)CrossbowState.Load;

        PlayerBehavior?.SetStat("walkspeed", mainHand ? PlayerStatsMainHandCategory : PlayerStatsOffHandCategory, Stats.LoadSpeedPenalty);

        return true;
    }

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Active)]
    protected virtual bool Aim(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (state != (int)CrossbowState.Loaded || eventData.AltPressed) return false;

        AnimationBehavior?.Play(mainHand, Stats.AimAnimation);

        state = (int)CrossbowState.Aimed;

        AimingSystem.StartAiming(AimingStats);
        AimingSystem.AimingState = WeaponAimingState.FullCharge;

        AimingAnimationController?.Play(mainHand);

        return true;
    }

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Released)]
    protected virtual bool Ease(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        PlayerBehavior?.SetStat("walkspeed", mainHand ? PlayerStatsMainHandCategory : PlayerStatsOffHandCategory);

        switch ((CrossbowState)state)
        {
            case CrossbowState.Draw:
                state = (int)CrossbowState.Unloaded;
                AnimationBehavior?.PlayReadyAnimation();
                return true;

            case CrossbowState.Load:
                state = (int)CrossbowState.Drawn;
                AnimationBehavior?.PlayReadyAnimation();
                Attachable.ClearAttachments(player.EntityId);
                return true;

            case CrossbowState.Aimed:
                state = BoltSlot == null ? (int)CrossbowState.Unloaded : (int)CrossbowState.Loaded;
                AnimationBehavior?.PlayReadyAnimation();
                AimingAnimationController?.Stop(mainHand);
                AimingSystem.StopAiming();
                return true;

            default:
                return false;
        }
    }

    [ActionEventHandler(EnumEntityAction.LeftMouseDown, ActionState.Pressed)]
    protected virtual bool Shoot(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (state != (int)CrossbowState.Aimed || eventData.AltPressed || BoltSlot == null) return false;

        AnimationBehavior?.Stop("string");
        AnimationBehavior?.Play(
            mainHand,
            Stats.ReleaseAnimation,
            weight: 1000,
            callback: () => ReleaseAnimationCallback(slot, mainHand, player),
            callbackHandler: callbackCode => ReleaseAnimationCallbackHandler(callbackCode, slot, mainHand, player));

        BoltSlot = null;

        return true;
    }

    protected virtual void DrawCallback(bool success)
    {
        if (success)
        {
            PlayerBehavior?.SetState((int)CrossbowState.Drawn, mainHand: true);
        }
        else
        {
            PlayerBehavior?.SetState((int)CrossbowState.Unloaded, mainHand: true);
        }
        AnimationBehavior?.PlayReadyAnimation();
    }
    protected virtual bool DrawAnimationCallback(ItemSlot slot, bool mainHand)
    {
        RangedWeaponSystem.Load(slot, mainHand, DrawCallback);
        AnimationBehavior?.PlayReadyAnimation();
        AnimationBehavior?.Play(mainHand, Stats.LoadedAnimation, category: "string", weight: 0.001f);
        PlayerBehavior?.SetStat("walkspeed", mainHand ? PlayerStatsMainHandCategory : PlayerStatsOffHandCategory);
        return true;
    }
    protected virtual void LoadCallback(bool success)
    {
        if (success)
        {
            PlayerBehavior?.SetState((int)CrossbowState.Loaded, mainHand: true);
        }
        else
        {
            PlayerBehavior?.SetState((int)CrossbowState.Drawn, mainHand: true);
        }
    }
    protected virtual bool LoadAnimationCallback(ItemSlot slot, bool mainHand, ItemSlot boltSlot)
    {
        RangedWeaponSystem.Reload(slot, boltSlot, 1, mainHand, LoadCallback);
        AnimationBehavior?.PlayReadyAnimation();
        PlayerBehavior?.SetStat("walkspeed", mainHand ? PlayerStatsMainHandCategory : PlayerStatsOffHandCategory);
        return true;
    }
    protected virtual void ShootCallback(bool success)
    {

    }
    protected virtual void ReleaseAnimationCallbackHandler(string callbackCode, ItemSlot slot, bool mainHand, EntityPlayer player)
    {
        switch (callbackCode)
        {
            case "shoot":
                {
                    Vintagestory.API.MathTools.Vec3d position = player.LocalEyePos + player.Pos.XYZ;
                    Vector3 targetDirection = AimingSystem.TargetVec;

                    targetDirection = ClientAimingSystem.Zeroing(targetDirection, Stats.Zeroing);

                    RangedWeaponSystem.Shoot(slot, 1, new((float)position.X, (float)position.Y, (float)position.Z), new(targetDirection.X, targetDirection.Y, targetDirection.Z), mainHand, ShootCallback);

                    Attachable.ClearAttachments(player.EntityId);
                }
                break;
        }
    }
    protected virtual bool ReleaseAnimationCallback(ItemSlot slot, bool mainHand, EntityPlayer player)
    {
        return true;
    }
    protected virtual bool CheckDrawRequirement(EntityPlayer player)
    {
        if (Stats.DrawRequirement == "") return true;

        ItemSlot? requirement = null;

        player.WalkInventory(slot =>
        {
            if (slot?.Itemstack?.Item == null) return true;

            if (WildcardUtil.Match(Stats.DrawRequirement, slot.Itemstack.Item.Code.ToString()))
            {
                requirement = slot;
                return false;
            }

            return true;
        });

        return requirement != null;
    }
}


public class CrossbowServer : RangeWeaponServer
{
    public CrossbowServer(ICoreServerAPI api, Item item) : base(api, item)
    {
        _projectileSystem = api.ModLoader.GetModSystem<CombatOverhaulSystem>().ServerProjectileSystem ?? throw new Exception();
        _stats = item.Attributes.AsObject<CrossbowStats>();
    }

    public override bool Reload(IServerPlayer player, ItemSlot slot, ItemSlot? ammoSlot, ReloadPacket packet)
    {
        if (ammoSlot?.Itemstack?.Item != null && ammoSlot.Itemstack.Item.HasBehavior<ProjectileBehavior>() && WildcardUtil.Match(_stats.BoltWildcard, ammoSlot.Itemstack.Item.Code.ToString()))
        {
            _boltSlots[player.Entity.EntityId] = ammoSlot;
            return true;
        }

        if (ammoSlot == null)
        {
            slot.Itemstack.Attributes.SetBool("crossbow-drawn", true);
            slot.MarkDirty();
            return true;
        }

        return false;
    }

    public override bool Shoot(IServerPlayer player, ItemSlot slot, ShotPacket packet, Entity shooter)
    {
        if (!_boltSlots.ContainsKey(player.Entity.EntityId)) return false;

        ItemSlot? boltSlot = _boltSlots[player.Entity.EntityId];

        if (boltSlot?.Itemstack == null || boltSlot.Itemstack.StackSize < 1) return false;

        ProjectileStats? stats = boltSlot.Itemstack.Item.GetCollectibleBehavior<ProjectileBehavior>(true)?.Stats;

        if (stats == null)
        {
            _boltSlots[player.Entity.EntityId] = null;
            return false;
        }

        ProjectileSpawnStats spawnStats = new()
        {
            ProducerEntityId = player.Entity.EntityId,
            DamageMultiplier = _stats.BoltDamageMultiplier,
            DamageStrength = _stats.BoltDamageStrength,
            Position = new Vector3(packet.Position[0], packet.Position[1], packet.Position[2]),
            Velocity = GetDirectionWithDispersion(packet.Velocity, _stats.DispersionMOA) * _stats.BoltVelocity
        };

        _projectileSystem.Spawn(packet.ProjectileId, stats, spawnStats, boltSlot.TakeOut(1), shooter);

        boltSlot.MarkDirty();

        slot.Itemstack.Item.DamageItem(player.Entity.World, player.Entity, slot, 1 + stats.AdditionalDurabilityCost);
        slot.Itemstack.Attributes.SetBool("crossbow-drawn", false);
        slot.MarkDirty();
        return true;
    }

    private readonly Dictionary<long, ItemSlot?> _boltSlots = new();
    private readonly ProjectileSystemServer _projectileSystem;
    private readonly CrossbowStats _stats;
}

public class CrossbowItem : Item, IHasWeaponLogic, IHasRangedWeaponLogic, IHasIdleAnimations
{
    public CrossbowClient? ClientLogic { get; private set; }
    public CrossbowServer? ServerLogic { get; private set; }

    public AnimationRequestByCode IdleAnimation { get; private set; }
    public AnimationRequestByCode ReadyAnimation { get; private set; }

    IClientWeaponLogic? IHasWeaponLogic.ClientLogic => ClientLogic;
    IServerRangedWeaponLogic? IHasRangedWeaponLogic.ServerWeaponLogic => ServerLogic;

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        if (api is ICoreClientAPI clientAPI)
        {
            CrossbowStats stats = Attributes.AsObject<CrossbowStats>();
            IdleAnimation = new(stats.IdleAnimation, 1, 1, "main", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false);
            ReadyAnimation = new(stats.ReadyAnimation, 1, 1, "main", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false);
            _clientApi = clientAPI;
            _ammoSelector = new(clientAPI, stats.BoltWildcard);

            ClientLogic = new(clientAPI, this, _ammoSelector);
        }

        if (api is ICoreServerAPI serverAPI)
        {
            ServerLogic = new(serverAPI, this);
        }
    }

    public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
    {
        WorldInteraction[] interactions = base.GetHeldInteractionHelp(inSlot);

        WorldInteraction ammoSelection = new()
        {
            ActionLangCode = Lang.Get("combatoverhaul:interaction-ammoselection"),
            HotKeyCodes = new string[1] { "toolmodeselect" },
            MouseButton = EnumMouseButton.None
        };

        return interactions.Append(ammoSelection).ToArray();
    }

    public override int GetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSelection)
    {
        if (_clientApi?.World.Player.Entity.EntityId == byPlayer.Entity.EntityId)
        {
            return _ammoSelector?.GetToolMode(slot, byPlayer, blockSelection) ?? 0;
        }

        return 0;
    }
    public override SkillItem[] GetToolModes(ItemSlot slot, IClientPlayer forPlayer, BlockSelection blockSel)
    {
        if (_clientApi?.World.Player.Entity.EntityId == forPlayer.Entity.EntityId)
        {
            return _ammoSelector?.GetToolModes(slot, forPlayer, blockSel) ?? Array.Empty<SkillItem>();
        }

        return Array.Empty<SkillItem>();
    }
    public override void SetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSelection, int toolMode)
    {
        if (_clientApi?.World.Player.Entity.EntityId == byPlayer.Entity.EntityId)
        {
            _ammoSelector?.SetToolMode(slot, byPlayer, blockSelection, toolMode);
        }
    }

    private AmmoSelector? _ammoSelector;
    private ICoreClientAPI? _clientApi;
}
