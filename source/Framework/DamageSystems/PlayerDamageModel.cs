﻿using CombatOverhaul.Colliders;
using CombatOverhaul.MeleeSystems;
using CombatOverhaul.Utils;
using System.Collections.Immutable;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace CombatOverhaul.DamageSystems;

[Flags]
public enum DamageZone
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

public sealed class PlayerDamageModelBehavior : EntityBehavior
{
    public PlayerDamageModelBehavior(Entity entity) : base(entity)
    {
    }

    public override string PropertyName() => "PlayerDamageModel";

    public PlayerDamageModel DamageModel { get; private set; } = new(Array.Empty<DamageZoneStatsJson>());
    public Dictionary<DamageZone, DamageResistData> Resists { get; private set; } = new();
    public readonly ImmutableDictionary<string, DamageZone> CollidersToZones = new Dictionary<string, DamageZone>()
    {
        { "LowerTorso", DamageZone.Torso },
        { "UpperTorso", DamageZone.Torso },
        { "Head", DamageZone.Head },
        { "Neck", DamageZone.Neck },
        { "UpperArmR", DamageZone.Arms },
        { "UpperArmL", DamageZone.Arms },
        { "LowerArmR", DamageZone.Hands },
        { "LowerArmL", DamageZone.Hands },
        { "UpperFootL", DamageZone.Legs },
        { "UpperFootR", DamageZone.Legs },
        { "LowerFootL", DamageZone.Feet },
        { "LowerFootR", DamageZone.Feet }
    }.ToImmutableDictionary();

    public DamageBlockStats? CurrentDamageBlock { get; set; } = null;

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        if (attributes.KeyExists("damageModel"))
        {
            PlayerDamageModelJson stats = attributes["damageModel"].AsObject<PlayerDamageModelJson>();

            DamageModel = new(stats.Zones);
        }
    }

    public override void AfterInitialized(bool onFirstSpawn)
    {
        _colliders = entity.GetBehavior<CollidersEntityBehavior>();
        entity.GetBehavior<EntityBehaviorHealth>().onDamaged += OnReceiveDamage;
    }

    private CollidersEntityBehavior? _colliders;

    private float OnReceiveDamage(float damage, DamageSource damageSource)
    {
        (DamageZone damageZone, float multiplier) = DetermineHitZone(damageSource);

        ApplyBlock(damageSource, damageZone, ref damage);

        ApplyArmorResists(damageSource, damageZone, ref damage);

        damage *= multiplier;

        return damage;
    }

    private (DamageZone zone, float multiplier) DetermineHitZone(DamageSource damageSource)
    {
        DamageZone damageZone;
        float multiplier;
        if (_colliders != null && damageSource is ILocationalDamage locationalDamageSource)
        {
            damageZone = CollidersToZones[_colliders.CollidersIds[locationalDamageSource.Collider]];
            multiplier = DamageModel.GetMultiplier(damageZone);
        }
        else if (damageSource is IDirectionalDamage directionalDamage)
        {
            (damageZone, multiplier) = DamageModel.GetZone(directionalDamage.Direction, directionalDamage.Target, directionalDamage.WeightMultiplier);
        }
        else if (damageSource.SourceEntity != null)
        {
            DirectionOffset direction = DirectionOffset.GetDirection(entity, damageSource.SourceEntity);

            (damageZone, multiplier) = DamageModel.GetZone(direction);
        }
        else
        {
            damageZone = DamageZone.None;
            multiplier = 1.0f;
        }

        return (damageZone, multiplier);
    }
    private void ApplyBlock(DamageSource damageSource, DamageZone zone, ref float damage)
    {
        if (CurrentDamageBlock == null) return;
        if ((zone & CurrentDamageBlock.ZoneType) == 0) return;

        if (damageSource is IDirectionalDamage directionalDamage)
        {
            if (!CurrentDamageBlock.Directions.Check(directionalDamage.Direction)) return;
        }
        else if (damageSource.SourceEntity != null)
        {
            DirectionOffset offset = DirectionOffset.GetDirection(entity, damageSource.SourceEntity);

            if (!CurrentDamageBlock.Directions.Check(offset)) return;
        }

        DamageData data = new(damageSource.Type, 0);
        if (damageSource is ITypedDamage typedDamage)
        {
            data = typedDamage.DamageTypeData;
            typedDamage.DamageTypeData = CurrentDamageBlock.Resists.ApplyResist(typedDamage.DamageTypeData, ref damage);
        }
        else
        {
            _ = CurrentDamageBlock.Resists.ApplyResist(data, ref damage);
        }

        CurrentDamageBlock.Callback.Invoke(data);
    }
    private void ApplyArmorResists(DamageSource damageSource, DamageZone zone, ref float damage)
    {
        if (damageSource is ITypedDamage typedDamage)
        {
            typedDamage.DamageTypeData = Resists[zone].ApplyResist(typedDamage.DamageTypeData, ref damage);
        }
        else
        {
            DamageData data = new(damageSource.Type, 0);
            _ = Resists[zone].ApplyResist(data, ref damage);
        }
    }
}
public sealed class PlayerDamageModel
{
    public readonly ImmutableArray<DamageZoneStats> DamageZones;

