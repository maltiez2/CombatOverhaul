using CombatOverhaul.DamageSystems;
using CombatOverhaul.Utils;
using Newtonsoft.Json.Linq;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace CombatOverhaul.Armor;

[Flags]
public enum ArmorLayers
{
    None = 0,
    Skin = 1,
    Middle = 2,
    Outer = 4
}

public sealed class ArmorTypeJson
{
    public string[] Layers { get; set; } = Array.Empty<string>();
    public string[] Slots { get; set; } = Array.Empty<string>();

    public ArmorType ToArmorType()
    {
        return new(
            Layers.Select(Enum.Parse<ArmorLayers>).Aggregate((first, second) => first | second),
            Slots.Select(Enum.Parse<DamageZone>).Aggregate((first, second) => first | second)
            );
    }
}

public readonly struct ArmorType
{
    public readonly ArmorLayers Layers;
    public readonly DamageZone Slots;

    public ArmorType(ArmorLayers layers, DamageZone slots)
    {
        Layers = layers;
        Slots = slots;
    }

    public bool Intersect(ArmorLayers layer, DamageZone slot) => (Layers & layer) != 0 && (Slots & slot) != 0;
    public bool Intersect(ArmorType type) => (Layers & type.Layers) != 0 && (Slots & type.Slots) != 0;

    public static ArmorType Combine(ArmorType first, ArmorType second) => new(first.Layers | second.Layers, first.Slots | second.Slots);
    public static ArmorType Combine(IEnumerable<ArmorType> armorTypes) => armorTypes.Aggregate(Combine);
    public static ArmorType Empty => new(ArmorLayers.None, DamageZone.None);

    public override string ToString()
    {
        ArmorLayers layersValue = Layers;
        string layers = layersValue == ArmorLayers.None ? "None" : Enum.GetValues<ArmorLayers>().Where(value => (value & layersValue) != 0).Select(value => value.ToString()).Aggregate((first, second) => $"{first}, {second}");

        DamageZone slotsValue = Slots;
        string slots = slotsValue == DamageZone.None ? "None" : Enum.GetValues<DamageZone>().Where(value => (value & slotsValue) != 0).Select(value => value.ToString()).Aggregate((first, second) => $"{first}, {second}");

        return $"({layers}|{slots})";
    }

    public string LayersToTranslatedString()
    {
        ArmorLayers layersValue = Layers;
        return layersValue == ArmorLayers.None ? Lang.Get("combatoverhaul:armor-layer-None") : Enum.GetValues<ArmorLayers>().Where(value => (value & layersValue) != 0).Select(value => Lang.Get($"combatoverhaul:armor-layer-{value}")).Aggregate((first, second) => $"{first}, {second}");
    }
    public string ZonesToTranslatedString()
    {
        DamageZone slotsValue = Slots;
        return slotsValue == DamageZone.None ? Lang.Get("combatoverhaul:damage-zone-None") : Enum.GetValues<DamageZone>().Where(value => (value & slotsValue) != 0).Select(value => Lang.Get($"combatoverhaul:damage-zone-{value}")).Aggregate((first, second) => $"{first}, {second}");
    }
}

public interface IArmor
{
    public ArmorType ArmorType { get; }
    public DamageResistData Resists { get; }
}

public interface IModularArmor
{
    public ArmorType ArmorType { get; }
    public DamageResistData GetResists(ItemSlot slot, ArmorType type);
}

public sealed class ArmorStatsJson
{
    public string[] Layers { get; set; } = Array.Empty<string>();
    public string[] Zones { get; set; } = Array.Empty<string>();
    public Dictionary<string, float> Resists { get; set; } = new();
    public Dictionary<string, float> FlatReduction { get; set; } = new();
    public Dictionary<string, float> PlayerStats { get; set; } = new();
}

public class ArmorBehavior : CollectibleBehavior, IArmor, IAffectsPlayerStats
{
    public ArmorBehavior(CollectibleObject collObj) : base(collObj)
    {
    }

