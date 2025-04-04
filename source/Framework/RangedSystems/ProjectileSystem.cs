using CombatOverhaul.Colliders;
using CombatOverhaul.DamageSystems;
using ProtoBuf;
using OpenTK.Mathematics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace CombatOverhaul.RangedSystems;

public class ProjectileStats
{
    public int AdditionalDurabilityCost { get; set; } = 0;
    public string ImpactSound { get; set; } = "game:sounds/arrow-impact";
    public string HitSound { get; set; } = "game:sounds/player/projectilehit";
    public float CollisionRadius { get; set; } = 0;
    public float PenetrationDistance { get; set; } = 0;
    public ProjectileDamageDataJson DamageStats { get; set; } = new();
    public int DamageTierBonus { get; set; } = 0;
    public float SpeedThreshold { get; set; } = 0;
    public float Knockback { get; set; } = 0;
    public string EntityCode { get; set; } = "";
    public int DurabilityDamage { get; set; } = 0;
    public float DropChance { get; set; } = 0;

    public ProjectileStats() { }
}

public struct ProjectileSpawnStats
{
    public long ProducerEntityId { get; set; }
    public float DamageMultiplier { get; set; }
    public float DamageStrength { get; set; }
    public Vector3d Position { get; set; }
    public Vector3d Velocity { get; set; }
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class ProjectileCollisionPacket
{
    public Guid Id { get; set; }
    public double[] CollisionPoint { get; set; } = Array.Empty<double>();
    public double[] AfterCollisionVelocity { get; set; } = Array.Empty<double>();
    public double RelativeSpeed { get; set; }
    public string Collider { get; set; } = "";
    public long ReceiverEntity { get; set; }
    public int PacketVersion { get; set; }
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class ProjectileCollisionCheckRequest
{
    public Guid ProjectileId { get; set; }
    public long ProjectileEntityId { get; set; }
    public double[] CurrentPosition { get; set; } = Array.Empty<double>();
    public double[] PreviousPosition { get; set; } = Array.Empty<double>();
    public double[] Velocity { get; set; } = Array.Empty<double>();
    public float Radius { get; set; }
    public float PenetrationDistance { get; set; }
    public bool CollideWithShooter { get; set; }
    public long[] IgnoreEntities { get; set; } = Array.Empty<long>();
    public int PacketVersion { get; set; }
}

public abstract class ProjectileSystemBase
{
    protected static ProjectileEntity SpawnProjectile(Guid id, ItemStack projectileStack, ItemStack? weaponStack, ProjectileStats stats, ProjectileSpawnStats spawnStats, ICoreAPI api, Entity shooter)
    {
        AssetLocation entityTypeAsset = new(stats.EntityCode);

        EntityProperties? entityType = api.World.GetEntityType(entityTypeAsset) ?? throw new InvalidOperationException();

        if (api.ClassRegistry.CreateEntity(entityType) is not ProjectileEntity projectile)
        {
            throw new InvalidOperationException();
        }

        projectile.ProjectileId = id;
        projectile.ProjectileStack = projectileStack;
        projectile.WeaponStack = weaponStack;
        projectile.DropOnImpactChance = stats.DropChance;
        projectile.ColliderRadius = stats.CollisionRadius;
        projectile.PenetrationDistance = stats.PenetrationDistance;
        projectile.DurabilityDamageOnImpact = stats.DurabilityDamage;

        projectile.ServerPos.SetPos(new Vec3d(spawnStats.Position.X, spawnStats.Position.Y, spawnStats.Position.Z));
        projectile.ServerPos.Motion.Set(new Vec3d(spawnStats.Velocity.X, spawnStats.Velocity.Y, spawnStats.Velocity.Z));
        projectile.Pos.SetFrom(projectile.ServerPos);
        projectile.World = api.World;
        projectile.SetRotation();
        projectile.ShooterId = shooter.EntityId;

        api.World.SpawnEntity(projectile);

        return projectile;
    }
}


public sealed class ProjectileSystemClient : ProjectileSystemBase
{
    public const string NetworkChannelId = "CombatOverhaul:projectiles";

    public ProjectileSystemClient(ICoreClientAPI api, EntityPartitioning entityPartitioning)
    {
        _clientChannel = api.Network.RegisterChannel(NetworkChannelId)
            .RegisterMessageType<ProjectileCollisionPacket>()
            .RegisterMessageType<ProjectileCollisionCheckRequest>()
            .SetMessageHandler<ProjectileCollisionCheckRequest>(HandleRequest);

        _api = api;
        _entityPartitioning = entityPartitioning;
    }

    public void Collide(Guid id, Entity target, Vector3d point, Vector3d velocity, double relativeSpeed, string collider, ProjectileCollisionCheckRequest packet)
    {
        ProjectileCollisionPacket newPacket = new()
        {
            Id = id,
            CollisionPoint = new double[] { point.X, point.Y, point.Z },
            AfterCollisionVelocity = new double[] { velocity.X, velocity.Y, velocity.Z },
            RelativeSpeed = relativeSpeed,
            ReceiverEntity = target.EntityId,
            Collider = collider,
            PacketVersion = packet.PacketVersion
        };

        _clientChannel.SendPacket(newPacket);
    }

    private readonly ICoreClientAPI _api;
    private readonly IClientNetworkChannel _clientChannel;
    private readonly EntityPartitioning _entityPartitioning;
    private const float _entityCollisionRadius = 5;

    private void HandleRequest(ProjectileCollisionCheckRequest packet)
    {
        Entity[] entities = _api.World.GetEntitiesAround(
            new Vec3d(packet.CurrentPosition[0], packet.CurrentPosition[1], packet.CurrentPosition[2]),
            _entityCollisionRadius + packet.Radius,
            _entityCollisionRadius + packet.Radius);

        Vector3d currentPosition = new(packet.CurrentPosition[0], packet.CurrentPosition[1], packet.CurrentPosition[2]);
        Vector3d previousPosition = new(packet.PreviousPosition[0], packet.PreviousPosition[1], packet.PreviousPosition[2]);
        Vector3d velocity = new(packet.Velocity[0], packet.Velocity[1], packet.Velocity[2]);

        foreach (Entity entity in entities.Where(entity => entity.IsCreature))
        {
            if (Collide(entity, packet, currentPosition, previousPosition, velocity))
            {
                return;
            }
        }
    }
    private bool Collide(Entity target, ProjectileCollisionCheckRequest packet, Vector3d currentPosition, Vector3d previousPosition, Vector3d velocity)
    {
        if (target.EntityId == packet.ProjectileEntityId) return false;

        if (!packet.CollideWithShooter && _api.World.Player.Entity.EntityId == target.EntityId) return false;

        if (packet.IgnoreEntities.Contains(target.EntityId)) return false;

        if (!CheckCollision(target, out string collider, out Vector3d point, currentPosition, previousPosition, packet.Radius, packet.PenetrationDistance)) return false;

        Vector3d targetVelocity = new((float)target.Pos.Motion.X, (float)target.Pos.Motion.Y, (float)target.Pos.Motion.Z);

        double relativeSpeed = (targetVelocity - velocity).Length;

        Collide(packet.ProjectileId, target, point, targetVelocity, relativeSpeed, collider, packet);

        return true;
    }
    private bool CheckCollision(Entity target, out string collider, out Vector3d point, Vector3d currentPosition, Vector3d previousPosition, float radius)
    {
        CollidersEntityBehavior? colliders = target.GetBehavior<CollidersEntityBehavior>();
        if (colliders != null)
        {
            bool result = colliders.Collide(currentPosition, previousPosition, radius, out collider, out _, out point);
            return result;
        }

        collider = "";

        CuboidAABBCollider collisionBox = GetCollisionBox(target);
        return collisionBox.Collide(currentPosition, previousPosition, radius, out point);
    }
    private bool CheckCollision(Entity target, out string collider, out Vector3d point, Vector3d currentPosition, Vector3d previousPosition, float radius, float penetrationDistance)
    {
        CollidersEntityBehavior? colliders = target.GetBehavior<CollidersEntityBehavior>();
        EntityDamageModelBehavior? damageModel = target.GetBehavior<EntityDamageModelBehavior>();
        collider = "";
        point = new();

        if (colliders == null)
        {
            CuboidAABBCollider collisionBox = GetCollisionBox(target);
            return collisionBox.Collide(currentPosition, previousPosition, radius, out point);
        }

        bool result = colliders.Collide(currentPosition, previousPosition, radius, penetrationDistance, out List<(string key, double parameter, Vector3d point)> intersections);

        if (!result) return false;

        if (damageModel == null && intersections.Count > 0)
        {
            collider = intersections[0].key;
            point = intersections[0].point;
        }

        if (damageModel != null && intersections.Count > 0)
        {
            float maxDamageMultiplier = 0;

            foreach ((string key, double parameter, Vector3d intersectionPoint) in intersections)
            {
                ColliderTypes colliderType = colliders.CollidersTypes[key];
                if (colliderType == ColliderTypes.Resistant)
                {
                    if (collider == "")
                    {
                        collider = key;
                        point = intersectionPoint;
                    }

                    break;
                }

                float damageMultiplier = damageModel.DamageMultipliers[colliderType];
                if (damageMultiplier >= maxDamageMultiplier)
                {
                    maxDamageMultiplier = damageMultiplier;
                    collider = key;
                    point = intersectionPoint;
                }
            }
        }

        return true;
    }
    private static CuboidAABBCollider GetCollisionBox(Entity entity)
    {
        Cuboidf collisionBox = entity.CollisionBox.Clone(); // @TODO: Refactor to not clone
        EntityPos position = entity.Pos;
        collisionBox.X1 += (float)position.X;
        collisionBox.Y1 += (float)position.Y;
        collisionBox.Z1 += (float)position.Z;
        collisionBox.X2 += (float)position.X;
        collisionBox.Y2 += (float)position.Y;
        collisionBox.Z2 += (float)position.Z;
        return new(collisionBox);
    }
}

public sealed class ProjectileSystemServer : ProjectileSystemBase
{
    public delegate void RangedDamageDelegate(Entity target, DamageSource damageSource, ItemStack? weaponStack, ref float damage);

    public event RangedDamageDelegate? OnDealRangedDamage;

    public ProjectileSystemServer(ICoreServerAPI api)
    {
        _api = api;
        _serverChannel = api.Network.RegisterChannel(NetworkChannelId)
            .RegisterMessageType<ProjectileCollisionPacket>()
            .SetMessageHandler<ProjectileCollisionPacket>(HandleCollision)
            .RegisterMessageType<ProjectileCollisionCheckRequest>();
    }

    public const string NetworkChannelId = "CombatOverhaul:projectiles";

    public void Spawn(Guid id, ProjectileStats projectileStats, ProjectileSpawnStats spawnStats, ItemStack projectileStack, ItemStack? weaponStack, Entity shooter)
    {
        ProjectileEntity projectile = SpawnProjectile(id, projectileStack, weaponStack, projectileStats, spawnStats, _api, shooter);

        _projectiles.Add(id, new(projectile, projectileStats, spawnStats, _api, ClearId, projectileStack));

        projectile.ServerProjectile = _projectiles[id];
    }
    public void TryCollide(ProjectileEntity projectile)
    {
        ProjectileCollisionCheckRequest packet = new()
        {
            ProjectileId = projectile.ProjectileId,
            ProjectileEntityId = projectile.EntityId,
            CurrentPosition = new double[3] { projectile.ServerPos.X, projectile.ServerPos.Y, projectile.ServerPos.Z },
            PreviousPosition = new double[3] { projectile.PreviousPosition.X, projectile.PreviousPosition.Y, projectile.PreviousPosition.Z },
            Velocity = new double[3] { projectile.PreviousVelocity.X, projectile.PreviousVelocity.Y, projectile.PreviousVelocity.Z },
            Radius = projectile.ColliderRadius,
            PenetrationDistance = projectile.PenetrationDistance,
            CollideWithShooter = false,
            IgnoreEntities = projectile.CollidedWith.ToArray(),
            PacketVersion = projectile.ServerProjectile?.PacketVersion ?? 0
        };

        IServerPlayer? player = (_api.World.GetEntityById(projectile.ShooterId) as EntityPlayer)?.Player as IServerPlayer;

        if (player != null) _serverChannel.SendPacket(packet, player);
    }
    public void OnDealDamage(Entity target, DamageSource damageSource, ItemStack? weaponStack, ref float damage)
    {
        OnDealRangedDamage?.Invoke(target, damageSource, weaponStack, ref damage);
    }

    private readonly ICoreServerAPI _api;
    private readonly IServerNetworkChannel _serverChannel;
    private readonly Dictionary<Guid, ProjectileServer> _projectiles = new();

    private void ClearId(Guid id) => _projectiles.Remove(id);
    private void HandleCollision(IServerPlayer player, ProjectileCollisionPacket packet)
    {
        if (_projectiles.TryGetValue(packet.Id, out ProjectileServer? projectileServer))
        {
            if (projectileServer.PacketVersion != packet.PacketVersion) return;
            projectileServer.PacketVersion++;

            projectileServer._entity.CollidedWith.Add(packet.ReceiverEntity);
            projectileServer._entity.Stuck = false;
            projectileServer.OnCollision(packet);
        }
    }
}
