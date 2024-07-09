using CombatOverhaul.DamageSystems;
using CombatOverhaul.Utils;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.Common;

namespace CombatOverhaul.Armor;

public class ArmorSlot : ItemSlot
{
    public ArmorType ArmorType { get; }
    public DamageZone DamageZone => ArmorType.Slots;
    public DamageResistData Resists { get; set; } = DamageResistData.Empty;
    public override int MaxSlotStackSize => 1;

    public ArmorSlot(InventoryBase inventory, ArmorType armorType) : base(inventory)
    {
        ArmorType = armorType;
        _inventory = inventory as ArmorInventory ?? throw new Exception();
    }

    public override bool CanHold(ItemSlot sourceSlot)
    {
        if (DrawUnavailable || !base.CanHold(sourceSlot) || IsArmor(sourceSlot.Itemstack.Item, out IArmor? armor)) return false;

        if (armor == null || !_inventory.CanHoldArmorPiece(armor)) return false;

        return armor.ArmorType.Intersect(ArmorType);
    }

    public override void OnItemSlotModified(ItemStack sinkStack)
    {
        if (IsArmor(Itemstack.Item, out IArmor? armor) && armor != null)
        {
            Resists = armor.Resists;
        }
        else
        {
            Resists = DamageResistData.Empty;
        }
    }

    private readonly ArmorInventory _inventory;

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
    public ArmorType OccupiedSlots { get; private set; } = ArmorType.Empty;

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
                    Player.Entity.TryGiveItemStack(itemStack);
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
        OccupiedSlots = CalculateOccupiedSlots();
    }
    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        tree.SetInt("qslots", _clothesArmorSlots);

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
        OccupiedSlots = CalculateOccupiedSlots();

        PlayerDamageModelBehavior? behavior = Player.Entity.GetBehavior<PlayerDamageModelBehavior>();
        if (behavior != null)
        {
            behavior.Resists = Resists;
        }
    }

    public bool IsArmorSlotAvailable(int index) => ArmorTypeFromIndex(index).Intersect(OccupiedSlots);

    public bool CanHoldArmorPiece(ArmorType armorType)
    {
        return !_slotsByType.Where(entry => !entry.Value.Empty).Any(entry => entry.Key.Intersect(armorType));
    }
    public bool CanHoldArmorPiece(IArmor armor) => CanHoldArmorPiece(armor.ArmorType);

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
    private static readonly int _clothesSlotsCount = Enum.GetValues<EnumCharacterDressType>().Length - _clothesArmorSlots - 1;
    private static readonly int _vanillaSlots = _clothesSlotsCount + _clothesArmorSlots;
    private static readonly int _moddedArmorSlotsCount = (Enum.GetValues<ArmorLayers>().Length - 1) * (Enum.GetValues<DamageZone>().Length - 1);
    private static readonly int _totalSlotsNumber = _clothesSlotsCount + _clothesArmorSlots + _moddedArmorSlotsCount;

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
            _armorSlotsIcons.TryGetValue(armorType, out slot.BackgroundIcon);
            return slot;
        }
    }

    private static bool IsVanillaArmorSlot(int index) => index >= _clothesSlotsCount && index <= _clothesSlotsCount + _clothesArmorSlots;
    private static ArmorType ArmorTypeFromIndex(int index)
    {
        int defaultSlotsCount = _clothesSlotsCount + _clothesArmorSlots;
        int zonesCount = Enum.GetValues<DamageZone>().Length - 1;

        if (index < defaultSlotsCount) return ArmorType.Empty;

        ArmorLayers layer = ArmorLayerFromIndex((index - defaultSlotsCount) / zonesCount);
        DamageZone zone = DamageZoneFromIndex(index - defaultSlotsCount - IndexFromArmorLayer(layer) * zonesCount);

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


    private void RecalculateResists()
    {
        foreach (DamageZone zone in Enum.GetValues<DamageZone>())
        {
            DamageResistData resist = DamageResistData.Combine(_slotsByType.Where(entry => (entry.Key.Slots & zone) != 0).Select(entry =>  entry.Value.Resists));
            Resists[zone] = resist;
        }
    }
    private ArmorType CalculateOccupiedSlots() => ArmorType.Combine(_slotsByType.Values.Select(x => x.ArmorType));
}