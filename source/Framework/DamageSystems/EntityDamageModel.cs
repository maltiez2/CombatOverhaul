using CombatOverhaul.Colliders;
using System.Collections.Immutable;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace CombatOverhaul.DamageSystems;

public sealed class EntityDamageModelJson
{
    public float TorsoDamageMultiplier { get; set; } = 1.0f;
    public float LimbsDamageMultiplier { get; set; } = 0.5f;
    public float HeadDamageMultiplier { get; set; } = 1.25f;
    public float CriticalDamageMultiplier { get; set; } = 2.0f;
    public Dictionary<string, float> DefaultResists { get; set; } = new();
    public Dictionary<string, Dictionary<string, float>> ResistsForColliders { get; set; } = new();
}

public sealed class EntityDamageModelBehavior : EntityBehavior
{
    public EntityDamageModelBehavior(Entity entity) : base(entity)
    {
    }

    public override string PropertyName() => "EntityDamageModel";
    public ImmutableDictionary<ColliderTypes, float> DamageMultipliers { get; private set; } = new Dictionary<ColliderTypes, float>()
    {
        { ColliderTypes.Torso, 1.0f },
        { ColliderTypes.Arm, 1.0f },
        { ColliderTypes.Leg, 1.0f },
        { ColliderTypes.Head, 1.0f },
        { ColliderTypes.Critical, 1.0f }
    }.ToImmutableDictionary();
    public DamageResistData Resists { get; set; } = new();
    public ImmutableDictionary<string, DamageResistData> ResistsForColliders { get; private set; } = new Dictionary<string, DamageResistData>().ToImmutableDictionary();

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {

        if (attributes.KeyExists("damageModel"))
        {
            EntityDamageModelJson stats = attributes["damageModel"].AsObject<EntityDamageModelJson>();

            Resists = new(stats.DefaultResists.ToDictionary(entry => Enum.Parse<EnumDamageType>(entry.Key), entry => entry.Value));

            ResistsForColliders = stats.ResistsForColliders
                .ToDictionary(entry => entry.Key, entry => new DamageResistData(entry.Value.ToDictionary(entry => Enum.Parse<EnumDamageType>(entry.Key), entry => entry.Value)))
                .ToImmutableDictionary();

            DamageMultipliers = new Dictionary<ColliderTypes, float>()
            {
                { ColliderTypes.Torso, stats.TorsoDamageMultiplier },
                { ColliderTypes.Arm, stats.LimbsDamageMultiplier },
                { ColliderTypes.Leg, stats.LimbsDamageMultiplier },
                { ColliderTypes.Head, stats.HeadDamageMultiplier },
                { ColliderTypes.Critical, stats.CriticalDamageMultiplier }
            }.ToImmutableDictionary();
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
        if (_colliders != null && damageSource is ILocationalDamage locationalDamageSource)
        {
            ColliderTypes colliderType = _colliders.GetColliderType(locationalDamageSource.Collider);
            float multiplier = DamageMultipliers[colliderType];
            damage *= multiplier;
        }

        if (damageSource is ITypedDamage typedDamage)
        {
            if (damageSource is ILocationalDamage locationalDamage && _colliders != null)
            {
                string collider = _colliders.CollidersIds[locationalDamage.Collider];

                if (ResistsForColliders.ContainsKey(collider))
                {
                    typedDamage.DamageTypeData = ResistsForColliders[collider].ApplyResist(typedDamage.DamageTypeData, ref damage);
                }
                else
                {
                    typedDamage.DamageTypeData = Resists.ApplyResist(typedDamage.DamageTypeData, ref damage);
                }
            }
            else
            {
                typedDamage.DamageTypeData = Resists.ApplyResist(typedDamage.DamageTypeData, ref damage);
            }
        }
        else
        {
            DamageData damageData = new(damageSource.Type, damageSource.DamageTier);
            Resists.ApplyResist(damageData, ref damage);
        }

        return damage;
    }
}
