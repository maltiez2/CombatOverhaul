using CombatOverhaul.Animations;
using CombatOverhaul.Inputs;
using CombatOverhaul.RangedSystems;
using System.Numerics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace CombatOverhaul.Implementations.Vanilla;

[HasActionEventHandlers]
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
        _arrowInventory.Read(slot, _inventoryId);
        _attachable.ClearAttachments(player.EntityId);
        if (_arrowInventory.Items.Any()) _attachable.SetAttachment(player.EntityId, "arrow", _arrowInventory.Items[0], _arrowTransform);
    }

    public override void OnDeselected(EntityPlayer player)
    {
        _arrowInventory.Clear();
        _attachable.ClearAttachments(player.EntityId);
    }

    private readonly ICoreClientAPI _api;
    private readonly ItemInventoryBuffer _arrowInventory = new();
    private readonly AnimatableAttachable _attachable;
    private readonly ModelTransform _arrowTransform;
    private const string _inventoryId = "arrow";

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Pressed)]
    private bool Load(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand)
    {
        if (state != 0 || eventData.AltPressed) return false;

        return true;
    }

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Active)]
    private bool Aim(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand)
    {
        if (state != 1 || eventData.AltPressed) return false;

        return true;
    }

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Released)]
    private bool Shoot(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand)
    {
        if (state != 2 || eventData.AltPressed) return false;

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
        _arrowInventory.Read(slot, _inventoryId);
        if (_arrowInventory.Items.Count == 0 && ammoSlot?.Itemstack?.Item != null && ammoSlot.Itemstack.Item.HasBehavior<ProjectileBehavior>())
        {
            ItemStack arrow = ammoSlot.TakeOut(1);
            ammoSlot.MarkDirty();

            _arrowInventory.Items.Add(arrow);
            _arrowInventory.Write(slot);

            _arrowInventory.Clear();
            return true;
        }

        if (ammoSlot == null && _arrowInventory.Items.Count != 0)
        {
            ItemStack arrow = _arrowInventory.Items[0];
            _arrowInventory.Write(slot);

            if (!player.Entity.TryGiveItemStack(arrow))
            {
                Api.World.SpawnItemEntity(arrow, player.Entity.Pos.XYZ);
            }

            _arrowInventory.Clear();
            return true;
        }

        _arrowInventory.Clear();
        return false;
    }

    public override bool Shoot(IServerPlayer player, ItemSlot slot, ShotPacket packet)
    {
        _arrowInventory.Read(slot, _inventoryId);

        if (_arrowInventory.Items.Count == 0) return false;

        ProjectileStats stats = _arrowInventory.Items[0].Item.GetCollectibleBehavior<ProjectileBehavior>(true).Stats;
        ProjectileCreationStats spawnStats = new()
        {
            ProducerEntityId = player.Entity.EntityId,
            DamageMultiplier = _damageMultiplier,
            StrengthMultiplier = _strengthMultiplier,
            Position = packet.Position,
            Velocity = Vector3.Normalize(packet.Velocity) * _velocity
        };

        _projectileSystem.Spawn(stats, spawnStats, _arrowInventory.Items[0]);

        _arrowInventory.Clear();

        return true;
    }

    private readonly ItemInventoryBuffer _arrowInventory = new();
    private readonly ProjectileSystemServer _projectileSystem;
    private const string _inventoryId = "arrow";
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
}
