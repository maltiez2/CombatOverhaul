using System.Numerics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace CombatOverhaul.RangedSystems;

public struct ReloadPacket
{
    public string InventoryId { get; set; }
    public int? SlotId { get; set; }
    public int Amount { get; set; }
    public int ItemId { get; set; }
    public bool RightHand { get; set; }
    public Guid ReloadId { get; set; }
}
public struct ReloadConfirmPacket
{
    public Guid ReloadId { get; set; }
    public bool Success { get; set; }
}
public struct ShotPacket
{
    public Guid ShotId { get; set; }
    public Vector3 Position { get; set; }
    public Vector3 Velocity { get; set; }
    public int ItemId { get; set; }
    public int Amount { get; set; }
    public bool RightHand { get; set; }
}
public struct ShotConfirmPacket
{
    public Guid ShotId { get; set; }
    public bool Success { get; set; }
}

public class RangedWeaponSystemClient
{
    public const string NetworkChannelId = "CombatOverhaul:rangeWeapon";

    public RangedWeaponSystemClient(ICoreClientAPI api)
    {
        _api = api;
        _clientChannel = api.Network.RegisterChannel(NetworkChannelId)
            .RegisterMessageType<ReloadPacket>()
            .RegisterMessageType<ReloadConfirmPacket>()
            .RegisterMessageType<ShotPacket>()
            .RegisterMessageType<ShotConfirmPacket>()
            .SetMessageHandler<ReloadConfirmPacket>(HandleReloadPacket)
            .SetMessageHandler<ShotConfirmPacket>(HandleShotPacket);
    }

    public void Reload(ItemSlot weapon, ItemSlot ammo, int amount, bool rightHand, Action<bool> reloadCallback)
    {
        Guid id = Guid.NewGuid();
        _callbacks[id] = reloadCallback;

        InventoryBase inventory = ammo.Inventory;
        int slotId = inventory.GetSlotId(weapon);

        ReloadPacket packet = new()
        {
            InventoryId = inventory.InventoryID,
            SlotId = slotId,
            Amount = amount,
            RightHand = rightHand,
            ItemId = weapon.Itemstack?.Item?.Id ?? 0,
            ReloadId = id
        };

        _clientChannel.SendPacket(packet);
    }
    public void Shoot(ItemSlot weapon, int amount, Vector3 position, Vector3 velocity, bool rightHand, Action<bool> shootCallback)
    {
        Guid id = Guid.NewGuid();
        _callbacks[id] = shootCallback;

        ShotPacket packet = new()
        {
            ShotId = id,
            Position = position,
            Velocity = velocity,
            ItemId = weapon.Itemstack?.Item?.Id ?? 0,
            Amount = amount,
            RightHand = rightHand
        };

        _clientChannel.SendPacket(packet);
    }

    private readonly ICoreClientAPI _api;
    private readonly IClientNetworkChannel _clientChannel;
    private readonly Dictionary<Guid, Action<bool>> _callbacks = new();

    private void HandleReloadPacket(ReloadConfirmPacket packet)
    {
        if (_callbacks.TryGetValue(packet.ReloadId, out Action<bool>? callback))
        {
            callback.Invoke(packet.Success);
        }
    }

    private void HandleShotPacket(ShotConfirmPacket packet)
    {
        if (_callbacks.TryGetValue(packet.ShotId, out Action<bool>? callback))
        {
            callback.Invoke(packet.Success);
        }
    }
}

public class RangedWeaponSystemServer
{
    public const string NetworkChannelId = "CombatOverhaul:rangeWeapon";

    public RangedWeaponSystemServer(ICoreServerAPI api)
    {
        _serverChannel = api.Network.RegisterChannel(NetworkChannelId)
            .RegisterMessageType<ReloadPacket>()
            .RegisterMessageType<ReloadConfirmPacket>()
            .RegisterMessageType<ShotPacket>()
            .RegisterMessageType<ShotConfirmPacket>()
            .SetMessageHandler<ReloadPacket>(HandleReloadPacket)
            .SetMessageHandler<ShotPacket>(HandleShotPacket);
    }

    private readonly IServerNetworkChannel _serverChannel;

    private void HandleReloadPacket(IServerPlayer player, ReloadPacket packet)
    {
        ItemSlot weaponSlot = GetWeaponSlot(player, packet.RightHand);

        if (weaponSlot.Itemstack?.Item?.Id != packet.ItemId)
        {
            OnFailedReload(player, packet);
            return;
        }

        if (weaponSlot.Itemstack.Item is not IRangedWeapon rangedWeapon)
        {
            OnFailedReload(player, packet);
            return;
        }

        ItemSlot? ammoSlot = null;
        if (packet.SlotId.HasValue)
        {
            ammoSlot = GetAmmoSlot(player, packet.InventoryId, packet.SlotId.Value);
        }

        if (rangedWeapon.ServerWeaponLogic.Reload(player, weaponSlot, ammoSlot, packet))
        {
            OnSuccessfulReload(player, packet);
        }
        else
        {
            OnFailedReload(player, packet);
        }
    }

    private void HandleShotPacket(IServerPlayer player, ShotPacket packet)
    {
        ItemSlot weaponSlot = GetWeaponSlot(player, packet.RightHand);

        if (weaponSlot.Itemstack?.Item?.Id != packet.ItemId)
        {
            OnFailedShot(player, packet);
            return;
        }

        if (weaponSlot.Itemstack.Item is not IRangedWeapon rangedWeapon)
        {
            OnFailedShot(player, packet);
            return;
        }

        if (rangedWeapon.ServerWeaponLogic.Shoot(player, weaponSlot, packet))
        {
            OnSuccessfulShot(player, packet);
        }
        else
        {
            OnFailedShot(player, packet);
        }
    }

    private void OnFailedReload(IServerPlayer player, ReloadPacket packet) => _serverChannel.SendPacket(new ReloadConfirmPacket() { ReloadId = packet.ReloadId, Success = false }, player);
    private void OnSuccessfulReload(IServerPlayer player, ReloadPacket packet) => _serverChannel.SendPacket(new ReloadConfirmPacket() { ReloadId = packet.ReloadId, Success = true }, player);
    private void OnFailedShot(IServerPlayer player, ShotPacket packet) => _serverChannel.SendPacket(new ShotConfirmPacket() { ShotId = packet.ShotId, Success = false }, player);
    private void OnSuccessfulShot(IServerPlayer player, ShotPacket packet) => _serverChannel.SendPacket(new ShotConfirmPacket() { ShotId = packet.ShotId, Success = true }, player);
    private static ItemSlot GetWeaponSlot(IServerPlayer player, bool RightHand) => RightHand ? player.Entity.RightHandItemSlot : player.Entity.LeftHandItemSlot;
    private static ItemSlot GetAmmoSlot(IServerPlayer player, string inventoryId, int slotId) => player.InventoryManager.GetInventory(inventoryId)[slotId];
}

public interface IRangedWeaponLogicServer
{
    bool Reload(IServerPlayer player, ItemSlot slot, ItemSlot? ammoSlot, ReloadPacket packet);
    bool Shoot(IServerPlayer player, ItemSlot slot, ShotPacket packet);
}

public interface IRangedWeapon
{
    IRangedWeaponLogicServer ServerWeaponLogic { get; }
}

public class ItemInventory
{
    public string Id { get; }
    public List<ItemStack> Items { get; } = new();
    public string Attribute => $"CombatOverhaul:inventory.{Id}";

    public ItemInventory(ItemSlot slot, string id)
    {
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

    public static ItemInventory Read(ItemSlot slot, string id) => new(slot, id);
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
}
