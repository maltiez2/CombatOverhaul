using CombatOverhaul.Colliders;
using CombatOverhaul.DamageSystems;
using CombatOverhaul.MeleeSystems;
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
    public DamageDataJson Damage { get; set; } = new();
    public float SpeedThreshold { get; set; } = 0;
    public float Knockback { get; set; } = 0;
    public string EntityCode { get; set; } = "";
    public int DurabilityDamage { get; set; } = 0;
    public float DropChance { get; set; } = 0;

    public ProjectileStats() { }
}

public struct ProjectileCreationStats
{
    public long ProducerEntityId { get; set; }
    public int ProjectileType { get; set; }
    public float DamageMultiplier { get; set; }
    public float StrengthMultiplier { get; set; }
    public Vector3 Position { get; set; }
    public Vector3 Velocity { get; set; }
}

public struct ProjectileCollisionPacket
{
    public Guid Id { get; set; }
    public Vector3 CollisionPoint { get; set; }
    public Vector3 AfterCollisionVelocity { get; set; }
    public int Collider { get; set; }
    public long ReceiverEntity { get; set; }
}


internal sealed class ProjectileSystemClient : MeleeSystem
{
    public ProjectileSystemClient(ICoreClientAPI api)
    {
        _clientChannel = api.Network.RegisterChannel(NetworkChannelId)
            .RegisterMessageType<ProjectileCollisionPacket>();
    }

    public void Collide(Guid id, Entity target, Vector3 point, Vector3 velocity, int collider)
    {
        ProjectileCollisionPacket packet = new()
        {
            Id = id,
            CollisionPoint = point,
            AfterCollisionVelocity = velocity,
            ReceiverEntity = target.EntityId,
            Collider = collider
        };

        _clientChannel.SendPacket(packet);
    }

    private readonly IClientNetworkChannel _clientChannel;
}

internal sealed class ProjectileSystemServer : MeleeSystem
{
    public ProjectileSystemServer(ICoreServerAPI api)
    {
        _api = api;
        api.Network.RegisterChannel(NetworkChannelId)
            .RegisterMessageType<ProjectileCollisionPacket>()
            .SetMessageHandler<ProjectileCollisionPacket>(HandleCollision);
    }

    private readonly ICoreServerAPI _api;
    private readonly Dictionary<Guid, ProjectileServer> _projectiles = new();

    public void Spawn(ProjectileStats projectileStats, ProjectileCreationStats spawnStats, ItemStack projectileStack)
    {
        Guid id = Guid.NewGuid();
        _projectiles.Add(id, new(id, projectileStats, spawnStats, _api, ClearId, projectileStack));
    }

    private void ClearId(Guid id) => _projectiles.Remove(id);
    private void HandleCollision(IServerPlayer player, ProjectileCollisionPacket packet)
    {
        if (_projectiles.TryGetValue(packet.Id, out ProjectileServer? projectileServer))
        {
            projectileServer.OnCollision(packet);
        }
    }
}

internal sealed class ProjectileClient
{
    public ProjectileClient(ICoreClientAPI api, ProjectileEntity entity, float colliderRadius, EntityPartitioning entityPartitioning)
    {
        _entity = entity;
        _colliderRadius = colliderRadius;
        _entityPartitioning = entityPartitioning;
        _api = api;

        _system = api.ModLoader.GetModSystem<CombatOverhaulSystem>().ClientProjectileSystem ?? throw new InvalidOperationException("ProjectileSystemClient is null");
    }

    public bool Collided { get; set; } = false;
    public bool SelfCollide { get; set; } = true;

    public bool Collide()
    {
        if (Collided) return true;

        _entityPartitioning.WalkEntities(_entity.Pos.XYZ, _entityCollisionRadius + _colliderRadius, Collide, EnumEntitySearchType.Creatures);

        return Collided;
    }

    private const float _entityCollisionRadius = 5;
    private readonly float _colliderRadius;
    private readonly ProjectileEntity _entity;
    private readonly EntityPartitioning _entityPartitioning;
    private readonly ProjectileSystemClient _system;
    private readonly ICoreClientAPI _api;

    private Vector3 Position => new((float)_entity.Pos.X, (float)_entity.Pos.Y, (float)_entity.Pos.Z);

