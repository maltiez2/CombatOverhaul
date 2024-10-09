using CombatOverhaul.Colliders;
using CombatOverhaul.DamageSystems;
using System.Numerics;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
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

        _entity.OnCollisionWithEntity(receiver, packet.Collider);
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

    private bool Attack(Entity attacker, Entity target, Vector3 position, string collider, float relativeSpeed)
    {
        if (!CheckPermissions(attacker, target)) return false;
        if (relativeSpeed < _stats.SpeedThreshold) return false;

        string targetName = target.GetName();
        string projectileName = _entity.GetName();

        float damage = _stats.DamageStats.Damage * _spawnStats.DamageMultiplier;
        DamageData damageData = new(Enum.Parse<EnumDamageType>(_stats.DamageStats.DamageType), _spawnStats.DamageStrength);

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

        CollidersEntityBehavior? colliders = target.GetBehavior<CollidersEntityBehavior>();
        ColliderTypes ColliderType = colliders?.CollidersTypes[collider] ?? ColliderTypes.Torso;

        float damageReceivedValue = damageReceived ? target.WatchedAttributes.GetFloat("onHurt") : 0;
        string damageLogMessage = Lang.Get("combatoverhaul:damagelog-dealt-damage-with-projectile", Lang.Get($"combatoverhaul:entity-damage-zone-{ColliderType}"), targetName, $"{damageReceivedValue:F2}", projectileName);
        ((attacker as EntityPlayer)?.Player as IServerPlayer)?.SendMessage(GlobalConstants.DamageLogChatGroup, damageLogMessage, EnumChatType.Notification);

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
}

public class ProjectileEntity : Entity
{
    public ProjectileServer? ServerProjectile { get; set; }
    public Guid ProjectileId { get; set; }
    public ItemStack? ProjectileStack { get; set; }
    public float DropOnImpactChance { get; set; }
    public Action<Guid>? ClearCallback { get; set; }
    public float ColliderRadius { get; set; }
    public long ShooterId { get; set; }
    public Vec3d PreviousPosition { get; private set; } = new(0, 0, 0);
    public Vec3d PreviousVelocity { get; private set; } = new(0, 0, 0);
    public List<long> CollidedWith { get; set; } = new();
    public bool Stuck
    {
        get => StuckInternal;
        set
        {
            StuckInternal = value;
            if (Api.Side == EnumAppSide.Server) WatchedAttributes.SetBool("stuck", StuckInternal);
        }
    }

    public override bool ApplyGravity => !Stuck;
    public override bool IsInteractable => false;

