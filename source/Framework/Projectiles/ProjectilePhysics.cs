using CollidersLib;
using CombatOverhaul.Utils;
using OpenTK.Mathematics;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace CombatOverhaul.RangedSystems;

public class ProjectilePhysicsBehaviorConfig
{
    public double ColliderRadius { get; set; } = 0;
    public bool CanRicochet { get; set; } = true;
    public float MinSpeedToRicochet { get; set; } = 0.5f;
    public float RicochetSpeedFactor { get; set; } = 0.5f;
    public float RicochetNormalSpeedFactor { get; set; } = 0.5f;
    public float MaxRicochetAngleDeg { get; set; } = 5;
}

public class ProjectilePhysicsBehavior : EntityBehaviorPassivePhysics
{
    public ProjectilePhysicsBehavior(Entity entity) : base(entity)
    {
        ModSettings = entity.Api.ModLoader.GetModSystem<CombatOverhaulSystem>().Settings;
    }

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        base.Initialize(properties, attributes);

        Config = attributes.AsObject<ProjectilePhysicsBehaviorConfig>();

        EntityBehaviorPassivePhysics_airDragValue ??= typeof(EntityBehaviorPassivePhysics).GetField("airDragValue", BindingFlags.NonPublic | BindingFlags.Instance);

        EntityBehaviorPassivePhysics_airDragValue?.SetValue(this, 1);
    }

    public bool Stuck { get; set; } = false;

    public ProjectilePhysicsBehaviorConfig Config { get; set; } = new();

    public void OnCollisionWithTerrain(TerrainWithShpereIntersectionData collisionData)
    {
        TerrainCollisionsQueue.Add(collisionData);
    }

    protected BlockPos MinPos = new(0);
    protected BlockPos MaxPos = new(0);
    protected BlockPos PosBuffer = new(0);
    protected Cuboidd EntityBox = new();
    protected Settings ModSettings;
    protected readonly List<TerrainWithShpereIntersectionData> TerrainCollisionsQueue = [];

    protected static FieldInfo? EntityBehaviorPassivePhysics_airDragValue = typeof(EntityBehaviorPassivePhysics).GetField("airDragValue", BindingFlags.NonPublic | BindingFlags.Instance);

    protected override void applyCollision(EntityPos pos, float dtFactor)
    {
        Vector3d CurrentPosition = new(pos.X, pos.Y, pos.Z);

        if (CurrentPosition.LengthSquared == 0) return;

        Vector3d PositionDelta = new(pos.Motion.X * dtFactor, pos.Motion.Y * dtFactor, pos.Motion.Z * dtFactor);
        Vector3d NextPosition = CurrentPosition + PositionDelta;

        if (true)//ModSettings.DebugProjectilesTrailsParticles)
        {
            if (pos.Motion.Length() > 0.1)
            {
                entity.Api?.World.SpawnParticles(1, ColorUtil.ColorFromRgba(255, 100, 100, 125), new(CurrentPosition.X, CurrentPosition.Y, CurrentPosition.Z), new(CurrentPosition.X, CurrentPosition.Y, CurrentPosition.Z), new Vec3f(), new Vec3f(), 0.8f, 0, 0.7f, EnumParticleModel.Cube);
            }
        }

        bool collided = TerrainCollisionsQueue.Count > 0;

        if (collided)
        {
            TerrainWithShpereIntersectionData collision = GetEarliestCollision();
            TerrainCollisionsQueue.Clear();

            Angle angle = Angle.BetweenVectors(PositionDelta, collision.Normal);
            float angleDeg = Math.Abs(angle.Degrees);

            if ((angleDeg > 90 - Config.MaxRicochetAngleDeg) && (angleDeg < 90 + Config.MaxRicochetAngleDeg) && (pos.Motion.Length() > Config.MinSpeedToRicochet))
            {
                switch (collision.Facing.Index)
                {
                    case 0: // North / South
                    case 2:
                        pos.Motion.Z *= -Config.RicochetNormalSpeedFactor;
                        break;

                    case 1: // East / West
                    case 3:
                        pos.Motion.X *= -Config.RicochetNormalSpeedFactor;
                        break;

                    case 4: // Up / Down
                    case 5:
                        pos.Motion.Y *= -Config.RicochetNormalSpeedFactor;
                        break;
                }

                PositionDelta = new(pos.Motion.X * dtFactor, pos.Motion.Y * dtFactor, pos.Motion.Z * dtFactor);
                NextPosition = collision.IntersectionPoint + PositionDelta * (1 - (CurrentPosition - collision.IntersectionPoint).Length / (CurrentPosition - NextPosition).Length);

                switch (collision.Facing.Index)
                {
                    case 2: // North / South
                        NextPosition.Z = Math.Max(collision.IntersectionPoint.Z + Config.ColliderRadius, NextPosition.Z);
                        break;
                    case 0:
                        NextPosition.Z = Math.Min(collision.IntersectionPoint.Z - Config.ColliderRadius, NextPosition.Z);
                        break;

                    case 1: // East / West
                        NextPosition.X = Math.Max(collision.IntersectionPoint.X + Config.ColliderRadius, NextPosition.X);
                        break;
                    case 3:
                        NextPosition.X = Math.Min(collision.IntersectionPoint.X - Config.ColliderRadius, NextPosition.X);
                        break;

                    case 4: // Up / Down
                        NextPosition.Y = Math.Max(collision.IntersectionPoint.Y + Config.ColliderRadius, NextPosition.Y);
                        break;
                    case 5:
                        NextPosition.Y = Math.Min(collision.IntersectionPoint.Y - Config.ColliderRadius, NextPosition.Y);
                        break;
                }

                newPos.Set(NextPosition.X, NextPosition.Y, NextPosition.Z);
                entity.CollidedHorizontally = false;
                entity.CollidedVertically = false;
                (entity as ProjectileEntity)?.SetRotation();
                pos.Motion *= Config.RicochetSpeedFactor;

                entity.Api.World.PlaySoundAt(
                    collision.Block?.Sounds?.Hit.Location ?? collision.Block?.Sounds?.ByTool?.Values?.FirstOrDefault()?.Hit.Location ?? collision.Block?.Sounds?.Break.Location ?? new AssetLocation("game:sounds/player/destruct"),
                    collision.IntersectionPoint.X,
                    collision.IntersectionPoint.Y,
                    collision.IntersectionPoint.Z);

                return;
            }

            newPos.Set(collision.IntersectionPoint.X, collision.IntersectionPoint.Y, collision.IntersectionPoint.Z);
            entity.WatchedAttributes.SetBool("stuck", true);
            entity.CollidedHorizontally = true;
            entity.CollidedVertically = true;

            entity.Api?.World.SpawnParticles(1, ColorUtil.ColorFromRgba(255, 255, 100, 125), new(collision.IntersectionPoint.X, collision.IntersectionPoint.Y, collision.IntersectionPoint.Z), new(CurrentPosition.X, CurrentPosition.Y, CurrentPosition.Z), new Vec3f(), new Vec3f(), 0.8f, 0, 1.5f, EnumParticleModel.Cube);

            BlockPos blockPosition = new(collision.BlockPosition.X, collision.BlockPosition.Y, collision.BlockPosition.Z, 0);

            collision.Block?.OnEntityCollide(entity.Api.World, entity, blockPosition, collision.Facing, pos.Motion, true);

            pos.Motion *= 0;
        }
        else
        {
            entity.CollidedHorizontally = false;
            entity.CollidedVertically = false;

            newPos.Set(NextPosition.X, NextPosition.Y, NextPosition.Z);
        }
    }

    protected virtual TerrainWithShpereIntersectionData GetEarliestCollision()
    {
        double minTime = double.MaxValue;
        TerrainWithShpereIntersectionData earliestCollision = TerrainCollisionsQueue[0];
        foreach (TerrainWithShpereIntersectionData collision in TerrainCollisionsQueue)
        {
            if (collision.PositionInTime < minTime)
            {
                minTime = collision.PositionInTime;
                earliestCollision = collision;
            }
        }

        return earliestCollision;
    }
}
