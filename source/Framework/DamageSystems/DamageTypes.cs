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
    /// Deals full damage if bypassed protection plus extra damage proportional to strength left after bypassing protection
    /// </summary>
    Heat
}

public interface ITypedDamage
{
    DamageData DamageTypeData { get; set; }
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

public sealed class DamageResistData
{
    public readonly ImmutableDictionary<EnumDamageType, float> Resists;

    public DamageResistData(Dictionary<EnumDamageType, float> resists)
    {
        Resists = resists.ToImmutableDictionary();
    }

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

    private static float DamageMultiplier(float protectionLevel, DamageData damageData)
    {
        return damageData.DamageType switch
        {
            EnumDamageType.Gravity => Percentage(protectionLevel, damageData.Strength),
            EnumDamageType.Fire => Percentage(protectionLevel, damageData.Strength),
            EnumDamageType.BluntAttack => PenetrationPercentage(protectionLevel, damageData.Strength),
            EnumDamageType.SlashingAttack => PenetrationPercentage(protectionLevel, damageData.Strength),
            EnumDamageType.PiercingAttack => PenetrationCheck(protectionLevel, damageData.Strength),
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
    private static float PenetrationCheck(float protection, float strength) => protection >= strength ? 0 : 1;
    private static float PenetrationPercentage(float protection, float strength) => Math.Clamp((strength - protection / strength), 0, 1);
}