using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace CombatOverhaul.Armor;

public sealed class ArmorStatsBehavior : EntityBehavior
{
    public ArmorStatsBehavior(Entity entity) : base(entity)
    {
        _player = entity as EntityPlayer ?? throw new InvalidDataException("This is player behavior");
    }

    public override string PropertyName() => "CO:ArmorStatsBehavior";
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

        foreach (IAffectsPlayerStats armor in inventory.Select(slot => slot.Itemstack?.Item).OfType<IAffectsPlayerStats>())
        {
            foreach ((string stat, float value) in armor.PlayerStats)
            {
                AddStatValue(stat, value);
            }
        }

        foreach (IAffectsPlayerStats armor in inventory
            .Select(slot => slot.Itemstack?.Item)
            .OfType<Item>()
            .Select(item => item.CollectibleBehaviors.Where(behavior => behavior is IAffectsPlayerStats).FirstOrDefault(defaultValue: null))
            .OfType<IAffectsPlayerStats>())
        {
            foreach ((string stat, float value) in armor.PlayerStats)
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