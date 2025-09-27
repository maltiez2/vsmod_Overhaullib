using Vintagestory.API.Common;

namespace CombatOverhaul.Implementations;

public readonly struct ItemStackRangedStats
{
    public readonly float ReloadSpeed;
    public readonly float DamageMultiplier;
    public readonly int DamageTierBonus;
    public readonly float ProjectileSpeed;
    public readonly float DispersionMultiplier;
    public readonly float AimingDifficulty;

    public ItemStackRangedStats(float reloadSpeed, float damageMultiplier, int damageTierBonus, float projectileSpeed, float dispersionMultiplier, float aimingDifficulty)
    {
        ReloadSpeed = reloadSpeed;
        DamageMultiplier = damageMultiplier;
        DamageTierBonus = damageTierBonus;
        ProjectileSpeed = projectileSpeed;
        DispersionMultiplier = dispersionMultiplier;
        AimingDifficulty = aimingDifficulty;
    }

    public ItemStackRangedStats()
    {
        ReloadSpeed = 1;
        DamageMultiplier = 1;
        DamageTierBonus = 0;
        ProjectileSpeed = 1;
        DispersionMultiplier = 1;
        AimingDifficulty = 0;
    }

    public static ItemStackRangedStats FromItemStack(ItemStack stack)
    {
        float reloadSpeed = stack.Attributes.GetFloat("reloadSpeed", 1);
        float damageMultiplier = stack.Attributes.GetFloat("damageMultiplier", 1);
        int damageTierBonus = stack.Attributes.GetInt("damageTierBonus", 0);
        float projectileSpeed = stack.Attributes.GetFloat("projectileSpeed", 1);
        float dispersionMultiplier = stack.Attributes.GetFloat("dispersionMultiplier", 1);
        float aimingDifficulty = stack.Attributes.GetFloat("aimingDifficulty", 1);

        return new ItemStackRangedStats(reloadSpeed, damageMultiplier, damageTierBonus, projectileSpeed, dispersionMultiplier, aimingDifficulty);
    }
}