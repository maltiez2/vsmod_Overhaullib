using CollidersLib;
using CollidersLib.Items;
using CombatOverhaul.Implementations;
using ProtoBuf;
using System.Diagnostics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace CombatOverhaul.MeleeSystems;

public class MeleeAttackStats
{
    public bool StopOnTerrainHit { get; set; } = false;
    public bool StopOnEntityHit { get; set; } = false;
    public bool CollideWithTerrain { get; set; } = true;
    public bool HitOnlyOneEntity { get; set; } = false;

    public MeleeDamageStatsJson[] DamageStats { get; set; } = [];
    public string[] DamageStatsTemplates { get; set; } = [];
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

    public MeleeAttack(ICoreClientAPI api, MeleeAttackStats stats, Dictionary<string, MeleeDamageStatsJson> damageStatsTemplates, ItemCollidersBehaviorClient collidersBehavior)
    {
        StopOnTerrainHit = stats.StopOnTerrainHit;
        StopOnEntityHit = stats.StopOnEntityHit;
        CollideWithTerrain = stats.CollideWithTerrain;
        HitOnlyOneEntity = stats.HitOnlyOneEntity;

        IEnumerable<MeleeDamageStats> damageStats = stats.DamageStatsTemplates.Select(code => damageStatsTemplates[code]).Select(stats => stats.ToDamageType());

        DamageStats = stats.DamageStats.Select(stats => stats.ToDamageType()).Concat(damageStats).ToArray();

        _collidersUsed = DamageStats.Select(stats => stats.Collider).Distinct().ToArray();
        _meleeSystem = api.ModLoader.GetModSystem<CombatOverhaulSystem>().ClientMeleeSystem ?? throw new Exception();
        _collidersBehavior = collidersBehavior;
    }

    public void Reset(EntityPlayer attacker, ItemSlot weaponSlot)
    {
        _attackedEntities.Clear();

        _collidersBehavior.ResetColliders(attacker, weaponSlot);
    }

    public void TryAttack(EntityPlayer attacker, ItemSlot weaponSlot, bool mainHand, ItemStackMeleeWeaponStats stats, out List<SingleItemCollisionData> collisions, out bool stopAttack, bool ignoreTerrainBehind)
    {
        List<SingleItemCollisionData> collisionsSorted = _collidersBehavior.CheckForCollisionsInOrder(attacker, weaponSlot, [0, 1], ignoreTerrainBehind);

        collisions = ValidateCollisions(collisionsSorted, out stopAttack, attacker.Api);

        List<MeleeDamagePacket> packets = CollectDamagePackets(attacker, mainHand, stats, collisions);

        if (packets.Count > 0)
        {
            _meleeSystem.SendPackets(packets);
        }
    }



    private readonly ItemCollidersBehaviorClient _collidersBehavior;
    private readonly HashSet<long> _attackedEntities = [];
    private readonly MeleeSystemClient _meleeSystem;
    private readonly int[] _collidersUsed;

    private static (double collider, double time, double distanceFromTail) ReversePriority(double priority)
    {
        const double step = 1000;

        double scaled = priority * (step * step * step);

        double time = Math.Truncate(scaled / (step * step));
        scaled -= time * (step * step);

        double collider = Math.Truncate(scaled / step);
        scaled -= collider * step;

        double distanceFromTail = scaled;

        return (collider, time, distanceFromTail);
    }

    private static string PrintPriority(double priority)
    {
        (double collider, double time, double distanceFromTail) = ReversePriority(priority);

        return $"{time:F2}|{collider}|{distanceFromTail:F2}";
    }


