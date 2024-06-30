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


namespace CombatOverhaul.Implementations;

public enum MagazineCrossbowState
{
    Unloaded,
    OpenLid,
    ReadyToLoad,
    Load,
    CloseLid,
    Ready,
    Shoot,
    Shot,
    Return
}

public class MagazineCrossbowStats : WeaponStats
{
    public string OpenLidAnimation { get; set; } = "";
    public string LoadBoltAnimation { get; set; } = "";
    public string CloseLidAnimation { get; set; } = "";
    public string ShootAnimation { get; set; } = "";
    public string ReturnAnimation { get; set; } = "";

    public AimingStatsJson Aiming { get; set; } = new();
    public float BoltDamageMultiplier { get; set; } = 1;
    public float BoltDamageStrength { get; set; } = 1;
    public float BoltVelocity { get; set; } = 1;
    public string BoltWildcard { get; set; } = "@.*(bolt-copper|bolt-flint)";
    public float Zeroing { get; set; } = 1.5f;

    public int MagazineSize { get; set; } = 5;
}

public class MagazineCrossbowClient : RangeWeaponClient
{
    public MagazineCrossbowClient(ICoreClientAPI api, Item item) : base(api, item)
    {
        Attachable = item.GetCollectibleBehavior<AnimatableAttachable>(withInheritance: true) ?? throw new Exception("Crossbow should have AnimatableAttachable behavior.");
        BoltTransform = new(item.Attributes["BoltTransform"].AsObject<ModelTransformNoDefaults>(), ModelTransform.BlockDefaultTp());
        AimingSystem = api.ModLoader.GetModSystem<CombatOverhaulSystem>().AimingSystem ?? throw new Exception();
        Stats = item.Attributes.AsObject<MagazineCrossbowStats>();
        AimingStats = Stats.Aiming.ToStats();
    }

    public override void OnSelected(ItemSlot slot, EntityPlayer player, bool mainHand, ref int state)
    {
        Attachable.ClearAttachments(player.EntityId);

        Inventory.Read(slot, InventoryId);

        if (Inventory.Items.Count != 0)
        {
            state = (int)MagazineCrossbowState.Ready;
            AimingSystem.AimingState = WeaponAimingState.FullCharge;
        }
        else
        {
            state = (int)MagazineCrossbowState.Unloaded;
            AimingSystem.AimingState = WeaponAimingState.Blocked;
        }

        AimingSystem.StartAiming(AimingStats);
        AimingAnimationController?.Play(true);

        Inventory.Clear();
    }
    public override void OnDeselected(EntityPlayer player)
    {
        Attachable.ClearAttachments(player.EntityId);
        AimingAnimationController?.Stop(true);
        AimingSystem.StopAiming();
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
    protected readonly MagazineCrossbowStats Stats;
    protected readonly AimingStats AimingStats;
    protected readonly ItemInventoryBuffer Inventory = new();
    protected const string InventoryId = "magazine";

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Active)]
    protected virtual bool OpenLid(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if ((state != (int)MagazineCrossbowState.Unloaded && state != (int)MagazineCrossbowState.Ready) || eventData.AltPressed) return false;

        Console.WriteLine("OpenLid");

        state = (int)MagazineCrossbowState.OpenLid;

        AnimationBehavior?.Play(mainHand, Stats.OpenLidAnimation, callback: OpenLidCallback);
        AimingSystem.StopAiming();

        return true;
    }
    protected virtual bool OpenLidCallback()
    {
        PlayerBehavior?.SetState((int)MagazineCrossbowState.ReadyToLoad, mainHand: true);
        return true;
    }

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Active)]
    protected virtual bool LoadBolt(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (state != (int)MagazineCrossbowState.ReadyToLoad || eventData.AltPressed) return false;

        Inventory.Read(slot, InventoryId);
        if (Inventory.Items.Count >= Stats.MagazineSize)
        {
            Inventory.Clear();
            return false;
        }
        Inventory.Clear();

        Console.WriteLine("LoadBolt");

        ItemSlot? ammoSlot = null;
        player.WalkInventory(slot =>
        {
            if (slot?.Itemstack?.Item == null) return true;

            if (slot.Itemstack.Item.HasBehavior<ProjectileBehavior>() && WildcardUtil.Match(Stats.BoltWildcard, slot.Itemstack.Item.Code.Path))
            {
                ammoSlot = slot;
                return false;
            }

            return true;
        });
        if (ammoSlot == null) return false;

        Attachable.SetAttachment(player.EntityId, "bolt", ammoSlot.Itemstack, BoltTransform);

        AnimationBehavior?.Play(mainHand, Stats.LoadBoltAnimation, callback: () => LoadBoltCallback(slot, ammoSlot, player));
        state = (int)MagazineCrossbowState.Load;

        return true;
    }
    protected virtual bool LoadBoltCallback(ItemSlot slot, ItemSlot ammoSlot, EntityPlayer player)
    {
        RangedWeaponSystem.Reload(slot, ammoSlot, 1, true, LoadBoltServerCallback);
        //Attachable.ClearAttachments(player.EntityId);
        return true;
    }
    protected virtual void LoadBoltServerCallback(bool success)
    {
        int state = PlayerBehavior?.GetState(mainHand: true) ?? 0;

        if (state == (int)MagazineCrossbowState.Load)
        {
            PlayerBehavior?.SetState((int)MagazineCrossbowState.ReadyToLoad, mainHand: true);
        }
    }

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Released)]
    protected virtual bool CloseLid(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if ((state != (int)MagazineCrossbowState.ReadyToLoad && state != (int)MagazineCrossbowState.Load && state != (int)MagazineCrossbowState.OpenLid) || eventData.AltPressed) return false;

        Console.WriteLine("CloseLid");

        AnimationBehavior?.Play(mainHand, Stats.CloseLidAnimation, callback: () => CloseLidCallback(slot));
        state = (int)MagazineCrossbowState.CloseLid;

        return true;
    }
    protected virtual bool CloseLidCallback(ItemSlot slot)
    {
        PlayerBehavior?.SetState((int)MagazineCrossbowState.Ready, mainHand: true);
        AimingSystem.StartAiming(AimingStats);

        Inventory.Read(slot, InventoryId);
        if (Inventory.Items.Count != 0)
        {
            AimingSystem.AimingState = WeaponAimingState.FullCharge;
        }
        else
        {
            AimingSystem.AimingState = WeaponAimingState.Blocked;
        }
        Inventory.Clear();

        return true;
    }

    [ActionEventHandler(EnumEntityAction.LeftMouseDown, ActionState.Active)]
    protected virtual bool Shoot(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (state != (int)MagazineCrossbowState.Ready || eventData.AltPressed) return false;

        Inventory.Read(slot, InventoryId);
        if (Inventory.Items.Count == 0)
        {
            Inventory.Clear();
            AimingSystem.AimingState = WeaponAimingState.Blocked;
            return false;
        }
        Inventory.Clear();
        AimingSystem.AimingState = WeaponAimingState.FullCharge;

        Console.WriteLine("Shoot");

        AnimationBehavior?.Play(mainHand, Stats.ShootAnimation, callback: () => ShootCallback(slot, player));
        state = (int)MagazineCrossbowState.Shoot;

        return true;
    }
    protected virtual bool ShootCallback(ItemSlot slot, EntityPlayer player)
    {
        Vintagestory.API.MathTools.Vec3d position = player.LocalEyePos + player.Pos.XYZ;
        Vector3 targetDirection = AimingSystem.TargetVec;
        targetDirection = ClientAimingSystem.Zeroing(targetDirection, Stats.Zeroing);
        RangedWeaponSystem.Shoot(slot, 1, new((float)position.X, (float)position.Y, (float)position.Z), new(targetDirection.X, targetDirection.Y, targetDirection.Z), true, _ => { });

        PlayerBehavior?.SetState((int)MagazineCrossbowState.Shot, mainHand: true);

        return true;
    }

    [ActionEventHandler(EnumEntityAction.LeftMouseDown, ActionState.Active)]
    protected virtual bool Return(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (state != (int)MagazineCrossbowState.Shot || eventData.AltPressed) return false;

        Console.WriteLine("Return");

        AnimationBehavior?.Play(mainHand, Stats.ReturnAnimation, callback: ReturnCallback);
        state = (int)MagazineCrossbowState.Return;

        return true;
    }
    protected virtual bool ReturnCallback()
    {
        PlayerBehavior?.SetState((int)MagazineCrossbowState.Ready, mainHand: true);
        return true;
    }
}

