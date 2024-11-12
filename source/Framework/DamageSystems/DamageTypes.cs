using System.Numerics;
using Vintagestory.API.Common;

namespace CombatOverhaul.DamageSystems;

public interface ITypedDamage
{
    DamageData DamageTypeData { get; set; }
}

public class DamageDataJson
{
    public string DamageType { get; set; } = "PiercingAttack";
    public float Strength { get; set; }
    public float Damage { get; set; }

    public DamageDataJson() { }
}

public class ProjectileDamageDataJson
{
    public string DamageType { get; set; } = "PiercingAttack";
    public float Damage { get; set; }

    public ProjectileDamageDataJson() { }
}

public readonly struct DamageData
{
    public readonly EnumDamageType DamageType;
    public readonly float Strength;

    public DamageData(EnumDamageType damageType, float strength)
    {
        DamageType = damageType;
        Strength = strength;
    }

    public static readonly Dictionary<EnumDamageType, string> Units = new Dictionary<EnumDamageType, string>()
    {
        { EnumDamageType.PiercingAttack, "tier" },
        { EnumDamageType.SlashingAttack, "tier" },
        { EnumDamageType.BluntAttack, "tier" },
        { EnumDamageType.Fire, "%" },
        { EnumDamageType.Heat, "%" }
    };
}

public interface ILocationalDamage
{
    public Vector3 Position { get; set; }
    public string Collider { get; set; }
}

public class DirectionalTypedDamageSource : DamageSource, ILocationalDamage, ITypedDamage
{
    public Vector3 Position { get; set; }
    public string Collider { get; set; } = "";
    public DamageData DamageTypeData { get; set; }
}

public class TypedDamageSource : DamageSource, ITypedDamage
{
    public DamageData DamageTypeData { get; set; }
}

public class DamageResistData
{
    public Dictionary<EnumDamageType, float> Resists { get; }
    public Dictionary<EnumDamageType, float> FlatDamageReduction { get; }

    public DamageResistData(Dictionary<EnumDamageType, float> resists, Dictionary<EnumDamageType, float> flatDamageReduction)
    {
        Resists = resists;
        FlatDamageReduction = flatDamageReduction;
    }
    public DamageResistData(Dictionary<EnumDamageType, float> resists)
    {
        Resists = resists;
        FlatDamageReduction = (new Dictionary<EnumDamageType, float>());
    }
    public DamageResistData()
    {
        Resists = (new Dictionary<EnumDamageType, float>());
        FlatDamageReduction = (new Dictionary<EnumDamageType, float>());
    }

    public static DamageResistData Empty => new();

    public DamageData ApplyResist(DamageData damageData, ref float damage)
    {
        float protectionLevel = 0;
        if (Resists.TryGetValue(damageData.DamageType, out float value))
        {
            protectionLevel = value;
            damage *= DamageMultiplier(protectionLevel, damageData);
        }

        if (FlatDamageReduction.TryGetValue(damageData.DamageType, out float flatReduction))
        {
            damage = Math.Clamp(damage - flatReduction, 0, damage);
        }

        return new(
            damageType: damageData.DamageType,
            strength: damageData.Strength - protectionLevel
            );
    }
    public DamageData ApplyResist(DamageData damageData, ref float damage, out int durabilityDamage)
    {
        float protectionLevel = 0;
        float initialDamage = damage;

        if (Resists.TryGetValue(damageData.DamageType, out float value))
        {
            protectionLevel = value;
            damage *= DamageMultiplier(protectionLevel, damageData);
        }

        if (FlatDamageReduction.TryGetValue(damageData.DamageType, out float flatReduction))
        {
            damage = Math.Clamp(damage - flatReduction, 0, damage);
        }

        durabilityDamage = (int)Math.Clamp(initialDamage - flatReduction, 0, initialDamage);

        return new(
            damageType: damageData.DamageType,
            strength: damageData.Strength - protectionLevel
            );
    }

    public static DamageResistData Combine(IEnumerable<DamageResistData> resists)
    {
        Dictionary<EnumDamageType, float> combinedResists = new();
        foreach ((EnumDamageType damageType, float protectionLevel) in resists.SelectMany(element => element.Resists))
        {
            if (!combinedResists.ContainsKey(damageType))
            {
                combinedResists[damageType] = protectionLevel;
            }
            else
            {
                combinedResists[damageType] += protectionLevel;
            }
        }

        Dictionary<EnumDamageType, float> combinedFlat = new();
        foreach ((EnumDamageType damageType, float protectionLevel) in resists.SelectMany(element => element.FlatDamageReduction))
        {
            if (!combinedFlat.ContainsKey(damageType))
            {
                combinedFlat[damageType] = protectionLevel;
            }
            else
            {
                combinedFlat[damageType] += protectionLevel;
            }
        }

        return new(combinedResists, combinedFlat);
    }

    private const float _damageReductionPower = 2;
    private const float _damageReductionThreshold = 0.05f;

    private static float DamageMultiplier(float protectionLevel, DamageData damageData)
    {
        return damageData.DamageType switch
        {
            EnumDamageType.Gravity => Percentage(protectionLevel, damageData.Strength),
            EnumDamageType.Fire => Percentage(protectionLevel, damageData.Strength),
            EnumDamageType.BluntAttack => PenetrationPercentage(protectionLevel, damageData.Strength, _damageReductionPower, _damageReductionThreshold),
            EnumDamageType.SlashingAttack => PenetrationPercentage(protectionLevel, damageData.Strength, _damageReductionPower, _damageReductionThreshold),
            EnumDamageType.PiercingAttack => PenetrationPercentage(protectionLevel, damageData.Strength, _damageReductionPower, _damageReductionThreshold),
            EnumDamageType.Suffocation => Percentage(protectionLevel, damageData.Strength),
            EnumDamageType.Heal => 1 + damageData.Strength + protectionLevel,
            EnumDamageType.Poison => Percentage(protectionLevel, damageData.Strength),
            EnumDamageType.Hunger => Percentage(protectionLevel, damageData.Strength),
            EnumDamageType.Crushing => Percentage(protectionLevel, damageData.Strength),
            EnumDamageType.Frost => Percentage(protectionLevel, damageData.Strength),
            EnumDamageType.Electricity => Percentage(protectionLevel, damageData.Strength),
            EnumDamageType.Heat => Percentage(protectionLevel, damageData.Strength),
            EnumDamageType.Injury => 1,
            _ => 1
        };
    }

    private static float Percentage(float protection, float strength) => Math.Clamp(1 + strength - protection, 0, 1);
    private static float PenetrationCheck(float protection, float strength) => protection > strength ? 0 : 1;
    private static float PenetrationPercentage(float protection, float strength, float power, float threshold)
    {
        if (protection == 0 || protection <= strength) return 1;

        float multiplier = Math.Clamp(MathF.Pow(strength / protection, power), 0, 1);

        return multiplier <= threshold ? 0 : multiplier;
    }
}