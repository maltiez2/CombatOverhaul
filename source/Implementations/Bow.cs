using CombatOverhaul.Animations;
using CombatOverhaul.Inputs;
using CombatOverhaul.RangedSystems;
using CombatOverhaul.RangedSystems.Aiming;
using CombatOverhaul.Utils;
using OpenTK.Mathematics;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.Client.NoObf;
using VSImGui.Debug;

namespace CombatOverhaul.Implementations;

public enum BowState
{
    Unloaded,
    Load,
    PreLoaded,
    Loaded,
    Draw,
    Drawn
}

public class WeaponStats
{
    public string ReadyAnimation { get; set; } = "";
    public string IdleAnimation { get; set; } = "";
    public string ProficiencyStat { get; set; } = "";
}

public sealed class BowStats : WeaponStats
{
    public string LoadAnimation { get; set; } = "";
    public string DrawAnimation { get; set; } = "";
    public string DrawAfterLoadAnimation { get; set; } = "";
    public string ReleaseAnimation { get; set; } = "";
    public string TpAimAnimation { get; set; } = "";
    public AimingStatsJson Aiming { get; set; } = new();
    public float ArrowDamageMultiplier { get; set; } = 1;
    public float ArrowDamageTier { get; set; } = 1;
    public float ArrowVelocity { get; set; } = 1;
    public string ArrowWildcard { get; set; } = "*arrow-*";
    public float Zeroing { get; set; } = 1.5f;
    public float[] DispersionMOA { get; set; } = new float[] { 0, 0 };
}

public class BowClient : RangeWeaponClient
{
    public BowClient(ICoreClientAPI api, Item item, AmmoSelector ammoSelector) : base(api, item)
    {
        Api = api;
        Attachable = item.GetCollectibleBehavior<AnimatableAttachable>(withInheritance: true) ?? throw new Exception("Bow should have AnimatableAttachable behavior.");
        ArrowTransform = new(item.Attributes["ArrowTransform"].AsObject<ModelTransformNoDefaults>(), ModelTransform.BlockDefaultTp());
        AimingSystem = api.ModLoader.GetModSystem<CombatOverhaulSystem>().AimingSystem ?? throw new Exception();

        Stats = item.Attributes.AsObject<BowStats>();
        AimingStats = Stats.Aiming.ToStats();
        AmmoSelector = ammoSelector;

        api.ModLoader.GetModSystem<CombatOverhaulSystem>().SettingsLoaded += settings =>
        {
            AimingStats.CursorType = Enum.Parse<AimingCursorType>(settings.BowsAimingCursorType);
            AimingStats.VerticalLimit = settings.BowsAimingVerticalLimit;
            AimingStats.HorizontalLimit = settings.BowsAimingHorizontalLimit;
        };

        //DebugWidgets.FloatDrag("test", "test3", $"{item.Code}-followX", () => AimingStats.AnimationFollowX, (value) => AimingStats.AnimationFollowX = value);
        //DebugWidgets.FloatDrag("test", "test3", $"{item.Code}-followY", () => AimingStats.AnimationFollowY, (value) => AimingStats.AnimationFollowY = value);
    }

    public override void OnSelected(ItemSlot slot, EntityPlayer player, bool mainHand, ref int state)
    {
        Attachable.ClearAttachments(player.EntityId);
        AttachmentSystem.SendClearPacket(player.EntityId);
    }

    public override void OnDeselected(EntityPlayer player, bool mainHand, ref int state)
    {
        Attachable.ClearAttachments(player.EntityId);
        AttachmentSystem.SendClearPacket(player.EntityId);
        PlayerBehavior?.SetState((int)BowState.Unloaded);
        AimingSystem.StopAiming();

        AnimationBehavior?.StopVanillaAnimation(Stats.TpAimAnimation, mainHand);
    }

    public override void OnRegistered(ActionsManagerPlayerBehavior behavior, ICoreClientAPI api)
    {
        base.OnRegistered(behavior, api);
        AimingAnimationController = new(AimingSystem, AnimationBehavior, AimingStats);
    }

