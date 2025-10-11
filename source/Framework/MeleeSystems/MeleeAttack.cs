using CombatOverhaul.Colliders;
using CombatOverhaul.Implementations;
using CombatOverhaul.Utils;
using OpenTK.Mathematics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace CombatOverhaul.MeleeSystems;

public class MeleeAttackStats
{
    public bool StopOnTerrainHit { get; set; } = false;
    public bool StopOnEntityHit { get; set; } = false;
    public bool CollideWithTerrain { get; set; } = true;
    public bool HitOnlyOneEntity { get; set; } = false;
    public float MaxReach { get; set; } = 6;

    public MeleeDamageTypeJson[] DamageTypes { get; set; } = Array.Empty<MeleeDamageTypeJson>();
}

public sealed class MeleeAttack
{
    public MeleeDamageType[] DamageTypes { get; }

    public bool StopOnTerrainHit { get; set; }
    public bool StopOnEntityHit { get; set; }
    public bool CollideWithTerrain { get; set; }
    public bool HitOnlyOneEntity { get; set; } = false;
    public float MaxReach { get; set; }

    public MeleeAttack(ICoreClientAPI api, MeleeAttackStats stats)
    {
        _api = api;
        StopOnTerrainHit = stats.StopOnTerrainHit;
        StopOnEntityHit = stats.StopOnEntityHit;
        CollideWithTerrain = stats.CollideWithTerrain;
        HitOnlyOneEntity = stats.HitOnlyOneEntity;
        MaxReach = stats.MaxReach;
        DamageTypes = stats.DamageTypes.Select(stats => stats.ToDamageType()).ToArray();

        _meleeSystem = api.ModLoader.GetModSystem<CombatOverhaulSystem>().ClientMeleeSystem ?? throw new Exception();
        _combatOverhaulSystem = _api.ModLoader.GetModSystem<CombatOverhaulSystem>();
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

        LineSegmentCollider.ResetPreviousColliders(DamageTypes.Select(element => element as IHasLineCollider));
    }

    public void Start(IPlayer player, ItemStackMeleeWeaponStats stats)
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

