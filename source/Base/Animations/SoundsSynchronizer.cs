using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace CombatOverhaul.Animations;

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class SoundPacket
{
    public string Code { get; set; } = "";
    public bool RandomizePitch { get; set; }
    public float Range { get; set; }
    public float Volume { get; set; }

    public SoundPacket()
    {
    }

    public SoundPacket(SoundFrame frame, int index)
    {
        Code = frame.Code[index];
        Range = frame.Range;
        Volume = frame.Volume;
        Range = frame.Range;
    }
}

public class SoundsSynchronizerClient
{
    public SoundsSynchronizerClient(ICoreClientAPI api)
    {
        _api = api;
        _channel = _api.Network.RegisterChannel("CombatOverhaul:sounds")
            .RegisterMessageType<SoundPacket>();
    }

    public void Play(SoundFrame frame)
    {
        int index = Math.Clamp((int)Math.Floor(_random.nextFloat(frame.Code.Length)), 0, frame.Code.Length - 1);

        _api.World.PlaySoundFor(new(frame.Code[index]), _api.World.Player, frame.RandomizePitch, frame.Range, frame.Volume);

        if (frame.Synchronize) _channel.SendPacket(new SoundPacket(frame, index));
    }

    public void Play(string code, bool randomizedPitch = false, float range = 32, float volume = 1, bool synchronize = true)
    {
        _api.World.PlaySoundFor(new(code), _api.World.Player, randomizedPitch, range, volume);

        SoundPacket packet = new()
        {
            Code = code,
            RandomizePitch = randomizedPitch,
            Range = range,
            Volume = volume
        };

        if (synchronize) _channel.SendPacket(packet);
    }

    private readonly ICoreClientAPI _api;
    private readonly IClientNetworkChannel _channel;
    private readonly NatFloat _random = new(0.5f, 0.5f, EnumDistribution.UNIFORM);
}

public class SoundsSynchronizerServer
{
    public SoundsSynchronizerServer(ICoreServerAPI api)
    {
        _api = api;
        _api.Network.RegisterChannel("CombatOverhaul:sounds")
            .RegisterMessageType<SoundPacket>()
            .SetMessageHandler<SoundPacket>(HandlePacket);
    }

    private readonly ICoreServerAPI _api;

    private void HandlePacket(IServerPlayer player, SoundPacket packet)
    {
        _api.World.PlaySoundAt(new(packet.Code), player.Entity, player, packet.RandomizePitch, packet.Range, packet.Volume);
    }
}
