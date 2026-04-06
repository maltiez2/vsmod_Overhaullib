using CombatOverhaul.DamageSystems;
using CombatOverhaul.Implementations;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace CombatOverhaul.RangedSystems;

public class ProjectileStats
{
    public int AdditionalDurabilityCost { get; set; } = 0;
    public string ImpactSound { get; set; } = "game:sounds/arrow-impact";
    public string HitSound { get; set; } = "game:sounds/player/projectilehit";
    public float CollisionRadius { get; set; } = 0;
    public float PenetrationDistance { get; set; } = 0;
    public ProjectileDamageDataJson DamageStats { get; set; } = new();
    public int DamageTierBonus { get; set; } = 0;
    public float SpeedThreshold { get; set; } = 0;
    public float Knockback { get; set; } = 0;
    public string EntityCode { get; set; } = "";
    public int DurabilityDamage { get; set; } = 0;
    public float DropChance { get; set; } = 0;
    public float PenetrationBonus { get; set; } = 0;
    public bool CanBeCollected { get; set; } = true;

    public ProjectileStats() { }

    public ProjectileStats(int additionalDurabilityCost, string impactSound, string hitSound, float collisionRadius, float penetrationDistance, ProjectileDamageDataJson damageStats, int damageTierBonus, float speedThreshold, float knockback, string entityCode, int durabilityDamage, float dropChance, float penetrationBonus, bool canBeCollected)
    {
        AdditionalDurabilityCost = additionalDurabilityCost;
        ImpactSound = impactSound;
        HitSound = hitSound;
        CollisionRadius = collisionRadius;
        PenetrationDistance = penetrationDistance;
        DamageStats = damageStats;
        DamageTierBonus = damageTierBonus;
        SpeedThreshold = speedThreshold;
        Knockback = knockback;
        EntityCode = entityCode;
        DurabilityDamage = durabilityDamage;
        DropChance = dropChance;
        PenetrationBonus = penetrationBonus;
        CanBeCollected = canBeCollected;
    }

    public ProjectileStats Clone()
    {
        return new ProjectileStats(AdditionalDurabilityCost, ImpactSound, HitSound, CollisionRadius, PenetrationDistance, new ProjectileDamageDataJson() { Damage = DamageStats.Damage, DamageType = DamageStats.DamageType }, DamageTierBonus, SpeedThreshold, Knockback, EntityCode, DurabilityDamage, DropChance, PenetrationBonus, CanBeCollected);
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

    public ProjectileStats GetStats(ItemStack stack)
    {
        ItemStackProjectileStats stackStats = ItemStackProjectileStats.FromItemStack(stack);

        ProjectileStats stats = Stats?.Clone() ?? throw new Exception();
        stats.DamageStats.Damage *= stackStats.DamageMultiplier;
        stats.DamageTierBonus += stackStats.DamageTierBonus;
        stats.DropChance = Math.Max(0, Math.Min(1, stats.DropChance * stackStats.DropChanceMultiplier));
        stats.Knockback *= stackStats.KnockbackMultiplier;
        stats.PenetrationBonus = Math.Max(0, stackStats.PenetrationBonus + stats.PenetrationBonus);
        stats.AdditionalDurabilityCost = Math.Max(0, stackStats.AdditionalDurabilityCost + stats.AdditionalDurabilityCost);

        return stats;
    }

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        if (Stats != null)
        {
            ItemStackMeleeWeaponStats weaponStackStats = ItemStackMeleeWeaponStats.FromItemStack(inSlot.Itemstack);
            ItemStackProjectileStats projectileStackStats = ItemStackProjectileStats.FromItemStack(inSlot.Itemstack);

            dsc.AppendLine(Lang.Get(
                "combatoverhaul:iteminfo-projectile",
                $"{Stats.DamageStats.Damage * weaponStackStats.DamageMultiplier * projectileStackStats.DamageMultiplier:F1}",
                Lang.Get($"combatoverhaul:damage-type-{Stats.DamageStats.DamageType}"),
                $"{(1 - Stats.DropChance * projectileStackStats.DropChanceMultiplier) * 100:F1}"));

            if (Stats.DamageTierBonus != 0)
            {
                dsc.AppendLine(Lang.Get("combatoverhaul:iteminfo-projectile-bonus-damagetier", Stats.DamageTierBonus + weaponStackStats.DamageTierBonus + projectileStackStats.DamageTierBonus));
            }

            if (Stats.AdditionalDurabilityCost != 0)
            {
                dsc.AppendLine(Lang.Get("combatoverhaul:iteminfo-projectile-durability-cost", Stats.AdditionalDurabilityCost));
            }
        }

        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
    }
}
