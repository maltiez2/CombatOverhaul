using OpenTK.Mathematics;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace CombatOverhaul.DamageSystems;

public interface IWeaponDamageSource
{
    ItemStack? Weapon { get; set; }
}

public interface ITypedDamage
{
    DamageData DamageTypeData { get; set; }
}

public class DamageDataJson
{
    public string DamageType { get; set; } = "PiercingAttack";
    public float Strength { get; set; } // Tier, left for compatibility reasons
    public int Tier { get; set; }
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
    public readonly float Tier;

    public DamageData(EnumDamageType damageType, float tier)
    {
        DamageType = damageType;
        Tier = tier;
    }
}

public interface ILocationalDamage
{
    public Vector3d Position { get; set; }
    public string Collider { get; set; }
}

public class DirectionalTypedDamageSource : DamageSource, ILocationalDamage, ITypedDamage, IWeaponDamageSource
{
    public Vector3d Position { get; set; }
    public string Collider { get; set; } = "";
    public DamageData DamageTypeData { get; set; }
    public ItemStack? Weapon { get; set; }
}

public class TypedDamageSource : DamageSource, ITypedDamage, IWeaponDamageSource
{
    public DamageData DamageTypeData { get; set; }
    public ItemStack? Weapon { get; set; }
}

public class DamageResistData
{
    public Dictionary<EnumDamageType, float> Resists { get; }
    public Dictionary<EnumDamageType, float> FlatDamageReduction { get; }

