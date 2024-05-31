using CombatOverhaul.DamageSystems;
using CombatOverhaul.Utils;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.Common;

namespace CombatOverhaul.Armor;

[Flags]
public enum ArmorLayers
{
    None = 0,
    Skin = 1,
    Middle = 2,
    Outer = 4
}

public sealed class ArmorTypeJson
{
    public string[] Layers { get; set; } = Array.Empty<string>();
    public string[] Slots { get; set; } = Array.Empty<string>();

    public ArmorType ToArmorType()
    {
        return new(
            Layers.Select(Enum.Parse<ArmorLayers>).Aggregate((first, second) => first | second),
            Slots.Select(Enum.Parse<DamageZone>).Aggregate((first, second) => first | second)
            );
    }
}

public readonly struct ArmorType
{
    public readonly ArmorLayers Layers;
    public readonly DamageZone Slots;

    public ArmorType(ArmorLayers layers, DamageZone slots)
    {
        Layers = layers;
        Slots = slots;
    }

    public bool Check(ArmorLayers layer, DamageZone slot) => (Layers & layer) != 0 && (Slots & slot) != 0;
    public bool Check(ArmorType type) => (Layers & type.Layers) != 0 && (Slots & type.Slots) != 0;

    public override string ToString()
    {
        ArmorLayers layersValue = Layers;
        string layers = Enum.GetValues<ArmorLayers>().Where(value => (value | layersValue) != 0).Select(value => value.ToString()).Aggregate((first, second) => $"{first}, {second}");

        DamageZone slotsValue = Slots;
        string slots = Enum.GetValues<DamageZone>().Where(value => (value | slotsValue) != 0).Select(value => value.ToString()).Aggregate((first, second) => $"{first}, {second}");

        return $"({layers}|{slots})";
    }
}

public interface IArmor
{
    public ArmorType ArmorType { get; }
    public DamageResistData Resists { get; }
}

public sealed class ArmorStatsJson
{
    public string[] Layers { get; set; } = Array.Empty<string>();
    public string[] Slots { get; set; } = Array.Empty<string>();
    public Dictionary<string, float> Resists { get; set; } = new();
}

public class ArmorItem : Item, IArmor
{
    public ArmorType ArmorType { get; protected set; } = new(ArmorLayers.None, DamageZone.None);
    public DamageResistData Resists { get; protected set; } = new(new Dictionary<EnumDamageType, float>());

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        if (!Attributes.KeyExists("stats"))
        {
            LoggerUtil.Error(api, this, $"Armor item '{Code}' does not have stats attribute");
            return;
        }

        ArmorStatsJson stats = Attributes["stats"].AsObject<ArmorStatsJson>();

        ArmorType = new(stats.Layers.Select(Enum.Parse<ArmorLayers>).Aggregate((first, second) => first | second), stats.Layers.Select(Enum.Parse<DamageZone>).Aggregate((first, second) => first | second));
        Resists = new(stats.Resists.ToDictionary(entry => Enum.Parse<EnumDamageType>(entry.Key), entry => entry.Value));
    }
}

public class ArmorSlot : ItemSlot
{
    public ArmorType ArmorType { get; }
    public override int MaxSlotStackSize { get => 1; }
    public event Action<ArmorType, ArmorType>? ItemPut;
    public event Action<ArmorType, ArmorType>? ItemTaken;

    public ArmorSlot(InventoryBase inventory, ArmorType armorType) : base(inventory)
    {
        ArmorType = armorType;
    }

    public override bool CanHold(ItemSlot sourceSlot)
    {
        if (DrawUnavailable || !base.CanHold(sourceSlot) || sourceSlot.Itemstack.Item is not IArmor armor) return false;

        return armor.ArmorType.Check(ArmorType);
    }

    public override int TryPutInto(ItemSlot sinkSlot, ref ItemStackMoveOperation op)
    {
        int result = base.TryPutInto(sinkSlot, ref op);

        if (result > 0)
        {
            ArmorType? armorType = (itemstack.Item as IArmor)?.ArmorType;
            if (armorType != null)
            {
                ItemPut?.Invoke(armorType.Value, ArmorType);
            }
        }

        return result;
    }
    public override ItemStack TakeOutWhole()
    {
        ItemStack result = base.TakeOutWhole();

        if (Itemstack == null || Itemstack?.StackSize == 0)
        {
            ArmorType? armorType = (result.Item as IArmor)?.ArmorType;
            if (armorType != null)
            {
                ItemTaken?.Invoke(armorType.Value, ArmorType);
            }
        }

        return result;
    }
}