        LineSegmentCollider.ResetPreviousColliders(DamageTypes.Select(element => element as IHasLineCollider));
    }

    public void Attack(IPlayer player, ItemSlot slot, bool mainHand, out IEnumerable<(Block block, Vector3d point)> terrainCollisions, out IEnumerable<(Entity entity, Vector3d point)> entitiesCollisions)
    {
        Attack(player, slot, mainHand, out terrainCollisions, out entitiesCollisions, new());
    }
    public void Attack(IPlayer player, ItemSlot slot, bool mainHand, out IEnumerable<(Block block, Vector3d point)> terrainCollisions, out IEnumerable<(Entity entity, Vector3d point)> entitiesCollisions, ItemStackMeleeWeaponStats stats)
    {
        

        terrainCollisions = Array.Empty<(Block block, Vector3d point)>();
        entitiesCollisions = Array.Empty<(Entity entity, Vector3d point)>();

        PrepareColliders(player, slot, mainHand);

#if DEBUG
        var pos1 = DamageTypes[0].InWorldCollider.Position;
        var pos3 = DamageTypes[0].InWorldCollider.Position + DamageTypes[0].InWorldCollider.Direction;
        Vec3d pos2 = new(pos1.X, pos1.Y, pos1.Z);
        Vec3d pos4 = new(pos3.X, pos3.Y, pos3.Z);
        float c = 16;
        for (int i = 0; i < c; i++)
        {
            Vec3d pos5 = pos2 + (i / c) * (pos4 - pos2);
            //player.Entity.Api.World.SpawnParticles(1, ColorUtil.ColorFromRgba((int)(255 * i / c), (int)(255 * i / c), (int)(255 * i / c), 255), pos5, pos5, new Vec3f(), new Vec3f(), 1, 0, 0.3f, EnumParticleModel.Cube);
        }
#endif

        double parameter = 1f;

        if (CollideWithTerrain)
        {
            bool collidedWithTerrain = TryCollideWithTerrain(out terrainCollisions, out parameter);

            if (collidedWithTerrain) return;
        }

        TryAttackEntities(player, slot, out entitiesCollisions, mainHand, parameter, stats);

#if DEBUG
        foreach (var entyry in entitiesCollisions)
        {
            var pos6 = entyry.point;
            Vec3d pos7 = new(pos6.X, pos6.Y, pos6.Z);
            //player.Entity.Api.World.SpawnParticles(1, ColorUtil.ColorFromRgba(255, 0, 0, 125), pos7, pos7, new Vec3f(), new Vec3f(), 1, 0, 1.0f, EnumParticleModel.Cube);
        }
#endif
    }
    public void PrepareColliders(IPlayer player, ItemSlot slot, bool mainHand)
    {
        LineSegmentCollider.Transform(DamageTypes.Select(element => element as IHasLineCollider), player.Entity, slot, _api, mainHand);
    }
    public bool TryCollideWithTerrain(out IEnumerable<(Block block, Vector3d point)> terrainCollisions, out double parameter)
    {
        terrainCollisions = CheckTerrainCollision(out parameter);

        return terrainCollisions.Any();
    }
    public bool TryAttackEntities(IPlayer player, ItemSlot slot, out IEnumerable<(Entity entity, Vector3d point)> entitiesCollisions, bool mainHand, double maximumParameter, ItemStackMeleeWeaponStats stats)
    {
        entitiesCollisions = CollideWithEntities(player, out IEnumerable<MeleeDamagePacket> damagePackets, mainHand, maximumParameter, stats);

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
    public void MergeAttackedEntities(MeleeAttack attack)
    {
        foreach (long entityId in _attackedEntities.Keys)
        {
            foreach (long id in _attackedEntities[entityId])
            {
                attack._attackedEntities[entityId].Add(id);
            }
            foreach (long id in attack._attackedEntities[entityId])
            {
                _attackedEntities[entityId].Add(id);
            }
        }
    }
    public void AddAttackedEntities(MeleeAttack attack)
    {
        foreach (long entityId in _attackedEntities.Keys)
        {
            foreach (long id in attack._attackedEntities[entityId])
            {
                _attackedEntities[entityId].Add(id);
            }
        }
    }

    private readonly ICoreClientAPI _api;
    private readonly Dictionary<long, HashSet<long>> _attackedEntities = new();
    private readonly MeleeSystemClient _meleeSystem;
    private readonly CombatOverhaulSystem _combatOverhaulSystem;

    private IEnumerable<(Block block, Vector3d point)> CheckTerrainCollision(out double parameter)
    {
        List<(Block block, Vector3d point)> terrainCollisions = new();

        parameter = 1f;

        foreach (MeleeDamageType damageType in DamageTypes)
        {
            (Block block, Vector3d position, double parameter)? result = damageType.InWorldCollider.IntersectTerrain(_api);

            if (result != null)
            {
                terrainCollisions.Add((result.Value.block, result.Value.position));
                if (result.Value.parameter < parameter) parameter = result.Value.parameter;
            }
        }

        return terrainCollisions;
    }
    private IEnumerable<(Entity entity, Vector3d point)> CollideWithEntities(IPlayer player, out IEnumerable<MeleeDamagePacket> packets, bool mainHand, double maximumParameter, ItemStackMeleeWeaponStats stats)
    {
        long entityId = player.Entity.EntityId;
        long mountedOn = player.Entity.MountedOn?.Entity?.EntityId ?? 0;

        if (!_attackedEntities.ContainsKey(entityId))
        {
            _attackedEntities.Add(entityId, new());
        }

        if (_attackedEntities[entityId].Count > 0 && HitOnlyOneEntity)
        {
            packets = Array.Empty<MeleeDamagePacket>();
            return Array.Empty<(Entity entity, Vector3d point)>();
        }

        Entity[] entities = _api.World.GetEntitiesAround(player.Entity.Pos.XYZ, _combatOverhaulSystem.Settings.CollisionRadius + MaxReach, _combatOverhaulSystem.Settings.CollisionRadius + MaxReach);

        List<(Entity entity, Vector3d point)> entitiesCollisions = new();

        List<MeleeDamagePacket> damagePackets = new();

        foreach (MeleeDamageType damageType in DamageTypes)
        {
            bool attacked = false;

            foreach (Entity entity in entities
                    .Where(entity => entity.IsCreature)
                    .Where(entity => entity.Alive)
                    .Where(entity => entity.EntityId != entityId && entity.EntityId != mountedOn)
                    .Where(entity => !_attackedEntities[entityId].Contains(entity.EntityId)))
            {
                attacked = damageType.TryAttack(player, entity, out string collider, out Vector3d point, out MeleeDamagePacket packet, mainHand, maximumParameter, stats);

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