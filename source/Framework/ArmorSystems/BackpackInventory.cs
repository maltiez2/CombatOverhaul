using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Common;
using Vintagestory.GameContent;

namespace CombatOverhaul.Armor;

public class InventoryPlayerBackPacksCombatOverhaul : InventoryPlayerBackPacks
{
    public BagInventory BagInventory => bagInv;
    public ItemSlot[] BackpackSlots => bagSlots;

    public InventoryPlayerBackPacksCombatOverhaul(string className, string playerUID, ICoreAPI api) : base(className, playerUID, api)
    {
    }

    public InventoryPlayerBackPacksCombatOverhaul(string inventoryId, ICoreAPI api) : base(inventoryId, api)
    {
    }

    public void ReloadBagInventory()
    {
        bagInv.ReloadBagInventory(this, AppendGearInventorySlots(bagSlots));
    }

    public override void AfterBlocksLoaded(IWorldAccessor world)
    {
        base.AfterBlocksLoaded(world);
        bagInv.ReloadBagInventory(this, AppendGearInventorySlots(bagSlots));
    }

    public override void OnItemSlotModified(ItemSlot slot)
    {
        // Player modified must have some backpack contents
        // lets store that change in the backpack stack
        if (slot is ItemSlotBagContent)
        {
            bagInv.SaveSlotIntoBag((ItemSlotBagContent)slot);
        }
        else
        {
            bagInv.ReloadBagInventory(this, AppendGearInventorySlots(bagSlots));

            if (Api.Side == EnumAppSide.Server)
            {
                (Api.World.PlayerByUid(playerUID) as IServerPlayer)?.BroadcastPlayerData();
            }
        }
    }

    public override object ActivateSlot(int slotId, ItemSlot sourceSlot, ref ItemStackMoveOperation op)
    {
        // Player is about to add a new backpack
        bool tryAddBag = slotId < bagSlots.Length && bagSlots[slotId].Itemstack == null;

        object packet = base.ActivateSlot(slotId, sourceSlot, ref op);

        if (tryAddBag) bagInv.ReloadBagInventory(this, AppendGearInventorySlots(bagSlots));
        return packet;
    }


    public override void DiscardAll()
    {
        for (int i = 0; i < bagSlots.Length; i++)
        {
            if (bagSlots[i].Itemstack != null)
            {
                dirtySlots.Add(i);
            }
            bagSlots[i].Itemstack = null;
        }

        bagInv.ReloadBagInventory(this, AppendGearInventorySlots(bagSlots));
    }

    public override void DropAll(Vec3d pos, int maxStackSize = 0)
    {
        JsonObject? attr = Player?.Entity?.Properties.Attributes;
        int timer = attr == null ? GlobalConstants.TimeToDespawnPlayerInventoryDrops : attr["droppedItemsOnDeathTimer"].AsInt(GlobalConstants.TimeToDespawnPlayerInventoryDrops);

        for (int i = 0; i < bagSlots.Length; i++)
        {
            ItemSlot slot = bagSlots[i];
            if (slot.Itemstack != null)
            {
                EnumHandling handling = EnumHandling.PassThrough;
                slot.Itemstack.Collectible.OnHeldDropped(Api.World, Player, slot, slot.StackSize, ref handling);
                if (handling != EnumHandling.PassThrough) continue;

                dirtySlots.Add(i);
                spawnItemEntity(slot.Itemstack, pos, timer);
                slot.Itemstack = null;
            }
        }

        bagInv.ReloadBagInventory(this, AppendGearInventorySlots(bagSlots));
    }

    private ItemSlot[] AppendGearInventorySlots(ItemSlot[] backpackSlots)
    {
        ItemSlot[] gearSlots = GetGearInventory(Owner)?.ToArray() ?? Array.Empty<ItemSlot>();

        return gearSlots.Concat(backpackSlots).ToArray();
    }

    private static InventoryBase? GetGearInventory(Entity entity)
    {
        return entity?.GetBehavior<EntityBehaviorPlayerInventory>()?.Inventory;
    }
}