public class MagazineCrossbowServer : RangeWeaponServer
{
    public MagazineCrossbowServer(ICoreServerAPI api, Item item) : base(api, item)
    {
        _projectileSystem = api.ModLoader.GetModSystem<CombatOverhaulSystem>().ServerProjectileSystem ?? throw new Exception();
        _stats = item.Attributes.AsObject<MagazineCrossbowStats>();
    }

    public override bool Reload(IServerPlayer player, ItemSlot slot, ItemSlot? ammoSlot, ReloadPacket packet)
    {
        _inventory.Read(slot, _inventoryId);
        if (_inventory.Items.Count >= _stats.MagazineSize) return false;

        if (ammoSlot?.Itemstack?.Item != null && ammoSlot.Itemstack.Item.HasBehavior<ProjectileBehavior>() && WildcardUtil.Match(_stats.BoltWildcard, ammoSlot.Itemstack.Item.Code.Path))
        {
            ItemStack ammo = ammoSlot.TakeOut(1);
            ammoSlot.MarkDirty();
            _inventory.Items.Add(ammo);
            _inventory.Write(slot);
            _inventory.Clear();
            return true;
        }

        return false;
    }

    public override bool Shoot(IServerPlayer player, ItemSlot slot, ShotPacket packet, Entity shooter)
    {
        _inventory.Read(slot, _inventoryId);

        if (_inventory.Items.Count == 0) return false;

        ItemStack ammo = _inventory.Items[0];
        ammo.ResolveBlockOrItem(Api.World);
        _inventory.Items.RemoveAt(0);
        _inventory.Write(slot);
        _inventory.Clear();

        ProjectileStats? stats = ammo.Item?.GetCollectibleBehavior<ProjectileBehavior>(true)?.Stats;

        if (stats == null)
        {
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

        _projectileSystem.Spawn(packet.ProjectileId, stats.Value, spawnStats, ammo, shooter);

        return true;
    }

    private readonly ProjectileSystemServer _projectileSystem;
    private readonly MagazineCrossbowStats _stats;
    private readonly ItemInventoryBuffer _inventory = new();
    private const string _inventoryId = "magazine";
}

public class MagazineCrossbowItem : Item, IHasWeaponLogic, IHasRangedWeaponLogic, IHasIdleAnimations
{
    public MagazineCrossbowClient? ClientLogic { get; private set; }
    public MagazineCrossbowServer? ServerLogic { get; private set; }

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

            MagazineCrossbowStats stats = Attributes.AsObject<MagazineCrossbowStats>();
            IdleAnimation = new(stats.IdleAnimation, 1, 1, "main", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false);
            ReadyAnimation = new(stats.ReadyAnimation, 1, 1, "main", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false);
        }

        if (api is ICoreServerAPI serverAPI)
        {
            ServerLogic = new(serverAPI, this);
        }
    }
}