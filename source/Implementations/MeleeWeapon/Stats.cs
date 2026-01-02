using CombatOverhaul.DamageSystems;
using CombatOverhaul.MeleeSystems;
using CombatOverhaul.RangedSystems.Aiming;
using Vintagestory.API.Common;

namespace CombatOverhaul.Implementations;

public class StanceStats
{
    public bool CanAttack { get; set; } = false;
    public bool CanParry { get; set; } = false;
    public bool CanBlock { get; set; } = false;
    public bool CanSprint { get; set; } = true;
    public bool CanThrow { get; set; } = false;
    public bool CanBash { get; set; } = false;
    public bool CanRiposte { get; set; } = false;
    public float SpeedPenalty { get; set; } = 0;
    public float BlockSpeedPenalty { get; set; } = 0;

    public float GripLengthFactor { get; set; } = 0;
    public float GripMinLength { get; set; } = 0;
    public float GripMaxLength { get; set; } = 0;

    public MeleeAttackStats? Attack { get; set; }
    public MeleeAttackStats? Riposte { get; set; }
    public MeleeAttackStats? BlockBash { get; set; }
    public Dictionary<string, MeleeAttackStats>? DirectionalAttacks { get; set; }
    public Dictionary<string, MeleeAttackStats>? DirectionalBlockBashes { get; set; }
    public DamageBlockJson? Block { get; set; }
    public DamageBlockJson? Parry { get; set; }
    public MeleeAttackStats? HandleAttack { get; set; }

    public string? AttackHitSound { get; set; } = null;
    public string? BashHitSound { get; set; } = null;
    public string? HandleHitSound { get; set; } = null;

    public float AttackCooldownMs { get; set; } = 0;
    public float BlockCooldownMs { get; set; } = 0;
    public bool ParryWithoutDelay { get; set; } = true;

    public string AttackDirectionsType { get; set; } = "None";
    public Dictionary<string, string[]> AttackAnimation { get; set; } = [];
    public Dictionary<string, string[]> BlockBashAnimation { get; set; } = [];
    public string? BlockAnimation { get; set; } = null;
    public string? RiposteAnimation { get; set; } = null;
    public string? ReadyAnimation { get; set; } = null;
    public string? IdleAnimation { get; set; } = null;
    public string? WalkAnimation { get; set; } = null;
    public string? RunAnimation { get; set; } = null;
    public string? SwimAnimation { get; set; } = null;
    public string? SwimIdleAnimation { get; set; } = null;

    public float AttackSpeedMultiplier { get; set; } = 1;
}

public class ThrowWeaponStats
{
    public string AimAnimation { get; set; } = "";
    public string ThrowAnimation { get; set; } = "";
    public string TpAimAnimation { get; set; } = "";
    public string TpThrowAnimation { get; set; } = "";

    public AimingStatsJson Aiming { get; set; } = new();
    public int DamageTier { get; set; }
    public float Knockback { get; set; } = 0;
    public int DurabilityDamage { get; set; } = 1;
    public float Velocity { get; set; } = 1;
    public float Zeroing { get; set; } = 1.5f;
}

public class MeleeWeaponStats : WeaponStats
{
    public StanceStats? OneHandedStance { get; set; } = null;
    public StanceStats? TwoHandedStance { get; set; } = null;
    public StanceStats? OffHandStance { get; set; } = null;
    public Dictionary<string, StanceStats> MainHandDualWieldStances { get; set; } = [];
    public Dictionary<string, StanceStats> OffHandDualWieldStances { get; set; } = [];
    public ThrowWeaponStats? ThrowAttack { get; set; } = null;
    public float ScreenShakeStrength { get; set; } = 0.25f;
    public float ThrowScreenShakeStrength { get; set; } = 0.15f;
    public bool RenderingOffset { get; set; } = false;
    public float AnimationStaggerOnHitDurationMs { get; set; } = 100;
}

public class MeleeWeaponModeStats : MeleeWeaponStats
{
    public string Icon { get; set; } = "";
    public string Name { get; set; } = "";
}

