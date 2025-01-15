using CombatOverhaul.Colliders;
using CombatOverhaul.Utils;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace CombatOverhaul.DamageSystems;

public sealed class EntityDamageModelJson
{
    public float TorsoDamageMultiplier { get; set; } = 1.0f;
    public float LimbsDamageMultiplier { get; set; } = 0.5f;
    public float HeadDamageMultiplier { get; set; } = 1.25f;
    public float CriticalDamageMultiplier { get; set; } = 2.0f;
    public float ResistantDamageMultiplier { get; set; } = 0.0f;
    public Dictionary<string, float> DefaultResists { get; set; } = new();
    public Dictionary<string, Dictionary<string, float>> ResistsForColliders { get; set; } = new();
    public Dictionary<string, SoundEffectData> HitSounds { get; set; } = new()
    {
        //{"Head", new() { Code = "game:sounds/player/projectilehit"}  },
        //{"Critical", new() { Code = "game:sounds/player/projectilehit"}  },
        {"Resistant", new() { Code = "game:sounds/held/shieldblock-wood-light"}  }
    };
}

public sealed class SoundEffectData
{
    public string Code { get; set; } = "";
    public bool RandomizePitch { get; set; } = false;
    public float Range { get; set; } = 32;
    public float Volume { get; set; } = 1;
}

public interface IEntityDamageModel
{
    event OnEntityReceiveDamageDelegate? OnReceiveDamage;
}

public delegate void OnEntityReceiveDamageDelegate(ref float damage, DamageSource damageSource, ColliderTypes damageZone, string? collider);

public sealed class EntityDamageModelBehavior : EntityBehavior, IEntityDamageModel
{
    public EntityDamageModelBehavior(Entity entity) : base(entity)
    {
    }

    public event OnEntityReceiveDamageDelegate? OnReceiveDamage;

    public override string PropertyName() => "EntityDamageModel";
    public Dictionary<ColliderTypes, float> DamageMultipliers { get; private set; } = new Dictionary<ColliderTypes, float>()
    {
        { ColliderTypes.Torso, 1.0f },
        { ColliderTypes.Arm, 1.0f },
        { ColliderTypes.Leg, 1.0f },
        { ColliderTypes.Head, 1.0f },
        { ColliderTypes.Critical, 1.0f },
        { ColliderTypes.Resistant, 0.0f }
    };
    public DamageResistData Resists { get; set; } = new();
    public Dictionary<ColliderTypes, DamageResistData> ResistsForColliders { get; private set; } = new Dictionary<ColliderTypes, DamageResistData>();
    public Dictionary<ColliderTypes, SoundEffectData> HitSounds { get; private set; } = new Dictionary<ColliderTypes, SoundEffectData>();

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        if (attributes.KeyExists("damageModel"))
        {
            EntityDamageModelJson stats = attributes["damageModel"].AsObject<EntityDamageModelJson>();

            Resists = new(stats.DefaultResists.ToDictionary(entry => Enum.Parse<EnumDamageType>(entry.Key), entry => entry.Value));

            ResistsForColliders = stats.ResistsForColliders
                .ToDictionary(entry => Enum.Parse<ColliderTypes>(entry.Key), entry => new DamageResistData(entry.Value.ToDictionary(entry => Enum.Parse<EnumDamageType>(entry.Key), entry => entry.Value)));

            DamageMultipliers = new Dictionary<ColliderTypes, float>()
            {
                { ColliderTypes.Torso, stats.TorsoDamageMultiplier },
                { ColliderTypes.Arm, stats.LimbsDamageMultiplier },
                { ColliderTypes.Leg, stats.LimbsDamageMultiplier },
                { ColliderTypes.Head, stats.HeadDamageMultiplier },
                { ColliderTypes.Critical, stats.CriticalDamageMultiplier },
                { ColliderTypes.Resistant, stats.ResistantDamageMultiplier }
            };

            HitSounds = stats.HitSounds.ToDictionary(entry => Enum.Parse<ColliderTypes>(entry.Key), entry => entry.Value);
        }
    }
    public override void GetInfoText(StringBuilder infotext)
    {
        if (!Resists.Resists.Values.Any(x => x > 0)) return;

        infotext.AppendLine(Lang.Get($"combatoverhaul:damage-protection-info"));
        foreach ((EnumDamageType type, float value) in Resists.Resists)
        {
            if (value <= 0) continue;

            string damageType = Lang.Get($"combatoverhaul:damage-type-{type}");
            infotext.AppendLine($"  {damageType}: {value}");
        }
    }
    public override void AfterInitialized(bool onFirstSpawn)
    {
        _colliders = entity.GetBehavior<CollidersEntityBehavior>();
        EntityBehaviorHealth? healthBehavior = entity.GetBehavior<EntityBehaviorHealth>();
        if (healthBehavior != null) healthBehavior.onDamaged += OnReceiveDamageHandler;

        if (_colliders == null)
        {
            LoggerUtil.Warn(entity.Api, this, $"Entity '{entity.Code}' does not have colliders behavior");
        }
    }

    private CollidersEntityBehavior? _colliders;

    private float OnReceiveDamageHandler(float damage, DamageSource damageSource)
    {   
        ColliderTypes colliderType = ColliderTypes.Torso;
        string? collider = null;

        if (_colliders != null && damageSource is ILocationalDamage locationalDamageSource)
        {
            if (_colliders.CollidersTypes.ContainsKey(locationalDamageSource.Collider)) colliderType = _colliders.CollidersTypes[locationalDamageSource.Collider];
            collider = locationalDamageSource.Collider;
            float multiplier = DamageMultipliers[colliderType];
            damage *= multiplier;
        }

        if (damageSource is ITypedDamage typedDamage)
        {
            if (ResistsForColliders.ContainsKey(colliderType))
            {
                typedDamage.DamageTypeData = ResistsForColliders[colliderType].ApplyNotPlayerResist(typedDamage.DamageTypeData, ref damage);
            }
            else
            {
                typedDamage.DamageTypeData = Resists.ApplyNotPlayerResist(typedDamage.DamageTypeData, ref damage);
            }
        }
        else
        {
            DamageData damageData = new(damageSource.Type, damageSource.DamageTier);
            Resists.ApplyNotPlayerResist(damageData, ref damage);
        }

        if (HitSounds.TryGetValue(colliderType, out SoundEffectData? value))
        {
            entity.Api.World.PlaySoundAt(new AssetLocation(value.Code), entity, randomizePitch: value.RandomizePitch, range: value.Range, volume: value.Volume);
        }

        OnReceiveDamage?.Invoke(ref damage, damageSource, colliderType, collider);

        return damage;
    }
}
