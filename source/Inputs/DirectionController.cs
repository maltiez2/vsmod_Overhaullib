using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace CombatOverhaul.Inputs;

public enum DirectionsConfiguration
{
    None = 1,
    TopBottom = 2,
    Triangle = 3,
    Square = 4,
    Star = 5,
    Eight = 8
}

public enum AttackDirection
{
    Top,
    TopRight,
    Right,
    BottomRight,
    Bottom,
    BottomLeft,
    Left,
    TopLeft
}

public readonly struct MouseMovementData
{
    public float Pitch { get; }
    public float Yaw { get; }
    public float DeltaPitch { get; }
    public float DeltaYaw { get; }

    public MouseMovementData(float pitch, float yaw, float deltaPitch, float deltaYaw)
    {
        Pitch = pitch;
        Yaw = yaw;
        DeltaPitch = deltaPitch;
        DeltaYaw = deltaYaw;
    }
}

public sealed class DirectionController
{
    public DirectionsConfiguration DirectionsConfiguration { get; set; } = DirectionsConfiguration.Eight;
    public int Depth { get; set; } = 5;
    public float Sensitivity => _settings.DirectionsSensitivity;
    public bool Invert => _settings.DirectionsInvert;
    public AttackDirection CurrentDirection { get; private set; }
    public AttackDirection CurrentDirectionWithInversion => Invert ? InvertDirection(CurrentDirection, DirectionsConfiguration) : CurrentDirection;
    public int CurrentDirectionNormalized { get; private set; }
    public bool AlternativeDirectionControls => _settings.DirectionsMovementControls;
    public bool DirectionsHotkeysControls => _settings.DirectionsHotkeysControls;

    public static readonly Dictionary<DirectionsConfiguration, List<int>> Configurations = new()
    {
        { DirectionsConfiguration.TopBottom, new() {0, 4} },
        { DirectionsConfiguration.Triangle, new() {0, 3, 5} },
        { DirectionsConfiguration.Square, new() {0, 2, 4, 6} },
        { DirectionsConfiguration.Star, new() {0, 1, 3, 5, 7} },
        { DirectionsConfiguration.Eight, new() {0, 1, 2, 3, 4, 5, 6, 7} }
    };

    public DirectionController(ICoreClientAPI api, DirectionCursorRenderer renderer, Settings settings)
    {
        _api = api;
        _directionCursorRenderer = renderer;
        _settings = settings;

        for (int count = 0; count < Depth * 2; count++)
        {
            _directionQueue.Enqueue(new(0, 0, 0, 0));
        }

        api.Input.RegisterHotKey("combatoverhaul:directions-cursor-forward", "(CO) Directions cursor Up", GlKeys.W);
        api.Input.RegisterHotKey("combatoverhaul:directions-cursor-backward", "(CO) Directions cursor Down", GlKeys.S);
        api.Input.RegisterHotKey("combatoverhaul:directions-cursor-left", "(CO) Directions cursor Left", GlKeys.A);
        api.Input.RegisterHotKey("combatoverhaul:directions-cursor-right", "(CO) Directions cursor Right", GlKeys.D);

        _forwardHotkey = api.Input.HotKeys["combatoverhaul:directions-cursor-forward"];
        _backwardHotkey = api.Input.HotKeys["combatoverhaul:directions-cursor-backward"];
        _leftHotkey = api.Input.HotKeys["combatoverhaul:directions-cursor-left"];
        _rightHotkey = api.Input.HotKeys["combatoverhaul:directions-cursor-right"];
    }

