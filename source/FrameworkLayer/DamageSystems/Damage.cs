using System.Collections.Immutable;
using Vintagestory.GameContent;

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
    /// Deals full damage if bypassed protection plus extra damage proportional to strength left after bypassing protection
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

    public static readonly ImmutableDictionary<DamageTypes, string> Units = new Dictionary<DamageTypes, string>()
    {
        { DamageTypes.Sharp, "mm of RHA" },
        { DamageTypes.Blunt, "MPA" },
        { DamageTypes.Thermal, "K" }

    }.ToImmutableDictionary();
}

public readonly struct DamageResistData
{
    public readonly float SharpResist;
    public readonly float BluntResist;
    public readonly float ThermalResist;

    public DamageResistData(float sharpResist, float bluntResist, float thermalResist)
    {
        SharpResist = sharpResist;
        BluntResist = bluntResist;
        ThermalResist = thermalResist;
    }

    public DamageData ApplyResist(DamageData damage)
    {
        float protectionLevel = damage.DamageType switch
        {
            DamageTypes.Sharp => SharpResist,
            DamageTypes.Blunt => BluntResist,
            DamageTypes.Thermal => ThermalResist,
            _ => 0
        };

        if (protectionLevel >= damage.Strength) return new DamageData(damage.DamageType, 0, 0);

        return new(
            damageType: damage.DamageType,
            capacity: ApplyResistToCapacity(protectionLevel, damage),
            strength: damage.Strength - protectionLevel
            );
    }

    public static float ApplyResistToCapacity(float protectionLevel, DamageData damage)
    {
        return damage.DamageType switch
        {
            DamageTypes.Sharp => damage.Capacity,
            DamageTypes.Blunt => damage.Capacity * (damage.Strength - protectionLevel) / damage.Strength,
            DamageTypes.Thermal => damage.Capacity * (1 + damage.Strength - protectionLevel) / protectionLevel,
            _ => damage.Capacity,
        };
    }
}