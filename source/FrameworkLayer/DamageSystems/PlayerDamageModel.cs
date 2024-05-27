using CombatOverhaul.Utils;
using System.Collections.Immutable;
using Vintagestory.API.MathTools;

namespace CombatOverhaul.DamageSystems;

[Flags]
public enum DamageZoneType
{
    None = 0,
    Head = 1,
    Face = 2,
    Neck = 4,
    Torso = 8,
    Arms = 16,
    Hands = 32,
    Legs = 64,
    Feet = 128
}

public sealed class PlayerDamageModel
{
    public readonly ImmutableArray<DamageZoneStats> DamageZones;

    public PlayerDamageModel(DamageZoneStatsJson[] zones)
    {
        DamageZones = zones.Select(zone => zone.ToStats()).Where(zone => zone.ZoneType != DamageZoneType.None).ToImmutableArray();
        _random = new(0.5f, 0.5f, EnumDistribution.UNIFORM);

        _weights = new();
        foreach (DamageZoneType zone in Enum.GetValues<DamageZoneType>())
        {
            _weights[zone] = 0;
        }
    }

    public (DamageZoneType zone, float damageMultiplier) GetZone(DirectionOffset direction, DamageZoneType target = DamageZoneType.None, float multiplier = 1f)
    {
        IEnumerable<DamageZoneStats> zones = DamageZones.Where(zone => zone.Directions.Check(direction));

        foreach ((DamageZoneType zone, _) in _weights)
        {
            _weights[zone] = 0;
        }

        float sum = 0;
        foreach (DamageZoneStats zone in zones)
        {
            float zoneMultiplier = (target | zone.ZoneType) != 0 ? multiplier : 1;
            sum += zone.Coverage * zoneMultiplier;
            _weights[zone.ZoneType] += zone.Coverage * zoneMultiplier;
        }

        foreach ((DamageZoneType zone, _) in _weights)
        {
            _weights[zone] /= sum;
        }

        float randomValue = _random.nextFloat();

        sum = 0;
        foreach ((DamageZoneType zone, float weight) in _weights)
        {
            sum += weight;
            if (sum >= randomValue)
            {
                return (zone, zones.Where(element => element.ZoneType == zone).Select(element => element.DamageMultiplier).Average());
            }
        }

        return (DamageZoneType.None, 1.0f);
    }

    private readonly NatFloat _random;
    private readonly Dictionary<DamageZoneType, float> _weights;
}

public sealed class PlayerDamageModelJson
{
    public DamageZoneStatsJson[] Zones { get; set; } = Array.Empty<DamageZoneStatsJson>();
}

public sealed class DamageZoneStatsJson
{
    public string Zone { get; set; } = "None";
    public float Coverage { get; set; } = 0;
    public float Top { get; set; } = 0;
    public float Bottom { get; set; } = 0;
    public float Left { get; set; } = 0;
    public float Right { get; set; } = 0;
    public float DamageMultiplier { get; set; } = 1;

    public DamageZoneStats ToStats() => new(Enum.Parse<DamageZoneType>(Zone), Coverage, DirectionConstrain.FromDegrees(Top, Bottom, Right, Left), DamageMultiplier);
}

public readonly struct DamageZoneStats
{
    public readonly DamageZoneType ZoneType;
    public readonly float Coverage;
    public readonly DirectionConstrain Directions;
    public readonly float DamageMultiplier;

    public DamageZoneStats(DamageZoneType type, float coverage, DirectionConstrain directions, float damageMultiplier)
    {
        ZoneType = type;
        Coverage = coverage;
        Directions = directions;
    }
}