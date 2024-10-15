using CombatOverhaul.Armor;
using CombatOverhaul.Colliders;
using CombatOverhaul.MeleeSystems;
using CombatOverhaul.Utils;
using ProtoBuf;
using System.Collections.Immutable;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
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

[Flags]
public enum PlayerBodyPart
{
    None = 0,
    Head = 1,
    Face = 2,
    Neck = 4,
    Torso = 8,
    LeftArm = 16,
    RightArm = 32,
    LeftHand = 64,
    RightHand = 128,
    LeftLeg = 256,
    RightLeg = 512,
    LeftFoot = 1024,
    RightFoot = 2048
}

public delegate void OnPlayerReceiveDamageDelegate(ref float damage, DamageSource damageSource, PlayerBodyPart bodyPart);

public sealed class PlayerDamageModelBehavior : EntityBehavior
{
    public PlayerDamageModelBehavior(Entity entity) : base(entity)
    {
    }

    public event OnPlayerReceiveDamageDelegate? OnReceiveDamage;

    public override string PropertyName() => "PlayerDamageModel";

    public PlayerDamageModel DamageModel { get; private set; } = new(Array.Empty<DamageZoneStatsJson>());
    public readonly ImmutableDictionary<string, PlayerBodyPart> CollidersToBodyParts = new Dictionary<string, PlayerBodyPart>()
    {
        { "LowerTorso", PlayerBodyPart.Torso },
        { "UpperTorso", PlayerBodyPart.Torso },
        { "Head", PlayerBodyPart.Head },
        { "Neck", PlayerBodyPart.Neck },
        { "UpperArmR", PlayerBodyPart.RightArm },
        { "UpperArmL", PlayerBodyPart.LeftArm },
        { "LowerArmR", PlayerBodyPart.RightHand },
        { "LowerArmL", PlayerBodyPart.LeftHand },
        { "UpperFootL", PlayerBodyPart.LeftLeg },
        { "UpperFootR", PlayerBodyPart.RightLeg },
        { "LowerFootL", PlayerBodyPart.LeftFoot },
        { "LowerFootR", PlayerBodyPart.RightFoot }
    }.ToImmutableDictionary();
    public readonly ImmutableDictionary<PlayerBodyPart, DamageZone> BodyPartsToZones = new Dictionary<PlayerBodyPart, DamageZone>()
    {
        { PlayerBodyPart.None, DamageZone.None },
        { PlayerBodyPart.Head, DamageZone.Head },
        { PlayerBodyPart.Face, DamageZone.Face },
        { PlayerBodyPart.Neck, DamageZone.Neck },
        { PlayerBodyPart.Torso, DamageZone.Torso },
        { PlayerBodyPart.LeftArm, DamageZone.Arms },
        { PlayerBodyPart.RightArm, DamageZone.Arms },
        { PlayerBodyPart.LeftHand, DamageZone.Hands },
        { PlayerBodyPart.RightHand, DamageZone.Hands },
        { PlayerBodyPart.LeftLeg, DamageZone.Legs },
        { PlayerBodyPart.RightLeg, DamageZone.Legs },
        { PlayerBodyPart.LeftFoot, DamageZone.Feet },
        { PlayerBodyPart.RightFoot, DamageZone.Feet }

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
        entity.GetBehavior<EntityBehaviorHealth>().onDamaged += OnReceiveDamageHandler;
    }

    private CollidersEntityBehavior? _colliders;

    private float OnReceiveDamageHandler(float damage, DamageSource damageSource)
    {
        if (damageSource.Type == EnumDamageType.Heal) return damage;

        (PlayerBodyPart detailedDamageZone, float multiplier) = DetermineHitZone(damageSource);

        DamageZone damageZone = BodyPartsToZones[detailedDamageZone];

        ApplyBlock(damageSource, detailedDamageZone, ref damage, out string blockDamageLogMessage);
        PrintToDamageLog(blockDamageLogMessage);

        ApplyArmorResists(damageSource, damageZone, ref damage, out string armorDamageLogMessage);
        PrintToDamageLog(armorDamageLogMessage);

        damage *= multiplier;

        OnReceiveDamage?.Invoke(ref damage, damageSource, detailedDamageZone);

        if (damage != 0)
        {
            string damageLogMessage = Lang.Get("combatoverhaul:damagelog-received-damage", $"{damage:F1}", Lang.Get($"combatoverhaul:detailed-damage-zone-{detailedDamageZone}"));
            PrintToDamageLog(damageLogMessage);
        }

        return damage;
    }
    private void PrintToDamageLog(string message)
    {
        if (message != "") ((entity as EntityPlayer)?.Player as IServerPlayer)?.SendMessage(GlobalConstants.DamageLogChatGroup, message, EnumChatType.Notification);
    }