    protected readonly ICoreClientAPI Api;
    protected readonly AnimatableAttachable Attachable;
    protected readonly ModelTransform ArrowTransform;
    protected readonly ClientAimingSystem AimingSystem;
    protected readonly BowStats Stats;
    protected readonly AimingStats AimingStats;
    protected readonly AmmoSelector AmmoSelector;
    protected AimingAnimationController? AimingAnimationController;
    protected bool AfterLoad = false;

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Active)]
    protected virtual bool Load(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (state != (int)BowState.Unloaded || eventData.AltPressed || !CheckForOtherHandEmpty(mainHand, player)) return false;

        ItemSlot? arrowSlot = GetArrowSlot(player);

        if (arrowSlot == null) return false;

        Attachable.SetAttachment(player.EntityId, "Arrow", arrowSlot.Itemstack, ArrowTransform);
        AttachmentSystem.SendAttachPacket(player.EntityId, "Arrow", arrowSlot.Itemstack, ArrowTransform);
        RangedWeaponSystem.Reload(slot, arrowSlot, 1, mainHand, ReloadCallback);

        AnimationBehavior?.Play(mainHand, Stats.LoadAnimation, animationSpeed: GetAnimationSpeed(player, Stats.ProficiencyStat), callback: LoadAnimationCallback);
        TpAnimationBehavior?.Play(mainHand, Stats.LoadAnimation, animationSpeed: GetAnimationSpeed(player, Stats.ProficiencyStat));

        AimingSystem.ResetAim();
        AimingSystem.StartAiming(AimingStats);
        AimingSystem.AimingState = WeaponAimingState.Blocked;

        AimingAnimationController?.Play(mainHand);

        state = (int)BowState.Load;

        AfterLoad = true;

        return true;
    }
    protected virtual void ReloadCallback(bool success)
    {
        BowState state = GetState<BowState>(mainHand: true);

        if (success)
        {
            switch (state)
            {
                case BowState.PreLoaded:
                    SetState(BowState.Loaded, mainHand: true);
                    break;
                case BowState.Load:
                    SetState(BowState.PreLoaded, mainHand: true);
                    break;
            }
        }
        else
        {
            AnimationBehavior?.PlayReadyAnimation(true);
            TpAnimationBehavior?.PlayReadyAnimation(true);
            SetState(BowState.Unloaded, mainHand: true);
        }
    }
    protected virtual bool LoadAnimationCallback()
    {
        BowState state = GetState<BowState>(mainHand: true);

        switch (state)
        {
            case BowState.PreLoaded:
                SetState(BowState.Loaded, mainHand: true);
                break;
            case BowState.Load:
                SetState(BowState.PreLoaded, mainHand: true);
                break;
        }

        return true;
    }

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Active)]
    protected virtual bool Aim(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (state != (int)BowState.Loaded || eventData.AltPressed || !CheckForOtherHandEmpty(mainHand, player)) return false;

        AnimationRequestByCode request = new(AfterLoad ? Stats.DrawAfterLoadAnimation : Stats.DrawAnimation, GetAnimationSpeed(player, Stats.ProficiencyStat), 1, "main", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), true, FullLoadCallback);
        AnimationBehavior?.Play(request, mainHand);
        TpAnimationBehavior?.Play(request, mainHand);

        AfterLoad = false;

        state = (int)BowState.Draw;

        if (!AimingSystem.Aiming)
        {
            AimingSystem.StartAiming(AimingStats);
            AimingSystem.AimingState = WeaponAimingState.Blocked;

            AimingAnimationController?.Play(mainHand);
        }

        if (TpAnimationBehavior == null) AnimationBehavior?.PlayVanillaAnimation(Stats.TpAimAnimation, mainHand);

        return true;
    }

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Released)]
    protected virtual bool Shoot(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        AimingSystem.StopAiming();

        AnimationBehavior?.StopVanillaAnimation(Stats.TpAimAnimation, mainHand);

        if (CheckState(state, BowState.Load, BowState.PreLoaded))
        {
            AnimationBehavior?.PlayReadyAnimation(mainHand);
            TpAnimationBehavior?.PlayReadyAnimation(mainHand);
            Attachable.ClearAttachments(player.EntityId);
            AttachmentSystem.SendClearPacket(player.EntityId);
            state = (int)BowState.Unloaded;
            return true;
        }

        if (CheckState(state, BowState.Draw, BowState.Loaded))
        {
            AnimationBehavior?.PlayReadyAnimation(mainHand);
            TpAnimationBehavior?.PlayReadyAnimation(mainHand);
            state = (int)BowState.Loaded;
            AfterLoad = false;
            return true;
        }

        if (state != (int)BowState.Drawn) return false;

        AnimationBehavior?.Play(mainHand, Stats.ReleaseAnimation, callback: () => ShootCallback(slot, player, mainHand));
        TpAnimationBehavior?.Play(mainHand, Stats.ReleaseAnimation);

        return true;
    }

    protected virtual bool ShootCallback(ItemSlot slot, EntityPlayer player, bool mainHand)
    {
        PlayerBehavior?.SetState(0, mainHand);

        Vintagestory.API.MathTools.Vec3d position = player.LocalEyePos + player.Pos.XYZ;
        Vector3 targetDirection = AimingSystem.TargetVec;
        targetDirection = ClientAimingSystem.Zeroing(targetDirection, Stats.Zeroing);

        RangedWeaponSystem.Shoot(slot, 1, new((float)position.X, (float)position.Y, (float)position.Z), new(targetDirection.X, targetDirection.Y, targetDirection.Z), mainHand, _ => { });
        Attachable.ClearAttachments(player.EntityId);
        AttachmentSystem.SendClearPacket(player.EntityId);
        AimingAnimationController?.Stop(mainHand);

        AnimationBehavior?.PlayReadyAnimation(mainHand);
        TpAnimationBehavior?.PlayReadyAnimation(mainHand);

        return true;
    }

    protected virtual bool FullLoadCallback()
    {
        PlayerBehavior?.SetState((int)BowState.Drawn);
        AimingSystem.AimingState = WeaponAimingState.FullCharge;
        return true;
    }

    protected virtual ItemSlot? GetArrowSlot(EntityPlayer player)
    {
        ItemSlot? arrowSlot = null;

        player.WalkInventory(slot =>
        {
            if (slot?.Itemstack?.Item == null) return true;

            if (slot.Itemstack.Item.HasBehavior<ProjectileBehavior>() && WildcardUtil.Match(AmmoSelector.SelectedAmmo, slot.Itemstack.Item.Code.ToString()))
            {
                arrowSlot = slot;
                return false;
            }

            return true;
        });

        if (arrowSlot == null)
        {
            player.WalkInventory(slot =>
            {
                if (slot?.Itemstack?.Item == null) return true;

                if (slot.Itemstack.Item.HasBehavior<ProjectileBehavior>() && WildcardUtil.Match(Stats.ArrowWildcard, slot.Itemstack.Item.Code.ToString()))
                {
                    arrowSlot = slot;
                    return false;
                }

                return true;
            });
        }

        return arrowSlot;
    }
}

