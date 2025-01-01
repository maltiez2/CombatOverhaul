using CombatOverhaul.DamageSystems;
using CombatOverhaul.Utils;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.Common;
using Vintagestory.GameContent;

namespace CombatOverhaul.Armor;

public class EntityCOArmorStand : EntityHumanoid
{
    EntityBehaviorArmorStandInventory invbh;
    float fireDamage;
    public override bool IsCreature { get { return false; } }

    int CurPose
    {
        get { return WatchedAttributes.GetInt("curPose"); }
        set { WatchedAttributes.SetInt("curPose", value); }
    }

    public EntityCOArmorStand() { }

    public override ItemSlot? RightHandItemSlot => invbh?.Inventory[ArmorInventory._totalSlotsNumber];
    public override ItemSlot? LeftHandItemSlot => invbh?.Inventory[ArmorInventory._totalSlotsNumber + 1];


    public override void Initialize(EntityProperties properties, ICoreAPI api, long InChunkIndex3d)
    {
        base.Initialize(properties, api, InChunkIndex3d);

        invbh = GetBehavior<EntityBehaviorArmorStandInventory>();
    }

    string[] poses = new string[] { "idle", "lefthandup", "righthandup", "twohandscross" };

    public override void OnInteract(EntityAgent byEntity, ItemSlot slot, Vec3d hitPosition, EnumInteractMode mode)
    {
        IPlayer plr = (byEntity as EntityPlayer)?.Player;
        if (plr != null && !byEntity.World.Claims.TryAccess(plr, Pos.AsBlockPos, EnumBlockAccessFlags.Use))
        {
            plr.InventoryManager.ActiveHotbarSlot.MarkDirty();
            WatchedAttributes.MarkAllDirty();
            return;
        }

        if (mode == EnumInteractMode.Interact && byEntity.RightHandItemSlot?.Itemstack?.Collectible is ItemWrench)
        {
            AnimManager.StopAnimation(poses[CurPose]);
            CurPose = (CurPose + 1) % poses.Length;
            AnimManager.StartAnimation(new AnimationMetaData() { Animation = poses[CurPose], Code = poses[CurPose] }.Init());
            return;
        }

        if (mode == EnumInteractMode.Interact && byEntity.RightHandItemSlot != null)
        {
            ItemSlot handslot = byEntity.RightHandItemSlot;
            if (handslot.Empty)
            {
                // Start from armor slot because it can't wear clothes atm
                for (int i = 0; i < invbh.Inventory.Count; i++)
                {
                    ItemSlot gslot = invbh.Inventory[i];
                    if (gslot.Empty) continue;
                    if (gslot.Itemstack.Collectible?.Code == null) { gslot.Itemstack = null; continue; }

                    if (gslot.TryPutInto(byEntity.World, handslot) > 0)
                    {
                        byEntity.World.Logger.Audit("{0} Took 1x{1} from Armor Stand at {2}.",
                            byEntity.GetName(),
                            handslot.Itemstack.Collectible.Code,
                             ServerPos.AsBlockPos
                        );
                        return;
                    }
                }
            }
            else
            {
                if (slot.Itemstack.Collectible.Tool != null || slot.Itemstack.ItemAttributes?["toolrackTransform"].Exists == true)
                {
                    var collectibleCode = handslot.Itemstack.Collectible.Code;
                    if (handslot.TryPutInto(byEntity.World, RightHandItemSlot) == 0)
                    {
                        handslot.TryPutInto(byEntity.World, LeftHandItemSlot);
                    }

                    byEntity.World.Logger.Audit("{0} Put 1x{1} onto Armor Stand at {2}.",
                        byEntity.GetName(),
                        collectibleCode,
                         ServerPos.AsBlockPos
                    );

                    return;
                }

                if (!ItemSlotCharacter.IsDressType(slot.Itemstack, EnumCharacterDressType.ArmorBody) && !ItemSlotCharacter.IsDressType(slot.Itemstack, EnumCharacterDressType.ArmorHead) && !ItemSlotCharacter.IsDressType(slot.Itemstack, EnumCharacterDressType.ArmorLegs))
                {

                    (byEntity.World.Api as ICoreClientAPI)?.TriggerIngameError(this, "cantplace", "Cannot place dresses or other non-armor items on armor stands");

                    return;
                }
            }


            WeightedSlot sinkslot = invbh.Inventory.GetBestSuitedSlot(handslot);
            if (sinkslot.weight > 0 && sinkslot.slot != null)
            {
                var collectibleCode = handslot.Itemstack.Collectible.Code;
                handslot.TryPutInto(byEntity.World, sinkslot.slot);

                byEntity.World.Logger.Audit("{0} Put 1x{1} onto Armor Stand at {2}.",
                    byEntity.GetName(),
                    collectibleCode,
                     ServerPos.AsBlockPos
                );
                return;
            }

            bool empty = true;
            for (int i = 0; i < invbh.Inventory.Count; i++)
            {
                ItemSlot gslot = invbh.Inventory[i];
                empty &= gslot.Empty;
            }

            if (empty && byEntity.Controls.ShiftKey)
            {
                ItemStack stack = new ItemStack(byEntity.World.GetItem(Code));
                if (!byEntity.TryGiveItemStack(stack))
                {
                    byEntity.World.SpawnItemEntity(stack, ServerPos.XYZ);
                }
                byEntity.World.Logger.Audit("{0} Took 1x{1} from Armor Stand at {2}.",
                    byEntity.GetName(),
                    stack.Collectible.Code,
                     ServerPos.AsBlockPos
                );
                Die();
                return;
            }
        }



        if (!Alive || World.Side == EnumAppSide.Client || mode == 0)
        {
            return;
        }


        base.OnInteract(byEntity, slot, hitPosition, mode);
    }



