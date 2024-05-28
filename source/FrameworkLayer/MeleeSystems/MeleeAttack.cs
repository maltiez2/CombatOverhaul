using CombatOverhaul.Colliders;
using System.Collections.Immutable;
using System.Numerics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace CombatOverhaul.MeleeSystems;

[Flags]
public enum AttackResultFlag
{
    None = 0,
    HitEntity = 1,
    HitTerrain = 2,
    Finished = 4,
    Blocked = 8,
    Parried = 16
}

public readonly struct AttackResult
{
    public readonly AttackResultFlag Result;
    public readonly IEnumerable<(Block block, Vector3 point)> Terrain = Array.Empty<(Block block, Vector3 point)>();
    public readonly IEnumerable<(Entity entity, Vector3 point, AttackResultFlag hitType)> Entities = Array.Empty<(Entity entity, Vector3 point, AttackResultFlag hitType)>();

    public AttackResult(AttackResultFlag result = AttackResultFlag.None, IEnumerable<(Block block, Vector3 point)>? terrain = null, IEnumerable<(Entity entity, Vector3 point, AttackResultFlag hitType)>? entities = null)
    {
        Result = result;
        if (terrain != null) Terrain = terrain;
        if (entities != null) Entities = entities;
    }
}

public class MeleeAttackStats
{
    public float Duration { get; set; } = 0;
    public float MaxReach { get; set; } = 4.5f;
    public MeleeAttackDamageTypeStats[] DamageTypes { get; set; } = Array.Empty<MeleeAttackDamageTypeStats>();
}

public sealed class MeleeAttack
{
    public int Id { get; }
    public int ItemId { get; }
    public TimeSpan Duration { get; }
    public IEnumerable<MeleeAttackDamageType> DamageTypes { get; }
    public float MaxReach { get; }

    public bool StopOnTerrainHit { get; set; } = true;
    public bool StopOnEntityHit { get; set; } = false;
    public bool StopOnParry { get; set; } = true;
    public bool StopOnBlock { get; set; } = true;
    public bool CollideWithTerrain { get; set; } = true;

    public MeleeAttack(int id, int itemId, ICoreClientAPI api, TimeSpan duration, IEnumerable<MeleeAttackDamageType> damageTypes, float maxReach)
    {
        Id = id;
        ItemId = itemId;
        _api = api;
        Duration = duration;
        DamageTypes = damageTypes;
        MaxReach = maxReach;
    }

    public MeleeAttack(ICoreClientAPI api, int id, int itemId, MeleeAttackStats stats)
    {
        Id = id;
        ItemId = itemId;
        _api = api;

        Duration = TimeSpan.FromMilliseconds(stats.Duration);
        MaxReach = stats.MaxReach;

        DamageTypes = stats.DamageTypes.Select((stats, index) => new MeleeAttackDamageType(new(itemId, id, index), stats)).ToImmutableArray();
    }

    public void Start(IPlayer player, AttackDirection direction)
    {
        long entityId = player.Entity.EntityId;

        _direction = direction;
        _currentTime[entityId] = 0;
        _totalTime[entityId] = (float)Duration.TotalMilliseconds; // @TODO: Make some stats to affect this
        if (_totalTime[entityId] <= 0) _totalTime[entityId] = 1;

        if (_attackedEntities.TryGetValue(entityId, out HashSet<long>? value))
        {
            value.Clear();
        }
        else
        {
            _attackedEntities[entityId] = new();
        }

    }
    public AttackResult Step(IPlayer player, float dt, ItemSlot slot, out IEnumerable<MeleeAttackDamagePacket> damagePackets, bool rightHand = true)
    {
        AttackResultFlag result = AttackResultFlag.None;

        damagePackets = Array.Empty<MeleeAttackDamagePacket>();

        _currentTime[player.Entity.EntityId] += dt * 1000;
        float progress = GameMath.Clamp(_currentTime[player.Entity.EntityId] / _totalTime[player.Entity.EntityId], 0, 1);
        if (progress >= 1)
        {
            return new(AttackResultFlag.Finished);
        }

        bool success = LineSegmentCollider.Transform(DamageTypes.Select(element => element as IHasLineCollider), player.Entity, slot, _api, rightHand);
        if (!success) return new(AttackResultFlag.None);

        if (CollideWithTerrain)
        {
            IEnumerable<(Block block, Vector3 point)> terrainCollisions = CheckTerrainCollision(progress);
            if (terrainCollisions.Any()) result |= AttackResultFlag.HitTerrain;

            if (StopOnTerrainHit && terrainCollisions.Any()) return new(result, terrain: terrainCollisions);
        }

        _damagePackets.Clear();

        IEnumerable<(Entity entity, Vector3 point, AttackResultFlag hitType)> entitiesCollisions = CollideWithEntities(progress, player, _damagePackets, slot);

        foreach ((_, _, AttackResultFlag hitType) in entitiesCollisions)
        {
            result |= hitType;
        }

        if (entitiesCollisions.Any())
        {
            damagePackets = _damagePackets;
            return new(result, entities: entitiesCollisions);
        }

        return new(result);
    }
    public void RenderDebugColliders(IPlayer player, ItemSlot slot, bool rightHand = true)
    {
        LineSegmentCollider.Transform(DamageTypes.Select(element => element as IHasLineCollider), player.Entity, slot, _api, rightHand);
        foreach (LineSegmentCollider collider in DamageTypes.Select(item => item.InWorldCollider))
        {
            collider.Render(_api, player.Entity);
        }
    }

