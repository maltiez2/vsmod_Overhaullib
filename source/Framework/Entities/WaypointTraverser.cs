using CombatOverhaul.Utils;
using OpenTK.Mathematics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Essentials;
using Vintagestory.GameContent;

namespace CombatOverhaul.Entities;

public sealed class COWaypointsTraverser : WaypointsTraverser
{
    public override Vec3d CurrentTarget => _waypoints[_waypoints.Count - 1].ToVanilla().ToVec3d();
    public override bool Ready => _waypoints != null && _asyncSearchObject == null;

    public COWaypointsTraverser(EntityAgent? entity, EnumAICreatureType creatureType = EnumAICreatureType.Default) : base(entity)
    {
        if (entity == null)
        {
            throw new ArgumentNullException(nameof(entity));
        }

        _entity = entity;

        if (entity.Properties.Server?.Attributes?.GetTreeAttribute("pathfinder") != null)
        {
            _minTurnAnglePerSec = (float)entity.Properties.Server.Attributes.GetTreeAttribute("pathfinder").GetDecimal("minTurnAnglePerSec", 250);
            _maxTurnAnglePerSec = (float)entity.Properties.Server.Attributes.GetTreeAttribute("pathfinder").GetDecimal("maxTurnAnglePerSec", 450);
        }
        else
        {
            _minTurnAnglePerSec = 250;
            _maxTurnAnglePerSec = 450;
        }

        _pathfindingSystem = entity.World.Api.ModLoader.GetModSystem<PathfindSystem>();
        _asyncPathfinder = entity.World.Api.ModLoader.GetModSystem<PathfindingAsync>();
        _creatureType = creatureType;
    }

