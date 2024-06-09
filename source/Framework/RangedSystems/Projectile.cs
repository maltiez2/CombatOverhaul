using CombatOverhaul.DamageSystems;
using System.Numerics;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace CombatOverhaul.RangedSystems;

public sealed class ProjectileServer
{
    public ProjectileServer(ProjectileEntity projectile, ProjectileStats projectileStats, ProjectileSpawnStats spawnStats, ICoreAPI api, Action<Guid> clearCallback, ItemStack projectileStack)
    {
        _stats = projectileStats;
        _spawnStats = spawnStats;
        _api = api;
        _shooter = _api.World.GetEntityById(spawnStats.ProducerEntityId);

        _system = _api.ModLoader.GetModSystem<CombatOverhaulSystem>().ServerProjectileSystem ?? throw new Exception();

        _entity = projectile;
        _entity.ClearCallback = clearCallback;
    }

    public int PacketVersion { get; set; } = 0;

    public void OnCollision(ProjectileCollisionPacket packet)
    {
        Entity receiver = _api.World.GetEntityById(packet.ReceiverEntity);

        Vector3 collisionPoint = new(packet.CollisionPoint[0], packet.CollisionPoint[1], packet.CollisionPoint[2]);

        _ = Attack(_shooter, receiver, collisionPoint, packet.Collider, packet.RelativeSpeed);

        _entity.ServerPos.SetPos(new Vec3d(collisionPoint.X, collisionPoint.Y, collisionPoint.Z));
        _entity.ServerPos.Motion.X = receiver.ServerPos.Motion.X;
        _entity.ServerPos.Motion.Y = receiver.ServerPos.Motion.Y;
        _entity.ServerPos.Motion.Z = receiver.ServerPos.Motion.Z;

        _entity.OnCollisionWithEntity();
    }

    public void TryCollide()
    {
        _system.TryCollide(_entity);
    }

    private readonly ProjectileStats _stats;
    private readonly ProjectileSpawnStats _spawnStats;
    internal readonly ProjectileEntity _entity;
    private readonly Entity _shooter;
    private readonly ICoreAPI _api;
    private readonly ProjectileSystemServer _system;

    private bool Attack(Entity attacker, Entity target, Vector3 position, int collider, float relativeSpeed)
    {
        if (!CheckPermissions(attacker, target)) return false;
        //if (relativeSpeed < _stats.SpeedThreshold) return false;

        float damage = _stats.DamageStats.Damage * _spawnStats.DamageMultiplier;
        DamageData damageData = new(Enum.Parse<EnumDamageType>(_stats.DamageStats.DamageType), _stats.DamageStats.Strength * _spawnStats.StrengthMultiplier);

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
}

public sealed class ProjectileEntity : Entity
{
    public ProjectileServer? ServerProjectile { get; set; }
    public Guid ProjectileId { get; set; }
    public ItemStack? ProjectileStack { get; set; }
    public float DropOnImpactChance { get; set; }
    public Action<Guid>? ClearCallback { get; set; }
    public float ColliderRadius { get; set; }
    public long ShooterId { get; set; }
    public Vec3d PreviousPosition { get; private set; } = new(0, 0, 0);
    public List<long> CollidedWith { get; set; } = new();
    public bool Stuck
    {
        get => _stuck;
        set
        {
            _stuck = value;
            if (Api.Side == EnumAppSide.Server) WatchedAttributes.SetBool("stuck", _stuck);
        }
    }

    public override bool ApplyGravity => !_stuck;
    public override bool IsInteractable => false;

    public override void Initialize(EntityProperties properties, ICoreAPI api, long InChunkIndex3d)
    {
        base.Initialize(properties, api, InChunkIndex3d);

        _spawnTime = TimeSpan.FromMilliseconds(World.ElapsedMilliseconds);

        _collisionTestBox = SelectionBox.Clone().OmniGrowBy(0.05f);

        GetBehavior<EntityBehaviorPassivePhysics>().OnPhysicsTickCallback = OnPhysicsTickCallback;
        GetBehavior<EntityBehaviorPassivePhysics>().collisionYExtra = 0f; // Slightly cheap hax so that stones/arrows don't collid with fences

        PreviousPosition = Pos.XYZ.Clone();
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

        double impactSpeed = Math.Max(_motionBeforeCollide.Length(), SidedPos.Motion.Length());
        if (_stuck)
        {
            if (Api.Side == EnumAppSide.Client)
            {
                ServerPos.SetFrom(Pos);
            }

            OnTerrainCollision(SidedPos, impactSpeed);
        }

        _beforeCollided = false;
        _motionBeforeCollide.Set(SidedPos.Motion.X, SidedPos.Motion.Y, SidedPos.Motion.Z);
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
    public override void OnCollided()
    {
        EntityPos sidedPos = base.SidedPos;
        OnTerrainCollision(base.SidedPos, Math.Max(_motionBeforeCollide.Length(), sidedPos.Motion.Length()));
        _motionBeforeCollide.Set(sidedPos.Motion.X, sidedPos.Motion.Y, sidedPos.Motion.Z);
    }
    public override void ToBytes(BinaryWriter writer, bool forClient)
    {
        base.ToBytes(writer, forClient);
        writer.Write(ShooterId);
        writer.Write(ProjectileId.ToString());
        ProjectileStack?.ToBytes(writer);
    }
    public override void FromBytes(BinaryReader reader, bool fromServer)
    {
        base.FromBytes(reader, fromServer);
        ShooterId = reader.ReadInt64();
        ProjectileId = Guid.Parse(reader.ReadString());
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
    private Vec3d _motionBeforeCollide = new();
    private bool _beforeCollided = false;
    private long _msCollide = 0;

    private void OnPhysicsTickCallback(float dtFac)
    {
        if (ShouldDespawn || !Alive) return;

        if (!_stuck && ServerProjectile != null)
        {
            ServerProjectile.TryCollide();
        }

        PreviousPosition = ServerPos.XYZ.Clone();
    }
    private void OnTerrainCollision(EntityPos pos, double impactSpeed)
    {
        pos.Motion.Set(0.0, 0.0, 0.0);
        if (_beforeCollided || !(World is IServerWorldAccessor) || World.ElapsedMilliseconds <= _msCollide + 500)
        {
            return;
        }

        if (impactSpeed >= 0.07)
        {
            World.PlaySoundAt(new AssetLocation("sounds/arrow-impact"), this, null, randomizePitch: false);
            WatchedAttributes.MarkAllDirty();
        }

        _msCollide = World.ElapsedMilliseconds;
        _beforeCollided = true;
    }
}

public class ProjectileBehavior : CollectibleBehavior
{
    public ProjectileStats Stats { get; private set; }

    public ProjectileBehavior(CollectibleObject collObj) : base(collObj)
    {
    }

    public override void Initialize(JsonObject properties)
    {
        Stats = properties["stats"].AsObject<ProjectileStats>();
    }
}