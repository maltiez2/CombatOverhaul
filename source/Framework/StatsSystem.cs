using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Server;

namespace CombatOverhaul.Inputs;

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public sealed class StatsPacket
{
    public string Stat { get; set; } = "";
    public string Category { get; set; } = "";
    public float Value { get; set; } = 0;
}

public class StatsSystemClient
{
    public StatsSystemClient(ICoreClientAPI api)
    {
        _api = api;
        _clientChannel = api.Network.RegisterChannel(_networkChannelId)
            .RegisterMessageType<StatsPacket>();
    }

    public void SetStat(string stat, string category, float value)
    {
        _clientChannel.SendPacket(new StatsPacket
        {
            Stat = stat,
            Category = category,
            Value = value
        });

        _api.World.Player.Entity.Stats.Set(stat, category, value);
    }

    private const string _networkChannelId = "CombatOverhaul:stats";
    private readonly IClientNetworkChannel _clientChannel;
    private readonly ICoreClientAPI _api;
}

public class StatsSystemServer
{
    public StatsSystemServer(ICoreServerAPI api)
    {
        api.Network.RegisterChannel(_networkChannelId)
            .RegisterMessageType<StatsPacket>()
            .SetMessageHandler<StatsPacket>(HandlePacket);
    }

    private const string _networkChannelId = "CombatOverhaul:stats";

    private void HandlePacket(IServerPlayer player, StatsPacket packet)
    {
        player.Entity.Stats.Set(packet.Stat, packet.Category, packet.Value);
    }
}