    public override void Initialize(EntityProperties properties, ICoreAPI api, long InChunkIndex3d)
    {
        base.Initialize(properties, api, InChunkIndex3d);

        SpawnTime = TimeSpan.FromMilliseconds(World.ElapsedMilliseconds);

        CollisionTestBox = SelectionBox.Clone().OmniGrowBy(0.05f);

        GetBehavior<EntityBehaviorPassivePhysics>().OnPhysicsTickCallback = OnPhysicsTickCallback;
        //GetBehavior<EntityBehaviorPassivePhysics>().collisionYExtra = 0f; // Slightly cheap hax so that stones/arrows don't collid with fences

        PreviousPosition = Pos.XYZ.Clone();
        PreviousVelocity = Pos.Motion.Clone();
    }
    public override void OnGameTick(float dt)
    {
        base.OnGameTick(dt);
        if (ShouldDespawn) return;

        EntityPos pos = SidedPos;

        Stuck = Collided || CollTester.IsColliding(World.BlockAccessor, CollisionTestBox, pos.XYZ) || WatchedAttributes.GetBool("stuck");
        if (Api.Side == EnumAppSide.Server) WatchedAttributes.SetBool("stuck", Stuck);

        if (!Stuck)
        {
            SetRotation();
        }

        double impactSpeed = Math.Max(MotionBeforeCollide.Length(), SidedPos.Motion.Length());
        if (Stuck)
        {
            if (Api.Side == EnumAppSide.Client)
            {
                ServerPos.SetFrom(Pos);
            }

            OnTerrainCollision(SidedPos, impactSpeed);
        }

        BeforeCollided = false;
        MotionBeforeCollide.Set(SidedPos.Motion.X, SidedPos.Motion.Y, SidedPos.Motion.Z);
    }
    public override bool CanCollect(Entity byEntity)
    {
        return Alive && TimeSpan.FromMilliseconds(World.ElapsedMilliseconds) - SpawnTime > CollisionDelay && ServerPos.Motion.Length() < 0.01;
    }
    public override ItemStack? OnCollected(Entity byEntity)
    {
        ClearCallback?.Invoke(ProjectileId);
        ProjectileStack?.ResolveBlockOrItem(World);
        return ProjectileStack;
    }
    public override void OnCollided()
    {
        EntityPos sidedPos = SidedPos;
        OnTerrainCollision(SidedPos, Math.Max(MotionBeforeCollide.Length(), sidedPos.Motion.Length()));
        MotionBeforeCollide.Set(sidedPos.Motion.X, sidedPos.Motion.Y, sidedPos.Motion.Z);
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
                + GameMath.Cos((float)(TimeSpan.FromMilliseconds(World.ElapsedMilliseconds) - SpawnTime).TotalMilliseconds / 200f) * 0.03f
            ;
            pos.Roll =
                -(float)Math.Asin(GameMath.Clamp(-pos.Motion.Y / speed, -1, 1))
                + GameMath.Sin((float)(TimeSpan.FromMilliseconds(World.ElapsedMilliseconds) - SpawnTime).TotalMilliseconds / 200f) * 0.03f
            ;
        }
    }

    public void OnCollisionWithEntity(Entity target, string collider)
    {
        WatchedAttributes.MarkAllDirty();
        TryDestroyOnCollision();
    }

    protected readonly TimeSpan CollisionDelay = TimeSpan.FromMilliseconds(500);
    protected TimeSpan SpawnTime = TimeSpan.Zero;
    protected bool StuckInternal;
    protected readonly CollisionTester CollTester = new();
    protected Cuboidf? CollisionTestBox;
    protected Vec3d MotionBeforeCollide = new();
    protected bool BeforeCollided = false;
    protected long MsCollide = 0;
    protected Random Rand = new();

    protected void OnPhysicsTickCallback(float dtFac)
    {
        if (ShouldDespawn || !Alive) return;

        if (!Stuck && ServerProjectile != null)
        {
            ServerProjectile.TryCollide();
        }

        PreviousPosition = SidedPos.XYZ.Clone();
        PreviousVelocity = SidedPos.Motion.Clone();
    }
    protected void OnTerrainCollision(EntityPos pos, double impactSpeed)
    {
        pos.Motion.Set(0.0, 0.0, 0.0);
        if (BeforeCollided || !(World is IServerWorldAccessor) || World.ElapsedMilliseconds <= MsCollide + 500)
        {
            return;
        }

        if (impactSpeed >= 0.07)
        {
            World.PlaySoundAt(new AssetLocation("sounds/arrow-impact"), this, null, randomizePitch: false);
            //TryDestroyOnCollision(); // Projectile can collide with terrain before packet of entity collision arrives
            WatchedAttributes.MarkAllDirty();
        }

        MsCollide = World.ElapsedMilliseconds;
        BeforeCollided = true;
    }
    protected virtual void TryDestroyOnCollision()
    {
        float random = (float)Rand.NextDouble();
        if (DropOnImpactChance <= random)
        {
            World.PlaySoundAt(new AssetLocation("sounds/effect/toolbreak"), this, null, randomizePitch: true, volume: 0.5f);
            Die();
        }
    }
}

public class ProjectileBehavior : CollectibleBehavior
{
    public ProjectileStats? Stats { get; private set; }

    public ProjectileBehavior(CollectibleObject collObj) : base(collObj)
    {
    }

    public override void Initialize(JsonObject properties)
    {
        base.Initialize(properties);
        
        Stats = properties["stats"].AsObject<ProjectileStats>();
    }

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        if (Stats != null)
        {
            dsc.AppendLine(Lang.Get(
            "combatoverhaul:iteminfo-projectile",
            Stats.DamageStats.Damage,
            Lang.Get($"combatoverhaul:damage-type-{Stats.DamageStats.DamageType}"),
            $"{(1 - Stats.DropChance) * 100:F1}"));
        }

        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
    }
}