    public static int MaxAttackTier { get; set; } = 9;
    public static int MaxArmorTier { get; set; } = 24;
    public static float[][] DamageReduction { get; set; } = new float[][]
    {
        new float[] { 0.75f, 1.00f, 1.00f, 1.00f, 1.00f, 1.00f, 1.00f, 1.00f, 1.00f },
        new float[] { 0.50f, 0.75f, 1.00f, 1.00f, 1.00f, 1.00f, 1.00f, 1.00f, 1.00f },
        new float[] { 0.25f, 0.50f, 0.75f, 1.00f, 1.00f, 1.00f, 1.00f, 1.00f, 1.00f },
        new float[] { 0.10f, 0.25f, 0.50f, 0.75f, 1.00f, 1.00f, 1.00f, 1.00f, 1.00f },
        new float[] { 0.05f, 0.15f, 0.33f, 0.50f, 0.75f, 1.00f, 1.00f, 1.00f, 1.00f },
        new float[] { 0.03f, 0.10f, 0.25f, 0.40f, 0.50f, 0.75f, 1.00f, 1.00f, 1.00f },
        new float[] { 0.02f, 0.05f, 0.20f, 0.33f, 0.40f, 0.50f, 0.75f, 1.00f, 1.00f },
        new float[] { 0.01f, 0.03f, 0.15f, 0.25f, 0.35f, 0.45f, 0.50f, 0.75f, 1.00f },
        new float[] { 0.01f, 0.02f, 0.10f, 0.20f, 0.30f, 0.40f, 0.45f, 0.50f, 0.75f },
        new float[] { 0.01f, 0.01f, 0.05f, 0.15f, 0.25f, 0.35f, 0.40f, 0.45f, 0.50f },
        new float[] { 0.01f, 0.01f, 0.03f, 0.10f, 0.20f, 0.30f, 0.35f, 0.41f, 0.46f },
        new float[] { 0.01f, 0.01f, 0.02f, 0.07f, 0.15f, 0.25f, 0.30f, 0.37f, 0.42f },
        new float[] { 0.01f, 0.01f, 0.01f, 0.05f, 0.10f, 0.20f, 0.25f, 0.33f, 0.39f },
        new float[] { 0.01f, 0.01f, 0.01f, 0.03f, 0.07f, 0.15f, 0.20f, 0.29f, 0.36f },
        new float[] { 0.01f, 0.01f, 0.01f, 0.02f, 0.06f, 0.10f, 0.17f, 0.25f, 0.33f },
        new float[] { 0.01f, 0.01f, 0.01f, 0.01f, 0.05f, 0.08f, 0.15f, 0.21f, 0.30f },
        new float[] { 0.01f, 0.01f, 0.01f, 0.01f, 0.03f, 0.07f, 0.12f, 0.18f, 0.27f },
        new float[] { 0.01f, 0.01f, 0.01f, 0.01f, 0.02f, 0.06f, 0.10f, 0.15f, 0.24f },
        new float[] { 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.05f, 0.08f, 0.12f, 0.21f },
        new float[] { 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.03f, 0.07f, 0.10f, 0.18f },
        new float[] { 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.02f, 0.06f, 0.08f, 0.15f },
        new float[] { 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.05f, 0.07f, 0.12f },
        new float[] { 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.03f, 0.06f, 0.10f },
        new float[] { 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.02f, 0.05f, 0.08f }
    };

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
            tier: damageData.Tier - protectionLevel
            );
    }
    public DamageData ApplyResist(DamageData damageData, ref float damage, out int durabilityDamage)
    {
        float protectionLevel = 0;
        float initialDamage = damage;

        if (FlatDamageReduction.TryGetValue(damageData.DamageType, out float flatReduction))
        {
            damage = Math.Clamp(damage - flatReduction, 0, damage);
        }

        if (Resists.TryGetValue(damageData.DamageType, out float value))
        {
            protectionLevel = value;
            damage *= DamageMultiplier(protectionLevel, damageData);
        }

        durabilityDamage = (int)Math.Clamp(initialDamage - flatReduction, 0, initialDamage);

        return new(
            damageType: damageData.DamageType,
            tier: damageData.Tier - protectionLevel
            );
    }
    public DamageData ApplyNotPlayerResist(DamageData damageData, ref float damage)
    {
        float protectionLevel = 0;

        if (FlatDamageReduction.TryGetValue(damageData.DamageType, out float flatReduction))
        {
            damage = Math.Clamp(damage - flatReduction, 0, damage);
        }

        if (Resists.TryGetValue(damageData.DamageType, out float value))
        {
            protectionLevel = value;
            damage *= DamageMultiplierNotPlayer(protectionLevel, damageData);
        }

        return new(
            damageType: damageData.DamageType,
            tier: damageData.Tier - protectionLevel
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

    private static float DamageMultiplier(float protectionLevel, DamageData damageData)
    {
        return damageData.DamageType switch
        {
            EnumDamageType.Gravity => Percentage(protectionLevel, damageData.Tier),
            EnumDamageType.Fire => Percentage(protectionLevel, damageData.Tier),
            EnumDamageType.BluntAttack => LookupTableMultiplier(protectionLevel, damageData.Tier),
            EnumDamageType.SlashingAttack => LookupTableMultiplier(protectionLevel, damageData.Tier),
            EnumDamageType.PiercingAttack => LookupTableMultiplier(protectionLevel, damageData.Tier),
            EnumDamageType.Suffocation => Percentage(protectionLevel, damageData.Tier),
            EnumDamageType.Heal => 1 + damageData.Tier + protectionLevel,
            EnumDamageType.Poison => Percentage(protectionLevel, damageData.Tier),
            EnumDamageType.Hunger => Percentage(protectionLevel, damageData.Tier),
            EnumDamageType.Crushing => Percentage(protectionLevel, damageData.Tier),
            EnumDamageType.Frost => Percentage(protectionLevel, damageData.Tier),
            EnumDamageType.Electricity => Percentage(protectionLevel, damageData.Tier),
            EnumDamageType.Heat => Percentage(protectionLevel, damageData.Tier),
            EnumDamageType.Injury => 1,
            _ => 1
        };
    }
    private static float DamageMultiplierNotPlayer(float protectionLevel, DamageData damageData)
    {
        return damageData.DamageType switch
        {
            EnumDamageType.Gravity => Percentage(protectionLevel, damageData.Tier),
            EnumDamageType.Fire => Percentage(protectionLevel, damageData.Tier),
            EnumDamageType.BluntAttack => FlatMultiplier(protectionLevel, damageData.Tier),
            EnumDamageType.SlashingAttack => FlatMultiplier(protectionLevel, damageData.Tier),
            EnumDamageType.PiercingAttack => FlatMultiplier(protectionLevel, damageData.Tier),
            EnumDamageType.Suffocation => Percentage(protectionLevel, damageData.Tier),
            EnumDamageType.Heal => 1 + damageData.Tier + protectionLevel,
            EnumDamageType.Poison => Percentage(protectionLevel, damageData.Tier),
            EnumDamageType.Hunger => Percentage(protectionLevel, damageData.Tier),
            EnumDamageType.Crushing => Percentage(protectionLevel, damageData.Tier),
            EnumDamageType.Frost => Percentage(protectionLevel, damageData.Tier),
            EnumDamageType.Electricity => Percentage(protectionLevel, damageData.Tier),
            EnumDamageType.Heat => Percentage(protectionLevel, damageData.Tier),
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
    private static float LookupTableMultiplier(float protection, float attackTier) => LookupTableMultiplier((int)protection, (int)attackTier);
    private static float LookupTableMultiplier(int protection, int attackTier)
    {
        if (protection == 0)
        {
            return 1;
        }
        else if (attackTier == 0 && protection > 0)
        {
            return 0;
        }

        return DamageReduction[GameMath.Clamp(protection - 1, 0, MaxArmorTier - 1)][GameMath.Clamp(attackTier - 1, 0, MaxAttackTier - 1)];
    }
    private static float FlatMultiplier(float protection, float attackTier)
    {
        if (attackTier >= protection)
            return 1;
        else if (attackTier < protection - 1)
            return 0.5f;
        else
            return 0.75f;
    }
}