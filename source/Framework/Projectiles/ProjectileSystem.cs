using OpenTK.Mathematics;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace CombatOverhaul.RangedSystems;


public readonly struct ItemStackProjectileStats
{
    public readonly float DamageMultiplier;
    public readonly int DamageTierBonus;
    public readonly float KnockbackMultiplier;
    public readonly float DropChanceMultiplier;
    public readonly float PenetrationBonus;
    public readonly int AdditionalDurabilityCost;

    public ItemStackProjectileStats()
    {
        DamageMultiplier = 1;
        DamageTierBonus = 0;
        KnockbackMultiplier = 1;
        DropChanceMultiplier = 1;
        PenetrationBonus = 0;
        AdditionalDurabilityCost = 0;
    }

    public ItemStackProjectileStats(float damageMultiplier, int damageTierBonus, float knockbackMultiplier, float dropChanceMultiplier, float penetrationBonus, int additionalDurabilityCost)
    {
        DamageMultiplier = damageMultiplier;
        DamageTierBonus = damageTierBonus;
        KnockbackMultiplier = knockbackMultiplier;
        DropChanceMultiplier = dropChanceMultiplier;
        PenetrationBonus = penetrationBonus;
        AdditionalDurabilityCost = additionalDurabilityCost;
    }

    public static ItemStackProjectileStats FromItemStack(ItemStack stack)
    {
        float damageMultiplier = stack.Attributes.GetFloat("damageMultiplier", 1);
        int damageTierBonus = stack.Attributes.GetInt("damageTierBonus", 0);
        float knockbackMultiplier = stack.Attributes.GetFloat("knockbackMultiplier", 1);
        float dropChanceMultiplier = stack.Attributes.GetFloat("dropChanceMultiplier", 1);
        float penetrationBonus = stack.Attributes.GetFloat("penetrationBonus", 0);
        int additionalDurabilityCost = stack.Attributes.GetInt("additionalDurabilityCost", 0);

        return new ItemStackProjectileStats(damageMultiplier, damageTierBonus, knockbackMultiplier, dropChanceMultiplier, penetrationBonus, additionalDurabilityCost);
    }
}

public struct ProjectileSpawnStats
{
    public long ProducerEntityId { get; set; }
    public float DamageMultiplier { get; set; }
    public int DamageTier { get; set; }
    public Vector3d Position { get; set; }
    public Vector3d Velocity { get; set; }
}


public sealed class ProjectileSystemServer
{
    public delegate void RangedDamageDelegate(Entity target, DamageSource damageSource, ItemStack? weaponStack, ref float damage);

    public event RangedDamageDelegate? OnDealRangedDamage;

    public ProjectileSystemServer(ICoreServerAPI api)
    {
        _api = api;
    }

    public const string NetworkChannelId = "CombatOverhaul:projectiles";

    public void Spawn(Guid id, ProjectileStats projectileStats, ProjectileSpawnStats spawnStats, ItemStack projectileStack, ItemStack? weaponStack, Entity shooter)
    {
        Spawn(id, projectileStats, spawnStats, projectileStack, weaponStack, shooter, shooter);
    }
    public void Spawn(Guid id, ProjectileStats projectileStats, ProjectileSpawnStats spawnStats, ItemStack projectileStack, ItemStack? weaponStack, Entity shooter, Entity target)
    {
        EntityPlayer? owner = (shooter as EntityPlayer) ??
            (target as EntityPlayer) ??
            _api.World.GetNearestEntity(target.Pos.XYZ, _nearestPlayerSearchRange, _nearestPlayerSearchRange, entity => entity is EntityPlayer) as EntityPlayer;

        if (owner == null)
        {
            return;
        }

        SpawnProjectile(id, projectileStack, weaponStack, projectileStats, spawnStats, _api, shooter, owner, out ProjectileEntity? projectile);

        if (projectile != null)
        {
            _projectiles.Add(id, new(projectile, projectileStats, spawnStats, _api, ClearId, projectileStack));
            projectile.ServerProjectile = _projectiles[id];
        }
    }

    public void OnDealDamage(Entity target, DamageSource damageSource, ItemStack? weaponStack, ref float damage)
    {
        OnDealRangedDamage?.Invoke(target, damageSource, weaponStack, ref damage);
    }

    private readonly ICoreServerAPI _api;
    private readonly Dictionary<Guid, ProjectileServer> _projectiles = new();
    private const float _nearestPlayerSearchRange = 300;

    private static void SpawnProjectile(Guid id, ItemStack projectileStack, ItemStack? weaponStack, ProjectileStats stats, ProjectileSpawnStats spawnStats, ICoreAPI api, Entity shooter, Entity owner, out ProjectileEntity? projectile)
    {
        AssetLocation entityTypeAsset = new(stats.EntityCode);

        EntityProperties? entityType = api.World.GetEntityType(entityTypeAsset) ?? throw new InvalidOperationException($"[Overhaul lib] Unable to create entity '{entityTypeAsset}'");

        Entity entity = api.ClassRegistry.CreateEntity(entityType) ?? throw new InvalidOperationException($"[Overhaul lib] Unable to create entity '{entityTypeAsset}'");

        entity.ServerPos.SetPos(new Vec3d(spawnStats.Position.X, spawnStats.Position.Y, spawnStats.Position.Z));
        entity.ServerPos.Motion.Set(new Vec3d(spawnStats.Velocity.X, spawnStats.Velocity.Y, spawnStats.Velocity.Z));
        entity.Pos.SetFrom(entity.ServerPos);
        entity.World = api.World;

        projectile = entity as ProjectileEntity;
        if (projectile != null)
        {
            projectile.ProjectileId = id;
            projectile.ProjectileStack = projectileStack;
            projectile.WeaponStack = weaponStack;
            projectile.DropOnImpactChance = stats.DropChance;
            projectile.ColliderRadius = stats.CollisionRadius;
            projectile.PenetrationDistance = stats.PenetrationDistance;
            projectile.PenetrationStrength = Math.Max(0, stats.PenetrationBonus + spawnStats.DamageTier);
            projectile.DurabilityDamageOnImpact = stats.DurabilityDamage;
            projectile.ShooterId = shooter.EntityId;
            projectile.OwnerId = owner.EntityId;
            projectile.CanBeCollected = stats.CanBeCollected;
            projectile.IgnoreInvFrames = true;

            projectile.SetRotation();
        }
        else if (entity is IProjectile vanillaProjectile)
        {
            vanillaProjectile.FiredBy = shooter;
            vanillaProjectile.Damage = stats.DamageStats.Damage * spawnStats.DamageMultiplier;
            vanillaProjectile.DamageTier = spawnStats.DamageTier + stats.DamageTierBonus;
            vanillaProjectile.ProjectileStack = projectileStack;
            vanillaProjectile.WeaponStack = weaponStack;
            vanillaProjectile.DropOnImpactChance = stats.DropChance;
            vanillaProjectile.DamageStackOnImpact = stats.DurabilityDamage > 0;
            vanillaProjectile.Weight = entity.Properties.Weight;
            vanillaProjectile.IgnoreInvFrames = true;
            vanillaProjectile.Collectible = stats.CanBeCollected;

            vanillaProjectile.PreInitialize();
        }

        api.World.SpawnEntity(entity);
    }
    private void ClearId(Guid id) => _projectiles.Remove(id);
}