public sealed class AimingAnimationController
{
    public AimingStats Stats { get; set; }

    public AimingAnimationController(ClientAimingSystem aimingSystem, FirstPersonAnimationsBehavior? animationBehavior, AimingStats stats)
    {
        _aimingSystem = aimingSystem;
        _animationBehavior = animationBehavior;
        Stats = stats;

        //DebugWidgets.FloatDrag("test", "test2", $"fovMult-{stats.AimDrift}", () => _fovMultiplier, value => _fovMultiplier = value);
    }

    public void Play(bool mainHand)
    {
        AnimationRequest request = new(_cursorFollowAnimation, 1.0f, 0, "aiming", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), true);
        _animationBehavior?.Play(request, mainHand);
        _aimingSystem.OnAimPointChange += UpdateCursorFollowAnimation;
    }
    public void Stop(bool mainHand)
    {
        _cursorStopFollowAnimation.PlayerKeyFrames[0] = new PLayerKeyFrame(PlayerFrame.Zero, TimeSpan.FromMilliseconds(500), EasingFunctionType.CosShifted);
        AnimationRequest request = new(_cursorStopFollowAnimation, 1.0f, 0, "aiming", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), true);
        _animationBehavior?.Play(request, mainHand);
        _aimingSystem.OnAimPointChange -= UpdateCursorFollowAnimation;
    }

    private readonly Animations.Animation _cursorFollowAnimation = Animations.Animation.Zero.Clone();
    private readonly Animations.Animation _cursorStopFollowAnimation = Animations.Animation.Zero.Clone();
    private const float _animationFollowMultiplier = 0.01f;
    private readonly ClientAimingSystem _aimingSystem;
    private readonly FirstPersonAnimationsBehavior? _animationBehavior;
    private float _fovMultiplier = 0.79f;

    private PLayerKeyFrame GetAimingFrame()
    {
        Vector2 currentAim = _aimingSystem.GetCurrentAim();

        /*DebugWidgets.FloatDrag("tweaks", "animation", "followX", () => _aimingStats.AnimationFollowX, value => _aimingStats.AnimationFollowX = value);
        DebugWidgets.FloatDrag("tweaks", "animation", "followY", () => _aimingStats.AnimationFollowY, value => _aimingStats.AnimationFollowY = value);
        DebugWidgets.FloatDrag("tweaks", "animation", "offsetX", () => _aimingStats.AnimationOffsetX, value => _aimingStats.AnimationOffsetX = value);
        DebugWidgets.FloatDrag("tweaks", "animation", "offsetY", () => _aimingStats.AnimationOffsetY, value => _aimingStats.AnimationOffsetY = value);*/

        float fovAdjustment = 1f - MathF.Cos(ClientSettings.FieldOfView * GameMath.DEG2RAD) * _fovMultiplier;

        float yaw = 0 - currentAim.X * _animationFollowMultiplier * Stats.AnimationFollowX * fovAdjustment + Stats.AnimationOffsetX;
        float pitch = currentAim.Y * _animationFollowMultiplier * Stats.AnimationFollowY * fovAdjustment + Stats.AnimationOffsetY;

        AnimationElement element = new(0, 0, 0, 0, yaw, pitch);

        PlayerFrame frame = new(upperTorso: element);

        return new PLayerKeyFrame(frame, TimeSpan.Zero, EasingFunctionType.Linear);
    }
    private void UpdateCursorFollowAnimation()
    {
        _cursorFollowAnimation.PlayerKeyFrames[0] = GetAimingFrame();
        _cursorFollowAnimation.Hold = true;
    }
}