    public ArmorType ArmorType { get; protected set; } = new(ArmorLayers.None, DamageZone.None);
    public DamageResistData Resists { get; protected set; } = new();
    public Dictionary<string, float> Stats { get; protected set; } = new();
    public Dictionary<string, float> PlayerStats(ItemSlot slot, EntityPlayer player) => Stats;

    public override void Initialize(JsonObject properties)
    {
        base.Initialize(properties);

        ArmorStatsJson stats = properties.AsObject<ArmorStatsJson>();

        Stats = stats.PlayerStats;

        if (!stats.Layers.Any() || !stats.Zones.Any())
        {
            return;
        }

        ArmorType = new(stats.Layers.Select(Enum.Parse<ArmorLayers>).Aggregate((first, second) => first | second), stats.Zones.Select(Enum.Parse<DamageZone>).Aggregate((first, second) => first | second));
        Resists = new(
            stats.Resists.ToDictionary(entry => Enum.Parse<EnumDamageType>(entry.Key), entry => entry.Value),
            stats.FlatReduction.ToDictionary(entry => Enum.Parse<EnumDamageType>(entry.Key), entry => entry.Value));
    }
    public void Initialize(ArmorType armorType, DamageResistData resists, Dictionary<string, float> stats)
    {
        JsonObject properties = new(new JObject());
        base.Initialize(properties);

        Stats = stats;
        ArmorType = armorType;
        Resists = resists;
    }

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        dsc.AppendLine(Lang.Get("combatoverhaul:armor-layers-info", ArmorType.LayersToTranslatedString()));
        dsc.AppendLine(Lang.Get("combatoverhaul:armor-zones-info", ArmorType.ZonesToTranslatedString()));
        if (Resists.Resists.Values.Any(value => value != 0))
        {
            dsc.AppendLine(Lang.Get("combatoverhaul:armor-fraction-protection"));
            foreach ((EnumDamageType type, float level) in Resists.Resists.Where(entry => entry.Value > 0))
            {
                string damageType = Lang.Get($"combatoverhaul:damage-type-{type}");
                dsc.AppendLine($"  {damageType}: {level}");
            }
        }

        if (Resists.FlatDamageReduction.Values.Any(value => value != 0))
        {
            dsc.AppendLine(Lang.Get("combatoverhaul:armor-flat-protection"));
            foreach ((EnumDamageType type, float level) in Resists.FlatDamageReduction.Where(entry => entry.Value > 0))
            {
                string damageType = Lang.Get($"combatoverhaul:damage-type-{type}");
                dsc.AppendLine($"  {damageType}: {level}");
            }
        }

        if (Stats.Values.Any(value => value != 0))
        {
            dsc.AppendLine(Lang.Get("combatoverhaul:stat-stats"));
            foreach ((string stat, float value) in Stats)
            {
                if (value != 0f) dsc.AppendLine($"  {Lang.Get($"combatoverhaul:stat-{stat}")}: {value * 100:F1}%");
            }
        }
        
        dsc.AppendLine();
    }
}

public class WearableWithStatsBehavior : CollectibleBehavior, IAffectsPlayerStats
{
    public WearableWithStatsBehavior(CollectibleObject collObj) : base(collObj)
    {
    }
    public Dictionary<string, float> Stats { get; set; } = new();
    public Dictionary<string, float> PlayerStats(ItemSlot slot, EntityPlayer player) => Stats;

    public override void Initialize(JsonObject properties)
    {
        base.Initialize(properties);

        ArmorStatsJson stats = properties.AsObject<ArmorStatsJson>();

        Stats = stats.PlayerStats;
    }

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        if (Stats.Values.Any(value => value != 0))
        {
            dsc.AppendLine(Lang.Get("combatoverhaul:stat-stats"));
            foreach ((string stat, float value) in Stats)
            {
                if (value != 0f) dsc.AppendLine($"  {Lang.Get($"combatoverhaul:stat-{stat}")}: {value * 100:F1}%");
            }
            dsc.AppendLine();
        }
    }
}