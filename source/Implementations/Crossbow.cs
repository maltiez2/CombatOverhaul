using CombatOverhaul.Animations;
using CombatOverhaul.Inputs;
using CombatOverhaul.RangedSystems;
using CombatOverhaul.RangedSystems.Aiming;
using System.Numerics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using VSImGui.Debug;

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

    public AimingStatsJson Aiming { get; set; } = new();
    public float BoltDamageMultiplier { get; set; } = 1;
    public float BoltDamageStrength { get; set; } = 1;
    public float BoltVelocity { get; set; } = 1;
    public string BoltWildcard { get; set; } = "*bolt-*";
}

public class CrossbowClient : RangeWeaponClient
{
    public CrossbowClient(ICoreClientAPI api, Item item) : base(api, item)
    {
        Attachable = item.GetCollectibleBehavior<AnimatableAttachable>(withInheritance: true) ?? throw new Exception("Crossbow should have AnimatableAttachable behavior.");
        BoltTransform = new(item.Attributes["BoltTransform"].AsObject<ModelTransformNoDefaults>(), ModelTransform.BlockDefaultTp());
        AimingSystem = api.ModLoader.GetModSystem<CombatOverhaulSystem>().AimingSystem ?? throw new Exception();
        Stats = item.Attributes.AsObject<CrossbowStats>();
        AimingStats = Stats.Aiming.ToStats();
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

    public override void OnDeselected(EntityPlayer player)
    {
        Attachable.ClearAttachments(player.EntityId);
        StopCursorFollowAnimation(true);
        AimingSystem.StopAiming();
        BoltSlot = null;
    }

    protected readonly AnimatableAttachable Attachable;
    protected readonly ClientAimingSystem AimingSystem;
    protected readonly ModelTransform BoltTransform;
    protected readonly CrossbowStats Stats;
    protected readonly AimingStats AimingStats;
    protected ItemSlot? BoltSlot;

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Active)]
    protected virtual bool Draw(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (state != (int)CrossbowState.Unloaded || eventData.AltPressed) return false;

        AnimationBehavior?.Play(mainHand, Stats.DrawAnimation, callback: () => DrawAnimationCallback(slot, mainHand), animationSpeed: 1.0f);

        state = (int)CrossbowState.Draw;

        return true;
    }

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Active)]
    protected virtual bool Load(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (state != (int)CrossbowState.Drawn || eventData.AltPressed) return false;

        player.WalkInventory(slot =>
        {
            if (slot?.Itemstack?.Item == null) return true;

            if (slot.Itemstack.Item.HasBehavior<ProjectileBehavior>() && WildcardUtil.Match(Stats.BoltWildcard, slot.Itemstack.Item.Code.Path))
            {
                BoltSlot = slot;
                return false;
            }

            return true;
        });

        /*DebugWidgets.FloatDrag("test", "test", "bolt trnasform x", () => BoltTransform.Translation.X, value => BoltTransform.Translation.X = value);
        DebugWidgets.FloatDrag("test", "test", "bolt trnasform y", () => BoltTransform.Translation.Y, value => BoltTransform.Translation.Y = value);
        DebugWidgets.FloatDrag("test", "test", "bolt trnasform z", () => BoltTransform.Translation.Z, value => BoltTransform.Translation.Z = value);
        DebugWidgets.FloatDrag("test", "test", "bolt trnasform rotation x", () => BoltTransform.Rotation.X, value => BoltTransform.Rotation.X = value);
        DebugWidgets.FloatDrag("test", "test", "bolt trnasform rotation y", () => BoltTransform.Rotation.Y, value => BoltTransform.Rotation.Y = value);
        DebugWidgets.FloatDrag("test", "test", "bolt trnasform rotation z", () => BoltTransform.Rotation.Z, value => BoltTransform.Rotation.Z = value);
        DebugWidgets.FloatDrag("test", "test", "bolt trnasform scale", () => BoltTransform.ScaleXYZ.X, value => BoltTransform.Scale = value);*/

        if (BoltSlot == null) return false;

        Attachable.SetAttachment(player.EntityId, "bolt", BoltSlot.Itemstack, BoltTransform);

        AnimationBehavior?.Play(mainHand, Stats.LoadAnimation, callback: () => LoadAnimationCallback(slot, mainHand, BoltSlot));

        state = (int)CrossbowState.Load;

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

        PlayCursorFollowAnimation(mainHand);

        return true;
    }

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Released)]
    protected virtual bool Ease(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
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
                StopCursorFollowAnimation(mainHand);
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
        AnimationBehavior?.Play(mainHand, Stats.ReleaseAnimation, weight: 1000, callback: () => ReleaseAnimationCallback(slot, mainHand, player));

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
        return true;
    }
    protected virtual void ShootCallback(bool success)
    {

    }
    protected virtual bool ReleaseAnimationCallback(ItemSlot slot, bool mainHand, EntityPlayer player)
    {
        Vintagestory.API.MathTools.Vec3d position = player.LocalEyePos + player.Pos.XYZ;
        Vector3 targetDirection = AimingSystem.TargetVec;

        targetDirection = ClientAimingSystem.Zeroing(targetDirection, 1.5f);

        RangedWeaponSystem.Shoot(slot, 1, new((float)position.X, (float)position.Y, (float)position.Z), new(targetDirection.X, targetDirection.Y, targetDirection.Z), mainHand, ShootCallback);

        Attachable.ClearAttachments(player.EntityId);
        return true;
    }


    private const float _animationFollowMultiplier = 0.01f;

    private PLayerKeyFrame GetAimingFrame()
    {
        Vector2 currentAim = AimingSystem.GetCurrentAim();

        /*DebugWidgets.FloatDrag("tweaks", "animation", "followX", () => _aimingStats.AnimationFollowX, value => _aimingStats.AnimationFollowX = value);
        DebugWidgets.FloatDrag("tweaks", "animation", "followY", () => _aimingStats.AnimationFollowY, value => _aimingStats.AnimationFollowY = value);
        DebugWidgets.FloatDrag("tweaks", "animation", "offsetX", () => _aimingStats.AnimationOffsetX, value => _aimingStats.AnimationOffsetX = value);
        DebugWidgets.FloatDrag("tweaks", "animation", "offsetY", () => _aimingStats.AnimationOffsetY, value => _aimingStats.AnimationOffsetY = value);*/

        float yaw = 0 - currentAim.X * _animationFollowMultiplier * AimingStats.AnimationFollowX + AimingStats.AnimationOffsetX;
        float pitch = currentAim.Y * _animationFollowMultiplier * AimingStats.AnimationFollowY + AimingStats.AnimationOffsetY;

        AnimationElement element = new(0, 0, 0, 0, yaw, pitch);

        PlayerFrame frame = new(upperTorso: element);

        return new PLayerKeyFrame(frame, TimeSpan.Zero, EasingFunctionType.Linear);
    }

    private Animations.Animation _cursorFollowAnimation = Animations.Animation.Zero;
    private Animations.Animation _cursorStopFollowAnimation = Animations.Animation.Zero;

    private void PlayCursorFollowAnimation(bool mainHand)
    {
        AnimationRequest request = new(_cursorFollowAnimation, 1.0f, 0, "aiming", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), true);
        AnimationBehavior?.Play(request, mainHand);
        AimingSystem.OnAimPointChange += UpdateCursorFollowAnimation;
    }
    private void StopCursorFollowAnimation(bool mainHand)
    {
        _cursorStopFollowAnimation.PlayerKeyFrames[0] = new PLayerKeyFrame(PlayerFrame.Zero, TimeSpan.FromMilliseconds(500), EasingFunctionType.CosShifted);
        AnimationRequest request = new(_cursorStopFollowAnimation, 1.0f, 0, "aiming", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), true);
        AnimationBehavior?.Play(request, mainHand);
        AimingSystem.OnAimPointChange -= UpdateCursorFollowAnimation;
    }
    private void UpdateCursorFollowAnimation()
    {
        _cursorFollowAnimation.PlayerKeyFrames[0] = GetAimingFrame();
        _cursorFollowAnimation.Hold = true;
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
        if (ammoSlot?.Itemstack?.Item != null && ammoSlot.Itemstack.Item.HasBehavior<ProjectileBehavior>() && WildcardUtil.Match(_stats.BoltWildcard, ammoSlot.Itemstack.Item.Code.Path))
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
            Velocity = Vector3.Normalize(new Vector3(packet.Velocity[0], packet.Velocity[1], packet.Velocity[2])) * _stats.BoltVelocity
        };

        _projectileSystem.Spawn(packet.ProjectileId, stats.Value, spawnStats, boltSlot.TakeOut(1), shooter);

        boltSlot.MarkDirty();

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
            ClientLogic = new(clientAPI, this);

            CrossbowStats stats = Attributes.AsObject<CrossbowStats>();
            IdleAnimation = new(stats.IdleAnimation, 1, 1, "main", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false);
            ReadyAnimation = new(stats.ReadyAnimation, 1, 1, "main", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false);
        }

        if (api is ICoreServerAPI serverAPI)
        {
            ServerLogic = new(serverAPI, this);
        }
    }
}
