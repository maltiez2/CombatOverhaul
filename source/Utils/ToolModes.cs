using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace CombatOverhaul.Utils;

public static class ToolModesUtils
{
    public static SkillItem[] GetModesFromSlots(ICoreClientAPI api, IEnumerable<ItemSlot> slots, System.Func<ItemSlot, string> descriptionGetter)
    {
        List<SkillItem> modes = new();
        int index = 0;
        foreach (ItemSlot slot in slots)
        {
            if (slot.Itemstack == null) continue;
            modes.Add(new SkillItem
            {
                Code = new AssetLocation($"{slot.Itemstack.Collectible.Code}-{index++}"),
                Name = descriptionGetter(slot),
                Data = slot
            });

            modes[^1].RenderHandler = GetItemStackRenderCallback(slot, api, ColorUtil.WhiteArgb);
        }

        return modes.ToArray();
    }
    public static RenderSkillItemDelegate GetItemStackRenderCallback(ItemSlot slot, ICoreClientAPI clientApi, int color)
    {
        return (AssetLocation code, float dt, double posX, double posY) =>
        {
            double size = GuiElementPassiveItemSlot.unscaledSlotSize + GuiElementItemSlotGridBase.unscaledSlotPadding;
            double scaledSize = GuiElement.scaled(size - 5);

            clientApi?.Render.RenderItemstackToGui(
                slot,
                posX + (scaledSize / 2),
                posY + (scaledSize / 2),
                100,
                (float)GuiElement.scaled(GuiElementPassiveItemSlot.unscaledItemSize),
                color,
                showStackSize: true);
        };
    }
}
