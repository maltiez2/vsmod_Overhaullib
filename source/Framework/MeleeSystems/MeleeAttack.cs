using CollidersLib;
using CollidersLib.Items;
using CombatOverhaul.Implementations;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace CombatOverhaul.MeleeSystems;

public class MeleeAttackStats
{
    public bool StopOnTerrainHit { get; set; } = false;
    public bool StopOnEntityHit { get; set; } = false;
    public bool CollideWithTerrain { get; set; } = true;
    public bool HitOnlyOneEntity { get; set; } = false;

    public MeleeDamageStatsJson[] DamageStats { get; set; } = [];
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class MeleeCollisionPacket
{
    public int PushTier { get; set; }
    public string Collider { get; set; } = "";
    public int ColliderType { get; set; }
    public long AttackerEntityId { get; set; }
    public long TargetEntityId { get; set; }
    public bool MainHand { get; set; }
}

public sealed class MeleeAttack
{
    public MeleeDamageStats[] DamageStats { get; }

    public bool StopOnTerrainHit { get; set; }
    public bool StopOnEntityHit { get; set; }
    public bool CollideWithTerrain { get; set; }
    public bool HitOnlyOneEntity { get; set; } = false;

    public MeleeAttack(ICoreClientAPI api, MeleeAttackStats stats, ItemCollidersBehaviorClient collidersBehavior)
    {
        StopOnTerrainHit = stats.StopOnTerrainHit;
        StopOnEntityHit = stats.StopOnEntityHit;
        CollideWithTerrain = stats.CollideWithTerrain;
        HitOnlyOneEntity = stats.HitOnlyOneEntity;
        DamageStats = stats.DamageStats.Select(stats => stats.ToDamageType()).ToArray();

        _collidersUsed = DamageStats.Select(stats => stats.Collider).Distinct().ToArray();
        _meleeSystem = api.ModLoader.GetModSystem<CombatOverhaulSystem>().ClientMeleeSystem ?? throw new Exception();
        _collidersBehavior = collidersBehavior;
    }

    public void Reset(EntityPlayer attacker, ItemSlot weaponSlot)
    {
        _attackedEntities.Clear();

        _collidersBehavior.ResetColliders(attacker, weaponSlot);
    }

    public void TryAttack(EntityPlayer attacker, ItemSlot weaponSlot, bool mainHand, ItemStackMeleeWeaponStats stats, out List<SingleCollisionData> collisions, out bool stopAttack, bool ignoreTerrainBehind)
    {
        _collidersBehavior.CheckCollisions(attacker, weaponSlot, out List<ItemColliderCollisionData> collisionsPerCollider, collidersToCheck: _collidersUsed);

        List<SingleCollisionData> collisionsSorted = _collidersBehavior.SortCollisions(attacker, collisionsPerCollider, DamageStats.ToDictionary(entry => entry.Collider, entry => entry.ColliderPriority), ignoreTerrainBehind);

        collisions = ValidateCollisions(collisionsSorted, out stopAttack);

        List<MeleeDamagePacket> packets = CollectDamagePackets(attacker, mainHand, stats, collisionsSorted);

        if (packets.Count > 0)
        {
            _meleeSystem.SendPackets(packets);
        }
    }



    private readonly ItemCollidersBehaviorClient _collidersBehavior;
    private readonly HashSet<long> _attackedEntities = [];
    private readonly MeleeSystemClient _meleeSystem;
    private readonly int[] _collidersUsed;

    private List<SingleCollisionData> ValidateCollisions(List<SingleCollisionData> collisionsSorted, out bool stopAttack)
    {
        List<SingleCollisionData> collisions = [];
        stopAttack = false;

        foreach (SingleCollisionData collision in collisionsSorted)
        {
            if (collision.BehindTerrain)
            {
                continue;
            }
            
            if (collision.TerrainCollision != null && CollideWithTerrain)
            {
                collisions.Add(collision);
                if (StopOnTerrainHit)
                {
                    stopAttack = true;
                    break;
                }
            }
            else if (collision.Target != null && collision.EntityCollision != null)
            {
                if (HitOnlyOneEntity && _attackedEntities.Count > 0)
                {
                    continue;
                }

                if (_attackedEntities.Contains(collision.Target.EntityId))
                {
                    continue;
                }

                collisions.Add(collision);
                _attackedEntities.Add(collision.Target.EntityId);
                if (StopOnEntityHit)
                {
                    stopAttack = true;
                    break;
                }
            }
        }

        return collisions;
    }

    private List<MeleeDamagePacket> CollectDamagePackets(EntityPlayer attacker, bool mainHand, ItemStackMeleeWeaponStats stats, List<SingleCollisionData> collisionsSorted)
    {
        List<MeleeDamagePacket> packets = [];

        foreach (SingleCollisionData collision in collisionsSorted)
        {
            if (collision.Target == null || collision.EntityCollision == null)
            {
                continue;
            }

            MeleeDamageStats damageStat = DamageStats.First(stat => stat.Collider == collision.ColliderIndex);
            EntityWithCapsuleIntersectionData entityCollision = collision.EntityCollision.Value;

            bool attacked = damageStat.TryAttack(attacker, collision.Target, entityCollision.IntersectionPoint, entityCollision.EntityCollider?.ShapeElementName ?? "", out MeleeDamagePacket packet, mainHand, stats);
            if (attacked)
            {
                packets.Add(packet);
            }
        }

        return packets;
    }
}
