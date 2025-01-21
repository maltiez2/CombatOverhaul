using CombatOverhaul.DamageSystems;
using CombatOverhaul.Utils;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.Common;

namespace CombatOverhaul.Armor;

public class ClothesSlot : ItemSlotCharacter
{
    public IWorldAccessor? World { get; set; }
    public string? OwnerUUID { get; set; }
    public bool PreviouslyHeldBag { get; set; } = false;

    public ClothesSlot(EnumCharacterDressType type, InventoryBase inventory) : base(type, inventory)
    {
    }

    public override void ActivateSlot(ItemSlot sourceSlot, ref ItemStackMoveOperation op)
    {
        if (Itemstack != null) EmptyBag(Itemstack);

        base.ActivateSlot(sourceSlot, ref op);
        OnItemSlotModified(null);
    }

    public override ItemStack? TakeOutWhole()
    {
        ItemStack itemStack = base.TakeOutWhole();

        if (itemStack != null) EmptyBag(itemStack);

        return itemStack;
    }

    public override ItemStack? TakeOut(int quantity)
    {
        ItemStack stack = base.TakeOut(quantity);

        EmptyBag(stack);

        return stack;
    }

    protected override void FlipWith(ItemSlot itemSlot)
    {
        base.FlipWith(itemSlot);

        ItemStack stack = itemSlot.Itemstack;

        if (stack != null) EmptyBag(stack);
    }

    protected void EmptyBag(ItemStack stack)
    {
        IHeldBag? bag = stack?.Item?.GetCollectibleInterface<IHeldBag>();

        try
        {
            if (bag != null && World != null && World.PlayerByUid(OwnerUUID)?.Entity != null)
            {
                ItemStack?[] bagContent = bag.GetContents(stack, World);
                if (bagContent != null)
                {
                    foreach (ItemStack? bagContentStack in bagContent)
                    {
                        if (bagContentStack != null) World.SpawnItemEntity(bagContentStack, World.PlayerByUid(OwnerUUID)?.Entity?.SidedPos.AsBlockPos);
                    }
                }

                bag.Clear(stack);
            }
        }
        catch (Exception exception)
        {
            LoggerUtil.Error(World?.Api, this, $"Error on emptying bag '{stack?.Collectible?.Code}': \n{exception}");
        }
    }

    protected void ModifyBackpackSlot()
    {
        InventoryPlayerBackPacks? backpack = GetBackpackInventory();
        if (backpack != null)
        {
            backpack[0].MarkDirty();
        }
    }

    protected InventoryPlayerBackPacks? GetBackpackInventory()
    {
        return World?.PlayerByUid(OwnerUUID)?.InventoryManager.GetOwnInventory(GlobalConstants.backpackInvClassName) as InventoryPlayerBackPacks;
    }
}

public class ArmorSlot : ItemSlot
{
    public ArmorType ArmorType { get; }
    public ArmorType StoredArmoredType => GetStoredArmorType();
    public DamageZone DamageZone => ArmorType.Slots;
    public ArmorLayers Layer => ArmorType.Layers;
    public DamageResistData Resists => GetResists();
    public override int MaxSlotStackSize => 1;
    public bool Available => _inventory.IsSlotAvailable(ArmorType);
    public List<int> SlotsWithSameItem { get; } = new();
    public IWorldAccessor? World { get; set; }
    public string? OwnerUUID { get; set; }
    public bool PreviouslyHeldBag { get; set; } = false;

    public ArmorSlot(InventoryBase inventory, ArmorType armorType) : base(inventory)
    {
        ArmorType = armorType;
        _inventory = inventory as ArmorInventory ?? throw new Exception();
    }

    public override bool CanHold(ItemSlot sourceSlot)
    {
        if (DrawUnavailable || !base.CanHold(sourceSlot) || !IsArmor(sourceSlot.Itemstack.Collectible, out IArmor? armor)) return false;

        if (armor == null || !_inventory.CanHoldArmorPiece(armor)) return false;

        return armor.ArmorType.Intersect(ArmorType);
    }
    public override void ActivateSlot(ItemSlot sourceSlot, ref ItemStackMoveOperation op)
    {
        if (Itemstack != null) EmptyBag(Itemstack);

        base.ActivateSlot(sourceSlot, ref op);
        OnItemSlotModified(null);
    }
    public override ItemStack? TakeOutWhole()
    {
        ItemStack itemStack = base.TakeOutWhole();

        if (itemStack != null) EmptyBag(itemStack);

        return itemStack;
    }
    public override ItemStack? TakeOut(int quantity)
    {
        ItemStack stack = base.TakeOut(quantity);

        EmptyBag(stack);

        return stack;
    }
    