public class ProjectileExplosiveBehavior : CollectibleBehavior
{
    public ExplosiveProjectileStats Stats { get; private set; }

    public ProjectileExplosiveBehavior(CollectibleObject collObj) : base(collObj)
    {
    }

    public override void Initialize(JsonObject properties)
    {
        Stats = properties["stats"].AsObject<ExplosiveProjectileStats>();
    }
}

public class ProjectileFragmentationBehavior : CollectibleBehavior
{
    public FragmentationProjectileStats Stats { get; private set; }

    public ProjectileFragmentationBehavior(CollectibleObject collObj) : base(collObj)
    {
    }

    public override void Initialize(JsonObject properties)
    {
        Stats = properties["stats"].AsObject<FragmentationProjectileStats>();
    }
}

public class ProjectileExplosive : ProjectileEntity
{
    public ExplosiveProjectileStats ExplosiveStats { get; set; } = new();
    public TimeSpan FuseTimer { get; set; } = TimeSpan.Zero;

    public override void Initialize(EntityProperties properties, ICoreAPI api, long InChunkIndex3d)
    {
        base.Initialize(properties, api, InChunkIndex3d);

        if (Api.Side != EnumAppSide.Server) return;

        ProjectileStack?.ResolveBlockOrItem(Api.World);

        Item item = ProjectileStack?.Item ?? throw new Exception();

        if (item.GetBehavior<ProjectileExplosiveBehavior>() is not ProjectileExplosiveBehavior behavior) throw new Exception();

        ExplosiveStats = behavior.Stats;

        FuseTimer = TimeSpan.FromMilliseconds(ExplosiveStats.FuseTimeMs);
    }

    public override void ToBytes(BinaryWriter writer, bool forClient)
    {
        base.ToBytes(writer, forClient);

        byte[] data = JsonUtil.ToBytes(ExplosiveStats);
        writer.Write(data.Length);
        writer.Write(data);
        writer.Write(FuseTimer.TotalMilliseconds);
    }

    public override void FromBytes(BinaryReader reader, bool fromServer)
    {
        base.FromBytes(reader, fromServer);

        ExplosiveStats = JsonUtil.FromBytes<ExplosiveProjectileStats>(reader.ReadBytes(reader.ReadInt32()));
        FuseTimer = TimeSpan.FromMilliseconds((float)reader.ReadDouble());
    }

    public override void OnGameTick(float dt)
    {
        base.OnGameTick(dt);

        if (Api.Side != EnumAppSide.Server) return;

        FuseTimer -= TimeSpan.FromSeconds(dt);

        if (FuseTimer > TimeSpan.Zero) return;

        Explode();
        Die();
    }

    protected void Explode()
    {
        Entity[] entities = Api.World.GetEntitiesAround(ServerPos.XYZ, ExplosiveStats.MaxRadius, ExplosiveStats.MaxRadius, entity => entity.IsCreature && entity.Alive);

        Entity shooter = Api.World.GetEntityById(ShooterId);

        Api.World.PlaySoundAt(ExplosiveStats.ExplosionSound, this);

        EnumDamageType damageType = Enum.Parse<EnumDamageType>(ExplosiveStats.DamageType);

        Utils.ParticleEffectsManager? particlesEffectsManager = Api.ModLoader.GetModSystem<CombatOverhaulAnimationsSystem>().ParticleEffectsManager;

        particlesEffectsManager?.Spawn(ExplosiveStats.ParticlesEffect, new((float)ServerPos.X, (float)ServerPos.Y, (float)ServerPos.Z), Vector3.Zero, ExplosiveStats.ParticlesIntensity);

        foreach (Entity entity in entities)
        {
            float damage = Math.Clamp((float)(entity.Pos.XYZ - ServerPos.XYZ).Length() / ExplosiveStats.MaxRadius, 0, 1) * ExplosiveStats.Damage;

            _ = entity.ReceiveDamage(new TypedDamageSource()
            {
                Source = EnumDamageSource.Entity,
                SourceEntity = this,
                CauseEntity = shooter,
                Type = damageType,
                DamageTier = (int)ExplosiveStats.DamageStrength,
                DamageTypeData = new(damageType, ExplosiveStats.DamageStrength)

            }, damage);
        }
    }
}

