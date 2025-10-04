using ImGuiNET;
using System.Diagnostics;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using VSImGui;
using VSImGui.API;

namespace CombatOverhaul.Integration;

public readonly struct PlayerPositionData(double height, bool onGround, long timeStamp)
{
    public readonly double Height = height;
    public readonly bool OnGround = onGround;
    public readonly long TimeStamp = timeStamp;

    public override string ToString() => $"{Height:F2} ({OnGround}) at {TimeSpan.FromMilliseconds(TimeStamp)}";
}

public class PositionBeforeFallingBehavior : EntityBehavior
{
    public PositionBeforeFallingBehavior(Entity entity) : base(entity)
    {
        _player = entity as EntityAgent ?? throw new ArgumentException("[PositionBeforeFallingBehavior] Entity should be EntityAgent", "entity");
        _api = entity.Api;

#if DEBUG
        if (_api.Side == EnumAppSide.Client)
        {
            _api.ModLoader.GetModSystem<ImGuiModSystem>().Draw += DrawPlots;
        }
#endif
    }

    public override string PropertyName() => "PositionBeforeFallingBehavior";
    public double LastFallHeight => GetFallHeight();
    public IEnumerable<PlayerPositionData> PositionsData => _positions;

    public override void OnGameTick(float deltaTime)
    {
        long currentTime = _api.World.ElapsedMilliseconds;
        double height = entity.SidedPos.Y;
        bool onGround = IsNotInFreeFall(_player);

        _positions.Enqueue(new(height, onGround, currentTime));

        if (_positions.Count > _maxPositionsStored)
        {
            PlayerPositionData lastData = _positions.Dequeue();
            if (lastData.OnGround)
            {
                _lastOnGroundHeight = lastData.Height;
            }
        }

#if DEBUG
        _fallHeight = GetFallHeight();
        _fallHeights.Enqueue(_fallHeight);
        if (_fallHeights.Count > _maxPositionsStored)
        {
            _fallHeights.Dequeue();
        }
#endif
    }

    private const int _maxPositionsStored = 512;
    private const double _moveUpThreshold = 0.01;
    private static bool _drawPlots = false;
    private readonly Queue<PlayerPositionData> _positions = new();
    private readonly Queue<double> _fallHeights = new();
    private readonly ICoreAPI _api;
    private readonly EntityAgent _player;
    private double _fallHeight = 0;
    private double _lastOnGroundHeight = 0;

    private double GetFallHeight()
    {
        PlayerPositionData[] positions = _positions.Reverse().ToArray();
        bool foundInAir = false;
        double prevHeight = _lastOnGroundHeight;
        double height = CurrentBelowBlockHeight();
        for (int index = 0; index < positions.Length; index++)
        {
            if (!positions[index].OnGround)
            {
                foundInAir = true;
            }

            if (!foundInAir && positions[index].OnGround)
            {
                height = positions[index].Height;
            }

            if (foundInAir && positions[index].OnGround)
            {

                prevHeight = positions[index].Height;
                return prevHeight - height;
            }
        }

        return prevHeight - height;
    }

    private static bool IsNotInFreeFall(EntityAgent player)
    {
        bool collided = player.CollidedVertically;
        bool mounted = player.MountedOn != null;
        bool notInAir = player.OnGround || player.FeetInLiquid || player.Swimming || player.InLava || player.ServerControls.IsStepping;
        bool gliding = player.ServerControls.Gliding;
        bool movingUp = player.SidedPos.Motion.Y > _moveUpThreshold;

        return collided || mounted || notInAir || gliding || movingUp;
    }

    private double CurrentBelowBlockHeight()
    {
        double height = _player.SidedPos.Y;
        IBlockAccessor accessor = _api.World.GetBlockAccessor(false, false, false);

        int heightDiff = 1;
        while (heightDiff < height)
        {
            BlockPos blockPos = _player.SidedPos.AsBlockPos;
            blockPos.Y -= heightDiff;

            BlockPos bp0 = blockPos.Copy();
            BlockPos bp1 = blockPos.Copy();
            BlockPos bp2 = blockPos.Copy();
            BlockPos bp3 = blockPos.Copy();

            Vec3d entityPosPos = _player.SidedPos.XYZ;

            float xDiff = _player.CollisionBox.XSize / 2f;
            float zDiff = _player.CollisionBox.ZSize / 2f;

            bp0.X = (int)(entityPosPos.X - xDiff);
            bp0.Z = (int)(entityPosPos.Z - zDiff);
            bp1.X = (int)(entityPosPos.X + xDiff);
            bp1.Z = (int)(entityPosPos.Z - zDiff);
            bp2.X = (int)(entityPosPos.X - xDiff);
            bp2.Z = (int)(entityPosPos.Z + zDiff);
            bp3.X = (int)(entityPosPos.X + xDiff);
            bp3.Z = (int)(entityPosPos.Z + zDiff);

            Block[] blocks = [
                accessor.GetBlock(bp0),
                accessor.GetBlock(bp1),
                accessor.GetBlock(bp2),
                accessor.GetBlock(bp3)
            ];
            
            if (blocks.Any(block => block?.CollisionBoxes != null && block.CollisionBoxes.Length > 0))
            {
                return blockPos.Y + blocks
                    .Where(block => block?.CollisionBoxes != null && block.CollisionBoxes.Length > 0)
                    .Select(block => block.CollisionBoxes
                        .Select(box => box.MaxY)
                        .Max())
                    .Max();
            }

            heightDiff++;
        }

        return 0;
    }

#if DEBUG
    private CallbackGUIStatus DrawPlots(float dt)
    {
        if (!_drawPlots) return CallbackGUIStatus.Closed;

        ImGui.Begin("Height plots");

        ImGui.Text($"Fall height: {GetFallHeight():F2}");
        ImGui.Text($"Last height: {_lastOnGroundHeight:F2}");

        float[] values = _positions.Select(a => (float)a.Height).ToArray();
        ImGui.PlotLines("Height", ref values[0], values.Length, 0, "", 0, 32, new(500, 200));

        float[] values2 = _positions.Select(a => a.OnGround ? (float)a.Height : 0).ToArray();
        ImGui.PlotLines("Height on ground", ref values2[0], values2.Length, 0, "", 0, 32, new(500, 200));

        float[] values3 = _fallHeights.Select(a => (float)a).ToArray();
        ImGui.PlotLines($"Fall height ({values3.Max():F2})", ref values3[0], values3.Length, 0, "", 0, values3.Max(), new(500, 200));

        ImGui.End();

        return CallbackGUIStatus.DontGrabMouse;
    }
#endif
}
