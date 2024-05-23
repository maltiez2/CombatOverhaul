using CompactExifLib;
using System.Collections.Immutable;
using Vintagestory.API.Common;

namespace CombatOverhaul.DamageSystems;

public enum DamageTypes
{
    /// <summary>
    /// If bypassed protection deals full damage
    /// </summary>
    Sharp,
    /// <summary>
    /// Deals fraction of full damage proportional to percentage of strength left after bypassing protection
    /// </summary>
    Blunt,
    /// <summary>
    /// Deals fulldamage if bypassed protection plus extra damage proportional to strength left after bypassing protection
    /// </summary>
    Thermal
}

public readonly struct DamageData
{
    public readonly DamageTypes DamageType;
    public readonly float Capacity;
    public readonly float Strength;

    public DamageData(DamageTypes damageType, float capacity, float strength)
    {
        DamageType = damageType;
        Capacity = capacity;
        Strength = strength;
    }
}

public readonly struct DamageResistData
{
    public readonly DamageTypes DamageType;
    public readonly float ProtectionLevel;

    public DamageResistData(DamageTypes damageType, float protectionLevel)
    {
        DamageType = damageType;
        ProtectionLevel = protectionLevel;
    }
}

public static class DamageUtils
{
    public static readonly ImmutableDictionary<DamageTypes, string> Units = new Dictionary<DamageTypes, string>()
    {
        { DamageTypes.Sharp, "mm of RHA" },
        { DamageTypes.Blunt, "MPA" },
        { DamageTypes.Thermal, "K" }
    
    }.ToImmutableDictionary();

    public static DamageData ApplyResist(DamageResistData resist, DamageData damage)
    {
        if (resist.DamageType != damage.DamageType) return damage;

        if (resist.ProtectionLevel >= damage.Strength) return new DamageData(damage.DamageType, 0, 0);

        return new(
            damageType: damage.DamageType,
            capacity: damage.Capacity * (damage.Strength - resist.ProtectionLevel) / damage.Strength,
            strength: damage.Strength - resist.ProtectionLevel
            );
    }

    public static float ApplyResistToCapacity(DamageResistData resist, DamageData damage)
    {
        return resist.DamageType switch
        {
            DamageTypes.Sharp => damage.Capacity,
            DamageTypes.Blunt => damage.Capacity * (damage.Strength - resist.ProtectionLevel) / damage.Strength,
            DamageTypes.Thermal => damage.Capacity * (1 + damage.Strength - resist.ProtectionLevel) / resist.ProtectionLevel,
            _ => damage.Capacity,
        };
    }
}