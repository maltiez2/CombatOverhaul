﻿using CombatOverhaul.Animations;
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


namespace CombatOverhaul.Implementations;

public enum MuzzleloaderState
{
    Unloaded,
    Loading,
    Loaded,
    Priming,
    Primed,
    Aim,
    Shoot
}

public enum MuzzleloaderLoadingStage
{
    Unloaded,
    Loading,
    Priming
}

public class MuzzleloaderStats : WeaponStats
{
    public string LoadAnimation { get; set; } = "";
    public string PrimeAnimation { get; set; } = "";
    public string AimAnimation { get; set; } = "";
    public string ShootAnimation { get; set; } = "";
    public string AimAnimationOffhand { get; set; } = "";
    public string ShootAnimationOffhand { get; set; } = "";

    public AimingStatsJson Aiming { get; set; } = new();
    public float BulletDamageMultiplier { get; set; } = 1;
    public float BulletDamageStrength { get; set; } = 1;
    public float BulletVelocity { get; set; } = 1;
    public string BulletWildcard { get; set; } = "*bullet-*";
    public float Zeroing { get; set; } = 1.5f;

    public int MagazineSize { get; set; } = 1;
    public int BulletLoadedPerReload { get; set; } = 1;
    public int WaddingUsedPerReload { get; set; } = 1;
    public int LoadPowderConsumption { get; set; } = 1;
    public int PrimePowderConsumption { get; set; } = 1;
    public string FlaskWildcard { get; set; } = "*powderflask-*";
    public string WaddingWildcard { get; set; } = "*linenpatch*";
    public string LoadingRequirementWildcard { get; set; } = "";
    public string PrimingRequirementWildcard { get; set; } = "";
}

public class MuzzleloaderClient : RangeWeaponClient
{
    public MuzzleloaderClient(ICoreClientAPI api, Item item) : base(api, item)
    {
        Attachable = item.GetCollectibleBehavior<AnimatableAttachable>(withInheritance: true) ?? throw new Exception("Crossbow should have AnimatableAttachable behavior.");
        BoltTransform = new(item.Attributes["BoltTransform"].AsObject<ModelTransformNoDefaults>(), ModelTransform.BlockDefaultTp());
        AimingSystem = api.ModLoader.GetModSystem<CombatOverhaulSystem>().AimingSystem ?? throw new Exception();
        Stats = item.Attributes.AsObject<MuzzleloaderStats>();
        AimingStats = Stats.Aiming.ToStats();
    }

    public override void OnSelected(ItemSlot slot, EntityPlayer player, bool mainHand, ref int state)
    {
        Attachable.ClearAttachments(player.EntityId);

        MuzzleloaderLoadingStage stage = GetLoadingStage<MuzzleloaderLoadingStage>(slot);
        state = stage switch
        {
            MuzzleloaderLoadingStage.Unloaded => (int)MuzzleloaderState.Unloaded,
            MuzzleloaderLoadingStage.Loading => (int)MuzzleloaderState.Loaded,
            MuzzleloaderLoadingStage.Priming => (int)MuzzleloaderState.Primed,
            _ => (int)MuzzleloaderState.Unloaded
        };
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
    protected readonly MuzzleloaderStats Stats;
    protected readonly AimingStats AimingStats;
    protected readonly ItemInventoryBuffer Inventory = new();
    protected const string InventoryId = "magazine";
    protected const string LoadingStageAttribute = "CombatOverhaul:loading-stage";

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Active)]
    protected virtual bool Load(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (!CheckState(state, MuzzleloaderState.Unloaded)) return false;
        if (eventData.AltPressed || !mainHand) return false;
        if (Stats.LoadingRequirementWildcard != "" && !CheckRequirement(Stats.LoadingRequirementWildcard, player)) return false;

        SetState(MuzzleloaderState.Loading);

        return true;
    }
    protected virtual bool LoadCallback(bool mainHand)
    {
        if (CheckState(mainHand, MuzzleloaderState.Loading)) SetState(MuzzleloaderState.Loaded);
        return true;
    }

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Active)]
    protected virtual bool Prime(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (!CheckState(state, MuzzleloaderState.Loaded)) return false;
        if (eventData.AltPressed || !mainHand) return false;
        if (Stats.PrimingRequirementWildcard != "" && !CheckRequirement(Stats.PrimingRequirementWildcard, player)) return false;

        SetState(MuzzleloaderState.Priming);

        return true;
    }
    protected virtual bool PrimeCallback(bool mainHand)
    {
        return true;
    }
    protected virtual void PrimeServerCallback(bool success, bool mainHand)
    {
        if (CheckState(mainHand, MuzzleloaderState.Priming) && success)
        {
            SetState(MuzzleloaderState.Primed);
        }
        else
        {
            SetState(MuzzleloaderState.Loaded);
        }
    }

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Active)]
    protected virtual bool Aim(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (!CheckState(state, MuzzleloaderState.Primed)) return false;
        if (eventData.AltPressed) return false;

        SetState(MuzzleloaderState.Aim);

        return true;
    }
    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Released)]
    protected virtual bool Ease(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        return false;
    }

    [ActionEventHandler(EnumEntityAction.LeftMouseDown, ActionState.Pressed)]
    protected virtual bool Shoot(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (!CheckState(state, MuzzleloaderState.Aim)) return false;
        if (eventData.AltPressed) return false;

        SetState(MuzzleloaderState.Shoot);

        return true;
    }
    protected virtual bool ShootCallback(bool mainHand)
    {
        return true;
    }
    protected virtual void ShootServerCallback(bool success)
    {
        SetState(MuzzleloaderState.Aim);
    }

    protected static byte[] SerializeLoadingStage<TStage>(TStage stage)
        where TStage : struct, Enum
    {

        int stageInt = (int)Enum.ToObject(typeof(TStage), stage);
        return BitConverter.GetBytes(stageInt);
    }
    protected static TStage GetLoadingStage<TStage>(ItemSlot slot)
        where TStage : struct, Enum
    {
        int stage = slot.Itemstack?.Attributes.GetAsInt(LoadingStageAttribute, 0) ?? 0;
        return (TStage)Enum.ToObject(typeof(TStage), stage);
    }
    protected static bool CheckRequirement(string requirementWildcard, EntityPlayer player)
    {
        ItemSlot? flaskSlot = null;
        player.WalkInventory(slot =>
        {
            if (slot?.Itemstack?.Item == null) return true;

            if (
                WildcardUtil.Match(requirementWildcard, slot.Itemstack.Item.Code.Path))
            {
                flaskSlot = slot;
                return false;
            }

            return true;
        });
        return flaskSlot != null;
    }
}