    public override bool ReceiveDamage(DamageSource damageSource, float damage)
    {
        if (damageSource.Source == EnumDamageSource.Internal && damageSource.Type == EnumDamageType.Fire) fireDamage += damage;
        if (fireDamage > 4) Die();

        return base.ReceiveDamage(damageSource, damage);
    }

}

public class EntityBehaviorCOArmorStandInventory : EntityBehaviorArmorStandInventory
{
    public override string PropertyName() => "coarmorstandinventory";
    EntityAgent eagent;
    public override InventoryBase Inventory => inv;

    public override string InventoryClassName => "inventory";

    ArmorStandArmorInventory inv;

    public EntityBehaviorCOArmorStandInventory(Entity entity) : base(entity)
    {
        eagent = entity as EntityAgent;
        inv = new ArmorStandArmorInventory(null, null);
    }

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        Api = entity.World.Api;

        inv.LateInitialize("gearinv-" + entity.EntityId, Api);
        loadInv();

        eagent.WatchedAttributes.RegisterModifiedListener("wearablesInv", wearablesModified);

        base.Initialize(properties, attributes);
    }

    private void wearablesModified()
    {
        loadInv();
        eagent.MarkShapeModified();
    }
}


public class ArmorStandArmorSlot : ItemSlot
{
    public ArmorType ArmorType { get; }
    public ArmorType StoredArmoredType => GetStoredArmorType();

    public ArmorStandArmorSlot(InventoryBase inventory, ArmorType armorType) : base(inventory)
    {
        ArmorType = armorType;
        _inventory = inventory as ArmorStandArmorInventory ?? throw new Exception();
    }

    public override bool CanHold(ItemSlot sourceSlot)
    {
        if (DrawUnavailable || !base.CanHold(sourceSlot) || !IsArmor(sourceSlot.Itemstack.Collectible, out IArmor? armor)) return false;

        if (armor == null || !_inventory.CanHoldArmorPiece(armor)) return false;

        return armor.ArmorType.Intersect(ArmorType);
    }

    private readonly ArmorStandArmorInventory _inventory;

    private ArmorType GetStoredArmorType()
    {
        if (Itemstack?.Item != null && IsArmor(Itemstack.Collectible, out IArmor? armor) && armor != null)
        {
            return armor.ArmorType;
        }
        else
        {
            return ArmorType.Empty;
        }
    }

    private static bool IsArmor(CollectibleObject item, out IArmor? armor)
    {
        if (item is IArmor armorItem)
        {
            armor = armorItem;
            return true;
        }

        CollectibleBehavior? behavior = item.CollectibleBehaviors.FirstOrDefault(x => x is IArmor);

        if (behavior is not IArmor armorBehavior)
        {
            armor = null;
            return false;
        }

        armor = armorBehavior;
        return true;
    }
}

public class ArmorStandArmorInventory : InventoryBase
{
    ItemSlot[] slots;

    public ArmorStandArmorInventory(string className, string id, ICoreAPI api) : base(className, id, api)
    {
        slots = GenEmptySlots(ArmorInventory._totalSlotsNumber + 6);
        baseWeight = 2.5f;
    }

    public ArmorStandArmorInventory(string inventoryId, ICoreAPI api) : base(inventoryId, api)
    {
        slots = GenEmptySlots(ArmorInventory._totalSlotsNumber + 6);
        baseWeight = 2.5f;
    }

    public override int Count
    {
        get { return slots.Length; }
    }

    public override ItemSlot this[int slotId] { get { return slots[slotId]; } set { slots[slotId] = value; } }

    public override void FromTreeAttributes(ITreeAttribute tree)
    {
        List<ItemSlot> modifiedSlots = new List<ItemSlot>();
        slots = SlotsFromTreeAttributes(tree, slots, modifiedSlots);
        for (int i = 0; i < modifiedSlots.Count; i++) DidModifyItemSlot(modifiedSlots[i]);
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        SlotsToTreeAttributes(slots, tree);
    }

    public bool IsSlotAvailable(ArmorType armorType) => !slots.Where(entry => !entry.Empty).OfType<ArmorStandArmorSlot>().Any(entry => entry.StoredArmoredType.Intersect(armorType));
    public bool CanHoldArmorPiece(ArmorType armorType)
    {
        return !slots.Where(entry => !entry.Empty).OfType<ArmorStandArmorSlot>().Any(entry => entry.StoredArmoredType.Intersect(armorType));
    }
    public bool CanHoldArmorPiece(IArmor armor) => CanHoldArmorPiece(armor.ArmorType);

    protected override ItemSlot NewSlot(int slotId)
    {
        if (slotId == ArmorInventory._totalSlotsNumber || slotId == ArmorInventory._totalSlotsNumber + 1) return new ItemSlotSurvival(this);
        if (slotId > ArmorInventory._totalSlotsNumber + 1)
        {
            return new ItemSlotBackpack(this);
        }

        ArmorStandArmorSlot slot = new(this, ArmorInventory.ArmorTypeFromIndex(slotId));

        return slot;
    }


    public override void DiscardAll()
    {
        base.DiscardAll();
        for (int i = 0; i < Count; i++)
        {
            DidModifyItemSlot(this[i]);
        }
    }


    public override void OnOwningEntityDeath(Vec3d pos)
    {
        // Don't drop contents on death
    }
}