using CombatOverhaul.DamageSystems;
using CombatOverhaul.Utils;
using System.Collections.Immutable;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

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
    public event Action<ArmorType>? ItemPut;
    public event Action<ArmorType>? ItemTaken;

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

        if (result > 0) ItemPut?.Invoke(ArmorType);

        return result;
    }
    public override ItemStack TakeOutWhole()
    {
        ItemStack result = base.TakeOutWhole();

        if (Itemstack == null || Itemstack?.StackSize == 0)
        {
            ItemTaken?.Invoke(ArmorType);
        }

        return result;
    }
}

public sealed class ArmorInventory : InventoryBasePlayer
{
    public ArmorInventory(string className, string playerUID, ICoreAPI api) : base(className, playerUID, api)
    {
        GenerateEmptySlots();
    }

    public override ItemSlot this[int slotId] { get => _slots[slotId]; set => LoggerUtil.Warn(Api, this, "Armor slots cannot be set"); }

    public override int Count => _slots.Length;
    public event Action<ArmorType>? ArmorChanged;

    public override void FromTreeAttributes(ITreeAttribute tree)
    {
        GenerateEmptySlots();
        for (int index = 0; index < _slots.Length; index++)
        {
            ItemStack? itemStack = tree.GetTreeAttribute("slots")?.GetItemstack(index.ToString() ?? "");
            _slots[index].Itemstack = itemStack;
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

    private ImmutableArray<ArmorSlot> _slots;
    private ImmutableDictionary<ArmorType, ArmorSlot> _slotsByType = new Dictionary<ArmorType, ArmorSlot>().ToImmutableDictionary();

    private void GenerateEmptySlots()
    {
        List<ArmorSlot> slots = new();
        Dictionary<ArmorType, ArmorSlot> slotsByType = new();

        foreach (ArmorLayers layer in Enum.GetValues<ArmorLayers>().Where(layer => layer != ArmorLayers.None))
        {
            foreach (DamageZone slotType in Enum.GetValues<DamageZone>().Where(layer => layer != DamageZone.None))
            {
                ArmorType armorType = new(layer, slotType);
                ArmorSlot slot = new(this, armorType);
                slots.Add(slot);
                slotsByType.Add(armorType, slot);
                slot.ItemPut += OnItemPut;
                slot.ItemTaken += OnItemTaken;
            }
        }

        _slots = slots.ToImmutableArray();
        _slotsByType = slotsByType.ToImmutableDictionary();
    }
    private void OnItemPut(ArmorType armorType)
    {
        IEnumerable<KeyValuePair<ArmorType, ArmorSlot>> slots = _slotsByType
            .Where(entry => entry.Key.Slots != armorType.Slots || entry.Key.Layers != armorType.Layers)
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

        ArmorChanged?.Invoke(armorType);
    }
    private void OnItemTaken(ArmorType armorType)
    {
        IEnumerable<KeyValuePair<ArmorType, ArmorSlot>> slots = _slotsByType
            .Where(entry => entry.Key.Slots != armorType.Slots || entry.Key.Layers != armorType.Layers)
            .Where(entry => (entry.Key.Slots & armorType.Slots) != 0 && (entry.Key.Layers & armorType.Layers) != 0);

        foreach ((_, ArmorSlot slot) in slots)
        {
            slot.DrawUnavailable = false;
        }

        ArmorChanged?.Invoke(armorType);
    }
}