public sealed class ArmorInventory : InventoryCharacter
{
    public ArmorInventory(string className, string playerUID, ICoreAPI api) : base(className, playerUID, api)
    {
        _slots = GenEmptySlots(_totalSlotsNumber);
    }
    public ArmorInventory(string inventoryID, ICoreAPI api) : base(inventoryID, api)
    {
        _slots = GenEmptySlots(_totalSlotsNumber);
    }


    public override ItemSlot this[int slotId] { get => _slots[slotId]; set => LoggerUtil.Warn(Api, this, "Armor slots cannot be set"); }

    public override int Count => _totalSlotsNumber;
    public event Action<ArmorType>? ArmorChanged;

    public override void FromTreeAttributes(ITreeAttribute tree)
    {
        _slots = GenEmptySlots(_totalSlotsNumber);
        for (int index = 0; index < _slots.Length; index++)
        {
            ItemStack? itemStack = tree.GetTreeAttribute("slots")?.GetItemstack(index.ToString() ?? "");
            _slots[index].Itemstack = itemStack;
            if (itemStack != null && index >= _clothesArmorSlots)
            {
                ArmorType? armorType = (itemStack.Item as IArmor)?.ArmorType;
                if (armorType != null)
                {
                    OnItemPut(armorType.Value, ArmorTypeFromIndex(index));
                }
            }
            if (Api?.World == null)
            {
                continue;
            }

            itemStack?.ResolveBlockOrItem(Api.World);
        }
    }
    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        TreeAttribute treeAttribute = new();
        for (int index = 0; index < _slots.Length; index++)
        {
            if (_slots[index].Itemstack != null)
            {
                treeAttribute.SetItemstack(index.ToString() ?? "", _slots[index].Itemstack.Clone());
            }
        }

