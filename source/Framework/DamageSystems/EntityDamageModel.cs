using CombatOverhaul.Colliders;
using System.Collections.Immutable;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
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
}

public delegate void OnEntityReceiveDamageDelegate(ref float damage, DamageSource damageSource, ColliderTypes damageZone);

public sealed class EntityDamageModelBehavior : EntityBehavior
{
    public EntityDamageModelBehavior(Entity entity) : base(entity)
    {
    }

    public event OnEntityReceiveDamageDelegate? OnReceiveDamage;

    public override string PropertyName() => "EntityDamageModel";
    public ImmutableDictionary<ColliderTypes, float> DamageMultipliers { get; private set; } = new Dictionary<ColliderTypes, float>()
    {
        { ColliderTypes.Torso, 1.0f },
        { ColliderTypes.Arm, 1.0f },
        { ColliderTypes.Leg, 1.0f },
        { ColliderTypes.Head, 1.0f },
        { ColliderTypes.Critical, 1.0f },
        { ColliderTypes.Resistant, 0.0f }
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
                { ColliderTypes.Critical, stats.CriticalDamageMultiplier },
                { ColliderTypes.Resistant, stats.ResistantDamageMultiplier }
            }.ToImmutableDictionary();
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
    }

    private CollidersEntityBehavior? _colliders;

    private void PrintToDamageLog(string message, DamageSource damageSource)
    {
        if (message != "") ((damageSource.CauseEntity as EntityPlayer)?.Player as IServerPlayer)?.SendMessage(GlobalConstants.DamageLogChatGroup, message, EnumChatType.Notification);
    }
    private float OnReceiveDamageHandler(float damage, DamageSource damageSource)
    {
        ColliderTypes colliderType = ColliderTypes.Torso;

        if (_colliders != null && damageSource is ILocationalDamage locationalDamageSource)
        {
            if (_colliders.CollidersTypes.ContainsKey(locationalDamageSource.Collider)) colliderType = _colliders.CollidersTypes[locationalDamageSource.Collider];
            float multiplier = DamageMultipliers[colliderType];
            damage *= multiplier;
        }

        if (damageSource is ITypedDamage typedDamage)
        {
            if (damageSource is ILocationalDamage locationalDamage && _colliders != null)
            {
                if (ResistsForColliders.ContainsKey(locationalDamage.Collider))
                {
                    typedDamage.DamageTypeData = ResistsForColliders[locationalDamage.Collider].ApplyResist(typedDamage.DamageTypeData, ref damage);
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

        OnReceiveDamage?.Invoke(ref damage, damageSource, colliderType);

        return damage;
    }
}