    public void OnGameTick(bool forceNewDirection = false)
    {
        if (DirectionsConfiguration == 0)
        {
            DirectionsConfiguration = DirectionsConfiguration.None;
        }

        if (DirectionsConfiguration == DirectionsConfiguration.None)
        {
            _directionCursorRenderer.Show = false;
            return;
        }

        _directionCursorRenderer.Show = true;

        float pitch = _api.Input.MousePitch;
        float yaw = _api.Input.MouseYaw;

        _directionQueue.Enqueue(new(pitch, yaw, pitch - _directionQueue.Last().Pitch, yaw - _directionQueue.Last().Yaw));

        MouseMovementData previous = _directionQueue.Dequeue();

        if (AlternativeDirectionControls || DirectionsHotkeysControls)
        {
            AttackDirection direction = CalculateDirectionWithAlternativeControls(out bool changeDirection);

            if (changeDirection)
            {
                CurrentDirectionNormalized = (int)direction;
                CurrentDirection = direction;
                _directionCursorRenderer.CurrentDirection = (int)CurrentDirection;
            }
        }
        else
        {
            int direction = CalculateDirection(previous.Yaw - yaw, previous.Pitch - pitch, (int)DirectionsConfiguration);

            float delta = _directionQueue.Last().DeltaPitch * _directionQueue.Last().DeltaPitch + _directionQueue.Last().DeltaYaw * _directionQueue.Last().DeltaYaw;

            if (forceNewDirection || delta > _sensitivityFactor / Sensitivity)
            {
                CurrentDirectionNormalized = direction;
                CurrentDirection = (AttackDirection)Configurations[DirectionsConfiguration][CurrentDirectionNormalized];
                _directionCursorRenderer.CurrentDirection = (int)CurrentDirection;
            }
        }

        if (Configurations.TryGetValue(DirectionsConfiguration, out List<int>? allowedDirections) && !allowedDirections.Contains((int)CurrentDirection))
        {
            CurrentDirection = (AttackDirection)allowedDirections[0];
            CurrentDirectionNormalized = allowedDirections[0];
            _directionCursorRenderer.CurrentDirection = (int)CurrentDirection;
        }
    }


    private const float _sensitivityFactor = 1e-3f;
    private readonly ICoreClientAPI _api;
    private readonly Queue<MouseMovementData> _directionQueue = new();
    private readonly DirectionCursorRenderer _directionCursorRenderer;
    private readonly Settings _settings;
    private readonly HotKey _forwardHotkey;
    private readonly HotKey _backwardHotkey;
    private readonly HotKey _leftHotkey;
    private readonly HotKey _rightHotkey;

    private int CalculateDirection(float yaw, float pitch, int directionsCount)
    {
        if (Invert)
        {
            yaw *= -1;
            pitch *= -1;
        }

        float angleSegment = 360f / directionsCount;
        float directionOffset = angleSegment / 2f;
        float angle = MathF.Atan2(yaw, pitch) * GameMath.RAD2DEG;
        float angleOffset = angle + directionOffset + 360;
        return (int)(angleOffset / angleSegment) % directionsCount;
    }

    private static AttackDirection InvertDirection(AttackDirection direction, DirectionsConfiguration configuration)
    {
        switch (configuration)
        {
            case DirectionsConfiguration.Triangle:
                return direction switch
                {
                    AttackDirection.Top => AttackDirection.Bottom,
                    AttackDirection.TopRight => AttackDirection.BottomLeft,
                    AttackDirection.Right => AttackDirection.Left,
                    AttackDirection.BottomRight => AttackDirection.TopLeft,
                    AttackDirection.Bottom => AttackDirection.Top,
                    AttackDirection.BottomLeft => AttackDirection.TopRight,
                    AttackDirection.Left => AttackDirection.Right,
                    AttackDirection.TopLeft => AttackDirection.BottomRight,
                    _ => AttackDirection.Top
                };
            case DirectionsConfiguration.Star:
                return direction switch
                {
                    AttackDirection.Top => AttackDirection.Bottom,
                    AttackDirection.TopRight => AttackDirection.BottomLeft,
                    AttackDirection.Right => AttackDirection.Left,
                    AttackDirection.BottomRight => AttackDirection.TopLeft,
                    AttackDirection.Bottom => AttackDirection.Top,
                    AttackDirection.BottomLeft => AttackDirection.TopRight,
                    AttackDirection.Left => AttackDirection.Right,
                    AttackDirection.TopLeft => AttackDirection.BottomRight,
                    _ => AttackDirection.Top
                };
            case DirectionsConfiguration.None:
            case DirectionsConfiguration.TopBottom:
            case DirectionsConfiguration.Square:
            case DirectionsConfiguration.Eight:
                return direction switch
                {
                    AttackDirection.Top => AttackDirection.Bottom,
                    AttackDirection.TopRight => AttackDirection.BottomLeft,
                    AttackDirection.Right => AttackDirection.Left,
                    AttackDirection.BottomRight => AttackDirection.TopLeft,
                    AttackDirection.Bottom => AttackDirection.Top,
                    AttackDirection.BottomLeft => AttackDirection.TopRight,
                    AttackDirection.Left => AttackDirection.Right,
                    AttackDirection.TopLeft => AttackDirection.BottomRight,
                    _ => AttackDirection.Top
                };
        }

        return AttackDirection.Top;
    }

