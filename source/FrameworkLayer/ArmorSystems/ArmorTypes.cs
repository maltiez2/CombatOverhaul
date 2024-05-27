using CombatOverhaul.DamageSystems;
using Vintagestory.API.Common;

namespace CombatOverhaul.Armor;

[Flags]
public enum ArmorLayers
{
    None = 0,
    Skin = 1,
    Middle = 2,
    Outer = 4
}

[Flags]
public enum ArmorSlots
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

public sealed class ArmorTypeJson
{
    public string[] Layers { get; set; } = Array.Empty<string>();
    public string[] Slots { get; set; } = Array.Empty<string>();

    public ArmorType ToArmorType()
    {
        return new(
            Layers.Select(Enum.Parse<ArmorLayers>).Aggregate((first, second) => first | second),
            Slots.Select(Enum.Parse<ArmorSlots>).Aggregate((first, second) => first | second)
            );
    }
}

public readonly struct ArmorType
{
    public readonly ArmorLayers Layers;
    public readonly ArmorSlots Slots;

    public ArmorType(ArmorLayers layers, ArmorSlots slots)
    {
        Layers = layers;
        Slots = slots;
    }

    public bool Check(ArmorLayers layer, ArmorSlots slot) => (Layers & layer) != 0 && (Slots & slot) != 0;
    public bool Check(ArmorType type) => (Layers & type.Layers) != 0 && (Slots & type.Slots) != 0;

    public override string ToString()
    {
        ArmorLayers layersValue = Layers;
        string layers = Enum.GetValues<ArmorLayers>().Where(value => (value | layersValue) != 0).Select(value => value.ToString()).Aggregate((first, second) => $"{first}, {second}");

        ArmorSlots slotsValue = Slots;
        string slots = Enum.GetValues<ArmorSlots>().Where(value => (value | slotsValue) != 0).Select(value => value.ToString()).Aggregate((first, second) => $"{first}, {second}");

        return $"({layers}|{slots})";
    }
}

public interface IArmor
{
    public ArmorType ArmorType { get; }
    public DamageResistData Resists { get; }
}

public class ArmorItem : Item, IArmor
{
    public ArmorType ArmorType { get; protected set; }
    public DamageResistData Resists { get; protected set; }
}

public class ArmorSlot : ItemSlot
{
    public ArmorType ArmorType { get; }
    public bool Disabled { get; set; } = false;
    public override int MaxSlotStackSize { get => 1; }

    public ArmorSlot(InventoryBase inventory, ArmorType armorType) : base(inventory)
    {
        ArmorType = armorType;
    }

    public override bool CanHold(ItemSlot sourceSlot)
    {
        if (Disabled || !base.CanHold(sourceSlot) || sourceSlot.Itemstack.Item is not IArmor armor) return false;

        return armor.ArmorType.Check(ArmorType);
    }
}