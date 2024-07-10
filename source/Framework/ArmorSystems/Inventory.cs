using CombatOverhaul.DamageSystems;
using CombatOverhaul.Utils;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.Common;

namespace CombatOverhaul.Armor;

public class ArmorSlot : ItemSlot
{
    public ArmorType ArmorType { get; }
    public ArmorType StoredArmoredType => GetStoredArmorType();
    public DamageZone DamageZone => ArmorType.Slots;
    public ArmorLayers Layer => ArmorType.Layers;
    public DamageResistData Resists => GetResists();
    public override int MaxSlotStackSize => 1;
    public bool Available => _inventory.IsSlotAvailable(ArmorType);

    public ArmorSlot(InventoryBase inventory, ArmorType armorType) : base(inventory)
    {
        ArmorType = armorType;
        _inventory = inventory as ArmorInventory ?? throw new Exception();
    }

    public override bool CanHold(ItemSlot sourceSlot)
    {
        if (DrawUnavailable || !base.CanHold(sourceSlot) || !IsArmor(sourceSlot.Itemstack.Item, out IArmor? armor)) return false;

        if (armor == null || !_inventory.CanHoldArmorPiece(armor)) return false;

        return armor.ArmorType.Intersect(ArmorType);
    }

    private readonly ArmorInventory _inventory;

    private ArmorType GetStoredArmorType()
    {
        if (Itemstack?.Item != null && IsArmor(Itemstack.Item, out IArmor? armor) && armor != null)
        {
            return armor.ArmorType;
        }
        else
        {
            return ArmorType.Empty;
        }
    }
    private DamageResistData GetResists()
    {
        if (Itemstack?.Item != null && IsArmor(Itemstack.Item, out IArmor? armor) && armor != null)
        {
            return armor.Resists;
        }
        else
        {
            return DamageResistData.Empty;
        }
    }

    private static bool IsArmor(CollectibleObject item, out IArmor? armor)
    {
        if (item is IArmor armorItem)
        {
            armor = armorItem;
            return true;
        }

        CollectibleBehavior? behavior = item.CollectibleBehaviors.FirstOrDefault(x => x is IArmor);

        if (behavior is not IArmor armorBehavior)
        {
            armor = null;
            return false;
        }

        armor = armorBehavior;
        return true;
    }
}

public sealed class ArmorInventory : InventoryCharacter
{
    public ArmorInventory(string className, string playerUID, ICoreAPI api) : base(className, playerUID, api)
    {
        _slots = GenEmptySlots(_totalSlotsNumber);

        for (int index = 0; index < _slots.Length; index++)
        {
            if (IsVanillaArmorSlot(index))
            {
                _slots[index].DrawUnavailable = true;
                _slots[index].HexBackgroundColor = "#884444";
                _slots[index].MaxSlotStackSize = 0;
            }
        }
    }
    public ArmorInventory(string inventoryID, ICoreAPI api) : base(inventoryID, api)
    {
        _slots = GenEmptySlots(_totalSlotsNumber);

        for (int index = 0; index < _slots.Length; index++)
        {
            if (IsVanillaArmorSlot(index))
            {
                _slots[index].DrawUnavailable = true;
                _slots[index].HexBackgroundColor = "#884444";
                _slots[index].MaxSlotStackSize = 0;
            }
        }
    }

    public override ItemSlot this[int slotId] { get => _slots[slotId]; set => LoggerUtil.Warn(Api, this, "Armor slots cannot be set"); }

    public override int Count => _totalSlotsNumber;
    public Dictionary<DamageZone, DamageResistData> Resists { get; private set; } = new();

    public override void FromTreeAttributes(ITreeAttribute tree)
    {
        _slots = GenEmptySlots(_totalSlotsNumber);
        for (int index = 0; index < _slots.Length; index++)
        {
            ItemStack? itemStack = tree.GetTreeAttribute("slots")?.GetItemstack(index.ToString() ?? "");

            if (itemStack != null)
            {
                if (Api?.World != null) itemStack.ResolveBlockOrItem(Api.World);
                
                if (IsVanillaArmorSlot(index))
                {
                    //Player.Entity.TryGiveItemStack(itemStack);
                }
                else
                {
                    _slots[index].Itemstack = itemStack;
                }
            }

            if (IsVanillaArmorSlot(index))
            {
                _slots[index].DrawUnavailable = true;
                _slots[index].HexBackgroundColor = "#884444";
                _slots[index].MaxSlotStackSize = 0;
            }
        }

        RecalculateResists();
    }
    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        tree.SetInt("qslots", _vanillaSlots);

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

    public override void OnItemSlotModified(ItemSlot slot)
    {
        base.OnItemSlotModified(slot);

        RecalculateResists();

        PlayerDamageModelBehavior? behavior = Player.Entity.GetBehavior<PlayerDamageModelBehavior>();
        if (behavior != null)
        {
            behavior.Resists = Resists;
        }

        _onSlotModified?.Invoke();
    }

