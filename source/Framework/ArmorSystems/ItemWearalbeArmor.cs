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
    public override void OnCreatedByCrafting(ItemSlot[] inSlots, ItemSlot outputSlot, GridRecipe byRecipe)
    {
        int newDurability = 0;

        if (outputSlot is not DummySlot)
        {
            EnsureConditionExists(outputSlot);
            outputSlot.Itemstack.Attributes.SetFloat("condition", 1);

            if (byRecipe.Name.Path.Contains("repair"))
            {
                CalculateRepairValueProperly(inSlots, outputSlot, out float repairValue, out int matCostPerMatType);

                int curDur = outputSlot.Itemstack.Collectible.GetRemainingDurability(outputSlot.Itemstack);
                int maxDur = GetMaxDurability(outputSlot.Itemstack);

                newDurability = Math.Min(maxDur, (int)(curDur + maxDur * repairValue));
            }
        }

        base.OnCreatedByCrafting(inSlots, outputSlot, byRecipe);

        // Prevent derp in the handbook
        if (outputSlot is DummySlot) return;

        if (byRecipe.Name.Path.Contains("repair"))
        {
            outputSlot.Itemstack.Attributes.SetInt("durability", newDurability);
        }
    }
    public override bool ConsumeCraftingIngredients(ItemSlot[] inSlots, ItemSlot outputSlot, GridRecipe recipe)
    {
        // Consume as much materials in the input grid as needed
        if (recipe.Name.Path.Contains("repair"))
        {
            CalculateRepairValueProperly(inSlots, outputSlot, out float repairValue, out int matCostPerMatType);

            foreach (ItemSlot islot in inSlots)
            {
                if (islot.Empty) continue;

                if (islot.Itemstack.Collectible == this) { islot.Itemstack = null; continue; }

                islot.TakeOut(matCostPerMatType);
            }

            return true;
        }

        return false;
    }

    protected static InventoryBase? GetGearInventory(Entity entity)
    {
        return entity.GetBehavior<EntityBehaviorPlayerInventory>().Inventory;
    }
    protected virtual void EnsureConditionExists(ItemSlot slot)
    {
        // Prevent derp in the handbook
        if (slot is DummySlot) return;

        if (!slot.Itemstack.Attributes.HasAttribute("condition") && api.Side == EnumAppSide.Server)
        {
            if (slot.Itemstack.ItemAttributes?["warmth"].Exists == true && slot.Itemstack.ItemAttributes?["warmth"].AsFloat() != 0)
            {
                if (slot is ItemSlotTrade)
                {
                    slot.Itemstack.Attributes.SetFloat("condition", (float)api.World.Rand.NextDouble() * 0.25f + 0.75f);
                }
                else
                {
                    slot.Itemstack.Attributes.SetFloat("condition", (float)api.World.Rand.NextDouble() * 0.4f);
                }

                slot.MarkDirty();
            }
        }
    }
    protected virtual void CalculateRepairValueProperly(ItemSlot[] inSlots, ItemSlot outputSlot, out float repairValue, out int matCostPerMatType)
    {
        int origMatCount = GetOrigMatCount(inSlots, outputSlot);
        if (origMatCount == 0)
        {
            origMatCount = Attributes["materialCount"].AsInt(1);
        }
        ItemSlot? armorSlot = inSlots.FirstOrDefault(slot => slot.Itemstack?.Collectible is ItemWearable);
        int curDur = outputSlot.Itemstack.Collectible.GetRemainingDurability(armorSlot.Itemstack);
        int maxDur = GetMaxDurability(outputSlot.Itemstack);

        // How much 1x mat repairs in %
        float repairValuePerItem = 2f / origMatCount;
        // How much the mat repairs in durability
        float repairDurabilityPerItem = repairValuePerItem * maxDur;
        // Divide missing durability by repair per item = items needed for full repair 
        int fullRepairMatCount = (int)Math.Max(1, Math.Round((maxDur - curDur) / repairDurabilityPerItem));
        // Limit repair value to smallest stack size of all repair mats
        int minMatStackSize = GetInputRepairCount(inSlots);
        // Divide the cost amongst all mats
        int matTypeCount = GetRepairMatTypeCount(inSlots);

        int availableRepairMatCount = Math.Min(fullRepairMatCount, minMatStackSize * matTypeCount);
        matCostPerMatType = Math.Min(fullRepairMatCount, minMatStackSize);

        // Repairing costs half as many materials as newly creating it
        repairValue = (float)availableRepairMatCount / origMatCount * 2;
    }
    protected virtual int GetRepairMatTypeCount(ItemSlot[] slots)
    {
        List<ItemStack> stackTypes = new();
        foreach (ItemSlot slot in slots)
        {
            if (slot.Empty) continue;
            bool found = false;
            if (slot.Itemstack.Collectible is ItemWearable) continue;

            foreach (ItemStack stack in stackTypes)
            {
                if (slot.Itemstack.Satisfies(stack))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                stackTypes.Add(slot.Itemstack);
            }
        }

        return stackTypes.Count;
    }
}