    private readonly ICoreClientAPI _api;
    private readonly Dictionary<long, float> _currentTime = new();
    private readonly Dictionary<long, float> _totalTime = new();
    private readonly Dictionary<long, HashSet<long>> _attackedEntities = new();
    private readonly List<MeleeAttackDamagePacket> _damagePackets = new();
    private readonly HashSet<(Block block, Vector3 point)> _terrainCollisionsBuffer = new();
    private readonly HashSet<(Entity entity, Vector3 point, AttackResultFlag hitType)> _entitiesCollisionsBuffer = new();
    private AttackDirection _direction = AttackDirection.Top;

    private IEnumerable<(Block block, Vector3 point)> CheckTerrainCollision(float progress)
    {
        _terrainCollisionsBuffer.Clear();

        foreach (MeleeAttackDamageType damageType in DamageTypes.Where(item => item.HitWindow.X <= progress && item.HitWindow.Y >= progress))
        {
            (Block block, Vector3 position)? result = damageType.InWorldCollider.IntersectTerrain(_api);

            if (result != null)
            {
                _terrainCollisionsBuffer.Add(result.Value);
                Vector3 direction = damageType.InWorldCollider.Direction / damageType.InWorldCollider.Direction.Length() * -1;
            }
        }

        return _terrainCollisionsBuffer.ToImmutableHashSet();
    }
    private IEnumerable<(Entity entity, Vector3 point, AttackResultFlag hitType)> CollideWithEntities(float progress, IPlayer player, List<MeleeAttackDamagePacket> packets, ItemSlot slot)
    {
        long entityId = player.Entity.EntityId;

        Entity[] entities = _api.World.GetEntitiesAround(player.Entity.Pos.XYZ, MaxReach, MaxReach);

        _entitiesCollisionsBuffer.Clear();

        foreach (MeleeAttackDamageType damageType in DamageTypes.Where(item => item.HitWindow.X <= progress && item.HitWindow.Y >= progress))
        {
            bool stop = false;

            foreach (EntityAgent entity in entities
                    .Where(entity => entity != player.Entity)
                    .Where(entity => !_attackedEntities[entityId].Contains(entity.EntityId))
                    .OfType<EntityAgent>()
                    )
            {
                MeleeBlockBehavior? blockBehavior = entity.GetBehavior<MeleeBlockBehavior>();
                if (blockBehavior == null || (!blockBehavior.IsParrying() && !blockBehavior.IsBlocking())) continue;

                Vector3? intersection = CollideWithMeleeBlock(blockBehavior, damageType);

                if (intersection == null) continue;

                if (blockBehavior.IsParrying() && StopOnParry) return new (Entity entity, Vector3 point, AttackResultFlag hitType)[] { (entity, intersection.Value, AttackResultFlag.Parried) };
                if (blockBehavior.IsBlocking() && StopOnBlock) return new (Entity entity, Vector3 point, AttackResultFlag hitType)[] { (entity, intersection.Value, AttackResultFlag.Blocked) };

                if (blockBehavior.IsParrying()) _entitiesCollisionsBuffer.Add((entity, intersection.Value, AttackResultFlag.Parried));
                if (blockBehavior.IsBlocking()) _entitiesCollisionsBuffer.Add((entity, intersection.Value, AttackResultFlag.Blocked));
            }

            if (stop) break;
        }

        foreach (MeleeAttackDamageType damageType in DamageTypes.Where(item => item.HitWindow.X <= progress && item.HitWindow.Y >= progress))
        {
            int collider = -1;
            foreach (Entity entity in entities
                    .Where(entity => entity != player.Entity)
                    .Where(entity => !_attackedEntities[entityId].Contains(entity.EntityId)))
            {
                Vector3? point = damageType.TryAttack(player, entity, _direction, out collider);

                if (point == null) continue;
                packets.Add(new MeleeAttackDamagePacket() { Id = damageType.Id, Position = new float[] { point.Value.X, point.Value.Y, point.Value.Z }, AttackerEntityId = player.Entity.EntityId, TargetEntityId = entity.EntityId, Collider = collider });

                _entitiesCollisionsBuffer.Add((entity, point.Value, AttackResultFlag.HitEntity));

                if (damageType.DurabilityDamage > 0)
                {
                    slot.Itemstack.Collectible.DamageItem(_api.World, player.Entity, slot, damageType.DurabilityDamage);
                }
                if (StopOnEntityHit) break;
            }
        }

        IEnumerable<(Entity entity, Vector3 point, AttackResultFlag hitType)> result = _entitiesCollisionsBuffer.ToImmutableHashSet();
        _entitiesCollisionsBuffer.Clear();
        return result;
    }

    private static Vector3? CollideWithMeleeBlock(MeleeBlockBehavior blockBehavior, MeleeAttackDamageType damageType)
    {
        IEnumerable<IParryCollider> colliders = blockBehavior.GetColliders();

        float closestParameter = float.MaxValue;
        Vector3 closestIntersection = Vector3.Zero;
        bool intersected = false;
        foreach (IParryCollider parryCollider in colliders)
        {
            if (parryCollider.IntersectSegment(damageType.InWorldCollider, out float parameter, out Vector3 intersection) && parameter < closestParameter)
            {
                closestParameter = parameter;
                closestIntersection = intersection;
                intersected = true;

                Console.WriteLine($"closestParameter: {closestParameter}");
            }
        }

        return intersected ? closestIntersection : null;
    }
}