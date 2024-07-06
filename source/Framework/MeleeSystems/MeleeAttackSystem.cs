using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Server;

namespace CombatOverhaul.MeleeSystems;

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public struct MeleeAttackPacket
{
    public MeleeDamagePacket[] MeleeAttackDamagePackets { get; set; }
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
        _clientChannel = api.Network.RegisterChannel(NetworkChannelId)
            .RegisterMessageType<MeleeAttackPacket>();
    }

    public void SendPackets(IEnumerable<MeleeDamagePacket> packets)
    {
        _clientChannel.SendPacket(new MeleeAttackPacket
        {
            MeleeAttackDamagePackets = packets.ToArray()
        });
    }

    private readonly IClientNetworkChannel _clientChannel;
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

    private readonly ICoreServerAPI _api;

    private void HandlePacket(IServerPlayer player, MeleeAttackPacket packet)
    {
        foreach (MeleeDamagePacket damagePacket in packet.MeleeAttackDamagePackets)
        {
            damagePacket.Attack(_api);
        }
    }
}