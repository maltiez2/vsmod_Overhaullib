using CombatOverhaul.Utils;
using OpenTK.Mathematics;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace CombatOverhaul.DamageSystems;


public static class DebugParticleSpawner
{
    public static void SpawnDebugBlockParticles(ICoreServerAPI api, Entity player, DamageBlockStats stats)
    {
        Vector3d center = (player.Pos.XYZ + player.LocalEyePos).ToOpenTK();
        Vector3d viewDirection = player.Pos.GetViewVector().ToOpenTK();

        IEnumerable<(Vector3d position, DirectionOffset direction)> entities = GetSurroundingEntitiesWithDirections(api, player, 8);
        foreach ((Vector3d position, DirectionOffset direction) in entities)
        {
            Color4 color = stats.Directions.Check(direction) ? Color4.Green : Color4.Red;

            SpawnParticlesRay(api, color, center, position, 25, 5);
        }

        Color4 bordersColor = Color4.Yellow;
        DirectionConstrain constraint = stats.Directions.Expand(Angle.FromDegrees(0));
        SpawnParticlesArc(api, bordersColor, viewDirection, center,
            new DirectionOffset(constraint.YawRight, constraint.PitchTop),
            new DirectionOffset(constraint.YawLeft, constraint.PitchTop), 16, 0, 0.5f);
        SpawnParticlesArc(api, bordersColor, viewDirection, center,
            new DirectionOffset(constraint.YawRight, constraint.PitchBottom),
            new DirectionOffset(constraint.YawLeft, constraint.PitchBottom), 16, 0, 0.5f);
        SpawnParticlesArc(api, bordersColor, viewDirection, center,
            new DirectionOffset(constraint.YawRight, constraint.PitchTop),
            new DirectionOffset(constraint.YawRight, constraint.PitchBottom), 16, 0, 0.5f);
        SpawnParticlesArc(api, bordersColor, viewDirection, center,
            new DirectionOffset(constraint.YawLeft, constraint.PitchTop),
            new DirectionOffset(constraint.YawLeft, constraint.PitchBottom), 16, 0, 0.5f);
        SpawnParticlesArc(api, bordersColor, viewDirection, center,
            new DirectionOffset(constraint.YawRight, constraint.PitchTop),
            new DirectionOffset(constraint.YawLeft, constraint.PitchBottom), 32, 0, 0.2f);
        SpawnParticlesArc(api, bordersColor, viewDirection, center,
            new DirectionOffset(constraint.YawRight, constraint.PitchBottom),
            new DirectionOffset(constraint.YawLeft, constraint.PitchTop), 32, 0, 0.2f);
    }


    private static (Vector3d, Vector3d, Vector3d, Vector3d) GetConstraintVertices(DirectionConstrain constraint, Vector3d direction)
    {
        Vector3d[] result = BuildConstraintVectors(direction, constraint.YawLeft.Radians, constraint.YawRight.Radians, constraint.PitchBottom.Radians, constraint.PitchTop.Radians);
        return (result[0], result[1], result[3], result[2]);
    }
    private static Vector3d[] BuildConstraintVectors(Vector3d direction, double h0, double h1, double v0, double v1)
    {
        direction = direction.Normalized();

        Vector3d up = Vector3d.UnitY;
        Vector3d right = Vector3d.Cross(up, direction);

        if (right.LengthSquared < 1e-12)
        {
            right = Vector3d.UnitX;
        }

        right.Normalize();
        up = Vector3d.Cross(direction, right).Normalized();

        Vector3d[] result = new Vector3d[4];
        int i = 0;

        foreach (double h in new[] { h0, h1 })
        {
            foreach (double v in new[] { v0, v1 })
            {
                Vector3d vec =
                      direction
                    + Math.Tan(h) * right
                    + Math.Tan(v) * up;

                result[i++] = vec.Normalized();
            }
        }

        return result;
    }
    private static void SpawnParticlesRay(ICoreServerAPI api, Color4 color, Vector3d start, Vector3d end, int particlesNumber, int startFrom = 1, float size = 0.5f)
    {
        Vector3d direction = end - start;
        for (int i = startFrom; i <= particlesNumber; i++)
        {
            Vector3d particlePosition = start + direction * i / particlesNumber;
            SpawnDebugParticle(api, color, particlePosition, size);
        }
    }
    private static void SpawnParticlesArc(ICoreServerAPI api, DirectionConstrain constraint, Vector3d view, Vector3d center, Vector3d start, Vector3d end, int particlesNumber, int startFrom = 1, float size = 0.5f)
    {
        Vector3d direction = end - start;
        for (int i = startFrom; i <= particlesNumber; i++)
        {
            Vector3d particlePosition = start + direction * i / particlesNumber;

            particlePosition = Vector3d.Normalize(particlePosition - center) + center;

            Vector3d particleDirection = Vector3d.Normalize(particlePosition - center);

            DirectionOffset offset = DirectionOffset.GetDirectionWithRespectToCamera(view, particleDirection);

            Color4 color = constraint.Check(offset) ? Color4.Green : Color4.Red;

            SpawnDebugParticle(api, color, particlePosition, size);
        }
    }
    private static void SpawnParticlesArc(ICoreServerAPI api, Color4 color, Vector3d view, Vector3d center, DirectionOffset start, DirectionOffset end, int particlesNumber, int startFrom = 1, float size = 0.5f)
    {
        DirectionOffset direction = end - start;
        for (int i = startFrom; i <= particlesNumber; i++)
        {
            DirectionOffset particleAngles = start + direction * i / particlesNumber;

            Vector3d particleDirection = DirectionOffset.GetDirectionVectorWithRespectToWorld(view, DirectionOffset.DirectionFromYawPitch(particleAngles));

            Vector3d particlePosition = center + particleDirection;

            SpawnDebugParticle(api, color, particlePosition, size);
        }
    }
    private static void SpawnParticlesArc(ICoreServerAPI api, Color4 color, Vector3d center, Vector3d start, Vector3d end, int particlesNumber, int startFrom = 1, float size = 0.5f)
    {
        Vector3d direction = end - start;
        for (int i = startFrom; i <= particlesNumber; i++)
        {
            Vector3d particlePosition = start + direction * i / particlesNumber;

            particlePosition = Vector3d.Normalize(particlePosition - center) + center;

            SpawnDebugParticle(api, color, particlePosition, size);
        }
    }
    private static void SpawnDebugParticle(ICoreServerAPI api, Color4 color, Vector3d position, float size = 0.5f)
    {
        api.World.SpawnParticles(1, ToVanillaParticleColor(color), position.ToVanilla().ToVec3d(), position.ToVanilla().ToVec3d(), new Vec3f(), new Vec3f(), 0.008f, 0f, size, EnumParticleModel.Cube);
    }
    private static int ToVanillaParticleColor(Color4 color)
    {
        return ColorUtil.ColorFromRgba((int)(color.B * 255), (int)(color.G * 255), (int)(color.R * 255), (int)(color.A * 255));
    }
    private static IEnumerable<(Vector3d position, DirectionOffset direction)> GetSurroundingEntitiesWithDirections(ICoreServerAPI api, Entity player, float range)
    {
        Entity[] targets = api.World.GetEntitiesAround(player.Pos.XYZ, range, range, entity => entity.IsCreature);
        return targets.Where(target => target != player).Select(target => (GetEyesPosition(target), DirectionOffset.GetDirectionWithRespectToCamera(player, target)));
    }
    private static Vector3d GetEyesPosition(Entity target)
    {
        return (target.Pos.XYZ + target.LocalEyePos).ToOpenTK();
    }
}
