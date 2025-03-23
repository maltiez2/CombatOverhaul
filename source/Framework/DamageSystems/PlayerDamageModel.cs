using CombatOverhaul.Armor;
using CombatOverhaul.Colliders;
using CombatOverhaul.MeleeSystems;
using CombatOverhaul.Utils;
using Newtonsoft.Json.Linq;
using ProtoBuf;
using System.Diagnostics;
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
        _printIntoChat = entity.Api.ModLoader.GetModSystem<CombatOverhaulSystem>().Settings.PrintPlayerBeingHit;
    }

    public event OnPlayerReceiveDamageDelegate? OnReceiveDamage;

    public override string PropertyName() => "PlayerDamageModel";

    public PlayerDamageModel DamageModel { get; private set; } = new(Array.Empty<DamageZoneStatsJson>());
    public readonly Dictionary<string, PlayerBodyPart> CollidersToBodyParts = new()
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
    };
    public readonly Dictionary<PlayerBodyPart, DamageZone> BodyPartsToZones = new()
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

    };
    public readonly List<EnumDamageType> DamageTypesToProcess = new()
    {
        EnumDamageType.PiercingAttack,
        EnumDamageType.SlashingAttack,
        EnumDamageType.BluntAttack
    };
    public TimeSpan SecondDefaultChanceCooldown { get; set; }
    public TimeSpan SecondChanceCooldown => SecondDefaultChanceCooldown * entity.Stats.GetBlended("secondChanceCooldown");
    public bool SecondChanceAvailable { get; set; }

    public DamageBlockStats? CurrentDamageBlock { get; set; } = null;

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        if (attributes.KeyExists("damageModel"))
        {
            PlayerDamageModelJson stats = attributes["damageModel"].AsObject<PlayerDamageModelJson>();

            DamageModel = new(stats.Zones);
        }

        if (attributes.KeyExists("bodyParts"))
        {
            CollidersToBodyParts.Clear();
            foreach (JToken token in attributes["bodyParts"].Token)
            {
                if (token is not JProperty property) continue;

                JsonObject value = new(property.Value);

                CollidersToBodyParts.Add(property.Name, Enum.Parse<PlayerBodyPart>(value.AsString("Torso")));
            }
        }

        SecondDefaultChanceCooldown = TimeSpan.FromSeconds(attributes["secondChanceCooldownSec"].AsFloat(60 * 5));
        SecondChanceAvailable = attributes["secondChanceAvailable"].AsBool(true);
    }

    public override void AfterInitialized(bool onFirstSpawn)
    {
        _colliders = entity.GetBehavior<CollidersEntityBehavior>();
        entity.GetBehavior<EntityBehaviorHealth>().onDamaged += OnReceiveDamageHandler;
    }

    public override void OnGameTick(float deltaTime)
    {
        float secondChanceCooldown = entity.WatchedAttributes.GetFloat("secondChanceCooldown", 0);
        secondChanceCooldown = Math.Clamp(secondChanceCooldown - deltaTime, 0, secondChanceCooldown);
        entity.WatchedAttributes.SetFloat("secondChanceCooldown", secondChanceCooldown);
    }

    private readonly bool _printIntoChat = false;
    private CollidersEntityBehavior? _colliders;
    private float _healthAfterSecondChance = 1;

    private float OnReceiveDamageHandler(float damage, DamageSource damageSource)
    {
        if (!DamageTypesToProcess.Contains(damageSource.Type)) return damage;

        (PlayerBodyPart detailedDamageZone, float multiplier) = DetermineHitZone(damageSource);

        DamageZone damageZone = BodyPartsToZones[detailedDamageZone];

        ApplyBlock(damageSource, detailedDamageZone, ref damage, out string blockDamageLogMessage);
        PrintToDamageLog(blockDamageLogMessage);

        ApplyArmorResists(damageSource, damageZone, ref damage, out string armorDamageLogMessage, out EnumDamageType damageType);
        PrintToDamageLog(armorDamageLogMessage);

        damage *= multiplier;

        if (SecondChanceAvailable) ApplySecondChance(ref damage);

        OnReceiveDamage?.Invoke(ref damage, damageSource, detailedDamageZone);

        if (damage != 0)
        {
            string damageLogMessage = Lang.Get("combatoverhaul:damagelog-received-damage", $"{damage:F1}", Lang.Get($"combatoverhaul:detailed-damage-zone-{detailedDamageZone}"), Lang.Get($"combatoverhaul:damage-type-{damageType}"));
            PrintToDamageLog(damageLogMessage);
        }

        return damage;
    }
    private void PrintToDamageLog(string message)
    {
        if (_printIntoChat && message != "") ((entity as EntityPlayer)?.Player as IServerPlayer)?.SendMessage(GlobalConstants.DamageLogChatGroup, message, EnumChatType.Notification);
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
        else if (damageSource.SourceEntity != null && damageSource.SourceEntity.EntityId != entity.EntityId)
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

        if (!CurrentDamageBlock.CanBlockProjectiles && IsCausedByProjectile(damageSource))
        {
            damageLogMessage = Lang.Get("combatoverhaul:damagelog-missed-block-projectile");
            return;
        }

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

        float damageTier = damageSource.DamageTier;
        float initialDamage = damage;
        EnumDamageType damageType = damageSource.Type;

        if (CurrentDamageBlock.BlockTier != null)
        {
            if (!CurrentDamageBlock.BlockTier.ContainsKey(damageType))
            {
                damageLogMessage = Lang.Get("combatoverhaul:damagelog-missed-block-damageType", Lang.Get($"damage-type-{damageType}"));
                return;
            }

            float blockTier = CurrentDamageBlock.BlockTier[damageType];
            if (blockTier < damageTier)
            {
                ApplyBlockResists(blockTier, damageTier, ref damage);
                damageSource.DamageTier = (int)(damageTier - blockTier);
                damageLogMessage = Lang.Get("combatoverhaul:damagelog-partial-block", Lang.Get($"combatoverhaul:detailed-damage-zone-{zone}"), $"{initialDamage - damage:F1}");
            }
            else
            {
                damageLogMessage = Lang.Get("combatoverhaul:damagelog-success-block", Lang.Get($"combatoverhaul:detailed-damage-zone-{zone}"), $"{damage:F1}");
                damage = 0;
            }
        }
        else
        {
            damage = 0;
        }

        CurrentDamageBlock.Callback.Invoke(initialDamage - damage);

        if (CurrentDamageBlock.Sound != null) entity.Api.World.PlaySoundAt(new(CurrentDamageBlock.Sound), entity);
    }
    private void ApplyArmorResists(DamageSource damageSource, DamageZone zone, ref float damage, out string damageLogMessage, out EnumDamageType damageType)
    {
        damageLogMessage = "";
        damageType = damageSource.Type;

        if ((entity as EntityPlayer)?.Player.InventoryManager.GetOwnInventory(GlobalConstants.characterInvClassName) is not ArmorInventory inventory) return;

        if (zone == DamageZone.None) return;

        IEnumerable<ArmorSlot> slots = inventory.GetNotEmptyZoneSlots(zone);

        if (!slots.Any()) return;

        DamageResistData resists = DamageResistData.Combine(slots
            .Where(slot => slot?.Itemstack?.Item != null)
            .Where(slot => slot?.Itemstack?.Item.GetRemainingDurability(slot.Itemstack) > 0)
            .Select(slot => slot.Resists));

        float previousDamage = damage;
        int durabilityDamage = 0;

        _ = resists.ApplyResist(new(damageSource.Type, damageSource.DamageTier), ref damage, out durabilityDamage);

        durabilityDamage = GameMath.Clamp(durabilityDamage, 1, durabilityDamage);
        int durabilityDamagePerItem = GameMath.Clamp(durabilityDamage / slots.Count(), 0, durabilityDamage);

        foreach (ArmorSlot slot in slots)
        {
            slot.Itemstack.Item.DamageItem(entity.Api.World, entity, slot, durabilityDamagePerItem);
            slot.MarkDirty();
        }

        if (previousDamage - damage > 0)
        {
            damageLogMessage = Lang.Get("combatoverhaul:damagelog-armor-damage-negation", $"{previousDamage - damage:F1}", Lang.Get($"combatoverhaul:damage-zone-{zone}"), durabilityDamage, Lang.Get($"combatoverhaul:damage-type-{damageType}"), damageSource.DamageTier);
        }
    }
    private void ApplyBlockResists(float blockTier, float damageTier, ref float damage)
    {
        damage *= 1 - MathF.Exp((blockTier - damageTier) / 2f);
    }
    private void ApplySecondChance(ref float damage)
    {
        float currentHealth = entity.GetBehavior<EntityBehaviorHealth>().Health;

        if (currentHealth > damage) return;

        float secondChanceCooldown = entity.WatchedAttributes.GetFloat("secondChanceCooldown", 0);
        if (secondChanceCooldown > 0)
        {
            PrintToDamageLog(Lang.Get("combatoverhaul:damagelog-second-chance-cooldown", (int)secondChanceCooldown));
            return;
        }

        entity.WatchedAttributes.SetFloat("secondChanceCooldown", (float)SecondChanceCooldown.TotalSeconds);
        damage = currentHealth - _healthAfterSecondChance;

        PrintToDamageLog(Lang.Get("combatoverhaul:damagelog-second-chance"));
    }
    private bool IsCausedByProjectile(DamageSource damageSource)
    {
        Entity? sourceEntity = damageSource.SourceEntity;
        Entity? causeEntity = damageSource.CauseEntity;

        return sourceEntity != null && causeEntity != null && sourceEntity != causeEntity;
    }
}
public sealed class PlayerDamageModel
{
    public readonly DamageZoneStats[] DamageZones;

