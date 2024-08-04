using CombatOverhaul.Animations;
using CombatOverhaul.Inputs;
using CombatOverhaul.RangedSystems;
using CombatOverhaul.RangedSystems.Aiming;
using CombatOverhaul.Utils;
using System.Numerics;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace CombatOverhaul.Implementations;

public enum BowState
{
    Unloaded,
    Load,
    Loaded,
    Draw,
    Drawn
}

public class WeaponStats
{
    public string ReadyAnimation { get; set; } = "";
    public string IdleAnimation { get; set; } = "";
}

public sealed class BowStats : WeaponStats
{
    public string DrawAnimation { get; set; } = "";
    public string ReleaseAnimation { get; set; } = "";
    public string TpAimAnimation { get; set; } = "";
    public AimingStatsJson Aiming { get; set; } = new();
    public float ArrowDamageMultiplier { get; set; } = 1;
    public float ArrowDamageStrength { get; set; } = 1;
    public float ArrowVelocity { get; set; } = 1;
    public string ArrowWildcard { get; set; } = "*arrow-*";
    public float Zeroing { get; set; } = 1.5f;
    public float[] DispersionMOA { get; set; } = new float[] { 0, 0 };
}

public sealed class BowClient : RangeWeaponClient
{
    public BowClient(ICoreClientAPI api, Item item, AmmoSelector ammoSelector) : base(api, item)
    {
        _api = api;
        _attachable = item.GetCollectibleBehavior<AnimatableAttachable>(withInheritance: true) ?? throw new Exception("Bow should have AnimatableAttachable behavior.");
        _arrowTransform = new(item.Attributes["ArrowTransform"].AsObject<ModelTransformNoDefaults>(), ModelTransform.BlockDefaultTp());
        _aimingSystem = api.ModLoader.GetModSystem<CombatOverhaulSystem>().AimingSystem ?? throw new Exception();

        _stats = item.Attributes.AsObject<BowStats>();
        _aimingStats = _stats.Aiming.ToStats();
        _ammoSelector = ammoSelector;
    }

    public override void OnSelected(ItemSlot slot, EntityPlayer player, bool mainHand, ref int state)
    {
        _attachable.ClearAttachments(player.EntityId);
    }

    public override void OnDeselected(EntityPlayer player, bool mainHand, ref int state)
    {
        _arrowSlot = null;
        _attachable.ClearAttachments(player.EntityId);
        PlayerBehavior?.SetState((int)BowState.Unloaded);
        _aimingSystem.StopAiming();

        AnimationBehavior?.StopVanillaAnimation(_stats.TpAimAnimation);
    }

    public override void OnRegistered(ActionsManagerPlayerBehavior behavior, ICoreClientAPI api)
    {
        base.OnRegistered(behavior, api);
        _aimingAnimationController = new(_aimingSystem, AnimationBehavior, _aimingStats);
    }