    public PlayerDamageModel(DamageZoneStatsJson[] zones)
    {
        DamageZones = zones.Select(zone => zone.ToStats()).Where(zone => zone.ZoneType != DamageZone.None).ToImmutableArray();
        _random = new(0.5f, 0.5f, EnumDistribution.UNIFORM);

        _weights = new();
        foreach (DamageZone zone in Enum.GetValues<DamageZone>())
        {
            _weights[zone] = 0;
        }
    }

    public (DamageZone zone, float damageMultiplier) GetZone(DirectionOffset direction, DamageZone target = DamageZone.None, float multiplier = 1f)
    {
        IEnumerable<DamageZoneStats> zones = DamageZones.Where(zone => zone.Directions.Check(direction));

        foreach ((DamageZone zone, _) in _weights)
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

        foreach ((DamageZone zone, _) in _weights)
        {
            _weights[zone] /= sum;
        }

        float randomValue = _random.nextFloat();

        sum = 0;
        foreach ((DamageZone zone, float weight) in _weights)
        {
            sum += weight;
            if (sum >= randomValue)
            {
                return (zone, zones.Where(element => (element.ZoneType & zone) != 0).Select(element => element.DamageMultiplier).Average());
            }
        }

        return (DamageZone.None, 1.0f);
    }

    public float GetMultiplier(DamageZone zone)
    {
        return DamageZones.Where(element => (element.ZoneType & zone) != 0).Select(element => element.DamageMultiplier).Average();
    }

    private readonly NatFloat _random;
    private readonly Dictionary<DamageZone, float> _weights;
}

public sealed class PlayerDamageModelJson
{
    public DamageZoneStatsJson[] Zones { get; set; } = Array.Empty<DamageZoneStatsJson>();
}

public interface IDirectionalDamage
{
    DirectionOffset Direction { get; }
    DamageZone Target { get; }
    float WeightMultiplier { get; }
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

    public DamageZoneStats ToStats() => new(Enum.Parse<DamageZone>(Zone), Coverage, DirectionConstrain.FromDegrees(Top, Bottom, Right, Left), DamageMultiplier);
}

public readonly struct DamageZoneStats
{
    public readonly DamageZone ZoneType;
    public readonly float Coverage;
    public readonly DirectionConstrain Directions;
    public readonly float DamageMultiplier;

    public DamageZoneStats(DamageZone type, float coverage, DirectionConstrain directions, float damageMultiplier)
    {
        ZoneType = type;
        Coverage = coverage;
        Directions = directions;
        DamageMultiplier = damageMultiplier;
    }
}

public sealed class DamageBlockStats
{
    public readonly DamageZone ZoneType;
    public readonly DirectionConstrain Directions;
    public readonly DamageResistData Resists;
    public readonly Action<DamageData> Callback;

    public DamageBlockStats(DamageZone type, DirectionConstrain directions, DamageResistData resists, Action<DamageData> callback)
    {
        ZoneType = type;
        Directions = directions;
        Resists = resists;
        Callback = callback;
    }
}