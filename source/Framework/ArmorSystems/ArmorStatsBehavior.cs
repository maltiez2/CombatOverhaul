using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using static Microsoft.WindowsAPICodePack.Shell.PropertySystem.SystemProperties.System;

namespace CombatOverhaul.Armor;

public interface IAffectsPlayerStats
{
    public Dictionary<string, float> PlayerStats(ItemSlot slot, EntityPlayer player);
}

public sealed class WearableStatsBehavior : EntityBehavior
{
    public WearableStatsBehavior(Entity entity) : base(entity)
    {
        _player = entity as EntityPlayer ?? throw new InvalidDataException("This is player behavior");
    }

    public override string PropertyName() => "CombatOverhaul:WearableStats";
    public Dictionary<string, float> Stats { get; } = new();

    public override void OnGameTick(float deltaTime)
    {
        InventoryBase? inventory = GetGearInventory(_player);

        if (_initialized || inventory == null) return;

        inventory.SlotModified += _ => UpdateStatsValues();
        UpdateStatsValues();

        _initialized = true;
    }

    private readonly EntityPlayer _player;
    private const string _statsCategory = "CombatOverhaul:Armor";
    private bool _initialized = false;

    private static InventoryBase? GetGearInventory(Entity entity)
    {
        return entity.GetBehavior<EntityBehaviorPlayerInventory>().Inventory;
    }

    private void UpdateStatsValues()
    {
        InventoryBase? inventory = GetGearInventory(_player);

        if (inventory == null) return;

        foreach ((string stat, _) in Stats)
        {
            _player.Stats.Remove(stat, _statsCategory);
        }

        Stats.Clear();

        foreach (ItemSlot slot in inventory
            .Where(slot => slot?.Itemstack?.Item != null)
            .Where(slot => slot.Itemstack.Item.GetRemainingDurability(slot.Itemstack) > 0))
        {
            if (slot?.Itemstack?.Item is not IAffectsPlayerStats item) continue;
            
            foreach ((string stat, float value) in item.PlayerStats(slot, _player))
            {
                AddStatValue(stat, value);
            }
        }

        foreach (ItemSlot slot in inventory
            .Where(slot => slot?.Itemstack?.Item != null)
            .Where(slot => slot.Itemstack.Item.GetRemainingDurability(slot.Itemstack) > 0))
        {
            IAffectsPlayerStats? behavior = slot.Itemstack.Item.CollectibleBehaviors.Where(behavior => behavior is IAffectsPlayerStats).FirstOrDefault(defaultValue: null) as IAffectsPlayerStats;

            if (behavior == null) continue;

            foreach ((string stat, float value) in behavior.PlayerStats(slot, _player))
            {
                AddStatValue(stat, value);
            }
        }

        foreach ((string stat, float value) in Stats)
        {
            _player.Stats.Set(stat, _statsCategory, value, false);
        }

        _player.walkSpeed = _player.Stats.GetBlended("walkspeed");
    }
    private void AddStatValue(string stat, float value)
    {
        if (stat == "walkspeed" && value < 0)
        {
            value *= _player.Stats.GetBlended("armorWalkSpeedAffectedness");
        }

        if (stat == "manipulationSpeed" && value < 0)
        {
            value *= _player.Stats.GetBlended("armorManipulationSpeedAffectedness");
        }

        if (!Stats.ContainsKey(stat))
        {
            Stats[stat] = value;
        }
        else
        {
            Stats[stat] += value;
        }
    }
}