    public override bool NavigateTo(
        Vec3d target,
        float movingSpeed,
        float targetDistance,
        Action? onGoalReached,
        Action? onStuck,
        Action? onNoPath = null,
        bool giveUpWhenNoPath = false,
        int searchDepth = 999,
        int mhdistanceTolerance = 0,
        EnumAICreatureType? creatureType = null)
    {
        _desiredTarget = target.ToOpenTK();
        _onNoPath = onNoPath;
        _onStuck_New = onStuck;
        _onGoalReached_New = onGoalReached;
        _movingSpeed_New = movingSpeed;
        _targetDistance_New = targetDistance;
        
        if (creatureType.HasValue) _creatureType = creatureType.Value;

        BlockPos startBlockPos = entity.ServerPos.AsBlockPos;
        if (entity.World.BlockAccessor.IsNotTraversable(startBlockPos))
        {
            HandleNoPathNew();
            return false;
        }

        FindPath(startBlockPos, target.AsBlockPos, searchDepth, mhdistanceTolerance);

        return AfterFoundPathNew();
    }
    public override bool NavigateTo_Async(Vec3d target, float movingSpeed, float targetDistance, Action OnGoalReached, Action OnStuck, Action onNoPath = null, int searchDepth = 999, int mhdistanceTolerance = 0, EnumAICreatureType? creatureType = null)
    {
        if (_asyncSearchObject != null) return false;  // Allow the one in progress to finish before trying another - maybe more than one AI task in the same tick tries to find a path?

        _desiredTarget = target.ToOpenTK();
        if (creatureType != null) _creatureType = (EnumAICreatureType)creatureType;

        // these all have to be saved because they are local parameters, but not used until we call AfterFoundPath()
        _onNoPath = onNoPath;
        _onGoalReached_New = OnGoalReached;
        _onStuck_New = OnStuck;
        _movingSpeed_New = movingSpeed;
        _targetDistance_New = targetDistance;

        BlockPos startBlockPos = entity.ServerPos.AsBlockPos;
        if (entity.World.BlockAccessor.IsNotTraversable(startBlockPos))
        {
            HandleNoPathNew();
            return false;
        }

        FindPath_Async(startBlockPos, target.AsBlockPos, searchDepth, mhdistanceTolerance);

        return true;
    }
    public override void Stop()
    {
        Active = false;
        entity.Controls.Forward = false;
        entity.ServerControls.Forward = false;
        entity.Controls.WalkVector.Set(0, 0, 0);
        stuckCounter = 0;
        _distCheckAccum = 0;
        _prevPosAccum = 0;
        _asyncSearchObject = null;
    }
    public override void Retarget()
    {
        Active = true;
        _distCheckAccum = 0;
        _prevPosAccum = 0;

        _waypointToReachIndex = _waypoints.Count - 1;
    }
    public override void OnGameTick(float dt)
    {
        if (_asyncSearchObject != null)
        {
            if (!_asyncSearchObject.Finished) return;

            AfterFoundPathNew();
        }

        if (!Active) return;

        bool nearHorizontally = false;
        int offset = 0;
        bool nearAllDirs =
            IsNearTarget(offset++, ref nearHorizontally)
            || IsNearTarget(offset++, ref nearHorizontally)
            || IsNearTarget(offset++, ref nearHorizontally)
        ;

        if (nearAllDirs)
        {
            _waypointToReachIndex += offset;
            _lastWaypointIncTotalMs = entity.World.ElapsedMilliseconds;
        }

        target = _waypoints[Math.Min(_waypoints.Count - 1, _waypointToReachIndex)].ToVanillaRef();

        bool onlastWaypoint = _waypointToReachIndex == _waypoints.Count - 1;

        if (_waypointToReachIndex >= _waypoints.Count)
        {
            Stop();
            OnGoalReached?.Invoke();
            return;
        }

        bool stuckBelowOrAbove = (nearHorizontally && !nearAllDirs && entity.Properties.Habitat == EnumHabitat.Land);

        bool stuck =
            (entity.CollidedVertically && entity.Controls.IsClimbing)
            || (entity.CollidedHorizontally && entity.ServerPos.Motion.Y <= 0)
            || stuckBelowOrAbove
            || (entity.CollidedHorizontally && _waypoints.Count > 1 && _waypointToReachIndex < _waypoints.Count && entity.World.ElapsedMilliseconds - _lastWaypointIncTotalMs > 2000)    // If it takes more than 2 seconds to reach next waypoint (waypoints are always 1 block apart)
        ;

        // This used to test motion, but that makes no sense, we want to test if the entity moved, not if it had motion
        double distsq = Vector3d.DistanceSquared(_prevPrevPos, _prevPos);
        stuck |= (distsq < 0.01 * 0.01) ? (entity.World.Rand.NextDouble() < GameMath.Clamp(1 - distsq * 1.2, 0.1, 0.9)) : false;


        // Test movement progress between two points in 150 millisecond intervalls
        _prevPosAccum += dt;
        if (_prevPosAccum > 0.2)
        {
            _prevPosAccum = 0;
            _prevPrevPos = _prevPos;
            _prevPos = entity.ServerPos.ToOpenTK();
        }

        // Long duration tests to make sure we're not just wobbling around in the same spot
        _distCheckAccum += dt;
        if (_distCheckAccum > 2)
        {
            _distCheckAccum = 0;
            if (Math.Abs(_sqDistToTarget - _lastDistToTarget) < 0.1)
            {
                stuck = true;
                stuckCounter += 30;
            }
            else if (!stuck) stuckCounter = 0;    // Only reset the stuckCounter in same tick as doing this test; otherwise the stuckCounter gets set to 0 every 2 or 3 ticks even if the entity collided horizontally (because motion vecs get set to 0 after the collision, so won't collide in the successive tick)
            _lastDistToTarget = _sqDistToTarget;
        }

        if (stuck)
        {
            stuckCounter++;
        }

        if (GlobalConstants.OverallSpeedMultiplier > 0 && stuckCounter > 60 / GlobalConstants.OverallSpeedMultiplier)
        {
            Stop();
            OnStuck?.Invoke();
            return;
        }

        EntityControls controls = entity.MountedOn == null ? entity.Controls : entity.MountedOn.Controls;
        if (controls == null) return;

        _targetVec = (_target - entity.ServerPos.ToOpenTKWithDimension()).Normalized();

        float desiredYaw = 0;

        if (_sqDistToTarget >= 0.01)
        {
            desiredYaw = (float)Math.Atan2(_targetVec.X, _targetVec.Z);
        }

        float nowMoveSpeed = movingSpeed;

        if (_sqDistToTarget < 1)
        {
            nowMoveSpeed = Math.Max(0.005f, movingSpeed * Math.Max(_sqDistToTarget, 0.2f));
        }

        float yawDist = GameMath.AngleRadDistance(entity.ServerPos.Yaw, desiredYaw);
        float turnSpeed = curTurnRadPerSec * dt * GlobalConstants.OverallSpeedMultiplier * movingSpeed;
        entity.ServerPos.Yaw += GameMath.Clamp(yawDist, -turnSpeed, turnSpeed);
        entity.ServerPos.Yaw = entity.ServerPos.Yaw % GameMath.TWOPI;



        double cosYaw = Math.Cos(entity.ServerPos.Yaw);
        double sinYaw = Math.Sin(entity.ServerPos.Yaw);
        controls.WalkVector.Set(sinYaw, GameMath.Clamp(_targetVec.Y, -1, 1), cosYaw);
        controls.WalkVector.Mul(nowMoveSpeed * GlobalConstants.OverallSpeedMultiplier / Math.Max(1, Math.Abs(yawDist) * 3));

        // Make it walk along the wall, but not walk into the wall, which causes it to climb
        if (entity.Properties.RotateModelOnClimb && entity.Controls.IsClimbing && entity.ClimbingIntoFace != null && entity.Alive)
        {
            BlockFacing facing = entity.ClimbingIntoFace;
            if (Math.Sign(facing.Normali.X) == Math.Sign(controls.WalkVector.X))
            {
                controls.WalkVector.X = 0;
            }

            if (Math.Sign(facing.Normali.Y) == Math.Sign(controls.WalkVector.Y))
            {
                controls.WalkVector.Y = -controls.WalkVector.Y;
            }

            if (Math.Sign(facing.Normali.Z) == Math.Sign(controls.WalkVector.Z))
            {
                controls.WalkVector.Z = 0;
            }
        }

        //   entity.World.SpawnParticles(0.3f, ColorUtil.WhiteAhsl, target, target, new Vec3f(), new Vec3f(), 0.1f, 0.1f, 3f, EnumParticleModel.Cube);

        if (entity.Properties.Habitat == EnumHabitat.Underwater)
        {
            controls.FlyVector.Set(controls.WalkVector);

            Vec3d pos = entity.Pos.XYZ;
            Block inblock = entity.World.BlockAccessor.GetBlock((int)pos.X, (int)(pos.Y), (int)pos.Z, BlockLayersAccess.Fluid);
            Block aboveblock = entity.World.BlockAccessor.GetBlock((int)pos.X, (int)(pos.Y + 1), (int)pos.Z, BlockLayersAccess.Fluid);
            float waterY = (int)pos.Y + inblock.LiquidLevel / 8f + (aboveblock.IsLiquid() ? 9 / 8f : 0);
            float bottomSubmergedness = waterY - (float)pos.Y;

            // 0 = at swim line  1 = completely submerged
            float swimlineSubmergedness = GameMath.Clamp(bottomSubmergedness - ((float)entity.SwimmingOffsetY), 0, 1);
            swimlineSubmergedness = 1f - Math.Min(1f, swimlineSubmergedness + 0.5f);
            if (swimlineSubmergedness > 0f)
            {
                //Push the fish back underwater if part is poking out ...  (may need future adaptation for sharks[?], probably by changing SwimmingOffsetY)
                controls.FlyVector.Y = GameMath.Clamp(controls.FlyVector.Y, -0.04f, -0.02f) * (1f - swimlineSubmergedness);
            }
            else
            {
                float factor = movingSpeed * GlobalConstants.OverallSpeedMultiplier / (float)Math.Sqrt(_targetVec.X * _targetVec.X + _targetVec.Z * _targetVec.Z);
                controls.FlyVector.Y = _targetVec.Y * factor;
            }
        }
        else if (entity.Swimming)
        {
            controls.FlyVector.Set(controls.WalkVector);

            Vec3d pos = entity.Pos.XYZ;
            Block inblock = entity.World.BlockAccessor.GetBlock((int)pos.X, (int)(pos.Y), (int)pos.Z, BlockLayersAccess.Fluid);
            Block aboveblock = entity.World.BlockAccessor.GetBlock((int)pos.X, (int)(pos.Y + 1), (int)pos.Z, BlockLayersAccess.Fluid);
            float waterY = (int)pos.Y + inblock.LiquidLevel / 8f + (aboveblock.IsLiquid() ? 9 / 8f : 0);
            float bottomSubmergedness = waterY - (float)pos.Y;

            // 0 = at swim line
            // 1 = completely submerged
            float swimlineSubmergedness = GameMath.Clamp(bottomSubmergedness - ((float)entity.SwimmingOffsetY), 0, 1);
            swimlineSubmergedness = Math.Min(1, swimlineSubmergedness + 0.5f);
            controls.FlyVector.Y = GameMath.Clamp(controls.FlyVector.Y, 0.02f, 0.04f) * swimlineSubmergedness;


            if (entity.CollidedHorizontally)
            {
                controls.FlyVector.Y = 0.05f;
            }
        }
    }
    public override bool WalkTowards(Vec3d target, float movingSpeed, float targetDistance, Action OnGoalReached, Action OnStuck, EnumAICreatureType creatureType = EnumAICreatureType.Default)
    {
        _waypoints = [target.ToOpenTK()];

        return base.WalkTowards(target, movingSpeed, targetDistance, OnGoalReached, OnStuck, creatureType);
    }

