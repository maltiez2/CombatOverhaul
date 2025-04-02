using CombatOverhaul.Utils;
using Vintagestory.API.Common;
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
        _player = entity as EntityPlayer ?? throw new Exception("This behavior should be attached only to player");
    }

    public override string PropertyName() => "CombatOverhaul:InInventory";

    public override void OnGameTick(float deltaTime)
    {
        try
        {
            if (_player?.World != null)
            {
                _player.WalkInventory(ProcessSlot);
            }
        }
        catch (Exception exception)
        {
            long entityId = entity.EntityId;
            if (_reportedEntities.Contains(entityId)) return;
            _reportedEntities.Add(entityId);
            
            LoggerUtil.Error(_player.Api, this, $"[OnGameTick] (entity code: {entity.Code}, class: {LoggerUtil.GetCallerTypeName(entity)}) Exception: {exception}");
        }
    }

    private readonly EntityPlayer _player;
    internal static readonly List<long> _reportedEntities = new();

    private bool ProcessSlot(ItemSlot slot)
    {
        if (slot?.Empty != false) return true;

        if (slot.Itemstack?.Collectible?.GetCollectibleInterface<IOnInInventory>() is IOnInInventory collectible)
        {
            collectible.OnInInventory(_player, slot);
        }

        return true;
    }
}
