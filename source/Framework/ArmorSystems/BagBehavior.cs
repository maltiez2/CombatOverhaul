using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace CombatOverhaul.Armor;


public class ItemSlotBagContentWithWildcardMatch : ItemSlotBagContent
{
    public string[] CanHoldWildcard { get; private set; }
    public ItemStack SourceBag { get; set; }

    public ItemSlotBagContentWithWildcardMatch(InventoryBase inventory, int BagIndex, int SlotIndex, EnumItemStorageFlags storageType, string? color, string[] canHoldWildcard) : base(inventory, BagIndex, SlotIndex, storageType)
    {
        CanHoldWildcard = canHoldWildcard;
        HexBackgroundColor = color;
    }

    public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge)
    {
        if (!CanHold(sourceSlot))
        {
            return false;
        }

        return base.CanTakeFrom(sourceSlot, priority);
    }

    public override bool CanHold(ItemSlot sourceSlot)
    {
        if (base.CanHold(sourceSlot) && sourceSlot?.Itemstack?.Collectible?.Code != null)
        {
            bool matchWithoutDomain = WildcardUtil.Match(CanHoldWildcard, sourceSlot.Itemstack.Collectible.Code.Path);
            bool matchWithDomain = WildcardUtil.Match(CanHoldWildcard, sourceSlot.Itemstack.Collectible.Code.ToString());

            return matchWithoutDomain || matchWithDomain;
        }

        return false;
    }
}

public class GearEquipableBag : CollectibleBehavior, IHeldBag, IAttachedInteractions
{
    public string[] CanHoldWildcard { get; private set; } = new string[] { "*" };
    public string? SlotColor { get; private set; } = null;
    public int SlotsNumber { get; private set; } = 0;

    public GearEquipableBag(CollectibleObject collObj) : base(collObj)
    {
    }

    public override void Initialize(JsonObject properties)
    {
        base.Initialize(properties);

        CanHoldWildcard = properties["canHoldWildcards"].AsArray().Select(element => element.AsString("*")).ToArray();
        SlotsNumber = properties["slotsNumber"].AsInt(0);
        SlotColor = properties["color"].AsString(null);
    }

    public void Clear(ItemStack backPackStack)
    {
        ITreeAttribute? stackBackPackTree = backPackStack.Attributes.GetTreeAttribute("backpack");

        if (stackBackPackTree == null) return;

        TreeAttribute slots = new();

        for (int slotIndex = 0; slotIndex < SlotsNumber; slotIndex++)
        {
            slots["slot-" + slotIndex] = new ItemstackAttribute(null);
        }

        stackBackPackTree["slots"] = slots;
    }

    public ItemStack?[] GetContents(ItemStack bagstack, IWorldAccessor world)
    {
        ITreeAttribute backPackTree = bagstack.Attributes.GetTreeAttribute("backpack");
        if (backPackTree == null) return Array.Empty<ItemStack?>();

        List<ItemStack?> contents = new();
        ITreeAttribute slotsTree = backPackTree.GetTreeAttribute("slots");

        foreach ((_, IAttribute attribute) in slotsTree.SortedCopy())
        {
            ItemStack? contentStack = (ItemStack?)attribute?.GetValue();

            if (contentStack != null)
            {
                contentStack.ResolveBlockOrItem(world);
            }

            contents.Add(contentStack);
        }

        return contents.ToArray();
    }

    public virtual bool IsEmpty(ItemStack bagstack)
    {
        ITreeAttribute backPackTree = bagstack.Attributes.GetTreeAttribute("backpack");
        if (backPackTree == null) return true;
        ITreeAttribute slotsTree = backPackTree.GetTreeAttribute("slots");

        foreach (KeyValuePair<string, IAttribute> val in slotsTree)
        {
            IItemStack stack = (IItemStack)val.Value?.GetValue();
            if (stack != null && stack.StackSize > 0) return false;
        }

        return true;
    }

    public virtual int GetQuantitySlots(ItemStack bagstack) => SlotsNumber;

    public void Store(ItemStack bagstack, ItemSlotBagContent slot)
    {
        ITreeAttribute stackBackPackTree = bagstack.Attributes.GetTreeAttribute("backpack");
        ITreeAttribute slotsTree = stackBackPackTree.GetTreeAttribute("slots");

        slotsTree["slot-" + slot.SlotIndex] = new ItemstackAttribute(slot.Itemstack);
    }

    public virtual string GetSlotBgColor(ItemStack bagstack)
    {
        return bagstack.ItemAttributes["backpack"]["slotBgColor"].AsString(null);
    }

    const int defaultFlags = (int)(EnumItemStorageFlags.General | EnumItemStorageFlags.Agriculture | EnumItemStorageFlags.Alchemy | EnumItemStorageFlags.Jewellery | EnumItemStorageFlags.Metallurgy | EnumItemStorageFlags.Outfit);
    public virtual EnumItemStorageFlags GetStorageFlags(ItemStack bagstack)
    {
        return (EnumItemStorageFlags)defaultFlags;
    }

