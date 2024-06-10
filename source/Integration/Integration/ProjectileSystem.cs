using CombatOverhaul.Colliders;
using CombatOverhaul.DamageSystems;
using ProtoBuf;
using System.Numerics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace CombatOverhaul.RangedSystems;

public struct ProjectileStats
{
    public int AdditionalDurabilityCost { get; set; } = 0;
    public AssetLocation ImpactSound { get; set; } = new("game:sounds/arrow-impact");
    public AssetLocation HitSound { get; set; } = new("game:sounds/player/projectilehit");
    public float CollisionRadius { get; set; } = 0;
    public DamageDataJson DamageStats { get; set; } = new();
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
    public float StrengthMultiplier { get; set; }
    public Vector3 Position { get; set; }
    public Vector3 Velocity { get; set; }
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class ProjectileCollisionPacket
{
    public Guid Id { get; set; }
    public float[] CollisionPoint { get; set; } = Array.Empty<float>();
    public float[] AfterCollisionVelocity { get; set; } = Array.Empty<float>();
    public float RelativeSpeed { get; set; }
    public int Collider { get; set; }
    public long ReceiverEntity { get; set; }
    public int PacketVersion { get; set; }
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class ProjectileCollisionCheckRequest
{
    public Guid ProjectileId { get; set; }
    public long ProjectileEntityId { get; set; }
    public float[] CurrentPosition { get; set; } = Array.Empty<float>();
    public float[] PreviousPosition { get; set; } = Array.Empty<float>();
    public float[] Velocity { get; set; } = Array.Empty<float>();
    public float Radius { get; set; }
    public bool CollideWithShooter { get; set; }
    public long[] IgnoreEntities { get; set; } = Array.Empty<long>();
    public int PacketVersion { get; set; }
}

public abstract class ProjectileSystemBase
{
    protected static ProjectileEntity SpawnProjectile(Guid id, ItemStack projectileStack, ProjectileStats stats, ProjectileSpawnStats spawnStats, ICoreAPI api, Entity shooter)
    {
        AssetLocation entityTypeAsset = new(stats.EntityCode);

        EntityProperties? entityType = api.World.GetEntityType(entityTypeAsset) ?? throw new InvalidOperationException();

        if (api.ClassRegistry.CreateEntity(entityType) is not ProjectileEntity projectile)
        {
            throw new InvalidOperationException();
        }

        if (stats.DurabilityDamage != 0) projectileStack.Item.DamageItem(api.World, projectile, new DummySlot(projectileStack), stats.DurabilityDamage);

        projectile.ProjectileId = id;
        projectile.ProjectileStack = projectileStack;
        projectile.DropOnImpactChance = stats.DropChance;
        projectile.ColliderRadius = stats.CollisionRadius;

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

    public void Collide(Guid id, Entity target, Vector3 point, Vector3 velocity, float relativeSpeed, int collider, ProjectileCollisionCheckRequest packet)
    {
        ProjectileCollisionPacket newPacket = new()
        {
            Id = id,
            CollisionPoint = new float[] { point.X, point.Y, point.Z },
            AfterCollisionVelocity = new float[] { velocity.X, velocity.Y, velocity.Z },
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

        Vector3 currentPosition = new(packet.CurrentPosition[0], packet.CurrentPosition[1], packet.CurrentPosition[2]);
        Vector3 previousPosition = new(packet.PreviousPosition[0], packet.PreviousPosition[1], packet.PreviousPosition[2]);
        Vector3 velocity = new(packet.Velocity[0], packet.Velocity[1], packet.Velocity[2]);

        /*foreach (Entity entity in entities)
        {
            if (entity.EntityId == packet.ProjectileEntityId && entity is ProjectileEntity projectile)
            {
                currentPosition = new Vector3((float)projectile.Pos.X, (float)projectile.Pos.Y, (float)projectile.Pos.Z);
                previousPosition = new Vector3((float)projectile.PreviousPosition.X, (float)projectile.PreviousPosition.Y, (float)projectile.PreviousPosition.Z);
                velocity = new Vector3((float)projectile.PreviousVelocity.X, (float)projectile.PreviousVelocity.Y, (float)projectile.PreviousVelocity.Z);
                break;
            }
        }*/

        foreach (Entity entity in entities.Where(entity => entity.IsCreature))
        {
            if (Collide(entity, packet, currentPosition, previousPosition, velocity))
            {
                return;
            }
        }
    }
    private bool Collide(Entity target, ProjectileCollisionCheckRequest packet, Vector3 currentPosition, Vector3 previousPosition, Vector3 velocity)
    {
        if (target.EntityId == packet.ProjectileEntityId) return false;

        if (!packet.CollideWithShooter && _api.World.Player.Entity.EntityId == target.EntityId) return false;

        if (packet.IgnoreEntities.Contains(target.EntityId)) return false;

        if (!CheckCollision(target, out int collider, out Vector3 point, currentPosition, previousPosition, packet.Radius)) return false;

        Vector3 targetVelocity = new Vector3((float)target.Pos.Motion.X, (float)target.Pos.Motion.Y, (float)target.Pos.Motion.Z);

        float relativeSpeed = (targetVelocity - velocity).Length();

        Collide(packet.ProjectileId, target, point, targetVelocity, relativeSpeed, collider, packet);

        return true;
    }
    private bool CheckCollision(Entity target, out int collider, out Vector3 point, Vector3 currentPosition, Vector3 previousPosition, float radius)
    {
        CollidersEntityBehavior? colliders = target.GetBehavior<CollidersEntityBehavior>();
        if (colliders != null)
        {
            return colliders.Collide(currentPosition, previousPosition, radius, out collider, out _, out point);
        }

        collider = -1;

        CuboidAABBCollider collisionBox = GetCollisionBox(target);
        return collisionBox.Collide(currentPosition, previousPosition, radius, out point);
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
    public ProjectileSystemServer(ICoreServerAPI api)
    {
        _api = api;
        _serverChannel = api.Network.RegisterChannel(NetworkChannelId)
            .RegisterMessageType<ProjectileCollisionPacket>()
            .SetMessageHandler<ProjectileCollisionPacket>(HandleCollision)
            .RegisterMessageType<ProjectileCollisionCheckRequest>();
    }

    public const string NetworkChannelId = "CombatOverhaul:projectiles";

    public void Spawn(Guid id, ProjectileStats projectileStats, ProjectileSpawnStats spawnStats, ItemStack projectileStack, Entity shooter)
    {
        ProjectileEntity projectile = SpawnProjectile(id, projectileStack, projectileStats, spawnStats, _api, shooter);

        _projectiles.Add(id, new(projectile, projectileStats, spawnStats, _api, ClearId, projectileStack));

        projectile.ServerProjectile = _projectiles[id];
    }
    public void TryCollide(ProjectileEntity projectile)
    {
        ProjectileCollisionCheckRequest packet = new()
        {
            ProjectileId = projectile.ProjectileId,
            ProjectileEntityId = projectile.EntityId,
            CurrentPosition = new float[3] { (float)projectile.ServerPos.X, (float)projectile.ServerPos.Y, (float)projectile.ServerPos.Z },
            PreviousPosition = new float[3] { (float)projectile.PreviousPosition.X, (float)projectile.PreviousPosition.Y, (float)projectile.PreviousPosition.Z },
            Velocity = new float[3] { (float)projectile.PreviousVelocity.X, (float)projectile.PreviousVelocity.Y, (float)projectile.PreviousVelocity.Z },
            Radius = projectile.ColliderRadius,
            CollideWithShooter = false,
            IgnoreEntities = projectile.CollidedWith.ToArray(),
            PacketVersion = projectile.ServerProjectile?.PacketVersion ?? 0
        };

        IServerPlayer? player = (_api.World.GetEntityById(projectile.ShooterId) as EntityPlayer)?.Player as IServerPlayer;

        if (player != null) _serverChannel.SendPacket(packet, player);
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