public class MeleeWeaponModeCollectionStats
{
    public Dictionary<string, MeleeWeaponModeStats> Modes { get; set; } = [];
}

public readonly struct ItemStackMeleeWeaponStats
{
    public readonly float DamageMultiplier;
    public readonly float DamageBonus;
    public readonly int DamageTierBonus;
    public readonly float AttackSpeed;
    public readonly int BlockTierBonus;
    public readonly int ParryTierBonus;
    public readonly float ThrownDamageMultiplier;
    public readonly int ThrownDamageTierBonus;
    public readonly float ThrownAimingDifficulty;
    public readonly float ThrownProjectileSpeedMultiplier;
    public readonly float KnockbackMultiplier;
    public readonly int ArmorPiercingBonus;

    public ItemStackMeleeWeaponStats(float damageMultiplier, float damageBonus, int damageTierBonus, float attackSpeed, int blockTierBonus, int parryTierBonus, float thrownDamageMultiplier, int thrownDamageTierBonus, float thrownAimingDifficulty, float thrownProjectileSpeedMultiplier, float knockbackMultiplier, int armorPiercingBonus)
    {
        DamageMultiplier = damageMultiplier;
        DamageBonus = damageBonus;
        DamageTierBonus = damageTierBonus;
        AttackSpeed = attackSpeed;
        BlockTierBonus = blockTierBonus;
        ParryTierBonus = parryTierBonus;
        ThrownDamageMultiplier = thrownDamageMultiplier;
        ThrownDamageTierBonus = thrownDamageTierBonus;
        ThrownAimingDifficulty = thrownAimingDifficulty;
        ThrownProjectileSpeedMultiplier = thrownProjectileSpeedMultiplier;
        KnockbackMultiplier = knockbackMultiplier;
        ArmorPiercingBonus = armorPiercingBonus;
    }

    public ItemStackMeleeWeaponStats()
    {
        DamageMultiplier = 1;
        DamageBonus = 0;
        DamageTierBonus = 0;
        AttackSpeed = 1;
        BlockTierBonus = 0;
        ParryTierBonus = 0;
        ThrownDamageMultiplier = 1;
        ThrownDamageTierBonus = 0;
        ThrownAimingDifficulty = 1;
        ThrownProjectileSpeedMultiplier = 1;
        KnockbackMultiplier = 1;
    }

    public static ItemStackMeleeWeaponStats FromItemStack(ItemStack stack)
    {
        float damageMultiplier = stack.Attributes.GetFloat("damageMultiplier", 1);
        float damageBonus = stack.Attributes.GetFloat("damageBonus", 0);
        int damageTierBonus = stack.Attributes.GetInt("damageTierBonus", 0);
        float attackSpeed = stack.Attributes.GetFloat("attackSpeed", 1);
        int blockTierBonus = stack.Attributes.GetInt("blockTierBonus", 0);
        int parryTierBonus = stack.Attributes.GetInt("parryTierBonus", 0);
        float thrownDamageMultiplier = stack.Attributes.GetFloat("thrownDamageMultiplier", 1);
        int thrownDamageTierBonus = stack.Attributes.GetInt("thrownDamageTierBonus", 0);
        float thrownAimingDifficulty = stack.Attributes.GetFloat("thrownAimingDifficulty", 1);
        float thrownProjectileSpeedMultiplier = stack.Attributes.GetFloat("thrownProjectileSpeedMultiplier", 1);
        float knockbackMultiplier = stack.Attributes.GetFloat("knockbackMultiplier", 1);
        int armorPiercingBonus = stack.Attributes.GetInt("armorPiercingBonus", 0);

        return new ItemStackMeleeWeaponStats(damageMultiplier, damageBonus, damageTierBonus, attackSpeed, blockTierBonus, parryTierBonus, thrownDamageMultiplier, thrownDamageTierBonus, thrownAimingDifficulty, thrownProjectileSpeedMultiplier, knockbackMultiplier, armorPiercingBonus);
    }
    public static float GetAttackSpeed(ItemStack stack) => stack.Attributes.GetFloat("attackSpeed", 1);
}