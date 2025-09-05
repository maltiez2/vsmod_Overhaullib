using CombatOverhaul.Implementations;
using CombatOverhaul.Utils;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace CombatOverhaul.RangedSystems;

[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public class AiTaskCOTurretModeConfig : AiTaskTurretModeConfig
{
    [JsonProperty] private JsonItemStack? projectileStack;
    [JsonProperty] public int ProjectilesNumber { get; set; } = 1;
    [JsonProperty] public float ProjectileDamageMultiplier { get; set; } = 1;
    [JsonProperty] public float DispersionReductionRate { get; set; } = 1;
    [JsonProperty] public float MinDistanceToRezero { get; set; } = 2;
    [JsonProperty] public float MaxDistanceToRezero { get; set; } = 10;

    public ItemStack? ProjectileStack { get; protected set; }

    public override void Init(EntityAgent entity)
    {
        base.Init(entity);

        if (projectileStack != null && projectileStack.Resolve(entity.Api.World, ""))
        {
            ProjectileStack = projectileStack.ResolvedItemstack;
            projectileStack = null;
        }
    }
}

public class AiTaskCOTurretMode : AiTaskTurretModeR
{
    private AiTaskCOTurretModeConfig Config => GetConfig<AiTaskCOTurretModeConfig>();

    public AiTaskCOTurretMode(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig) : base(entity, taskConfig, aiConfig)
    {
        baseConfig = LoadConfig<AiTaskCOTurretModeConfig>(entity, taskConfig, aiConfig);

        CombatOverhaulSystem system = entity.Api.ModLoader.GetModSystem<CombatOverhaulSystem>();
        ProjectileSystem = system.ServerProjectileSystem ?? throw new Exception();
        LastEntityPosition = entity.Pos.XYZ.Clone();
        BallisticSolver = new BallisticSolver();
    }

    protected enum EnumArcCalculationType
    {
        Any,
        Full,
        Horizontal,
        Vertical
    }

    protected Vec3d LastEntityPosition;
    protected ProjectileSystemServer ProjectileSystem;
    protected ICoreAPI Api => entity.Api;
    protected IBallisticSolver BallisticSolver;


    protected override void SetOrAdjustDispersion()
    {
        if (targetEntity == null) return;

        if (targetEntity.EntityId == previousTargetId && (LastEntityPosition - entity.Pos.XYZ).Length() < Config.MinDistanceToRezero)
        {
            currentYawDispersion = MathF.Max(Config.YawDispersionDeg, currentYawDispersion * Config.DispersionReductionRate);
            currentPitchDispersion = MathF.Max(Config.PitchDispersionDeg, currentPitchDispersion * Config.DispersionReductionRate);
        }
        else
        {
            float distance = (float)(LastEntityPosition - entity.Pos.XYZ).Length();
            float factor = GameMath.Clamp((distance - Config.MinDistanceToRezero) / (Config.MaxDistanceToRezero - Config.MinDistanceToRezero), 0, 1);

            currentYawDispersion = MathF.Max((Config.MaxYawDispersionDeg - Config.YawDispersionDeg) * factor + Config.YawDispersionDeg, Config.YawDispersionDeg);
            currentPitchDispersion = MathF.Max((Config.MaxPitchDispersionDeg - Config.PitchDispersionDeg) * factor + Config.PitchDispersionDeg, Config.PitchDispersionDeg);
            previousTargetId = targetEntity.EntityId;
        }

        LastEntityPosition.Set(entity.Pos.XYZ);
    }

    protected override void ShootProjectile()
    {
        if (targetEntity == null || Config.ProjectileStack == null) return;

        ProjectileStats? stats = Config.ProjectileStack.Item.GetCollectibleBehavior<ProjectileBehavior>(true)?.GetStats(Config.ProjectileStack);

        if (stats == null)
        {
            LoggerUtil.Warn(Api, this, $"Failed to create projectile '{Config.ProjectileStack.Collectible?.Code}': item is missing ProjectileBehavior");
            return;
        }

        IBallisticSolver.TargetData target = IBallisticSolver.GetTargetData(entity, targetEntity, Config.ProjectileGravityFactor, Config.ProjectileSpeed);
        IBallisticSolver.BallisticOutput ballisticData = BallisticSolver.SolveBallisticArc(target);
        IBallisticSolver.DispersionData dispersion = new(Angle.FromDegrees(currentYawDispersion), Angle.FromDegrees(currentPitchDispersion), randomFloat);

        for (int count = 0; count < Config.ProjectilesNumber; count++)
        {
            IBallisticSolver.BallisticOutput ballisticDataWithDispersion = BallisticSolver.ApplyDispersion(ballisticData, dispersion);
#if DEBUG
            BallisticSolver.DrawBallisticArc(ballisticDataWithDispersion, target, entity.Api.World);
#endif
            ProjectileSpawnStats spawnStats = new()
            {
                ProducerEntityId = entity.EntityId,
                DamageMultiplier = Config.ProjectileDamageMultiplier,
                DamageTier = Config.ProjectileDamageTier,
                Position = target.ShooterPosition.ToOpenTK(),
                Velocity = ballisticDataWithDispersion.Velocity.ToOpenTK()
            };

            ProjectileSystem.Spawn(Guid.NewGuid(), stats, spawnStats, Config.ProjectileStack, null, entity, targetEntity);
        }
    }
}