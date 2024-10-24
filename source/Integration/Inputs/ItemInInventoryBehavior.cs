﻿using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace CombatOverhaul.Inputs;

public interface IOnInInventory
{
    void OnInInventory(EntityPlayer player, ItemSlot slot);
}

public class InInventoryPlayerBehavior : EntityBehavior
{
    public InInventoryPlayerBehavior(Entity entity) : base(entity)
    {
        _player = entity as EntityPlayer ?? throw new Exception("This behavior should be attached to player");
    }

    public override string PropertyName() => "CombatOverhaul:InInventory";

    public override void OnGameTick(float deltaTime)
    {
        _player.WalkInventory(ProcessSlot);
    }

    private readonly EntityPlayer _player;

    private bool ProcessSlot(ItemSlot slot)
    {
        if (slot?.Empty != true) return true;

        if (slot.Itemstack?.Item is IOnInInventory item)
        {
            item.OnInInventory(_player, slot);
        }
        else if (slot.Itemstack?.Block is IOnInInventory block)
        {
            block.OnInInventory(_player, slot);
        }

        return true;
    }
}