    private readonly ICoreClientAPI _api;
    private readonly AnimatableAttachable _attachable;
    private readonly ModelTransform _arrowTransform;
    private readonly ClientAimingSystem _aimingSystem;
    private ItemSlot? _arrowSlot = null;
    private readonly BowStats _stats;
    private readonly AimingStats _aimingStats;
    private readonly AmmoSelector _ammoSelector;
    private AimingAnimationController? _aimingAnimationController;

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Pressed)]
    private bool Load(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (state != (int)BowState.Unloaded || eventData.AltPressed || !CheckForOtherHandEmpty(mainHand, player)) return false;

        player.WalkInventory(slot =>
        {
            if (slot?.Itemstack?.Item == null) return true;

            if (slot.Itemstack.Item.HasBehavior<ProjectileBehavior>() && WildcardUtil.Match(_ammoSelector.SelectedAmmo, slot.Itemstack.Item.Code.ToString()))
            {
                _arrowSlot = slot;
                return false;
            }

            return true;
        });

        if (_arrowSlot == null)
        {
            player.WalkInventory(slot =>
            {
                if (slot?.Itemstack?.Item == null) return true;

                if (slot.Itemstack.Item.HasBehavior<ProjectileBehavior>() && WildcardUtil.Match(_stats.ArrowWildcard, slot.Itemstack.Item.Code.ToString()))
                {
                    _arrowSlot = slot;
                    return false;
                }

                return true;
            });
        }

        if (_arrowSlot == null) return false;

        _attachable.SetAttachment(player.EntityId, "Arrow", _arrowSlot.Itemstack, _arrowTransform);
        RangedWeaponSystem.Reload(slot, _arrowSlot, 1, mainHand, ReloadCallback);

        state = (int)BowState.Load;

        return true;
    }

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Active)]
    private bool Aim(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (state != (int)BowState.Loaded || eventData.AltPressed || !CheckForOtherHandEmpty(mainHand, player)) return false;

        AnimationRequestByCode request = new(_stats.DrawAnimation, PlayerBehavior?.ManipulationSpeed ?? 1, 1, "main", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), true, FullLoadCallback);
        AnimationBehavior?.Play(request, mainHand);

        state = (int)BowState.Draw;

        _aimingSystem.StartAiming(_aimingStats);
        _aimingSystem.AimingState = WeaponAimingState.Blocked;

        _aimingAnimationController?.Play(mainHand);

        AnimationBehavior?.PlayVanillaAnimation(_stats.TpAimAnimation);

        return true;
    }

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Released)]
    private bool Shoot(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        _aimingSystem.StopAiming();

        AnimationBehavior?.StopVanillaAnimation(_stats.TpAimAnimation);

        if (state == (int)BowState.Draw)
        {
            AnimationBehavior?.PlayReadyAnimation(mainHand);
            state = (int)BowState.Loaded;
            return true;
        }

        if (state != (int)BowState.Drawn) return false;

        AnimationBehavior?.Play(mainHand, _stats.ReleaseAnimation, callback: () => ShootCallback(slot, player, mainHand));

        return true;
    }

    private bool ShootCallback(ItemSlot slot, EntityPlayer player, bool mainHand)
    {
        PlayerBehavior?.SetState(0, mainHand);
        _arrowSlot = null;

        Vintagestory.API.MathTools.Vec3d position = player.LocalEyePos + player.Pos.XYZ;
        Vector3 targetDirection = _aimingSystem.TargetVec;
        targetDirection = ClientAimingSystem.Zeroing(targetDirection, _stats.Zeroing);

        RangedWeaponSystem.Shoot(slot, 1, new((float)position.X, (float)position.Y, (float)position.Z), new(targetDirection.X, targetDirection.Y, targetDirection.Z), mainHand, _ => { });
        _attachable.ClearAttachments(player.EntityId);
        _aimingAnimationController?.Stop(mainHand);

        AnimationBehavior?.PlayReadyAnimation(mainHand);

        return true;
    }

    private void ReloadCallback(bool success)
    {
        if (success)
        {
            PlayerBehavior?.SetState((int)BowState.Loaded);
        }
        else
        {
            AnimationBehavior?.PlayReadyAnimation(true);
            PlayerBehavior?.SetState(0);
        }
    }
    private bool FullLoadCallback()
    {
        PlayerBehavior?.SetState((int)BowState.Drawn);
        _aimingSystem.AimingState = WeaponAimingState.FullCharge;
        return true;
    }
}