    private (PlayerBodyPart zone, float multiplier) DetermineHitZone(DamageSource damageSource)
    {
        PlayerBodyPart damageZone;
        float multiplier;
        if (_colliders != null && damageSource is ILocationalDamage locationalDamageSource && locationalDamageSource.Collider != "")
        {
            damageZone = CollidersToBodyParts[locationalDamageSource.Collider];
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
    private void ApplyBlock(DamageSource damageSource, PlayerBodyPart zone, ref float damage, out string damageLogMessage)
    {
        damageLogMessage = "";

        if (CurrentDamageBlock == null) return;

        if ((zone & CurrentDamageBlock.ZoneType) == 0)
        {
            damageLogMessage = Lang.Get("combatoverhaul:damagelog-missed-block-zone", Lang.Get($"combatoverhaul:detailed-damage-zone-{zone}"));
            return;
        }

        if (damageSource is IDirectionalDamage directionalDamage)
        {
            if (!CurrentDamageBlock.Directions.Check(directionalDamage.Direction))
            {
                damageLogMessage = Lang.Get("combatoverhaul:damagelog-missed-block-direction", directionalDamage.Direction);
                return;
            }
        }
        else if (damageSource.SourceEntity != null)
        {
            DirectionOffset offset = DirectionOffset.GetDirectionWithRespectToCamera(entity, damageSource.SourceEntity);

            if (!CurrentDamageBlock.Directions.Check(offset))
            {
                damageLogMessage = Lang.Get("combatoverhaul:damagelog-missed-block-direction", offset);
                return;
            }
        }

        damage = 0;

        damageLogMessage = Lang.Get("combatoverhaul:damagelog-success-block", Lang.Get($"combatoverhaul:detailed-damage-zone-{zone}"));

        CurrentDamageBlock.Callback.Invoke();

        if (CurrentDamageBlock.Sound != null) entity.Api.World.PlaySoundAt(new(CurrentDamageBlock.Sound), entity);
    }
    private void ApplyArmorResists(DamageSource damageSource, DamageZone zone, ref float damage, out string damageLogMessage)
    {
        damageLogMessage = "";

        if ((entity as EntityPlayer)?.Player.InventoryManager.GetOwnInventory(GlobalConstants.characterInvClassName) is not ArmorInventory inventory) return;

        if (zone == DamageZone.None) return;

        IEnumerable<ArmorSlot> slots = inventory.GetNotEmptyZoneSlots(zone);

        if (!slots.Any()) return;

        DamageResistData resists = DamageResistData.Combine(slots.Select(slot => slot.Resists));

        float previousDamage = damage;
        int durabilityDamage = 0;

        if (damageSource is ITypedDamage typedDamage)
        {
            typedDamage.DamageTypeData = resists.ApplyResist(typedDamage.DamageTypeData, ref damage, out durabilityDamage);
        }
        else
        {
            _ = resists.ApplyResist(new(damageSource.Type, damageSource.DamageTier), ref damage, out durabilityDamage);
        }

        durabilityDamage = GameMath.Clamp(durabilityDamage / slots.Count(), 0, durabilityDamage);

        foreach (ArmorSlot slot in slots)
        {
            slot.Itemstack.Item.DamageItem(entity.Api.World, entity, slot, durabilityDamage);
            slot.MarkDirty();
        }

        if (previousDamage - damage > 0)
        {
            damageLogMessage = Lang.Get("combatoverhaul:damagelog-armor-damage-negation", $"{previousDamage - damage:F1}", Lang.Get($"combatoverhaul:damage-zone-{zone}"), durabilityDamage);
        }
    }
}
public sealed class PlayerDamageModel
{
    public readonly ImmutableArray<DamageZoneStats> DamageZones;

    public PlayerDamageModel(DamageZoneStatsJson[] zones)
    {
        DamageZones = zones.Select(zone => zone.ToStats()).Where(zone => zone.ZoneType != PlayerBodyPart.None).ToImmutableArray();
        _random = new(0.5f, 0.5f, EnumDistribution.UNIFORM);

        _weights = new();
        foreach (PlayerBodyPart zone in Enum.GetValues<PlayerBodyPart>())
        {
            _weights[zone] = 0;
        }
    }

    public (PlayerBodyPart zone, float damageMultiplier) GetZone(DirectionOffset? direction = null, PlayerBodyPart target = PlayerBodyPart.None, float multiplier = 1f)
    {
        IEnumerable<DamageZoneStats> zones = direction == null ? DamageZones : DamageZones.Where(zone => zone.Directions.Check(direction.Value));

        foreach ((PlayerBodyPart zone, _) in _weights)
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

        foreach ((PlayerBodyPart zone, _) in _weights)
        {
            _weights[zone] /= sum;
        }

        float randomValue = _random.nextFloat();

        sum = 0;
        foreach ((PlayerBodyPart zone, float weight) in _weights)
        {
            sum += weight;
            if (sum >= randomValue)
            {
                return (zone, zones.Where(element => (element.ZoneType & zone) != 0).Select(element => element.DamageMultiplier).Average());
            }
        }

        return (PlayerBodyPart.None, 1.0f);
    }

    public float GetMultiplier(PlayerBodyPart zone)
    {
        return DamageZones.Where(element => (element.ZoneType & zone) != 0).Select(element => element.DamageMultiplier).Average();
    }

    private readonly NatFloat _random;
    private readonly Dictionary<PlayerBodyPart, float> _weights;
}

public sealed class PlayerDamageModelJson
{
    public DamageZoneStatsJson[] Zones { get; set; } = Array.Empty<DamageZoneStatsJson>();
}

public interface IDirectionalDamage
{
    DirectionOffset Direction { get; }
    PlayerBodyPart Target { get; }
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

    public DamageZoneStats ToStats() => new(Enum.Parse<PlayerBodyPart>(Zone), Coverage, DirectionConstrain.FromDegrees(Top, Bottom, Right, Left), DamageMultiplier);
}

public readonly struct DamageZoneStats
{
    public readonly PlayerBodyPart ZoneType;
    public readonly float Coverage;
    public readonly DirectionConstrain Directions;
    public readonly float DamageMultiplier;

    public DamageZoneStats(PlayerBodyPart type, float coverage, DirectionConstrain directions, float damageMultiplier)
    {
        ZoneType = type;
        Coverage = coverage;
        Directions = directions;
        DamageMultiplier = damageMultiplier;
    }
}

public sealed class DamageBlockStats
{
    public readonly PlayerBodyPart ZoneType;
    public readonly DirectionConstrain Directions;
    public readonly Action Callback;
    public readonly string? Sound;

    public DamageBlockStats(PlayerBodyPart type, DirectionConstrain directions, Action callback, string? sound)
    {
        ZoneType = type;
        Directions = directions;
        Callback = callback;
        Sound = sound;
    }
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public sealed class DamageBlockPacket
{
    public int Zones { get; set; }
    public float[] Directions { get; set; } = Array.Empty<float>();
    public bool MainHand { get; set; }
    public string? Sound { get; set; } = null;

    public DamageBlockStats ToBlockStats(Action callback)
    {
        return new((PlayerBodyPart)Zones, DirectionConstrain.FromArray(Directions), callback, Sound);
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
    public string? Sound { get; set; } = null;

    public DamageBlockPacket ToPacket()
    {
        return new()
        {
            Zones = (int)Zones.Select(Enum.Parse<PlayerBodyPart>).Aggregate((first, second) => first | second),
            Directions = Directions,
            Sound = Sound
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