    protected override void FlipWith(ItemSlot itemSlot)
    {
        base.FlipWith(itemSlot);

        ItemStack stack = itemSlot.Itemstack;

        if (stack != null) EmptyBag(stack);
    }
    protected void EmptyBag(ItemStack stack)
    {
        IHeldBag? bag = stack?.Item?.GetCollectibleInterface<IHeldBag>();

        try
        {
            if (bag != null && World != null && World.PlayerByUid(OwnerUUID)?.Entity != null)
            {
                ItemStack?[] bagContent = bag.GetContents(stack, World);
                if (bagContent != null)
                {
                    foreach (ItemStack? bagContentStack in bagContent)
                    {
                        if (bagContentStack != null) World.SpawnItemEntity(bagContentStack, World.PlayerByUid(OwnerUUID)?.Entity?.SidedPos.AsBlockPos);
                    }
                }

                bag.Clear(stack);
            }
        }
        catch (Exception exception)
        {
            LoggerUtil.Error(World?.Api, this, $"Error on emptying bag '{stack?.Collectible?.Code}': \n{exception}");
        }
    }
    protected void ModifyBackpackSlot()
    {
        InventoryPlayerBackPacks? backpack = GetBackpackInventory();
        if (backpack != null)
        {
            backpack[0].MarkDirty();
        }
    }
    protected InventoryPlayerBackPacks? GetBackpackInventory()
    {
        return World?.PlayerByUid(OwnerUUID)?.InventoryManager.GetOwnInventory(GlobalConstants.backpackInvClassName) as InventoryPlayerBackPacks;
    }


