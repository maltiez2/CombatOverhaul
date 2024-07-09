using CombatOverhaul.Colliders;
using CombatOverhaul.MeleeSystems;
using CombatOverhaul.Utils;
using ProtoBuf;
using System.Collections.Immutable;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace CombatOverhaul.DamageSystems;

[Flags]
public enum DamageZone
{
    None = 0,
    Head = 1,
    Face = 2,
    Neck = 4,
    Torso = 8,
    Arms = 16,
    Hands = 32,
    Legs = 64,
    Feet = 128
}

public sealed class PlayerDamageModelBehavior : EntityBehavior
{
    public PlayerDamageModelBehavior(Entity entity) : base(entity)
    {
    }

    public override string PropertyName() => "PlayerDamageModel";

    public PlayerDamageModel DamageModel { get; private set; } = new(Array.Empty<DamageZoneStatsJson>());
    public Dictionary<DamageZone, DamageResistData> Resists { get; set; } = new();
    public readonly ImmutableDictionary<string, DamageZone> CollidersToZones = new Dictionary<string, DamageZone>()
    {
        { "LowerTorso", DamageZone.Torso },
        { "UpperTorso", DamageZone.Torso },
        { "Head", DamageZone.Head },
        { "Neck", DamageZone.Neck },
        { "UpperArmR", DamageZone.Arms },
        { "UpperArmL", DamageZone.Arms },
        { "LowerArmR", DamageZone.Hands },
        { "LowerArmL", DamageZone.Hands },
        { "UpperFootL", DamageZone.Legs },
        { "UpperFootR", DamageZone.Legs },
        { "LowerFootL", DamageZone.Feet },
        { "LowerFootR", DamageZone.Feet }
    }.ToImmutableDictionary();

    public DamageBlockStats? CurrentDamageBlock { get; set; } = null;

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        if (attributes.KeyExists("damageModel"))
        {
            PlayerDamageModelJson stats = attributes["damageModel"].AsObject<PlayerDamageModelJson>();

            DamageModel = new(stats.Zones);
        }
    }

    public override void AfterInitialized(bool onFirstSpawn)
    {
        _colliders = entity.GetBehavior<CollidersEntityBehavior>();
        entity.GetBehavior<EntityBehaviorHealth>().onDamaged += OnReceiveDamage;
    }

    private CollidersEntityBehavior? _colliders;

    private float OnReceiveDamage(float damage, DamageSource damageSource)
    {
        (DamageZone damageZone, float multiplier) = DetermineHitZone(damageSource);

        Console.WriteLine($"Hit zone: {damageZone}"); // @DEBUG

        ApplyBlock(damageSource, damageZone, ref damage);

        ApplyArmorResists(damageSource, damageZone, ref damage);

        damage *= multiplier;

        return damage;
    }

    private (DamageZone zone, float multiplier) DetermineHitZone(DamageSource damageSource)
    {
        DamageZone damageZone;
        float multiplier;
        if (_colliders != null && damageSource is ILocationalDamage locationalDamageSource)
        {
            damageZone = CollidersToZones[_colliders.CollidersIds[locationalDamageSource.Collider]];
            multiplier = DamageModel.GetMultiplier(damageZone);
        }
        else if (damageSource is IDirectionalDamage directionalDamage)
        {
            (damageZone, multiplier) = DamageModel.GetZone(directionalDamage.Direction, directionalDamage.Target, directionalDamage.WeightMultiplier);
        }
        else if (damageSource.SourceEntity != null)
        {
            DirectionOffset direction = DirectionOffset.GetDirection(entity, damageSource.SourceEntity);

            (damageZone, multiplier) = DamageModel.GetZone(direction);
        }
        else
        {
            (damageZone, multiplier) = DamageModel.GetZone();
        }

        return (damageZone, multiplier);
    }
    private void ApplyBlock(DamageSource damageSource, DamageZone zone, ref float damage)
    {
        if (CurrentDamageBlock == null) return;
        if ((zone & CurrentDamageBlock.ZoneType) == 0) return;

        if (damageSource is IDirectionalDamage directionalDamage)
        {
            if (!CurrentDamageBlock.Directions.Check(directionalDamage.Direction)) return;
        }
        else if (damageSource.SourceEntity != null)
        {
            DirectionOffset offset = DirectionOffset.GetDirection(entity, damageSource.SourceEntity);

            if (!CurrentDamageBlock.Directions.Check(offset)) return;
        }

        damage = 0;

        CurrentDamageBlock.Callback.Invoke();
    }
    private void ApplyArmorResists(DamageSource damageSource, DamageZone zone, ref float damage)
    {
        if (!Resists.ContainsKey(zone)) return;
        
        if (damageSource is ITypedDamage typedDamage)
        {
            typedDamage.DamageTypeData = Resists[zone].ApplyResist(typedDamage.DamageTypeData, ref damage);
        }
        else
        {
            DamageData data = new(damageSource.Type, 0);
            _ = Resists[zone].ApplyResist(data, ref damage);
        }
    }
}
public sealed class PlayerDamageModel
{
    public readonly ImmutableArray<DamageZoneStats> DamageZones;

