using CollidersLib;
using CollidersLib.Items;
using CombatOverhaul.Implementations;
using OpenTK.Mathematics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

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

    public MeleeAttack(ICoreClientAPI api, MeleeAttackStats stats, ItemCollidersBehaviorClient collidersBehavior)
    {
        _api = api;
        StopOnTerrainHit = stats.StopOnTerrainHit;
        StopOnEntityHit = stats.StopOnEntityHit;
        CollideWithTerrain = stats.CollideWithTerrain;
        HitOnlyOneEntity = stats.HitOnlyOneEntity;
        MaxReach = stats.MaxReach;
        DamageTypes = stats.DamageTypes.Select(stats => stats.ToDamageType()).ToArray();

        _collidersUsed = DamageTypes.Select(stats => stats.Collider).Distinct().ToArray();
        _meleeSystem = api.ModLoader.GetModSystem<CombatOverhaulSystem>().ClientMeleeSystem ?? throw new Exception();
        _combatOverhaulSystem = _api.ModLoader.GetModSystem<CombatOverhaulSystem>();
        _collidersBehavior = collidersBehavior;
    }

    public void Start(IPlayer player, bool mainHand)
    {
        long entityId = player.Entity.EntityId;

        if (_attackedEntities.TryGetValue(entityId, out HashSet<long>? value))
        {
            value.Clear();
        }
        else
        {
            _attackedEntities[entityId] = [];
        }

        _hitPlayer = false;

        foreach (int colliderIndex in _collidersUsed)
        {
            _collidersBehavior.Colliders[colliderIndex].TransformCollider(player.Entity, mainHand, resetPreviousCollider: true);
        }
    }

    public bool Attack(IPlayer player, ItemSlot slot, bool mainHand, out IEnumerable<(Block block, Vector3d point)> terrainCollisions, out IEnumerable<(Entity entity, Vector3d point)> entitiesCollisions, ItemStackMeleeWeaponStats stats)
    {
        terrainCollisions = Array.Empty<(Block block, Vector3d point)>();
        entitiesCollisions = Array.Empty<(Entity entity, Vector3d point)>();

        PrepareColliders(player, slot, mainHand);

        double parameter = 1f;

        _ = TryCollideWithTerrain(out terrainCollisions, out parameter);

        bool attacked = TryAttackEntities(player, slot, out entitiesCollisions, mainHand, parameter, stats);

        /*if (_combatOverhaulSystem.Settings.DebugHitParticles)
        {
            foreach ((Entity entity, Vector3d point) entry in entitiesCollisions)
            {
                Vector3d pos6 = entry.point;
                Vec3d pos7 = new(pos6.X, pos6.Y, pos6.Z);
                if (attacked)
                {
                    player.Entity.Api.World.SpawnParticles(1, ColorUtil.ColorFromRgba(0, 0, 255, 200), pos7, pos7, new Vec3f(), new Vec3f(), 1, 0, 1.0f, EnumParticleModel.Cube);
                }
                else
                {
                    player.Entity.Api.World.SpawnParticles(1, ColorUtil.ColorFromRgba(255, 255, 255, 64), pos7, pos7, new Vec3f(), new Vec3f(), 1, 0, 1.0f, EnumParticleModel.Cube);
                }
                    
            }
        }*/

        return attacked;
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
        entitiesCollisions = CollideWithEntities(player, out IEnumerable<MeleeDamagePacket> damagePackets, out IEnumerable<MeleeCollisionPacket> collisions, mainHand, maximumParameter, stats);

        if (damagePackets.Any()) _meleeSystem.SendPackets(damagePackets);
        if (collisions.Any()) _meleeSystem.SendPackets(collisions);

        return damagePackets.Any();
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
    public void AddAttackedEntities(MeleeAttack attack, long currentEntityId)
    {
        foreach (long id in attack._attackedEntities[currentEntityId])
        {
            _attackedEntities[currentEntityId].Add(id);
        }
    }

    private readonly ICoreClientAPI _api;
    private readonly ItemCollidersBehaviorClient _collidersBehavior;
    private readonly Dictionary<long, HashSet<long>> _attackedEntities = new();
    private readonly MeleeSystemClient _meleeSystem;
    private readonly CombatOverhaulSystem _combatOverhaulSystem;
    private readonly int[] _collidersUsed;
    private bool _hitPlayer = false;
}