    private AttackDirection CalculateDirectionWithAlternativeControls(out bool changeDirection)
    {
        (bool forward, bool backward, bool left, bool right) = DirectionsHotkeysControls ? GetHotkeysState() : GetControlsState();

        if (Invert)
        {
            bool temp = forward;
            forward = backward;
            backward = temp;

            temp = left;
            left = right;
            right = temp;
        }

        changeDirection = true;

        switch (DirectionsConfiguration)
        {
            case DirectionsConfiguration.None:
                return 0;
            case DirectionsConfiguration.TopBottom:
                if (forward) return AttackDirection.Top;
                if (backward) return AttackDirection.Bottom;
                changeDirection = false;
                break;
            case DirectionsConfiguration.Triangle:
                if (forward) return AttackDirection.Top;
                if (right) return AttackDirection.BottomRight;
                if (left) return AttackDirection.BottomLeft;
                changeDirection = false;
                break;
            case DirectionsConfiguration.Square:
                if (forward) return AttackDirection.Top;
                if (backward) return AttackDirection.Bottom;
                if (left) return AttackDirection.Left;
                if (right) return AttackDirection.Right;
                changeDirection = false;
                break;
            case DirectionsConfiguration.Star:
                if (forward) return AttackDirection.Top;
                if (left && !backward) return AttackDirection.TopLeft;
                if (right && !backward) return AttackDirection.TopRight;
                if (left) return AttackDirection.BottomLeft;
                if (right) return AttackDirection.BottomRight;
                changeDirection = false;
                break;
            case DirectionsConfiguration.Eight:
                if (forward && left) return AttackDirection.TopLeft;
                if (forward && right) return AttackDirection.TopRight;
                if (backward && left) return AttackDirection.BottomLeft;
                if (backward && right) return AttackDirection.BottomRight;
                if (forward) return AttackDirection.Top;
                if (backward) return AttackDirection.Bottom;
                if (left) return AttackDirection.Left;
                if (right) return AttackDirection.Right;
                changeDirection = false;
                break;
            default:
                changeDirection = false;
                break;
        }

        return 0;
    }

    private (bool forward, bool backward, bool left, bool right) GetControlsState()
    {
        Vintagestory.API.Common.EntityControls controls = _api.World.Player.Entity.Controls;
        bool forward = controls.Forward;
        bool backward = controls.Backward;
        bool left = controls.Left;
        bool right = controls.Right;

        return (forward, backward, left, right);
    }

    private (bool forward, bool backward, bool left, bool right) GetHotkeysState()
    {
        bool forward = _api.Input.IsHotKeyPressed(_forwardHotkey);
        bool backward = _api.Input.IsHotKeyPressed(_backwardHotkey);
        bool left = _api.Input.IsHotKeyPressed(_leftHotkey);
        bool right = _api.Input.IsHotKeyPressed(_rightHotkey);

        return (forward, backward, left, right);
    }
}