    public PlayerDamageModel(DamageZoneStatsJson[] zones)
    {
        DamageZones = zones.Select(zone => zone.ToStats()).Where(zone => zone.ZoneType != PlayerBodyPart.None).ToArray();
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

        Trace.WriteLine(direction);
        Trace.WriteLine(zones.Select(zone => zone.ZoneType.ToString()).Aggregate((zone1, zone2) => $"{zone1}, {zone2}"));

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
    public readonly Action<float> Callback;
    public readonly string? Sound;
    public readonly Dictionary<EnumDamageType, float>? BlockTier;
    public readonly bool CanBlockProjectiles;

    public DamageBlockStats(PlayerBodyPart type, DirectionConstrain directions, Action<float> callback, string? sound, Dictionary<EnumDamageType, float>? blockTier, bool canBlockProjectiles)
    {
        ZoneType = type;
        Directions = directions;
        Callback = callback;
        Sound = sound;
        BlockTier = blockTier;
        CanBlockProjectiles = canBlockProjectiles;
    }
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public sealed class DamageBlockPacket
{
    public int Zones { get; set; }
    public float[] Directions { get; set; } = Array.Empty<float>();
    public bool MainHand { get; set; }
    public string? Sound { get; set; } = null;
    public Dictionary<EnumDamageType, float>? BlockTier { get; set; }
    public bool CanBlockProjectiles { get; set; }

    public DamageBlockStats ToBlockStats(Action<float> callback)
    {
        return new((PlayerBodyPart)Zones, DirectionConstrain.FromArray(Directions), callback, Sound, BlockTier, CanBlockProjectiles);
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
    public Dictionary<string, float>? BlockTier { get; set; }
    public bool CanBlockProjectiles { get; set; } = true;

    public DamageBlockPacket ToPacket()
    {
        return new()
        {
            Zones = (int)Zones.Select(Enum.Parse<PlayerBodyPart>).Aggregate((first, second) => first | second),
            Directions = Directions,
            Sound = Sound,
            BlockTier = BlockTier?.ToDictionary(entry => Enum.Parse<EnumDamageType>(entry.Key), entry => entry.Value),
            CanBlockProjectiles = CanBlockProjectiles
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
    public void BlockCallback(IServerPlayer player, ItemSlot slot, bool mainHand, float damageBlocked);
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
        player.Entity.GetBehavior<PlayerDamageModelBehavior>().CurrentDamageBlock = packet.ToBlockStats(damageBlocked => BlockCallback(player, packet.MainHand, damageBlocked));
    }

    private void HandlePacket(IServerPlayer player, DamageStopBlockPacket packet)
    {
        player.Entity.GetBehavior<PlayerDamageModelBehavior>().CurrentDamageBlock = null;
    }

    private static void BlockCallback(IServerPlayer player, bool mainHand, float damageBlocked)
    {
        ItemSlot slot = mainHand ? player.Entity.RightHandItemSlot : player.Entity.LeftHandItemSlot;

        if (slot?.Itemstack?.Item is not IHasServerBlockCallback item) return;

        item.BlockCallback(player, slot, mainHand, damageBlocked);
    }
}