    // Will need to be harmony patched into EntityBehaviorEmotionStates
    public PathfinderTask PreparePathfinderTaskNew(BlockPos startBlockPos, BlockPos targetBlockPos, int searchDepth = 999, int mhdistanceTolerance = 0, EnumAICreatureType? creatureType = null)
    {
        EntityBehaviorControlledPhysics? bh = entity.GetBehavior<EntityBehaviorControlledPhysics>();
        float stepHeight = bh == null ? 0.6f : bh.StepHeight;
        bool avoidFall = entity.Properties.FallDamage && entity.Properties.Attributes?["reckless"].AsBool(false) != true;
        int maxFallHeight = avoidFall ? 4 - (int)(movingSpeed * 30) : 12;   // fast moving entities cannot safely fall so far (might miss target block below due to outward drift)

        return new PathfinderTask(startBlockPos, targetBlockPos, maxFallHeight, stepHeight, entity.CollisionBox, searchDepth, mhdistanceTolerance, creatureType ?? _creatureType);
    }

    protected override bool BeginGo()
    {
        entity.Controls.Forward = true;
        entity.ServerControls.Forward = true;
        curTurnRadPerSec = _minTurnAnglePerSec + (float)entity.World.Rand.NextDouble() * (_maxTurnAnglePerSec - _minTurnAnglePerSec);
        curTurnRadPerSec *= GameMath.DEG2RAD * 50;

        stuckCounter = 0;
        _waypointToReachIndex = 0;
        _lastWaypointIncTotalMs = entity.World.ElapsedMilliseconds;
        _distCheckAccum = 0;
        _prevPosAccum = 0;

        return true;
    }