    private readonly ArmorInventory _inventory;
    private ArmorType GetStoredArmorType()
    {
        if (Itemstack?.Item != null && IsArmor(Itemstack.Collectible, out IArmor? armor) && armor != null)
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
        if (Itemstack?.Item != null && IsArmor(Itemstack.Collectible, out IArmor? armor) && armor != null)
        {
            return armor.Resists;
        }
        else if (Itemstack?.Item != null && IsModularArmor(Itemstack.Collectible, out IModularArmor? modularArmor) && modularArmor != null)
        {
            return modularArmor.GetResists(this, ArmorType);
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
    private static bool IsModularArmor(CollectibleObject item, out IModularArmor? armor)
    {
        if (item is IModularArmor armorItem)
        {
            armor = armorItem;
            return true;
        }

        CollectibleBehavior? behavior = item.CollectibleBehaviors.FirstOrDefault(x => x is IModularArmor);

        if (behavior is not IModularArmor armorBehavior)
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
        FillArmorIconsDict(api);

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

        FillSlotsOwnerAndWorld();
    }
    public ArmorInventory(string inventoryID, ICoreAPI api) : base(inventoryID, api)
    {
        FillArmorIconsDict(api);

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

        FillSlotsOwnerAndWorld();
    }

    public override ItemSlot this[int slotId] { get => _slots[slotId]; set => LoggerUtil.Warn(Api, this, "Armor slots cannot be set"); }

    public override int Count => _totalSlotsNumber;

    public override void FromTreeAttributes(ITreeAttribute tree)
    {
        _slots = GenEmptySlots(_totalSlotsNumber);
        for (int index = 0; index < _slots.Length; index++)
        {
            ItemStack? itemStack = tree.GetTreeAttribute("slots")?.GetItemstack(index.ToString() ?? "");

            if (itemStack != null)
            {
                if (Api?.World != null) itemStack.ResolveBlockOrItem(Api.World);

                _slots[index].Itemstack = itemStack;
            }

            if (IsVanillaArmorSlot(index))
            {
                _slots[index].DrawUnavailable = true;
                _slots[index].HexBackgroundColor = "#884444";
                _slots[index].MaxSlotStackSize = 0;
            }
        }

        FillSlotsOwnerAndWorld();
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

        if (slot is ClothesSlot clothesSlot)
        {
            bool containsBag = clothesSlot.Itemstack?.Collectible?.GetCollectibleInterface<IHeldBag>() != null;
            if (clothesSlot.PreviouslyHeldBag || containsBag)
            {
                ReloadBagInventory();
            }
            clothesSlot.PreviouslyHeldBag = containsBag;
        }

        if (slot is ArmorSlot armorSlot)
        {
            bool containsBag = armorSlot.Itemstack?.Collectible?.GetCollectibleInterface<IHeldBag>() != null;
            if (armorSlot.PreviouslyHeldBag || containsBag)
            {
                ReloadBagInventory();
            }
            armorSlot.PreviouslyHeldBag = containsBag;
        }

        OnSlotModified?.Invoke();
        if (slot is ArmorSlot)
        {
            OnArmorSlotModified?.Invoke();
        }
    }
    public override object ActivateSlot(int slotId, ItemSlot sourceSlot, ref ItemStackMoveOperation op)
    {
        object result = base.ActivateSlot(slotId, sourceSlot, ref op);

        if (slotId < _clothesSlotsCount)
        {
            ReloadBagInventory();
        }

        return result;
    }
    public override void DiscardAll()
    {
        base.DiscardAll();

        ReloadBagInventory();
    }
    public override void DropAll(Vec3d pos, int maxStackSize = 0)
    {
        base.DropAll(pos, maxStackSize);

        ReloadBagInventory();
    }

    public static int IndexFromArmorType(ArmorLayers layer, DamageZone zone)
    {
        int zonesCount = Enum.GetValues<DamageZone>().Length - 1;

        return _vanillaSlots + IndexFromArmorLayer(layer) * zonesCount + IndexFromDamageZone(zone);
    }
    public static int IndexFromArmorType(ArmorType type) => IndexFromArmorType(type.Layers, type.Slots);

    public IEnumerable<ArmorType> GetSlotsBlockedSlot(ArmorType armorType) => _slotsByType
        .Where(entry => entry.Value.ArmorType.Intersect(armorType))
        .Select(entry => entry.Key);
    public IEnumerable<int> GetSlotsBlockedSlotIndices(ArmorType armorType) => GetSlotsBlockedSlot(armorType).Select(IndexFromArmorType);

    public IEnumerable<ArmorType> GetSlotBlockingSlots(ArmorType armorType) => _slotsByType
        .Where(entry => !entry.Value.Empty)
        .Where(entry => entry.Value.StoredArmoredType.Intersect(armorType))
        .Select(entry => entry.Key);
    public ArmorType GetSlotBlockingSlot(ArmorType armorType) => GetSlotBlockingSlots(armorType).FirstOrDefault(defaultValue: ArmorType.Empty);
    public ArmorType GetSlotBlockingSlot(ArmorLayers layer, DamageZone zone) => GetSlotBlockingSlot(new ArmorType(layer, zone));
    public IEnumerable<int> GetSlotBlockingSlotsIndices(ArmorType armorType) => GetSlotBlockingSlots(armorType).Select(IndexFromArmorType);
    public int GetSlotBlockingSlotIndex(ArmorType armorType) => IndexFromArmorType(GetSlotBlockingSlot(armorType));
    public int GetSlotBlockingSlotIndex(ArmorLayers layer, DamageZone zone) => IndexFromArmorType(GetSlotBlockingSlot(new ArmorType(layer, zone)));

    public ArmorType GetFittingSlot(ArmorType armorType) => _slotsByType.Keys.Where(slot => slot.Intersect(armorType)).FirstOrDefault(ArmorType.Empty);
    public int GetFittingSlotIndex(ArmorType armorType) => IndexFromArmorType(GetFittingSlot(armorType));

    public bool IsSlotAvailable(ArmorType armorType) => !_slotsByType.Where(entry => !entry.Value.Empty).Any(entry => entry.Value.StoredArmoredType.Intersect(armorType));
    public bool IsSlotAvailable(ArmorLayers layer, DamageZone zone) => IsSlotAvailable(new ArmorType(layer, zone));
    public bool IsSlotAvailable(int index) => IsSlotAvailable(ArmorTypeFromIndex(index));

    public bool CanHoldArmorPiece(ArmorType armorType)
    {
        return !_slotsByType.Where(entry => !entry.Value.Empty).Any(entry => entry.Value.StoredArmoredType.Intersect(armorType));
    }
    public bool CanHoldArmorPiece(IArmor armor) => CanHoldArmorPiece(armor.ArmorType);
    public bool CanHoldArmorPiece(ArmorLayers layer, DamageZone zone) => CanHoldArmorPiece(new ArmorType(layer, zone));

    public IEnumerable<ArmorSlot> GetNotEmptyZoneSlots(DamageZone zone)
    {
        List<ArmorSlot> slots = new();

        ArmorSlot? outer = GetSlotForArmorType(ArmorLayers.Outer, zone);
        ArmorSlot? middle = GetSlotForArmorType(ArmorLayers.Middle, zone);
        ArmorSlot? skin = GetSlotForArmorType(ArmorLayers.Skin, zone);

        if (outer != null && !outer.Empty) slots.Add(outer);
        if (middle != null && !middle.Empty && middle != outer) slots.Add(middle);
        if (skin != null && !skin.Empty && skin != outer && skin != middle) slots.Add(skin);

        return slots;
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
    internal const int _clothesArmorSlots = 3;
    internal static readonly int _clothesSlotsCount = Enum.GetValues<EnumCharacterDressType>().Length - _clothesArmorSlots - 1;
    internal static readonly int _vanillaSlots = _clothesSlotsCount + _clothesArmorSlots;
    internal static readonly int _moddedArmorSlotsCount = (Enum.GetValues<ArmorLayers>().Length - 1) * (Enum.GetValues<DamageZone>().Length - 1);
    internal static readonly int _totalSlotsNumber = _clothesSlotsCount + _clothesArmorSlots + _moddedArmorSlotsCount;
    internal event Action? OnSlotModified;
    internal event Action? OnArmorSlotModified;
    private static readonly FieldInfo? _backpackBagInventory = typeof(InventoryPlayerBackPacks).GetField("bagInv", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? _backpackBagSlots = typeof(InventoryPlayerBackPacks).GetField("bagSlots", BindingFlags.NonPublic | BindingFlags.Instance);

    protected override ItemSlot NewSlot(int slotId)
    {
        if (slotId < _clothesSlotsCount)
        {
            ClothesSlot slot = new((EnumCharacterDressType)slotId, this);
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

    private void FillSlotsOwnerAndWorld()
    {
        foreach (ItemSlot slot in _slots)
        {
            if (slot is ClothesSlot clothesSlot)
            {
                clothesSlot.OwnerUUID = playerUID;
                clothesSlot.World = Api.World;
            }
            else if (slot is ArmorSlot armorSlot)
            {
                armorSlot.OwnerUUID = playerUID;
                armorSlot.World = Api.World;
            }
        }
    }

    private void FillArmorIconsDict(ICoreAPI api)
    {
        foreach (ArmorLayers layer in Enum.GetValues<ArmorLayers>())
        {
            foreach (DamageZone zone in Enum.GetValues<DamageZone>())
            {
                string iconPath = $"combatoverhaul:textures/gui/icons/armor-{layer}-{zone}.svg";
                string iconCode = $"combatoverhaul-armor-{layer}-{zone}";

                if (api.Assets.Exists(new AssetLocation(iconPath)))
                {
                    _armorSlotsIcons.Add(new(layer, zone), iconCode);
                }
            }
        }
    }

    private static bool IsVanillaArmorSlot(int index) => index >= _clothesSlotsCount && index < _clothesSlotsCount + _clothesArmorSlots;

    internal static ArmorType ArmorTypeFromIndex(int index)
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

    private ArmorSlot? GetSlotForArmorType(ArmorLayers layer, DamageZone zone)
    {
        ArmorType skinSlotType = GetSlotBlockingSlot(layer, zone);
        if (skinSlotType.Slots != DamageZone.None && skinSlotType.Layers != ArmorLayers.None)
        {
            return _slotsByType[GetSlotBlockingSlot(layer, zone)];
        }
        else
        {
            return null;
        }
    }

    private InventoryPlayerBackPacks? GetBackpackInventory()
    {
        return Player?.InventoryManager.GetOwnInventory(GlobalConstants.backpackInvClassName) as InventoryPlayerBackPacks;
    }

    private void ReloadBagInventory()
    {
        InventoryPlayerBackPacks? backpack = GetBackpackInventory();
        if (backpack == null) return;

        BagInventory? bag = (BagInventory?)_backpackBagInventory?.GetValue(backpack);
        ItemSlot[]? bagSlots = (ItemSlot[]?)_backpackBagSlots?.GetValue(backpack);
        if (bag == null || bagSlots == null) return;

        bag.ReloadBagInventory(backpack, bagSlots);
    }
}