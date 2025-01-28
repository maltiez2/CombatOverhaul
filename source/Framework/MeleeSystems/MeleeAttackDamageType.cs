using CombatOverhaul.Colliders;
using CombatOverhaul.DamageSystems;
using OpenTK.Mathematics;
using ProtoBuf;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace CombatOverhaul.MeleeSystems;

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class MeleeDamagePacket
{
    public string DamageType { get; set; }
    public float Strength { get; set; }
    public float Damage { get; set; }
    public float Knockback { get; set; }
    public double[] Position { get; set; }
    public string Collider { get; set; }
    public int ColliderType { get; set; }
    public long AttackerEntityId { get; set; }
    public long TargetEntityId { get; set; }
    public int DurabilityDamage { get; set; }
    public bool MainHand { get; set; }
}

public class MeleeDamageTypeJson
{
    public DamageDataJson Damage { get; set; } = new();
    public float Knockback { get; set; } = 0;
    public int DurabilityDamage { get; set; } = 1;
    public float[] Collider { get; set; } = new float[6];

    public MeleeDamageType ToDamageType() => new(this);
}

public class MeleeDamageType : IHasLineCollider
{
    public LineSegmentCollider RelativeCollider { get; set; }
    public LineSegmentCollider InWorldCollider { get; set; }

    public readonly float Damage;
    public readonly DamageData DamageTypeData;
    public readonly float Knockback;
    public readonly int DurabilityDamage;

    public MeleeDamageType(MeleeDamageTypeJson stats)
    {
        Damage = stats.Damage.Damage;
        DamageTypeData = new(Enum.Parse<EnumDamageType>(stats.Damage.DamageType), Math.Max(stats.Damage.Strength, stats.Damage.Tier));
        Knockback = stats.Knockback;
        RelativeCollider = new LineSegmentCollider(stats.Collider);
        InWorldCollider = new LineSegmentCollider(stats.Collider);
        DurabilityDamage = stats.DurabilityDamage;
    }

    public bool TryAttack(IPlayer attacker, Entity target, out string collider, out Vector3d collisionPoint, out MeleeDamagePacket packet, bool mainHand, double maximumParameter)
    {
        bool collided = Collide(target, out collider, out collisionPoint, out double parameter, out ColliderTypes colliderType);

        packet = new();

        if (maximumParameter < parameter) return false;
        if (!collided) return false;

        bool received = Attack(attacker.Entity, target, collisionPoint, collider, out packet, mainHand, colliderType);

        return received;
    }
    public bool Attack(Entity attacker, Entity target, Vector3d position, string collider, out MeleeDamagePacket packet, bool mainHand, ColliderTypes colliderType)
    {
        packet = new();

        if (attacker.Api is ICoreServerAPI serverApi && attacker is EntityPlayer playerAttacker)
        {
            if (target is EntityPlayer && (!serverApi.Server.Config.AllowPvP || !playerAttacker.Player.HasPrivilege("attackplayers"))) return false;
            if (target is not EntityPlayer && !playerAttacker.Player.HasPrivilege("attackcreatures")) return false;
        }

        float damage = Damage * attacker.Stats.GetBlended("meleeWeaponsDamage");
        if (target.Properties.Attributes?["isMechanical"].AsBool() == true)
        {
            damage *= attacker.Stats.GetBlended("mechanicalsDamage");
        }

        bool damageReceived = target.ReceiveDamage(new DirectionalTypedDamageSource()
        {
            Source = attacker is EntityPlayer ? EnumDamageSource.Player : EnumDamageSource.Entity,
            SourceEntity = attacker,
            CauseEntity = attacker,
            DamageTypeData = DamageTypeData,
            Position = position,
            Collider = collider,
            KnockbackStrength = Knockback
        }, damage);

        bool received = damageReceived || Damage > 0;

        packet = new()
        {
            DamageType = DamageTypeData.DamageType.ToString(),
            Strength = DamageTypeData.Tier,
            Damage = damage,
            Knockback = Knockback,
            Position = new double[3] { position.X, position.Y, position.Z },
            Collider = collider,
            ColliderType = (int)colliderType,
            AttackerEntityId = attacker.EntityId,
            TargetEntityId = target.EntityId,
            DurabilityDamage = DurabilityDamage,
            MainHand = mainHand
        };

        return received;
    }

    private bool Collide(Entity target, out string collider, out Vector3d collisionPoint, out double parameter, out ColliderTypes colliderType)
    {
        parameter = 1f;

        colliderType = ColliderTypes.Torso;
        collisionPoint = Vector3.Zero;
        CollidersEntityBehavior? colliders = target.GetBehavior<CollidersEntityBehavior>();
        if (colliders != null)
        {
            bool intersects = colliders.Collide(InWorldCollider.Position, InWorldCollider.Direction, out collider, out parameter, out collisionPoint);

            if (intersects) colliders.CollidersTypes.TryGetValue(collider, out colliderType);

            return intersects;
        }

        collider = "";

        Cuboidf collisionBox = GetCollisionBox(target);
        if (!InWorldCollider.RoughIntersect(collisionBox)) return false;
        Vector3d? point = InWorldCollider.IntersectCuboid(collisionBox, out parameter);

        if (point == null) return false;

        collisionPoint = point.Value;
        return true;
    }
    private static Cuboidf GetCollisionBox(Entity entity)
    {
        Cuboidf collisionBox = entity.CollisionBox.Clone(); // @TODO: Refactor to not clone
        EntityPos position = entity.Pos;
        collisionBox.X1 += (float)position.X;
        collisionBox.Y1 += (float)position.Y;
        collisionBox.Z1 += (float)position.Z;
        collisionBox.X2 += (float)position.X;
        collisionBox.Y2 += (float)position.Y;
        collisionBox.Z2 += (float)position.Z;
        return collisionBox;
    }
}