public class ProjectileFragmentation : ProjectileEntity
{
    public FragmentationProjectileStats FragmentationStats { get; set; } = new();
    public TimeSpan FuseTimer { get; set; } = TimeSpan.Zero;

    public override void Initialize(EntityProperties properties, ICoreAPI api, long InChunkIndex3d)
    {
        base.Initialize(properties, api, InChunkIndex3d);

        if (Api.Side != EnumAppSide.Server) return;

        ProjectileStack?.ResolveBlockOrItem(Api.World);

        Item item = ProjectileStack?.Item ?? throw new Exception();

        if (item.GetBehavior<ProjectileFragmentationBehavior>() is not ProjectileFragmentationBehavior behavior) throw new Exception();

        FragmentationStats = behavior.Stats;

        FuseTimer = TimeSpan.FromMilliseconds(FragmentationStats.FuseTimeMs);
    }

    public override void ToBytes(BinaryWriter writer, bool forClient)
    {
        base.ToBytes(writer, forClient);

        FragmentationStats.FragmentStack.ResolvedItemstack = null;

        byte[] data = JsonUtil.ToBytes(FragmentationStats);
        writer.Write(data.Length);
        writer.Write(data);
        writer.Write(FuseTimer.TotalMilliseconds);
    }

    public override void FromBytes(BinaryReader reader, bool fromServer)
    {
        base.FromBytes(reader, fromServer);

        FragmentationStats = JsonUtil.FromBytes<FragmentationProjectileStats>(reader.ReadBytes(reader.ReadInt32()));
        FuseTimer = TimeSpan.FromMilliseconds((float)reader.ReadDouble());
    }

    public override void OnGameTick(float dt)
    {
        base.OnGameTick(dt);

        if (Api.Side != EnumAppSide.Server) return;

        FuseTimer -= TimeSpan.FromSeconds(dt);

        if (FuseTimer > TimeSpan.Zero) return;

        Explode();
        Die();
    }

    protected readonly NatFloat Rand = new(0, 1, EnumDistribution.UNIFORM);

    protected void Explode()
    {
        Entity shooter = Api.World.GetEntityById(ShooterId);

        Api.World.PlaySoundAt(FragmentationStats.ExplosionSound, this);

        Utils.ParticleEffectsManager? particlesEffectsManager = Api.ModLoader.GetModSystem<CombatOverhaulAnimationsSystem>().ParticleEffectsManager;
        ProjectileSystemServer? projectileSystem = Api.ModLoader.GetModSystem<CombatOverhaulSystem>().ServerProjectileSystem;

        FragmentationStats.FragmentStack.Resolve(Api.World, "", false);

        particlesEffectsManager?.Spawn(FragmentationStats.ParticlesEffect, new((float)ServerPos.X, (float)ServerPos.Y, (float)ServerPos.Z), Vector3.Zero, FragmentationStats.ParticlesIntensity);

        for (int count = 0; count < FragmentationStats.FragmentsNumber; count++)
        {
            Guid guid = Guid.NewGuid();
            ProjectileStats stats = FragmentationStats.FragmentStats;
            ProjectileSpawnStats spawnStats = new()
            {
                ProducerEntityId = shooter.EntityId,
                DamageMultiplier = 1,
                DamageStrength = FragmentationStats.FragmentDamageStrength,
                Position = new Vector3((float)ServerPos.X, (float)ServerPos.Y, (float)ServerPos.Z),
                Velocity = Vector3.Normalize(GenerateRandomDirection()) * FragmentationStats.FragmentVelocity
            };

            projectileSystem?.Spawn(guid, stats, spawnStats, FragmentationStats.FragmentStack.ResolvedItemstack, shooter);
        }
    }

    protected Vector3 GenerateRandomDirection()
    {
        double theta = Rand.nextFloat() * 2 * Math.PI;
        double phi = Rand.nextFloat() * Math.PI;

        float x = (float)(Math.Sin(phi) * Math.Cos(theta));
        float y = (float)(Math.Sin(phi) * Math.Sin(theta));
        float z = (float)(Math.Cos(phi));

        return new(x, y, z);
    }
}

public class ProjectileOreBomb : ProjectileEntity
{

}

public class ProjectileFirework : ProjectileEntity
{

}