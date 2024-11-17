using CombatOverhaul.Colliders;
using System.Diagnostics;
using System.Numerics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace CombatOverhaul.MeleeSystems;

public class MeleeAttackStats
{
    public bool StopOnTerrainHit { get; set; } = false;
    public bool StopOnEntityHit { get; set; } = false;
    public bool CollideWithTerrain { get; set; } = true;
    public float MaxReach { get; set; } = 6;

    public MeleeDamageTypeJson[] DamageTypes { get; set; } = Array.Empty<MeleeDamageTypeJson>();
}

public sealed class MeleeAttack
{
    public MeleeDamageType[] DamageTypes { get; }

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
        DamageTypes = stats.DamageTypes.Select(stats => stats.ToDamageType()).ToArray();

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

        float parameter = 1f;

        if (CollideWithTerrain)
        {
            bool collidedWithTerrain = TryCollideWithTerrain(out terrainCollisions, out parameter);

            if (collidedWithTerrain && StopOnTerrainHit) return;
        }

        TryAttackEntities(player, slot, out entitiesCollisions, mainHand, parameter);
    }
    public void PrepareColliders(IPlayer player, ItemSlot slot, bool mainHand)
    {
        LineSegmentCollider.Transform(DamageTypes.Select(element => element as IHasLineCollider), player.Entity, slot, _api, mainHand);
    }
    public bool TryCollideWithTerrain(out IEnumerable<(Block block, Vector3 point)> terrainCollisions, out float parameter)
    {
        terrainCollisions = CheckTerrainCollision(out parameter);

        return terrainCollisions.Any();
    }
    public bool TryAttackEntities(IPlayer player, ItemSlot slot, out IEnumerable<(Entity entity, Vector3 point)> entitiesCollisions, bool mainHand, float maximumParameter)
    {
        entitiesCollisions = CollideWithEntities(player, out IEnumerable<MeleeDamagePacket> damagePackets, mainHand, maximumParameter);

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
    private readonly MeleeSystemClient _meleeSystem;

    private IEnumerable<(Block block, Vector3 point)> CheckTerrainCollision(out float parameter)
    {
        List<(Block block, Vector3 point)> terrainCollisions = new();

        parameter = 1f;

        foreach (MeleeDamageType damageType in DamageTypes)
        {
            (Block block, Vector3 position, float parameter)? result = damageType.InWorldCollider.IntersectTerrain(_api);

            if (result != null)
            {
                terrainCollisions.Add((result.Value.block, result.Value.position));
                if (result.Value.parameter < parameter) parameter = result.Value.parameter;
            }
        }

        return terrainCollisions;
    }
    private IEnumerable<(Entity entity, Vector3 point)> CollideWithEntities(IPlayer player, out IEnumerable<MeleeDamagePacket> packets, bool mainHand, float maximumParameter)
    {
        long entityId = player.Entity.EntityId;
        long mountedOn = player.Entity.MountedOn?.Entity?.EntityId ?? 0;

        Entity[] entities = _api.World.GetEntitiesAround(player.Entity.Pos.XYZ, MaxReach, MaxReach);

        List<(Entity entity, Vector3 point)> entitiesCollisions = new();

        List<MeleeDamagePacket> damagePackets = new();

        foreach (MeleeDamageType damageType in DamageTypes)
        {
            bool attacked = false;

            foreach (Entity entity in entities
                    .Where(entity => entity.EntityId != entityId && entity.EntityId != mountedOn)
                    .Where(entity => _attackedEntities.ContainsKey(entityId))
                    .Where(entity => !_attackedEntities[entityId].Contains(entity.EntityId)))
            {
                attacked = damageType.TryAttack(player, entity, out string collider, out Vector3 point, out MeleeDamagePacket packet, mainHand, maximumParameter);

                if (!attacked) continue;

                entitiesCollisions.Add((entity, point));
                damagePackets.Add(packet);

                _attackedEntities[entityId].Add(entity.EntityId);

                if (StopOnEntityHit) break;
            }

            if (attacked && StopOnEntityHit) break;
        }

        packets = damagePackets;

        return entitiesCollisions;
    }
}