public class MuzzleloaderServer : RangeWeaponServer
{
    public MuzzleloaderServer(ICoreServerAPI api, Item item) : base(api, item)
    {
        Stats = item.Attributes.AsObject<MuzzleloaderStats>();
    }

    public override bool Reload(IServerPlayer player, ItemSlot slot, ItemSlot? ammoSlot, ReloadPacket packet)
    {
        MuzzleloaderLoadingStage currentStage = GetLoadingStage<MuzzleloaderLoadingStage>(packet);
        //MuzzleloaderLoadingStage finishedStage = GetLoadingStage<MuzzleloaderLoadingStage>(slot);
        //if (currentStage < finishedStage) return false;

        int powderNeeded = currentStage switch
        {
            MuzzleloaderLoadingStage.Loading => Stats.LoadPowderConsumption,
            MuzzleloaderLoadingStage.Priming => Stats.PrimePowderConsumption,
            _ => 1
        };

        ItemSlot? flask = GetFlask(player, powderNeeded);
        ItemSlot? wadding = GetWadding(player);

        if (flask?.Itemstack?.Item == null || wadding?.Itemstack?.Item == null) return false;

        if (ammoSlot != null)
        {
            Inventory.Read(slot, InventoryId);
            if (Inventory.Items.Count >= Stats.MagazineSize) return false;

            if (
                ammoSlot.Itemstack?.Item != null &&
                ammoSlot.Itemstack.Item.HasBehavior<ProjectileBehavior>() &&
                WildcardUtil.Match(Stats.BulletWildcard, ammoSlot.Itemstack.Item.Code.Path) &&
                ammoSlot.Itemstack.StackSize > Stats.BulletLoadedPerReload)
            {
                for (int count = 0; count < Stats.BulletLoadedPerReload; count++)
                {
                    ItemStack ammo = ammoSlot.TakeOut(1);
                    Inventory.Items.Add(ammo);
                }
                
                ammoSlot.MarkDirty();
                Inventory.Write(slot);
                Inventory.Clear();
            }
            else
            {
                return false;
            }
        }

        flask.Itemstack.Attributes.SetInt("durability", slot.Itemstack.Collectible.GetRemainingDurability(slot.Itemstack) - powderNeeded);
        flask.MarkDirty();

        wadding.TakeOut(Stats.WaddingUsedPerReload);
        wadding.MarkDirty();

        SetLoadingStage(slot, currentStage);

        return true;
    }

