using CombatOverhaul.Utils;
using OpenTK.Mathematics;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using static CombatOverhaul.RangedSystems.IBallisticSolver;

namespace CombatOverhaul.RangedSystems;

public interface IBallisticSolver
{
    enum EnumArcCalculationType
    {
        Any,
        Full,
        Horizontal,
        Vertical
    }

    readonly struct TargetData
    {
        public readonly FastVec3d ShooterPosition;
        public readonly FastVec3d TargetPosition;
        public readonly FastVec3d TargetVelocity;
        public readonly double GravityAcceleration;
        public readonly double Speed;

        public TargetData(FastVec3d shooterPosition, FastVec3d targetPosition, FastVec3d targetVelocity, double gravityAcceleration, double speed)
        {
            ShooterPosition = shooterPosition;
            TargetPosition = targetPosition;
            TargetVelocity = targetVelocity;
            GravityAcceleration = gravityAcceleration;
            Speed = speed;
        }
    }

    readonly struct BallisticOutput
    {
        public readonly bool Success;
        public readonly FastVec3d Velocity;
        public readonly EnumArcCalculationType ArcCalculationType;

        public BallisticOutput(bool success, FastVec3d velocity, EnumArcCalculationType calculationType)
        {
            Success = success;
            Velocity = velocity;
            ArcCalculationType = calculationType;
        }
    }

    readonly struct DispersionData
    {
        public readonly Angle YawDispersion;
        public readonly Angle PitchDispersion;
        public readonly NatFloat Distribution;

        public DispersionData(Angle yawDispersion, Angle pitchDispersion, NatFloat distribution)
        {
            YawDispersion = yawDispersion;
            PitchDispersion = pitchDispersion;
            Distribution = distribution;
        }
    }

    BallisticOutput SolveBallisticArc(TargetData data);
    void DrawBallisticArc(BallisticOutput arcData, TargetData targetData, IWorldAccessor world);
    BallisticOutput ApplyDispersion(BallisticOutput targetData, DispersionData dispersionData);
    static TargetData GetTargetData(Entity shooterEntity, Entity targetEntity, double gravityFactor, double speed)
    {
        Vec3d pos = shooterEntity.ServerPos.XYZ.Add(0, shooterEntity.LocalEyePos.Y, 0);
        Vec3d targetPos = targetEntity.ServerPos.XYZ.Add(0, targetEntity.LocalEyePos.Y, 0);
        Vec3d targetVelocity = targetEntity.ServerPos.Motion;
        double gravityAcceleration = gravityFactor * GlobalConstants.GravityPerSecond * _blocksPerSecondToBlocksPerMinute;

        return new(new FastVec3d(pos.X, pos.Y, pos.Z), new FastVec3d(targetPos.X, targetPos.Y, targetPos.Z), new FastVec3d(targetVelocity.X, targetVelocity.Y, targetVelocity.Z), gravityAcceleration, speed);
    }


    private const float _blocksPerSecondToBlocksPerMinute = 1 / 60f;
}

public struct BallisticSolver : IBallisticSolver
{
    public BallisticOutput SolveBallisticArc(TargetData data)
    {
        bool success = GetProjectilePositionAndVelocity(out Vector3d velocity, data.ShooterPosition.ToOpenTK(), data.TargetPosition.ToOpenTK(), data.TargetVelocity.ToOpenTK(), data.GravityAcceleration, data.Speed, out EnumArcCalculationType calcType);

        return new(success, velocity.ToVanilla(), calcType);
    }
    public void DrawBallisticArc(BallisticOutput arcData, TargetData targetData, IWorldAccessor world)
    {
        DrawBallisticArc(arcData.Velocity.ToOpenTK(), targetData.ShooterPosition.ToOpenTK(), targetData.TargetPosition.ToOpenTK(), targetData.GravityAcceleration, arcData.ArcCalculationType, world);
    }
    public BallisticOutput ApplyDispersion(BallisticOutput targetData, DispersionData dispersionData)
    {
        Vector3d direction = targetData.Velocity.ToOpenTK();
        double speed = direction.Length;
        Vector2 dispersion = new(dispersionData.YawDispersion.Degrees, dispersionData.PitchDispersion.Degrees);

        direction = GetDirectionWithDispersion(Vector3d.Normalize(direction), dispersion, dispersionData.Distribution);

        return new(targetData.Success, (direction * speed).ToVanilla(), targetData.ArcCalculationType);
    }

