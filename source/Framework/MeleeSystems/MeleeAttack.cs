using CombatOverhaul.Colliders;
using System.Collections.Immutable;
using System.Numerics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace CombatOverhaul.MeleeSystems;

public class MeleeAttackStats
{
    public bool StopOnTerrainHit { get; set; } = true;
    public bool StopOnEntityHit { get; set; } = false;
    public bool CollideWithTerrain { get; set; } = true;
    public float MaxReach { get; set; } = 6;

    public MeleeDamageTypeJson[] DamageTypes { get; set; } = Array.Empty<MeleeDamageTypeJson>();
}

public sealed class MeleeAttack
{
    public IEnumerable<MeleeDamageType> DamageTypes { get; }

    public bool StopOnTerrainHit { get; set; }
    public bool StopOnEntityHit { get; set; }
    public bool CollideWithTerrain { get; set; }
    public float MaxReach { get; set; }

    public MeleeAttack(ICoreClientAPI api, MeleeAttackStats stats)
    {
        _api = api;
        StopOnTerrainHit = stats.StopOnTerrainHit;
        StopOnEntityHit = stats.StopOnEntityHit;
        CollideWithTerrain = stats.CollideWithTerrain;
        MaxReach = stats.MaxReach;
        DamageTypes = stats.DamageTypes.Select(stats => stats.ToDamageType()).ToImmutableArray();

        _meleeSystem = api.ModLoader.GetModSystem<CombatOverhaulSystem>().ClientMeleeSystem ?? throw new Exception();
    }

    public void Start(IPlayer player)
    {
        long entityId = player.Entity.EntityId;

        if (_attackedEntities.TryGetValue(entityId, out HashSet<long>? value))
        {
            value.Clear();
        }
        else
        {
            _attackedEntities[entityId] = new();
        }
    }

    public void Attack(IPlayer player, ItemSlot slot, bool mainHand, out IEnumerable<(Block block, Vector3 point)> terrainCollisions, out IEnumerable<(Entity entity, Vector3 point)> entitiesCollisions)
    {
        terrainCollisions = Array.Empty<(Block block, Vector3 point)>();
        entitiesCollisions = Array.Empty<(Entity entity, Vector3 point)>();

        PrepareColliders(player, slot, mainHand);

        if (CollideWithTerrain)
        {
            bool collidedWithTerrain = TryCollideWithTerrain(out terrainCollisions);

            if (collidedWithTerrain && StopOnTerrainHit) return;
        }

        TryAttackEntities(player, slot, out entitiesCollisions);
    }
    public void PrepareColliders(IPlayer player, ItemSlot slot, bool mainHand)
    {
        LineSegmentCollider.Transform(DamageTypes.Select(element => element as IHasLineCollider), player.Entity, slot, _api, mainHand);
    }
    public bool TryCollideWithTerrain(out IEnumerable<(Block block, Vector3 point)> terrainCollisions)
    {
        terrainCollisions = CheckTerrainCollision();

        return terrainCollisions.Any();
    }
    public bool TryAttackEntities(IPlayer player, ItemSlot slot, out IEnumerable<(Entity entity, Vector3 point)> entitiesCollisions)
    {
        entitiesCollisions = CollideWithEntities(player, out IEnumerable<MeleeDamagePacket> damagePackets, slot);

        if (damagePackets.Any()) _meleeSystem.SendPackets(damagePackets);

        return entitiesCollisions.Any();
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
    private readonly Dictionary<long, HashSet<long>> _attackedEntities = new();
    private readonly HashSet<(Block block, Vector3 point)> _terrainCollisionsBuffer = new();
    private readonly HashSet<(Entity entity, Vector3 point)> _entitiesCollisionsBuffer = new();
    private readonly MeleeSystemClient _meleeSystem;

    private IEnumerable<(Block block, Vector3 point)> CheckTerrainCollision()
    {
        _terrainCollisionsBuffer.Clear();

        foreach (MeleeDamageType damageType in DamageTypes)
        {
            (Block block, Vector3 position)? result = damageType.InWorldCollider.IntersectTerrain(_api);

            if (result != null)
            {
                _terrainCollisionsBuffer.Add(result.Value);
            }
        }

        return _terrainCollisionsBuffer.ToImmutableHashSet();
    }
    private IEnumerable<(Entity entity, Vector3 point)> CollideWithEntities(IPlayer player, out IEnumerable<MeleeDamagePacket> packets, ItemSlot slot)
    {
        long entityId = player.Entity.EntityId;

        Entity[] entities = _api.World.GetEntitiesAround(player.Entity.Pos.XYZ, MaxReach, MaxReach);

        _entitiesCollisionsBuffer.Clear();

        List<MeleeDamagePacket> damagePackets = new();

        foreach (MeleeDamageType damageType in DamageTypes)
        {
            foreach (Entity entity in entities
                    .Where(entity => entity != player.Entity)
                    .Where(entity => !_attackedEntities[entityId].Contains(entity.EntityId)))
            {
                bool attacked = damageType.TryAttack(player, entity, out int collider, out Vector3 point, out MeleeDamagePacket packet);

                if (!attacked) continue;

                _entitiesCollisionsBuffer.Add((entity, point));
                damagePackets.Add(packet);

                if (damageType.DurabilityDamage > 0)
                {
                    slot.Itemstack.Collectible.DamageItem(_api.World, player.Entity, slot, damageType.DurabilityDamage);
                }
                if (StopOnEntityHit) break;
            }
        }

        packets = damagePackets.ToImmutableArray();

        IEnumerable<(Entity entity, Vector3 point)> result = _entitiesCollisionsBuffer.ToImmutableHashSet();
        _entitiesCollisionsBuffer.Clear();
        return result;
    }
}