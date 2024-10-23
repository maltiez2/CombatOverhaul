using CombatOverhaul.Utils;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace CombatOverhaul.Armor;

public class ItemWearableArmor : ItemWearable
{
    public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handHandling)
    {
        if (byEntity.Controls.ShiftKey)
        {
            base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handHandling);
            return;
        }

        if (slot.Itemstack.Item == null) return;

        IPlayer? player = (byEntity as EntityPlayer)?.Player;
        if (player == null) return;

        ArmorInventory? inventory = GetGearInventory(byEntity) as ArmorInventory;
        if (inventory == null) return;

        ArmorBehavior? behavior = GetCollectibleBehavior<ArmorBehavior>(true);
        if (behavior == null) return;

        string code = slot.Itemstack.Item.Code;
        ArmorType armorType = behavior.ArmorType;

        try
        {
            IEnumerable<int> slots = inventory.GetSlotBlockingSlotsIndices(armorType);

            foreach (int index in slots)
            {
                ItemStack stack = inventory[index].TakeOutWhole();
                if (!player.InventoryManager.TryGiveItemstack(stack))
                {
                    byEntity.Api.World.SpawnItemEntity(stack, byEntity.ServerPos.AsBlockPos);
                }
                inventory[index].MarkDirty();
            }

            int slotIndex = inventory.GetFittingSlotIndex(armorType);
            inventory[slotIndex].TryFlipWith(slot);

            inventory[slotIndex].MarkDirty();
            slot.MarkDirty();

            handHandling = EnumHandHandling.PreventDefault;
        }
        catch (Exception exception)
        {
            LoggerUtil.Error(api, this, $"Error on equipping '{code}' that occupies {armorType}:\n{exception}");
        }
    }

    protected static InventoryBase? GetGearInventory(Entity entity)
    {
        return entity.GetBehavior<EntityBehaviorPlayerInventory>().Inventory;
    }
}