    public List<ItemSlotBagContent?> GetOrCreateSlots(ItemStack bagstack, InventoryBase parentinv, int bagIndex, IWorldAccessor world)
    {
        List<ItemSlotBagContent?> bagContents = new();

        EnumItemStorageFlags flags = (EnumItemStorageFlags)defaultFlags;
        int quantitySlots = SlotsNumber;

        ITreeAttribute stackBackPackTree = bagstack.Attributes.GetTreeAttribute("backpack");
        if (stackBackPackTree == null)
        {
            stackBackPackTree = new TreeAttribute();
            ITreeAttribute slotsTree = new TreeAttribute();

            for (int slotIndex = 0; slotIndex < quantitySlots; slotIndex++)
            {
                ItemSlotBagContentWithWildcardMatch slot = new(parentinv, bagIndex, slotIndex, flags, SlotColor, CanHoldWildcard);
                slot.SourceBag = bagstack;
                bagContents.Add(slot);
                slotsTree["slot-" + slotIndex] = new ItemstackAttribute(null);
            }

            stackBackPackTree["slots"] = slotsTree;
            bagstack.Attributes["backpack"] = stackBackPackTree;
        }
        else
        {
            ITreeAttribute slotsTree = stackBackPackTree.GetTreeAttribute("slots");

            foreach (KeyValuePair<string, IAttribute> val in slotsTree)
            {
                int slotIndex = val.Key.Split("-")[1].ToInt();
                ItemSlotBagContentWithWildcardMatch slot = new(parentinv, bagIndex, slotIndex, flags, SlotColor, CanHoldWildcard);
                slot.SourceBag = bagstack;

                if (val.Value?.GetValue() != null)
                {
                    ItemstackAttribute attr = (ItemstackAttribute)val.Value;
                    slot.Itemstack = attr.value;
                    slot.Itemstack.ResolveBlockOrItem(world);
                }

                while (bagContents.Count <= slotIndex) bagContents.Add(null);
                bagContents[slotIndex] = slot;
            }
        }

        return bagContents;
    }


    public void OnAttached(ItemSlot itemslot, int slotIndex, Entity toEntity, EntityAgent byEntity)
    {

    }

    public void OnDetached(ItemSlot itemslot, int slotIndex, Entity fromEntity, EntityAgent byEntity)
    {
        getOrCreateContainerWorkspace(slotIndex, fromEntity, null).Close((byEntity as EntityPlayer).Player);
    }


    public AttachedContainerWorkspace getOrCreateContainerWorkspace(int slotIndex, Entity onEntity, Action onRequireSave)
    {
        return ObjectCacheUtil.GetOrCreate(onEntity.Api, "att-cont-workspace-" + slotIndex + "-" + onEntity.EntityId + "-" + collObj.Id, () => new AttachedContainerWorkspace(onEntity, onRequireSave));
    }

    public AttachedContainerWorkspace getContainerWorkspace(int slotIndex, Entity onEntity)
    {
        return ObjectCacheUtil.TryGet<AttachedContainerWorkspace>(onEntity.Api, "att-cont-workspace-" + slotIndex + "-" + onEntity.EntityId + "-" + collObj.Id);
    }


    public virtual void OnInteract(ItemSlot bagSlot, int slotIndex, Entity onEntity, EntityAgent byEntity, Vec3d hitPosition, EnumInteractMode mode, ref EnumHandling handled, Action onRequireSave)
    {
        EntityControls controls = byEntity.MountedOn?.Controls ?? byEntity.Controls;
        if (!controls.Sprint)
        {
            handled = EnumHandling.PreventDefault;
            getOrCreateContainerWorkspace(slotIndex, onEntity, onRequireSave).OnInteract(bagSlot, slotIndex, onEntity, byEntity, hitPosition);
        }
    }

    public void OnReceivedClientPacket(ItemSlot bagSlot, int slotIndex, Entity onEntity, IServerPlayer player, int packetid, byte[] data, ref EnumHandling handled, Action onRequireSave)
    {
        int targetSlotIndex = packetid >> 11;

        if (slotIndex != targetSlotIndex) return;

        int first10Bits = (1 << 11) - 1;
        packetid = packetid & first10Bits;

        getOrCreateContainerWorkspace(slotIndex, onEntity, onRequireSave).OnReceivedClientPacket(player, packetid, data, bagSlot, slotIndex, ref handled);
    }

    public bool OnTryAttach(ItemSlot itemslot, int slotIndex, Entity toEntity)
    {
        return true;
    }

    public bool OnTryDetach(ItemSlot itemslot, int slotIndex, Entity fromEntity)
    {
        return IsEmpty(itemslot.Itemstack);
    }

    public void OnEntityDespawn(ItemSlot itemslot, int slotIndex, Entity onEntity, EntityDespawnData despawn)
    {
        getContainerWorkspace(slotIndex, onEntity)?.OnDespawn(despawn);
    }

    public void OnEntityDeath(ItemSlot itemslot, int slotIndex, Entity onEntity, DamageSource damageSourceForDeath)
    {
        ItemStack?[] contents = GetContents(itemslot.Itemstack, onEntity.World);
        foreach (ItemStack? stack in contents)
        {
            if (stack == null) continue;
            onEntity.World.SpawnItemEntity(stack, onEntity.Pos.XYZ);
        }
    }
}