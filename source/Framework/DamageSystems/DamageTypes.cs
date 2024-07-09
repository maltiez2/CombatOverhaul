using System.Collections.Immutable;
using System.Numerics;
using Vintagestory.API.Common;

namespace CombatOverhaul.DamageSystems;

public interface ITypedDamage
{
    DamageData DamageTypeData { get; set; }
}

public struct DamageDataJson
{
    public string DamageType { get; set; } = "PiercingAttack";
    public float Strength { get; set; }
    public float Damage { get; set; }

    public DamageDataJson() { }
}

public struct ProjectileDamageDataJson
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

    public static readonly ImmutableDictionary<EnumDamageType, string> Units = new Dictionary<EnumDamageType, string>()
    {
        { EnumDamageType.PiercingAttack, "tier" },
        { EnumDamageType.SlashingAttack, "tier" },
        { EnumDamageType.BluntAttack, "tier" },
        { EnumDamageType.Fire, "%" },
        { EnumDamageType.Heat, "%" }

    }.ToImmutableDictionary();
}

public interface ILocationalDamage
{
    public Vector3 Position { get; set; }
    public int Collider { get; set; }
}

public class DirectionalTypedDamageSource : DamageSource, ILocationalDamage, ITypedDamage
{
    public Vector3 Position { get; set; }
    public int Collider { get; set; }
    public DamageData DamageTypeData { get; set; }
}

public readonly struct DamageResistData
{
    public readonly ImmutableDictionary<EnumDamageType, float> Resists;

    public DamageResistData(Dictionary<EnumDamageType, float> resists)
    {
        Resists = resists.ToImmutableDictionary();
    }
    public DamageResistData()
    {
        Resists = (new Dictionary<EnumDamageType, float>()).ToImmutableDictionary();
    }

    public static DamageResistData Empty => new();

    public DamageData ApplyResist(DamageData damageData, ref float damage)
    {
        if (!Resists.TryGetValue(damageData.DamageType, out float value)) return damageData;

        float protectionLevel = value;

        damage *= DamageMultiplier(protectionLevel, damageData);

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
            combinedResists[damageType] += protectionLevel;
        }
        return new(combinedResists);
    }


    private static float DamageMultiplier(float protectionLevel, DamageData damageData)
    {
        return damageData.DamageType switch
        {
            EnumDamageType.Gravity => Percentage(protectionLevel, damageData.Strength),
            EnumDamageType.Fire => Percentage(protectionLevel, damageData.Strength),
            EnumDamageType.BluntAttack => PenetrationPercentage(protectionLevel, damageData.Strength, 2),
            EnumDamageType.SlashingAttack => PenetrationPercentage(protectionLevel, damageData.Strength, 2),
            EnumDamageType.PiercingAttack => PenetrationPercentage(protectionLevel, damageData.Strength, 2),
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
    private static float PenetrationPercentage(float protection, float strength, float power)
    {
        if (protection == 0 || protection <= strength) return 1;

        return Math.Clamp(MathF.Pow(strength / protection, power), 0, 1);
    }
}