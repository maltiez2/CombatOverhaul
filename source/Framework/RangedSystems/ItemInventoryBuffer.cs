using Vintagestory.API.Common;

namespace CombatOverhaul.RangedSystems;

public class ItemInventoryBuffer
{
    public string Id { get; private set; } = "";
    public List<ItemStack> Items { get; private set; } = new();
    public string Attribute => $"CombatOverhaul:inventory.{Id}";

    public ItemInventoryBuffer()
    {
    }

    public void Read(ItemSlot slot, string id)
    {
        Clear();

        Id = id;

        if (!slot.Itemstack.Attributes.HasAttribute(Attribute))
        {
            return;
        }

        byte[] serialized = slot.Itemstack.Attributes.GetBytes(Attribute);

        using MemoryStream input = new(serialized);
        using BinaryReader stream = new(input);
        int size = stream.ReadInt32();
        for (int index = 0; index < size; index++)
        {
            Items.Add(new ItemStack(stream));
        }
    }
    public void Write(ItemSlot slot)
    {
        using MemoryStream memoryStream = new();
        using (BinaryWriter stream = new(memoryStream))
        {
            stream.Write(Items.Count);
            for (int index = 0; index < Items.Count; index++)
            {
                Items[index].ToBytes(stream);
            }
        }

        slot.Itemstack.Attributes.RemoveAttribute(Attribute);
        slot.Itemstack.Attributes.SetBytes(Attribute, memoryStream.ToArray());
        slot.MarkDirty();
    }
    public void Clear()
    {
        Id = "";
        Items.Clear();
    }
}