    private bool Collide(Entity target)
    {
        if (!SelfCollide && _api.World.Player.Entity.EntityId == target.EntityId) return false;

        if (!CheckCollision(target, out int collider, out Vector3 point)) return false;

        _system.Collide(_entity.ProjectileId, target, point, new Vector3((float)_entity.Pos.Motion.X, (float)_entity.Pos.Motion.Y, (float)_entity.Pos.Motion.Z), collider);

        return true;
    }
    private bool CheckCollision(Entity target, out int collider, out Vector3 point)
    {
        CollidersEntityBehavior? colliders = target.GetBehavior<CollidersEntityBehavior>();
        if (colliders != null)
        {
            return colliders.Collide(Position, _colliderRadius, out collider, out _, out point);
        }

        collider = -1;

        CuboidAABBCollider collisionBox = GetCollisionBox(target);
        return collisionBox.Collide(Position, _colliderRadius, out point);
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

internal sealed class ProjectileServer
{
    public ProjectileServer(Guid id, ProjectileStats projectileStats, ProjectileCreationStats spawnStats, ICoreServerAPI api, Action<Guid> clearCallback, ItemStack projectileStack)
    {
        _stats = projectileStats;
        _spawnStats = spawnStats;
        _api = api;
        _producer = _api.World.GetEntityById(spawnStats.ProducerEntityId);

        _entity = SpawnProjectile(id, projectileStack);
        _entity.ClearCallback = clearCallback;
    }

    public void OnCollision(ProjectileCollisionPacket packet)
    {
        Entity receiver = _api.World.GetEntityById(packet.ReceiverEntity);

        _ = Attack(_producer, receiver, packet.CollisionPoint, packet.Collider);

        _entity.ServerPos.SetPos(new Vec3d(packet.CollisionPoint.X, packet.CollisionPoint.Y, packet.CollisionPoint.Z));
        _entity.ServerPos.Motion.X = receiver.ServerPos.Motion.X;
        _entity.ServerPos.Motion.Y = receiver.ServerPos.Motion.Y;
        _entity.ServerPos.Motion.Z = receiver.ServerPos.Motion.Z;

        _entity.OnCollisionWithEntity();
    }

    private readonly ProjectileStats _stats;
    private readonly ProjectileCreationStats _spawnStats;
    private readonly ProjectileEntity _entity;
    private readonly Entity _producer;
    private readonly ICoreServerAPI _api;

    private bool Attack(Entity attacker, Entity target, Vector3 position, int collider)
    {
        if (!CheckPermissions(attacker, target)) return false;
        if (!CheckRelativeSpeed(target)) return false;

        float damage = _stats.Damage.Damage * _spawnStats.DamageMultiplier;
        DamageData damageData = new(Enum.Parse<EnumDamageType>(_stats.Damage.DamageType), _stats.Damage.Strength * _spawnStats.StrengthMultiplier);

        bool damageReceived = target.ReceiveDamage(new DirectionalTypedDamageSource()
        {
            Source = EnumDamageSource.Entity,
            SourceEntity = _entity,
            CauseEntity = attacker,
            Type = damageData.DamageType,
            Position = position,
            Collider = collider,
            DamageTypeData = damageData,
        }, damage);

        bool received = damageReceived || damage <= 0;

        if (received)
        {
            Vec3f knockback = _entity.Pos.Motion.ToVec3f() * _stats.Knockback * (1.0f - target.Properties.KnockbackResistance);
            target.SidedPos.Motion.Add(knockback);
        }

        return received;
    }
    private static bool CheckPermissions(Entity attacker, Entity target)
    {
        if (attacker.Api is ICoreServerAPI serverApi && attacker is EntityPlayer playerAttacker)
        {
            if (target is EntityPlayer && (!serverApi.Server.Config.AllowPvP || !playerAttacker.Player.HasPrivilege("attackplayers"))) return false;
            if (target is not EntityPlayer && !playerAttacker.Player.HasPrivilege("attackcreatures")) return false;
        }

        return true;
    }
    private bool CheckRelativeSpeed(Entity target)
    {
        Vector3 targetVelocity = new((float)target.ServerPos.Motion.X, (float)target.ServerPos.Motion.Y, (float)target.ServerPos.Motion.Z);
        Vector3 projectileVelocity = new((float)_entity.ServerPos.Motion.X, (float)_entity.ServerPos.Motion.Y, (float)_entity.ServerPos.Motion.Z);
        float relativeSpeed = (projectileVelocity - targetVelocity).Length();

        return relativeSpeed >= _stats.SpeedThreshold;
    }
    private ProjectileEntity SpawnProjectile(Guid id, ItemStack projectileStack)
    {
        AssetLocation entityTypeAsset = new(projectileStack.Collectible.Attributes["projectile"].AsString(_stats.EntityCode));

        EntityProperties? entityType = _api.World.GetEntityType(entityTypeAsset) ?? throw new InvalidOperationException();

        if (_api.ClassRegistry.CreateEntity(entityType) is not ProjectileEntity projectile)
        {
            throw new InvalidOperationException();
        }

        if (_stats.DurabilityDamage != 0) projectileStack.Item.DamageItem(_api.World, projectile, new DummySlot(projectileStack), _stats.DurabilityDamage);

        projectile.ProjectileId = id;
        projectile.ServerProjectile = this;
        projectile.ProjectileStack = projectileStack;
        projectile.DropOnImpactChance = _stats.DropChance;

        projectile.ServerPos.SetPos(new Vec3d(_spawnStats.Position.X, _spawnStats.Position.Y, _spawnStats.Position.Z));
        projectile.ServerPos.Motion.Set(new Vec3d(_spawnStats.Velocity.X, _spawnStats.Velocity.Y, _spawnStats.Velocity.Z));
        projectile.Pos.SetFrom(projectile.ServerPos);
        projectile.World = _api.World;
        projectile.SetRotation();
        projectile.ProducerId = _producer.EntityId;

        _api.World.SpawnEntity(projectile);

        return projectile;
    }
}


public sealed class ProjectileEntity : Entity
{
    internal ProjectileClient? ClientProjectile { get; set; }
    internal ProjectileServer? ServerProjectile { get; set; }
    public Guid ProjectileId { get; set; }
    public ItemStack? ProjectileStack { get; set; }
    public float DropOnImpactChance { get; set; }
    public Action<Guid>? ClearCallback { get; set; }
    public float ColliderRadius { get; set; }
    public long ProducerId { get; set; }

    public override bool ApplyGravity => !_stuck;
    public override bool IsInteractable => false;

    public override void Initialize(EntityProperties properties, ICoreAPI api, long InChunkIndex3d)
    {
        base.Initialize(properties, api, InChunkIndex3d);

        _spawnTime = TimeSpan.FromMilliseconds(World.ElapsedMilliseconds);

        _collisionTestBox = SelectionBox.Clone().OmniGrowBy(0.05f);

        GetBehavior<EntityBehaviorPassivePhysics>().OnPhysicsTickCallback = OnPhysicsTickCallback;
        GetBehavior<EntityBehaviorPassivePhysics>().collisionYExtra = 0f; // Slightly cheap hax so that stones/arrows don't collid with fences

        if (api is ICoreClientAPI clientApi && clientApi.World.Player.Entity.EntityId == ProducerId)
        {
            ClientProjectile = new(clientApi, this, ColliderRadius, api.ModLoader.GetModSystem<EntityPartitioning>());
        }
    }
    public override void OnGameTick(float dt)
    {
        base.OnGameTick(dt);
        if (ShouldDespawn) return;

        EntityPos pos = SidedPos;

        _stuck = Collided || _collTester.IsColliding(World.BlockAccessor, _collisionTestBox, pos.XYZ) || WatchedAttributes.GetBool("stuck");
        if (Api.Side == EnumAppSide.Server) WatchedAttributes.SetBool("stuck", _stuck);

        if (!_stuck)
        {
            SetRotation();
        }
    }
    public override bool CanCollect(Entity byEntity)
    {
        return Alive && TimeSpan.FromMilliseconds(World.ElapsedMilliseconds) - _spawnTime > _collisionDelay && ServerPos.Motion.Length() < 0.01;
    }
    public override ItemStack? OnCollected(Entity byEntity)
    {
        ClearCallback?.Invoke(ProjectileId);
        ProjectileStack?.ResolveBlockOrItem(World);
        return ProjectileStack;
    }
    public override void ToBytes(BinaryWriter writer, bool forClient)
    {
        base.ToBytes(writer, forClient);
        writer.Write(ProducerId);
        ProjectileStack?.ToBytes(writer);
    }
    public override void FromBytes(BinaryReader reader, bool fromServer)
    {
        base.FromBytes(reader, fromServer);
        ProducerId = reader.ReadInt64();
        ProjectileStack = new ItemStack(reader);
    }
    public override void OnEntityDespawn(EntityDespawnData despawn)
    {
        base.OnEntityDespawn(despawn);
        ClearCallback?.Invoke(ProjectileId);
    }
    public void SetRotation()
    {
        EntityPos pos = (World is IServerWorldAccessor) ? ServerPos : Pos;

        double speed = pos.Motion.Length();

        if (speed > 0.01)
        {
            pos.Pitch = 0;
            pos.Yaw =
                GameMath.PI + (float)Math.Atan2(pos.Motion.X / speed, pos.Motion.Z / speed)
                + GameMath.Cos((float)(TimeSpan.FromMilliseconds(World.ElapsedMilliseconds) - _spawnTime).TotalMilliseconds / 200f) * 0.03f
            ;
            pos.Roll =
                -(float)Math.Asin(GameMath.Clamp(-pos.Motion.Y / speed, -1, 1))
                + GameMath.Sin((float)(TimeSpan.FromMilliseconds(World.ElapsedMilliseconds) - _spawnTime).TotalMilliseconds / 200f) * 0.03f
            ;
        }
    }

    public void OnCollisionWithEntity()
    {
        WatchedAttributes.MarkAllDirty();
    }

    private readonly TimeSpan _collisionDelay = TimeSpan.FromMilliseconds(500);
    private TimeSpan _spawnTime = TimeSpan.Zero;

    private bool _stuck;
    private readonly CollisionTester _collTester = new();
    private Cuboidf? _collisionTestBox;

    private void OnPhysicsTickCallback(float dtFac)
    {
        if (ShouldDespawn || !Alive) return;

        TimeSpan currentTime = TimeSpan.FromMilliseconds(World.ElapsedMilliseconds);

        if (ClientProjectile != null)
        {
            ClientProjectile.SelfCollide = currentTime - _spawnTime >= _collisionDelay;
            _ = ClientProjectile.Collide();
        }
    }
}