    public PlayerDamageModel(DamageZoneStatsJson[] zones)
    {
        DamageZones = zones.Select(zone => zone.ToStats()).Where(zone => zone.ZoneType != DamageZone.None).ToImmutableArray();
        _random = new(0.5f, 0.5f, EnumDistribution.UNIFORM);

        _weights = new();
        foreach (DamageZone zone in Enum.GetValues<DamageZone>())
        {
            _weights[zone] = 0;
        }
    }

    public (DamageZone zone, float damageMultiplier) GetZone(DirectionOffset? direction = null, DamageZone target = DamageZone.None, float multiplier = 1f)
    {
        IEnumerable<DamageZoneStats> zones = direction == null ? DamageZones : DamageZones.Where(zone => zone.Directions.Check(direction.Value));

        foreach ((DamageZone zone, _) in _weights)
        {
            _weights[zone] = 0;
        }

        float sum = 0;
        foreach (DamageZoneStats zone in zones)
        {
            float zoneMultiplier = (target | zone.ZoneType) != 0 ? multiplier : 1;
            sum += zone.Coverage * zoneMultiplier;
            _weights[zone.ZoneType] += zone.Coverage * zoneMultiplier;
        }

        foreach ((DamageZone zone, _) in _weights)
        {
            _weights[zone] /= sum;
        }

        float randomValue = _random.nextFloat();

        sum = 0;
        foreach ((DamageZone zone, float weight) in _weights)
        {
            sum += weight;
            if (sum >= randomValue)
            {
                return (zone, zones.Where(element => (element.ZoneType & zone) != 0).Select(element => element.DamageMultiplier).Average());
            }
        }

        return (DamageZone.None, 1.0f);
    }

    public float GetMultiplier(DamageZone zone)
    {
        return DamageZones.Where(element => (element.ZoneType & zone) != 0).Select(element => element.DamageMultiplier).Average();
    }

    private readonly NatFloat _random;
    private readonly Dictionary<DamageZone, float> _weights;
}

public sealed class PlayerDamageModelJson
{
    public DamageZoneStatsJson[] Zones { get; set; } = Array.Empty<DamageZoneStatsJson>();
}

public interface IDirectionalDamage
{
    DirectionOffset Direction { get; }
    DamageZone Target { get; }
    float WeightMultiplier { get; }
}

public sealed class DamageZoneStatsJson
{
    public string Zone { get; set; } = "None";
    public float Coverage { get; set; } = 0;
    public float Top { get; set; } = 0;
    public float Bottom { get; set; } = 0;
    public float Left { get; set; } = 0;
    public float Right { get; set; } = 0;
    public float DamageMultiplier { get; set; } = 1;

    public DamageZoneStats ToStats() => new(Enum.Parse<DamageZone>(Zone), Coverage, DirectionConstrain.FromDegrees(Top, Bottom, Right, Left), DamageMultiplier);
}