    private static bool GetProjectilePositionAndVelocity(out Vector3d velocity, Vector3d shooterPos, Vector3d targetPos, Vector3d targetVelocity, double gravityAcceleration, double speed, out EnumArcCalculationType calcType)
    {
        calcType = EnumArcCalculationType.Any;
        velocity = new();

        Vector3d originalTargetPos = targetPos;

        float blocksPerSecondToBlocksPerMinute = 1 / 60f;

        speed *= blocksPerSecondToBlocksPerMinute;

        Vector3d start = new(shooterPos.X, shooterPos.Y, shooterPos.Z);
        Vector3d target = new(targetPos.X, targetPos.Y, targetPos.Z);

        bool solvedBallisticArc = false;
        for (int triesCount = 0; triesCount < 30; triesCount++)
        {
            solvedBallisticArc = SolveBallisticArc(out velocity, out double time, start, target, speed, gravityAcceleration, out calcType, EnumArcCalculationType.Any);

            if (targetVelocity.Length < 0.01)
            {
                if (solvedBallisticArc) break;
                speed *= 1.1f;
                continue;
            }

            targetPos += targetVelocity * time;
            target = new(targetPos.X, targetPos.Y, targetPos.Z);

            _ = SolveBallisticArc(out velocity, out time, start, target, speed, gravityAcceleration, out calcType, EnumArcCalculationType.Any);

            targetPos = originalTargetPos + targetVelocity * time;
            target = new(targetPos.X, targetPos.Y, targetPos.Z);

            solvedBallisticArc = SolveBallisticArc(out velocity, out _, start, target, speed, gravityAcceleration, out EnumArcCalculationType resultCalcType, calcType);

            calcType = resultCalcType;

            if (solvedBallisticArc) break;

            speed *= 1.1f;
        }

        return solvedBallisticArc;
    }
    private static void DrawBallisticArc(Vector3d velocity, Vector3d start, Vector3d target, double acceleration, EnumArcCalculationType calcType, IWorldAccessor world)
    {
        int color = calcType switch
        {
            EnumArcCalculationType.Any => ColorUtil.ColorFromRgba(255, 255, 255, 255),
            EnumArcCalculationType.Full => ColorUtil.ColorFromRgba(0, 255, 0, 255),
            EnumArcCalculationType.Horizontal => ColorUtil.ColorFromRgba(255, 0, 255, 255),
            EnumArcCalculationType.Vertical => ColorUtil.ColorFromRgba(0, 255, 255, 255),
            _ => ColorUtil.ColorFromRgba(255, 255, 255, 255)
        };

        double cutoff = Math.Min(start.Y, target.Y) - 1;
        Vector3d position = start;
        double dt = 0.01;
        double time = 0;
        while (position.Y > cutoff)
        {
            time += dt;
            position += velocity * dt;
            velocity.Y -= acceleration * dt;
            if (time > 0.1)
            {
                time = 0;
                world.SpawnParticles(1, color, new(position.X, position.Y, position.Z), new(position.X, position.Y, position.Z), new Vec3f(), new Vec3f(), 2, 0, 0.5f, EnumParticleModel.Cube);
            }
        }
    }
    private static bool SolveBallisticArc(out Vector3d velocity, out double time, Vector3d start, Vector3d target, double speed, double g, out EnumArcCalculationType calcType, EnumArcCalculationType forceCalcType)
    {
        calcType = forceCalcType;

        switch (forceCalcType)
        {
            case EnumArcCalculationType.Full:
                return SolveBallisticArcFullSpeed(out velocity, out time, start, target, speed, g);
            case EnumArcCalculationType.Horizontal:
                return SolveBallisticArcHor(out velocity, out time, start, target, speed, g);
            case EnumArcCalculationType.Vertical:
                return SolveBallisticArcVert(out velocity, out time, start, target, speed, g);
            default:
                break;
        }

        bool fullSolved = SolveBallisticArcFullSpeed(out velocity, out time, start, target, speed, g);
        calcType = EnumArcCalculationType.Full;

        if (fullSolved) return true;

        Vector3d delta = target - start;
        Vector2d deltaXZ = new(delta.X, delta.Z);
        double h = Math.Abs(delta.Y);
        double l = deltaXZ.Length / 2;

        if (h > l)
        {
            bool solved = SolveBallisticArcVert(out velocity, out time, start, target, speed, g);
            calcType = EnumArcCalculationType.Vertical;
            if (!solved)
            {
                calcType = EnumArcCalculationType.Horizontal;
                return SolveBallisticArcHor(out velocity, out time, start, target, speed, g);
            }
            return solved;
        }
        else
        {
            bool solved = SolveBallisticArcHor(out velocity, out time, start, target, speed, g);
            calcType = EnumArcCalculationType.Horizontal;
            if (!solved)
            {
                calcType = EnumArcCalculationType.Vertical;
                return SolveBallisticArcVert(out velocity, out time, start, target, speed, g);
            }
            return solved;
        }
    }
    private static bool SolveBallisticArcHor(out Vector3d velocity, out double time, Vector3d start, Vector3d target, double horizSpeed, double g)
    {
        Vector3d delta = target - start;
        Vector2d deltaXZ = new(delta.X, delta.Z);
        double h = delta.Y;
        double l = deltaXZ.Length;
        time = l / horizSpeed;

        if (l < 1e-6)
        {
            velocity = Vector3d.Zero;
            return false; // no horizontal travel possible
        }

        // Time of flight determined by horizontal motion
        double t = l / horizSpeed;

        // Vertical velocity needed to reach the target in that time
        double vY = h / t + 0.5 * g * t;

        // Build velocity vector
        deltaXZ = Vector2d.Normalize(deltaXZ) * horizSpeed;
        velocity = new Vector3d(deltaXZ.X, vY, deltaXZ.Y);

        return true;
    }
    private static bool SolveBallisticArcVert(out Vector3d velocity, out double time, Vector3d start, Vector3d target, double vY, double g)
    {
        velocity = Vector3d.Zero;
        time = 0;

        Vector3d delta = target - start;
        Vector2d deltaXZ = new(delta.X, delta.Z);
        double h = delta.Y;
        double l = deltaXZ.Length;

        // Quadratic for time of flight: 0.5*g*t^2 - vY*t + h = 0
        double a = 0.5 * g;
        double b = -vY;
        double c = h;

        double discriminant = b * b - 4 * a * c;
        if (discriminant < 0)
            return false; // no solution with this vY

        double sqrtDisc = Math.Sqrt(discriminant);

        // Two possible times
        double t1 = (-b + sqrtDisc) / (2 * a);
        double t2 = (-b - sqrtDisc) / (2 * a);

        double t = double.NaN;
        if (t1 > 1e-6) t = t1;
        if (t2 > 1e-6 && (double.IsNaN(t) || t2 < t)) t = t2;

        if (double.IsNaN(t))
            return false;

        time = t;

        if (l < 1e-6)
        {
            // Pure vertical travel
            velocity = new Vector3d(0, (float)vY, 0);
            return true;
        }

        // Horizontal velocity from required travel
        deltaXZ = Vector2d.Normalize(deltaXZ) * (float)(l / t);

        velocity = new Vector3d(deltaXZ.X, (float)vY, deltaXZ.Y);
        return true;
    }
    private static bool SolveBallisticArcFullSpeed(out Vector3d velocity, out double time, Vector3d start, Vector3d target, double speed, double acceleration)
    {
        velocity = Vector3d.Zero;
        Vector3d delta = target - start;

        // Split into horizontal and vertical distances
        Vector2d deltaXZ = new(delta.X, delta.Z);
        double horizontalDist = deltaXZ.Length;
        double verticalDist = delta.Y;

        double speedSq = speed * speed;
        double speed4 = speedSq * speedSq;

        double discriminant = speed4 - acceleration * (acceleration * horizontalDist * horizontalDist + 2 * verticalDist * speedSq);

        time = 0;
        if (discriminant < 0f)
            return false; // No valid solution

        double sqrtDisc = Math.Sqrt(discriminant);

        // Low angle shot (use the minus root)
        double angle = Math.Atan2(speedSq - sqrtDisc, acceleration * horizontalDist);

        // Build the velocity vector
        double vy = speed * Math.Sin(angle);
        double horizontalSpeed = speed * Math.Cos(angle);

        Vector2d dirXZ = Vector2d.Normalize(deltaXZ);
        double vx = dirXZ.X * horizontalSpeed;
        double vz = dirXZ.Y * horizontalSpeed;

        velocity = new Vector3d(vx, vy, vz);
        time = deltaXZ.Length / horizontalSpeed;
        return true;
    }
    private static Vector3d GetDirectionWithDispersion(Vector3d direction, Vector2 dispersionDeg, NatFloat random)
    {
        float randomYaw = random.nextFloat() * dispersionDeg.X * GameMath.DEG2RAD;
        float randomPitch = random.nextFloat() * dispersionDeg.Y * GameMath.DEG2RAD;

        Vector3 verticalAxis = new(0, 0, 1);
        bool directionIsVertical = (verticalAxis - direction).Length < 1E9 || (verticalAxis + direction).Length < 1E9;
        if (directionIsVertical) verticalAxis = new(0, 1, 0);

        Vector3d forwardAxis = Vector3d.Normalize(direction);
        Vector3d yawAxis = Vector3d.Normalize(Vector3d.Cross(forwardAxis, verticalAxis));
        Vector3d pitchAxis = Vector3d.Normalize(Vector3d.Cross(yawAxis, forwardAxis));

        Vector3d yawComponent = yawAxis * Math.Tan(randomYaw);
        Vector3d pitchComponent = pitchAxis * Math.Tan(randomPitch);

        return Vector3d.Normalize(forwardAxis + yawComponent + pitchComponent);
    }
}