    private const bool _pathFindDebug = true;
    
    private readonly float _minTurnAnglePerSec;
    private readonly float _maxTurnAnglePerSec;
    private readonly PathfindSystem _pathfindingSystem;
    private readonly PathfindingAsync _asyncPathfinder;
    private readonly EntityAgent _entity;

    private Vector3d _targetVec = new();
    private Vector3d _prevPos = new(0, -2000, 0);
    private Vector3d _prevPrevPos = new(0, -1000, 0);
    private List<Vector3d> _waypoints = [];
    private List<Vector3d> _newWaypoints = [];
    private PathfinderTask? _asyncSearchObject;
    private int _waypointToReachIndex = 0;
    private long _lastWaypointIncTotalMs;
    private Vector3d _desiredTarget = new();
    private EnumAICreatureType _creatureType;
    private float _prevPosAccum;
    private float _sqDistToTarget;
    private float _distCheckAccum = 0;
    private float _lastDistToTarget = 0;
    private Action? _onNoPath; // These next five fields are used to save parameters, ready for the AfterPathFound() call which might be next tick or even later, after asynchronous pathfinding has finished
    private Action? _onFoundPath; // These next five fields are used to save parameters, ready for the AfterPathFound() call which might be next tick or even later, after asynchronous pathfinding has finished
    private Action? _onGoalReached_New; // These next five fields are used to save parameters, ready for the AfterPathFound() call which might be next tick or even later, after asynchronous pathfinding has finished
    private Action? _onStuck_New; // These next five fields are used to save parameters, ready for the AfterPathFound() call which might be next tick or even later, after asynchronous pathfinding has finished
    private float _movingSpeed_New; // These next five fields are used to save parameters, ready for the AfterPathFound() call which might be next tick or even later, after asynchronous pathfinding has finished
    private float _targetDistance_New; // These next five fields are used to save parameters, ready for the AfterPathFound() call which might be next tick or even later, after asynchronous pathfinding has finished

    private Vector3d _target
    {
        get => target.ToOpenTK();
        set => target = value.ToVanillaRef();
    }

