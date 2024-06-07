using CombatOverhaul.Animations;
using CombatOverhaul.Inputs;
using CombatOverhaul.RangedSystems;
using System.Numerics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
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
        PlayerBehavior.SetState(2);
    }

    private readonly ICoreClientAPI _api;
    private readonly AnimatableAttachable _attachable;
    private readonly ModelTransform _arrowTransform;
    private ItemSlot? _arrowSlot = null;

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Pressed)]
    private bool Load(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand)
    {
        if (state != 0 || eventData.AltPressed) return false;


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

        _attachable.SetAttachment(player.EntityId, "arrow", _arrowSlot.Itemstack, _arrowTransform);
        RangedWeaponSystem.Reload(slot, _arrowSlot, 1, mainHand, ReloadCallback);

        state = 1;

        return true;
    }

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Active)]
    private bool Aim(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand)
    {
        if (state != 2 || eventData.AltPressed) return false;

        return true;
    }

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Released)]
    private bool Shoot(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand)
    {
        if (state != 3 || eventData.AltPressed) return false;

        return true;
    }

    private void ReloadCallback(bool success)
    {
        if (success)
        {
            PlayerBehavior.SetState(2);
        }
        else
        {
            PlayerBehavior.SetState(0);
        }
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

    public override bool Shoot(IServerPlayer player, ItemSlot slot, ShotPacket packet)
    {
        if (_arrowSlot?.Itemstack == null || _arrowSlot.Itemstack.StackSize < 1) return false;

        ProjectileStats? stats = _arrowSlot.Itemstack.Item.GetCollectibleBehavior<ProjectileBehavior>(true)?.Stats;

        if (stats ==  null)
        {
            _arrowSlot = null;
            return false;
        }

        ProjectileCreationStats spawnStats = new()
        {
            ProducerEntityId = player.Entity.EntityId,
            DamageMultiplier = _damageMultiplier,
            StrengthMultiplier = _strengthMultiplier,
            Position = packet.Position,
            Velocity = Vector3.Normalize(packet.Velocity) * _velocity
        };

        _projectileSystem.Spawn(stats.Value, spawnStats, _arrowSlot.TakeOut(1));

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
