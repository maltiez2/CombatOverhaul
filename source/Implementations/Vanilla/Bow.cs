using CombatOverhaul.Animations;
using CombatOverhaul.Inputs;
using CombatOverhaul.RangedSystems;
using CombatOverhaul.RangedSystems.Aiming;
using System.Numerics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;

namespace CombatOverhaul.Implementations.Vanilla;

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
}

public sealed class BowClient : RangeWeaponClient
{
    public BowClient(ICoreClientAPI api, Item item) : base(api, item)
    {
        _api = api;
        _attachable = item.GetCollectibleBehavior<AnimatableAttachable>(withInheritance: true) ?? throw new Exception("Bow should have AnimatableAttachable behavior.");
        _arrowTransform = new(item.Attributes["arrowTransform"].AsObject<ModelTransformNoDefaults>(), ModelTransform.BlockDefaultTp());
        _aimingSystem = api.ModLoader.GetModSystem<CombatOverhaulSystem>().AimingSystem ?? throw new Exception();

        _stats = item.Attributes.AsObject<BowStats>();
    }

    public override void OnSelected(ItemSlot slot, EntityPlayer player, bool mainHand, ref int state)
    {
        _attachable.ClearAttachments(player.EntityId);

        AnimationRequestByCode request = new(_stats.ReadyAnimation, 1.0f, 1, "main", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), true);
        AnimationBehavior?.Play(request, mainHand);
    }

    public override void OnDeselected(EntityPlayer player)
    {
        _arrowSlot = null;
        _attachable.ClearAttachments(player.EntityId);
        PlayerBehavior?.SetState((int)BowState.Unloaded);
        AnimationBehavior?.Stop("idle");
        _aimingSystem.StopAiming();
    }

    private readonly ICoreClientAPI _api;
    private readonly AnimatableAttachable _attachable;
    private readonly ModelTransform _arrowTransform;
    private readonly ClientAimingSystem _aimingSystem;
    private ItemSlot? _arrowSlot = null;
    private BowStats _stats;

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Pressed)]
    private bool Load(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (state != (int)BowState.Unloaded || eventData.AltPressed) return false;

        /*DebugWidgets.FloatDrag("test", "test", "arrow trnasform x", () => _arrowTransform.Translation.X, value => _arrowTransform.Translation.X = value);
        DebugWidgets.FloatDrag("test", "test", "arrow trnasform y", () => _arrowTransform.Translation.Y, value => _arrowTransform.Translation.Y = value);
        DebugWidgets.FloatDrag("test", "test", "arrow trnasform z", () => _arrowTransform.Translation.Z, value => _arrowTransform.Translation.Z = value);
        DebugWidgets.FloatDrag("test", "test", "arrow trnasform rotation x", () => _arrowTransform.Rotation.X, value => _arrowTransform.Rotation.X = value);
        DebugWidgets.FloatDrag("test", "test", "arrow trnasform rotation y", () => _arrowTransform.Rotation.Y, value => _arrowTransform.Rotation.Y = value);
        DebugWidgets.FloatDrag("test", "test", "arrow trnasform rotation z", () => _arrowTransform.Rotation.Z, value => _arrowTransform.Rotation.Z = value);
        DebugWidgets.FloatDrag("test", "test", "arrow trnasform scale", () => _arrowTransform.ScaleXYZ.X, value => _arrowTransform.Scale = value);*/

        player.WalkInventory(slot =>
        {
            if (slot?.Itemstack?.Item == null) return true;

            if (slot.Itemstack.Item.HasBehavior<ProjectileBehavior>())
            {
                _arrowSlot = slot;
                return false;
            }

            return true;
        });

        if (_arrowSlot == null) return false;

        _attachable.SetAttachment(player.EntityId, "Arrow", _arrowSlot.Itemstack, _arrowTransform);
        RangedWeaponSystem.Reload(slot, _arrowSlot, 1, mainHand, ReloadCallback);

        state = (int)BowState.Load;

        return true;
    }

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Active)]
    private bool Aim(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (state != (int)BowState.Loaded || eventData.AltPressed) return false;

        AnimationRequestByCode request = new(_stats.DrawAnimation, 1.0f, 1, "main", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), true, FullLoadCallback);
        AnimationBehavior?.Play(request, mainHand);

        state = (int)BowState.Draw;

        _aimingSystem.StartAiming(new AimingStats());

        return true;
    }

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Released)]
    private bool Shoot(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        _aimingSystem.StopAiming();

        if (state == (int)BowState.Draw)
        {
            AnimationRequestByCode idleRequest = new(_stats.ReadyAnimation, 1.0f, 1, "main", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), true);
            AnimationBehavior?.Play(idleRequest, true);
            state = (int)BowState.Loaded;
            return true;
        }

        if (state != (int)BowState.Drawn) return false;

        AnimationRequestByCode request = new(_stats.ReadyAnimation, 1.0f, 1, "main", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), true); //new(_stats.ReleaseAnimation, 1.0f, 1, "main", TimeSpan.FromSeconds(0.0), TimeSpan.FromSeconds(0.2), true, ReleasedAnimationCallback);
        AnimationBehavior?.Play(request, mainHand);

        state = 0;

        Vintagestory.API.MathTools.Vec3d position = player.LocalEyePos + player.Pos.XYZ;
        Vintagestory.API.MathTools.Vec3f viewDirection = player.Pos.GetViewVector();
        Vector3 targetDirection = _aimingSystem.TargetVec;

        _arrowSlot = null;
        _attachable.ClearAttachments(player.EntityId);

        RangedWeaponSystem.Shoot(slot, 1, new((float)position.X, (float)position.Y, (float)position.Z), new(targetDirection.X, targetDirection.Y, targetDirection.Z), mainHand, ShootCallback);

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
            AnimationBehavior?.Stop("main");
            PlayerBehavior?.SetState(0);
        }
    }
    private void ShootCallback(bool success)
    {

    }
    private bool FullLoadCallback()
    {
        PlayerBehavior?.SetState((int)BowState.Drawn);
        return true;
    }
    private bool ReleasedAnimationCallback()
    {
        AnimationRequestByCode request = new(_stats.ReadyAnimation, 1.0f, 1, "main", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), true);
        AnimationBehavior?.Play(request, true);

        return true;
    }
}