    private void FindPath(BlockPos startBlockPos, BlockPos targetBlockPos, int searchDepth, int manhattanDistanceTolerance = 0)
    {
        _waypointToReachIndex = 0;

        EntityBehaviorControlledPhysics? bh = entity.GetBehavior<EntityBehaviorControlledPhysics>();
        float stepHeight = bh == null ? 0.6f : bh.StepHeight;
        int maxFallHeight = entity.Properties.FallDamage ? Math.Min(8, (int)Math.Round(3.51 / Math.Max(0.01, entity.Properties.FallDamageMultiplier))) - (int)(movingSpeed * 30) : 8;   // fast moving entities cannot safely fall so far (might miss target block below due to outward drift)

        _newWaypoints = _pathfindingSystem.FindPathAsWaypoints(startBlockPos, targetBlockPos, maxFallHeight, stepHeight, entity.CollisionBox, searchDepth, manhattanDistanceTolerance, _creatureType).Select(value => value.ToOpenTK()).ToList();
    }
    private void FindPath_Async(BlockPos startBlockPos, BlockPos targetBlockPos, int searchDepth, int manhattanDistanceTolerance = 0)
    {
        _waypointToReachIndex = 0;
        _asyncSearchObject = PreparePathfinderTask(startBlockPos, targetBlockPos, searchDepth, manhattanDistanceTolerance, _creatureType);
        _asyncPathfinder.EnqueuePathfinderTask(_asyncSearchObject);
    }
    private bool IsNearTarget(int waypointOffset, ref bool nearHorizontally)
    {
        if (_waypoints.Count - 1 < _waypointToReachIndex + waypointOffset) return false;

        int wayPointIndex = Math.Min(_waypoints.Count - 1, _waypointToReachIndex + waypointOffset);
        Vector3d target = _waypoints[wayPointIndex];
        

        double curPosY = entity.ServerPos.InternalY;
        _sqDistToTarget = (float)Vector2d.DistanceSquared(target.Xz, entity.ServerPos.ToOpenTK().Xz);//target.HorizontalSquareDistanceTo(entity.ServerPos.X, entity.ServerPos.Z);

        double vdistsq = (target.Y - curPosY) * (target.Y - curPosY);
        bool above = curPosY > target.Y;
        _sqDistToTarget += (float)Math.Max(0, vdistsq - (above ? 1 : 0.5)); // Ok to be up to 1 block above or 0.5 blocks below

        if (!nearHorizontally)
        {
            double horsqDistToTarget = (float)Vector2d.DistanceSquared(target.Xz, entity.ServerPos.ToOpenTK().Xz);
            nearHorizontally = horsqDistToTarget < TargetDistance * TargetDistance;
        }

        return _sqDistToTarget < TargetDistance * TargetDistance;
    }
    private bool AfterFoundPathNew()
    {
        if (_asyncSearchObject != null)
        {
            _newWaypoints = _asyncSearchObject.waypoints.Select(value => value.ToOpenTK()).ToList();
            _asyncSearchObject = null;
        }

        if (_newWaypoints == null /*|| newWaypoints.Count == 0 - uh no. this is a successful search*/)
        {
            HandleNoPathNew();
            return false;
        }

        _waypoints = _newWaypoints;

        // Debug visualization
        if (_pathFindDebug)
        {
            List<BlockPos> poses = new();
            List<int> colors = new();
            int i = 0;
            foreach (Vector3d node in _waypoints)
            {
                poses.Add(node.ToVanillaRef().AsBlockPos);
                colors.Add(ColorUtil.ColorFromRgba(128, 128, Math.Min(255, 128 + i * 8), 150));
                i++;
            }

            poses.Add(_desiredTarget.ToVanillaRef().AsBlockPos);
            colors.Add(ColorUtil.ColorFromRgba(128, 0, 255, 255));

            IPlayer player = entity.World.AllOnlinePlayers[0];
            entity.World.HighlightBlocks(player, 2, poses,
                colors,
                EnumHighlightBlocksMode.Absolute, EnumHighlightShape.Arbitrary
            );
        }

        _waypoints.Add(_desiredTarget);

        base.WalkTowards(_desiredTarget.ToVanillaRef(), _movingSpeed_New, _targetDistance_New, _onGoalReached_New, _onStuck_New);

        _onFoundPath?.Invoke();

        return true;
    }
    private void HandleNoPathNew()
    {
        _waypoints = [];

        if (_pathFindDebug)
        {
            // Debug visualization
            List<BlockPos> poses = new();
            List<int> colors = new();
            int i = 0;
            foreach (PathNode? node in entity.World.Api.ModLoader.GetModSystem<PathfindSystem>().astar.closedSet)
            {
                poses.Add(node);
                colors.Add(ColorUtil.ColorFromRgba(Math.Min(255, i * 4), 0, 0, 150));
                i++;
            }

            IPlayer player = entity.World.AllOnlinePlayers[0];
            entity.World.HighlightBlocks(player, 2, poses,
                colors,
                EnumHighlightBlocksMode.Absolute, EnumHighlightShape.Arbitrary
            );
        }

        _waypoints.Add(_desiredTarget);

        base.WalkTowards(_desiredTarget.ToVanillaRef(), _movingSpeed_New, _targetDistance_New, _onGoalReached_New, _onStuck_New);

        if (_onNoPath != null)
        {
            Active = false;
            _onNoPath.Invoke();
        }
    }
}