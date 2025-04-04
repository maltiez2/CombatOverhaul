using CombatOverhaul.Armor;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace CombatOverhaul.Integration;

public static class ArmorAutoPatcher
{
    public static void Patch(ICoreAPI api)
    {
        foreach (Item item in api.World.Items)
        {
            if (item.Attributes == null) continue;

            if (!IsVanillaArmor(item) || IsCOArmor(item)) continue;

            Patch(item);
        }
    }

    public static void Patch(Item item)
    {
        JsonObject properties = GenerateProperties(item);

        ArmorBehavior behavior = new(item);

        behavior.Initialize(properties);

        item.CollectibleBehaviors = item.CollectibleBehaviors.Append(behavior).ToArray();

        RemoveVanillaStats(item);
    }

    private static List<string> _vanillaArmorParts = new()
    {
        "armorhead",
        "armorbody",
        "armorlegs"
    };
    private static Dictionary<string, string> _damageZoneFromArmorPart = new()
    {
        { "armorhead", "[\"Head\", \"Neck\", \"Face\"]" },
        { "armorbody", "[\"Torso\", \"Arms\", \"Hands\"]" },
        { "armorlegs", "[\"Legs\", \"Feet\"]" }
    };
    private const int _minProtectionTier = 2;
    private const int _maxProtectionTier = 14;
    private const float _minVanillaRelativeProtection = 0.4f;
    private const float _maxVanillaRelativeProtection = 0.97f;
    private const int _minVanillaProtectionTier = 0;
    private const int _maxVanillaProtectionTier = 4;
    private const float _minProtectionTierMultiplier = 0.20f;
    private const float _maxProtectionTierMultiplier = 1.00f;

    private static bool IsVanillaArmor(Item item)
    {
        string armorPart = VanillaArmorPart(item);

        return _vanillaArmorParts.Contains(armorPart);
    }
    private static string VanillaArmorPart(Item item)
    {
        string? clothesCategory = null;
        if (item.Attributes.KeyExists("clothesCategory"))
        {
            clothesCategory = item.Attributes["clothesCategory"].AsString(null);
        }
        if (item.Attributes.KeyExists("clothescategory"))
        {
            clothesCategory = item.Attributes["clothescategory"].AsString(null);
        }

        string? categoryCode = null;
        if (item.Attributes.KeyExists("attachableToEntity") && item.Attributes["attachableToEntity"].KeyExists("categoryCode"))
        {
            categoryCode = item.Attributes["attachableToEntity"]["categoryCode"].AsString(null);
        }

        return clothesCategory ?? categoryCode ?? "";
    }
    private static bool IsCOArmor(Item item)
    {
        IArmor? armor = item.GetCollectibleInterface<IArmor>();
        IModularArmor? modularArmor = item.GetCollectibleInterface<IModularArmor>();

        return armor != null || modularArmor != null;
    }
    private static (float relativeProtection, float flatDamageReduction, int protectionTier) VanillaArmorStats(Item item)
    {
        if (!item.Attributes.KeyExists("protectionModifiers")) return (0, 0, 0);

        float relativeProtection = item.Attributes["protectionModifiers"]["relativeProtection"].AsFloat(0);
        float flatDamageReduction = item.Attributes["protectionModifiers"]["flatDamageReduction"].AsFloat(0);
        int protectionTier = item.Attributes["protectionModifiers"]["protectionTier"].AsInt(0);

        return (relativeProtection, flatDamageReduction, protectionTier);
    }
    private static int ProtectionTierFromVanillaStats(float relativeProtection, float flatDamageReduction, int protectionTier)
    {
        float relativeProtectionFraction = (relativeProtection - _minVanillaRelativeProtection) / (_maxVanillaRelativeProtection - _minVanillaRelativeProtection);
        float newProtectionTier = _minProtectionTier + (_maxProtectionTier - _minProtectionTier) * relativeProtectionFraction * MultiplierFromVanillaProtectionTier(protectionTier);

        return (int)MathF.Floor(newProtectionTier);
    }
    private static float MultiplierFromVanillaProtectionTier(int protectionTier)
    {
        float tierFraction = (protectionTier - _minVanillaProtectionTier) / (float)(_maxVanillaProtectionTier - _minVanillaProtectionTier);
        float multiplier = _minProtectionTierMultiplier + (_maxProtectionTierMultiplier - _minProtectionTierMultiplier) * tierFraction;
        return multiplier;
    }
    private static JsonObject GenerateProperties(Item item)
    {
        string armorPart = VanillaArmorPart(item);
        (float relativeProtection, float flatDamageReduction, int protectionTier) = VanillaArmorStats(item);
        int newProtectionTier = ProtectionTierFromVanillaStats(relativeProtection, flatDamageReduction, protectionTier);

        string layers = "\"Layers\":[\"Outer\", \"Middle\"]";
        string zones = $"\"Zones\":{_damageZoneFromArmorPart[armorPart]}";
        string resists = $"\"Resists\":{{\"PiercingAttack\": {newProtectionTier}, \"SlashingAttack\": {newProtectionTier}, \"BluntAttack\": {newProtectionTier * 0.5f}}}";
        string stats = "\"PlayerStats\":{\"walkspeed\": -0.05, \"manipulationSpeed\": -0.05, \"steadyAim\": -0.05, \"healingeffectivness\": -0.05, \"hungerrate\": 0.05}";

        string properties = "{" + $"{layers},{zones},{resists},{stats}" + "}";

        JsonObject propertiesJson = JsonObject.FromJson(properties);

        return propertiesJson;
    }
    private static void RemoveVanillaStats(Item item)
    {
        (item.Attributes.Token as JObject)?.Remove("protectionModifiers");
        (item.Attributes.Token as JObject)?.Remove("statModifiers");

        if (item is ItemWearable armor)
        {
            if (armor.StatModifers != null)
            {
                armor.StatModifers.rangedWeaponsAcc = 0;
                armor.StatModifers.rangedWeaponsSpeed = 0;
                armor.StatModifers.walkSpeed = 0;
                armor.StatModifers.healingeffectivness = 0;
                armor.StatModifers.hungerrate = 0;
                armor.StatModifers.canEat = true;
            }
            
            if (armor.ProtectionModifiers != null)
            {
                armor.ProtectionModifiers.RelativeProtection = 0;
                armor.ProtectionModifiers.PerTierRelativeProtectionLoss = new float[0];
                armor.ProtectionModifiers.FlatDamageReduction = 0;
                armor.ProtectionModifiers.PerTierFlatDamageReductionLoss = new float[0];
                armor.ProtectionModifiers.ProtectionTier = 0;
                armor.ProtectionModifiers.HighDamageTierResistant = false;
            }
        }
    }
}