public class BowServer : RangeWeaponServer
{
    public BowServer(ICoreServerAPI api, Item item) : base(api, item)
    {
        _projectileSystem = api.ModLoader.GetModSystem<CombatOverhaulSystem>().ServerProjectileSystem ?? throw new Exception();
        _damageMultiplier = item.Attributes["arrowDamageMultiplier"].AsFloat();
        _strengthMultiplier = item.Attributes["arrowStrengthMultiplier"].AsFloat();
        _velocity = item.Attributes["arrowVelocity"].AsFloat();
    }

    public override bool Reload(IServerPlayer player, ItemSlot slot, ItemSlot? ammoSlot, ReloadPacket packet)
    {
        if (ammoSlot?.Itemstack?.Item != null && ammoSlot.Itemstack.Item.HasBehavior<ProjectileBehavior>())
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
            DamageMultiplier = _damageMultiplier,
            StrengthMultiplier = _strengthMultiplier,
            Position = new Vector3(packet.Position[0], packet.Position[1], packet.Position[2]),
            Velocity = Vector3.Normalize(new Vector3(packet.Velocity[0], packet.Velocity[1], packet.Velocity[2])) * _velocity
        };

        _projectileSystem.Spawn(packet.ProjectileId, stats.Value, spawnStats, arrowSlot.TakeOut(1), shooter);

        arrowSlot.MarkDirty();
        return true;
    }

    private readonly Dictionary<long, ItemSlot?> _arrowSlots = new();
    private readonly ProjectileSystemServer _projectileSystem;
    private readonly float _damageMultiplier;
    private readonly float _strengthMultiplier;
    private readonly float _velocity;
}

public class BowItem : Item, IHasWeaponLogic, IHasRangedWeaponLogic
{
    public BowClient? ClientLogic { get; private set; }
    public BowServer? ServerLogic { get; private set; }

    IClientWeaponLogic? IHasWeaponLogic.ClientLogic => ClientLogic;
    IServerRangedWeaponLogic? IHasRangedWeaponLogic.ServerWeaponLogic => ServerLogic;

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        if (api is ICoreClientAPI clientAPI)
        {
            ClientLogic = new(clientAPI, this);
        }

        if (api is ICoreServerAPI serverAPI)
        {
            ServerLogic = new(serverAPI, this);
        }
    }

    public override void OnHeldDropped(IWorldAccessor world, IPlayer byPlayer, ItemSlot slot, int quantity, ref EnumHandling handling)
    {
    }
}
