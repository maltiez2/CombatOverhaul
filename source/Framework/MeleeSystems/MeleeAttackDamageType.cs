using CombatOverhaul.Colliders;
using CombatOverhaul.DamageSystems;
using ProtoBuf;
using System.Numerics;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Client.NoObf;
using static Microsoft.WindowsAPICodePack.Shell.PropertySystem.SystemProperties.System;

namespace CombatOverhaul.MeleeSystems;

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public struct MeleeDamagePacket
{
    public string DamageType { get; set; }
    public float Strength { get; set; }
    public float Damage { get; set; }
    public float[] Knockback { get; set; }
    public float[] Position { get; set; }
    public int Collider { get; set; }
    public int ColliderType { get; set; }
    public long AttackerEntityId { get; set; }
    public long TargetEntityId { get; set; }
    public int DurabilityDamage { get; set; }
    public bool MainHand { get; set; }

    public readonly void Attack(ICoreServerAPI api)
    {
        Entity target = api.World.GetEntityById(TargetEntityId);
        Entity attacker = api.World.GetEntityById(AttackerEntityId);

        bool damageReceived = target.ReceiveDamage(new DirectionalTypedDamageSource()
        {
            Source = attacker is EntityPlayer ? EnumDamageSource.Player : EnumDamageSource.Entity,
            SourceEntity = null,
            CauseEntity = attacker,
            DamageTypeData = new DamageData(Enum.Parse<EnumDamageType>(DamageType), Strength),
            Position = new Vector3(Position[0], Position[1], Position[2]),
            Collider = Collider
        }, Damage);

        bool received = damageReceived || Damage <= 0;

        if (received)
        {
            Vec3f knockback = new(Knockback[0], Knockback[1], Knockback[2]);

            target.Pos.Motion.Add(knockback);
            target.ServerPos.Motion.Add(knockback);
        }

        if (DurabilityDamage > 0)
        {
            ItemSlot? slot = (MainHand ? (attacker as EntityAgent)?.RightHandItemSlot : (attacker as EntityAgent)?.LeftHandItemSlot);
            slot?.Itemstack.Collectible.DamageItem(target.Api.World, attacker, slot, DurabilityDamage);
            slot?.MarkDirty();
        }

        string damageLogMessage = Lang.Get("combatoverhaul:damagelog-dealt-damage", Lang.Get($"combatoverhaul:entity-damage-zone-{(ColliderTypes)ColliderType}"), target.GetName(), $"{target.WatchedAttributes.GetFloat("onHurt"):F2}");

        ((attacker as EntityPlayer)?.Player as IServerPlayer)?.SendMessage(GlobalConstants.DamageLogChatGroup, damageLogMessage, EnumChatType.Notification);
    }
}

public class MeleeDamageTypeJson
{
    public DamageDataJson Damage { get; set; }
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
        DamageTypeData = new(Enum.Parse<EnumDamageType>(stats.Damage.DamageType), stats.Damage.Strength);
        Knockback = stats.Knockback;
        RelativeCollider = new LineSegmentCollider(stats.Collider);
        InWorldCollider = new LineSegmentCollider(stats.Collider);
        DurabilityDamage = stats.DurabilityDamage;
    }

    public bool TryAttack(IPlayer attacker, Entity target, out int collider, out Vector3 collisionPoint, out MeleeDamagePacket packet, bool mainHand, float maximumParameter)
    {
        bool collided = Collide(target, out collider, out collisionPoint, out float parameter, out ColliderTypes colliderType);

        packet = new();

        if (maximumParameter < parameter) return false;
        if (!collided) return false;

        bool received = Attack(attacker.Entity, target, collisionPoint, collider, out packet, mainHand, colliderType);

        return received;
    }
    public bool Attack(Entity attacker, Entity target, Vector3 position, int collider, out MeleeDamagePacket packet, bool mainHand, ColliderTypes colliderType)
    {
        packet = new();

        if (attacker.Api is ICoreServerAPI serverApi && attacker is EntityPlayer playerAttacker)
        {
            if (target is EntityPlayer && (!serverApi.Server.Config.AllowPvP || !playerAttacker.Player.HasPrivilege("attackplayers"))) return false;
            if (target is not EntityPlayer && !playerAttacker.Player.HasPrivilege("attackcreatures")) return false;
        }

        bool damageReceived = target.ReceiveDamage(new DirectionalTypedDamageSource()
        {
            Source = attacker is EntityPlayer ? EnumDamageSource.Player : EnumDamageSource.Entity,
            SourceEntity = null,
            CauseEntity = attacker,
            DamageTypeData = DamageTypeData,
            Position = position,
            Collider = collider
        }, Damage);

        bool received = damageReceived || Damage <= 0;

        Vec3f knockback = new();
        if (received)
        {
            knockback = (target.Pos.XYZFloat - attacker.Pos.XYZFloat).Normalize() * Knockback * _knockbackFactor * (1.0f - target.Properties.KnockbackResistance);

            target.Pos.Motion.Add(knockback);
            target.ServerPos.Motion.Add(knockback);
        }

        packet = new()
        {
            DamageType = DamageTypeData.DamageType.ToString(),
            Strength = DamageTypeData.Strength,
            Damage = Damage,
            Knockback = received ? new float[3] { knockback.X, knockback.Y, knockback.Z } : new float[3] { 0, 0, 0 },
            Position = new float[3] { position.X, position.Y, position.Z },
            Collider = collider,
            ColliderType = (int)colliderType,
            AttackerEntityId = attacker.EntityId,
            TargetEntityId = target.EntityId,
            DurabilityDamage = DurabilityDamage,
            MainHand = mainHand
        };

        return received;
    }

    private const float _knockbackFactor = 0.1f;
    private bool Collide(Entity target, out int collider, out Vector3 collisionPoint, out float parameter, out ColliderTypes colliderType)
    {
        parameter = 1f;

        colliderType = ColliderTypes.Torso;
        collisionPoint = Vector3.Zero;
        CollidersEntityBehavior? colliders = target.GetBehavior<CollidersEntityBehavior>();
        if (colliders != null)
        {
            bool intersects = colliders.Collide(InWorldCollider.Position, InWorldCollider.Direction, out collider, out parameter, out collisionPoint);
            colliderType = colliders.ColliderFromIndex(collider);
            return intersects;
        }

        collider = -1;

        Cuboidf collisionBox = GetCollisionBox(target);
        if (!InWorldCollider.RoughIntersect(collisionBox)) return false;
        Vector3? point = InWorldCollider.IntersectCuboid(collisionBox, out parameter);

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