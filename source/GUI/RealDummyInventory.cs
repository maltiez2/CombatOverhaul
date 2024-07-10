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
        RealDummySlot slot = new(this);
        slot.DrawUnavailable = false;
        return slot;
    }
}

public class RealDummySlot : ItemSlot
{
    public RealDummySlot(InventoryBase inventory) : base(inventory)
    {
    }

    public override bool CanTake() => false;
    public override bool CanHold(ItemSlot sourceSlot) => false;
}