public class BowServer : RangeWeaponServer
{
    public BowServer(ICoreServerAPI api, Item item) : base(api, item)
    {
        ProjectileSystem = api.ModLoader.GetModSystem<CombatOverhaulSystem>().ServerProjectileSystem ?? throw new Exception();
        Stats = item.Attributes.AsObject<BowStats>();
    }

    public override bool Reload(IServerPlayer player, ItemSlot slot, ItemSlot? ammoSlot, ReloadPacket packet)
    {
        if (ammoSlot?.Itemstack?.Item != null && ammoSlot.Itemstack.Item.HasBehavior<ProjectileBehavior>() && WildcardUtil.Match(Stats.ArrowWildcard, ammoSlot.Itemstack.Item.Code.ToString()))
        {
            ArrowSlots[player.Entity.EntityId] = (ammoSlot.Inventory, ammoSlot.Inventory.GetSlotId(ammoSlot));
            return true;
        }

        if (ammoSlot == null)
        {
            ArrowSlots.Remove(player.Entity.EntityId);
            return true;
        }

        return false;
    }

    public override bool Shoot(IServerPlayer player, ItemSlot slot, ShotPacket packet, Entity shooter)
    {
        if (!ArrowSlots.ContainsKey(player.Entity.EntityId)) return false;

        (InventoryBase inventory, int slotId) = ArrowSlots[player.Entity.EntityId];

        if (inventory.Count <= slotId) return false;

        ItemSlot? arrowSlot = inventory[slotId];

        if (arrowSlot?.Itemstack == null || arrowSlot.Itemstack.StackSize < 1) return false;

        ProjectileStats? stats = arrowSlot.Itemstack.Item.GetCollectibleBehavior<ProjectileBehavior>(true)?.Stats;

        if (stats == null)
        {
            ArrowSlots.Remove(player.Entity.EntityId);
            return false;
        }

        ProjectileSpawnStats spawnStats = new()
        {
            ProducerEntityId = player.Entity.EntityId,
            DamageMultiplier = Stats.ArrowDamageMultiplier,
            DamageStrength = Stats.ArrowDamageTier,
            Position = new Vector3d(packet.Position[0], packet.Position[1], packet.Position[2]),
            Velocity = GetDirectionWithDispersion(packet.Velocity, Stats.DispersionMOA) * Stats.ArrowVelocity
        };

        ProjectileSystem.Spawn(packet.ProjectileId[0], stats, spawnStats, arrowSlot.TakeOut(1), slot.Itemstack, shooter);

        slot.Itemstack.Item.DamageItem(player.Entity.World, player.Entity, slot, 1 + stats.AdditionalDurabilityCost);

        slot.MarkDirty();
        arrowSlot.MarkDirty();
        return true;
    }