    public static int IndexFromArmorType(ArmorLayers layer, DamageZone zone)
    {
        int zonesCount = Enum.GetValues<DamageZone>().Length - 1;

        return _vanillaSlots + IndexFromArmorLayer(layer) * zonesCount + IndexFromDamageZone(zone);
    }
    public static int IndexFromArmorType(ArmorType type) => IndexFromArmorType(type.Layers, type.Slots);

    public ArmorType GetSlotBlockingSlot(ArmorType armorType) => _slotsByType
        .Where(entry => !entry.Value.Empty)
        .Where(entry => entry.Value.StoredArmoredType.Intersect(armorType))
        .Select(entry => entry.Key)
        .FirstOrDefault(defaultValue: ArmorType.Empty);
    public ArmorType GetSlotBlockingSlot(ArmorLayers layer, DamageZone zone) => GetSlotBlockingSlot(new ArmorType(layer, zone));
    public int GetSlotBlockingSlotIndex(ArmorType armorType) => IndexFromArmorType(GetSlotBlockingSlot(armorType));
    public int GetSlotBlockingSlotIndex(ArmorLayers layer, DamageZone zone) => IndexFromArmorType(GetSlotBlockingSlot(new ArmorType(layer, zone)));

    public bool IsSlotAvailable(ArmorType armorType) => !_slotsByType.Where(entry => !entry.Value.Empty).Any(entry => entry.Value.StoredArmoredType.Intersect(armorType));
    public bool IsSlotAvailable(ArmorLayers layer, DamageZone zone) => IsSlotAvailable(new ArmorType(layer, zone));
    public bool IsSlotAvailable(int index) => IsSlotAvailable(ArmorTypeFromIndex(index));

    public bool CanHoldArmorPiece(ArmorType armorType)
    {
        return !_slotsByType.Where(entry => !entry.Value.Empty).Any(entry => entry.Value.StoredArmoredType.Intersect(armorType));
    }
    public bool CanHoldArmorPiece(IArmor armor) => CanHoldArmorPiece(armor.ArmorType);
    public bool CanHoldArmorPiece(ArmorLayers layer, DamageZone zone) => CanHoldArmorPiece(new ArmorType(layer, zone));

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
    internal const int _clothesArmorSlots = 3;
    internal static readonly int _clothesSlotsCount = Enum.GetValues<EnumCharacterDressType>().Length - _clothesArmorSlots - 1;
    internal static readonly int _vanillaSlots = _clothesSlotsCount + _clothesArmorSlots;
    internal static readonly int _moddedArmorSlotsCount = (Enum.GetValues<ArmorLayers>().Length - 1) * (Enum.GetValues<DamageZone>().Length - 1);
    internal static readonly int _totalSlotsNumber = _clothesSlotsCount + _clothesArmorSlots + _moddedArmorSlotsCount;
    internal event Action? _onSlotModified;

    protected override ItemSlot NewSlot(int slotId)
    {
        if (slotId < _clothesSlotsCount)
        {
            ItemSlotCharacter slot = new((EnumCharacterDressType)slotId, this);
            _clothesSlotsIcons.TryGetValue((EnumCharacterDressType)slotId, out slot.BackgroundIcon);
            return slot;
        }
        else if (slotId < _vanillaSlots)
        {
            ArmorSlot slot = new(this, ArmorType.Empty);
            slot.DrawUnavailable = true;
            return slot;
        }
        else
        {
            ArmorType armorType = ArmorTypeFromIndex(slotId);
            ArmorSlot slot = new(this, armorType);
            _slotsByType[armorType] = slot;
            _armorSlotsIcons.TryGetValue(armorType, out slot.BackgroundIcon);
            return slot;
        }
    }

    private static bool IsVanillaArmorSlot(int index) => index >= _clothesSlotsCount && index < _clothesSlotsCount + _clothesArmorSlots;

    private static ArmorType ArmorTypeFromIndex(int index)
    {
        int zonesCount = Enum.GetValues<DamageZone>().Length - 1;

        if (index < _vanillaSlots) return ArmorType.Empty;

        ArmorLayers layer = ArmorLayerFromIndex((index - _vanillaSlots) / zonesCount);
        DamageZone zone = DamageZoneFromIndex(index - _vanillaSlots - IndexFromArmorLayer(layer) * zonesCount);

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
    private static int IndexFromDamageZone(DamageZone index)
    {
        return index switch
        {
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


    private void RecalculateResists()
    {
        foreach (DamageZone zone in Enum.GetValues<DamageZone>())
        {
            DamageResistData resist = DamageResistData.Combine(_slotsByType.Where(entry => (entry.Key.Slots & zone) != 0).Select(entry => entry.Value.Resists));
            Resists[zone] = resist;
        }
    }
    private ArmorType CalculateOccupiedSlots() => ArmorType.Combine(_slotsByType.Values.Select(x => x.StoredArmoredType));
}