using System.Numerics;
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
    public float SpeedThreshold { get; set; } = 0;
    public float Knockback { get; set; } = 0;
    public string EntityCode { get; set; } = "";
    public int DurabilityDamage { get; set; } = 0;
    public float DropChance { get; set; } = 0;

    public ProjectileStats() { }
}

public struct ProjectileDamageDataJson
{
    public string DamageType { get; set; } = "PiercingAttack";
    public float Damage { get; set; }

    public ProjectileDamageDataJson() { }
}

public struct ProjectileSpawnStats
{
    public long ProducerEntityId { get; set; }
    public float DamageMultiplier { get; set; }
    public float DamageStrength { get; set; }
    public Vector3 Position { get; set; }
    public Vector3 Velocity { get; set; }
}

public sealed class ProjectileSystemServer
{
    public ProjectileSystemServer(ICoreServerAPI api)
    {
        _api = api;
    }

    public void Spawn(Guid id, ProjectileStats projectileStats, ProjectileSpawnStats spawnStats, ItemStack projectileStack, Entity shooter)
    {
        AssetLocation entityTypeAsset = new(projectileStats.EntityCode);

        EntityProperties? entityType = _api.World.GetEntityType(entityTypeAsset) ?? throw new InvalidOperationException();

        if (_api.ClassRegistry.CreateEntity(entityType) is not EntityProjectile projectile)
        {
            throw new InvalidOperationException();
        }

        projectile.ProjectileStack = projectileStack;
        projectile.DropOnImpactChance = projectileStats.DropChance;

        projectile.Damage = projectileStats.DamageStats.Damage * spawnStats.DamageMultiplier;
        projectile.DamageType = Enum.Parse<EnumDamageType>(projectileStats.DamageStats.DamageType);
        projectile.DamageTier = (int)spawnStats.DamageStrength;
        projectile.FiredBy = shooter;
        projectile.ProjectileStack = projectileStack;

        projectile.ServerPos.SetPos(new Vec3d(spawnStats.Position.X, spawnStats.Position.Y, spawnStats.Position.Z));
        projectile.ServerPos.Motion.Set(new Vec3d(spawnStats.Velocity.X, spawnStats.Velocity.Y, spawnStats.Velocity.Z));
        projectile.Pos.SetFrom(projectile.ServerPos);
        projectile.World = _api.World;
        projectile.SetRotation();

        _api.World.SpawnEntity(projectile);
    }

    private readonly ICoreServerAPI _api;
}