public readonly struct DamageZoneStats
{
    public readonly DamageZone ZoneType;
    public readonly float Coverage;
    public readonly DirectionConstrain Directions;
    public readonly float DamageMultiplier;

    public DamageZoneStats(DamageZone type, float coverage, DirectionConstrain directions, float damageMultiplier)
    {
        ZoneType = type;
        Coverage = coverage;
        Directions = directions;
        DamageMultiplier = damageMultiplier;
    }
}

public sealed class DamageBlockStats
{
    public readonly DamageZone ZoneType;
    public readonly DirectionConstrain Directions;
    public readonly Action Callback;

    public DamageBlockStats(DamageZone type, DirectionConstrain directions, Action callback)
    {
        ZoneType = type;
        Directions = directions;
        Callback = callback;
    }
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public sealed class DamageBlockPacket
{
    public int Zones { get; set; }
    public float[] Directions { get; set; } = Array.Empty<float>();
    public bool MainHand { get; set; }

    public DamageBlockStats ToBlockStats(Action callback)
    {
        return new((DamageZone)Zones, DirectionConstrain.FromArray(Directions), callback);
    }
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public sealed class DamageStopBlockPacket
{
    public bool MainHand { get; set; }
}

public sealed class DamageBlockJson
{
    public string[] Zones { get; set; } = Array.Empty<string>();
    public float[] Directions { get; set; } = Array.Empty<float>();

    public DamageBlockPacket ToPacket()
    {
        return new()
        {
            Zones = (int)Zones.Select(Enum.Parse<DamageZone>).Aggregate((first, second) => first | second),
            Directions = Directions
        };
    }
}

public sealed class MeleeBlockSystemClient : MeleeSystem
{
    public MeleeBlockSystemClient(ICoreClientAPI api)
    {
        _clientChannel = api.Network.RegisterChannel(NetworkChannelId)
            .RegisterMessageType<DamageBlockPacket>()
            .RegisterMessageType<DamageStopBlockPacket>();
    }

    public void StartBlock(DamageBlockJson block, bool mainHand)
    {
        DamageBlockPacket packet = block.ToPacket();
        packet.MainHand = mainHand;
        _clientChannel.SendPacket(packet);
    }
    public void StopBlock(bool mainHand)
    {
        _clientChannel.SendPacket(new DamageStopBlockPacket() { MainHand = mainHand });
    }

    private readonly IClientNetworkChannel _clientChannel;
}

public interface IHasServerBlockCallback
{
    public void BlockCallback(IServerPlayer player, ItemSlot slot, bool mainHand);
}

public sealed class MeleeBlockSystemServer : MeleeSystem
{
    public MeleeBlockSystemServer(ICoreServerAPI api)
    {
        _api = api;
        api.Network.RegisterChannel(NetworkChannelId)
            .RegisterMessageType<DamageBlockPacket>()
            .RegisterMessageType<DamageStopBlockPacket>()
            .SetMessageHandler<DamageBlockPacket>(HandlePacket)
            .SetMessageHandler<DamageStopBlockPacket>(HandlePacket);
    }

    private readonly ICoreServerAPI _api;

    private void HandlePacket(IServerPlayer player, DamageBlockPacket packet)
    {
        player.Entity.GetBehavior<PlayerDamageModelBehavior>().CurrentDamageBlock = packet.ToBlockStats(() => BlockCallback(player, packet.MainHand));
    }

    private void HandlePacket(IServerPlayer player, DamageStopBlockPacket packet)
    {
        player.Entity.GetBehavior<PlayerDamageModelBehavior>().CurrentDamageBlock = null;
    }

    private static void BlockCallback(IServerPlayer player, bool mainHand)
    {
        ItemSlot slot = mainHand ? player.Entity.RightHandItemSlot : player.Entity.LeftHandItemSlot;

        if (slot?.Itemstack?.Item is not IHasServerBlockCallback item) return;

        item.BlockCallback(player, slot, mainHand);
    }
}