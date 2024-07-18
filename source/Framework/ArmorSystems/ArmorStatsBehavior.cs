using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

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
        if (_initialized || _player.GearInventory == null) return;

        _player.GearInventory.SlotModified += _ => UpdateStatsValues();
        UpdateStatsValues();

        _initialized = true;
    }

    private readonly EntityPlayer _player;
    private const string _statsCategory = "CombatOverhaul:Armor";
    private bool _initialized = false;

    private void UpdateStatsValues()
    {
        foreach ((string stat, _) in Stats)
        {
            _player.Stats.Remove(stat, _statsCategory);
        }

        Stats.Clear();

        foreach (IAffectsPlayerStats armor in _player.GearInventory.Select(slot => slot.Itemstack?.Item).OfType<IAffectsPlayerStats>())
        {
            foreach ((string stat, float value) in armor.PlayerStats)
            {
                AddStatValue(stat, value);
            }
        }

        foreach (IAffectsPlayerStats armor in _player.GearInventory
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
            _player.Stats.Set(stat, _statsCategory, value, true);
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