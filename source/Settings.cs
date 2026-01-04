namespace CombatOverhaul;

public sealed class Settings
{
    public float DirectionsCursorTransparency { get; set; } = 1.0f;
    public float DirectionsCursorScale { get; set; } = 1.0f;

    public string BowsAimingCursorType { get; set; } = "Fixed";
    public float BowsAimingHorizontalLimit { get; set; } = 0.125f;
    public float BowsAimingVerticalLimit { get; set; } = 0.35f;

    public string ThrownWeaponsCursorType { get; set; } = "Fixed";
    public float ThrownWeaponsAimingHorizontalLimit { get; set; } = 0.125f;
    public float ThrownWeaponsAimingVerticalLimit { get; set; } = 0.25f;

    public string SlingsAimingCursorType { get; set; } = "Fixed";
    public float SlingsAimingHorizontalLimit { get; set; } = 0.125f;
    public float SlingsAimingVerticalLimit { get; set; } = 0.35f;

    public bool PrintRangeHits { get; set; } = false;
    public bool PrintMeleeHits { get; set; } = false;
    public bool PrintPlayerHits { get; set; } = false;

    public float DirectionsSensitivity { get; set; } = 1f;
    public bool DirectionsInvert { get; set; } = false;
    public bool FlipDirectionAfterAttack { get; set; } = true;

    public bool HandsYawSmoothing { get; set; } = false;

    public bool VanillaActionsWhileBlocking { get; set; } = true;

    public float CollisionRadius { get; set; } = 16f;

    public float DefaultColliderPenetrationResistance { get; set; } = 5f;

    public bool DirectionsMovementControls { get; set; } = false;
    public bool DirectionsHotkeysControls { get; set; } = false;

    public bool DisableAllAnimations { get; set; } = false;
    public bool DisableThirdPersonAnimations { get; set; } = false;

    public bool MeleeWeaponStopOnTerrainHit { get; set; } = true;

    public bool MeleeWeaponIgnoreTerrainBehind { get; set; } = false;

    public float MeleeWeaponAttackSpeedMultiplier { get; set; } = 1;

    public int GlobalAttackCooldownMs { get; set; } = 1000;

    public bool SecondChanceParticles { get; set; } = true;

    public bool DebugHitParticles { get; set; } = false;

    public bool DebugWeaponTrailParticles { get; set; } = false;

    public float DebugWeaponTrailParticlesSize { get; set; } = 0.5f;

    public bool DebugProjectilesTrailsParticles { get; set; } = false;
}