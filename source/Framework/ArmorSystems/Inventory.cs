using CombatOverhaul.DamageSystems;
using CombatOverhaul.Utils;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.Common;

namespace CombatOverhaul.Armor;

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
    private static readonly int _totalSlotsNumber = _clothesSlotsCount + (Enum.GetValues<ArmorLayers>().Length - 1) * (Enum.GetValues<DamageZone>().Length - 1);

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