    private List<SingleItemCollisionData> ValidateCollisions(List<SingleItemCollisionData> collisionsSorted, out bool stopAttack, ICoreAPI api)
    {
        List<SingleItemCollisionData> collisions = [];
        stopAttack = false;
        bool hitTerrain = false;

        /*string output = "Before: ";
        foreach (SingleItemCollisionData collision in collisionsSorted)
        {
            //if (collision.ColliderIndex == 0) Debug.Write($"t({collision.ColliderIndex} : {collision.DistanceFromTail})\t");
            string type = collision.TerrainCollision != null ? "t" : "e";
            string behind = collision.BehindTerrain ? "b" : "-";
            output += $"{type}{behind}({collision.ColliderIndex} : {collision.DistanceFromTail:F2})\t";
            if (collision.TerrainCollision != null)
            {
                //Debug.Write($"t({collision.Priority * 1000:F12})\t");
                Vec3d pos8 = new(collision.TerrainCollision.Value.IntersectionPoint.X, collision.TerrainCollision.Value.IntersectionPoint.Y, collision.TerrainCollision.Value.IntersectionPoint.Z);
                //api.World.SpawnParticles(1, ColorUtil.ColorFromRgba(0, 255, 0, 125), pos8, pos8, new Vec3f(), new Vec3f(), 1, 0, 1.0f, EnumParticleModel.Cube);
            }
            else if (collision.EntityCollision != null)
            {
                //Debug.Write($"e({collision.Priority * 1000:F12})\t");
                Vec3d pos8 = new(collision.EntityCollision.Value.IntersectionPoint.X, collision.EntityCollision.Value.IntersectionPoint.Y, collision.EntityCollision.Value.IntersectionPoint.Z);
                //api.World.SpawnParticles(1, ColorUtil.ColorFromRgba(255, 0, 0, 125), pos8, pos8, new Vec3f(), new Vec3f(), 1, 0, 1.0f, EnumParticleModel.Cube);
            }
        }
        Debug.WriteLine(output);*/

        foreach (SingleItemCollisionData collision in collisionsSorted)
        {
            if (collision.EntityCollision != null && collision.BehindTerrain)
            {
                continue;
            }

            if (collision.TerrainCollision != null && CollideWithTerrain)
            {
                collisions.Add(collision);
                hitTerrain = true;
                if (StopOnTerrainHit)
                {
                    stopAttack = true;
                    break;
                }
            }
            else if (!hitTerrain && collision.Target != null && collision.EntityCollision != null)
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

        /*string output2 = "After: ";
        foreach (SingleItemCollisionData collision in collisions)
        {
            //if (collision.ColliderIndex == 0) Debug.Write($"t({collision.ColliderIndex} : {collision.DistanceFromTail})\t");
            string type = collision.TerrainCollision != null ? "t" : "e";
            string behind = collision.BehindTerrain ? "b" : "-";
            output2 += $"{type}{behind}({collision.ColliderIndex} : {collision.DistanceFromTail:F2})\t";
            if (collision.TerrainCollision != null)
            {
                //Debug.Write($"t({collision.Priority * 1000:F12})\t");
                Vec3d pos8 = new(collision.TerrainCollision.Value.IntersectionPoint.X, collision.TerrainCollision.Value.IntersectionPoint.Y, collision.TerrainCollision.Value.IntersectionPoint.Z);
                api.World.SpawnParticles(1, ColorUtil.ColorFromRgba(0, 255, 0, 125), pos8, pos8, new Vec3f(), new Vec3f(), 1, 0, 1.0f, EnumParticleModel.Cube);
            }
            else if (collision.EntityCollision != null)
            {
                //Debug.Write($"e({collision.Priority * 1000:F12})\t");
                Vec3d pos8 = new(collision.EntityCollision.Value.IntersectionPoint.X, collision.EntityCollision.Value.IntersectionPoint.Y, collision.EntityCollision.Value.IntersectionPoint.Z);
                api.World.SpawnParticles(1, ColorUtil.ColorFromRgba(255, 0, 0, 125), pos8, pos8, new Vec3f(), new Vec3f(), 1, 0, 1.0f, EnumParticleModel.Cube);
            }
        }
        Debug.WriteLine(output2);*/


        return collisions;
    }

    private List<MeleeDamagePacket> CollectDamagePackets(EntityPlayer attacker, bool mainHand, ItemStackMeleeWeaponStats stats, List<SingleItemCollisionData> collisionsSorted)
    {
        List<MeleeDamagePacket> packets = [];

        foreach (SingleItemCollisionData collision in collisionsSorted)
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