    protected readonly Dictionary<long, (InventoryBase, int)> ArrowSlots = new();
    protected readonly BowStats Stats;
}

public sealed class AmmoSelector
{
    public AmmoSelector(ICoreClientAPI api, string ammoWildcard)
    {
        _api = api;
        _ammoWildcard = ammoWildcard;
        SelectedAmmo = ammoWildcard;
    }

    public string SelectedAmmo { get; private set; }

    public int GetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSelection)
    {
        if (_ammoSlots.Count == 0) UpdateAmmoSlots(byPlayer);

        return GetSelectedModeIndex();
    }
    public SkillItem[] GetToolModes(ItemSlot slot, IClientPlayer forPlayer, BlockSelection blockSel)
    {
        UpdateAmmoSlots(forPlayer);


        return GetOrGenerateModes();
    }
    public void SetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSelection, int toolMode)
    {
        if (toolMode == 0 || _ammoSlots.Count < toolMode)
        {
            SelectedAmmo = _ammoWildcard;
            return;
        }

        SelectedAmmo = _ammoSlots[toolMode - 1].Itemstack.Collectible.Code.ToString();
    }

    private readonly ICoreClientAPI _api;
    private readonly string _ammoWildcard;
    private readonly List<ItemSlot> _ammoSlots = new();
    private readonly TimeSpan _generationTimeout = TimeSpan.FromSeconds(1);
    private TimeSpan _lastGenerationTime = TimeSpan.Zero;
    private SkillItem[] _modesCache = Array.Empty<SkillItem>();

    private int GetSelectedModeIndex()
    {
        if (SelectedAmmo == _ammoWildcard) return 0;

        for (int index = 0; index < _ammoSlots.Count; index++)
        {
            if (WildcardUtil.Match(_ammoWildcard, _ammoSlots[index].Itemstack.Item.Code.ToString()))
            {
                return index + 1;
            }
        }

        return 0;
    }
    private void UpdateAmmoSlots(IPlayer player)
    {
        _ammoSlots.Clear();

        player.Entity.WalkInventory(slot =>
        {
            if (slot?.Itemstack?.Item == null) return true;

            if (slot.Itemstack.Item.HasBehavior<ProjectileBehavior>() && WildcardUtil.Match(_ammoWildcard, slot.Itemstack.Item.Code.ToString()))
            {
                AddAmmoStackToList(slot.Itemstack.Clone());
            }

            return true;
        });
    }
    private void AddAmmoStackToList(ItemStack stack)
    {
        foreach (ItemSlot slot in _ammoSlots.Where(slot => slot.Itemstack.Collectible.Code.ToString() == stack.Collectible.Code.ToString()))
        {
            slot.Itemstack.StackSize += stack.StackSize;
            return;
        }

        _ammoSlots.Add(new DummySlot(stack));
    }
    private SkillItem[] GetOrGenerateModes()
    {
        TimeSpan currentTime = TimeSpan.FromMilliseconds(_api.World.ElapsedMilliseconds);
        if (currentTime - _lastGenerationTime < _generationTimeout)
        {
            return _modesCache;
        }

        _lastGenerationTime = currentTime;
        _modesCache = GenerateToolModes();
        return _modesCache;
    }
    private SkillItem[] GenerateToolModes()
    {
        SkillItem[] modes = ToolModesUtils.GetModesFromSlots(_api, _ammoSlots, slot => slot.Itemstack.Collectible.GetHeldItemName(slot.Itemstack));

        SkillItem mode = new()
        {
            Code = new("none-0"),
            Name = Lang.Get("combatoverhaul:toolmode-noselection")
        };

        return modes.Prepend(mode).ToArray();
    }
}

