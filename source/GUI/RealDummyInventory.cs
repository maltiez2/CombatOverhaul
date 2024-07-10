using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace CombatOverhaul;

public class RealDummyInventory : DummyInventory
{
    public RealDummyInventory(ICoreAPI api, int quantitySlots = 1) : base(api, quantitySlots)
    {
    }

    public override bool CanContain(ItemSlot sinkSlot, ItemSlot sourceSlot)
    {
        return false;
    }

    public override bool CanPlayerAccess(IPlayer player, EntityPos position)
    {
        return false;
    }

    public override bool CanPlayerModify(IPlayer player, EntityPos position)
    {
        return false;
    }

    protected override ItemSlot NewSlot(int i)
    {
        ItemSlot slot = base.NewSlot(i);
        slot.DrawUnavailable = false;
        return slot;
    }
}