    public override bool Shoot(IServerPlayer player, ItemSlot slot, ShotPacket packet, Entity shooter)
    {
        MuzzleloaderLoadingStage finishedStage = GetLoadingStage<MuzzleloaderLoadingStage>(slot);
        if (finishedStage != LastStage) return false;

        Inventory.Read(slot, InventoryId);

        if (Inventory.Items.Count == 0) return false;

        ItemStack ammo = Inventory.Items[0];
        ammo.ResolveBlockOrItem(Api.World);
        Inventory.Items.RemoveAt(0);
        Inventory.Write(slot);
        int ammoLeft = Inventory.Items.Count;
        Inventory.Clear();

        ProjectileStats? stats = ammo.Item?.GetCollectibleBehavior<ProjectileBehavior>(true)?.Stats;

        if (stats == null)
        {
            return false;
        }

        ProjectileSpawnStats spawnStats = new()
        {
            ProducerEntityId = player.Entity.EntityId,
            DamageMultiplier = Stats.BulletDamageMultiplier,
            DamageStrength = Stats.BulletDamageStrength,
            Position = new Vector3(packet.Position[0], packet.Position[1], packet.Position[2]),
            Velocity = Vector3.Normalize(new Vector3(packet.Velocity[0], packet.Velocity[1], packet.Velocity[2])) * Stats.BulletVelocity
        };

        ProjectileSystem.Spawn(packet.ProjectileId, stats.Value, spawnStats, ammo, shooter);

        if (ammoLeft == 0) SetLoadingStage(slot, MuzzleloaderLoadingStage.Unloaded);

        return true;
    }

    protected readonly MuzzleloaderStats Stats;
    protected readonly ItemInventoryBuffer Inventory = new();
    protected const string InventoryId = "magazine";
    protected const string LoadingStageAttribute = "CombatOverhaul:loading-stage";
    protected readonly MuzzleloaderLoadingStage LastStage = MuzzleloaderLoadingStage.Priming;

    protected static TStage GetLoadingStage<TStage>(ReloadPacket packet)
        where TStage : struct, Enum
    {
        
        int stage = BitConverter.ToInt32(packet.Data, 0);
        return (TStage)Enum.ToObject(typeof(TStage), stage);
    }
    protected static TStage GetLoadingStage<TStage>(ItemSlot slot)
        where TStage : struct, Enum
    {
        int stage = slot.Itemstack?.Attributes.GetAsInt(LoadingStageAttribute, 0) ?? 0;
        return (TStage)Enum.ToObject(typeof(TStage), stage);
    }
    protected static void SetLoadingStage<TStage>(ItemSlot slot, TStage stage)
        where TStage : struct, Enum
    {
        slot.Itemstack?.Attributes.SetInt(LoadingStageAttribute, (int)Enum.ToObject(typeof(TStage), stage));
        slot.MarkDirty();
    }

    protected ItemSlot? GetFlask(IServerPlayer player, int powderNeeded)
    {
        ItemSlot? flaskSlot = null;
        player.Entity.WalkInventory(slot =>
        {
            if (slot?.Itemstack?.Item == null) return true;

            if (
                WildcardUtil.Match(Stats.FlaskWildcard, slot.Itemstack.Item.Code.Path) &&
                slot.Itemstack.Item.GetRemainingDurability(slot.Itemstack) > powderNeeded)
            {
                flaskSlot = slot;
                return false;
            }

            return true;
        });
        return flaskSlot;
    }
    protected ItemSlot? GetWadding(IServerPlayer player)
    {
        ItemSlot? waddingSlot = null;
        player.Entity.WalkInventory(slot =>
        {
            if (slot?.Itemstack?.Item == null) return true;

            if (
                WildcardUtil.Match(Stats.WaddingWildcard, slot.Itemstack.Item.Code.Path) &&
                slot.Itemstack.StackSize > Stats.WaddingUsedPerReload)
            {
                waddingSlot = slot;
                return false;
            }

            return true;
        });
        return waddingSlot;
    }
}

public class MuzzleloaderItem : Item, IHasWeaponLogic, IHasRangedWeaponLogic, IHasIdleAnimations
{
    public MuzzleloaderClient? ClientLogic { get; private set; }
    public MuzzleloaderServer? ServerLogic { get; private set; }

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

            MuzzleloaderStats stats = Attributes.AsObject<MuzzleloaderStats>();
            IdleAnimation = new(stats.IdleAnimation, 1, 1, "main", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false);
            ReadyAnimation = new(stats.ReadyAnimation, 1, 1, "main", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false);
        }

        if (api is ICoreServerAPI serverAPI)
        {
            ServerLogic = new(serverAPI, this);
        }
    }
}