public class BowItem : Item, IHasWeaponLogic, IHasRangedWeaponLogic, IHasIdleAnimations
{
    public BowClient? ClientLogic { get; private set; }
    public BowServer? ServerLogic { get; private set; }

    IClientWeaponLogic? IHasWeaponLogic.ClientLogic => ClientLogic;
    IServerRangedWeaponLogic? IHasRangedWeaponLogic.ServerWeaponLogic => ServerLogic;

    public AnimationRequestByCode IdleAnimation { get; set; }
    public AnimationRequestByCode ReadyAnimation { get; set; }

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        if (api is ICoreClientAPI clientAPI)
        {
            BowStats stats = Attributes.AsObject<BowStats>();
            IdleAnimation = new(stats.IdleAnimation, 1, 1, "main", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false);
            ReadyAnimation = new(stats.ReadyAnimation, 1, 1, "main", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false);
            _stats = stats;
            _ammoSelector = new(clientAPI, _stats.ArrowWildcard);
            _clientApi = clientAPI;

            ClientLogic = new(clientAPI, this, _ammoSelector);
        }

        if (api is ICoreServerAPI serverAPI)
        {
            ServerLogic = new(serverAPI, this);
        }

        _altForInteractions = new()
        {
            MouseButton = EnumMouseButton.None,
            HotKeyCode = "Alt",
            ActionLangCode = "combatoverhaul:interaction-hold-alt"
        };

        _ammoSelection = new()
        {
            ActionLangCode = Lang.Get("combatoverhaul:interaction-ammoselection"),
            HotKeyCodes = new string[1] { "toolmodeselect" },
            MouseButton = EnumMouseButton.None
        };
    }

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

        if (_stats == null) return;

        dsc.AppendLine(Lang.Get("combatoverhaul:iteminfo-range-weapon-damage", _stats.ArrowDamageMultiplier, _stats.ArrowDamageTier));
    }

    public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
    {
        WorldInteraction[] interactions = base.GetHeldInteractionHelp(inSlot);

        return interactions.Append(_ammoSelection).Append(_altForInteractions);
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

    public override void OnHeldAttackStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, ref EnumHandHandling handling)
    {
        handling = EnumHandHandling.PreventDefault;
    }

    private BowStats? _stats;
    private AmmoSelector? _ammoSelector;
    private ICoreClientAPI? _clientApi;
    private WorldInteraction? _altForInteractions;
    private WorldInteraction? _ammoSelection;
}
