using CombatOverhaul.Animations;
using CombatOverhaul.RangedSystems.Aiming;
using OpenTK.Mathematics;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace CombatOverhaul.Implementations;

public sealed class AimingAnimationController
{
    public AimingStats Stats { get; set; }

    public AimingAnimationController(ClientAimingSystem aimingSystem, FirstPersonAnimationsBehavior? animationBehavior, AimingStats stats)
    {
        _aimingSystem = aimingSystem;
        _animationBehavior = animationBehavior;
        Stats = stats;

        //DebugWidgets.FloatDrag("test", "test2", $"fovMult-{stats.AimDrift}", () => _fovMultiplier, value => _fovMultiplier = value);
    }

    public void Play(bool mainHand)
    {
        AnimationRequest request = new(_cursorFollowAnimation, 1.0f, 0, "aiming", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), true);
        _animationBehavior?.Play(request, mainHand);
        _aimingSystem.OnAimPointChange += UpdateCursorFollowAnimation;
    }
    public void Stop(bool mainHand)
    {
        _cursorStopFollowAnimation.PlayerKeyFrames[0] = new PLayerKeyFrame(PlayerFrame.Zero, TimeSpan.FromMilliseconds(500), EasingFunctionType.CosShifted);
        AnimationRequest request = new(_cursorStopFollowAnimation, 1.0f, 0, "aiming", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), true);
        _animationBehavior?.Play(request, mainHand);
        _aimingSystem.OnAimPointChange -= UpdateCursorFollowAnimation;
    }

    private readonly Animations.Animation _cursorFollowAnimation = Animations.Animation.Zero.Clone();
    private readonly Animations.Animation _cursorStopFollowAnimation = Animations.Animation.Zero.Clone();
    private const float _animationFollowMultiplier = 0.01f;
    private readonly ClientAimingSystem _aimingSystem;
    private readonly FirstPersonAnimationsBehavior? _animationBehavior;
    private float _fovMultiplier = 0.79f;

    private PLayerKeyFrame GetAimingFrame()
    {
        Vector2 currentAim = _aimingSystem.GetCurrentAim();

        /*DebugWidgets.FloatDrag("tweaks", "animation", "followX", () => _aimingStats.AnimationFollowX, value => _aimingStats.AnimationFollowX = value);
        DebugWidgets.FloatDrag("tweaks", "animation", "followY", () => _aimingStats.AnimationFollowY, value => _aimingStats.AnimationFollowY = value);
        DebugWidgets.FloatDrag("tweaks", "animation", "offsetX", () => _aimingStats.AnimationOffsetX, value => _aimingStats.AnimationOffsetX = value);
        DebugWidgets.FloatDrag("tweaks", "animation", "offsetY", () => _aimingStats.AnimationOffsetY, value => _aimingStats.AnimationOffsetY = value);*/

        float fovAdjustment = 1f - MathF.Cos(ClientSettings.FieldOfView * GameMath.DEG2RAD) * _fovMultiplier;

        float yaw = 0 - currentAim.X * _animationFollowMultiplier * Stats.AnimationFollowX * fovAdjustment + Stats.AnimationOffsetX;
        float pitch = currentAim.Y * _animationFollowMultiplier * Stats.AnimationFollowY * fovAdjustment + Stats.AnimationOffsetY;

        AnimationElement element = new(0, 0, 0, 0, yaw, pitch);

        PlayerFrame frame = new(upperTorso: element);

        return new PLayerKeyFrame(frame, TimeSpan.Zero, EasingFunctionType.Linear);
    }
    private void UpdateCursorFollowAnimation()
    {
        _cursorFollowAnimation.PlayerKeyFrames[0] = GetAimingFrame();
        _cursorFollowAnimation.Hold = true;
    }
}