public sealed class AimingAnimationController
{
    public AimingAnimationController(ClientAimingSystem aimingSystem, FirstPersonAnimationsBehavior? animationBehavior, AimingStats stats)
    {
        _aimingSystem = aimingSystem;
        _animationBehavior = animationBehavior;
        _aimingStats = stats;
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

    private readonly Animations.Animation _cursorFollowAnimation = Animations.Animation.Zero;
    private readonly Animations.Animation _cursorStopFollowAnimation = Animations.Animation.Zero;
    private const float _animationFollowMultiplier = 0.01f;
    private readonly ClientAimingSystem _aimingSystem;
    private readonly AimingStats _aimingStats;
    private readonly FirstPersonAnimationsBehavior? _animationBehavior;

    private PLayerKeyFrame GetAimingFrame()
    {
        Vector2 currentAim = _aimingSystem.GetCurrentAim();

        /*DebugWidgets.FloatDrag("tweaks", "animation", "followX", () => _aimingStats.AnimationFollowX, value => _aimingStats.AnimationFollowX = value);
        DebugWidgets.FloatDrag("tweaks", "animation", "followY", () => _aimingStats.AnimationFollowY, value => _aimingStats.AnimationFollowY = value);
        DebugWidgets.FloatDrag("tweaks", "animation", "offsetX", () => _aimingStats.AnimationOffsetX, value => _aimingStats.AnimationOffsetX = value);
        DebugWidgets.FloatDrag("tweaks", "animation", "offsetY", () => _aimingStats.AnimationOffsetY, value => _aimingStats.AnimationOffsetY = value);*/

        float yaw = 0 - currentAim.X * _animationFollowMultiplier * _aimingStats.AnimationFollowX + _aimingStats.AnimationOffsetX;
        float pitch = currentAim.Y * _animationFollowMultiplier * _aimingStats.AnimationFollowY + _aimingStats.AnimationOffsetY;

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

public sealed class BowServer : RangeWeaponServer
{
    public BowServer(ICoreServerAPI api, Item item) : base(api, item)
    {
        _projectileSystem = api.ModLoader.GetModSystem<CombatOverhaulSystem>().ServerProjectileSystem ?? throw new Exception();
        _stats = item.Attributes.AsObject<BowStats>();
    }

    public override bool Reload(IServerPlayer player, ItemSlot slot, ItemSlot? ammoSlot, ReloadPacket packet)
    {
        if (ammoSlot?.Itemstack?.Item != null && ammoSlot.Itemstack.Item.HasBehavior<ProjectileBehavior>() && WildcardUtil.Match(_stats.ArrowWildcard, ammoSlot.Itemstack.Item.Code.Path))
        {
            _arrowSlots[player.Entity.EntityId] = ammoSlot;
            return true;
        }

        if (ammoSlot == null)
        {
            _arrowSlots[player.Entity.EntityId] = null;
            return true;
        }

        return false;
    }

    public override bool Shoot(IServerPlayer player, ItemSlot slot, ShotPacket packet, Entity shooter)
    {
        if (!_arrowSlots.ContainsKey(player.Entity.EntityId)) return false;

        ItemSlot? arrowSlot = _arrowSlots[player.Entity.EntityId];

        if (arrowSlot?.Itemstack == null || arrowSlot.Itemstack.StackSize < 1) return false;

        ProjectileStats? stats = arrowSlot.Itemstack.Item.GetCollectibleBehavior<ProjectileBehavior>(true)?.Stats;

        if (stats == null)
        {
            _arrowSlots[player.Entity.EntityId] = null;
            return false;
        }

        ProjectileSpawnStats spawnStats = new()
        {
            ProducerEntityId = player.Entity.EntityId,
            DamageMultiplier = _stats.ArrowDamageMultiplier,
            DamageStrength = _stats.ArrowDamageStrength,
            Position = new Vector3(packet.Position[0], packet.Position[1], packet.Position[2]),
            Velocity = GetDirectionWithDispersion(packet.Velocity, _stats.DispersionMOA) * _stats.ArrowVelocity
        };

        _projectileSystem.Spawn(packet.ProjectileId, stats, spawnStats, arrowSlot.TakeOut(1), shooter);

        arrowSlot.MarkDirty();
        return true;
    }

    private readonly Dictionary<long, ItemSlot?> _arrowSlots = new();
    private readonly ProjectileSystemServer _projectileSystem;
    private readonly BowStats _stats;
}

public class AmmoSelector
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
    }

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

        if (_stats == null) return;

        dsc.AppendLine(Lang.Get("combatoverhaul:iteminfo-range-weapon-damage", _stats.ArrowDamageMultiplier, _stats.ArrowDamageStrength));
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

    private BowStats? _stats;
    private AmmoSelector? _ammoSelector;
    private ICoreClientAPI? _clientApi;
}