        tree["slots"] = treeAttribute;
    }

    private ItemSlot[] _slots;
    private readonly Dictionary<ArmorType, ArmorSlot> _slotsByType = new();
    private readonly Dictionary<EnumCharacterDressType, string> _clothesSlotsIcons = new()
    {
        {
            EnumCharacterDressType.Foot,
            "boots"
        },
        {
            EnumCharacterDressType.Hand,
            "gloves"
        },
        {
            EnumCharacterDressType.Shoulder,
            "cape"
        },
        {
            EnumCharacterDressType.Head,
            "hat"
        },
        {
            EnumCharacterDressType.LowerBody,
            "trousers"
        },
        {
            EnumCharacterDressType.UpperBody,
            "shirt"
        },
        {
            EnumCharacterDressType.UpperBodyOver,
            "pullover"
        },
        {
            EnumCharacterDressType.Neck,
            "necklace"
        },
        {
            EnumCharacterDressType.Arm,
            "bracers"
        },
        {
            EnumCharacterDressType.Waist,
            "belt"
        },
        {
            EnumCharacterDressType.Emblem,
            "medal"
        },
        {
            EnumCharacterDressType.Face,
            "mask"
        },
        {
            EnumCharacterDressType.ArmorHead,
            "armorhead"
        },
        {
            EnumCharacterDressType.ArmorBody,
            "armorbody"
        },
        {
            EnumCharacterDressType.ArmorLegs,
            "armorlegs"
        }
    };
    private readonly Dictionary<ArmorType, string> _armorSlotsIcons = new()
    {

    };
    private const int _clothesArmorSlots = 3;
    private static int _clothesSlotsCount = Enum.GetValues<EnumCharacterDressType>().Length - _clothesArmorSlots - 1;
    private static int _totalSlotsNumber = _clothesSlotsCount + (Enum.GetValues<ArmorLayers>().Length - 1) * (Enum.GetValues<DamageZone>().Length - 1);

    protected override ItemSlot NewSlot(int slotId)
    {
        int defaultSlotsCount = _clothesSlotsCount;

        if (slotId < defaultSlotsCount)
        {
            ItemSlotCharacter slot = new((EnumCharacterDressType)slotId, this);
            _clothesSlotsIcons.TryGetValue((EnumCharacterDressType)slotId, out slot.BackgroundIcon);
            return slot;
        }
        else
        {
            ArmorType armorType = ArmorTypeFromIndex(slotId);
            ArmorSlot slot = new(this, armorType);
            _slotsByType[armorType] = slot;
            slot.ItemPut += OnItemPut;
            slot.ItemTaken += OnItemTaken;
            _armorSlotsIcons.TryGetValue(armorType, out slot.BackgroundIcon);
            return slot;
        }
    }


    private static ArmorType ArmorTypeFromIndex(int index)
    {
        int defaultSlotsCount = _clothesSlotsCount;
        int zonesCount = Enum.GetValues<DamageZone>().Length - 1;

        ArmorLayers layer = ArmorLayerFromIndex((index - defaultSlotsCount) / zonesCount);
        DamageZone zone = DamageZoneFromIndex(index - defaultSlotsCount - IndexFromArmorLayer(layer) * zonesCount);

        Console.WriteLine($"index:{index}; layer: {layer}; zone: {zone}");

        return new(layer, zone);
    }
    private static ArmorLayers ArmorLayerFromIndex(int index)
    {
        return index switch
        {
            0 => ArmorLayers.Skin,
            1 => ArmorLayers.Middle,
            2 => ArmorLayers.Outer,
            _ => ArmorLayers.None
        };
    }
    private static int IndexFromArmorLayer(ArmorLayers layer)
    {
        return layer switch
        {
            ArmorLayers.None => 0,
            ArmorLayers.Skin => 0,
            ArmorLayers.Middle => 1,
            ArmorLayers.Outer => 2,
            _ => 0
        };
    }
    private static DamageZone DamageZoneFromIndex(int index)
    {
        return index switch
        {
            0 => DamageZone.Head,
            1 => DamageZone.Face,
            2 => DamageZone.Neck,
            3 => DamageZone.Torso,
            4 => DamageZone.Arms,
            5 => DamageZone.Hands,
            6 => DamageZone.Legs,
            7 => DamageZone.Feet,
            _ => DamageZone.None
        };
    }
    private static int IndexFromDamageZone(DamageZone zone)
    {
        return zone switch
        {
            DamageZone.None => 0,
            DamageZone.Head => 0,
            DamageZone.Face => 1,
            DamageZone.Neck => 2,
            DamageZone.Torso => 3,
            DamageZone.Arms => 4,
            DamageZone.Hands => 5,
            DamageZone.Legs => 6,
            DamageZone.Feet => 7,
            _ => 0
        };
    }
    private static int IndexFromArmorType(ArmorType armorType)
    {
        int defaultSlotsCount = Enum.GetValues<EnumCharacterDressType>().Length - _clothesArmorSlots;
        int layersCount = Enum.GetValues<ArmorLayers>().Length - 1;

        return defaultSlotsCount + layersCount * ((int)armorType.Layers - 1) + (int)armorType.Slots - 1;
    }
    private void OnItemPut(ArmorType armorType, ArmorType slotType)
    {
        IEnumerable<KeyValuePair<ArmorType, ArmorSlot>> slots = _slotsByType
            .Where(entry => entry.Key.Slots != slotType.Slots || entry.Key.Layers != slotType.Layers)
            .Where(entry => (entry.Key.Slots & armorType.Slots) != 0 && (entry.Key.Layers & armorType.Layers) != 0);

        foreach ((_, ArmorSlot slot) in slots)
        {
            slot.DrawUnavailable = true;
            ItemStack stack = slot.TakeOutWhole();
            if (!Player.InventoryManager.TryGiveItemstack(stack))
            {
                spawnItemEntity(stack, Player.Entity.Pos.XYZ, (int)TimeSpan.FromMinutes(10).TotalMilliseconds);
            }
        }

        ArmorChanged?.Invoke(slotType);
    }
    private void OnItemTaken(ArmorType armorType, ArmorType slotType)
    {
        IEnumerable<KeyValuePair<ArmorType, ArmorSlot>> slots = _slotsByType
            .Where(entry => entry.Key.Slots != slotType.Slots || entry.Key.Layers != slotType.Layers)
            .Where(entry => (entry.Key.Slots & armorType.Slots) != 0 && (entry.Key.Layers & armorType.Layers) != 0);

        foreach ((_, ArmorSlot slot) in slots)
        {
            slot.DrawUnavailable = false;
        }

        ArmorChanged?.Invoke(slotType);
    }
}