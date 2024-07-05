using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using CombatOverhaul.Colliders;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace CombatOverhaul.MeleeSystems;

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public struct MeleeAttackPacket
{
    public bool RightHand { get; set; }
    public int Direction { get; set; }
    public MeleeAttackDamagePacket[] MeleeAttackDamagePackets { get; set; }
}

public abstract class MeleeSystem
{
    public const string NetworkChannelId = "CombatOverhaul:damage-packets";
}

public readonly struct AttackId
{
    public readonly int ItemId;
    public readonly int Id;

    public AttackId(int itemId, int id)
    {
        ItemId = itemId;
        Id = id;
    }
}

public sealed class MeleeSystemClient : MeleeSystem
{
    public MeleeSystemClient(ICoreClientAPI api)
    {
        _api = api;
        _clientChannel = api.Network.RegisterChannel(NetworkChannelId)
            .RegisterMessageType<MeleeAttackPacket>();
    }

    public bool Register(AttackId id, MeleeAttack attack) => _attacks.TryAdd(id, attack);
    public bool Register(AttackId id, MeleeAttackStats stats) => _attacks.TryAdd(id, new(_api, id.Id, id.ItemId, stats));
    public void Start(AttackId id, Action<AttackResult> callback, bool rightHand = true)
    {
        ItemSlot slot = rightHand ? _api.World.Player.Entity.RightHandItemSlot : _api.World.Player.Entity.LeftHandItemSlot;

        if (_attacks[id].ItemId != (slot.Itemstack?.Item?.Id ?? 0))
        {
            return;
        }

        Stop(rightHand);

        _attacks[id].Start(_api.World.Player);

        long timer = _api.World.RegisterGameTickListener(dt => Step(dt, callback, _attacks[id], _api.World.Player, slot, rightHand), 0);
        if (rightHand)
        {
            _rightHandAttackTimer = timer;
        }
        else
        {
            _leftHandAttackTimer = timer;
        }
    }
    public void Stop(bool rightHand = true)
    {
        if (rightHand)
        {
            _api.World.UnregisterGameTickListener(_rightHandAttackTimer);
        }
        else
        {
            _api.World.UnregisterGameTickListener(_leftHandAttackTimer);
        }
    }

    private readonly ICoreClientAPI _api;
    private readonly IClientNetworkChannel _clientChannel;
    private readonly Dictionary<AttackId, MeleeAttack> _attacks = new();
    private long _rightHandAttackTimer = -1;
    private long _leftHandAttackTimer = -1;

    private void SendPacket(bool rightHand, IEnumerable<MeleeAttackDamagePacket> packets)
    {
        _clientChannel.SendPacket(new MeleeAttackPacket
        {
            RightHand = rightHand,
            MeleeAttackDamagePackets = packets.ToArray()
        });
    }
    private void Step(float dt, Action<AttackResult> callback, MeleeAttack attack, IPlayer player, ItemSlot slot, bool rightHand)
    {
        AttackResult result = attack.Step(player, dt, slot, out IEnumerable<MeleeAttackDamagePacket> damagePackets, rightHand);

        if (damagePackets.Any())
        {
            SendPacket(rightHand, damagePackets);
        }

        if (result.Result != AttackResultFlag.None) callback.Invoke(result);
    }
}

public sealed class MeleeSystemServer : MeleeSystem
{
    public MeleeSystemServer(ICoreServerAPI api)
    {
        _api = api;
        api.Network.RegisterChannel(NetworkChannelId)
            .RegisterMessageType<MeleeAttackPacket>()
            .SetMessageHandler<MeleeAttackPacket>(HandlePacket);
    }

    public void Register(int id, int itemId, MeleeAttackStats stats)
    {
        stats.DamageTypes.Select((stats, index) => new MeleeAttackDamageType(new(itemId, id, index), stats)).Foreach(damageType => Register(damageType));
    }
    public bool Register(MeleeAttackDamageType damageType) => _attackDamageTypes.TryAdd(damageType.Id, damageType);

    private readonly ICoreServerAPI _api;
    private readonly Dictionary<MeleeAttackDamageId, MeleeAttackDamageType> _attackDamageTypes = new();

    private void HandlePacket(IServerPlayer player, MeleeAttackPacket packet)
    {
        long playerEntityId = player.Entity.EntityId;
        int itemId = packet.RightHand ? player.Entity.RightHandItemSlot.Itemstack?.Item?.Id ?? -1 : player.Entity.LeftHandItemSlot.Itemstack?.Item?.Id ?? -1;

        if (itemId == -1)
        {
            return;
        }

        foreach (MeleeAttackDamagePacket damagePacket in packet.MeleeAttackDamagePackets)
        {
            if (damagePacket.AttackerEntityId != playerEntityId && damagePacket.Id.ItemId != itemId)
            {
                continue;
            }

            if (!_attackDamageTypes.TryGetValue(damagePacket.Id, out MeleeAttackDamageType damageType))
            {
                continue;
            }

            Entity? targetEntity = _api.World.GetEntityById(damagePacket.TargetEntityId);

            if (targetEntity == null)
            {
                continue;
            }

            damageType.Attack(player.Entity, targetEntity, new(damagePacket.Position[0], damagePacket.Position[1], damagePacket.Position[2]), damagePacket.Collider); // @TODO: check distance and reach first

            if (damageType.DurabilityDamage > 0)
            {
                ItemSlot itemSlot = packet.RightHand ? player.Entity.RightHandItemSlot : player.Entity.LeftHandItemSlot;
                itemSlot.Itemstack.Collectible.DamageItem(_api.World, player.Entity, itemSlot, damageType.DurabilityDamage);
                itemSlot.MarkDirty();
            }
        }
    }
}