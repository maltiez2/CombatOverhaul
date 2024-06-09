using CombatOverhaul.Animations;
using CombatOverhaul.Inputs;
using CombatOverhaul.RangedSystems;
using System.Numerics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;

namespace CombatOverhaul.Implementations.Vanilla;

public sealed class BowClient : RangeWeaponClient
{
    public BowClient(ICoreClientAPI api, Item item) : base(api, item)
    {
        _api = api;
        _attachable = item.GetCollectibleBehavior<AnimatableAttachable>(withInheritance: true) ?? throw new Exception("Bow should have AnimatableAttachable behavior.");
        _arrowTransform = new(item.Attributes["arrowTransform"].AsObject<ModelTransformNoDefaults>(), ModelTransform.BlockDefaultTp());
    }

    public override void OnSelected(ItemSlot slot, EntityPlayer player, bool mainHand, ref int state)
    {
        _attachable.ClearAttachments(player.EntityId);
    }

    public override void OnDeselected(EntityPlayer player)
    {
        _arrowSlot = null;
        _attachable.ClearAttachments(player.EntityId);
        PlayerBehavior?.SetState(0);
    }

    private readonly ICoreClientAPI _api;
    private readonly AnimatableAttachable _attachable;
    private readonly ModelTransform _arrowTransform;
    private ItemSlot? _arrowSlot = null;

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Pressed)]
    private bool Load(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (state != 0 || eventData.AltPressed) return false;

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

        state = 1;

        return true;
    }

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Active)]
    private bool Aim(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (state != 2 || eventData.AltPressed) return false;

        AnimationRequestByCode request = new("combatoverhaul:bow-draw-simple", 1.0f, 1, "main", TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(0.2), true);

        AnimationBehavior?.Play(request, mainHand);

        state = 3;

        return true;
    }

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Released)]
    private bool Shoot(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (state != 3 || eventData.AltPressed) return false;

        AnimationBehavior?.Stop("main");

        state = 0;

        Vintagestory.API.MathTools.Vec3d position = player.LocalEyePos + player.Pos.XYZ;
        Vintagestory.API.MathTools.Vec3f viewDirection = player.Pos.GetViewVector();

        _arrowSlot = null;
        _attachable.ClearAttachments(player.EntityId);

        Guid id = Guid.NewGuid();

        RangedWeaponSystem.Shoot(slot, id, 1, new((float)position.X, (float)position.Y, (float)position.Z), new(viewDirection.X, viewDirection.Y, viewDirection.Z), mainHand, ShootCallback);

        return true;
    }

    private void ReloadCallback(bool success)
    {
        if (success)
        {
            PlayerBehavior?.SetState(2);
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
            _arrowSlot = ammoSlot;
            return true;
        }

        if (ammoSlot == null)
        {
            _arrowSlot = null;
            return true;
        }

        return false;
    }

    public override bool Shoot(IServerPlayer player, ItemSlot slot, ShotPacket packet, Entity shooter)
    {
        if (_arrowSlot?.Itemstack == null || _arrowSlot.Itemstack.StackSize < 1) return false;

        ProjectileStats? stats = _arrowSlot.Itemstack.Item.GetCollectibleBehavior<ProjectileBehavior>(true)?.Stats;

        if (stats == null)
        {
            _arrowSlot = null;
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

        _projectileSystem.Spawn(packet.ProjectileId, stats.Value, spawnStats, _arrowSlot.TakeOut(1), shooter);

        _arrowSlot.MarkDirty();

        return true;
    }

    private ItemSlot? _arrowSlot = null;
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
